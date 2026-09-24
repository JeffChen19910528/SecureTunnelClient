# Phase C7 Acceptance Report — Elevated Real-Windows & Real-WireGuard Acceptance

## Status

**BLOCKED** for the phase's own primary objective (real elevated Service installation and real WireGuard runtime acceptance). The user was explicitly asked whether to grant elevation and/or install WireGuard for this phase and chose to proceed with the non-mutating/non-elevated path instead - consistent with the phase's own Rule 5 ("if the required environment is unavailable, document the exact blocker and do not fabricate results") and Rule 6 ("do not mark a test as passed merely because a script was written"). What follows is exactly what was genuinely achievable without elevation or WireGuard, including two new pieces of real evidence and one real defect found and fixed.

## A. Environment and privilege context

Re-verified identical to `docs/c4-acceptance-report.md` Part A: Windows 11 Pro, current user `desktop-a2s5vv0\owner`, member of `BUILTIN\Administrators` but **deny-only** (UAC-filtered token), `IsInRole(Administrator)` → `False`, WireGuard not installed (`Get-Command wireguard.exe` → nothing, `C:\Program Files\WireGuard\wireguard.exe` → does not exist). No second Windows identity available. Nothing changed between C4 and C7 in this environment.

**User decision this phase:** asked explicitly (via a clarifying question) whether to grant elevation and/or install WireGuard for real C7-A/C7-B testing; chose "Document as Blocked again."

## B. Repository and deployment baseline

Reviewed the complete C6 deployment implementation (`deploy/package.ps1`, `deploy/install-service.ps1`, `deploy/uninstall-service.ps1`, `Directory.Build.props`, `docs/deployment-guide.md`, `docs/installer-technology-assessment.md`) - all as left at the end of Phase C6, no drift found.

## C. Packaging verification

**Real, executed.** `deploy\package.ps1` re-run fresh this phase:
- Both components published (Release, win-x64, framework-dependent).
- Version metadata consistent: `0.6.0` from `Directory.Build.props`, reflected in the manifest and both archive filenames.
- Checksum generation confirmed working: `SecureTunnel.Client.Agent-0.6.0-win-x64.zip` (SHA256 `B62AF23AB3DC651EC96EB64269EA3A8F9F2811ADDF39C12083DE5D6405E4521A`), `SecureTunnel.Connect-0.6.0-win-x64.zip` (SHA256 `F50F26AE7B9142070E114E12371D0188F65248D4E170DCD525B44D5B180597CB`).
- Package contents re-verified: automated dev-path scan passed (0 issues - confirming the Phase C6 `PathMap` fix still holds); no `.pdb` in the distributable zips; no secrets - no script in this repository ever accepts, logs, or embeds key material, re-confirmed by inspection.
- **As in Phase C6, checksums differ run-to-run** (byte-level build reproducibility still not verified/claimed - unchanged, documented limitation).

## D. Installation results

**Blocked.** `install-service.ps1` was not run in full (it calls `New-Service`, which was independently confirmed this phase to fail without elevation - see Part E). No directories/permissions/service registration from a full script run exist to verify.

**Partial real evidence obtained (new this phase, not a full install):** the directory-creation-and-ACL-provisioning logic that `install-service.ps1` uses was extracted and run for real, standalone, against a disposable probe directory (`C:\ProgramData\SecureTunnel-C7-Test`, not the product's real data path) to verify the *mechanism* independent of the full script:
- `New-Item -ItemType Directory` under `%ProgramData%` **succeeded** without elevation.
- `Set-Acl` restricting the directory to `NT AUTHORITY\SYSTEM` + `BUILTIN\Administrators` only (removing inherited/other rules) **succeeded** without elevation.
- Immediately afterward, **this same non-elevated session could no longer delete the directory it had just created** (`Remove-Item` → `UnauthorizedAccessException: Access to the path ... is denied`) - real, direct evidence that the ACL is genuinely restrictive (not just correct-looking code), since even a local Administrators-group member with a UAC-filtered token was locked out.

**Disclosure:** this probe left `C:\ProgramData\SecureTunnel-C7-Test` on disk (0 bytes, empty directory) because the session that created it cannot remove it without elevation. It is inert and does not resemble or collide with the product's real data path (`%ProgramData%\SecureTunnel\Agent\...`). It can be removed with `Remove-Item -Recurse -Force "C:\ProgramData\SecureTunnel-C7-Test"` from an elevated shell, or safely left in place.

## E. Windows Service registration results

**Blocked, with direct negative-case evidence.** A real (not simulated) `New-Service` call was attempted against a disposable probe name (`SecureTunnelAgent-C7-ProbeOnly`, never the real `SecureTunnelAgent` name, and it failed before creating anything - no cleanup needed):

```
Expected failure: ServiceCommandException: Service ' (SecureTunnelAgent-C7-ProbeOnly)' cannot be created due to the following error: Access is denied
```

This is real evidence that Windows Service registration genuinely requires elevation in this environment (not an assumption) - and that `install-service.ps1`'s own `New-Service` call would fail identically. No service was created; nothing to clean up.

## F. Windows Service runtime results

**Blocked.** No service exists to start/stop/restart/query. Not attempted, not claimed.

## G. Named Pipe and ACL results

**Unchanged from Phase C4/C6, re-confirmed by re-running the existing real in-process tests this phase (not new work):** `NamedPipeIntegrationTests` and `NamedPipePrivilegedClientProxyTests` (real OS Named Pipe, same-identity) pass. `PipeSecurityFactoryTests` (ACL object inspection) pass. **Real cross-identity pipe ACL testing remains Blocked** - no second Windows identity available, unchanged.

## H. DPAPI results

**Unchanged core finding from Phase C4** (same-identity `CurrentUser`→`LocalMachine` DPAPI round trip succeeds - re-confirmed by re-running `DpapiClientConfigurationStoreTests` this phase, all pass). **New this phase:** the data-directory ACL provisioning that protects this DPAPI data was verified as a real, standalone mechanism (see Part D) - it genuinely restricts access, though this was tested against a probe directory, not the actual installed Agent's real data path under a real service identity. **Real installed-service DPAPI persistence-across-restart remains Blocked** - no service exists to restart.

## I. Upgrade and uninstall results

**Blocked.** No installation exists to upgrade, repair, or uninstall for real. `install-service.ps1`'s idempotency logic (same version/path → no-op; different → explicit fail-with-instructions) and `uninstall-service.ps1`'s data-retention logic (`-RemoveData` opt-in) were reviewed by re-reading the Phase C6 code - no changes found necessary, no defects found in this review pass.

## J. Real WireGuard environment prerequisites

Not met: WireGuard for Windows is not installed; no test peer/Gateway was supplied (the user declined to provide one). Per this phase's own instruction ("Only execute this section if a real WireGuard test environment is available"), Phase C7-B was not attempted.

## K. Real WireGuard execution results

**Not executed / Blocked - entirely.** None of the 15 items in Phase C7-B's checklist (executable discovery through Gateway-unavailable behavior) were attempted for real. All existing WireGuard-outcome coverage in this repository continues to go through `FakeWireGuardClientService`/`FakeProcessRunner` (explicitly labeled test doubles) or, for the one real-process test that exists, a benign `ping.exe` proving generic kill-on-timeout mechanics - never `wireguard.exe` itself.

## L. Connection state transition results

Not newly verified against real WireGuard this phase (Blocked, see K). The state machine's correctness (Connected reachable only via `AwaitingHandshake`, no shortcut from a bare command or pipe success) remains verified only at the unit/test-double level (Phase C1-C5), re-confirmed passing this phase, not newly proven against a real tunnel.

## M. Disconnect and isolation results

Not newly verified against real WireGuard this phase. Existing unit/test-double coverage of tunnel-scoped, idempotent disconnect (Phase C3-C5) re-confirmed passing, not newly proven against a real tunnel or real unrelated interfaces.

## N. Security verification

Re-checked this phase (code inspection + the real evidence above): no shell concatenation (re-grepped, clean); no arbitrary executable path (unchanged validation logic reviewed, no defect found); no private keys in any command line/log/report/artifact produced this phase (the packaging manifest, ACL probe output, and `New-Service` probe output above contain no key material - manually re-checked before writing this report); the DPAPI data-directory ACL is now confirmed **genuinely restrictive in practice**, not just correct-looking source (Part D). No new security issue found.

## O. Defects discovered

**One, real, reproducible:** `WireGuardProcessRunnerTests.RunAsync_CancelledMidRun_KillsTheProcess` failed when the full solution test suite ran under load (all four test assemblies executing together via `dotnet test SecureTunnelClient.slnx`) but passed reliably in isolation.

- **Reproduction:** run `dotnet test SecureTunnelClient.slnx` (whole solution); observe `RunAsync_CancelledMidRun_KillsTheProcess` fail with "Collection was not empty: [System.Diagnostics.Process (PING)]". Run the same test alone (`--filter FullyQualifiedName~RunAsync_CancelledMidRun_KillsTheProcess`); it passes.
- **Root cause:** the test cancelled a real `ping.exe` child process, then waited a single fixed `Task.Delay(500)` before asserting no lingering `ping` process remained. Under the additional CPU/IO contention of the full solution's four test assemblies executing concurrently, 500ms was not always sufficient for the OS to finish tearing down the killed process before the assertion ran - a test-timing issue, **not** a defect in `WireGuardProcessRunner`'s actual kill logic (the kill call itself is unconditional and correct; it just hadn't always finished by the fixed checkpoint).
- **Fix:** replaced the single fixed delay with a poll loop (up to 5 seconds, checking every 100ms) before asserting. This removes the load-dependent flakiness without weakening the assertion - it still requires the process to actually be gone, just allows the OS more time to get there under contention.
- **Verification:** re-ran the full solution suite three consecutive times after the fix; all three runs passed 168/168 with 0 failures (see Part R).

No other defects were found this phase - the ACL/directory provisioning logic, the `New-Service` elevation requirement, and the packaging pipeline all behaved exactly as documented, with no discrepancy between documented and observed behavior.

## P. Defects fixed

The one defect from Part O, in `tests/SecureTunnel.Client.Infrastructure.Tests/WireGuard/WireGuardProcessRunnerTests.cs`.

## Q. Regression tests added

None added as a *new* test - the existing `RunAsync_CancelledMidRun_KillsTheProcess` test itself was hardened (poll instead of fixed delay) and now serves as its own regression coverage against the flakiness. Per this phase's instruction not to add tests solely to inflate the count, and since the existing test already covers the exact scenario, no separate new test was created.

## R. Full test results

**168/168 passing**, re-run three consecutive times after the flakiness fix (all three clean - see Part O for why three runs specifically). Unchanged total count from Phase C6 (no new tests added, one existing test hardened).

## S. Build and publish results

`dotnet build SecureTunnelClient.slnx`: 0 warnings, 0 errors. `deploy\package.ps1`: succeeded, real versioned/checksummed artifacts produced (Part C).

## T. Evidence collected

- Packaging manifest and SHA256 checksums (Part C).
- Real `New-Item`/`Set-Acl`/`Remove-Item` output proving the data-directory ACL mechanism works and is genuinely restrictive (Part D).
- Real `New-Service` failure output proving elevation is genuinely required (Part E).
- Three consecutive full-suite test run summaries, 168/168 each (Part R).
- All of the above are reproduced verbatim (no secrets present in any of it) in this report.

## U. Blocked tests

Real Windows Service install/start/stop/restart/status/logs/uninstall-of-a-real-installation; real cross-identity Named Pipe ACL denial; all real WireGuard execution (all 15 Phase C7-B items); real installed-service DPAPI persistence-across-restart; real upgrade/repair/uninstall of an actual installation.

## V. Not executed tests

Same list as Blocked (U) - in this environment, "Blocked" and "Not executed" are the same set, since every blocked item is blocked specifically because it was never executed, not because it was executed and inconclusive.

## W. Accepted risks

Unchanged from Phase C4/C5/C6: `BUILTIN\Users` Named Pipe ACL breadth, not yet validated against a real second identity.

## X. Deferred items

Unchanged from Phase C6 (WiX MSI, Start Menu shortcut, atomic upgrade/rollback, file-integrity repair, uninstall-time tunnel disconnect, per-user pipe SID allow-list) - nothing new deferred this phase.

## Y. Production release blockers

Unchanged and restated: no real Windows Service has ever been installed in any phase through C7; no real WireGuard tunnel or handshake has ever been observed in any phase through C7; no real cross-identity ACL denial has been observed. All three remain hard blockers to a production release claim, and none were resolved this phase (by the user's own explicit choice to document rather than provide the required environment).

## Z. Final status

**BLOCKED** for Phase C7's primary objective (real elevated Service installation and real WireGuard runtime acceptance) - the required environment (elevation, WireGuard, a second identity, a test Gateway/peer) was not provided, exactly as anticipated and explicitly confirmed with the user before proceeding. Within that constraint, everything genuinely achievable was done for real (not simulated): packaging re-verified, a real ACL-mechanism probe, a real elevation-requirement probe, and one real, reproducible test-flakiness defect found and fixed with the full regression suite re-confirmed clean (168/168 × 3 consecutive runs). No fabricated results appear anywhere in this report.

**Cross-reference (added by Phase C7-ENV, does not change the status above):** Phase C7-ENV subsequently built and real-executed a non-destructive readiness probe, an evidence-collection harness, and a manual elevated-setup guide intended to make a future rerun of this phase faster and more evidence-complete. That phase did not re-attempt any item in this report and this report's **BLOCKED** status stands unchanged. See `docs/c7-environment-readiness.md` and `docs/c7-env-report.md`.
