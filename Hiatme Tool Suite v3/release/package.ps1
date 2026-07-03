<#
.SYNOPSIS
  Build and package a Hiatme Tool Suite v3 release zip ready to upload to
  https://hiatme.com/downloads/hiatme-tool-suite/

.DESCRIPTION
  1. Reads the current AssemblyVersion from Properties\AssemblyInfo.cs.
  2. Builds the solution in Release (unless -SkipBuild).
  3. Bundles bin\Release of both projects (main app + Update.exe) into
     HiatmeToolSuite-<version>.zip in this folder.

  AppOnly (default) omits Resources\login_backgrounds (~450 MB of PNGs). Existing
  installs keep their backgrounds; the updater only overwrites files in the zip.
  Use -PackageMode Full when login art changed or for a first-install bundle.

.PARAMETER PackageMode
  AppOnly — exe, dlls, configs, Update.exe (~15 MB). Default for routine updates.
  Full    — everything in bin\Release including login_backgrounds (~470 MB).

.PARAMETER ReleaseNotes
  Optional inline release notes string. Written to HiatmeToolSuite-<version>.md.

.PARAMETER SkipBuild
  Skip MSBuild; stage from an existing Release output (must already be built).
#>

param(
    [ValidateSet('AppOnly', 'Full')]
    [string]$PackageMode = 'AppOnly',
    [string]$ReleaseNotes,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

# -- paths
$scriptDir   = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot    = Resolve-Path (Join-Path $scriptDir '..')
$mainProjDir = Join-Path $repoRoot 'Hiatme Tool Suite v3'
$asmInfo     = Join-Path $mainProjDir 'Properties\AssemblyInfo.cs'
$mainRelease = Join-Path $mainProjDir 'bin\Release'
$updRelease  = Join-Path $repoRoot 'Update\bin\Release'
$solution    = Join-Path $repoRoot 'Hiatme Tool Suite v3.sln'

if (-not (Test-Path $asmInfo))   { throw "AssemblyInfo.cs not found at $asmInfo" }
if (-not (Test-Path $solution))  { throw "Solution not found at $solution" }

# -- read version
$asmText = Get-Content $asmInfo -Raw
$m = [regex]::Match($asmText, '(?m)^\s*\[assembly:\s*AssemblyVersion\("([^"]+)"\)\]')
if (-not $m.Success) { throw "Could not parse AssemblyVersion from $asmInfo" }
$ver = $m.Groups[1].Value
if ($ver -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    throw "AssemblyVersion is '$ver'. Use a fixed 4-part version like 1.0.1.0 - wildcards are not allowed for releases."
}
Write-Host "Packaging Hiatme Tool Suite v$ver ($PackageMode)"

# Dev/user folders that must never ship even in Full packages.
$alwaysExcludeTopDirs = @(
    'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday',
    'Template Temps'
)

function Get-RelativePath([string]$root, [string]$fullPath) {
    $rel = $fullPath.Substring($root.Length).TrimStart('\', '/')
    return $rel -replace '/', '\'
}

function Test-ExcludedFromPackage([string]$relativePath) {
    if ([string]::IsNullOrWhiteSpace($relativePath)) { return $false }
    $rel = $relativePath -replace '/', '\'
    $top = ($rel -split '\\')[0]
    if ($alwaysExcludeTopDirs -contains $top) { return $true }
    if ($PackageMode -eq 'Full') { return $false }
    if ($rel -match '^Resources\\login_backgrounds(\\|$)') { return $true }
    return $false
}

function Copy-ReleaseTree([string]$srcRoot, [string]$dstRoot) {
    $srcRoot = $srcRoot.TrimEnd('\')
    foreach ($dir in [System.IO.Directory]::EnumerateDirectories($srcRoot, '*', [System.IO.SearchOption]::AllDirectories)) {
        $rel = Get-RelativePath $srcRoot $dir
        if (Test-ExcludedFromPackage $rel) { continue }
        $target = Join-Path $dstRoot $rel
        if (-not (Test-Path $target)) {
            New-Item -ItemType Directory -Path $target -Force | Out-Null
        }
    }
    foreach ($file in [System.IO.Directory]::EnumerateFiles($srcRoot, '*', [System.IO.SearchOption]::AllDirectories)) {
        $rel = Get-RelativePath $srcRoot $file
        if (Test-ExcludedFromPackage $rel) { continue }
        $target = Join-Path $dstRoot $rel
        $targetDir = Split-Path $target -Parent
        if (-not (Test-Path $targetDir)) {
            New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
        }
        Copy-Item -Path $file -Destination $target -Force
    }
}

function Find-MsBuild {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path $vswhere)) { $vswhere = "${env:ProgramFiles}\Microsoft Visual Studio\Installer\vswhere.exe" }
    if (Test-Path $vswhere) {
        $found = & $vswhere -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
        if ($found) { return $found }
    }
    $candidates = @(
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
    )
    foreach ($c in $candidates) {
        if (Test-Path $c) { return $c }
    }
    return $null
}

if (-not $SkipBuild) {
    $msbuild = Find-MsBuild
    if (-not $msbuild) { throw "MSBuild not found. Install VS 2022 Build Tools or pass -SkipBuild." }
    Write-Host "Using MSBuild: $msbuild"
    Write-Host "Building solution (Release)..."
    & $msbuild $solution /t:Restore,Build /p:Configuration=Release /v:minimal /nologo
    if ($LASTEXITCODE -ne 0) { throw "MSBuild failed with exit code $LASTEXITCODE" }
} else {
    Write-Host "Skipped build (-SkipBuild)."
}

if (-not (Test-Path $mainRelease)) { throw "Main app Release output missing: $mainRelease" }
if (-not (Test-Path $updRelease))  { throw "Update.exe Release output missing: $updRelease"  }

# -- stage payload
$staging = Join-Path $env:TEMP "HiatmeToolSuitePkg_$(Get-Date -Format yyyyMMddHHmmss)"
New-Item -ItemType Directory -Path $staging | Out-Null
try {
    Write-Host "Staging main app ($PackageMode)..."
    if ($PackageMode -eq 'AppOnly') {
        Write-Host "  (skipping Resources\login_backgrounds - existing installs keep their PNGs)"
    }
    Copy-ReleaseTree $mainRelease $staging

    Write-Host "Staging Update.exe..."
    Copy-Item -Path (Join-Path $updRelease 'Update.exe') -Destination $staging -Force
    $updPdb = Join-Path $updRelease 'Update.pdb'
    if (Test-Path $updPdb) { Copy-Item -Path $updPdb -Destination $staging -Force }
    $updCfg = Join-Path $updRelease 'Update.exe.config'
    if (Test-Path $updCfg) { Copy-Item -Path $updCfg -Destination $staging -Force }
    $materialSkin = Join-Path $updRelease 'MaterialSkin.dll'
    if (Test-Path $materialSkin) { Copy-Item -Path $materialSkin -Destination $staging -Force }

    $zipName = "HiatmeToolSuite-$ver.zip"
    $zipPath = Join-Path $scriptDir $zipName
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Write-Host "Creating $zipName..."
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory($staging, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)

    if ($ReleaseNotes) {
        $mdPath = Join-Path $scriptDir "HiatmeToolSuite-$ver.md"
        $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
        [System.IO.File]::WriteAllText($mdPath, $ReleaseNotes, $utf8NoBom)
        Write-Host "Wrote release notes: $mdPath"
    }

    $sha = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLower()
    $sizeMb = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
    Write-Host ""
    Write-Host "Done."
    Write-Host "  Mode  : $PackageMode"
    Write-Host "  Zip   : $zipPath"
    Write-Host "  Size  : $sizeMb MB"
    Write-Host "  SHA256: $sha"
    Write-Host ""
    Write-Host "Next steps:"
    Write-Host "  1) Upload $zipName to /downloads/hiatme-tool-suite/ on hiatme.com."
    Write-Host "  2) (Optional) Upload HiatmeToolSuite-$ver.md alongside it for release notes."
    if ($PackageMode -eq 'AppOnly') {
        Write-Host ""
        Write-Host "AppOnly zip: desks that already have login backgrounds download ~15 MB."
        Write-Host "New PCs or refreshed art: run .\package.ps1 -PackageMode Full once and upload that zip"
        Write-Host "when backgrounds change (or hand them the last Full zip for first install)."
    }
}
finally {
    if (Test-Path $staging) { Remove-Item -Path $staging -Recurse -Force -ErrorAction SilentlyContinue }
}
