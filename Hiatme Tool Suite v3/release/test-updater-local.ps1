<#
.SYNOPSIS
  Local end-to-end test for the Hiatme Tool Suite updater (carousel + Update.exe handoff).

.EXAMPLE
  .\test-updater-local.ps1 -Launch
#>
param(
    [string]$OldVersion = "4.0.0.2",
    [string]$NewVersion = "4.0.0.3",
    [int]$Port = 8765,
    [string]$SandboxRoot = "F:\Projects\apps\hiatme-updater-test",
    [switch]$Launch,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$scriptDir   = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot    = Resolve-Path (Join-Path $scriptDir "..")
$mainProjDir = Join-Path $repoRoot "Hiatme Tool Suite v3"
$asmInfo     = Join-Path $mainProjDir "Properties\AssemblyInfo.cs"
$packagePs1  = Join-Path $scriptDir "package.ps1"
$feedServer  = Join-Path $scriptDir "update-feed-server.ps1"
$installDir  = Join-Path $SandboxRoot "install"
$feedDir     = Join-Path $SandboxRoot "feed"
$serverLog   = Join-Path $SandboxRoot "feed-server.log"
$pidFile     = Join-Path $SandboxRoot "feed-server.pid"

function Set-AssemblyVersion([string]$version) {
    $text = Get-Content $asmInfo -Raw
    $text = [regex]::Replace($text, '(?m)(\[assembly:\s*AssemblyVersion\(")[^"]+("\)\])', '${1}' + $version + '${2}')
    $text = [regex]::Replace($text, '(?m)(\[assembly:\s*AssemblyFileVersion\(")[^"]+("\)\])', '${1}' + $version + '${2}')
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($asmInfo, $text, $utf8)
    Write-Host "AssemblyInfo -> $version"
}

function Copy-ReleaseInstall([string]$srcRelease, [string]$dest) {
    if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
    New-Item -ItemType Directory -Path $dest -Force | Out-Null
    robocopy $srcRelease $dest /E /NFL /NDL /NJH /NJS /nc /ns /np | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed copying Release to $dest" }
}

function Set-ConfigManifestUrl([string]$configPath, [string]$url) {
    [xml]$xml = Get-Content $configPath
    $node = $xml.configuration.appSettings.add | Where-Object { $_.key -eq "UpdateManifestUrl" }
    if ($null -eq $node) {
        throw "UpdateManifestUrl missing in $configPath. Rebuild Release after App.config change."
    }
    $node.value = $url
    $xml.Save($configPath)
}

function Stop-FeedServerIfRunning {
    if (-not (Test-Path $pidFile)) { return }
    $oldPid = (Get-Content $pidFile -Raw).Trim()
    if ($oldPid -match '^\d+$') {
        try { Stop-Process -Id ([int]$oldPid) -Force -ErrorAction SilentlyContinue } catch { }
    }
    Remove-Item $pidFile -Force -ErrorAction SilentlyContinue
}

function Start-FeedServer([string]$feed, [int]$listenPort) {
    Stop-FeedServerIfRunning
    New-Item -ItemType Directory -Path $feed -Force | Out-Null
    if (Test-Path $serverLog) { Remove-Item $serverLog -Force }
    $procArgs = @(
        "-ExecutionPolicy", "Bypass", "-File", $feedServer,
        "-FeedDir", $feed, "-Port", $listenPort, "-LogPath", $serverLog
    )
    $proc = Start-Process -FilePath "powershell.exe" -ArgumentList $procArgs -PassThru -WindowStyle Hidden
    Set-Content -Path $pidFile -Value $proc.Id -Encoding ascii
    Start-Sleep -Seconds 2
    try {
        $null = Invoke-WebRequest -Uri "http://127.0.0.1:$listenPort/latest.php" -UseBasicParsing -TimeoutSec 5
    }
    catch {
        throw "Local update feed failed to start on port $listenPort. Try running as Administrator or pick another -Port."
    }
    return $proc
}

function New-ManifestJson([string]$feed, [string]$version, [string]$zipName, [string]$notesPath, [string]$baseUrl) {
    $zipPath = Join-Path $feed $zipName
    if (-not (Test-Path $zipPath)) { throw "Zip missing: $zipPath" }
    $sha = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLower()
    $size = (Get-Item $zipPath).Length
    $notes = ""
    if ($notesPath -and (Test-Path -LiteralPath $notesPath)) {
        $notes = [System.IO.File]::ReadAllText($notesPath)
    }
    $manifest = [ordered]@{
        version      = $version
        downloadUrl  = ($baseUrl.TrimEnd('/') + '/' + [uri]::EscapeDataString($zipName))
        sha256       = $sha
        sizeBytes    = $size
        publishedAt  = (Get-Date).ToUniversalTime().ToString("o")
        releaseNotes = [string]$notes
    }
    $manifestPath = Join-Path $feed "latest.php"
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Compress), $utf8)
    Write-Host "Manifest written: $manifestPath"
}

Write-Host ""
Write-Host "=== Hiatme local updater test ===" -ForegroundColor Cyan
Write-Host "Old install : v$OldVersion"
Write-Host "New package : v$NewVersion"
Write-Host "Feed URL    : http://127.0.0.1:$Port/latest.php"
Write-Host ""

$running = Get-Process -Name "Hiatme Tool Suite v3" -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "WARNING: Close the running Hiatme Tool Suite before building Release." -ForegroundColor Yellow
}

New-Item -ItemType Directory -Path $SandboxRoot -Force | Out-Null
New-Item -ItemType Directory -Path $feedDir -Force | Out-Null

$releaseNotes = @"
What's new in $($NewVersion):

Updater UX test
- Carousel release notes with Prev / Next while download runs
- Restart when you're ready after download verifies
- Update.exe waits for file unlock before copying and launching

Schedule Builder
- Map preload and mileage HUD improvements
- Modivcare new-trip sync into Reserves
"@

if (-not $SkipBuild) {
    Write-Host "Building sandbox install at v$OldVersion..." -ForegroundColor Green
    Set-AssemblyVersion $OldVersion
    $solution = Join-Path $repoRoot "Hiatme Tool Suite v3.sln"
    dotnet msbuild $solution -t:Clean -p:Configuration=Release -v:minimal -nologo | Out-Host
    Remove-Item -Recurse -Force (Join-Path $mainProjDir "bin\Release") -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force (Join-Path $mainProjDir "obj\Release") -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force (Join-Path $repoRoot "Update\bin\Release") -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force (Join-Path $repoRoot "Update\obj\Release") -ErrorAction SilentlyContinue
    dotnet msbuild $solution -t:Restore,Build -p:Configuration=Release -v:minimal -nologo
    if ($LASTEXITCODE -ne 0) { throw "Release build failed for v$OldVersion" }
    $builtVer = (Get-Item (Join-Path $mainProjDir "bin\Release\Hiatme Tool Suite v3.exe")).VersionInfo.FileVersion
    if ($builtVer -ne $OldVersion) {
        throw "Built exe is v$builtVer but expected v$OldVersion. Assembly version stamping did not take effect."
    }
    Copy-ReleaseInstall (Join-Path $mainProjDir "bin\Release") $installDir
    $updRelease = Join-Path $repoRoot "Update\bin\Release"
    Copy-Item (Join-Path $updRelease "Update.exe") $installDir -Force
    if (Test-Path (Join-Path $updRelease "Update.exe.config")) {
        Copy-Item (Join-Path $updRelease "Update.exe.config") $installDir -Force
    }
    if (Test-Path (Join-Path $updRelease "MaterialSkin.dll")) {
        Copy-Item (Join-Path $updRelease "MaterialSkin.dll") $installDir -Force
    }

    Write-Host "Building update package at v$NewVersion..." -ForegroundColor Green
    Set-AssemblyVersion $NewVersion
    & $packagePs1 -PackageMode AppOnly -ReleaseNotes $releaseNotes
    if ($LASTEXITCODE -ne 0) { throw "package.ps1 failed" }
}

$zipName = "HiatmeToolSuite-$NewVersion.zip"
$zipSrc  = Join-Path $scriptDir $zipName
if (-not (Test-Path $zipSrc)) { throw "Missing $zipSrc" }
Copy-Item $zipSrc (Join-Path $feedDir $zipName) -Force
$notesSrc = Join-Path $scriptDir "HiatmeToolSuite-$NewVersion.md"
$notesFeed = Join-Path $feedDir "HiatmeToolSuite-$NewVersion.md"
if (Test-Path $notesSrc) { Copy-Item $notesSrc $notesFeed -Force }

New-ManifestJson -feed $feedDir -version $NewVersion -zipName $zipName -notesPath $notesFeed -baseUrl "http://127.0.0.1:$Port/"

$serverProc = Start-FeedServer -feed $feedDir -listenPort $Port
$configPath = Join-Path $installDir "Hiatme Tool Suite v3.exe.config"
Set-ConfigManifestUrl $configPath "http://127.0.0.1:$Port/latest.php"

Write-Host ""
Write-Host "Ready." -ForegroundColor Green
Write-Host "  Sandbox: $installDir"
Write-Host "  Feed log: $serverLog (server pid $($serverProc.Id))"
Write-Host ""
Write-Host "Test checklist:"
Write-Host "  [ ] Carousel slides (Prev/Next)"
Write-Host "  [ ] Download while browsing"
Write-Host "  [ ] RESTART TO INSTALL appears when verified"
Write-Host "  [ ] Update.exe installs, then LAUNCH button"
Write-Host "  [ ] App reopens as v$NewVersion without file-in-use error"
Write-Host ""

if ($Launch) {
    Start-Process -FilePath (Join-Path $installDir "Hiatme Tool Suite v3.exe") -WorkingDirectory $installDir
}
