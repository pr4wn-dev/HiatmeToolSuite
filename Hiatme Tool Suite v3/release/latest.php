<?php
// =========================================================================================================
// /downloads/hiatme-tool-suite/latest.php
//
// Update manifest endpoint for Hiatme Tool Suite v3.
// The desktop app polls this URL on launch (and whenever the user clicks "Check for updates").
//
// HOW IT WORKS
//   * Scans the same directory for files named exactly:        HiatmeToolSuite-MAJOR.MINOR.BUILD.REV.zip
//   * Picks the file with the highest 4-part version.
//   * If a sibling text file named HiatmeToolSuite-VERSION.md exists, its contents are returned as release notes.
//   * Returns JSON shaped to match UpdateManifest in UpdateClient.cs.
//
// PUBLISHING A NEW VERSION
//   1) Bump AssemblyVersion in Hiatme Tool Suite v3\Properties\AssemblyInfo.cs (e.g. 1.0.1.0).
//   2) Build Release.
//   3) Run release\package.ps1 to produce HiatmeToolSuite-1.0.1.0.zip (uses bin\Release).
//   4) Upload the .zip into this folder. Optionally drop a HiatmeToolSuite-1.0.1.0.md beside it.
//   5) Done — this endpoint will start advertising 1.0.1.0 on the next request.
// =========================================================================================================

header('Content-Type: application/json; charset=utf-8');
header('Cache-Control: no-cache, no-store, must-revalidate, max-age=0');
header('Pragma: no-cache');
header('Expires: 0');
// Allow simple cross-origin GET so a future web installer / status badge can hit this from another origin.
header('Access-Control-Allow-Origin: *');
header('Access-Control-Allow-Methods: GET, OPTIONS');

if ($_SERVER['REQUEST_METHOD'] === 'OPTIONS') {
    http_response_code(204);
    exit;
}

$folder      = __DIR__;
$baseUrl     = rtrim((isset($_SERVER['HTTPS']) && $_SERVER['HTTPS'] === 'on' ? 'https' : 'http')
                    . '://' . $_SERVER['HTTP_HOST']
                    . dirname($_SERVER['REQUEST_URI']), '/') . '/';
$zipPattern  = '/^HiatmeToolSuite-(\d+)\.(\d+)\.(\d+)\.(\d+)\.zip$/';

$best        = null;        // filename
$bestTuple   = null;        // [maj, min, build, rev]
$bestVerStr  = null;        // "X.Y.Z.W"

$entries = scandir($folder);
if ($entries === false) {
    http_response_code(500);
    echo json_encode(['error' => 'Cannot read download folder.']);
    exit;
}

foreach ($entries as $f) {
    if (preg_match($zipPattern, $f, $m)) {
        $tuple = [(int)$m[1], (int)$m[2], (int)$m[3], (int)$m[4]];
        if ($bestTuple === null || version_cmp($tuple, $bestTuple) > 0) {
            $bestTuple  = $tuple;
            $best       = $f;
            $bestVerStr = implode('.', $tuple);
        }
    }
}

if ($best === null) {
    http_response_code(404);
    echo json_encode(['error' => 'No HiatmeToolSuite-X.Y.Z.W.zip files in this folder yet.']);
    exit;
}

$fullPath = $folder . DIRECTORY_SEPARATOR . $best;
if (!is_readable($fullPath)) {
    http_response_code(500);
    echo json_encode(['error' => 'Selected release zip is not readable.']);
    exit;
}

// Optional sidecar release notes file.
$notesPath = $folder . DIRECTORY_SEPARATOR . 'HiatmeToolSuite-' . $bestVerStr . '.md';
$releaseNotes = '';
if (is_file($notesPath) && is_readable($notesPath)) {
    $releaseNotes = (string)file_get_contents($notesPath);
}

// SHA256 is the security boundary: the desktop client refuses to install a zip whose hash doesn't match
// what this endpoint returns. Don't cache the hash — recompute on every request so swapping the zip on
// disk is immediately picked up.
$sha = hash_file('sha256', $fullPath);
$size = filesize($fullPath);
$published = gmdate('c', filemtime($fullPath));

echo json_encode([
    'version'      => $bestVerStr,
    'downloadUrl'  => $baseUrl . rawurlencode($best),
    'sha256'       => $sha,
    'sizeBytes'    => $size,
    'publishedAt'  => $published,
    'releaseNotes' => $releaseNotes,
], JSON_UNESCAPED_SLASHES);

function version_cmp(array $a, array $b): int
{
    for ($i = 0; $i < 4; $i++) {
        $da = $a[$i] ?? 0;
        $db = $b[$i] ?? 0;
        if ($da !== $db) return $da - $db;
    }
    return 0;
}
