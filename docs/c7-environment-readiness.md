# C7 Environment Readiness

## Status

**Implemented & Tested (real execution).** This document defines the 15 required readiness areas; `deploy/check-c7-environment.ps1` implements and (this phase) actually executed every check below on this real development machine. The results recorded here are real output from that real run, not simulated - re-run `deploy\check-c7-environment.ps1` at any time to reproduce.

## Status value definitions

| Value | Meaning |
|---|---|
| `READY` | The check was actually attempted and the requirement is met. |
| `NOT READY` | The check was actually attempted and the requirement is **not** met (the product/environment genuinely lacks it). |
| `BLOCKED` | The check itself could not be attempted (e.g. a required cmdlet/module is unavailable) - distinct from `NOT READY`, which means the check ran and found a gap. |
| `NOT APPLICABLE` | The check does not apply in this context. |
| `NOT CHECKED` | The check was never attempted for a reason other than a known blocker. |

## Real readiness results (this machine, this run)

| # | Requirement | Detection method | Expected | Actual | Status | Remediation | Security impact |
|---|---|---|---|---|---|---|---|
| 1 | Windows version and architecture | `Get-CimInstance Win32_OperatingSystem` | Windows 10/11, x64 | Windows 11 Pro 10.0.26200, x64 | **READY** | N/A | None - informational. |
| 2 | Current user identity | `[Security.Principal.WindowsIdentity]::GetCurrent()` | A resolvable identity | `DESKTOP-A2S5VV0\owner` | **READY** | N/A | Identity name only, no credential. |
| 3 | Current privilege/elevation state | `Test-C7ProcessElevated` (`WindowsPrincipal.IsInRole(Administrator)`) | Elevated (True) | **False** | **NOT READY** | Relaunch PowerShell "Run as administrator". | Elevation state alone reveals no secret. |
| 4 | Administrator group membership | `Test-C7AdministratorGroupMember` (`Get-LocalGroupMember`) | Informational | **True** (deny-only, unelevated - see `docs/c4-acceptance-report.md`) | **READY** | N/A - informational only. | None. |
| 5 | .NET SDK and runtime versions | `dotnet --list-sdks` / `--list-runtimes` | .NET 10 SDK + `Microsoft.WindowsDesktop.App` 10.x | Both present | **READY** | N/A | None. |
| 6 | WireGuard for Windows installation status | `Test-Path 'C:\Program Files\WireGuard\wireguard.exe'` | Present | **False** | **NOT READY** | Operator installs WireGuard from the official source (not auto-installed - see `docs/c7-elevated-setup-guide.md`). | Do not auto-install from an unaudited source. |
| 7 | WireGuard executable discovery | `Get-Command wireguard.exe` | Found on PATH | **False** | **NOT READY** | Same as #6. | None. |
| 8 | Windows Service management capability | `Get-Service` (query a well-known service) | Query succeeds | True | **READY** | N/A | None. |
| 9 | Windows Event Log access | `Get-WinEvent -ListLog Application` | Accessible | True | **READY** | N/A | None. |
| 10 | Named Pipe capability | Create+dispose a disposable, uniquely-named `NamedPipeServerStream` | Succeeds | True | **READY** | N/A | None - disposable probe pipe only. |
| 11 | Available disk space | `Get-PSDrive` on the system drive | ≥ 1 GB free | 554.8 GB free | **READY** | N/A | None. |
| 12 | Network adapter visibility | `Get-NetAdapter` | ≥ 1 adapter | 9 adapters | **READY** | N/A | Adapter names/descriptions only. |
| 13 | Existing WireGuard interfaces | `Get-NetAdapter` filtered to `InterfaceDescription -match WireGuard` | 0 (clean baseline) | 0 found | **READY** | N/A - a nonzero count would mean a prior tunnel wasn't cleaned up. | Adapter names only. |
| 14 | Existing SecureTunnel services | `Get-Service` filtered to `Name -match SecureTunnel` | Only `SecureTunnelAgent`, or none | 0 found | **READY** | N/A | Service names only. |
| 15 | Test data directory availability | `New-Item` + write/delete probe under `%TEMP%` | Writable | True | **READY** | N/A | Test-only, non-production temp location. |

**Summary: 13 READY, 3 NOT READY (all three are the mandatory C7 prerequisites - elevation, WireGuard installed, WireGuard on PATH), 0 BLOCKED, 0 NOT APPLICABLE, 0 NOT CHECKED.**

This confirms, with real evidence rather than assumption, that this development environment is not yet ready for Phase C7's real Service/WireGuard acceptance - exactly matching what Phase C4 and C7 already concluded, now backed by a repeatable, automated probe rather than one-off manual commands.

## Reproducing

```powershell
.\deploy\check-c7-environment.ps1
```

Exits `0` if all mandatory-for-C7 prerequisites are `READY`, `1` otherwise - usable as a CI/operator gate before attempting a real C7 rerun.
