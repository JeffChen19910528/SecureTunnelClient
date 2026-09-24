# Deployment Guide

## Status

- **Implemented & Tested (packaging path, real evidence):** `deploy/package.ps1` was actually executed on this development machine and produced real, versioned, checksummed artifacts - see `docs/c6-packaging-report.md`.
- **Implemented, not executed (install path):** `deploy/install-service.ps1`/`uninstall-service.ps1` are written, syntax-checked, and reviewed but have never been run - real installation remains Blocked (no elevation available/authorized - see `docs/c4-acceptance-report.md`).
- See `docs/installer-technology-assessment.md` for why this is PowerShell-based rather than an MSI/MSIX/Inno Setup installer at this stage.

## Deployment layout

### Application files (immutable per install, replaced on upgrade)

| Component | Location (recommended production path) | Produced by |
|---|---|---|
| Agent | `%ProgramFiles%\SecureTunnel\Agent\` (e.g. `SecureTunnel.Client.Agent.exe` + dependencies) | `deploy\package.ps1` → `SecureTunnel.Client.Agent-<version>-win-x64.zip` |
| WPF Client | `%ProgramFiles%\SecureTunnel\Connect\` | `deploy\package.ps1` → `SecureTunnel.Connect-<version>-win-x64.zip` |

Both are published framework-dependent for `win-x64` (the target machine needs the .NET 10 Desktop Runtime - see Prerequisites below); this keeps the distributable small and avoids bundling a full self-contained runtime per component. `.pdb` files are excluded from the distributable zip (kept in the local publish output only, for developer-side crash-symbol resolution).

### Mutable data (never touched by an application-file upgrade or repair)

| Data | Location | Written by | Protection |
|---|---|---|---|
| Client configuration (incl. encrypted private/preshared keys) | `%ProgramData%\SecureTunnel\Agent\configs\*.stc` | `DpapiClientConfigurationStore` | DPAPI `LocalMachine` scope + directory ACL restricted to `NT AUTHORITY\SYSTEM` and `BUILTIN\Administrators` only (provisioned by `install-service.ps1`) - **not** `BUILTIN\Users`-readable, unlike the Named Pipe itself (see `docs/ipc-protocol.md`). |
| Temporary tunnel files | `%TEMP%\stc-<guid>.conf` | `WireGuardClientService` | Per-file ACL restricted to the Agent's own identity at creation time; deleted in a `finally` block immediately after use (including on timeout - see `docs/wireguard-connection-lifecycle.md` and the Phase C5 regression test `ConnectAsync_OperationTimesOut_TempTunnelFileIsStillCleanedUp`). |
| Service logs/diagnostics | Windows Event Log (`Microsoft.Extensions.Logging.EventLog`, wired in automatically by `AddWindowsService()` when running as an installed service) | .NET Generic Host default logging | Standard Windows Event Log ACLs - no separate log file directory is created by this phase; a dedicated rotating file log is a **Deferred Item** if Event Log proves insufficient in practice. |
| WPF diagnostics | In-memory only (`DiagnosticsViewModel`), not persisted to disk | N/A | N/A - nothing to protect; see `docs/diagnostics.md`. |

**No private key or other secret is ever placed in a source-controlled file, an installer log, a shortcut, or a command-line argument** - re-verified this phase (see `docs/c6-packaging-report.md` Part M).

## Windows Service installation (`SecureTunnelAgent`)

Handled by `deploy/install-service.ps1`:

1. Validates the supplied executable path (absolute, `.exe`, exists, no `..` segments - unchanged from Phase C3, re-verified).
2. Detects an existing installation: same version + same path → idempotent no-op (exit 0, no error); different version/path → fails with a clear message instructing an uninstall-then-reinstall (Phase C6 does not attempt an in-place binary swap of a *running* service - see `docs/installer-technology-assessment.md` on why this is weaker than an MSI's atomic upgrade).
3. Provisions `%ProgramData%\SecureTunnel\Agent\configs` with a restricted ACL (new this phase).
4. Registers the service (`New-Service`, `StartupType Automatic`, explicit `DisplayName`/`Description`).
5. Configures recovery behavior (new this phase): restart on the first two failures, no action on the third, via `sc.exe failure` (the only mechanism available on Windows PowerShell 5.1 for this - still explicit, non-concatenated arguments, not a shell string).
6. Does **not** start the service - starting is a separate, explicit operator action (`Start-Service SecureTunnelAgent`).

Identity: **LocalSystem** (the `New-Service` default when no `-Credential` is supplied) - see `docs/windows-service-boundary.md`'s Service Identity Rationale for why.

## WPF Client installation

Phase C6 does not build a Start Menu shortcut installer (no MSI/Inno Setup exists yet - see the technology assessment). The WPF client is deployed as a plain framework-dependent publish output (`deploy\package.ps1`'s `SecureTunnel.Connect-<version>-win-x64.zip`), extracted to `%ProgramFiles%\SecureTunnel\Connect\` and launched directly (`SecureTunnel.Connect.exe`) or via a manually-created shortcut. It:

- Never requires Administrator to launch (`app.manifest` still `asInvoker`, re-verified this phase).
- Communicates only through `IPrivilegedClientService`/the Named Pipe - no direct Agent/WireGuard/DPAPI access (unchanged, re-verified: `SecureTunnel.Connect.csproj` has no reference to `SecureTunnel.Client.Agent`).
- Resolves no development-only absolute paths - re-verified by `deploy\package.ps1`'s automated packaging security check (see `docs/c6-packaging-report.md`), which is what caught and led to fixing a real embedded-PDB-path finding this phase.

A proper Start Menu shortcut / `.lnk` creation step is a **Deferred Item** pending a real installer technology (WiX).

## Upgrade, repair, and uninstall behavior

| Scenario | Behavior |
|---|---|
| **Fresh install** | `install-service.ps1` with no prior service present: provisions data directory, registers service, configures recovery. |
| **Reinstall (same version)** | `install-service.ps1` detects identical version + path → idempotent no-op, exit 0. |
| **Upgrade (newer version)** | `install-service.ps1` detects a version/path mismatch → fails with an explicit instruction to run `uninstall-service.ps1` (without `-RemoveData`) first, then re-run install. Configuration in `%ProgramData%\SecureTunnel\Agent\configs` is untouched by this sequence - uninstall alone never deletes it (see below). This is a manual, scripted upgrade procedure, not an atomic MSI upgrade - documented as a real limitation, not glossed over. |
| **Repair** | Not implemented as a distinct operation this phase. The documented procedure is: re-run `deploy\package.ps1` to produce a fresh publish, then `uninstall-service.ps1` + `install-service.ps1` to re-register. User configuration in `%ProgramData%` is preserved throughout (never deleted by either script unless `-RemoveData` is explicitly passed to uninstall). |
| **Uninstall** | `uninstall-service.ps1` (no `-RemoveData`): stops the service if running, removes it from the Service Control Manager, **does not** touch `%ProgramData%\SecureTunnel\Agent`, does not touch any WireGuard tunnel/interface, does not touch any firewall rule, does not touch any unrelated network adapter (it never invokes `wireguard.exe` or any network-configuration command at all - it only talks to the SCM). `-RemoveData` additionally deletes the data directory - an explicit, opt-in, documented action, never the default. |

**Active-tunnel handling on uninstall is explicit and manual this phase:** the uninstall script does not itself call `wireguard.exe /uninstalltunnelservice` - if a tunnel happens to be active when the Agent service is stopped, the WireGuard-created tunnel service (a *separate*, WireGuard-owned Windows service per `docs/wireguard-connection-lifecycle.md`) is not touched by this script. This is called out as a **Known Limitation**: a fully-polished uninstall would offer to disconnect first. Not implemented this phase, since it would require actually exercising the connection engine during uninstall, which is out of this phase's packaging-focused scope.

## Prerequisites for a real installation (unchanged from Phase C4, restated)

- Windows 10/11, x64.
- .NET 10 Desktop Runtime (framework-dependent publish - not bundled).
- Administrator/elevated PowerShell session to run `install-service.ps1`/`uninstall-service.ps1`.
- WireGuard for Windows installed, if real tunnel connectivity is desired (not required for installing the Client itself).

## Known Limitations

- No atomic upgrade/rollback (PowerShell-based, not MSI-transactional) - see `docs/installer-technology-assessment.md`.
- No file-integrity "repair" operation distinct from a full reinstall.
- No Start Menu shortcut creation.
- No uninstall-time active-tunnel disconnect prompt.
- No dedicated rotating file log directory (relies on Windows Event Log).
