<#
.SYNOPSIS
    Non-destructive readiness probe for Phase C7 (real Windows/WireGuard
    acceptance). Inspects and reports; never installs, modifies, or
    deletes anything.

.DESCRIPTION
    Checks the 15 requirement areas from docs/c7-environment-readiness.md
    and prints a table with the required status values (READY / NOT
    READY / BLOCKED / NOT APPLICABLE / NOT CHECKED). Exits non-zero if
    any mandatory prerequisite for a real C7 rerun is missing, so it can
    be used as a CI/operator gate.

    Safe to run repeatedly: every check here is read-only (Get-*,
    Test-Path, Get-Command, Get-Service, Get-CimInstance, etc.) - nothing
    is created, started, stopped, installed, or deleted. Uses
    deploy/C7EnvironmentChecks.psm1's Get-C7CheckStatus/Protect-C7SensitiveText
    so the classification and redaction logic is exactly what the Pester
    tests in deploy/tests/C7EnvironmentChecks.Tests.ps1 cover.

.PARAMETER TestDataDirectory
    Directory to probe for "is a writable test-data location available"
    (item 15). Defaults to a subfolder under the current user's temp
    directory - never a production data path.

.EXAMPLE
    .\deploy\check-c7-environment.ps1
#>
[CmdletBinding()]
param(
    [string]$TestDataDirectory = (Join-Path $env:TEMP "SecureTunnelClient-C7-Probe")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
Import-Module (Join-Path $scriptDir "C7EnvironmentChecks.psm1") -Force

$results = New-Object System.Collections.Generic.List[PSCustomObject]

function Get-Outcome {
    # PowerShell 5.1 has no ternary operator - this replaces `$b ? 'Succeeded' : 'Failed'`.
    param([bool]$Condition)
    if ($Condition) { return 'Succeeded' } else { return 'Failed' }
}

function Add-Result {
    param(
        [string]$Requirement,
        [string]$DetectionMethod,
        [string]$Expected,
        [string]$Actual,
        [string]$Status,
        [string]$Remediation,
        [string]$SecurityImpact
    )
    $results.Add([PSCustomObject]@{
        Requirement     = $Requirement
        DetectionMethod = $DetectionMethod
        Expected        = $Expected
        Actual          = (Protect-C7SensitiveText -Text $Actual)
        Status          = $Status
        Remediation     = $Remediation
        SecurityImpact  = $SecurityImpact
    }) | Out-Null
}

# 1. Windows version and architecture
try {
    $os = Get-CimInstance Win32_OperatingSystem -ErrorAction Stop
    $arch = $os.OSArchitecture
    $ok = $os.Caption -match "Windows (10|11)" -and $arch -match "64"
    Add-Result "Windows version/architecture" "Get-CimInstance Win32_OperatingSystem" "Windows 10/11, x64" "$($os.Caption) $($os.Version) $arch" `
        (Get-C7CheckStatus -Applicable $true -Outcome (Get-Outcome $ok)) `
        "Use a supported Windows 10/11 x64 machine." "None - informational."
} catch {
    Add-Result "Windows version/architecture" "Get-CimInstance Win32_OperatingSystem" "Windows 10/11, x64" "error" (Get-C7CheckStatus -Applicable $true -Outcome 'Blocked' -BlockedReason "WMI/CIM query failed: $($_.Exception.Message)") "N/A" "None."
}

# 2. Current user identity
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
Add-Result "Current user identity" "[Security.Principal.WindowsIdentity]::GetCurrent()" "A resolvable local/domain identity" $identity.Name `
    (Get-C7CheckStatus -Applicable $true -Outcome 'Succeeded') "None needed." "Identity name only - no credential captured."

# 3. Current privilege/elevation state
$elevated = Test-C7ProcessElevated
Add-Result "Elevation state" "Test-C7ProcessElevated (WindowsPrincipal.IsInRole(Administrator))" "Elevated (True) for C7-A install/service steps" "$elevated" `
    (Get-C7CheckStatus -Applicable $true -Outcome (Get-Outcome $elevated)) `
    "Relaunch PowerShell with 'Run as administrator'." "Elevation state alone reveals no secret."

# 4. Administrator group membership (distinct from elevation - a user
# can be a deny-only Administrators member, as this development machine
# is; see the fix note in C7EnvironmentChecks.psm1)
try {
    $isMember = Test-C7AdministratorGroupMember
    Add-Result "Administrator group membership" "Test-C7AdministratorGroupMember (Get-LocalGroupMember)" "Member of BUILTIN\Administrators (informational - distinct from elevation)" "$isMember" `
        (Get-C7CheckStatus -Applicable $true -Outcome 'Succeeded') "N/A - informational only." "None."
} catch {
    Add-Result "Administrator group membership" "Test-C7AdministratorGroupMember (Get-LocalGroupMember)" "Member of BUILTIN\Administrators" "error" `
        (Get-C7CheckStatus -Applicable $true -Outcome 'Blocked' -BlockedReason $_.Exception.Message) "Ensure the LocalAccounts PowerShell module is available." "None."
}

# 5. .NET SDK and runtime versions
try {
    $sdks = & dotnet --list-sdks 2>&1
    $runtimes = & dotnet --list-runtimes 2>&1
    $hasNet10Sdk = ($sdks -join "`n") -match "10\."
    $hasDesktopRuntime = ($runtimes -join "`n") -match "Microsoft\.WindowsDesktop\.App 10\."
    $ok = $hasNet10Sdk -and $hasDesktopRuntime
    Add-Result ".NET 10 SDK + Desktop Runtime" "dotnet --list-sdks / --list-runtimes" ".NET 10 SDK and Microsoft.WindowsDesktop.App 10.x present" "SDK10=$hasNet10Sdk DesktopRuntime10=$hasDesktopRuntime" `
        (Get-C7CheckStatus -Applicable $true -Outcome (Get-Outcome $ok)) "Install the .NET 10 SDK (includes the Desktop Runtime)." "None."
} catch {
    Add-Result ".NET 10 SDK + Desktop Runtime" "dotnet --list-sdks" "present" "error" (Get-C7CheckStatus -Applicable $true -Outcome 'Blocked' -BlockedReason "dotnet CLI not found or failed: $($_.Exception.Message)") "Install .NET 10 SDK." "None."
}

# 6. WireGuard for Windows installation status
$wgPath = "C:\Program Files\WireGuard\wireguard.exe"
$wgInstalled = Test-Path -LiteralPath $wgPath
Add-Result "WireGuard for Windows installed" "Test-Path 'C:\Program Files\WireGuard\wireguard.exe'" "Present" "$wgInstalled" `
    (Get-C7CheckStatus -Applicable $true -Outcome (Get-Outcome $wgInstalled)) `
    "Download and install WireGuard for Windows from the official source (operator-verified, not auto-installed by this script - see docs/c7-elevated-setup-guide.md)." "Do not auto-install from an unaudited source (Scope Restriction #10)."

# 7. WireGuard executable discovery (PATH-based, mirrors WireGuardProcessRunner.IsExecutableAvailable)
$wgOnPath = $null -ne (Get-Command wireguard.exe -ErrorAction SilentlyContinue)
Add-Result "WireGuard executable discoverable on PATH" "Get-Command wireguard.exe" "Found" "$wgOnPath" `
    (Get-C7CheckStatus -Applicable $true -Outcome (Get-Outcome $wgOnPath)) `
    "Ensure the WireGuard install directory is on PATH, or the Agent's configured executable name resolves." "None."

# 8. Windows Service management capability
$canQueryServices = $true
try { Get-Service -Name "Spooler" -ErrorAction Stop | Out-Null } catch { $canQueryServices = $false }
Add-Result "Windows Service management capability (query)" "Get-Service (any well-known service)" "Query succeeds" "$canQueryServices" `
    (Get-C7CheckStatus -Applicable $true -Outcome (Get-Outcome $canQueryServices)) "N/A - querying rarely requires elevation." "None."

$existingService = Get-Service -Name "SecureTunnelAgent" -ErrorAction SilentlyContinue
Add-Result "SecureTunnelAgent service currently installed" "Get-Service -Name SecureTunnelAgent" "Not present (clean baseline) or present+known-version" $(if ($existingService) { $existingService.Status } else { "Not installed" }) `
    (Get-C7CheckStatus -Applicable $true -Outcome 'Succeeded') "N/A - informational." "None."

# 9. Windows Event Log access
try {
    Get-WinEvent -ListLog "Application" -ErrorAction Stop | Out-Null
    Add-Result "Windows Event Log access" "Get-WinEvent -ListLog Application" "Accessible" "True" (Get-C7CheckStatus -Applicable $true -Outcome 'Succeeded') "N/A" "None."
} catch {
    Add-Result "Windows Event Log access" "Get-WinEvent -ListLog Application" "Accessible" "error" (Get-C7CheckStatus -Applicable $true -Outcome 'Blocked' -BlockedReason $_.Exception.Message) "Check Event Log service is running." "None."
}

# 10. Named Pipe capability (generic OS capability check - not the product's real pipe, which requires the Agent running)
try {
    $testPipeName = "SecureTunnelClient-C7-Probe-$([Guid]::NewGuid().ToString('N'))"
    $server = New-Object System.IO.Pipes.NamedPipeServerStream($testPipeName, [System.IO.Pipes.PipeDirection]::InOut, 1)
    $server.Dispose()
    Add-Result "Named Pipe capability (OS-level, not the product's real pipe)" "Create+dispose a disposable NamedPipeServerStream" "Succeeds" "True" (Get-C7CheckStatus -Applicable $true -Outcome 'Succeeded') "N/A" "None - disposable, uniquely-named probe pipe only."
} catch {
    Add-Result "Named Pipe capability" "NamedPipeServerStream probe" "Succeeds" "error" (Get-C7CheckStatus -Applicable $true -Outcome 'Blocked' -BlockedReason $_.Exception.Message) "Investigate local Named Pipe restrictions/policy." "None."
}

# 11. Available disk space
try {
    $drive = Get-PSDrive -Name ($env:SystemDrive.TrimEnd(':')) -ErrorAction Stop
    $freeGb = [math]::Round($drive.Free / 1GB, 1)
    $ok = $freeGb -ge 1
    Add-Result "Available disk space" "Get-PSDrive $($env:SystemDrive)" ">= 1 GB free" "$freeGb GB free" (Get-C7CheckStatus -Applicable $true -Outcome (Get-Outcome $ok)) "Free up disk space." "None."
} catch {
    Add-Result "Available disk space" "Get-PSDrive" ">= 1 GB free" "error" (Get-C7CheckStatus -Applicable $true -Outcome 'Blocked' -BlockedReason $_.Exception.Message) "N/A" "None."
}

# 12. Network adapter visibility
try {
    $adapterCount = (Get-NetAdapter -ErrorAction Stop | Measure-Object).Count
    Add-Result "Network adapter visibility" "Get-NetAdapter" ">= 1 adapter visible" "$adapterCount adapter(s)" (Get-C7CheckStatus -Applicable $true -Outcome (Get-Outcome ($adapterCount -ge 1))) "N/A" "None - adapter names/descriptions only, no addressing captured."
} catch {
    Add-Result "Network adapter visibility" "Get-NetAdapter" ">= 1 adapter visible" "error" (Get-C7CheckStatus -Applicable $true -Outcome 'Blocked' -BlockedReason $_.Exception.Message) "N/A" "None."
}

# 13. Existing WireGuard interfaces
try {
    $wgAdapters = @(Get-NetAdapter -ErrorAction Stop | Where-Object { $_.InterfaceDescription -match "WireGuard" })
    Add-Result "Existing WireGuard interfaces" "Get-NetAdapter | Where InterfaceDescription -match WireGuard" "0 (clean baseline) before a real C7 test tunnel is created" "$($wgAdapters.Count) found" (Get-C7CheckStatus -Applicable $true -Outcome 'Succeeded') "N/A - informational; a nonzero count means a prior tunnel wasn't cleaned up (see docs/c7-cleanup-guide.md)." "None - adapter names only."
} catch {
    Add-Result "Existing WireGuard interfaces" "Get-NetAdapter" "0" "error" (Get-C7CheckStatus -Applicable $true -Outcome 'Blocked' -BlockedReason $_.Exception.Message) "N/A" "None."
}

# 14. Existing SecureTunnel services (beyond the one already checked in #8 - broader name search)
try {
    $stServices = @(Get-Service -ErrorAction Stop | Where-Object { $_.Name -match "SecureTunnel" })
    # Under Set-StrictMode -Version Latest, accessing a member (e.g.
    # .Name) via collection-enumeration on an EMPTY array throws
    # PropertyNotFoundException, even though the same access on a
    # non-empty array works fine - a real defect found by actually
    # running this script (it reproduces identically outside the script
    # too), not by inspection. Guard explicitly for the empty case.
    $stNames = if ($stServices.Count -gt 0) { $stServices.Name -join ', ' } else { '(none)' }
    Add-Result "Existing SecureTunnel* services (broad search)" "Get-Service | Where Name -match SecureTunnel" "Only SecureTunnelAgent, or none" "$($stServices.Count) found: $stNames" (Get-C7CheckStatus -Applicable $true -Outcome 'Succeeded') "N/A - informational." "None - service names only."
} catch {
    Add-Result "Existing SecureTunnel* services" "Get-Service" "clean" "error" (Get-C7CheckStatus -Applicable $true -Outcome 'Blocked' -BlockedReason $_.Exception.Message) "N/A" "None."
}

# 15. Test data directory availability
try {
    if (-not (Test-Path -LiteralPath $TestDataDirectory)) {
        New-Item -ItemType Directory -Path $TestDataDirectory -Force -ErrorAction Stop | Out-Null
    }
    $writable = $true
    $probeFile = Join-Path $TestDataDirectory "write-probe.tmp"
    Set-Content -LiteralPath $probeFile -Value "probe" -ErrorAction Stop
    Remove-Item -LiteralPath $probeFile -Force -ErrorAction Stop
    Add-Result "Test data directory availability" "New-Item + write/delete probe under `$env:TEMP" "Writable" "True ($TestDataDirectory)" (Get-C7CheckStatus -Applicable $true -Outcome 'Succeeded') "N/A" "Test-only, non-production temp location; contains no product data unless a later step writes there."
} catch {
    Add-Result "Test data directory availability" "New-Item + write probe" "Writable" "error" (Get-C7CheckStatus -Applicable $true -Outcome 'Blocked' -BlockedReason $_.Exception.Message) "Choose a writable temp location." "None."
}

$results | Format-Table -Property Requirement, Status, Actual -AutoSize | Out-String -Width 200 | Write-Host

$mandatoryForC7A = @("Elevation state")
$mandatoryForC7B = @("WireGuard for Windows installed", "WireGuard executable discoverable on PATH")

# @(...) forces an array even when Where-Object matches 0 or 1 items -
# without it, a single match unwraps to a scalar with no .Count property
# under Set-StrictMode (the same class of real defect fixed in
# C7EnvironmentChecks.psm1's Test-C7AdministratorGroupMember - found by
# actually running this script, not by inspection).
$notReadyMandatory = @($results | Where-Object { ($_.Requirement -in $mandatoryForC7A + $mandatoryForC7B) -and $_.Status -ne "READY" })

Write-Host ""
Write-Host "=== Summary ==="
Write-Host "Total checks:      $($results.Count)"
Write-Host "READY:             $((@($results | Where-Object Status -eq 'READY')).Count)"
Write-Host "NOT READY:         $((@($results | Where-Object Status -eq 'NOT READY')).Count)"
Write-Host "BLOCKED:           $((@($results | Where-Object Status -eq 'BLOCKED')).Count)"
Write-Host "NOT APPLICABLE:    $((@($results | Where-Object Status -eq 'NOT APPLICABLE')).Count)"
Write-Host "NOT CHECKED:       $((@($results | Where-Object Status -eq 'NOT CHECKED')).Count)"

if ($notReadyMandatory.Count -gt 0) {
    Write-Host ""
    Write-Host "Mandatory-for-C7 prerequisites NOT READY:" -ForegroundColor Yellow
    $notReadyMandatory | ForEach-Object { Write-Host "  - $($_.Requirement): $($_.Status)" -ForegroundColor Yellow }
    exit 1
}

Write-Host ""
Write-Host "All mandatory-for-C7 prerequisites are READY." -ForegroundColor Green
exit 0
