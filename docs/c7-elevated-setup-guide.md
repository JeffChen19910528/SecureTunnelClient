# C7 Elevated Setup Guide (Operator Manual Procedure)

## Status

**Documented, not executed.** This is a manual procedure for a human operator to follow on a real test machine before a Phase C7 rerun. Nothing in this document is automated by this repository's tooling - `deploy/check-c7-environment.ps1` verifies the *result* of these steps, it does not perform them (per Phase C7-ENV Scope Restrictions #9/#10: no automatic destructive/broad security changes, no automatic software installation from unaudited sources).

## 1. Administrator session

**Opening an elevated PowerShell session:**
1. Start menu → type `PowerShell` → right-click "Windows PowerShell" → "Run as administrator" (or right-click `pwsh.exe`/`powershell.exe` and choose the same).
2. Accept the UAC prompt.

**Confirming elevation (do not skip - see the pitfall below):**
```powershell
([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
# Must print True. If False, the session is not actually elevated.
```

**Confirming the effective identity:**
```powershell
whoami
[Security.Principal.WindowsIdentity]::GetCurrent().Name
```

**The pitfall this project has documented since Phase C4:** being a *member* of `BUILTIN\Administrators` is **not** the same as running an *elevated* process. A user can be an Administrators-group member while every ordinary process they launch carries a UAC-filtered, deny-only token - `whoami /groups` will show `BUILTIN\Administrators ... Group used for deny only` in exactly this case. Always check `IsInRole(Administrator)` (or equivalently, that `install-service.ps1` actually succeeds rather than failing with `Access is denied`), never group membership alone. `deploy\check-c7-environment.ps1` reports both signals separately for exactly this reason.

## 2. WireGuard installation

**This repository does not install WireGuard automatically.** The operator must:

1. Download WireGuard for Windows from the official source: `https://www.wireguard.com/install/` (verify the download operator-side; this document does not embed a download step to avoid this repo silently fetching and running third-party installer binaries).
2. Run the installer with the privileges it requests (WireGuard's own installer manages its elevation prompt).
3. **Verify installation** (does not require elevation):
   ```powershell
   Test-Path "C:\Program Files\WireGuard\wireguard.exe"
   Get-Command wireguard.exe -ErrorAction SilentlyContinue
   ```
4. **Verify version:**
   ```powershell
   & "C:\Program Files\WireGuard\wireguard.exe" /version
   ```
   (Confirm the printed version banner contains no unexpected output - do not pipe raw WireGuard output into any shared log without reviewing it first, per the private-key-never-in-logs rule below.)

**Never embed a private key in a command-line argument or a log.** This applies to every manual verification step in this guide too - if you manually run `wireguard.exe` against a real tunnel file while following this guide, do not paste that tunnel file's contents (or any WireGuard CLI output that might echo it) into a shared report, chat, or ticket. `deploy/check-c7-environment.ps1` and `deploy/collect-c7-evidence.ps1`'s output already passes through `Protect-C7SensitiveText`, but manual copy-paste from a terminal does not - review before sharing.

## 3. SecureTunnel Client installation

1. **Run the current package** (produces versioned artifacts, does not itself install anything):
   ```powershell
   .\deploy\package.ps1
   ```
2. **Extract** `artifacts\<version>\Agent\SecureTunnel.Client.Agent-<version>-win-x64.zip` to a stable location, e.g. `C:\Program Files\SecureTunnel\Agent\`.
3. **Execute installation with Administrator privileges** (from the elevated session opened in step 1):
   ```powershell
   .\deploy\install-service.ps1 -ExecutablePath "C:\Program Files\SecureTunnel\Agent\SecureTunnel.Client.Agent.exe"
   ```
4. **Verify service registration:**
   ```powershell
   Get-Service -Name SecureTunnelAgent
   Get-CimInstance Win32_Service -Filter "Name='SecureTunnelAgent'" | Select-Object Name, DisplayName, StartName, PathName, StartMode
   ```
5. **Verify service identity** - `StartName` above should read `LocalSystem` (see `docs/windows-service-boundary.md`'s Service Identity Rationale).
6. **Verify installation directories:**
   ```powershell
   Test-Path "C:\Program Files\SecureTunnel\Agent\SecureTunnel.Client.Agent.exe"
   ```
7. **Verify mutable data directories and ACLs** (provisioned by `install-service.ps1` itself):
   ```powershell
   Test-Path "$env:ProgramData\SecureTunnel\Agent\configs"
   (Get-Acl "$env:ProgramData\SecureTunnel\Agent\configs").Access | Format-Table IdentityReference, FileSystemRights, AccessControlType -AutoSize
   # Expect exactly: NT AUTHORITY\SYSTEM (FullControl, Allow) and BUILTIN\Administrators (FullControl, Allow) - nothing else.
   ```

## 4. Second identity (for cross-identity Named Pipe testing)

**This repository does not create user accounts automatically.** If the operator wants to attempt the cross-identity pipe test Phase C7 documents as Blocked:

1. Create a second **non-administrator** local account manually, e.g. via Settings → Accounts → Family & other users → Add account, or `New-LocalUser` run by the operator interactively (not scripted here, since account creation with a caller-supplied password is exactly the kind of action this guide avoids automating).
2. Log in as that account (or use `runas`/a separate session) to launch `SecureTunnel.Connect.exe` and confirm it can still reach the Agent's Named Pipe (an authorized-but-non-administrator user, matching the intended `BUILTIN\Users`-scoped ACL).
3. For an actual access-denied test, a third scenario is needed: a process running as an identity **not** in the pipe's ACL at all (e.g. a service account with no `BUILTIN\Users` membership) attempting to connect and observing rejection.

**Never document real credentials for this account in this repository.** Use a disposable, test-only password the operator manages themselves; do not commit it anywhere.

## 5. Test data policy

- **Use only test-only WireGuard configuration and test-only keys** - generate a fresh keypair for acceptance testing (`wireguard.exe genkey`/`genpsk` equivalents, or the WireGuard GUI's own key generation), never reuse a real Gateway's production peer keys.
- **Never reuse production keys** for any C7 acceptance step.
- **Never commit a private key** to this repository, in any form (config file, script parameter default, test fixture) - `.gitignore` already excludes `*.key`/`*.conf`/`*.pem` patterns; do not override that.
- **Never include secrets in screenshots or reports** - if a screenshot of the WPF app's configuration summary is taken as evidence, confirm it shows only `ClientId`/`GatewayEndpoint`/`InterfaceAddress`/`AllowedIPs` (which is all `ConfigurationSummary` can ever contain by construction - see `docs/security-model.md`), never a raw imported `.conf` file's contents.
- **Clean up test artifacts after acceptance** - see `docs/c7-cleanup-guide.md`.
