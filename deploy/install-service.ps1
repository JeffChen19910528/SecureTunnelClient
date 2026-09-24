<#
.SYNOPSIS
    Installs the SecureTunnel Agent as a Windows Service, and provisions
    its required data directories.

.DESCRIPTION
    Registers SecureTunnel.Client.Agent.exe as a Windows Service named
    "SecureTunnelAgent" (must match Program.cs's AddWindowsService call).
    Uses New-Service with explicit, individually-validated parameters -
    never a concatenated sc.exe command-line string. Validates the
    executable path before use: it must exist, must be an .exe file, and
    must not be a relative path or contain ".." segments, to avoid
    resolving an arbitrary or untrusted executable.

    Idempotent where practical (Phase C6): if the service is already
    installed, this script detects it, compares the installed binary's
    file version against the one being installed, and either reports
    "already up to date" (same version, same binary path - a no-op,
    success) or fails with a clear message instructing the operator to
    run uninstall-service.ps1 first for an upgrade (Phase C6 does not
    attempt an in-place binary swap of a running service, since that
    requires stopping it first and is left to a documented manual
    upgrade procedure - see docs/deployment-guide.md).

    Also creates (idempotently - Test-Path guarded) the machine-wide data
    directories the Agent needs (%ProgramData%\SecureTunnel\Agent\configs)
    with an ACL restricted to SYSTEM and Administrators only - not
    world-readable, consistent with the DPAPI-LocalMachine-protected data
    stored there (see docs/security-model.md).

    THIS SCRIPT HAS NOT BEEN EXECUTED AS PART OF ANY PHASE THROUGH C6. It
    requires an elevated (Administrator) PowerShell session, which this
    development session does not have and was not authorized to acquire.
    It has been written, reviewed, and syntax-checked for correctness
    only - installing the service is an explicit, separate, real-Windows-
    environment acceptance step for a later phase. See
    docs/windows-service-installation.md and docs/c6-packaging-report.md.

.PARAMETER ExecutablePath
    Full path to SecureTunnel.Client.Agent.exe (the published/built
    binary, e.g. produced by deploy\package.ps1). Must be an absolute
    path to an existing .exe file.

.EXAMPLE
    .\install-service.ps1 -ExecutablePath "C:\Program Files\SecureTunnel\Agent\SecureTunnel.Client.Agent.exe"
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [string]$ExecutablePath
)

$ServiceName = "SecureTunnelAgent"
$DisplayName = "SecureTunnel Agent"
$Description = "Privileged WireGuard connection engine for SecureTunnel Connect. Communicates with the SecureTunnel Connect desktop app only over a restricted local Named Pipe."
$DataRoot = Join-Path $env:ProgramData "SecureTunnel\Agent"
$ConfigsDir = Join-Path $DataRoot "configs"

function Assert-ValidExecutablePath {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "ExecutablePath must not be empty."
    }

    if (-not [System.IO.Path]::IsPathRooted($Path)) {
        throw "ExecutablePath must be an absolute path: '$Path'"
    }

    if ($Path -match '\.\.[\\/]') {
        throw "ExecutablePath must not contain '..' path segments: '$Path'"
    }

    if (-not $Path.EndsWith(".exe", [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "ExecutablePath must reference a .exe file: '$Path'"
    }

    $resolved = [System.IO.Path]::GetFullPath($Path)
    if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
        throw "ExecutablePath does not exist: '$resolved'"
    }

    return $resolved
}

function New-RestrictedDataDirectory {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }

    # Least privilege: SYSTEM (the Agent's own identity) and
    # Administrators get full control; inheritance from the parent
    # %ProgramData% ACL (which grants BUILTIN\Users read by default) is
    # explicitly removed, since this directory holds DPAPI-protected
    # private key material that must not be broadly readable.
    $acl = Get-Acl -LiteralPath $Path
    $acl.SetAccessRuleProtection($true, $false)
    $acl.Access | ForEach-Object { $acl.RemoveAccessRule($_) | Out-Null }

    $systemRule = New-Object System.Security.AccessControl.FileSystemAccessRule(
        "NT AUTHORITY\SYSTEM", "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow")
    $adminRule = New-Object System.Security.AccessControl.FileSystemAccessRule(
        "BUILTIN\Administrators", "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow")
    $acl.AddAccessRule($systemRule)
    $acl.AddAccessRule($adminRule)

    Set-Acl -LiteralPath $Path -AclObject $acl
}

$validatedPath = Assert-ValidExecutablePath -Path $ExecutablePath
$newVersion = (Get-Item -LiteralPath $validatedPath).VersionInfo.FileVersion

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -ne $existing) {
    $existingWmi = Get-CimInstance Win32_Service -Filter "Name='$ServiceName'" -ErrorAction SilentlyContinue
    $existingPath = if ($existingWmi) { $existingWmi.PathName.Trim('"') } else { $null }
    $existingVersion = if ($existingPath -and (Test-Path -LiteralPath $existingPath)) {
        (Get-Item -LiteralPath $existingPath).VersionInfo.FileVersion
    } else { $null }

    if ($existingPath -eq $validatedPath -and $existingVersion -eq $newVersion) {
        Write-Host "Service '$ServiceName' is already installed at version $newVersion with the same executable path - nothing to do (idempotent no-op)."
        exit 0
    }

    Write-Error "A service named '$ServiceName' already exists (version: $existingVersion, path: $existingPath) and differs from the requested install (version: $newVersion, path: $validatedPath). Run uninstall-service.ps1 first, then re-run this script, to upgrade."
    exit 1
}

if ($PSCmdlet.ShouldProcess($ServiceName, "Install Windows Service and provision data directories")) {
    try {
        New-RestrictedDataDirectory -Path $ConfigsDir

        New-Service `
            -Name $ServiceName `
            -DisplayName $DisplayName `
            -Description $Description `
            -BinaryPathName $validatedPath `
            -StartupType Automatic `
            -ErrorAction Stop | Out-Null

        # Explicit recovery configuration: restart on the first two
        # failures (transient crash resilience), no action on the third
        # (avoid a restart-crash loop masking a real problem) -
        # sc.exe failure is the only supported mechanism for this on
        # Windows PowerShell 5.1 (no native New-Service equivalent);
        # arguments are still explicit, non-concatenated tokens, not a
        # shell string.
        & sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/none/0 | Out-Null

        Write-Host "Service '$ServiceName' installed successfully at version $newVersion. Data directory: $ConfigsDir. The service has not been started - run 'Start-Service $ServiceName' (elevated) when ready."
    }
    catch {
        Write-Error "Failed to install service '$ServiceName': $($_.Exception.Message)"
        exit 1
    }
}
