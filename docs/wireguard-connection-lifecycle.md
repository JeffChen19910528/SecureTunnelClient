# WireGuard Connection Lifecycle

## Status

- **Implemented & Tested & Passed:** the outcome model, temp-file handling, process-timeout/kill behavior, idempotent disconnect, and dump-output parsing below - covered by `WireGuardClientServiceTests`, `WireGuardDumpParserTests`, and `WireGuardProcessRunnerTests` (a real short-lived child process, proving actual termination on cancellation).
- **Blocked:** real WireGuard connectivity. No test in this repository has launched the real `wireguard.exe` against a real Gateway; `IProcessRunner` is faked in every WireGuard-outcome test except the process-runner's own generic kill-on-timeout test (which uses a benign `ping.exe`, not `wireguard.exe`).

## Outcome model

`SecureTunnel.Client.Core.WireGuard.WireGuardOutcome` (16 values total):

| Outcome | Meaning |
|---|---|
| `WireGuardNotInstalled` | The `wireguard.exe` binary could not be found. |
| `PermissionDenied` | The process exited with code 5 (WireGuard's own permission-denied exit code). |
| `InvalidConfiguration` | The stored/supplied configuration failed validation or could not be loaded. |
| `ConnectionFailed` | A connect attempt's process exited nonzero, or timed out. |
| `DisconnectFailed` | A disconnect (uninstall) attempt's process exited nonzero, or timed out. |
| `ProcessStartFailed` | The external process could not be started at all (missing binary, OS-level launch failure) - distinct from a process that started and failed. |
| `StatusUnavailable` | A status query itself could not be run or its output could not be parsed - distinct from a *confirmed* absent tunnel. |
| `Connected` | `GetStatusAsync` observed a peer entry with a nonzero `latest-handshake`. |
| `Disconnected` | `GetStatusAsync` observed no tunnel at all (empty dump output, exit 0). |
| `InterfaceActive` | `GetStatusAsync` observed the interface line but no peer entry yet. |
| `AwaitingHandshake` | `GetStatusAsync` observed a peer entry but `latest-handshake == 0`. |
| `Unknown` | A connect/disconnect command was accepted (exit 0) but not yet corroborated by a status check. |
| `ServiceUnavailable` / `PipeConnectionFailed` / `AccessDenied` / `InvalidRequest` | Named Pipe transport-level outcomes - see `ipc-protocol.md`. |

Only `GetStatusAsync` (via `WireGuardDumpParser`) may ever produce `Connected`, `InterfaceActive`, or `AwaitingHandshake` - `ConnectAsync`'s own exit-code-0 path always returns `Unknown`, never a connectivity claim.

## Connection preparation

Before any connect attempt, `WireGuardClientService.ConnectAsync`:
1. Checks `wireguard.exe` is on `PATH` - `WireGuardNotInstalled` if not.
2. Loads the stored `ClientConfiguration` + private key via `IClientConfigurationStore.LoadAsync` - `InvalidConfiguration` on any load failure (this also enforces that Interface private key, Interface address, Peer public key, Peer endpoint, and AllowedIPs are all present, since `IClientConfigurationStore` only ever persists a fully-validated `ClientConfiguration`, per `configuration-format.md`).
3. Never silently fabricates a missing field and never widens `AllowedIPs`.

## Temp tunnel file handling

`WireGuardClientService.ConnectAsync` writes a complete `[Interface]`+`[Peer]` document (via `TunnelFileTextBuilder`, see `configuration-format.md`) to `Path.GetTempPath()/stc-{guid}.conf`:
- The filename uses a cryptographically-random GUID, not a predictable name.
- Immediately after writing, the file is marked `NotContentIndexed` and its ACL is restricted to the current user (`FileSystemAccessRule` granting only that identity `FullControl`, with inheritance removed).
- **Known Limitation, not overclaimed:** there is a brief window between `File.WriteAllTextAsync` completing and the ACL restriction being applied, during which the file carries the temp directory's inherited (broader) ACL. This is an accepted risk for a short-lived file rather than a guarantee - a fully TOCTOU-safe approach would require creating the file with a restrictive ACL atomically at creation time, which is not implemented this phase.
- The file is deleted in a `finally` block regardless of whether the connect attempt succeeded, failed, or the ACL restriction itself failed.
- The private key is never placed on the command line - only the file *path* is passed via `ProcessStartInfo.ArgumentList`.

## Process execution

`WireGuardProcessRunner`:
- Uses `ProcessStartInfo.ArgumentList` exclusively - `grep -rn "\.Arguments\s*=" src/` returns no matches anywhere in this codebase.
- Never invokes `cmd.exe` or `powershell.exe` as an intermediary for WireGuard operations (test code separately spawns a benign process like `ping.exe` purely to prove the generic kill-on-timeout mechanism works - that is test infrastructure, not the production WireGuard code path).
- Every operation (`ConnectAsync`, `DisconnectAsync`, `GetStatusAsync`) runs under a 15-second default timeout (`WireGuardClientService`'s `operationTimeout` constructor parameter). On timeout, `Process.Kill(entireProcessTree: true)` is called before the cancellation propagates - verified by `WireGuardProcessRunnerTests.RunAsync_CancelledMidRun_KillsTheProcess`, which spawns a real 30-second `ping` and confirms no lingering process after a 300ms-triggered cancellation.
- stdout/stderr destined for `UserSafeMessage` is passed through `DiagnosticResultFactory.Sanitize` (strips 42-44-char base64-shaped tokens) plus a line-level filter that drops anything starting with `private-key`/`preshared-key` - defense in depth beyond the dump parser already discarding the interface line.

## Status verification (`WireGuardDumpParser`)

`wireguard.exe /dumptunnelservice <name>` prints one interface line (`private-key`, `public-key`, `listen-port`, `fwmark`) followed by zero or more tab-separated peer lines (`public-key`, `preshared-key`, `endpoint`, `allowed-ips`, `latest-handshake`, `transfer-rx`, `transfer-tx`, `persistent-keepalive`). `WireGuardDumpParser.Parse`:
- Non-started process or nonzero exit → `StatusUnavailable`.
- Empty output, exit 0 → `Disconnected`.
- Interface line only (no peer line) → `InterfaceActive`.
- Peer line present, `latest-handshake == 0` → `AwaitingHandshake`.
- Peer line present, `latest-handshake > 0` → `Connected`.
- Malformed/short peer line → `StatusUnavailable`.

The interface line (which contains the private key) is read only to detect its presence; its content is never returned, stored, or logged - `WireGuardDumpParserTests` includes a test verifying the parser's only output is an outcome enum value.

## Disconnect behavior

`DisconnectAsync` is **idempotent and scoped to exactly the caller's own tunnel**:
1. It calls `GetStatusAsync` first. If the outcome is already `Disconnected`, it returns `Success=true, Outcome=Disconnected` immediately **without invoking `/uninstalltunnelservice` at all** - verified by `DisconnectAsync_TunnelAlreadyAbsent_IsIdempotent_NeverInvokesUninstall`, which asserts the uninstall command was never issued.
2. Only when a tunnel is actually present does it invoke `/uninstalltunnelservice <sanitized-clientid>` - `<sanitized-clientid>` is derived from the caller's own `ClientId` (alphanumeric characters only), so the operation can only ever target that one tunnel, never a wildcard or a different tunnel name.
3. A nonzero exit on an actual uninstall attempt maps to `DisconnectFailed` (previously mis-mapped to a generic failure in Phase C1/C2) with a redacted error message.

No code path in this client touches Windows firewall rules, unrelated network adapters, or any WireGuard tunnel other than the one identified by the caller's own `ClientId`.
