$ErrorActionPreference = "Stop"
$OsrmRoot = Split-Path $PSScriptRoot -Parent
Set-Location $OsrmRoot
docker compose down
Write-Host "OSRM stopped."
