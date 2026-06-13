# Round-trip test: preview lines -> xlsx -> csv -> BuildDriverLines
$ErrorActionPreference = "Stop"
$exeDir = "c:\Users\megap\HiatmeToolSuite\Hiatme Tool Suite v3\Hiatme Tool Suite v3\bin\Debug"
$asm = [Reflection.Assembly]::LoadFrom("$exeDir\Hiatme Tool Suite v3.exe")

function Get-TypeInternal($name) {
    $t = $asm.GetType("Hiatme_Tool_Suite_v3.$name")
    if (-not $t) { throw "Type not found: $name" }
    return $t
}

function Invoke-Static($typeName, $methodName, $params) {
    $t = Get-TypeInternal $typeName
    $m = $t.GetMethod($methodName, [Reflection.BindingFlags]"Static,Public,NonPublic")
    if (-not $m) { throw "Method $typeName.$methodName not found" }
    return $m.Invoke($null, $params)
}

function New-Trip($num) {
    $tripType = Get-TypeInternal "MCDownloadedTrip"
    $trip = [Activator]::CreateInstance($tripType)
    $tripType.GetProperty("TripNumber").SetValue($trip, $num)
    $tripType.GetProperty("Date").SetValue($trip, "6/13/2026")
    $tripType.GetProperty("ClientFullName").SetValue($trip, "Test Client")
    $tripType.GetProperty("PUStreet").SetValue($trip, "1 Main St")
    $tripType.GetProperty("PUCity").SetValue($trip, "Portland")
    $tripType.GetProperty("PUTime").SetValue($trip, "8:00 AM")
    $tripType.GetProperty("DOStreet").SetValue($trip, "2 Oak St")
    $tripType.GetProperty("DOCITY").SetValue($trip, "Portland")
    $tripType.GetProperty("DOTime").SetValue($trip, "9:00 AM")
    return $trip
}

function New-PreviewLine($kind, $trip) {
    $lineType = Get-TypeInternal "ScheduleBuilderPreviewLine"
    $line = [Activator]::CreateInstance($lineType)
    $lineType.GetProperty("Kind").SetValue($line, $kind)
    if ($trip) { $lineType.GetProperty("Trip").SetValue($line, $trip) }
    return $line
}

$lineKindType = Get-TypeInternal "ScheduleBuilderPreviewLine+LineKind"
$tripKind = [Enum]::Parse($lineKindType, "Trip")
$gapKind = [Enum]::Parse($lineKindType, "Gap")

$lines = [System.Collections.Generic.List[object]]::new()
[void]$lines.Add((New-PreviewLine $tripKind (New-Trip "1001")))
[void]$lines.Add((New-PreviewLine $gapKind $null))
[void]$lines.Add((New-PreviewLine $gapKind $null))
[void]$lines.Add((New-PreviewLine $tripKind (New-Trip "1002")))

$dictType = [Type]::GetType("System.Collections.Generic.Dictionary`2")
$dictGeneric = $dictType.MakeGenericType([string], $lines.GetType())
$linesByTab = [Activator]::CreateInstance($dictGeneric, [StringComparer]::OrdinalIgnoreCase)
$dictGeneric.GetMethod("Add").Invoke($linesByTab, @("DriverA", $lines))

$optType = Get-TypeInternal "ScheduleBuilderPreviewCsvExport+Options"
$opt = [Activator]::CreateInstance($optType)
$optType.GetProperty("IncludeGaps").SetValue($opt, $true)
$optType.GetProperty("IncludeGroupHeaders").SetValue($opt, $false)

$tabs = Invoke-Static "ScheduleBuilderPreviewCsvExport" "BuildWorkbookTabs" @($linesByTab, $opt)
Write-Host "Built tabs: $($tabs.Count), rows: $($tabs[0].Rows.Count)"

$xlsxPath = Join-Path $env:TEMP "GapRoundTripTest.xlsx"
Invoke-Static "ScheduleBuilderXlsxWriter" "WriteWorkbookFromTabs" @($xlsxPath, $tabs)
Write-Host "Wrote xlsx: $xlsxPath ($((Get-Item $xlsxPath).Length) bytes)"

# Inspect zip for gap marker in column O
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($xlsxPath)
$sheet = $zip.GetEntry("xl/worksheets/sheet1.xml")
$sr = New-Object IO.StreamReader($sheet.Open())
$xml = $sr.ReadToEnd()
$sr.Close()
$zip.Dispose()
$gapCells = [regex]::Matches($xml, 'r="O\d+"')
Write-Host "Column O cells in sheet1: $($gapCells.Count)"
if ($gapCells.Count -lt 2) { Write-Host "FAIL: expected 2 gap markers in column O"; exit 1 }

$tempDir = Join-Path $env:TEMP "GapRoundTripCsv"
if (Test-Path $tempDir) { Remove-Item $tempDir -Recurse -Force }
$exported = Invoke-Static "ScheduleBuilderXlsxReader" "ExportSheetsToCsvFolder" @($xlsxPath, $tempDir)
Write-Host "Exported sheets: $($exported.Count)"
$csvPath = $exported[0].CsvPath
Write-Host "CSV path: $csvPath"
Get-Content $csvPath | ForEach-Object { $i=0 } { Write-Host "  line: $_"; $i++ }

$loaded = Invoke-Static "ScheduleBuilderGroupInference" "BuildDriverLines" @($csvPath, "DriverA", "Saturday", [ref]$null)
# BuildDriverLines has out param - use different approach
$gi = Get-TypeInternal "ScheduleBuilderGroupInference"
$m = $gi.GetMethod("BuildDriverLines")
$args = @($csvPath, "DriverA", "Saturday", $null)
$linesOut = $m.Invoke($null, $args)
$groupNote = $args[3]
Write-Host "Grouping note: $groupNote"

$lineType = Get-TypeInternal "ScheduleBuilderPreviewLine"
$gapKindVal = [Enum]::Parse($lineType.GetNestedType("LineKind"), "Gap")
$gapCount = 0
foreach ($l in $linesOut) {
    $k = $lineType.GetProperty("Kind").GetValue($l)
    if ($k.ToString() -eq "Gap") { $gapCount++ }
}
Write-Host "Loaded gap count: $gapCount (expected 2)"
if ($gapCount -ne 2) { Write-Host "FAIL"; exit 1 }
Write-Host "PASS"
