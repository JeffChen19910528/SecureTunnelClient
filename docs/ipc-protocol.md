# Named Pipe IPC Protocol

## Status

- **Implemented & Tested & Passed:** the wire protocol below, exercised end-to-end with a real OS Named Pipe (server + client in the same process, no Windows Service install) by `NamedPipeIntegrationTests` and `NamedPipePrivilegedClientProxyTests` (see `testing-guide.md`).
- **Accepted Risk:** the pipe access control (`PipeSecurityFactory`) grants `BUILTIN\Users` (not a specific interactive-user SID) at the object-construction level - it has not been validated against a real second Windows identity, because no elevated Windows Service installation exists in this phase to test against.
- **Blocked:** a true cross-identity access-denied test (needs a second logon session), and any test involving an actually-installed Windows Service.

## Why Named Pipes

Named Pipes are the standard local IPC mechanism on Windows for a client process talking to a Windows Service, support per-connection access control via `PipeSecurity`, and require no network port or firewall consideration (which matters for a product whose whole point is a restrictive, private-resource-only network posture).

## Pipe identity

- Full pipe path: `\\.\pipe\SecureTunnelClientAgent.v1` (base name `SecureTunnelClientAgent.v1`, defined once as `SecureTunnel.Client.Core.Client.Ipc.PipeProtocol.PipeName`).
- The name is a fixed constant. Neither the server (`NamedPipeHostedService`) nor the client (`NamedPipePrivilegedClientProxy`) accept a caller-supplied pipe name - there is no configuration surface for it. The `.v1` suffix exists so a future breaking protocol change can move to a new pipe name rather than a client and service silently talking past each other.

## Framing

Every message (request or response) is:

```
[4 bytes: Int32, little-endian, body length in bytes][body: UTF-8 JSON]
```

`SecureTunnel.Client.Core.Client.Ipc.PipeFraming` implements both `WriteFrameAsync` and `ReadFrameAsync` and is shared by both ends, so the framing logic is defined exactly once. `ReadFrameAsync` validates the declared length against `PipeProtocol.MaxMessageSizeBytes` (64 KiB) **before** allocating a body buffer or reading further - an oversized or corrupt length prefix is rejected cheaply, without ever attempting to read 64 KiB+ of attacker-controlled data into memory.

## Message shapes

**Request** (`PipeRequestEnvelope`):

```json
{ "Operation": "Connect" | "Disconnect" | "Status" | "ValidateConfiguration", "ClientId": "string", "RawConfigText": "string | null" }
```

`Operation` is checked against `PipeProtocol.AllowedOperations` on the server before anything is dispatched; any other value (or a missing/empty `ClientId`) is rejected as `InvalidRequest` without reaching the privileged handler. `RawConfigText` is only meaningful for `ValidateConfiguration` and is only ever fed into `IClientConfigurationParser.Parse` - the parser immediately wraps any discovered key in `SensitiveString` and the parsed key never re-enters a response.

**Response** (`PipeResponseEnvelope`):

```json
{
  "Success": true,
  "Outcome": "string (WireGuardOutcome name)",
  "ErrorCode": "string | null",
  "UserSafeMessage": "string | null",
  "ObservedState": "string (ClientState name) | null",
  "ConfigClientId": "string | null",
  "ConfigGatewayEndpoint": "string | null",
  "ConfigInterfaceAddress": "string | null",
  "ConfigAllowedIPs": ["string"] | null
}
```

The `Config*` fields are a flattened, optional projection of `ConfigurationSummary`, populated only on a successful `ValidateConfiguration` response (this is how the WPF import flow learns the newly-assigned `ClientId` - see `wireguard-connection-lifecycle.md` and the WPF import flow in `README.md`). They are kept as flat nullable primitives rather than a nested object to keep the wire schema simple. Never contains a private key, preshared key, or any other secret - verified by `NamedPipeIntegrationTests.Response_NeverContainsPrivateKeyOrPresharedKey`, which asserts on the raw serialized response bytes.

## Timeouts

| Boundary | Value | Purpose |
|---|---|---|
| Server per-session operation budget | 30s (`PipeProtocol.ServerOperationTimeoutMs`) | Upper bound on read+dispatch+write for one request, so a stuck client or a slow WireGuard process call can't hold a server thread forever. |
| Client connect timeout | 10s (`PipeProtocol.ClientConnectTimeoutMs`) | How long `NamedPipeClientStream.ConnectAsync` waits before giving up - if the service isn't running, this expires and the proxy reports `ServiceUnavailable`. |
| Client overall call timeout | 35s (`PipeProtocol.ClientCallTimeoutMs`) | Upper bound on connect+write+read for one proxy call. |

The accept loop itself (`NamedPipeHostedService.ExecuteAsync`) has no timeout on `WaitForConnectionAsync` - it is meant to sit and wait for the next client indefinitely, which is normal for a long-running service.

## Error mapping

| Condition | `WireGuardOutcome` |
|---|---|
| No server listening / connect timed out | `ServiceUnavailable` |
| Connection established but broken mid-call (I/O error, unexpected disconnect) | `PipeConnectionFailed` |
| Pipe connection rejected by the server's ACL | `AccessDenied` |
| Malformed JSON, oversized declared length, unknown/missing `Operation`, missing `ClientId` | `InvalidRequest` |
| WireGuard-level outcomes (`WireGuardNotInstalled`, `PermissionDenied`, `InvalidConfiguration`, `ConnectionFailed`, `Connected`, `Disconnected`, `Unknown`) | passed through unchanged from `IWireGuardClientService`/`ClientAgentServiceFoundation` |

These are never collapsed into one generic "connection failed" value - each is distinct so the WPF UI (or any future caller) can react appropriately (e.g. prompt to install WireGuard vs. retry vs. surface a permissions issue).

## Access control (`PipeSecurityFactory`)

The server creates its `NamedPipeServerStream` (via `NamedPipeServerStreamAcl.Create`) with a `PipeSecurity` that:

- Grants `PipeAccessRights.ReadWrite` to `BUILTIN\Users` (the well-known local Users group).
- Explicitly denies `PipeAccessRights.FullControl` to `Everyone` (World SID), `ANONYMOUS LOGON`, and `NT AUTHORITY\NETWORK`.

This changed in Phase C3 from a current-process-identity-only grant to the `BUILTIN\Users` grant above, because the Agent now runs (by design, once installed) as LocalSystem (see `windows-service-boundary.md`'s Service Identity Rationale) while the WPF client runs as an arbitrary interactive desktop user - a current-user-only ACL on the *server* side made no sense once the server's identity and the client's identity are different by design.

**Explicit limitation, not overclaimed:** this has only been verified at the object-construction level (`PipeSecurityFactoryTests` inspects the resulting `PipeAccessRule` entries directly - four tests: grants `BUILTIN\Users`, denies `Everyone`, denies `ANONYMOUS LOGON`, denies `NETWORK`). There is no automated or manual test in this phase that proves a *different* Windows identity is actually denied at runtime, because doing so requires a second logon session that isn't available in this development/test environment, and because there is no real elevated Windows Service installation to test against yet. **Accepted risk:** any local user account (not just the one who imported a configuration) can issue pipe operations against whatever configuration is currently stored - a per-importing-user SID allow-list, which would close this gap, requires an install-time configuration step that does not exist yet and is a **Deferred Item**.

## No generic command forwarding

The request envelope's `Operation` field is a closed string enum checked against `PipeProtocol.AllowedOperations` (`Connect`, `Disconnect`, `Status`, `ValidateConfiguration`) before any dispatch occurs. There is no operation that accepts a shell command, executable path, or arbitrary argument list - `RawConfigText` is the only free-form string field, and it is only ever handed to the WireGuard configuration parser, never to a process launcher.

## What is deferred to a later phase

- Actually installing `SecureTunnel.Client.Agent` as a Windows Service (`New-Service`, LocalSystem) - see `windows-service-installation.md`. This phase only makes it *capable* of running as one via `UseWindowsService()`.
- A per-importing-user SID allow-list (narrower than `BUILTIN\Users`).
- Pipe protocol versioning/negotiation beyond the fixed `.v1` name suffix.
- Any transport other than Named Pipes (e.g. gRPC) - not needed, not attempted.
