# 在 Host 仓库根目录运行：检查契约库路径并构建 Host + plugins-src 示例插件。
# 用法（仓库根目录）：powershell -ExecutionPolicy Bypass -File .\installer\scripts\verify-dev-setup.ps1
# 依赖：已按 docs/repositories.md §8 将契约仓库克隆为 <repo>/PluginAbstractions/

$ErrorActionPreference = "Stop"
# PSScriptRoot = .../installer/scripts -> repo root is two levels up
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$contractProj = Join-Path $repoRoot "PluginAbstractions\PluginAbstractions.csproj"

if (-not (Test-Path $contractProj)) {
    Write-Host "ERROR: 未找到契约项目: $contractProj" -ForegroundColor Red
    Write-Host ""
    Write-Host "请在 Host 仓库根目录下克隆契约仓库，且目录名必须为 PluginAbstractions，例如：" -ForegroundColor Yellow
    Write-Host "  cd `"$repoRoot`""
    Write-Host "  git clone https://github.com/kckc7887/ConverTool-PluginAbstractions.git PluginAbstractions"
    Write-Host ""
    Write-Host "详见 docs/repositories.md §8"
    exit 1
}

Write-Host "== verify-dev-setup: repo root = $repoRoot"
Write-Host "== contract: $contractProj"

$projects = @(
    (Join-Path $repoRoot "Host\Host.csproj"),
    (Join-Path $repoRoot "plugins-src\FfmpegVideoTranscoder\FfmpegVideoTranscoder.csproj"),
    (Join-Path $repoRoot "plugins-src\ImageMagickImageTranscoder\ImageMagickImageTranscoder.csproj")
)

foreach ($p in $projects) {
    if (-not (Test-Path $p)) { throw "Missing project: $p" }
    Write-Host ""
    Write-Host "-- dotnet build $p"
    dotnet build $p -c Release
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Write-Host ""
Write-Host "OK: Host + plugins-src sample plugins build succeeded."
