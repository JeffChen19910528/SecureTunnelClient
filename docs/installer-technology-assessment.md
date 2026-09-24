# Installer Technology Assessment

## Status

**Implemented:** a PowerShell-based deployment approach (packaging + service install/uninstall scripts). **Deferred:** a WiX MSI, evaluated and recommended as the eventual production installer but not built this phase (see rationale below).

## Options compared

| Criterion | WiX MSI | Inno Setup | MSIX | PowerShell-based (chosen) |
|---|---|---|---|---|
| Windows Service install support | Native (`ServiceInstall`/`ServiceControl` elements) | Via `[Run]`/custom code, less native | Poor - MSIX packages are sandboxed/per-user by design and do not cleanly support installing a LocalSystem Windows Service | Native (`New-Service`, already implemented) |
| Elevation behavior | Standard MSI UAC elevation prompt, well understood | Standard, via `[Setup] PrivilegesRequired=admin` | Complex - MSIX elevation for full-trust apps requires additional packaging (`Add-AppxPackage` provisioning, sparse packages) that fights against the sandboxed model | Explicit: the script itself declares "run elevated" in its header; the *caller* (a human or a future wrapping installer) invokes an elevated shell - no self-elevation logic is implemented, avoiding a whole class of elevation-bypass risk |
| Upgrade support | Native (`MajorUpgrade`/`Upgrade` table, product/package GUIDs) | Native (`[Setup] AppId`, version checks) | Native (package identity + version) | Manual/scripted (Part 4's idempotency check: same version+path → no-op; different → instructs operator to uninstall first) - weaker than MSI's atomic upgrade, explicitly documented as a limitation |
| Repair support | Native (`REINSTALLMODE`) | Limited, custom-coded | Native (package re-deploy) | Not implemented - "repair" today means re-running `package.ps1` + `install-service.ps1` after an uninstall; no file-integrity repair of a partially-corrupted install exists |
| Uninstall support | Native, transactional | Native | Native | Implemented (`uninstall-service.ps1`), non-transactional (if it fails partway, no automatic rollback - each step is independently idempotent-safe to re-run, though) |
| Rollback behavior | Native (MSI transactional rollback on failure) | Limited | Native | None - a failed `install-service.ps1` run leaves whatever completed in place; every step is designed to be safely re-runnable rather than auto-rolled-back |
| Per-machine installation | Native, standard | Native, standard | MSIX defaults to per-user unless explicitly provisioned per-machine (extra complexity) | Native - `New-Service` and `%ProgramData%` are inherently per-machine |
| WPF + Windows Service compatibility | Excellent - the standard pattern for exactly this combination | Good | Poor for the Service half (see above); fine for the WPF half alone | Good - this is exactly what a Worker Service + WPF app pairing conventionally uses before a "real" installer is built |
| CI/CD packaging ease | Requires the WiX toolset (`dotnet tool install wix` or the WiX v3 MSBuild SDK) as a build-time dependency not currently in this repo | Requires the separate Inno Setup compiler (`iscc.exe`), not a .NET-native toolchain | Requires `MakeAppx`/`SignTool` and a code-signing certificate for meaningful distribution | Zero new toolchain dependency - `dotnet publish` + built-in PowerShell cmdlets, runs in this repo's existing environment today |
| Operational complexity | Moderate (author WiX XML, manage GUIDs) | Low-moderate | High (packaging identity, signing, sandbox implications) | Low today, but shifts complexity to manual operator discipline (no GUI installer experience, no automatic elevation prompt, no upgrade transactionality) |
| Security considerations | Signed MSI, well-audited installer technology, Windows Installer service handles privilege separation | Signed EXE, custom Pascal-like scripting surface is a larger trust surface than declarative WiX | Strongest sandboxing model, but wrong shape for a LocalSystem service | No new attack surface introduced beyond what already exists (`New-Service`, explicit path validation, no shell concatenation) - but also no installer-level code signing, no tamper-evident package format beyond the SHA256 checksum this phase adds |

## Recommendation

**Primary approach for this phase: the existing PowerShell-based deployment (packaging + install/uninstall scripts), extended in Phase C6.** Rationale:

1. **Zero new toolchain dependency.** WiX, Inno Setup, and MSIX all require installing and maintaining an additional build-time tool this repository does not currently depend on. Given this phase's constraint (no elevation, no ability to install arbitrary new software without authorization - see `docs/c4-acceptance-report.md`), adding a new installer toolchain mid-phase was avoided in favor of extending what Phase C3 already started (`deploy/install-service.ps1`/`uninstall-service.ps1`).
2. **MSIX is a poor fit** for a LocalSystem Windows Service specifically - its sandboxing model actively works against this architecture. It is not recommended even for a future phase unless the Service/Agent split is redesigned, which is explicitly out of scope.
3. **WiX MSI is the recommended long-term production installer** once this project moves toward a real release: it has native, transactional support for exactly the two things the PowerShell approach lacks today (atomic upgrade and rollback), and is the conventional choice for a WPF-app-plus-Windows-Service pairing. This is recorded as a **Deferred Item** for a post-C6 phase, not attempted now.
4. **Inno Setup** was considered as a lighter-weight alternative to WiX but was not chosen over WiX for the eventual production path, since it offers no meaningful advantage over WiX for this specific Service+WPF shape and would still require adding a new toolchain now.

**Do not introduce multiple installer technologies.** This phase deliberately implements exactly one (PowerShell) and documents one recommended future direction (WiX MSI) rather than partially building several.
