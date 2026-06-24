# Configure and build Hiatme Tool Suite on a dispatch desk.
# Usage:
#   powershell -ExecutionPolicy Bypass -File scripts\setup-desk.ps1 -OfficePanelUrl "http://192.168.1.23:8787"
#   powershell -ExecutionPolicy Bypass -File scripts\setup-desk.ps1 -OfficePanelUrl "http://192.168.1.23:8787" -RemotePanelUrl "http://100.64.0.5:8787"
param(
    [Parameter(Mandatory = $true)]
    [string]$OfficePanelUrl,
    [string]$RemotePanelUrl = "",
    [string]$LocalPanelUrl = "http://127.0.0.1:8787",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$projectDir = Join-Path $repoRoot "Hiatme Tool Suite v3\Hiatme Tool Suite v3"
$csproj = Join-Path $projectDir "Hiatme Tool Suite v3.csproj"
$syncScript = Join-Path $projectDir "scripts\sync-hiatme-ai-config.ps1"

Write-Host "=== Hiatme Tool Suite desk setup ===" -ForegroundColor Cyan
Write-Host "Repo: $repoRoot"

function Find-MsBuild {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $msb = & $vswhere -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
        if ($msb) { return $msb }
    }
    $candidates = @(
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe"
    )
    foreach ($c in $candidates) {
        if (Test-Path $c) { return $c }
    }
    return $null
}

if (-not $SkipBuild) {
    $msbuild = Find-MsBuild
    if (-not $msbuild) {
        Write-Host "MSBuild not found. Install VS 2022 Build Tools, or run with -SkipBuild after building in Visual Studio." -ForegroundColor Red
        exit 1
    }
    Write-Host "Building $Configuration ..."
    & $msbuild $csproj /p:Configuration=$Configuration /v:minimal /restore
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Write-Host "Build OK -> $projectDir\bin\$Configuration\" -ForegroundColor Green
} else {
    Write-Host "Skipped build (-SkipBuild)." -ForegroundColor Yellow
}

if (-not (Test-Path $syncScript)) {
    Write-Host "Missing $syncScript" -ForegroundColor Red
    exit 1
}

$env:HIATME_OFFICE_PANEL_URL = $OfficePanelUrl.TrimEnd('/')
if ($RemotePanelUrl) { $env:HIATME_REMOTE_PANEL_URL = $RemotePanelUrl.TrimEnd('/') }
& powershell -ExecutionPolicy Bypass -File $syncScript `
    -OfficePanelUrl $env:HIATME_OFFICE_PANEL_URL `
    -RemotePanelUrl $RemotePanelUrl `
    -LocalPanelUrl $LocalPanelUrl `
    -Configuration $Configuration
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$exe = Join-Path $projectDir "bin\$Configuration\Hiatme Tool Suite v3.exe"
Write-Host ""
Write-Host "=== Desk setup complete ===" -ForegroundColor Green
Write-Host "Run: $exe"
Write-Host ""
Write-Host "Before BUILD / LOAD:" -ForegroundColor Yellow
Write-Host "  - Copy weekday template folders (Monday, Tuesday, ...) beside the exe or install dir."
Write-Host "  - Sign in to Modivcare once for BUILD (LOAD does not need Modivcare)."
Write-Host ""
Write-Host "Deployment doc: docs\DEPLOYMENT.md"
Write-Host "AIagent server: https://github.com/pr4wn-dev/AIagent/blob/main/docs/DEPLOY-NEW-SERVER.md"
