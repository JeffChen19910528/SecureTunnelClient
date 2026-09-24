# Windows Service Installation

## Status

- **Implemented:** `deploy/install-service.ps1` and `deploy/uninstall-service.ps1`, path-validated, explicit-parameter, non-shell-concatenated install/uninstall scripts. Extended in Phase C6 with idempotent version/path detection, restricted-ACL data-directory provisioning, explicit service recovery configuration, and explicit (opt-in) data-retention control on uninstall (`-RemoveData`) - see `docs/deployment-guide.md`.
- **Tested:** the scripts were syntax-checked with PowerShell's own parser (`[System.Management.Automation.Language.Parser]::ParseFile`) - they parse without error, re-confirmed after the Phase C6 changes.
- **Blocked:** actually running either script. This development session does not have administrator/elevated access; the user was explicitly asked in Phase C7 whether to grant elevation and chose not to. **No Windows Service has been installed, started, stopped, restarted, or uninstalled as of Phase C7.** (Packaging *up to* the point of installation - build, publish, versioning, checksums - was actually executed and re-verified in Phase C7; see `docs/c6-packaging-report.md` and `docs/c7-acceptance-report.md`.)
- **Real partial evidence (Phase C7):** the data-directory creation + restricted-ACL mechanism `install-service.ps1` uses was independently run for real against a disposable probe directory - it succeeded, and the resulting ACL genuinely blocked the same non-elevated session from deleting what it had just created, confirming the mechanism (not just the code) is sound. A real, unelevated `New-Service` call was also attempted against a disposable probe name and failed with `Access is denied`, confirming elevation is genuinely required (not assumed). See `docs/c7-acceptance-report.md` Parts D/E for full detail.
- **Phase C7-ENV addition:** this phase did not attempt installation again (C7 remains BLOCKED, unchanged), but built the tooling a future elevated rerun needs: a non-destructive readiness probe (`deploy/check-c7-environment.ps1`, real-executed, confirmed this machine is still unelevated with WireGuard not installed - 13/16 checks READY, 3 NOT READY), a manual operator procedure for the elevated setup itself (`docs/c7-elevated-setup-guide.md`), and a read-only evidence-collection harness for the actual install/start/stop/restart/uninstall run once an elevated session is available (`deploy/collect-c7-evidence.ps1`, `docs/c7-evidence-collection.md`). None of this performs installation; it only prepares for and will record evidence from the next real attempt. See `docs/c7-environment-readiness.md`.

## Service identity

See `windows-service-boundary.md`'s "Service Identity Rationale" for why the Agent is designed to run as **LocalSystem**. `New-Service` (used by `install-service.ps1`) defaults new services to `LocalSystem` when no `-Credential` is supplied, which matches this design - the script does not need to (and does not) prompt for or embed any credential.

## Service configuration

| Property | Value |
|---|---|
| Service name | `SecureTunnelAgent` (must match `Program.cs`'s `AddWindowsService(o => o.ServiceName = "SecureTunnelAgent")`) |
| Display name | `SecureTunnel Agent` |
| Description | "Privileged WireGuard connection engine for SecureTunnel Connect. Communicates with the SecureTunnel Connect desktop app only over a restricted local Named Pipe." |
| Startup type | Automatic |
| Binary path | Caller-supplied, validated (see below) |

Display name and description are Service Control Manager metadata - they cannot be set by the hosted `Microsoft.Extensions.Hosting` process itself at runtime, only at registration time, which is why they live in the install script rather than `Program.cs`.

## Path validation (`install-service.ps1`)

Before calling `New-Service`, the script rejects the supplied executable path unless it is:
- non-empty,
- an **absolute** path (`[System.IO.Path]::IsPathRooted`),
- free of `..` path segments,
- ending in `.exe`,
- and actually exists on disk (`Test-Path -PathType Leaf`).

This prevents installing an arbitrary or relative/untrusted executable path. The script never builds a shell command string - `New-Service`'s `-BinaryPathName` parameter is passed the validated, absolute path directly, and `uninstall-service.ps1`'s fallback (`sc.exe delete <name>`, used only on PowerShell versions without `Remove-Service`) passes the service name as a single, unconcatenated argument.

## Lifecycle commands (documented, not executed)

```powershell
# Install (run elevated)
.\deploy\install-service.ps1 -ExecutablePath "C:\Program Files\SecureTunnel\Agent\SecureTunnel.Client.Agent.exe"

# Start / Stop / Restart / Status (run elevated; standard PowerShell cmdlets, no custom scripting needed)
Start-Service SecureTunnelAgent
Stop-Service SecureTunnelAgent
Restart-Service SecureTunnelAgent
Get-Service SecureTunnelAgent

# Uninstall (run elevated)
.\deploy\uninstall-service.ps1
```

None of the commands above were run in this environment. They are documented for a later phase's real-Windows acceptance pass.

## Known Limitations

- No installer package (MSI/EXE) exists - the install script is a manual/scripted step, not a double-click installer. See `docs/installer-technology-assessment.md` for the evaluated alternatives and recommended future direction (WiX MSI).
- No atomic upgrade/rollback - see `docs/deployment-guide.md`.
- The script assumes the Agent binary has already been built/published to the path supplied (typically via `deploy\package.ps1`); it does not build or copy files itself.
