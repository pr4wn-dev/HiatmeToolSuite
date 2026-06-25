using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Reads saved schedule .xlsx workbooks without Microsoft Excel installed.</summary>
    internal static class ScheduleBuilderXlsxReader
    {
        private static readonly XNamespace Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace PkgRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        public static List<(string Tab, string CsvPath)> ExportSheetsToCsvFolder(string workbookPath, string tempDir)
        {
            var exported = new List<(string Tab, string CsvPath)>();
            if (string.IsNullOrWhiteSpace(workbookPath) || !File.Exists(workbookPath))
                return exported;

            Directory.CreateDirectory(tempDir);

            using (var zip = ZipFile.OpenRead(workbookPath))
            {
                var sharedStrings = LoadSharedStrings(zip);
                var sheetEntries = LoadSheetEntries(zip);

                foreach (var entry in sheetEntries)
                {
                    string tab = entry.Name?.Trim() ?? "";
                    if (string.IsNullOrEmpty(tab) || tab.Equals("Sheet1", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var rows = ReadSheetRows(zip, entry.Path, sharedStrings);
                    if (rows.Count == 0)
                        continue;

                    string safe = MakeSafeFileName(tab);
                    string csvPath = Path.Combine(tempDir, safe + ".csv");
                    WriteRowsAsCsv(csvPath, rows);
                    exported.Add((tab, csvPath));
                }
            }

            return exported;
        }

        /// <summary>
        /// Reads all workbook sheets as row/cell strings without requiring desktop Excel.
        /// </summary>
        public static List<(string Tab, List<List<string>> Rows)> ReadWorkbookSheets(string workbookPath)
        {
            var sheets = new List<(string Tab, List<List<string>> Rows)>();
            if (string.IsNullOrWhiteSpace(workbookPath) || !File.Exists(workbookPath))
                return sheets;

            using (var zip = ZipFile.OpenRead(workbookPath))
            {
                var sharedStrings = LoadSharedStrings(zip);
                var sheetEntries = LoadSheetEntries(zip);

                foreach (var entry in sheetEntries)
                {
                    string tab = (entry.Name ?? "").Trim();
                    if (string.IsNullOrEmpty(tab))
                        continue;

                    var rows = ReadSheetRows(zip, entry.Path, sharedStrings);
                    sheets.Add((tab, rows));
                }
            }

            return sheets;
        }

        private static List<string> LoadSharedStrings(ZipArchive zip)
        {
            var list = new List<string>();
            var entry = zip.GetEntry("xl/sharedStrings.xml");
            if (entry == null)
                return list;

            using (var stream = entry.Open())
            {
                var doc = XDocument.Load(stream);
                foreach (var si in doc.Descendants(Ns + "si"))
                {
                    var text = string.Concat(si.Descendants(Ns + "t").Select(t => t.Value));
                    if (string.IsNullOrEmpty(text))
                        text = string.Concat(si.Descendants(Ns + "r").SelectMany(r => r.Descendants(Ns + "t")).Select(t => t.Value));
                    list.Add(text ?? "");
                }
            }

            return list;
        }

        private sealed class SheetEntry
        {
            public string Name { get; set; }
            public string Path { get; set; }
        }

        private static List<SheetEntry> LoadSheetEntries(ZipArchive zip)
        {
            var rels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var relEntry = zip.GetEntry("xl/_rels/workbook.xml.rels");
            if (relEntry != null)
            {
                using (var stream = relEntry.Open())
                {
                    var doc = XDocument.Load(stream);
                    foreach (var rel in doc.Descendants(PkgRelNs + "Relationship"))
                    {
                        string id = rel.Attribute("Id")?.Value;
                        string target = rel.Attribute("Target")?.Value;
                        if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(target))
                            rels[id] = NormalizePartPath(target);
                    }
                }
            }

            var sheets = new List<SheetEntry>();
            var wbEntry = zip.GetEntry("xl/workbook.xml");
            if (wbEntry == null)
                return sheets;

            using (var stream = wbEntry.Open())
            {
                var doc = XDocument.Load(stream);
                foreach (var sheet in doc.Descendants(Ns + "sheet"))
                {
                    string name = sheet.Attribute("name")?.Value ?? "";
                    string relId = sheet.Attribute(RelNs + "id")?.Value;
                    if (string.IsNullOrEmpty(relId) || !rels.TryGetValue(relId, out string target))
                        continue;
                    sheets.Add(new SheetEntry { Name = name, Path = target });
                }
            }

            return sheets;
        }

        private static string NormalizePartPath(string target)
        {
            target = (target ?? "").Replace('\\', '/').TrimStart('/');
            if (target.StartsWith("xl/", StringComparison.OrdinalIgnoreCase))
                return target;
            return "xl/" + target;
        }

        private static List<List<string>> ReadSheetRows(ZipArchive zip, string partPath, IList<string> sharedStrings)
        {
            var rows = new List<List<string>>();
            var entry = zip.GetEntry(partPath) ?? zip.GetEntry(partPath.TrimStart('/'));
            if (entry == null)
                return rows;

            using (var stream = entry.Open())
            {
                var doc = XDocument.Load(stream);
                foreach (var row in doc.Descendants(Ns + "sheetData").Elements(Ns + "row"))
                {
                    var cells = new Dictionary<int, string>();
                    int maxCol = 0;
                    int nextCol = 1;
                    foreach (var cell in row.Elements(Ns + "c"))
                    {
                        string cellRef = cell.Attribute("r")?.Value;
                        int colIndex;
                        if (!string.IsNullOrEmpty(cellRef))
                        {
                            string colLetters = new string(cellRef.TakeWhile(char.IsLetter).ToArray());
                            colIndex = ColumnLettersToIndex(colLetters);
                            nextCol = colIndex + 1;
                        }
                        else
                        {
                            // LibreOffice / Excel sometimes omit cell refs — place in order.
                            colIndex = nextCol;
                            nextCol++;
                        }

                        if (colIndex > maxCol)
                            maxCol = colIndex;

                        cells[colIndex] = ReadCellValue(cell, sharedStrings);
                    }

                    if (cells.Count == 0)
                    {
                        rows.Add(new List<string>());
                        continue;
                    }

                    var line = new List<string>();
                    for (int c = 1; c <= Math.Max(maxCol, ScheduleBuilderPreviewCsvExport.WorkbookExportColumnCount); c++)
                        line.Add(cells.TryGetValue(c, out string v) ? v ?? "" : "");
                    rows.Add(line);
                }
            }

            return rows;
        }

        private static string ReadCellValue(XElement cell, IList<string> sharedStrings)
        {
            string type = cell.Attribute("t")?.Value ?? "";

            var isEl = cell.Element(Ns + "is");
            if (isEl != null)
                return string.Concat(isEl.Descendants(Ns + "t").Select(t => t.Value));

            if (type == "inlineStr")
                return string.Concat(cell.Descendants(Ns + "t").Select(t => t.Value));

            var v = cell.Element(Ns + "v");
            if (v == null)
                return "";

            string raw = v.Value ?? "";
            if (type == "s")
            {
                if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int idx)
                    && idx >= 0 && idx < sharedStrings.Count)
                    return sharedStrings[idx] ?? "";
                return raw;
            }

            if (type == "str")
                return raw;

            if (type == "b")
                return raw == "1" ? "TRUE" : "FALSE";

            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double num))
            {
                int col = ColumnLettersToIndex(new string((cell.Attribute("r")?.Value ?? "A1")
                    .TakeWhile(char.IsLetter).ToArray()));

                if (col == 2 && num > 20000 && num < 60000)
                {
                    try
                    {
                        return DateTime.FromOADate(num).ToString("M/d/yyyy", CultureInfo.InvariantCulture);
                    }
                    catch
                    {
                        /* use raw */
                    }
                }

                if (TryFormatExcelTripTimeColumn(col, num, out string timeText))
                    return timeText;

                if (Math.Abs(num - Math.Round(num)) < 0.0000001)
                    return ((long)Math.Round(num)).ToString(CultureInfo.InvariantCulture);
            }

            return raw;
        }

        /// <summary>Columns G and K — PU time and DO time in the 14-column schedule export.</summary>
        private static bool TryFormatExcelTripTimeColumn(int columnIndex1Based, double num, out string formatted)
        {
            formatted = null;
            if (columnIndex1Based != 7 && columnIndex1Based != 11)
                return false;

            double dayFraction = num;
            if (dayFraction >= 1.0)
            {
                dayFraction = dayFraction % 1.0;
                if (dayFraction <= 0.0000001)
                    return false;
            }
            else if (dayFraction < 0)
                return false;

            try
            {
                var clock = DateTime.Today.Add(TimeSpan.FromDays(dayFraction));
                formatted = clock.ToString("h:mm tt", CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static int ColumnLettersToIndex(string letters)
        {
            if (string.IsNullOrEmpty(letters))
                return 1;
            int index = 0;
            foreach (char ch in letters.ToUpperInvariant())
            {
                if (ch < 'A' || ch > 'Z')
                    continue;
                index = index * 26 + (ch - 'A' + 1);
            }
            return Math.Max(1, index);
        }

        private static void WriteRowsAsCsv(string csvPath, List<List<string>> rows)
        {
            var sb = new StringBuilder();
            foreach (var row in rows)
            {
                if (row == null || row.Count == 0)
                {
                    sb.AppendLine();
                    continue;
                }

                int colCount = Math.Max(row.Count, ScheduleBuilderPreviewCsvExport.WorkbookExportColumnCount);
                for (int i = 0; i < colCount; i++)
                {
                    if (i > 0)
                        sb.Append(',');
                    sb.Append('"');
                    sb.Append((i < row.Count ? row[i] ?? "" : "").Replace("\"", "\"\""));
                    sb.Append('"');
                }
                sb.AppendLine();
            }

            File.WriteAllText(csvPath, sb.ToString(), Encoding.UTF8);
        }

        private static string MakeSafeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return string.IsNullOrWhiteSpace(name) ? "Sheet" : name.Trim();
        }

        /// <summary>Reads trip-grid column widths (A–N) from the first driver sheet in a saved workbook.</summary>
        public static double[] ReadTripGridColumnWidths(string workbookPath)
        {
            if (string.IsNullOrWhiteSpace(workbookPath) || !File.Exists(workbookPath))
                return null;

            using (var zip = ZipFile.OpenRead(workbookPath))
            {
                foreach (var entry in LoadSheetEntries(zip))
                {
                    string tab = (entry.Name ?? "").Trim();
                    if (string.IsNullOrEmpty(tab)
                        || tab.Equals("Sheet1", StringComparison.OrdinalIgnoreCase)
                        || tab.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
                        continue;

                    return ReadSheetColumnWidths(zip, entry.Path, ScheduleBuilderPreviewCsvExport.ColumnCount);
                }
            }

            return null;
        }

        private static double[] ReadSheetColumnWidths(ZipArchive zip, string partPath, int columnCount)
        {
            var entry = zip.GetEntry(partPath) ?? zip.GetEntry(partPath.TrimStart('/'));
            if (entry == null)
                return null;

            var widths = new double[columnCount];
            bool any = false;

            using (var stream = entry.Open())
            {
                var doc = XDocument.Load(stream);
                var colsEl = doc.Element(Ns + "worksheet")?.Element(Ns + "cols");
                if (colsEl == null)
                    return null;

                foreach (var col in colsEl.Elements(Ns + "col"))
                {
                    string w = col.Attribute("width")?.Value;
                    if (string.IsNullOrEmpty(w))
                        continue;
                    if (!double.TryParse(w, NumberStyles.Float, CultureInfo.InvariantCulture, out double width))
                        continue;

                    int min = ParseIntAttr(col, "min", 0);
                    int max = ParseIntAttr(col, "max", min);
                    if (min < 1)
                        continue;

                    for (int c = min; c <= max && c <= columnCount; c++)
                    {
                        widths[c - 1] = width;
                        any = true;
                    }
                }
            }

            return any ? widths : null;
        }

        private static int ParseIntAttr(XElement el, string name, int fallback)
        {
            string raw = el.Attribute(name)?.Value;
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;
        }
    }
}
