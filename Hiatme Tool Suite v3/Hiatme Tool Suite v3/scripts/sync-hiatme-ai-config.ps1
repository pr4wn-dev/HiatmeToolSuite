# Writes hiatme_ai.defaults.json (+ optional hiatme_ai.json) from AIagent .env token.
# Run after pull or when HIATME_API_TOKEN changes. Safe to re-run.
param(
    [string]$OfficePanelUrl = $(if ($env:HIATME_OFFICE_PANEL_URL) { $env:HIATME_OFFICE_PANEL_URL } else { "http://192.168.1.50:8787" }),
    [string]$LocalPanelUrl = "http://127.0.0.1:8787",
    [switch]$SkipPersonal
)

$ErrorActionPreference = "Stop"
$projectDir = Split-Path $PSScriptRoot -Parent
$binDir = Join-Path $projectDir "bin\Debug"

function Find-AiagentEnv {
    $repoRoot = (Get-Item $projectDir).Parent.Parent.FullName
    $candidates = @(
        (Join-Path $repoRoot "AIagent\.env"),
        "F:\Projects\AIagent\.env",
        "C:\Users\megap\AIagent\.env"
    )
    foreach ($p in $candidates) {
        if (Test-Path $p) { return $p }
    }
    return $null
}

function Read-Token($envPath) {
    if (-not $envPath) { return "" }
    foreach ($line in Get-Content $envPath) {
        if ($line -match '^\s*HIATME_API_TOKEN\s*=\s*(.+)\s*$') {
            return $Matches[1].Trim().Trim('"').Trim("'")
        }
    }
    return ""
}

function Test-Panel($url, $token) {
    if (-not $url) { return $false }
    try {
        $h = @{}
        if ($token) { $h["Authorization"] = "Bearer $token" }
        $r = Invoke-WebRequest -Uri "$($url.TrimEnd('/'))/api/hiatme/geo/status" -Headers $h -TimeoutSec 3 -UseBasicParsing
        return $r.StatusCode -eq 200
    } catch { return $false }
}

$token = Read-Token (Find-AiagentEnv)
if (-not $token) {
    Write-Warning "HIATME_API_TOKEN not found in AIagent .env; ApiToken left empty."
}

$resolved = $null
foreach ($u in @($LocalPanelUrl, $OfficePanelUrl)) {
    if (Test-Panel $u $token) { $resolved = $u; break }
}

$defaults = [ordered]@{
    BaseUrl                        = $OfficePanelUrl
    FallbackBaseUrls               = @($LocalPanelUrl)
    ApiToken                       = $token
    UseServerGeo                   = $true
    UseServerSolve                 = $true
    AllowLocalSolveFallback        = $false
    UseWeekdayTemplates            = $true
    FinishRemainingAfterTemplates  = $true
    RememberOnSave                 = $true
}
if ($resolved) {
    $defaults["LastResolvedBaseUrl"] = $resolved
}

$json = ($defaults | ConvertTo-Json -Depth 5)
$targets = @(
    (Join-Path $projectDir "hiatme_ai.defaults.json"),
    (Join-Path $binDir "hiatme_ai.defaults.json")
)
foreach ($t in $targets) {
    $dir = Split-Path $t -Parent
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    Set-Content -Path $t -Value $json -Encoding UTF8
    Write-Host "Wrote $t"
}

if (-not $SkipPersonal) {
    $personalPath = Join-Path $binDir "hiatme_ai.json"
    $personal = [ordered]@{
        BaseUrl                        = $OfficePanelUrl
        FallbackBaseUrls               = @($LocalPanelUrl)
        ApiToken                       = $token
        ClientId                       = ""
        LastResolvedBaseUrl            = $(if ($resolved) { $resolved } else { $LocalPanelUrl })
        RememberOnSave                 = $true
        UseServerGeo                   = $true
        UseServerSolve                 = $true
        AllowLocalSolveFallback        = $false
        UseWeekdayTemplates            = $true
        FinishRemainingAfterTemplates  = $true
    }
    if (-not (Test-Path $binDir)) { New-Item -ItemType Directory -Path $binDir -Force | Out-Null }
    Set-Content -Path $personalPath -Value ($personal | ConvertTo-Json -Depth 5) -Encoding UTF8
    Write-Host "Wrote $personalPath"
}

$resolvedLabel = if ($resolved) { $resolved } else { "none" }
Write-Host "Panel probe: office=$OfficePanelUrl local=$LocalPanelUrl resolved=$resolvedLabel"
