# One-time preprocess: extract -> partition -> customize (MLD). CPU-heavy (~20-60 min).
$ErrorActionPreference = "Stop"
$OsrmRoot = Split-Path $PSScriptRoot -Parent
$DataDir = Join-Path $OsrmRoot "data"
$Pbf = Join-Path $DataDir "maine-latest.osm.pbf"
$Image = "ghcr.io/project-osrm/osrm-backend"

if (-not (Test-Path $Pbf)) {
    Write-Error "Missing $Pbf - run 01-download-maine.ps1 first."
}

function Invoke-OsrmStep {
    param([string]$Name, [string[]]$CmdArgs)
    Write-Host ""
    Write-Host "=== $Name ==="
    & docker run --rm -t -v "${DataDir}:/data" $Image @CmdArgs
    if ($LASTEXITCODE -ne 0) { throw "OSRM step failed: $Name (exit $LASTEXITCODE)" }
}

Invoke-OsrmStep "osrm-extract" @(
    "osrm-extract", "-p", "/opt/car.lua", "/data/maine-latest.osm.pbf"
)
Invoke-OsrmStep "osrm-partition" @(
    "osrm-partition", "/data/maine-latest.osrm"
)
Invoke-OsrmStep "osrm-customize" @(
    "osrm-customize", "/data/maine-latest.osrm"
)

Write-Host ""
Write-Host "Graph ready. Start the server with scripts\start-osrm.ps1"
