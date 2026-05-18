# Downloads Geofabrik Maine extract into tools/osrm/data/
$ErrorActionPreference = "Stop"
$OsrmRoot = Split-Path $PSScriptRoot -Parent
$DataDir = Join-Path $OsrmRoot "data"
$Url = "https://download.geofabrik.de/north-america/us/maine-latest.osm.pbf"
$OutFile = Join-Path $DataDir "maine-latest.osm.pbf"

if (-not (Test-Path $DataDir)) { New-Item -ItemType Directory -Path $DataDir | Out-Null }

if (Test-Path $OutFile) {
    Write-Host "Already exists: $OutFile"
    Write-Host "Delete it to re-download."
    exit 0
}

Write-Host "Downloading Maine OSM extract (~100 MB)..."
Write-Host "  $Url"
Invoke-WebRequest -Uri $Url -OutFile $OutFile -UseBasicParsing
Write-Host "Done: $OutFile"
