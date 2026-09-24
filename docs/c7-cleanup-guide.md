# C7 Cleanup Guide

## Status

**Documented.** Scoped strictly to SecureTunnel test artifacts. No blanket/destructive cleanup is implemented or documented here, per Phase C7-ENV Scope Restriction #12.

## What this guide cleans up (and only this)

| Artifact | Location | Command | Requires elevation | Data retained unless removed |
|---|---|---|---|---|
| Test service | `SecureTunnelAgent` (if installed for a C7 test) | `.\deploy\uninstall-service.ps1` | Yes | N/A - service registration only. |
| Test installation directories | `C:\Program Files\SecureTunnel\Agent\`, `C:\Program Files\SecureTunnel\Connect\` (wherever the operator extracted the packaged zips per `docs/c7-elevated-setup-guide.md`) | `Remove-Item -LiteralPath "C:\Program Files\SecureTunnel" -Recurse -Force` | Yes | These are application files only - no user data here. |
| Test data directory | `%ProgramData%\SecureTunnel\Agent\configs` | `.\deploy\uninstall-service.ps1 -RemoveData` (or manually: `Remove-Item -LiteralPath "$env:ProgramData\SecureTunnel\Agent" -Recurse -Force`) | Yes (the ACL provisioned in `docs/deployment-guide.md` restricts this to SYSTEM/Administrators - an elevated session is required to delete it, which is itself a real, working confirmation of the ACL) | **Opt-in only** - `-RemoveData` is never implied. Confirm this is truly test-only data (no real Gateway-issued configuration) before removing. |
| Test logs | Windows Event Log entries under the Agent's log source | `Get-WinEvent` to review; `wevtutil cl Application` clears the **entire** Application log and is explicitly **not** recommended here (too broad - would remove unrelated entries) | Varies | Prefer leaving Event Log entries in place; Windows rotates them automatically. |
| Temporary WireGuard configuration files | `%TEMP%\stc-*.conf` | Already deleted automatically by `WireGuardClientService` in a `finally` block (including on timeout - see `docs/wireguard-connection-lifecycle.md`); if one is found lingering after a crash, `Remove-Item "$env:TEMP\stc-*.conf" -Force` | No | None - these are transient by design, never meant to persist. |
| Test-only WireGuard tunnels | Any tunnel created during C7-B testing | `wireguard.exe /uninstalltunnelservice <name>` (real WireGuard CLI - this repo's own `uninstall-service.ps1` never touches WireGuard tunnels, by design - see `docs/deployment-guide.md`) | Yes | N/A - always remove a test tunnel once acceptance testing is done. |
| `check-c7-environment.ps1`'s test-data probe directory | `%TEMP%\SecureTunnelClient-C7-Probe` | `Remove-Item -Recurse -Force "$env:TEMP\SecureTunnelClient-C7-Probe"` | No | None - write/delete probe only, no product data. |
| Phase C7 (prior phase) leftover probe directory | `C:\ProgramData\SecureTunnel-C7-Test` (disclosed in `docs/c7-acceptance-report.md`) | `Remove-Item -Recurse -Force "C:\ProgramData\SecureTunnel-C7-Test"` | **Yes** - this session could not remove it unelevated, which is itself the evidence that the ACL-provisioning mechanism it was created to test works correctly | None - empty probe directory, inert. |
| C7 evidence files | `artifacts/c7-evidence/*.json` | `Remove-Item -Recurse -Force ".\artifacts\c7-evidence"` | No | Sanitized (no secrets) but still local-only; gitignored, never committed. Retain until the next C7 rerun's results supersede them, then remove. |

## What this guide explicitly does NOT do

Per Scope Restriction #12 and the phase's explicit prohibition list:

- Does **not** delete all WireGuard tunnels - only ones explicitly created for C7 testing, by name, one at a time.
- Does **not** delete all network adapters.
- Does **not** reset all firewall rules (this product has never touched firewall rules in any phase - nothing to reset).
- Does **not** remove all services with similar names - `uninstall-service.ps1` only ever targets the exact, hardcoded name `SecureTunnelAgent`.
- Does **not** delete all `%ProgramData%` contents - only `%ProgramData%\SecureTunnel\Agent`, and only with the explicit `-RemoveData` opt-in.

## If cleanup cannot be safely automated

Every item in the table above already has a documented manual command precisely because Phase C7-ENV's own scope restrictions prohibit broad, automatic cleanup. There is no additional automated "cleanup script" beyond the already-scoped `uninstall-service.ps1 -RemoveData` - every other item is a single, explicit, reviewable command an operator runs deliberately.
