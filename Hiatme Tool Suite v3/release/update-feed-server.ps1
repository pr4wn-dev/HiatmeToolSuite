param(
    [Parameter(Mandatory = $true)][string]$FeedDir,
    [Parameter(Mandatory = $true)][int]$Port,
    [Parameter(Mandatory = $true)][string]$LogPath
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Web

$listener = New-Object System.Net.HttpListener
$prefix = "http://127.0.0.1:$Port/"
$listener.Prefixes.Add($prefix)
$listener.Start()
"$(Get-Date -Format o) listening on $prefix feed=$FeedDir" | Out-File $LogPath -Encoding utf8

while ($listener.IsListening) {
    try {
        $ctx = $listener.GetContext()
        $path = $ctx.Request.Url.LocalPath.TrimStart('/')
        if ([string]::IsNullOrWhiteSpace($path)) { $path = 'latest.php' }
        $file = Join-Path $FeedDir $path
        if (-not (Test-Path -LiteralPath $file)) {
            $ctx.Response.StatusCode = 404
            $bytes = [Text.Encoding]::UTF8.GetBytes('Not found')
            $ctx.Response.OutputStream.Write($bytes, 0, $bytes.Length)
            $ctx.Response.Close()
            continue
        }
        $bytes = [System.IO.File]::ReadAllBytes($file)
        if ($path -like '*.json' -or $path -like 'latest.php') {
            $ctx.Response.ContentType = 'application/json; charset=utf-8'
        }
        elseif ($path -like '*.zip') {
            $ctx.Response.ContentType = 'application/zip'
        }
        $ctx.Response.ContentLength64 = $bytes.Length
        $ctx.Response.OutputStream.Write($bytes, 0, $bytes.Length)
        $ctx.Response.Close()
    }
    catch {
        "$(Get-Date -Format o) ERROR: $_" | Out-File $LogPath -Append -Encoding utf8
    }
}
