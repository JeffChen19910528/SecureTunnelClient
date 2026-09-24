# C7 Evidence Collection Design

## Status

**Implemented & Tested (real execution).** `deploy/collect-c7-evidence.ps1` implements every collection function below; this phase actually ran `Save-C7Evidence` and produced a real, sanitized JSON evidence file reflecting this machine's real (not-yet-installed, not-yet-WireGuard-capable) state - see `artifacts/c7-evidence/*.json` (gitignored, local evidence only).

## Evidence items

| Evidence name | Collection command/procedure | Expected output | Sensitive info to redact | Storage location | Retention | Cleanup |
|---|---|---|---|---|---|---|
| Environment readiness | `deploy\check-c7-environment.ps1` | 16-row table + summary counts (READY/NOT READY/BLOCKED/N-A/NOT CHECKED) | None expected; output passed through `Protect-C7SensitiveText` regardless | Console output; optionally redirect to a file under `artifacts/` | Until next C7 rerun | Delete the redirected file; nothing else created. |
| Elevated status | `Test-C7ProcessElevated` / manual `whoami` | Boolean + identity name | None (identity name only) | Console / evidence JSON (`Get-C7SanitizedDiagnosticSummary`) | Session-only unless saved | N/A |
| Service installation | `Get-C7InstallationDirectoryEvidence` | Booleans for Agent/Connect executable presence | None (paths only) | Evidence JSON | Until next C7 rerun | Delete evidence JSON. |
| Service registration | `Get-C7ServiceIdentityEvidence` (`Get-CimInstance Win32_Service`) | `StartName` (expect `LocalSystem`), `PathName` | `PathName` passed through redaction (defense in depth; not expected to contain anything) | Evidence JSON | Until next C7 rerun | Delete evidence JSON. |
| Service startup | `Get-C7ServiceStatusEvidence` before/after `Start-Service SecureTunnelAgent` | `Status`/`StartType` transitioning `Stopped` → `Running` | None | Evidence JSON (call twice, before/after) | Until next C7 rerun | Delete evidence JSON; `Stop-Service` if left running unintentionally. |
| Service shutdown | Same, around `Stop-Service SecureTunnelAgent` | `Running` → `Stopped` | None | Evidence JSON | Until next C7 rerun | Delete evidence JSON. |
| Service restart | Same, around `Restart-Service SecureTunnelAgent` | `Running` throughout (or a brief `Stopped` blip), same `StartName` after | None | Evidence JSON | Until next C7 rerun | Delete evidence JSON. |
| Named Pipe access | `Get-C7NamedPipeConnectivityEvidence` | `Reachable=True` once the service is running; `False` with a redacted `Error` otherwise | Connection error text passed through redaction | Evidence JSON | Until next C7 rerun | Delete evidence JSON. |
| ACL behavior | `Get-C7DataDirectoryAclEvidence` | Exactly `NT AUTHORITY\SYSTEM` + `BUILTIN\Administrators`, `FullControl`, `Allow` | None (SIDs/rights only, no credential) | Evidence JSON | Until next C7 rerun | Delete evidence JSON. |
| DPAPI persistence | Manual: import a test config via the WPF client, `Restart-Service SecureTunnelAgent`, re-query status/connect | `LoadResult.Success=true` after restart (observed via WPF status, not by reading the encrypted file) | **Never** capture the `.stc` file contents or any raw key - only pass/fail of the load | Manual notes / evidence JSON's `ServiceStatus` field around the restart | Until next C7 rerun | Delete any test `.stc` file per `docs/c7-cleanup-guide.md`. |
| Upgrade behavior | `install-service.ps1` run twice (same version → no-op; bumped version → explicit fail-with-instructions) | Exit code 0 (no-op) or a clear error message | None | Console output | Until next C7 rerun | N/A (no state left behind by a no-op or a failed run). |
| Uninstall behavior | `uninstall-service.ps1` (with and without `-RemoveData`) | Service removed; data directory preserved unless `-RemoveData` | None | Console output | Until next C7 rerun | See `docs/c7-cleanup-guide.md`. |
| WireGuard interface state | `Get-C7WireGuardInterfaceEvidence` | Adapter count/names matching the real tunnel's lifecycle | Adapter names only (no addressing) | Evidence JSON | Until next C7 rerun | Delete evidence JSON; `wireguard.exe /uninstalltunnelservice` for any leftover test tunnel. |
| WireGuard handshake | Manual: `wireguard.exe /dumptunnelservice <name>` (real WireGuard CLI, not this repo's tooling) inspected for `latest-handshake > 0` | A specific, non-zero timestamp | **Never capture the private-key or preshared-key line of the dump output** - only the `latest-handshake` field | Manual notes, redacted before saving | Until next C7 rerun | Ensure any saved dump excerpt has the key lines removed before it leaves the operator's machine. |
| Connection state transitions | WPF `DiagnosticsViewModel`/`MainViewModel.State` observed live, or `GetStatusAsync` polled via a real pipe call | `Disconnected → Connecting → InterfaceActive → AwaitingHandshake → Connected` in order, never skipping a step | None (state names only) | Evidence JSON / manual notes | Until next C7 rerun | N/A |
| Disconnect behavior | `DisconnectAsync` twice in a row (idempotency) | Second call succeeds without invoking a real uninstall (see `docs/wireguard-connection-lifecycle.md`) | None | Evidence JSON / manual notes | Until next C7 rerun | N/A |
| Isolation from unrelated interfaces | `Get-NetAdapter` before/after a test disconnect, confirming only the test tunnel's adapter changed | Unrelated adapters (e.g. the pre-existing "OpenVPN Wintun" adapter observed on this machine) unchanged | Adapter names only | Evidence JSON | Until next C7 rerun | N/A |

## What must never be collected or stored

Private keys, passwords, tokens, complete production configuration, unredacted user profiles, or unnecessary machine identifiers (e.g. do not capture a full `whoami /all` dump - only the specific identity name and elevation/group-membership booleans this design calls for). Every automated collection function in `deploy/collect-c7-evidence.ps1` routes free-text fields through `Protect-C7SensitiveText` before they can be written to a report file.
