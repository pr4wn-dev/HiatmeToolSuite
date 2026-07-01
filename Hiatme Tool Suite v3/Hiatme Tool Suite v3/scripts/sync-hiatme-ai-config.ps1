# Writes hiatme_ai.defaults.json (+ optional hiatme_ai.json) from AIagent .env token.
# Run after pull or when HIATME_API_TOKEN changes. Safe to re-run.
#
# Connect-from-anywhere (port forward + DDNS):
#   -PublicPanelUrl "http://hiatme.yourdomain.com:8787" `
#   -OfficePanelUrl "http://192.168.1.23:8787" `
#   -HomePanelUrl "http://192.168.0.50:8787"
# Public URL is BaseUrl (works off-LAN). Office/home LAN IPs are fallbacks when on those Wi‑Fi networks.
param(
    [string]$PublicPanelUrl = $(if ($env:HIATME_PUBLIC_PANEL_URL) { $env:HIATME_PUBLIC_PANEL_URL } else { "" }),
    [string]$OfficePanelUrl = $(if ($env:HIATME_OFFICE_PANEL_URL) { $env:HIATME_OFFICE_PANEL_URL } else { "http://192.168.1.23:8787" }),
    [string]$HomePanelUrl = $(if ($env:HIATME_HOME_PANEL_URL) { $env:HIATME_HOME_PANEL_URL } else { "" }),
    [string]$RemotePanelUrl = $(if ($env:HIATME_REMOTE_PANEL_URL) { $env:HIATME_REMOTE_PANEL_URL } else { "" }),
    [string]$LocalPanelUrl = "http://127.0.0.1:8787",
    [ValidateSet("Debug", "Release", "Both")]
    [string]$Configuration = "Both",
    [switch]$SkipPersonal,
    [switch]$IncludeLocalFallback
)

$ErrorActionPreference = "Stop"
$projectDir = Split-Path $PSScriptRoot -Parent

function Find-AiagentEnv {
    $repoRoot = (Get-Item $projectDir).Parent.Parent.FullName
    $candidates = @(
        (Join-Path $repoRoot "AIagent\.env"),
        (Join-Path (Split-Path $repoRoot -Parent) "AIagent\.env"),
        "F:\Projects\AIagent\.env",
        "C:\Projects\AIagent\.env",
        (Join-Path $env:USERPROFILE "AIagent\.env")
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
        $r = Invoke-WebRequest -Uri "$($url.TrimEnd('/'))/api/hiatme/geo/status" -Headers $h -TimeoutSec 6 -UseBasicParsing
        return $r.StatusCode -eq 200
    } catch { return $false }
}

$token = Read-Token (Find-AiagentEnv)
if (-not $token) {
    Write-Warning "HIATME_API_TOKEN not found in AIagent .env; ApiToken left empty."
}

function Add-UniqueUrl($list, $url) {
    if (-not $url) { return }
    $u = $url.Trim().TrimEnd('/')
    if (-not $u) { return }
    if (-not $list.Contains($u)) { [void]$list.Add($u) }
}

$primary = if ($PublicPanelUrl) { $PublicPanelUrl.TrimEnd('/') } else { $OfficePanelUrl.TrimEnd('/') }

$fallbacks = New-Object System.Collections.Generic.List[string]
foreach ($u in @($OfficePanelUrl, $HomePanelUrl, $RemotePanelUrl)) { Add-UniqueUrl $fallbacks $u }
if ($IncludeLocalFallback) { Add-UniqueUrl $fallbacks $LocalPanelUrl }
# Drop primary from fallbacks so we do not duplicate BaseUrl.
$fallbacks = @($fallbacks | Where-Object { $_ -ne $primary })

$probeUrls = @($primary) + @($fallbacks)
if ($IncludeLocalFallback) { $probeUrls += $LocalPanelUrl.TrimEnd('/') }
$resolved = $null
foreach ($u in ($probeUrls | Select-Object -Unique)) {
    if (Test-Panel $u $token) { $resolved = $u; break }
}

$defaults = [ordered]@{
    BaseUrl                        = $primary
    FallbackBaseUrls               = @($fallbacks)
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
$targets = @((Join-Path $projectDir "hiatme_ai.defaults.json"))
if ($Configuration -eq "Debug" -or $Configuration -eq "Both") {
    $targets += (Join-Path $projectDir "bin\Debug\hiatme_ai.defaults.json")
}
if ($Configuration -eq "Release" -or $Configuration -eq "Both") {
    $targets += (Join-Path $projectDir "bin\Release\hiatme_ai.defaults.json")
}

foreach ($t in $targets) {
    $dir = Split-Path $t -Parent
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    Set-Content -Path $t -Value $json -Encoding UTF8
    Write-Host "Wrote $t"
}

if (-not $SkipPersonal) {
    foreach ($cfg in @("Debug", "Release")) {
        if ($Configuration -ne "Both" -and $Configuration -ne $cfg) { continue }
        $binDir = Join-Path $projectDir "bin\$cfg"
        if (-not (Test-Path $binDir)) { continue }
        $personalPath = Join-Path $binDir "hiatme_ai.json"
        $personal = [ordered]@{
            BaseUrl                        = $primary
            FallbackBaseUrls               = @($fallbacks)
            ApiToken                       = $token
            ClientId                       = ""
            LastResolvedBaseUrl            = $(if ($resolved) { $resolved } else { $primary })
            RememberOnSave                 = $true
            UseServerGeo                   = $true
            UseServerSolve                 = $true
            AllowLocalSolveFallback        = $false
            UseWeekdayTemplates            = $true
            FinishRemainingAfterTemplates  = $true
        }
        Set-Content -Path $personalPath -Value ($personal | ConvertTo-Json -Depth 5) -Encoding UTF8
        Write-Host "Wrote $personalPath"
    }
}

$resolvedLabel = if ($resolved) { $resolved } else { "none" }
Write-Host "Panel probe: primary=$primary office=$($OfficePanelUrl.TrimEnd('/')) home=$HomePanelUrl public=$PublicPanelUrl resolved=$resolvedLabel"
