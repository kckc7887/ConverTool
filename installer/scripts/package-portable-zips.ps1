# After installer/scripts/build-installer.ps1, create portable zips under artifacts/ with release naming.
param(
    [Parameter(Mandatory = $false)]
    [string]$Version = "1.2.1"
)

$ErrorActionPreference = "Stop"
# PSScriptRoot = .../installer/scripts -> repo root is two levels up
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$fullDir = Join-Path $repoRoot "artifacts\host\v$Version\win-x64"
$liteDir = Join-Path $repoRoot "artifacts\host\v$Version\win-x64-lite"
$outDir = Join-Path $repoRoot "artifacts"

if (-not (Test-Path $fullDir)) { throw "Missing full payload: $fullDir (run build-installer.ps1 first)" }
if (-not (Test-Path $liteDir)) { throw "Missing lite payload: $liteDir (run build-installer.ps1 first)" }

$fullZip = Join-Path $outDir "ConverTool-v$Version-win-x64-full.zip"
$liteZip = Join-Path $outDir "ConverTool-v$Version-win-x64-lite.zip"

if (Test-Path $fullZip) { Remove-Item -Force $fullZip }
if (Test-Path $liteZip) { Remove-Item -Force $liteZip }

# Zip directory contents so extract yields ConverTool.exe at archive root (same layout as publish folder).
Compress-Archive -Path (Join-Path $fullDir '*') -DestinationPath $fullZip -CompressionLevel Optimal
Compress-Archive -Path (Join-Path $liteDir '*') -DestinationPath $liteZip -CompressionLevel Optimal

Write-Host "OK: $fullZip"
Write-Host "OK: $liteZip"
