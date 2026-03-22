# Optional: publishes win-x64 PDF render folder (same layout as nuget.org pull + shim DLLs) for local inspection or diff.
# Runtime: the plugin downloads packages from api.nuget.org; no Release asset required.
param(
    [string]$Version = "1.1.0"
)

$ErrorActionPreference = "Stop"
# PSScriptRoot = .../plugins-src/PandocDocumentTranscoder -> repo root is two levels up
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$proj = Join-Path $repoRoot "plugins-src\PandocDocumentTranscoder.PdfRender\PandocDocumentTranscoder.PdfRender.csproj"
$out = Join-Path $repoRoot "artifacts\pdf-render-bundle-$Version"
$zip = Join-Path $repoRoot "artifacts\pdf-render-win-x64-$Version.zip"

if (-not (Test-Path $proj)) { throw "Missing project: $proj" }

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force -Path (Split-Path $zip) | Out-Null

Write-Host "== dotnet publish PdfRender (win-x64) -> $out"
dotnet publish $proj -c Release -r win-x64 --self-contained false -o $out
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip -CompressionLevel Optimal
Write-Host "OK: $zip (upload to GitHub Release v$Version as asset matching plugin download URL)"
