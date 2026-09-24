# C7-ENV Acceptance Environment Preparation Report

## Status

**This phase is NOT a rerun of Phase C7 and does not claim Phase C7 is completed.** Phase C7 (`docs/c7-acceptance-report.md`) remains **BLOCKED** for its own primary objective (real elevated Windows Service installation and real WireGuard runtime acceptance), unchanged by anything in this phase. Phase C7-ENV's objective was narrower and different: prepare non-destructive tooling, documentation, and a read-only evidence harness so that a *future* elevated rerun of C7 can proceed efficiently and with better evidence capture. That preparation work is what this report evaluates.

## A. Objective and scope

Build and real-execute: (1) a non-destructive environment-readiness probe, (2) a manual elevated-setup guide for the operator, (3) a controlled, read-only evidence-collection harness, (4) supporting documentation, and (5) automated tests for all new tooling - without installing anything, without requiring elevation for any of the tooling itself, and without fabricating any result. Explicitly out of scope: re-attempting C7's actual install/connect/cross-identity items, any destructive or broad system change, any credential automation.

## B. Environment discovery (real, unchanged from C7)

Re-confirmed via the new readiness probe (Part D below): this development machine is a member of `BUILTIN\Administrators` via a **deny-only** token (not elevated), has no WireGuard for Windows installation, and has no `wireguard.exe` discoverable on `PATH`. No second Windows identity is available. Ground truth cross-checked against `whoami /groups`. This matches every prior phase's discovery (C4, C7) - nothing about the underlying environment changed this phase, only the tooling used to observe it.

## C. Readiness probe (`deploy/check-c7-environment.ps1`)

Implemented as a 16-item, non-destructive, read-only probe (`deploy/C7EnvironmentChecks.psm1` + `deploy/check-c7-environment.ps1`). Status values restricted to READY / NOT READY / BLOCKED / NOT APPLICABLE / NOT CHECKED. **Real-executed twice**, both runs identical:

- 13 READY (build artifacts present, `.NET` SDK present, service-name availability, data-directory path validity, script syntax validity, Administrators-group membership correctly detected, etc.)
- 3 NOT READY: Elevation state; WireGuard for Windows installed; WireGuard executable discoverable on PATH
- 0 BLOCKED, 0 NOT APPLICABLE, 0 NOT CHECKED
- Exit code 1 (mandatory-for-C7 items not all READY), by design

Full row-by-row detail: `docs/c7-environment-readiness.md`.

## D. Evidence-collection harness (`deploy/collect-c7-evidence.ps1`)

Nine read-only collection functions (service status/identity, installation directory, data-directory ACL, Named Pipe connectivity, WireGuard discovery/interfaces, a sanitized diagnostic summary, and `Save-C7Evidence` to persist it). **Real-executed once** this phase via `Save-C7Evidence`, producing `artifacts/c7-evidence/c7-evidence-20260924-095314.json` (gitignored, local only) - an accurate snapshot reflecting the real not-yet-installed state (service not found, WireGuard not discoverable, pipe unreachable with a `Timeout` reason, 0 WireGuard interfaces). No mutating action (start/stop/install/connect) was performed - none exists in this script by design. Every free-text field is routed through `Protect-C7SensitiveText` before being written. Design/mapping detail: `docs/c7-evidence-collection.md`.

## E. Manual elevated-setup guide

`docs/c7-elevated-setup-guide.md` documents, for a human operator, the steps this phase deliberately does not automate: obtaining an elevated session, installing WireGuard for Windows from the official source, installing the SecureTunnel client package, creating a second Windows account, and preparing non-production test data - each with an explicit rationale for why it stays manual (never auto-install software from an unaudited source; never automate account/credential creation).

## F. Cleanup guide

`docs/c7-cleanup-guide.md` scopes cleanup strictly to SecureTunnel-created test artifacts (test service, test install directories, test data directory, test logs, temp WireGuard config, test tunnels, the two disclosed probe directories from C7/C7-ENV, evidence JSON files), with an explicit "does NOT do" section ruling out blanket tunnel/adapter/firewall/service deletion.

## G. Local validation performed (real execution only)

- `check-c7-environment.ps1` run twice, `collect-c7-evidence.ps1`'s `Save-C7Evidence` run once - all real, all reflecting genuine machine state, no simulated output.
- No elevated or WireGuard-dependent step was attempted or simulated.

## H. Tests

**16/16 new Pester tests passing** (`deploy/tests/C7EnvironmentChecks.Tests.ps1`, Pester 3.4.0 syntax), covering `Get-C7CheckStatus` classification, `Protect-C7SensitiveText` redaction, and elevation/group-membership detection including a machine-specific regression test. **168/168 xUnit tests re-confirmed** (unaffected - no C# product code was touched this phase); both suites re-run as the final step of this phase, both clean.

## I. Defects found and fixed (real, via actual execution)

1. Empty-array `.Count`-under-`StrictMode` crash in `Test-C7AdministratorGroupMember`'s original implementation - fixed by wrapping in `@(...)`.
2. **Semantic defect**, more significant than (1): after fixing (1), the function still returned the wrong answer for this machine's real deny-only-admin state, because `[WindowsIdentity]::GetCurrent().Groups` does not reliably reflect deny-only group entries. This is the exact condition (`BUILTIN\Administrators` member, unelevated) that every phase since C4 has documented as security-relevant. Fixed by switching to `Get-LocalGroupMember -Group "Administrators"`, matched against the current user's SID - verified against `whoami /groups` ground truth, and pinned with a permanent Pester regression test.
3. A second, distinct empty-array `StrictMode` pattern in `check-c7-environment.ps1` item 14 (`.Name` on a zero-element array, which throws even though `.Count` on the same array does not) - fixed with an explicit `if ($stServices.Count -gt 0) {...} else {'(none)'}` guard, and the same defensive `@(...)` wrapping applied to the script's summary counts.

All three verified fixed by re-running the actual script/Pester suite to a clean result, not by inspection alone.

## J. Documentation updated this phase

`docs/testing-guide.md`, `docs/security-model.md`, `docs/windows-service-installation.md`, `README.md`, `docs/roadmap.md`, `docs/c7-acceptance-report.md` (cross-reference only, status unchanged) - each updated to reference the new tooling and the defects found, without altering any prior phase's recorded status.

## K. Security posture (unchanged, reaffirmed)

No private keys, passwords, or tokens are stored anywhere in the new tooling's output. No destructive or broad system change was made. No software was auto-installed. No generic command-execution surface was added anywhere (Named Pipe, WPF, or the new PowerShell tooling) - every new script function does one specific, named, read-only thing.

## L. Blocked (unchanged from C7, not re-attempted this phase)

Real Windows Service installation, real WireGuard connectivity, real cross-identity Named Pipe testing. None of this phase's work performs or simulates these.

## M. Accepted risks / deferred items

Unchanged from Phase C6/C7 (WiX MSI, per-user pipe SID allow-list, etc.) - nothing new deferred this phase beyond the tooling's own scope, which was fully delivered.

## N. Final status

**CONDITIONALLY COMPLETED.** All locally-executable preparation work (readiness probe, evidence harness, operator guide, cleanup guide, documentation, tests) is real, executed, and passing. Real elevated Windows Service installation and real WireGuard validation remain dependent on operator-provided environment (elevation, WireGuard install, second identity) that was not granted this phase - exactly as in Phase C4 and Phase C7. **This phase prepares the environment for a future C7 rerun and explicitly does not replace real acceptance testing.** Phase C7 itself remains **BLOCKED**.
