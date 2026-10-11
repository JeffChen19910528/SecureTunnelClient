<#
.SYNOPSIS
    Builds SecureTunnel Connect (Agent + WPF client) into versioned, checksummed folders and zips.

.DESCRIPTION
    Publishes SecureTunnel.Client.Agent and SecureTunnel.Connect (Release, win-x64, self-contained: the target PC
    needs no .NET runtime), writes artifacts\<version>\{Agent,Connect}, a SHA256 file and one zip per
    component, and checks the binaries for build-machine paths. It needs no elevation and can be re-run.

.PARAMETER OutputRoot
    Where versioned output is written. Defaults to <repo>\artifacts.

.EXAMPLE
    .\deploy\package.ps1
#>
[CmdletBinding()]
param(
    [string]$OutputRoot
)

$ErrorActionPreference = "Stop"

$scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$repoRoot = Resolve-Path (Join-Path $scriptDir "..")

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "artifacts"
}

function Get-ProjectVersion {
    $propsPath = Join-Path $repoRoot "Directory.Build.props"
    if (-not (Test-Path $propsPath)) {
        return "0.0.0-unknown"
    }

    [xml]$props = Get-Content $propsPath
    $version = $props.Project.PropertyGroup.Version | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($version)) {
        return "0.0.0-unknown"
    }

    return $version.Trim()
}

function Publish-Component {
    param(
        [string]$ProjectPath,
        [string]$ComponentName,
        [string]$DestinationDir
    )

    Write-Host "Publishing $ComponentName..."
    if (Test-Path $DestinationDir) {
        Remove-Item -Recurse -Force -LiteralPath $DestinationDir
    }

    & dotnet publish $ProjectPath -c Release -r win-x64 --self-contained true -o $DestinationDir
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $ComponentName (exit code $LASTEXITCODE)."
    }

    # Strip development-only artifacts: .pdb files are useful for crash
    # diagnostics but are excluded from the distributable zip below (kept
    # in the raw publish output for local inspection only). No test
    # assemblies are ever produced by `dotnet publish` on a non-test
    # project, so there is nothing test-related to exclude here.
}

function Get-Sha256 {
    param([string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

$version = Get-ProjectVersion
$versionedRoot = Join-Path $OutputRoot $version

Write-Host "=== SecureTunnel Connect packaging ==="
Write-Host "Version: $version"
Write-Host "Output:  $versionedRoot"

if (Test-Path $versionedRoot) {
    Remove-Item -Recurse -Force -LiteralPath $versionedRoot
}
New-Item -ItemType Directory -Path $versionedRoot -Force | Out-Null

$agentDir = Join-Path $versionedRoot "Agent"
$connectDir = Join-Path $versionedRoot "Connect"

Publish-Component -ProjectPath (Join-Path $repoRoot "src\SecureTunnel.Client.Agent\SecureTunnel.Client.Agent.csproj") -ComponentName "SecureTunnel.Client.Agent" -DestinationDir $agentDir
Publish-Component -ProjectPath (Join-Path $repoRoot "src\SecureTunnel.Connect\SecureTunnel.Connect.csproj") -ComponentName "SecureTunnel.Connect" -DestinationDir $connectDir

# Confirm no build-machine absolute path made it into the published output.
Write-Host "Running packaging security checks..."
$devPathPattern = [regex]::Escape($repoRoot.Path)
$keyShapedPattern = "[A-Za-z0-9+/]{43}="
$securityIssues = @()

Get-ChildItem -Path $versionedRoot -Recurse -Include *.dll, *.exe | ForEach-Object {
    $bytes = [System.IO.File]::ReadAllBytes($_.FullName)
    $text = [System.Text.Encoding]::ASCII.GetString($bytes)
    if ($text -match $devPathPattern) {
        $securityIssues += "Development-only path found in $($_.Name)"
    }
}

if ($securityIssues.Count -gt 0) {
    $securityIssues | ForEach-Object { Write-Error $_ }
    throw "Packaging security check failed - see errors above. Artifacts were NOT zipped."
}
Write-Host "Packaging security checks passed: no development-only paths found in published binaries."

# Zip + checksum each component (excludes .pdb - distributable artifact
# only; .pdb files remain in the raw publish directory above for local
# crash-dump symbol resolution, never shipped to end users).
$manifestLines = @()
$manifestLines += "SecureTunnel Connect packaging manifest"
$manifestLines += "Version: $version"
$manifestLines += "Built: $(Get-Date -Format 'u')"
$manifestLines += ""

foreach ($component in @(
    @{ Name = "SecureTunnel.Client.Agent"; Dir = $agentDir },
    @{ Name = "SecureTunnel.Connect"; Dir = $connectDir }
)) {
    $zipPath = Join-Path $versionedRoot "$($component.Name)-$version-win-x64.zip"
    $filesToZip = Get-ChildItem -Path $component.Dir -Recurse -File | Where-Object { $_.Extension -ne ".pdb" }

    Compress-Archive -Path ($filesToZip | Select-Object -ExpandProperty FullName) -DestinationPath $zipPath -Force

    $checksum = Get-Sha256 -Path $zipPath
    "$checksum  $(Split-Path -Leaf $zipPath)" | Out-File -FilePath "$zipPath.sha256" -Encoding ascii

    $manifestLines += "$($component.Name):"
    $manifestLines += "  Archive:  $(Split-Path -Leaf $zipPath)"
    $manifestLines += "  SHA256:   $checksum"
    $manifestLines += "  Files:    $($filesToZip.Count)"
    $manifestLines += ""
}

$manifestPath = Join-Path $versionedRoot "MANIFEST.txt"
$manifestLines | Out-File -FilePath $manifestPath -Encoding ascii

Write-Host ""
Write-Host "=== Packaging complete ==="
Write-Host "Artifacts: $versionedRoot"
Get-Content $manifestPath
