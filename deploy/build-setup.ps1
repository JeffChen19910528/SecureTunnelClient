<#
.SYNOPSIS
    Builds the single-file installer SecureTunnelConnect-Setup-<version>-win-x64.exe.

.DESCRIPTION
    Run deploy\package.ps1 first. This zips artifacts\<version>\{Agent,Connect}, embeds the zip into
    installer\SecureTunnel.Setup and publishes it as one self-contained executable. Only the .NET SDK is needed:
    no WiX or other installer tool. Building needs no elevation; running the installer does (it asks for it).

    Setup usage:  SecureTunnelConnect-Setup-<version>-win-x64.exe [/silent]
                  SecureTunnelConnect-Setup-<version>-win-x64.exe /uninstall [/removedata] [/silent]
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

[xml]$props = Get-Content (Join-Path $repoRoot "Directory.Build.props")
$version = ($props.Project.PropertyGroup.Version | Select-Object -First 1).Trim()
$artifacts = Join-Path $repoRoot "artifacts\$version"

foreach ($component in "Agent", "Connect") {
    if (-not (Test-Path (Join-Path $artifacts $component))) {
        throw "Missing $artifacts\$component - run deploy\package.ps1 first."
    }
}

# One zip with Agent\ and Connect\ at its root.
$payload = Join-Path $artifacts "payload.zip"
if (Test-Path $payload) { Remove-Item -LiteralPath $payload -Force }
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::Open($payload, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($component in "Agent", "Connect") {
        $base = Join-Path $artifacts $component
        Get-ChildItem -LiteralPath $base -Recurse -File | Where-Object { $_.Extension -ne ".pdb" } | ForEach-Object {
            $entryName = ($component + "/" + $_.FullName.Substring($base.Length + 1)).Replace("\", "/")
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $_.FullName, $entryName, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
}
finally {
    $zip.Dispose()
}

$publishDir = Join-Path $artifacts "setup"
if (Test-Path $publishDir) { Remove-Item -Recurse -Force -LiteralPath $publishDir }

& dotnet publish (Join-Path $repoRoot "installer\SecureTunnel.Setup\SecureTunnel.Setup.csproj") `
    -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    "-p:PayloadZip=$payload" -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "Setup build failed (exit code $LASTEXITCODE)." }

$out = Join-Path $artifacts "SecureTunnelConnect-Setup-$version-win-x64.exe"
Copy-Item -LiteralPath (Join-Path $publishDir "SecureTunnelSetup.exe") -Destination $out -Force
$hash = (Get-FileHash -LiteralPath $out -Algorithm SHA256).Hash
"$hash  $(Split-Path -Leaf $out)" | Out-File -FilePath "$out.sha256" -Encoding ascii
Remove-Item -LiteralPath $payload -Force

Write-Host "Built: $out"
Write-Host "SHA256: $hash"
