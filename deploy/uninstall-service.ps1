<#
.SYNOPSIS
    Uninstalls the SecureTunnel Agent Windows Service.

.DESCRIPTION
    Stops (if running) and removes the "SecureTunnelAgent" Windows
    Service using explicit cmdlet parameters - never a concatenated
    sc.exe command-line string.

    Data retention (Phase C6): by default, this script NEVER deletes
    %ProgramData%\SecureTunnel\Agent (the DPAPI-protected configuration
    store) - user configuration is preserved across an uninstall unless
    -RemoveData is explicitly passed. This matches "do not silently
    delete user configuration" and "uninstall does not delete unrelated
    files" from docs/deployment-guide.md. This script never touches any
    WireGuard tunnel, network adapter, or firewall rule - it only
    interacts with the Windows Service Control Manager and (optionally,
    with -RemoveData) its own data directory.

    THIS SCRIPT HAS NOT BEEN EXECUTED AS PART OF ANY PHASE THROUGH C6 (no
    service has ever been installed in this environment - see
    install-service.ps1's header). It requires an elevated
    (Administrator) PowerShell session.

.PARAMETER RemoveData
    If specified, also deletes %ProgramData%\SecureTunnel\Agent (all
    stored configurations) after the service is removed. Off by default
    - an uninstall alone preserves user data for a possible reinstall.

.EXAMPLE
    .\uninstall-service.ps1
.EXAMPLE
    .\uninstall-service.ps1 -RemoveData
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [switch]$RemoveData
)

$ServiceName = "SecureTunnelAgent"
$DataRoot = Join-Path $env:ProgramData "SecureTunnel\Agent"

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -eq $service) {
    Write-Host "No service named '$ServiceName' is installed. Nothing to do."
}
else {
    if ($PSCmdlet.ShouldProcess($ServiceName, "Stop and remove Windows Service")) {
        if ($service.Status -ne 'Stopped') {
            try {
                Stop-Service -Name $ServiceName -Force -ErrorAction Stop
            }
            catch {
                Write-Error "Failed to stop service '$ServiceName': $($_.Exception.Message)"
                exit 1
            }
        }

        try {
            # Remove-Service requires PowerShell 6+; sc.exe delete (still
            # with an explicit, non-concatenated argument, not a shell
            # string) is the fallback for Windows PowerShell 5.1.
            if (Get-Command Remove-Service -ErrorAction SilentlyContinue) {
                Remove-Service -Name $ServiceName -ErrorAction Stop
            }
            else {
                & sc.exe delete $ServiceName | Out-Null
                if ($LASTEXITCODE -ne 0) {
                    throw "sc.exe delete exited with code $LASTEXITCODE"
                }
            }

            Write-Host "Service '$ServiceName' removed successfully."
        }
        catch {
            Write-Error "Failed to remove service '$ServiceName': $($_.Exception.Message)"
            exit 1
        }
    }
}

if ($RemoveData) {
    if (Test-Path -LiteralPath $DataRoot) {
        if ($PSCmdlet.ShouldProcess($DataRoot, "Delete SecureTunnel Agent data directory")) {
            Remove-Item -LiteralPath $DataRoot -Recurse -Force
            Write-Host "Data directory removed: $DataRoot"
        }
    }
    else {
        Write-Host "No data directory found at $DataRoot - nothing to remove."
    }
}
else {
    Write-Host "User configuration preserved at $DataRoot (pass -RemoveData to also delete it)."
}
