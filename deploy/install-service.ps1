<#
.SYNOPSIS
    Installs the SecureTunnel Agent as a Windows Service.

.DESCRIPTION
    Registers SecureTunnel.Client.Agent.exe as the "SecureTunnelAgent" service (automatic start, restart on the
    first two failures) and creates %ProgramData%\SecureTunnel\Agent\configs, restricted to SYSTEM and
    Administrators because it holds the encrypted tunnel configuration. The service is not started by this
    script: run "Start-Service SecureTunnelAgent" afterwards.

    Run from an elevated (Administrator) PowerShell. Running it again with the same executable is a no-op;
    to upgrade, run uninstall-service.ps1 first.

.PARAMETER ExecutablePath
    Absolute path to SecureTunnel.Client.Agent.exe (a folder produced by deploy\package.ps1, or an install folder).
    wireguard.dll must sit in the same folder.

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
