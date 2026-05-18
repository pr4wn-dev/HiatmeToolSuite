# Bangor-area smoke test (2 points inside Maine)
$ErrorActionPreference = "Stop"
$Url = "http://127.0.0.1:5000/route/v1/driving/-68.77,44.80;-68.65,44.91?overview=false"
Write-Host "GET $Url"
try {
    $resp = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 30
    Write-Host "HTTP $($resp.StatusCode)"
    $json = $resp.Content | ConvertFrom-Json
    if ($json.code -eq "Ok") {
        Write-Host "OK - OSRM local routing works."
        exit 0
    }
    Write-Error "OSRM returned code: $($json.code)"
}
catch {
    $msg = $_.Exception.Message
    Write-Error "OSRM not reachable. Is Docker running? Did you run start-osrm.ps1? $msg"
}
