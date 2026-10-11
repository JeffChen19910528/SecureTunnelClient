<#
.SYNOPSIS
    Builds the SecureTunnel Connect MSI from the output of deploy\package.ps1.

.DESCRIPTION
    Requires the WiX local tool restored from dotnet-tools.json and
    artifacts\<version>\{Agent,Connect} to exist (run deploy\package.ps1
    first). Performs no elevation-requiring action: it only compiles an
    MSI into installer\SecureTunnel.Installer\bin. Building the MSI does
    NOT install it - installing requires elevation and explicit operator
    action (see docs/deployment-guide.md).
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")

[xml]$props = Get-Content (Join-Path $repoRoot "Directory.Build.props")
$version = ($props.Project.PropertyGroup.Version | Select-Object -First 1).Trim()

foreach ($component in "Agent", "Connect") {
    $dir = Join-Path $repoRoot "artifacts\$version\$component"
    if (-not (Test-Path $dir)) {
        throw "Missing $dir - run deploy\package.ps1 first."
    }
}

Push-Location $repoRoot
try {
    & dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore failed." }

    & dotnet build (Join-Path $repoRoot "installer\SecureTunnel.Installer\SecureTunnel.Installer.wixproj") -c Release
    if ($LASTEXITCODE -ne 0) { throw "MSI build failed (exit code $LASTEXITCODE)." }
}
finally {
    Pop-Location
}

Get-ChildItem (Join-Path $repoRoot "installer\SecureTunnel.Installer\bin") -Recurse -Filter *.msi |
    ForEach-Object { Write-Host "Built: $($_.FullName)" }
