param(
    [string]$Configuration = "Release",
    [string]$HostPluginsDir = "",
    [string]$DistDir = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$sourceRoot = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($HostPluginsDir)) {
    $HostPluginsDir = Join-Path $repoRoot "Host\bin\Debug\net8.0\plugins"
}
if ([string]::IsNullOrWhiteSpace($DistDir)) {
    $DistDir = Join-Path $repoRoot "plugins-dist"
}
$stageRoot = Join-Path $repoRoot ".tmp\plugin-build-stage"

Write-Host "== Plugin build+sync =="
Write-Host "source: $sourceRoot"
Write-Host "dist:   $DistDir"
Write-Host "host:   $HostPluginsDir"

New-Item -ItemType Directory -Force -Path $DistDir | Out-Null
New-Item -ItemType Directory -Force -Path $HostPluginsDir | Out-Null
if (Test-Path $stageRoot) { Remove-Item $stageRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stageRoot | Out-Null

$manifestPaths = Get-ChildItem $sourceRoot -Filter manifest.json -Recurse -File |
    Where-Object { $_.FullName -notmatch "\\bin\\|\\obj\\|\\out\\|\\dist\\" }

if (-not $manifestPaths -or $manifestPaths.Count -eq 0) {
    throw "No plugin manifest.json found under $sourceRoot"
}

foreach ($manifestFile in $manifestPaths) {
    $pluginDir = Split-Path -Parent $manifestFile.FullName
    $manifest = Get-Content $manifestFile.FullName -Raw | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace($manifest.pluginId)) {
        throw "manifest missing pluginId: $($manifestFile.FullName)"
    }
    if ([string]::IsNullOrWhiteSpace($manifest.assembly)) {
        throw "manifest missing assembly: $($manifestFile.FullName)"
    }

    $pluginId = [string]$manifest.pluginId
    $assemblyName = [string]$manifest.assembly

    $csproj = Get-ChildItem $pluginDir -Filter *.csproj -File | Select-Object -First 1
    if (-not $csproj) {
        throw "No csproj found for plugin $pluginId in $pluginDir"
    }

    Write-Host ""
    Write-Host "-- Building $pluginId"
    dotnet build $csproj.FullName -c $Configuration | Out-Host

    $assemblyFile = Get-ChildItem (Join-Path $pluginDir "bin\$Configuration") -Filter $assemblyName -Recurse -File |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if (-not $assemblyFile) {
        throw "Assembly not found after build: $assemblyName ($pluginId)"
    }

    $buildOutDir = Split-Path -Parent $assemblyFile.FullName
    $stagePluginDir = Join-Path $stageRoot $pluginId
    if (Test-Path $stagePluginDir) { Remove-Item $stagePluginDir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $stagePluginDir | Out-Null

    Copy-Item $manifestFile.FullName $stagePluginDir -Force
    if (Test-Path (Join-Path $pluginDir "locales")) {
        Copy-Item (Join-Path $pluginDir "locales") $stagePluginDir -Recurse -Force
    }

    Get-ChildItem $buildOutDir -File | ForEach-Object {
        Copy-Item $_.FullName $stagePluginDir -Force
    }

    $hostPluginDir = Join-Path $HostPluginsDir $pluginId
    if (Test-Path $hostPluginDir) { Remove-Item $hostPluginDir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $hostPluginDir | Out-Null
    Copy-Item (Join-Path $stagePluginDir "*") $hostPluginDir -Recurse -Force

    Get-ChildItem $DistDir -Filter "$pluginId*.zip" -File -ErrorAction SilentlyContinue | Remove-Item -Force
    $zipPath = Join-Path $DistDir "$pluginId.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path (Join-Path $stagePluginDir "*") -DestinationPath $zipPath -CompressionLevel Optimal

    Write-Host "   synced => $hostPluginDir"
    Write-Host "   zipped => $zipPath"
}

Write-Host ""
Write-Host "Done."
