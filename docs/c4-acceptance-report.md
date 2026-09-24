# Phase C4 Acceptance Report — Real Windows Service & WireGuard Acceptance

## Status

**Phase C4 is BLOCKED, not COMPLETED**, per its own completion criteria: real Windows Service installation, real WireGuard connectivity, and cross-identity Named Pipe testing could not be performed in this environment without elevated/administrator access and a WireGuard installation, neither of which was available or authorized this session. The user was asked and chose to proceed with the non-mutating inspection/build/security/regression path rather than granting elevation or installing WireGuard. Every item below is reported honestly against that constraint - nothing was simulated, mocked, or code-reviewed and then reported as a real acceptance result.

## A. Environment

Recorded on this development machine (non-mutating discovery only):

| Property | Value |
|---|---|
| OS | Microsoft Windows 11 Pro, build 10.0.26200 |
| Current user | `desktop-a2s5vv0\owner` |
| Administrator group membership | Present in `BUILTIN\Administrators`, but **deny-only** (UAC-filtered token) |
| Elevation status | **Not elevated** (`IsInRole(Administrator)` → `False`) |
| .NET SDK | 10.0.401 |
| .NET runtimes present | ASP.NET Core 8.0.31 / 10.0.12, .NET Core 8.0.31 / 10.0.12, Windows Desktop 8.0.31 / 10.0.12 |
| WireGuard installed | **No** (`wireguard.exe`/`wg.exe` not found on PATH; `C:\Program Files\WireGuard\wireguard.exe` does not exist) |
| Existing `SecureTunnelAgent` service | Not present |
| WireGuard-related network adapters | None (only an unrelated "OpenVPN Wintun" adapter present, disconnected) |
| Second Windows identity available for cross-identity testing | No |
| Reachable test WireGuard Gateway / protected test resource | Not available / not checked (moot without WireGuard installed) |

**No software was installed and no system state was modified during environment discovery** - every command above was read-only (`Get-CimInstance`, `whoami`, `Get-Command`, `Test-Path`, `dotnet --list-sdks`, `Get-Service`, `Get-NetAdapter`).

## B. Repository inspection

Reviewed the complete C1–C3 implementation already present in this session's working memory: Agent Worker Service (`Program.cs`, `NamedPipeHostedService`, `PipeSessionHandler`), `deploy/install-service.ps1`/`uninstall-service.ps1`, `PipeSecurityFactory`, `WireGuardClientService`/`WireGuardDumpParser`/`WireGuardProcessRunner`, `WireGuardConfigTextParser`/`TunnelFileTextBuilder`, `ClientStateMachine`, `ImportViewModel`/`MainViewModel`, `DiagnosticsViewModel`, `TrayIconService`, `DpapiClientConfigurationStore`. Confirmed: service name `SecureTunnelAgent`, display name "SecureTunnel Agent", Agent executable path resolved via `dotnet publish` to `publish\Agent\SecureTunnel.Client.Agent.exe`, configuration storage path `%ProgramData%\SecureTunnel\Agent\configs`, intended identity LocalSystem (not yet actually running as such - only ever run as the interactive test user in every automated test).

**Readiness assessment:** the codebase is architecturally ready for a real install attempt (path-validated script, correct service name wired end-to-end, `UseWindowsService()` present) but has never actually been installed - see Part G below.

## C. Architecture assumptions verified

- Service name (`SecureTunnelAgent`), display name, and description are wired correctly between `Program.cs` and `deploy/install-service.ps1` - confirmed by reading both files together, not executing the script.
- The Agent publishes to a working `win-x64` executable with all required dependencies present (see Part D/E).
- No development-only absolute paths or embedded secrets found in the published output (see Part E).
- **One assumption from Phase C3 was found to be WRONG and corrected with real evidence this phase**: see Part L (DPAPI compatibility).

## D. Files created

- `docs/c4-acceptance-report.md` (this file)
- `publish/Agent/*`, `publish/Connect/*` - Release, `win-x64` published output (gitignored; evidence artifacts for Part E, not committed)

## E. Files modified

- `tests/SecureTunnel.Client.Infrastructure.Tests/Storage/DpapiClientConfigurationStoreTests.cs` - added `LoadAsync_LegacyCurrentUserScopedData_SameIdentity_ActuallyDecryptsSuccessfully` (see Part L)
- `docs/security-model.md` - corrected the DPAPI scope-change claim with real evidence
- `docs/testing-guide.md` - updated test counts (138 total) and referenced this report
- `docs/roadmap.md` - C4 status added

## F. Implemented

No new product features were implemented this phase (by design - C4 is acceptance-only). One test correction and its documentation fix were made.

## G. Real Windows Service validation

| Item | Status | Evidence | Limitation |
|---|---|---|---|
| Install the service | **Blocked** | N/A | No elevated/administrator shell available; user chose not to grant elevation this session. |
| Verify service name/display name/executable path/account/startup type/recovery config | **Blocked** | N/A | Depends on install. |
| Start/Stop/Restart, verify RUNNING state | **Blocked** | N/A | Depends on install. |
| Uninstall after testing | **Blocked** | N/A | Depends on install. |
| Install script exists, is path-validated, uses explicit (non-concatenated) `New-Service` parameters | **Passed (static/inspection only)** | `deploy/install-service.ps1` read in full; syntax-checked with `[System.Management.Automation.Language.Parser]::ParseFile` in Phase C3 (`Parser errors: 0`) | This is a code-review/syntax-check pass, explicitly **not** a real installation - per this phase's own instruction, not claimed as installation success. |

## H. Service identity validation

**Blocked entirely.** No service is installed, so there is no running process whose identity, DPAPI access, or resource access could be inspected. `whoami`/token inspection was performed only for the *current interactive session* (see Part A), not for an installed service.

## I. Named Pipe cross-identity validation

| Item | Status | Evidence |
|---|---|---|
| Authorized interactive user connects successfully | **Passed (same-identity only)** | `NamedPipeIntegrationTests`/`NamedPipePrivilegedClientProxyTests` (Phase C2/C3, re-run this phase: all pass) exercise a real OS Named Pipe end-to-end as the current interactive identity. |
| Unauthorized identity is rejected / Anonymous access rejected / Network identity rejected | **Blocked** | No second Windows identity available to attempt a real connection as. `PipeSecurityFactoryTests` verifies only the constructed `PipeAccessRule` objects (deny entries for `Everyone`/`ANONYMOUS LOGON`/`NETWORK` are present), not a live denial. |
| Malformed / unknown-operation / oversized request rejected | **Passed** | `PipeSessionHandlerTests` + `NamedPipeIntegrationTests` (real pipe) - re-run this phase, all pass. |
| Service restart → client receives `ServiceUnavailable` | **Blocked (real), Passed (unit-level)** | No installed/restartable service exists. `NamedPipePrivilegedClientProxyTests.ServiceNotRunning_ReturnsServiceUnavailable` proves the client-side mapping when no listener is present at all, which is the closest achievable proxy for "service down." |
| No generic command execution possible through IPC | **Passed** | `PipeSessionHandler`'s operation allow-list is a closed switch (verified by `PipeSessionHandlerTests.UnknownOperation_ReturnsInvalidRequest`/`UnsupportedOperation_Rejected`), re-confirmed by re-reading the source this phase. |
| `BUILTIN\Users` ACL breadth assessment | **Accepted Risk, documented, not newly mitigated this phase** | `BUILTIN\Users` is broader than a single interactive user - any local account can reach the pipe once a service is actually installed. No narrower ACL was implemented this phase (would require a real install to test against). |

## J. Real WireGuard validation

**Entirely Blocked.** WireGuard is not installed in this environment (see Part A) and no test Gateway/protected resource was available or supplied. None of the 24 sub-items in the original Part 7 checklist (config import through a real Gateway, real Connect, real interface/peer/handshake observation, real Disconnect, reconnect, etc.) were attempted. All WireGuard-outcome testing in this repository (Phase C1–C3, re-confirmed this phase) uses `FakeProcessRunner`/`FakeWireGuardClientService`, explicitly labeled test doubles - never `wireguard.exe` itself, except `WireGuardProcessRunnerTests`, which spawns a benign `ping.exe`/`cmd.exe` purely to prove the generic process-runner's timeout/kill mechanism works, not WireGuard-specific behavior.

## K. WPF end-to-end validation

| Item | Status | Evidence |
|---|---|---|
| WPF executable builds/publishes | **Passed** | `dotnet publish src/SecureTunnel.Connect -c Release -r win-x64` succeeded; `SecureTunnel.Connect.exe` (162,816 bytes) confirmed present with all dependencies in `publish/Connect/`. |
| Launch unelevated | **Not Executed** | The published exe was not actually launched in this session (no interactive desktop verification was performed as part of this non-interactive acceptance pass). |
| Import/validate/connect/diagnose/disconnect/tray manual walkthrough | **Not Executed / Blocked** | Requires an interactive session and, for Connect/Diagnose, a reachable Agent - neither exercised. ViewModel-level logic for all of these is Unit-tested (`SecureTunnel.Client.Connect.Tests`, 14/14 passing, re-confirmed this phase) but that is explicitly not the same as a real UI walkthrough. |

## L. DPAPI compatibility validation

**Real finding, corrects a Phase C3 assumption.** A new test, `LoadAsync_LegacyCurrentUserScopedData_SameIdentity_ActuallyDecryptsSuccessfully`, was added and actually executed on this Windows machine:

1. Data is saved with `DataProtectionScope.CurrentUser` (simulating a pre-C3 saved configuration).
2. The same data is then loaded through a store configured with `DataProtectionScope.LocalMachine` (the current default).
3. **Result: the load succeeds and returns the correct private key.**

This is real evidence, not an assumption: Windows' `CryptUnprotectData` determines which master key to use from metadata embedded in the DPAPI blob itself, not solely from the scope flag passed by the calling application. Phase C3's documentation claimed this would be a "breaking storage-format change with no migration path" - that claim was **too broad** and has been corrected in `docs/security-model.md`. For a single-user desktop where the same interactive account both created Phase C1/C2 data and now runs a C3+ build unelevated, **no data loss occurs and no re-import is required**.

**What remains unverified (genuinely Blocked, not merely relabeled):** whether a *different* Windows identity - specifically LocalSystem, once the Agent is actually installed as a service - can decrypt data a different interactive user encrypted. This was not testable without an elevated install and remains an open question for Phase C5.

## M. Security verification

Re-reviewed (not newly re-implemented) against the checklist, all Passed except where noted:

| Control | Status |
|---|---|
| No shell command concatenation | **Passed** - `grep -rn "\.Arguments\s*=" src/` returns no matches. |
| No arbitrary executable path input | **Passed** - `install-service.ps1` validates path is absolute, `.exe`, exists, no `..` segments. |
| No generic command execution | **Passed** - closed allow-lists in `PrivilegedOperationDispatcher`/`PipeSessionHandler`, re-verified. |
| No private keys in argv/IPC/logs/UI | **Passed** - re-confirmed via existing redaction tests, all re-run this phase. |
| Restricted Named Pipe ACL | **Accepted Risk** - `BUILTIN\Users`, not per-user; see Part I. |
| Malformed/oversized/unknown-operation IPC fail-closed | **Passed** |
| No full-tunnel default | **Passed** - `AllowedIPs` required, never defaulted, re-verified in parser. |
| Disconnect is tunnel-scoped | **Passed** (by code inspection + fakes-based test; not real-WireGuard-verified - see Part J) |
| WPF remains unelevated | **Passed** - `app.manifest` confirmed `asInvoker`, and published `.exe` was not launched-and-inspected for a UAC prompt (would require Part K's Not-Executed step), so this is a static-manifest-inspection result, not a runtime observation. |
| Agent owns privileged operations | **Passed** - `SecureTunnel.Connect.csproj` confirmed to have no `SecureTunnel.Client.Agent` reference. |
| Temporary configuration files cleaned up | **Passed** - checked for leftover `stc-*.conf` files in the temp directory after the full test run; none found. |
| Service executable/configuration paths controlled | **Passed (script-level)** - `install-service.ps1`'s path validation reviewed; not exercised against a real install. |

No new "Failed" security findings this phase.

## N. Tests added

One: `DpapiClientConfigurationStoreTests.LoadAsync_LegacyCurrentUserScopedData_SameIdentity_ActuallyDecryptsSuccessfully` (Real Windows - genuine DPAPI encrypt/decrypt on this machine, not mocked). No other tests were added, per the instruction not to add tests solely to inflate the count - C4 surfaced exactly one defect-worthy finding (a documentation/assumption error, not a code defect) and that is what got a regression test.

## O. Test results

138/138 passing (was 137 before this phase's one addition). See `docs/testing-guide.md` for the full per-project breakdown, re-run and confirmed during this phase.

## P. Build results

`dotnet build SecureTunnelClient.slnx` (Debug): 0 warnings, 0 errors, 9 projects. `dotnet build SecureTunnelClient.slnx -c Release`: 0 warnings, 0 errors. `dotnet publish` for both `SecureTunnel.Client.Agent` and `SecureTunnel.Connect` at `-r win-x64 --self-contained false`: both succeeded, executables and all dependencies confirmed present, no dev-only absolute paths or key-shaped strings found in the compiled output (one coincidental base64-shaped match was found in a third-party `Microsoft.Extensions.Logging.Console.dll` and manually confirmed to be embedded source-file-name metadata, not a secret).

## Q. Passed

Build (Debug, Release, publish); all 138 automated tests; same-identity Named Pipe round trip (real OS pipe); DPAPI same-identity round trip across both scopes (real, including the corrected legacy-data finding); temp file cleanup; static security checklist (no shell concatenation, no generic exec, redaction, path validation, WPF-unelevated-by-manifest, Agent-owns-privileged-ops).

## R. Failed

None.

## S. Blocked

Real Windows Service install/start/stop/restart/uninstall; service identity runtime verification; cross-identity Named Pipe access-denied testing (unauthorized/anonymous/network identities); all real WireGuard validation (config-to-Gateway, connect, interface/peer/handshake observation, disconnect, reconnect); interactive WPF UI walkthrough (launch, import, connect, diagnostics, tray); cross-identity DPAPI (LocalSystem-vs-interactive-user) verification.

## T. Known limitations

- The codebase has never been exercised end-to-end against a real installed Windows Service or real WireGuard binary in any phase (C1–C4).
- `BUILTIN\Users` pipe ACL is broader than a single authorized user.
- Cross-identity DPAPI behavior (LocalSystem reading interactive-user-encrypted data) is unverified.

## U. Accepted Risks

`BUILTIN\Users` Named Pipe ACL breadth (unchanged from Phase C3, re-affirmed here since no narrower ACL could be tested without a real install).

## V. Deferred Items

Per-user pipe SID allow-list; real Windows Service installation and its acceptance testing; real WireGuard connectivity testing; cross-identity DPAPI verification; a possible DPAPI migration path if the cross-identity case is ever found to fail. All deferred to Phase C5, contingent on an elevated environment with WireGuard installed becoming available.

## W. Not Implemented

Everything explicitly out of scope per the phase spec (Control Plane API, Admin Portal, accounts, Linux/mobile clients, cloud sync, auto-update, enterprise dashboard, multi-Gateway management, full-tunnel automation, unrelated firewall/adapter management) - unchanged, none of it was touched.

## X. Documentation updated

`docs/security-model.md` (corrected DPAPI claim), `docs/testing-guide.md` (test counts + reference to this report), `docs/roadmap.md` (C4 status), and this new `docs/c4-acceptance-report.md`.

## Y. Release readiness

**Not release-ready.** Phase C4's own completion criteria (real service install, real WireGuard tunnel, real handshake, cross-identity ACL verification) were not met - all Blocked due to environment constraints (no elevation, no WireGuard, no second identity), not due to any code defect found. The one genuine finding this phase (DPAPI same-identity round-trip works better than Phase C3 assumed) is good news, not bad, but does not substitute for the Blocked items above. Per the phase's own rule, this is reported as **CONDITIONALLY COMPLETED / BLOCKED**, not COMPLETED.

## Z. Recommended next phase: C5 - Elevated Real-Windows & Real-WireGuard Acceptance

Exact prerequisites for a rerun:
1. An elevated (Administrator) PowerShell session on a Windows machine, authorized in advance.
2. WireGuard for Windows installed (`https://www.wireguard.com/install/` - or internal package source per organizational policy).
3. A second Windows user account (local or domain) for cross-identity Named Pipe testing.
4. A disposable/test WireGuard Gateway endpoint and a non-production test configuration (never production credentials).

With those in place, re-run Part 4 (install/start/stop/restart/uninstall), Part 5 (identity verification), Part 6 (cross-identity pipe tests), Part 7 (real WireGuard connect/handshake/disconnect), and Part 8 (interactive WPF walkthrough) exactly as specified in this phase's brief, and only then mark C4/C5 COMPLETED.
