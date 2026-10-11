<#
.SYNOPSIS
    Removes the SecureTunnel Agent Windows Service.

.DESCRIPTION
    Stops (if running) and removes the "SecureTunnelAgent" service. By default the stored configurations in
    %ProgramData%\SecureTunnel\Agent are kept so a reinstall does not require importing again. Only the service
    is touched: no network adapter or firewall rule is changed.

    Run from an elevated (Administrator) PowerShell.

.PARAMETER RemoveData
    Also delete %ProgramData%\SecureTunnel\Agent (all stored configurations).

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
