param(
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$hostProject = Join-Path $repoRoot "Host\Host.csproj"
$fullOut = Join-Path $repoRoot "artifacts\host\v$Version\win-x64"
$liteOut = Join-Path $repoRoot "artifacts\host\v$Version\win-x64-lite"
$issPath = Join-Path $repoRoot "installer\ConverTool.iss"

function Resolve-IsccPath {
    $candidates = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
    )
    foreach ($p in $candidates) {
        if (Test-Path $p) { return $p }
    }

    $cmd = Get-Command iscc.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    return $null
}

Write-Host "== Build full/lite payloads =="
if (Test-Path $fullOut) { Remove-Item -Recurse -Force $fullOut }
if (Test-Path $liteOut) { Remove-Item -Recurse -Force $liteOut }

dotnet publish $hostProject -c Release -r win-x64 --self-contained true -o $fullOut
dotnet publish $hostProject -c Release -r win-x64 --self-contained false -o $liteOut

foreach ($d in @($fullOut, $liteOut)) {
    if (Test-Path (Join-Path $d "Host.exe")) {
        Copy-Item (Join-Path $d "Host.exe") (Join-Path $d "ConverTool.exe") -Force
        Remove-Item (Join-Path $d "Host.exe") -Force
    }
}
if (Test-Path (Join-Path $liteOut "Host.pdb")) { Remove-Item (Join-Path $liteOut "Host.pdb") -Force }
if (Test-Path (Join-Path $liteOut "ConverTool.pdb")) { Remove-Item (Join-Path $liteOut "ConverTool.pdb") -Force }
if (Test-Path (Join-Path $liteOut "createdump.exe")) { Remove-Item (Join-Path $liteOut "createdump.exe") -Force }

# Keep installer SetupIconFile in sync with the app icon.
$iconSrc = Join-Path $repoRoot "Host\Assets\convertool-icon.ico"
$iconDstDir = Join-Path $repoRoot "installer\Assets"
$iconDst = Join-Path $iconDstDir "convertool-icon.ico"
New-Item -ItemType Directory -Force $iconDstDir | Out-Null
if (-not (Test-Path $iconSrc)) { throw "Missing app icon: $iconSrc" }
Copy-Item -Force $iconSrc $iconDst

$uninstallPng = Join-Path $repoRoot "installer\Assets\uninstall.png"
$uninstallIco = Join-Path $repoRoot "installer\Assets\uninstall.ico"
$genUninstallIco = Join-Path $repoRoot "installer\scripts\generate-uninstall-ico.py"
if (-not (Test-Path $uninstallPng)) { throw "Missing uninstall art: $uninstallPng" }
python $genUninstallIco $uninstallPng $uninstallIco

Write-Host "== Compile installer =="
$iscc = Resolve-IsccPath
if (-not $iscc) {
    throw "Inno Setup compiler (ISCC.exe) not found. Install Inno Setup 6 first."
}

Push-Location (Join-Path $repoRoot "installer")
try {
    & $iscc $issPath
    if ($LASTEXITCODE -ne 0) {
        throw "ISCC failed with exit code $LASTEXITCODE"
    }
} finally {
    Pop-Location
}

Write-Host "Done. Installer output should be under artifacts/."
