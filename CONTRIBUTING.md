# Contributing

## Development principles

1. Never log, print, or pass as a command-line argument a WireGuard private key or any other secret. Use `SecureTunnel.Client.Core.Security.SensitiveString` for anything secret; never store the key as a plain `string`.
2. Never build an external process command from a concatenated string. Use `ProcessStartInfo.ArgumentList` (via `SecureTunnel.Client.Infrastructure.WireGuard.IProcessRunner`).
3. Never add a generic/free-form privileged operation. Any new privileged capability requires a new `PrivilegedRequest` subtype, a corresponding method on `IPrivilegedOperationHandler`, and a new entry in `PipeProtocol.AllowedOperations` - not a change that lets `PrivilegedOperationDispatcher` or `PipeSessionHandler` accept arbitrary input.
4. Fail closed. On corrupted or ambiguous state (storage, configuration, process output, malformed/oversized IPC message), report failure rather than guessing or silently repairing.
5. Never claim `ClientState.Connected` from a command's exit code alone or from a successful Named Pipe round trip alone; only a corroborated `GetStatusAsync` result may justify that transition.
6. Keep `SecureTunnel.Client.Core` free of I/O and Windows-specific APIs (the IPC wire-protocol types in `Client/Ipc/` operate on generic `Stream` only) so it stays testable without a Windows machine or WireGuard installed.
7. Every fake/mock added to `SecureTunnel.Client.TestSupport` (or local to a test project's `Ipc/` folder) must be XML-doc-commented as a test double, not a real implementation.
8. Never put a private key or preshared key in a Named Pipe request or response envelope, or in any pipe-related log/exception message.
9. Do not report a feature as Passed in documentation unless it was actually executed (`dotnet test`, or a manual check you performed) - see `docs/testing-guide.md`'s status taxonomy (Implemented / Tested / Passed / Blocked / Accepted Risk / Deferred / Not Implemented / Known Limitations).
10. Never take a real Windows Service installation, elevation, or WireGuard installation action without explicit user authorization and a suitable environment - write and syntax-check `deploy/*.ps1` scripts, but do not execute them speculatively.
11. Never claim `Connected`/interface-up/handshake-observed without going through `WireGuardDumpParser`'s actual output parsing - do not add a shortcut that infers connectivity from a process exit code, a pipe round trip, or a timer.
12. The WPF project (`SecureTunnel.Connect`) must never reference `SecureTunnel.Client.Agent` - it reaches privileged operations only through `IPrivilegedClientService` (Core) / `NamedPipePrivilegedClientProxy` (Infrastructure).

## Pre-submit checklist

- [ ] `dotnet build SecureTunnelClient.slnx` succeeds with no new warnings.
- [ ] `dotnet test SecureTunnelClient.slnx` passes.
- [ ] No `ProcessStartInfo.Arguments = ...` (string) usage was introduced (`grep -rn "\.Arguments\s*=" src/`).
- [ ] No new code path logs or exception-messages a raw `SensitiveString.Reveal()` result.
- [ ] Any new privileged operation was added as a new `PrivilegedRequest` subtype + handler method, not a generic execute path.
- [ ] Relevant `docs/*.md` status sections were updated to reflect what was actually implemented/tested, not aspirationally.

See `README.md` for the project layout and `docs/architecture.md` for where new code should live.
