# Phase C6 Packaging Report — Windows Client Packaging, Installer & Deployment Readiness

## Status

**CONDITIONALLY COMPLETED** for standalone packaging scope. Packaging itself (build, publish, versioning, checksums, an automated security check) was **actually executed** on this development machine and is real evidence, not simulated. Real elevated Windows Service installation remains exactly as Blocked as documented in `docs/c4-acceptance-report.md` - it was out of this phase's own scope (packaging and installer *logic*, not installation execution) and was not re-attempted.

## A. Repository inspection

Reviewed the WPF project, Agent project, Core/Infrastructure projects, service host, Named Pipe implementation, DPAPI storage, all configuration/temp/data paths, the existing `deploy/install-service.ps1`/`uninstall-service.ps1` (Phase C3, enhanced in Phase C4/C5's absence of change), README, and existing test conventions (xUnit, `Test double` labeling, per-project layering).

**Current deployment assumptions found:** no shared solution-wide version existed before this phase (each csproj defaulted to `1.0.0.0` implicitly); `install-service.ps1` required the operator to already have a published Agent binary at a known path but had no companion script that actually produces one in a versioned, checksummed way; the data directory (`%ProgramData%\SecureTunnel\Agent\configs`) was referenced in code (`DpapiClientConfigurationStore`'s default) but never explicitly provisioned with an ACL by any script - it would have been created with `%ProgramData%`'s default, broader ACL had the Agent simply run and written to it for the first time.

**Existing installation scripts:** `deploy/install-service.ps1`/`uninstall-service.ps1` (Phase C3), reviewed and enhanced this phase.

**Required runtime dependencies:** .NET 10 Desktop Runtime (Windows) on the target machine (framework-dependent publish, confirmed via `dotnet publish --self-contained false`).

**Service registration requirements:** `New-Service` with `-BinaryPathName`, `-DisplayName`, `-Description`, `-StartupType Automatic`; LocalSystem identity (default, no credential supplied).

**Required directories and permissions:** `%ProgramData%\SecureTunnel\Agent\configs` (DPAPI data, now ACL-restricted to SYSTEM+Administrators as of this phase), `%TEMP%` (transient tunnel files, already ACL-restricted per-file since Phase C3/C4).

**Existing uninstall behavior (before this phase):** stopped and removed the service only; never touched the data directory (implicitly - there was no explicit data-directory awareness in the script at all before this phase).

**Risks/missing pieces identified:** no versioning scheme; no repeatable packaging step; no automated check for embedded development-only paths in shipped binaries (this phase's real run found exactly such an issue); no idempotency in `install-service.ps1` (a second run would simply fail rather than detecting "already installed at this version"); no explicit data-retention control on uninstall.

**Proposed installer approach:** see Part B.

## B. Installer technology assessment

See `docs/installer-technology-assessment.md` for the full comparison (WiX MSI, Inno Setup, MSIX, PowerShell-based) against elevation behavior, upgrade/repair/rollback support, per-machine installation, WPF+Service compatibility, CI/CD ease, operational complexity, and security. **Recommended primary approach: extend the existing PowerShell-based deployment** (zero new toolchain dependency, already partially built). **Recommended future production installer: WiX MSI**, deferred - MSIX was evaluated and rejected as a poor fit for a LocalSystem Windows Service specifically; multiple installer technologies were deliberately not introduced simultaneously.

## C. Selected packaging strategy

`deploy/package.ps1`: `dotnet publish -c Release -r win-x64 --self-contained false` for both components → versioned output directory (`artifacts/<version>/`) → automated packaging security check (development-path scan) → `Compress-Archive` per component, excluding `.pdb` → SHA256 checksum file per archive → a `MANIFEST.txt` documenting artifact contents. Version is read from the new shared `Directory.Build.props` (`Version=0.6.0`).

## D. Files created

- `Directory.Build.props` (shared version + `ContinuousIntegrationBuild`/`PathMap` build-path hygiene)
- `deploy/package.ps1`
- `docs/installer-technology-assessment.md`
- `docs/deployment-guide.md`
- `docs/c6-packaging-report.md` (this file)
- `artifacts/0.6.0/**` (packaging output - gitignored, evidence artifact, not committed)

## E. Files modified

- `deploy/install-service.ps1` - idempotent version/path detection, restricted-ACL data-directory provisioning, explicit service recovery configuration
- `deploy/uninstall-service.ps1` - explicit, opt-in `-RemoveData` switch (data preserved by default)
- `src/SecureTunnel.Client.Infrastructure/Storage/DpapiClientConfigurationStore.cs` - exposed `StorageRoot` as a public property (test seam for path-resolution verification)
- `tests/SecureTunnel.Client.Infrastructure.Tests/Storage/DpapiClientConfigurationStoreTests.cs` - 2 new path-resolution tests
- `.gitignore` - added `artifacts/`
- `README.md`, `docs/security-model.md`, `docs/testing-guide.md`, `docs/roadmap.md`, `docs/windows-service-installation.md` - updated with Phase C6 status and the real packaging finding

## F. Deployment layout

See `docs/deployment-guide.md`'s "Deployment layout" section for the full table (application files vs. mutable data, exact paths, exact protections).

## G. Windows Service packaging

`deploy/package.ps1` publishes `SecureTunnel.Client.Agent` to `artifacts/<version>/Agent/`; `deploy/install-service.ps1` (enhanced this phase, not executed) registers it as `SecureTunnelAgent`, LocalSystem, `StartupType Automatic`, with explicit recovery configuration (restart on first two failures, no action on the third).

## H. WPF packaging

`deploy/package.ps1` publishes `SecureTunnel.Connect` to `artifacts/<version>/Connect/`. No shortcut-creation step exists this phase (Deferred - see `docs/deployment-guide.md`).

## I. Directory and permission handling

`install-service.ps1` now provisions `%ProgramData%\SecureTunnel\Agent\configs` idempotently (`Test-Path` guarded) with an ACL restricted to `NT AUTHORITY\SYSTEM` and `BUILTIN\Administrators` only, inheritance explicitly broken from `%ProgramData%`'s default (broader, `BUILTIN\Users`-readable) ACL. This logic is written and reviewed but **not executed** (requires elevation) - see Part S.

## J. Upgrade behavior

Detected (same version+path → no-op; different → explicit fail-with-instructions), not transactional. See `docs/deployment-guide.md`.

## K. Repair behavior

Not implemented as a distinct operation; documented manual procedure (re-package, uninstall, reinstall) preserves user data throughout. See `docs/deployment-guide.md`.

## L. Uninstall behavior

`uninstall-service.ps1`: stops/removes the service; **never** deletes `%ProgramData%\SecureTunnel\Agent` unless `-RemoveData` is explicitly passed; never touches any WireGuard tunnel, network adapter, or firewall rule (it only calls Service Control Manager cmdlets/`sc.exe delete`, nothing network-related). Active-tunnel-on-uninstall handling is explicitly manual this phase (Known Limitation, documented, not silently glossed over).

## M. Security verification

| Check | Result |
|---|---|
| No arbitrary executable path | Passed - `Assert-ValidExecutablePath`, re-verified |
| No arbitrary service name | Passed - hardcoded `$ServiceName = "SecureTunnelAgent"`, never a parameter |
| No unrestricted command execution | Passed - `New-Service`/`sc.exe failure`/`sc.exe delete` all take explicit, individually-passed arguments |
| No shell concatenation | Passed - re-verified across all `deploy/*.ps1` |
| No secrets in installer arguments/logs | Passed - no script ever references, logs, or accepts a key/secret parameter |
| Controlled installation/service-executable paths | Passed |
| No blanket network/firewall cleanup | Passed - no script touches networking at all |
| No deletion of unrelated WireGuard tunnels | Passed - uninstall never invokes `wireguard.exe` |
| Safe handling of interrupted install/uninstall | Partial - each step is independently re-runnable/idempotent-safe, but there is no transactional rollback (documented limitation, not a failure - see `docs/installer-technology-assessment.md`) |
| **Real finding this phase** | Published binaries embedded the developer machine's absolute build path (`C:\Users\owner\...`) in PDB debug-directory metadata - **found by `deploy/package.ps1`'s own automated check, on a real execution, and fixed** via `Directory.Build.props`'s `PathMap`. Re-verified: a subsequent real packaging run passed with 0 issues. |

## N. Packaging scripts

`deploy/package.ps1` (new), `deploy/install-service.ps1` (enhanced), `deploy/uninstall-service.ps1` (enhanced) - all syntax-checked via `[System.Management.Automation.Language.Parser]::ParseFile`; `package.ps1` additionally **actually executed** (see Part S).

## O. Tests added

2: `DefaultStorageRoot_ResolvesUnder_ProgramData_SecureTunnel_Agent_Configs`, `CustomStorageRoot_OutsideAllowedRoots_IsRejected` (both Unit, Infrastructure.Tests).

## P. Test results

168/168 passing (up from 166). Full breakdown: `docs/testing-guide.md`.

## Q. Build results

`dotnet build SecureTunnelClient.slnx` (Debug): 0 warnings, 0 errors, 9 projects, re-confirmed after all Phase C6 changes including a full clean (`obj`/`bin` removed) rebuild.

## R. Verified locally

Packaging pipeline (real execution, real artifacts, real checksums); packaging security check (real execution, found and confirmed the fix of a real defect); all `deploy/*.ps1` scripts' syntax; path-resolution logic (`DpapiClientConfigurationStore.StorageRoot`, real unit tests); full regression suite (168/168).

## S. Real Windows validation

**Real and executed this phase:**
- `dotnet build -c Release` (0 warnings/errors).
- `deploy/package.ps1` executed three times end-to-end on this real Windows 11 machine: the first run caught the embedded-path defect (the script correctly failed rather than silently shipping it); the second and third runs (after the `PathMap` fix) both succeeded, each producing `artifacts/0.6.0/Agent/SecureTunnel.Client.Agent-0.6.0-win-x64.zip` and `artifacts/0.6.0/Connect/SecureTunnel.Connect-0.6.0-win-x64.zip` with valid SHA256 checksums alongside each. **Note:** the checksums differed between the second and third runs despite identical source - byte-for-byte build reproducibility was not verified/guaranteed this phase (likely PE timestamp or similar non-IL metadata varying between builds); each run's own checksum file is internally consistent (correctly identifies that exact run's archive), which is what matters for integrity verification of a given distributed artifact, but cross-run determinism is a separate, unverified property and is not claimed here.

**Not real / not executed this phase (unchanged from Phase C4):**
- No `install-service.ps1`/`uninstall-service.ps1` execution (requires elevation, not available/authorized).
- No real Windows Service registration, start, stop, restart, or uninstall.
- No real data-directory ACL provisioning (the logic was written and reviewed, not run).

## T. Blocked

Everything requiring elevation: real service install/start/stop/restart/uninstall, real data-directory ACL provisioning, real cross-identity pipe testing (unchanged from C4/C5), real WireGuard connectivity (unchanged).

## U. Failed

None. (The embedded-path issue was a real finding, not a "Failed" test outcome - it was caught, fixed, and re-verified within this phase, so it is reported as a resolved finding, not an open failure.)

## V. Known limitations

No atomic upgrade/rollback; no file-integrity repair; no Start Menu shortcut; no uninstall-time active-tunnel disconnect prompt; no dedicated rotating file log directory (relies on Windows Event Log). All listed in `docs/deployment-guide.md`.

## W. Accepted Risks

Unchanged from Phase C4/C5: `BUILTIN\Users` Named Pipe ACL breadth.

## X. Deferred Items

WiX MSI production installer; Start Menu shortcut creation; atomic upgrade/rollback; file-integrity repair; uninstall-time tunnel disconnect; per-user pipe SID allow-list (all unchanged carry-forwards except the WiX MSI recommendation, which is new this phase).

## Y. Not Implemented

Everything explicitly out of scope per the phase spec (Gateway, Control Plane, Admin Portal, server-side policy/nftables/NAT, protected-resource integration, Linux/Mobile clients, cloud sync, auto-update service, multi-Gateway management, full-tunnel automation, unrelated firewall/adapter management, large WPF redesign) - untouched.

## Z. Documentation updated

`README.md`, `docs/security-model.md`, `docs/testing-guide.md`, `docs/roadmap.md`, `docs/windows-service-installation.md`, plus new `docs/installer-technology-assessment.md`, `docs/deployment-guide.md`, and this report.

## AA. Release readiness

**Not release-ready** - real elevated installation and real WireGuard connectivity remain unverified (unchanged from C4/C5). Packaging itself is real and repeatable, which is meaningful forward progress, but does not by itself make the product release-ready.

## AB. Recommended next phase: C7 - Elevated Real-Windows & Real-WireGuard Acceptance

Exact prerequisites and scope: see `docs/roadmap.md`'s "Recommended next phase: C7" section (identical prerequisites to the C4 report's Part Z, now additionally able to use the enhanced, idempotent `install-service.ps1` and the real packaged artifacts from this phase as the actual install source).

---

**Final status: CONDITIONALLY COMPLETED** (standalone packaging scope, per this phase's own three-way status vocabulary) - implementation and real local packaging evidence exist; real elevated installation is Blocked, not attempted, and not claimed as passed.
