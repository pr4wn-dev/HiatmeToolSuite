$ErrorActionPreference = "Stop"
$OsrmRoot = Split-Path $PSScriptRoot -Parent
$GraphMarker = Join-Path $OsrmRoot "data\maine-latest.osrm.properties"
if (-not (Test-Path $GraphMarker)) {
    Write-Error "Missing preprocessed graph. Run 01-download-maine.ps1 then 02-build-graph.ps1 first."
}
Set-Location $OsrmRoot
docker compose up -d
Write-Host "OSRM starting on http://127.0.0.1:5000 - run scripts\test-osrm.ps1 to verify."
