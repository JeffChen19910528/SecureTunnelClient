# Roadmap

## Phase C1: Windows Client Architecture & Foundation (complete)

Domain models/interfaces, WireGuard config parser, DPAPI storage, WireGuard process adapter, privileged operation contract (in-process only), minimal WPF shell. 49/49 tests passing at completion. Known limitation carried forward: no peer identity on `ClientConfiguration`, no real service/IPC boundary.

## Phase C2: Complete WireGuard Configuration & Windows Service IPC (complete)

**Implemented:** complete `[Interface]`+`[Peer]` configuration model, a real .NET 10 Worker Service host, a Named Pipe IPC transport, `IPrivilegedClientService` + `NamedPipePrivilegedClientProxy` wired into a minimal WPF shell.

**Tested & Passed:** 77/77 automated xUnit tests at completion, including real in-process Named Pipe round trips and real DPAPI encryption round trips.

**Blocked/Deferred carried into C3:** real Windows Service installation, real cross-identity pipe ACL testing, real WireGuard connectivity, a production-grade per-identity pipe ACL, the real WPF import/connect/diagnostics UX.

## Phase C3: Windows Service Deployment, WireGuard Connection Engine & Client UX (this phase, complete)

**Implemented:**
- Richer `WireGuardOutcome` (16 values: added `ProcessStartFailed`, `DisconnectFailed`, `StatusUnavailable`, `InterfaceActive`, `AwaitingHandshake`) and a matching `ClientState`/`ClientStateTrigger` expansion (`Validating`, `InterfaceActive`, `AwaitingHandshake`, `ServiceUnavailable`), with `Connected` now reachable **only** from `AwaitingHandshake` - see `docs/wireguard-connection-lifecycle.md`.
- `WireGuardDumpParser`, parsing real `wireguard.exe /dumptunnelservice` output to distinguish interface-up from handshake-confirmed.
- Process-execution hardening: per-operation timeout with actual process-tree kill on cancellation (`WireGuardProcessRunner`), idempotent tunnel-scoped disconnect, stdout/stderr redaction.
- `SecureTunnel.Client.Agent`'s service name corrected to `SecureTunnelAgent` (display name "SecureTunnel Agent"); a Service Identity Rationale documenting why LocalSystem was chosen (`docs/windows-service-boundary.md`).
- Hardened `PipeSecurityFactory` (grants `BUILTIN\Users`, denies `Everyone`/`ANONYMOUS LOGON`/`NETWORK` - up from a current-user-only grant that made no sense once the server runs as LocalSystem).
- DPAPI storage moved to `LocalMachine` scope / `%ProgramData%` (a breaking, undocumented-migration change - see `docs/security-model.md`).
- `deploy/install-service.ps1` / `uninstall-service.ps1` - path-validated, explicit-parameter install/uninstall scripts (written, syntax-checked, **not executed**).
- WPF: a real import flow (`ImportViewModel`, file picker, configuration summary display), a diagnostics panel (`DiagnosticsViewModel`), a system tray presence (`TrayIconService`, `System.Windows.Forms.NotifyIcon` - no new NuGet dependency), and the `MainViewModel.ClientId` gap fix (it now actually flows from a successful import).

**Tested & Passed:** 137/137 automated xUnit tests (up from 77), including a new `SecureTunnel.Client.Connect.Tests` project for the WPF ViewModels and a real-child-process test proving the process-timeout kill actually works - see `docs/testing-guide.md`.

**Explicitly Blocked / Deferred / Not Implemented this phase:**

- **Blocked:** installing `SecureTunnel.Client.Agent` as a real Windows Service and testing it under the Service Control Manager; a real cross-identity Named Pipe access-denied test; real `wireguard.exe` connectivity; interactive WPF UI automation (tray icon, file dialog, clipboard). See `docs/windows-service-installation.md` and `docs/testing-guide.md` for the full, honest breakdown - 18 of the Part-12 acceptance checklist's 20 items were not achievable in this environment.
- **Deferred:** a per-importing-user pipe SID allow-list (narrower than `BUILTIN\Users`); automatic migration of pre-C3 `CurrentUser`-scoped DPAPI blobs; a service-installer package; a dedicated pipe-only health check distinct from `GetStatusAsync`.
- **Not Implemented:** Control Plane API, remote configuration delivery, user login/accounts, Admin Portal, Linux/Mobile clients, auto-update, a full enterprise dashboard, cloud synchronization, automatic full-tunnel VPN, unrelated firewall/network-adapter management, advanced traffic analytics, multi-Gateway management. Gateway repository code was not touched.

## Phase C4: Real Windows Service & WireGuard Acceptance (attempted, CONDITIONALLY COMPLETED / BLOCKED)

**Environment discovery (real, non-mutating):** this development machine is not elevated (UAC deny-only admin token), has no WireGuard installation, and has no second Windows identity available - see `docs/c4-acceptance-report.md` Part A. The user was asked whether to grant elevation/install WireGuard for real acceptance testing and chose to proceed with the non-mutating path instead.

**Genuine finding this phase:** a real-Windows test (`LoadAsync_LegacyCurrentUserScopedData_SameIdentity_ActuallyDecryptsSuccessfully`) showed a Phase C3 assumption was too broad - `CurrentUser`-scoped DPAPI data remains readable by a `LocalMachine`-scoped store when accessed by the *same* Windows identity, correcting the "breaking change, no migration" claim for the common single-user-desktop case. See `docs/security-model.md`.

**Blocked (all real-environment acceptance items):** real Windows Service install/start/stop/restart/uninstall; service identity runtime verification; cross-identity Named Pipe access-denied testing; all real WireGuard validation (connect, interface/peer/handshake observation, disconnect, reconnect); interactive WPF UI walkthrough; cross-identity DPAPI verification (LocalSystem vs. interactive user). Full item-by-item breakdown, evidence, and exact rerun prerequisites: `docs/c4-acceptance-report.md`.

**Tested & Passed (everything achievable without elevation/WireGuard/a second identity):** Release build; `dotnet publish -r win-x64` for both Agent and WPF client, confirmed executables + dependencies present, no dev-only paths or embedded secrets; 138/138 automated tests (was 137, +1 this phase); temp-file cleanup verified; full static security checklist re-confirmed.

## Phase C5: Standalone Client Hardening & Local Acceptance (complete)

Deliberately Gateway-free and elevation-free: hardened the client entirely through unit/local-integration/test-double coverage. Rebuilt `FakeWireGuardClientService` into a 14-scenario named test double; hardened `ClientStateMachineTests` against repeated-Connect/repeated-Disconnect/ServiceUnavailable-vs-WireGuard-failure conflation; extended WPF ViewModel tests for `InterfaceActive`/`AwaitingHandshake` display distinctions and repeated-disconnect safety; added a timeout-path temp-file-cleanup regression test. Found and fixed one real (if minor) defect: `MainViewModel.StatusText` was discarding a specific timeout message in favor of a generic one. Reviewed (did not redesign) the `BUILTIN\Users` pipe ACL per the phase's explicit "evaluate, don't redesign" instruction. 166/166 tests passing (up from 138). See `docs/c5-standalone-client-acceptance.md` for the full breakdown. **Status: COMPLETED** for standalone-client scope - real Service/WireGuard/Gateway items remain exactly as Blocked/Not Verified in `docs/c4-acceptance-report.md`.

## Phase C6: Windows Client Packaging, Installer & Deployment Readiness (complete)

Evaluated WiX MSI / Inno Setup / MSIX / PowerShell-based deployment (`docs/installer-technology-assessment.md`); chose PowerShell-based for this phase (zero new toolchain dependency) with WiX MSI recommended and Deferred for a real production installer. Defined the deployment layout (application files vs. mutable data - `docs/deployment-guide.md`); added a shared `Directory.Build.props` (versioning). Wrote and **actually ran** `deploy/package.ps1` - a repeatable, idempotent local packaging script that builds/publishes both components, runs an automated packaging security check, and produces versioned, checksummed zip artifacts. **That real run found and fixed a genuine defect**: published binaries embedded the developer machine's local absolute build path in their PDB debug-directory metadata (standard .NET behavior absent explicit configuration) - fixed via Roslyn `PathMap` in `Directory.Build.props`, re-verified by re-running the packaging script until its security check passed. Enhanced `install-service.ps1` (idempotent version/path detection, restricted-ACL data-directory provisioning, explicit recovery configuration) and `uninstall-service.ps1` (explicit, opt-in `-RemoveData` - preserves user configuration by default). Added 2 path-resolution regression tests. 168/168 tests passing (up from 166). See `docs/c6-packaging-report.md` for the full breakdown. **Status: CONDITIONALLY COMPLETED** - packaging/installer-logic scope is complete and partly real-evidence-backed (the packaging run itself), but real elevated installation remains exactly as Blocked as in `docs/c4-acceptance-report.md` (unchanged - not re-attempted this phase, since it was explicitly out of C6's own scope).

## Phase C7: Elevated Real-Windows & Real-WireGuard Acceptance (attempted, BLOCKED)

Explicitly asked the user for elevation and/or a WireGuard install to attempt real C7-A/C7-B acceptance; the user chose to document as Blocked again (environment unchanged from Phase C4). Within that constraint, did everything genuinely achievable for real: re-verified packaging (fresh versioned/checksummed artifacts); ran a real, standalone probe of the data-directory ACL-provisioning mechanism (`New-Item`/`Set-Acl` against a disposable probe directory) - it succeeded and the resulting ACL genuinely locked the same non-elevated session out of deleting its own probe directory, confirming the mechanism works in practice, not just in source; ran a real unelevated `New-Service` probe against a disposable service name and captured its `Access is denied` failure, confirming elevation is genuinely required rather than assumed. **Found and fixed one real, reproducible defect**: `WireGuardProcessRunnerTests.RunAsync_CancelledMidRun_KillsTheProcess` was flaky under the load of a full-solution test run (passed in isolation, sometimes failed alongside the other three test assemblies) due to a fixed 500ms delay being insufficient under contention - fixed by polling up to 5s instead of a single fixed wait; re-verified clean across three consecutive full-solution runs (168/168 each). See `docs/c7-acceptance-report.md` for the full A-Z breakdown, including a disclosed, inert leftover probe directory (`C:\ProgramData\SecureTunnel-C7-Test`) that this session cannot remove without elevation. **Status: BLOCKED** for the phase's own primary objective (real Service install, real WireGuard runtime) - real Service/WireGuard/cross-identity items remain exactly as Blocked/Not Verified as in `docs/c4-acceptance-report.md`, now with two additional pieces of real supporting evidence and one real regression fix.

## Phase C7-ENV: Real Windows Acceptance Environment Preparation (this phase, CONDITIONALLY COMPLETED)

**Explicitly not a rerun of C7 and does not replace real acceptance.** C7 itself remains **BLOCKED** exactly as recorded in `docs/c7-acceptance-report.md`; nothing in this phase changes that status. This phase instead builds and real-executes the tooling a future elevated rerun needs, so that rerun can be fast, evidence-generating, and non-destructive from the first command:

- `deploy/C7EnvironmentChecks.psm1` + `deploy/check-c7-environment.ps1` - a non-destructive, 16-item readiness probe, real-executed and repeatable (confirmed 13 READY / 3 NOT READY / 0 BLOCKED on this machine across two runs: elevation, WireGuard installation, and WireGuard-on-PATH are the three genuine gaps).
- `deploy/collect-c7-evidence.ps1` - a read-only, sanitized evidence-collection harness for the actual install/start/stop/restart/connect/disconnect/uninstall sequence, real-executed once this phase (produced a real, accurate "not yet installed" evidence snapshot).
- `docs/c7-elevated-setup-guide.md` - a manual operator procedure for the elevated session, WireGuard install, second identity, and test data, none of which this phase performed or automated.
- `docs/c7-evidence-collection.md`, `docs/c7-cleanup-guide.md` - design/reference docs for the evidence harness and a strictly-scoped cleanup procedure (including the still-undisclosed-but-tracked leftover `C:\ProgramData\SecureTunnel-C7-Test` probe directory from Phase C7).
- **Two real defects found and fixed** during this phase's own tooling development (not in shipped product code): an empty-array `Set-StrictMode` crash pattern (two instances), and - more significant - `Test-C7AdministratorGroupMember`'s first implementation returning the wrong answer for a deny-only Administrators token (the exact condition this project's security model has documented since Phase C4), fixed by switching from token-group inspection to `Get-LocalGroupMember`. See `docs/security-model.md` and `docs/c7-environment-readiness.md`.
- 16/16 new Pester tests passing; 168/168 xUnit tests re-confirmed unaffected (no product C# code changed this phase).

See `docs/c7-env-report.md` for the full A-X phase report.

## Final Closeout Audit (complete)

A repository-wide closeout audit re-verified every phase's evidence rather than trusting historical reports alone: re-ran the Debug and Release builds (0 warnings/0 errors each), the full xUnit suite (168/168), the Pester suite (16/16), `deploy/package.ps1` (checksums regenerated, dev-path scan clean), and `deploy/check-c7-environment.ps1` (13 READY/3 NOT READY, identical to prior runs) - no discrepancy was found against the recorded phase statuses above. A repository/security scan found no committed secrets, no generic command-execution surface, no debug-only code paths, and no accidental Gateway-repository changes. **Final classification: DEVELOPMENT COMPLETE, LOCALLY VERIFIED, Real Windows Runtime NOT VERIFIED, Real WireGuard NOT VERIFIED, RELEASE BLOCKED.** No new development phase is required before a real C7 rerun. See `docs/final-client-closeout-report.md` for the full A-S report.

## Recommended next phase: C8 - Elevated Real-Windows & Real-WireGuard Acceptance

Exact prerequisites (see `docs/c4-acceptance-report.md` Part Z, `docs/c7-acceptance-report.md` Part Y, and `docs/c7-environment-readiness.md` for full detail):
1. An elevated (Administrator) PowerShell session, authorized in advance.
2. WireGuard for Windows installed.
3. A second Windows user account for cross-identity Named Pipe testing.
4. A disposable/test WireGuard Gateway endpoint and a non-production test configuration.
5. Removal of the disclosed leftover probe directory `C:\ProgramData\SecureTunnel-C7-Test` from Phase C7, if not already cleaned up (`Remove-Item -Recurse -Force` from an elevated shell - inert, safe to leave otherwise; see `docs/c7-cleanup-guide.md`).

With those in place: run `deploy/check-c7-environment.ps1` first to confirm all mandatory items are READY, then actually install `SecureTunnel.Client.Agent` via `deploy/install-service.ps1` (now idempotent/version-aware, with restricted-ACL data directory provisioning) and verify start/stop/restart/status under the real Service Control Manager; attempt the real cross-identity pipe ACL denial test; attempt a real, manually-verified `wireguard.exe` connect/disconnect cycle and confirm a peer handshake is actually observed via `WireGuardDumpParser`'s real output; verify cross-identity DPAPI behavior (LocalSystem reading interactive-user-encrypted data) and write a migration path only if that is found to actually fail; capture every step's evidence via `deploy/collect-c7-evidence.ps1`; only after a real, tested connection path exists end-to-end, build the WiX MSI recommended in `docs/installer-technology-assessment.md` and revisit auto-update.
