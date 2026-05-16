<#
.SYNOPSIS
  Build and package a Hiatme Tool Suite v3 release zip ready to upload to
  https://hiatme.com/downloads/hiatme-tool-suite/

.DESCRIPTION
  1. Reads the current AssemblyVersion from
     "Hiatme Tool Suite v3\Properties\AssemblyInfo.cs".
  2. Builds the solution in Release.
  3. Bundles bin\Release of both projects (main app + Update.exe) into
     HiatmeToolSuite-<version>.zip in this folder.

  After running, upload the produced HiatmeToolSuite-X.Y.Z.W.zip into the
  /downloads/hiatme-tool-suite/ folder on hiatme.com along with this folder's
  latest.php (once). Optionally drop a HiatmeToolSuite-X.Y.Z.W.md beside the
  zip with release notes; latest.php will surface them in the desktop update
  prompt.

  IMPORTANT: bump AssemblyVersion before running this script. The desktop
  client compares the manifest version against the running build AssemblyVersion -
  if they are equal the user will (correctly) be told they are already up to date.

.PARAMETER ReleaseNotes
  Optional inline release notes string. If supplied, it is written to
  HiatmeToolSuite-<version>.md next to the zip.
#>

param(
    [string]$ReleaseNotes
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
Write-Host "Packaging Hiatme Tool Suite v$ver"

# -- locate MSBuild
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vswhere)) { $vswhere = "${env:ProgramFiles}\Microsoft Visual Studio\Installer\vswhere.exe" }
if (-not (Test-Path $vswhere)) { throw "vswhere.exe not found. Install Visual Studio 2019/2022 or pass -MSBuild path." }
$msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) { throw "MSBuild not found via vswhere." }
Write-Host "Using MSBuild: $msbuild"

# -- build Release
Write-Host "Building solution (Release)..."
& $msbuild $solution /t:Restore,Build /p:Configuration=Release /v:minimal /nologo
if ($LASTEXITCODE -ne 0) { throw "MSBuild failed with exit code $LASTEXITCODE" }

if (-not (Test-Path $mainRelease)) { throw "Main app Release output missing: $mainRelease" }
if (-not (Test-Path $updRelease))  { throw "Update.exe Release output missing: $updRelease"  }

# -- stage payload
$staging = Join-Path $env:TEMP "HiatmeToolSuitePkg_$(Get-Date -Format yyyyMMddHHmmss)"
New-Item -ItemType Directory -Path $staging | Out-Null
try {
    Write-Host "Staging main app..."
    # Copy everything from bin\Release EXCEPT user-data subfolders that may have leaked into a dev build.
    # (Templates and login files are not put there by the app, but the safety net costs nothing.)
    $excludeDirs = @(
        'Monday','Tuesday','Wednesday','Thursday','Friday','Saturday','Sunday',
        'Template Temps'
    )
    Get-ChildItem -Path $mainRelease -Force | ForEach-Object {
        if ($_.PSIsContainer -and ($excludeDirs -contains $_.Name)) { return }
        Copy-Item -Path $_.FullName -Destination $staging -Recurse -Force
    }

    Write-Host "Staging Update.exe..."
    Copy-Item -Path (Join-Path $updRelease 'Update.exe')        -Destination $staging -Force
    $updPdb = Join-Path $updRelease 'Update.pdb'
    if (Test-Path $updPdb) { Copy-Item -Path $updPdb -Destination $staging -Force }
    $updCfg = Join-Path $updRelease 'Update.exe.config'
    if (Test-Path $updCfg) { Copy-Item -Path $updCfg -Destination $staging -Force }

    # -- write zip
    $zipName = "HiatmeToolSuite-$ver.zip"
    $zipPath = Join-Path $scriptDir $zipName
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Write-Host "Creating $zipName..."
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory($staging, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)

    # -- write release notes sidecar
    # Use explicit UTF8Encoding(false) so we don't emit a BOM. Set-Content -Encoding UTF8 in Windows
    # PowerShell 5.1 writes a BOM which then shows up as a stray \ufeff at the top of the in-app dialog.
    if ($ReleaseNotes) {
        $mdPath = Join-Path $scriptDir "HiatmeToolSuite-$ver.md"
        $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
        [System.IO.File]::WriteAllText($mdPath, $ReleaseNotes, $utf8NoBom)
        Write-Host "Wrote release notes: $mdPath"
    }

    # -- show sha256 so the user can sanity-check post-upload
    $sha = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLower()
    Write-Host ""
    Write-Host "Done."
    Write-Host "  Zip   : $zipPath"
    Write-Host "  Size  : $((Get-Item $zipPath).Length) bytes"
    Write-Host "  SHA256: $sha"
    Write-Host ""
    Write-Host "Next steps:"
    Write-Host "  1) Upload $zipName to /downloads/hiatme-tool-suite/ on hiatme.com."
    Write-Host "  2) (Optional) Upload HiatmeToolSuite-$ver.md alongside it for release notes."
    Write-Host "  3) Make sure latest.php is already deployed in that folder (only once)."
}
finally {
    if (Test-Path $staging) { Remove-Item -Path $staging -Recurse -Force -ErrorAction SilentlyContinue }
}
