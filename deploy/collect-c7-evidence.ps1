<#
.SYNOPSIS
    Read-only acceptance-evidence collection helpers for a Phase C7
    rerun. Every function here is explicitly scoped, read-only by
    default, safe to repeat, and sanitized via
    deploy/C7EnvironmentChecks.psm1's Protect-C7SensitiveText.

.DESCRIPTION
    Implements the "Controlled Test Harness Preparation" items from
    Phase C7-ENV Part D:
      1. Service status collection      -> Get-C7ServiceStatusEvidence
      2. Service identity collection    -> Get-C7ServiceIdentityEvidence
      3. Installation directory check   -> Get-C7InstallationDirectoryEvidence
      4. ACL inspection                 -> Get-C7DataDirectoryAclEvidence
      5. Named Pipe connectivity check  -> Get-C7NamedPipeConnectivityEvidence
      6. WireGuard executable discovery -> Get-C7WireGuardDiscoveryEvidence
      7. WireGuard interface inventory  -> Get-C7WireGuardInterfaceEvidence
      8. Sanitized diagnostic collection-> Get-C7SanitizedDiagnosticSummary
      9. Test evidence collection       -> Save-C7Evidence (writes all of
                                            the above, sanitized, to a
                                            timestamped report file)

    Deliberately NOT implemented (and never will be, per Phase C7-ENV
    Scope Restrictions #8/#9 and the product's own "no generic command
    execution" rule, unchanged since Phase C1): there is no function here
    that starts, stops, installs, uninstalls, or otherwise mutates
    anything. Every "collection" function is a read-only query. There is
    no WPF-callable equivalent of any of this and there will not be one -
    the WPF client's only privileged channel remains the four-operation
    Named Pipe contract documented in docs/ipc-protocol.md.

.EXAMPLE
    Import-Module .\deploy\C7EnvironmentChecks.psm1 -Force
    . .\deploy\collect-c7-evidence.ps1
    Save-C7Evidence -OutputPath ".\artifacts\c7-evidence"
#>

Set-StrictMode -Version Latest

$__c7EvidenceScriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
Import-Module (Join-Path $__c7EvidenceScriptDir "C7EnvironmentChecks.psm1") -Force

function Get-C7ServiceStatusEvidence {
    [CmdletBinding()]
    param([string]$ServiceName = "SecureTunnelAgent")

    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($null -eq $service) {
        return [PSCustomObject]@{ Found = $false; Status = $null; StartType = $null }
    }
    return [PSCustomObject]@{ Found = $true; Status = $service.Status.ToString(); StartType = $service.StartType.ToString() }
}

function Get-C7ServiceIdentityEvidence {
    [CmdletBinding()]
    param([string]$ServiceName = "SecureTunnelAgent")

    $wmiService = Get-CimInstance Win32_Service -Filter "Name='$ServiceName'" -ErrorAction SilentlyContinue
    if ($null -eq $wmiService) {
        return [PSCustomObject]@{ Found = $false; StartName = $null; PathName = $null }
    }
    return [PSCustomObject]@{
        Found     = $true
        StartName = $wmiService.StartName
        # PathName is a filesystem path, not a secret, but is passed
        # through redaction anyway for defense in depth against an
        # unexpected embedded credential in a service path (would never
        # happen for this product's own service, but this helper may be
        # reused/adapted).
        PathName  = (Protect-C7SensitiveText -Text $wmiService.PathName)
    }
}

function Get-C7InstallationDirectoryEvidence {
    [CmdletBinding()]
    param(
        [string]$AgentExecutablePath = "C:\Program Files\SecureTunnel\Agent\SecureTunnel.Client.Agent.exe",
        [string]$ConnectExecutablePath = "C:\Program Files\SecureTunnel\Connect\SecureTunnel.Connect.exe"
    )

    return [PSCustomObject]@{
        AgentExecutableExists   = Test-Path -LiteralPath $AgentExecutablePath
        ConnectExecutableExists = Test-Path -LiteralPath $ConnectExecutablePath
    }
}

function Get-C7DataDirectoryAclEvidence {
    [CmdletBinding()]
    param([string]$DataDirectory = (Join-Path $env:ProgramData "SecureTunnel\Agent\configs"))

    if (-not (Test-Path -LiteralPath $DataDirectory)) {
        return [PSCustomObject]@{ Exists = $false; AccessRules = @() }
    }

    $acl = Get-Acl -LiteralPath $DataDirectory
    $rules = $acl.Access | ForEach-Object {
        [PSCustomObject]@{
            Identity = $_.IdentityReference.Value
            Rights   = $_.FileSystemRights.ToString()
            Type     = $_.AccessControlType.ToString()
        }
    }
    return [PSCustomObject]@{ Exists = $true; AccessRules = @($rules) }
}

function Get-C7NamedPipeConnectivityEvidence {
    [CmdletBinding()]
    param(
        [string]$PipeName = "SecureTunnelClientAgent.v1",
        [int]$TimeoutMs = 2000
    )

    try {
        $client = New-Object System.IO.Pipes.NamedPipeClientStream(".", $PipeName, [System.IO.Pipes.PipeDirection]::InOut)
        try {
            $client.Connect($TimeoutMs)
            return [PSCustomObject]@{ Reachable = $true; Error = $null }
        }
        finally {
            $client.Dispose()
        }
    }
    catch [TimeoutException] {
        return [PSCustomObject]@{ Reachable = $false; Error = "Timeout - no listener (service not running or ServiceUnavailable)" }
    }
    catch {
        return [PSCustomObject]@{ Reachable = $false; Error = (Protect-C7SensitiveText -Text $_.Exception.Message) }
    }
}

function Get-C7WireGuardDiscoveryEvidence {
    [CmdletBinding()]
    param()

    $onPath = $null -ne (Get-Command wireguard.exe -ErrorAction SilentlyContinue)
    $defaultPathExists = Test-Path -LiteralPath "C:\Program Files\WireGuard\wireguard.exe"
    return [PSCustomObject]@{ DiscoverableOnPath = $onPath; DefaultInstallPathExists = $defaultPathExists }
}

function Get-C7WireGuardInterfaceEvidence {
    [CmdletBinding()]
    param()

    $adapters = @(Get-NetAdapter -ErrorAction SilentlyContinue | Where-Object { $_.InterfaceDescription -match "WireGuard" })
    $names = if ($adapters.Count -gt 0) { @($adapters | ForEach-Object { $_.Name }) } else { @() }
    return [PSCustomObject]@{ Count = $adapters.Count; Names = $names }
}

function Get-C7SanitizedDiagnosticSummary {
    [CmdletBinding()]
    param()

    return [PSCustomObject]@{
        TimestampUtc     = (Get-Date).ToUniversalTime().ToString("o")
        ServiceStatus    = Get-C7ServiceStatusEvidence
        ServiceIdentity  = Get-C7ServiceIdentityEvidence
        InstallationDirs = Get-C7InstallationDirectoryEvidence
        DataDirectoryAcl = Get-C7DataDirectoryAclEvidence
        NamedPipe        = Get-C7NamedPipeConnectivityEvidence
        WireGuard        = Get-C7WireGuardDiscoveryEvidence
        WireGuardIfaces  = Get-C7WireGuardInterfaceEvidence
    }
}

function Save-C7Evidence {
    [CmdletBinding()]
    param(
        [string]$OutputPath = (Join-Path (Split-Path -Parent $__c7EvidenceScriptDir) "artifacts\c7-evidence")
    )

    if (-not (Test-Path -LiteralPath $OutputPath)) {
        New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
    }

    $summary = Get-C7SanitizedDiagnosticSummary
    $timestamp = (Get-Date).ToUniversalTime().ToString("yyyyMMdd-HHmmss")
    $reportPath = Join-Path $OutputPath "c7-evidence-$timestamp.json"

    $summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $reportPath -Encoding utf8

    Write-Host "Evidence written to: $reportPath"
    return $reportPath
}
