# Testing Guide

## Status

All figures below reflect an actual `dotnet test` run executed on this development machine (Windows) against .NET SDK 10.0.401. Nothing here is aspirational.

```
dotnet build SecureTunnelClient.slnx
dotnet test SecureTunnelClient.slnx
```

**Build result: 0 warnings, 0 errors, 9 projects (also verified in Release configuration and via `dotnet publish -r win-x64` for both `SecureTunnel.Client.Agent` and `SecureTunnel.Connect`, and via the full `deploy\package.ps1` packaging pipeline - see `docs/c4-acceptance-report.md` and `docs/c6-packaging-report.md`).**
**Test result: PASSED. Total tests: 168 (xUnit). Passed: 168. Failed: 0. Skipped: 0.** (Re-confirmed in Phase C7 across three consecutive full-solution runs, after fixing a real load-dependent flakiness defect in `WireGuardProcessRunnerTests.RunAsync_CancelledMidRun_KillsTheProcess` - see `docs/c7-acceptance-report.md` Part O. Re-confirmed again, unchanged, during the final closeout audit - see `docs/final-client-closeout-report.md` Section F.)

**Phase C7-ENV addition: 16 real Pester unit tests** for the new environment-readiness tooling (`deploy/C7EnvironmentChecks.psm1`, covered by `deploy/tests/C7EnvironmentChecks.Tests.ps1`) - all passing, re-confirmed after two real defects found and fixed during this phase (see `docs/c7-environment-readiness.md` and the Pester test file's own comments): (1) an empty-array `.Count`/StrictMode crash in `Test-C7AdministratorGroupMember`, and (2) that same function returning the wrong answer for a deny-only Administrators token - the exact scenario this project has documented since Phase C4 - fixed by switching from token-group inspection to `Get-LocalGroupMember`. Run with `Invoke-Pester -Script deploy\tests\C7EnvironmentChecks.Tests.ps1` (requires the Pester module; this machine has Pester 3.4.0 available).

| Test project | Tests | Passed | Failed |
|---|---|---|---|
| `SecureTunnel.Client.Core.Tests` | 57 | 57 | 0 |
| `SecureTunnel.Client.Infrastructure.Tests` | 56 | 56 | 0 |
| `SecureTunnel.Client.Agent.Tests` | 37 | 37 | 0 |
| `SecureTunnel.Client.Connect.Tests` | 18 | 18 | 0 |

Phase C4 added one regression test that corrected a Phase C3 documentation assumption with real-Windows evidence - see `docs/c4-acceptance-report.md` and `docs/security-model.md`. Phase C5 added 28 more (state-machine hardening, a 14-scenario WireGuard test double, WPF display-distinction tests, and a timeout-path temp-file-cleanup test), including a real code fix to `MainViewModel.StatusText` - see `docs/c5-standalone-client-acceptance.md`. Phase C6 added 2 more (deployment path-resolution verification) and, separately from `dotnet test`, actually ran `deploy\package.ps1` end-to-end, which found and led to fixing a real embedded-developer-path defect in published binaries - see `docs/c6-packaging-report.md`.

`SecureTunnel.Client.Connect.Tests` is new in Phase C3 - it tests the pure WPF ViewModels (`MainViewModel`, `ImportViewModel`, `DiagnosticsViewModel`) with fakes; `TrayIconService` and the file-picker dialog are **not** covered by automated tests - see "Real Windows acceptance-tested" below.

## Coverage by verification level

**Unit-tested** (fakes/mocks, no real OS resource beyond in-memory objects):
- WireGuard configuration parsing and generation (unchanged since C2): complete `[Interface]`+`[Peer]` parse, missing/invalid peer public key, missing/invalid endpoint, missing AllowedIPs (never defaulted), preshared key redaction, parser round-trip via `TunnelFileTextBuilder`.
- `WireGuardDumpParser`: interface-only → `InterfaceActive`, zero-handshake peer → `AwaitingHandshake`, nonzero-handshake peer → `Connected`, malformed/nonzero-exit → `StatusUnavailable`, private-key line never exposed.
- `WireGuardClientService` (fakes only): `ProcessStartFailed` on start failure, operation-timeout triggers a kill request, `DisconnectFailed` on a failed uninstall, idempotent disconnect (never invokes uninstall when already absent), stdout/stderr redaction of a synthetic secret line.
- Private key/preshared key redaction (`SensitiveString`), diagnostic sanitization, `WireGuardOutcome`'s 16-member shape.
- Client state machine: every transition table entry (including the new `Validating`/`InterfaceActive`/`AwaitingHandshake`/`ServiceUnavailable` states and their triggers), illegal-transition rejection, "`ConnectionConfirmed` is legal only from `AwaitingHandshake`" (checked against every enum value), full happy-path walk from `Disconnected` to `Connected`.
- `ClientAgentServiceFoundation`/`PrivilegedOperationDispatcher`: allow-listed operation surface, no generic execute method, `ConfigurationSummary` population on successful validation, `ConfigurationSummary` has no key-typed property (reflection check).
- `PipeSessionHandler` (via a fake dispatcher and a hand-rolled duplex test stream, not a real OS pipe): unknown-operation rejection, malformed-JSON rejection, oversized-declared-length rejection before any body read, unsupported-operation rejection, structured (never-throwing) error responses.
- `PipeSecurityFactory`: the constructed `PipeSecurity` object grants `BUILTIN\Users` and denies `Everyone`/`ANONYMOUS LOGON`/`NETWORK` - **object-level only**, not a runtime cross-identity test (see below).
- `MainViewModel`/`ImportViewModel`/`DiagnosticsViewModel` (WPF ViewModels, fakes only): `ClientId` flows from a successful import into subsequent Connect/Disconnect/Status calls (regression test for the Phase C1/C2 gap where `ClientId` was permanently empty), status text distinguishes `ServiceUnavailable`/`WireGuardNotInstalled`/other outcomes, import never auto-connects, invalid import shows a validation issue without throwing, diagnostics/clipboard text never contains a key-shaped token.

**Integration-tested** (a real OS Named Pipe, server and client in the same test process, no Windows Service installation involved):
- `NamedPipeIntegrationTests`: a valid request/response round trip; a client that connects then disconnects without sending anything, followed by a fresh server instance still correctly serving a second, well-behaved client; an operation that never receives a request from the client returning within an externally-supplied cancellation budget instead of hanging; an oversized outgoing message being rejected by `PipeFraming` before it's ever put on the wire; a response asserted byte-for-byte to never contain `PrivateKey`/`PresharedKey`.
- `NamedPipePrivilegedClientProxyTests`: the WPF-facing proxy's Connect/Disconnect/Status/ValidateConfiguration calls against a minimal real pipe server, plus `ServiceNotRunning_ReturnsServiceUnavailable` (no server listening at all).
- `DpapiClientConfigurationStoreTests`: real Windows DPAPI (`ProtectedData`) encrypt/decrypt round trips, now including an explicit `DataProtectionScope.LocalMachine` round-trip test (the new default scope) - both scopes verified to actually work unelevated on this development machine.
- `WireGuardProcessRunnerTests`: a real child process (`cmd.exe`/`ping.exe`, not `wireguard.exe`) is launched to prove valid-executable output capture, invalid-path handling (`Started=false`, never throws), and - most importantly - that cancellation genuinely kills the process (`Process.GetProcessesByName` confirms no lingering child after a cancelled long-running `ping`).

**Real Windows acceptance-tested:** none this phase. Specifically **not** tested:
- **No real Windows Service was installed.** `SecureTunnel.Client.Agent` was never registered via `New-Service`/`sc.exe`; every test runs its hosted-service logic as an ordinary process. `deploy/install-service.ps1`/`uninstall-service.ps1` were only syntax-checked (`[System.Management.Automation.Language.Parser]::ParseFile`), never executed - see `windows-service-installation.md`.
- **No real cross-identity access-denied scenario was tested.** `PipeSecurityFactoryTests` only inspects the ACL object `PipeSecurityFactory.Create()` builds; no test connects as a genuinely different Windows identity and observes a denial.
- **No real WireGuard connectivity was tested.** All WireGuard process interaction in `WireGuardClientServiceTests` goes through `FakeProcessRunner`/`FakeWireGuardClientService` (both explicitly labeled as test doubles). No test in this repository has launched the real `wireguard.exe` or established a tunnel - see `wireguard-connection-lifecycle.md`.
- **No WPF UI automation.** `TrayIconService` (WinForms `NotifyIcon`) and the `Microsoft.Win32.OpenFileDialog` file picker are not exercised by any automated test - they require an interactive Windows session. Verified only by `dotnet build` succeeding and manual manifest inspection (no `requireAdministrator`).
- **No Clipboard interaction was tested.** `DiagnosticsViewModel.CopyToClipboardCommand` (which calls `System.Windows.Clipboard`) is not invoked by `DiagnosticsViewModelTests` - only the underlying `BuildClipboardText()` method is tested directly, since `Clipboard` requires a UI-thread/STA context unreliable in headless test runs.

**Blocked, not attempted as automated tests this phase** (would require infrastructure/privileges not available here, or are inherently manual/CI-environment acceptance steps): installing and uninstalling a real Windows Service; a real second-identity pipe access-denied test; real `wireguard.exe` connectivity end-to-end; interactive WPF UI automation (tray icon, file dialog, clipboard).

## Fakes and mocks

Every test double (in `tests/SecureTunnel.Client.TestSupport` or local to a test project's own folder) is XML-doc-commented `Test double - not a real implementation.` at its declaration: `FakeClientConfigurationStore`, `FakeWireGuardClientService`, `FakeProcessRunner` (now with `ThrowOperationCanceled`/`KillRequested`/`ResultsByFirstArgument`), `InMemoryLoggerSink`, `FakePrivilegedClientService`, `FakeOperationDispatcher`, `DuplexTestStream`.

## Reproducing

```
dotnet build SecureTunnelClient.slnx
dotnet test SecureTunnelClient.slnx
```

Both commands were run from the repository root on this development machine and produced the results recorded above. The Infrastructure test run takes roughly 10 seconds because `NamedPipePrivilegedClientProxyTests.ServiceNotRunning_ReturnsServiceUnavailable` genuinely waits out the 10-second client connect timeout with no server listening, and `WireGuardProcessRunnerTests` spawns real (short-lived) child processes - none of this is test flakiness.
