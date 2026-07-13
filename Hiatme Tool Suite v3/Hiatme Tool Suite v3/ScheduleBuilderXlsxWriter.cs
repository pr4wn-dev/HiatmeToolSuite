using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Builds schedule .xlsx workbooks without Microsoft Excel.</summary>
    internal static class ScheduleBuilderXlsxWriter
    {
        private static readonly XNamespace Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly Regex CsvSplit = new Regex(",(?=(?:[^\"]*\"[^\"]*\")*(?![^\"]*\"))");

        private const double DefaultColWidth = 7.5;
        private const double MinColWidth = 5.0;
        private const double MaxColWidth = 28.0;
        /// <summary>Extra space beyond longest cell text (Excel character units).</summary>
        private const double ColWidthPadding = 1.0;

        /// <summary>
        /// Per-column caps (A–N trip grid) so exported workbooks stay compact; long text wraps in Excel.
        /// </summary>
        private static readonly int[] MaxVisibleCharsByColumn =
        {
            10, // A Trip #
            10, // B Date
            11, // C Client
            14, // D PU street
            10, // E PU city
            10, // F PU phone
            9,  // G PU time  ("0:00" / "9:30")
            14, // H DO street
            10, // I DO city
            10, // J DO phone
            9,  // K DO time
            4,  // L Age
            5,  // M Miles
            22, // N Comments
        };

        public static void WriteWorkbookFromTabs(
            string outputPath,
            IReadOnlyList<ScheduleBuilderPreviewCsvExport.WorkbookTab> tabs,
            double[] preferredColumnWidths = null)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("Output path is required.", nameof(outputPath));
            if (tabs == null || tabs.Count == 0)
                throw new InvalidOperationException("No workbook tabs to export.");

            var sheets = new List<(string Name, List<List<string>> Rows, Dictionary<(int Row, int Col), Color> Fills, List<ScheduleBuilderPreviewCsvExport.WorkbookTab.RowMergeBar> MergeBars)>();
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tab in tabs)
            {
                if (tab == null || string.IsNullOrWhiteSpace(tab.TabName))
                    continue;

                string sheetName = MakeUniqueSheetName(tab.TabName, usedNames);
                usedNames.Add(sheetName);
                sheets.Add((
                    sheetName,
                    tab.Rows ?? new List<List<string>>(),
                    tab.CellFills ?? new Dictionary<(int, int), Color>(),
                    tab.MergeBars ?? new List<ScheduleBuilderPreviewCsvExport.WorkbookTab.RowMergeBar>()));
            }

            if (sheets.Count == 0)
                throw new InvalidOperationException("No readable workbook tabs were found.");

            WriteWorkbookInternal(outputPath, sheets, preferredColumnWidths);
        }

        public static void WriteWorkbookFromCsvFiles(string outputPath, IReadOnlyList<string> csvFilePaths)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("Output path is required.", nameof(outputPath));
            if (csvFilePaths == null || csvFilePaths.Count == 0)
                throw new InvalidOperationException("No driver CSV files to export.");

            var sheets = new List<(string Name, List<List<string>> Rows, Dictionary<(int Row, int Col), Color> Fills, List<ScheduleBuilderPreviewCsvExport.WorkbookTab.RowMergeBar> MergeBars)>();
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string csvPath in csvFilePaths.OrderBy(
                p => Path.GetFileNameWithoutExtension(p),
                Comparer<string>.Create(ScheduleBuilderPreviewCsvExport.CompareWorkbookTabNames)))
            {
                if (string.IsNullOrWhiteSpace(csvPath) || !File.Exists(csvPath))
                    continue;

                string baseName = Path.GetFileNameWithoutExtension(csvPath) ?? "Sheet";
                string sheetName = MakeUniqueSheetName(baseName, usedNames);
                usedNames.Add(sheetName);
                sheets.Add((sheetName, ReadCsvRows(csvPath), new Dictionary<(int, int), Color>(), new List<ScheduleBuilderPreviewCsvExport.WorkbookTab.RowMergeBar>()));
            }

            if (sheets.Count == 0)
                throw new InvalidOperationException("No readable driver CSV files were found.");

            WriteWorkbookInternal(outputPath, sheets);
        }

        private static void WriteWorkbookInternal(
            string outputPath,
            IReadOnlyList<(string Name, List<List<string>> Rows, Dictionary<(int Row, int Col), Color> Fills, List<ScheduleBuilderPreviewCsvExport.WorkbookTab.RowMergeBar> MergeBars)> sheets,
            double[] preferredColumnWidths = null)
        {
            string dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var sharedStrings = new List<string>();
            var sharedIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            int IndexOfShared(string value)
            {
                value = SanitizeXmlText(value ?? "");
                if (!sharedIndex.TryGetValue(value, out int idx))
                {
                    idx = sharedStrings.Count;
                    sharedStrings.Add(value);
                    sharedIndex[value] = idx;
                }
                return idx;
            }

            foreach (var sheet in sheets)
            {
                foreach (var row in sheet.Rows)
                {
                    if (row == null) continue;
                    foreach (var cell in row)
                        IndexOfShared(cell);
                }
            }

            var colorToStyle = BuildColorStyleMap(sheets);
            int defaultStyle = colorToStyle.TryGetValue(Color.Empty, out int ds) ? ds : 0;
            var fontToIndex = BuildFontColorMap(sheets);
            var mergeTextStyles = BuildMergeTextStyleMap(sheets, colorToStyle, fontToIndex);

            if (File.Exists(outputPath))
                File.Delete(outputPath);

            using (var zip = ZipFile.Open(outputPath, ZipArchiveMode.Create))
            {
                WriteEntry(zip, "[Content_Types].xml", BuildContentTypes(sheets.Count));
                WriteEntry(zip, "_rels/.rels", BuildRootRels());
                WriteEntry(zip, "xl/workbook.xml", BuildWorkbookXml(sheets));
                WriteEntry(zip, "xl/_rels/workbook.xml.rels", BuildWorkbookRels(sheets.Count));
                WriteEntry(zip, "xl/sharedStrings.xml", BuildSharedStringsXml(sharedStrings));
                WriteEntry(zip, "xl/styles.xml", BuildStylesXml(colorToStyle, fontToIndex, mergeTextStyles));

                for (int i = 0; i < sheets.Count; i++)
                {
                    string part = "xl/worksheets/sheet" + (i + 1) + ".xml";
                    WriteEntry(zip, part, BuildWorksheetXml(
                        sheets[i].Rows,
                        sheets[i].Fills,
                        sheets[i].MergeBars,
                        sharedIndex,
                        colorToStyle,
                        defaultStyle,
                        preferredColumnWidths,
                        fontToIndex,
                        mergeTextStyles));
                }
            }
        }

        internal static bool IsFileInUse(IOException ex)
        {
            if (ex == null)
                return false;
            string msg = ex.Message ?? "";
            return msg.IndexOf("being used by another process", StringComparison.OrdinalIgnoreCase) >= 0
                || msg.IndexOf("used by another process", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static bool IsFileLockError(Exception ex)
        {
            if (ex == null)
                return false;

            if (ex is AggregateException aggregate)
            {
                foreach (var inner in aggregate.Flatten().InnerExceptions)
                {
                    if (IsFileLockError(inner))
                        return true;
                }
                return false;
            }

            for (var cur = ex; cur != null; cur = cur.InnerException)
            {
                if (cur is IOException io && IsFileInUse(io))
                    return true;
                string msg = cur.Message ?? "";
                if (msg.IndexOf("is open in another program", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        internal static Exception UnwrapException(Exception ex)
        {
            if (ex is AggregateException aggregate)
                return aggregate.GetBaseException() ?? ex;
            return ex;
        }

        internal static InvalidOperationException CreateFileInUseException(string path, Exception inner)
        {
            string name = Path.GetFileName(path ?? "");
            if (string.IsNullOrEmpty(name))
                name = "the workbook";
            return new InvalidOperationException(
                "Cannot save because \"" + name + "\" is open in another program.\n\n"
                + "Close LibreOffice Calc or Excel on that file, then save again.",
                inner);
        }


        private static Dictionary<Color, int> BuildColorStyleMap(
            IReadOnlyList<(string Name, List<List<string>> Rows, Dictionary<(int Row, int Col), Color> Fills, List<ScheduleBuilderPreviewCsvExport.WorkbookTab.RowMergeBar> MergeBars)> sheets)
        {
            var colors = new List<Color> { Color.Empty };
            var map = new Dictionary<Color, int> { [Color.Empty] = 0 };

            foreach (var sheet in sheets)
            {
                if (sheet.Fills != null)
                {
                    foreach (var fill in sheet.Fills.Values)
                    {
                        Color key = NormalizeColor(fill);
                        if (map.ContainsKey(key))
                            continue;
                        map[key] = colors.Count;
                        colors.Add(key);
                    }
                }

                if (sheet.MergeBars == null)
                    continue;
                foreach (var bar in sheet.MergeBars)
                {
                    Color key = NormalizeColor(bar.Color);
                    if (map.ContainsKey(key))
                        continue;
                    map[key] = colors.Count;
                    colors.Add(key);
                }
            }

            return map;
        }

        private static Dictionary<Color, int> BuildFontColorMap(
            IReadOnlyList<(string Name, List<List<string>> Rows, Dictionary<(int Row, int Col), Color> Fills, List<ScheduleBuilderPreviewCsvExport.WorkbookTab.RowMergeBar> MergeBars)> sheets)
        {
            var map = new Dictionary<Color, int> { [Color.Empty] = 0 };
            if (sheets == null)
                return map;

            foreach (var sheet in sheets)
            {
                if (sheet.MergeBars == null)
                    continue;
                foreach (var bar in sheet.MergeBars)
                {
                    if (!bar.TextColor.HasValue)
                        continue;
                    Color key = NormalizeColor(bar.TextColor.Value);
                    if (map.ContainsKey(key))
                        continue;
                    map[key] = map.Count;
                }
            }

            return map;
        }

        private static Dictionary<(int Fill, bool Center, int Font), int> BuildMergeTextStyleMap(
            IReadOnlyList<(string Name, List<List<string>> Rows, Dictionary<(int Row, int Col), Color> Fills, List<ScheduleBuilderPreviewCsvExport.WorkbookTab.RowMergeBar> MergeBars)> sheets,
            IReadOnlyDictionary<Color, int> colorToStyle,
            IReadOnlyDictionary<Color, int> fontToIndex)
        {
            var map = new Dictionary<(int Fill, bool Center, int Font), int>();
            if (sheets == null || colorToStyle == null || fontToIndex == null)
                return map;

            int fillCount = colorToStyle.Count;
            int nextStyle = fillCount * 3;

            foreach (var sheet in sheets)
            {
                if (sheet.MergeBars == null)
                    continue;
                foreach (var bar in sheet.MergeBars)
                {
                    if (!bar.TextColor.HasValue)
                        continue;
                    if (!colorToStyle.TryGetValue(NormalizeColor(bar.Color), out int fillIdx))
                        fillIdx = 0;
                    if (!fontToIndex.TryGetValue(NormalizeColor(bar.TextColor.Value), out int fontIdx))
                        continue;
                    if (fontIdx <= 0)
                        continue;

                    var key = (fillIdx, bar.CenterText, fontIdx);
                    if (map.ContainsKey(key))
                        continue;
                    map[key] = nextStyle++;
                }
            }

            return map;
        }

        private static Color NormalizeColor(Color color)
        {
            return Color.FromArgb(255, color.R, color.G, color.B);
        }

        private static List<List<string>> ReadCsvRows(string csvPath)
        {
            var rows = new List<List<string>>();
            foreach (string line in File.ReadAllLines(csvPath))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    rows.Add(new List<string>());
                    continue;
                }

                string[] fields = CsvSplit.Split(line);
                var row = new List<string>(fields.Length);
                for (int i = 0; i < fields.Length; i++)
                {
                    string v = fields[i] ?? "";
                    if (v.Length >= 2 && v[0] == '"' && v[v.Length - 1] == '"')
                        v = v.Substring(1, v.Length - 2).Replace("\"\"", "\"");
                    row.Add(v);
                }
                rows.Add(row);
            }

            return rows;
        }

        private static string MakeUniqueSheetName(string baseName, ISet<string> usedNames)
        {
            string sanitized = SanitizeSheetName(baseName);
            if (usedNames.Add(sanitized))
                return sanitized;

            for (int n = 2; n < 100; n++)
            {
                string candidate = SanitizeSheetName(TrimForSuffix(sanitized, n));
                if (usedNames.Add(candidate))
                    return candidate;
            }

            return SanitizeSheetName("Sheet" + usedNames.Count);
        }

        private static string SanitizeSheetName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "Sheet";

            var invalid = new[] { ':', '\\', '/', '?', '*', '[', ']' };
            var sb = new StringBuilder(name.Trim());
            for (int i = 0; i < sb.Length; i++)
            {
                if (invalid.Contains(sb[i]))
                    sb[i] = '_';
            }

            string result = sb.ToString();
            if (result.Length > 31)
                result = result.Substring(0, 31);
            return string.IsNullOrWhiteSpace(result) ? "Sheet" : result;
        }

        private static string TrimForSuffix(string name, int suffix)
        {
            string tail = " (" + suffix + ")";
            int maxBase = Math.Max(1, 31 - tail.Length);
            if (name.Length <= maxBase)
                return name + tail;
            return name.Substring(0, maxBase) + tail;
        }

        private static void WriteEntry(ZipArchive zip, string path, string xml)
        {
            var entry = zip.CreateEntry(path.Replace('\\', '/'), CompressionLevel.Optimal);
            using (var stream = entry.Open())
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                writer.Write(xml);
        }

        private static string BuildContentTypes(int sheetCount)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
            sb.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");
            sb.Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>");
            sb.Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
            sb.Append("<Override PartName=\"/xl/sharedStrings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml\"/>");
            sb.Append("<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
            for (int i = 1; i <= sheetCount; i++)
            {
                sb.Append("<Override PartName=\"/xl/worksheets/sheet");
                sb.Append(i);
                sb.Append(".xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
            }
            sb.Append("</Types>");
            return sb.ToString();
        }

        private static string BuildRootRels()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>"
                + "</Relationships>";
        }

        private static string BuildWorkbookXml(
            IReadOnlyList<(string Name, List<List<string>> Rows, Dictionary<(int Row, int Col), Color> Fills, List<ScheduleBuilderPreviewCsvExport.WorkbookTab.RowMergeBar> MergeBars)> sheets)
        {
            var doc = new XDocument(
                new XElement(Ns + "workbook",
                    new XAttribute(XNamespace.Xmlns + "r", RelNs),
                    new XElement(Ns + "sheets",
                        sheets.Select((sheet, i) =>
                            new XElement(Ns + "sheet",
                                new XAttribute("name", sheet.Name),
                                new XAttribute("sheetId", (uint)(i + 1)),
                                new XAttribute(RelNs + "id", "rId" + (i + 1)))))));
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" + doc;
        }

        private static string BuildWorkbookRels(int sheetCount)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
            for (int i = 1; i <= sheetCount; i++)
            {
                sb.Append("<Relationship Id=\"rId");
                sb.Append(i);
                sb.Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet");
                sb.Append(i);
                sb.Append(".xml\"/>");
            }
            sb.Append("<Relationship Id=\"rId");
            sb.Append(sheetCount + 1);
            sb.Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings\" Target=\"sharedStrings.xml\"/>");
            sb.Append("<Relationship Id=\"rId");
            sb.Append(sheetCount + 2);
            sb.Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
            sb.Append("</Relationships>");
            return sb.ToString();
        }

        private static string BuildSharedStringsXml(IReadOnlyList<string> sharedStrings)
        {
            var doc = new XDocument(
                new XElement(Ns + "sst",
                    new XAttribute("count", sharedStrings.Count),
                    new XAttribute("uniqueCount", sharedStrings.Count),
                    sharedStrings.Select(text =>
                        new XElement(Ns + "si",
                            new XElement(Ns + "t",
                                new XAttribute(XNamespace.Xml + "space", "preserve"),
                                SanitizeXmlText(text ?? ""))))));
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" + doc;
        }

        /// <summary>Strip characters illegal in XML 1.0 text nodes (e.g. control chars in trip notes).</summary>
        private static string SanitizeXmlText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text ?? "";

            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (IsLegalXmlChar(c))
                    sb.Append(c);
            }
            return sb.ToString();
        }

        private static bool IsLegalXmlChar(char c)
        {
            return c == 0x9 || c == 0xA || c == 0xD
                || (c >= 0x20 && c <= 0xD7FF)
                || (c >= 0xE000 && c <= 0xFFFD);
        }

        private static string BuildStylesXml(
            IReadOnlyDictionary<Color, int> colorToStyle,
            IReadOnlyDictionary<Color, int> fontToIndex = null,
            IReadOnlyDictionary<(int Fill, bool Center, int Font), int> mergeTextStyles = null)
        {
            var colors = colorToStyle.Keys.OrderBy(c => colorToStyle[c]).ToList();
            var fonts = (fontToIndex ?? new Dictionary<Color, int> { [Color.Empty] = 0 })
                .OrderBy(kv => kv.Value)
                .Select(kv => kv.Key)
                .ToList();
            if (fonts.Count == 0)
                fonts.Add(Color.Empty);

            var extras = (mergeTextStyles ?? new Dictionary<(int Fill, bool Center, int Font), int>())
                .OrderBy(kv => kv.Value)
                .ToList();

            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");

            sb.Append("<fonts count=\"").Append(fonts.Count).Append("\">");
            for (int i = 0; i < fonts.Count; i++)
            {
                Color fc = fonts[i];
                sb.Append("<font><sz val=\"11\"/><name val=\"Calibri\"/>");
                if (i > 0 && fc != Color.Empty)
                {
                    sb.Append("<color rgb=\"");
                    sb.Append(ColorToRgb(fc));
                    sb.Append("\"/>");
                }
                sb.Append("</font>");
            }
            sb.Append("</fonts>");

            sb.Append("<fills count=\"").Append(colors.Count).Append("\">");
            sb.Append("<fill><patternFill patternType=\"none\"/></fill>");
            for (int i = 1; i < colors.Count; i++)
            {
                Color c = colors[i];
                sb.Append("<fill><patternFill patternType=\"solid\"><fgColor rgb=\"");
                sb.Append(ColorToRgb(c));
                sb.Append("\"/><bgColor indexed=\"64\"/></patternFill></fill>");
            }
            sb.Append("</fills>");
            sb.Append("<borders count=\"1\"><border/></borders>");
            sb.Append("<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>");

            int cellXfCount = colors.Count * 3 + extras.Count;
            sb.Append("<cellXfs count=\"").Append(cellXfCount).Append("\">");
            for (int i = 0; i < colors.Count; i++)
            {
                sb.Append("<xf numFmtId=\"0\" fontId=\"0\" fillId=\"");
                sb.Append(i);
                sb.Append("\" borderId=\"0\" xfId=\"0\" applyFill=\"1\"/>");
            }
            for (int i = 0; i < colors.Count; i++)
            {
                sb.Append("<xf numFmtId=\"0\" fontId=\"0\" fillId=\"");
                sb.Append(i);
                sb.Append("\" borderId=\"0\" xfId=\"0\" applyFill=\"1\" applyAlignment=\"1\">");
                sb.Append("<alignment horizontal=\"right\"/>");
                sb.Append("</xf>");
            }
            for (int i = 0; i < colors.Count; i++)
            {
                sb.Append("<xf numFmtId=\"0\" fontId=\"0\" fillId=\"");
                sb.Append(i);
                sb.Append("\" borderId=\"0\" xfId=\"0\" applyFill=\"1\" applyAlignment=\"1\">");
                sb.Append("<alignment horizontal=\"center\"/>");
                sb.Append("</xf>");
            }
            foreach (var kv in extras)
            {
                int fillId = kv.Key.Fill;
                int fontId = kv.Key.Font;
                bool center = kv.Key.Center;
                sb.Append("<xf numFmtId=\"0\" fontId=\"");
                sb.Append(fontId);
                sb.Append("\" fillId=\"");
                sb.Append(fillId);
                sb.Append("\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\"");
                if (center)
                {
                    sb.Append(" applyAlignment=\"1\">");
                    sb.Append("<alignment horizontal=\"center\"/>");
                    sb.Append("</xf>");
                }
                else
                {
                    sb.Append("/>");
                }
            }
            sb.Append("</cellXfs>");
            sb.Append("</styleSheet>");
            return sb.ToString();
        }

        private static string ColorToRgb(Color color)
        {
            return string.Format("{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
        }

        private static int StyleIndexForCell(int baseStyle, int columnIndex, int fillStyleCount)
        {
            if (fillStyleCount <= 0 || baseStyle < 0 || baseStyle >= fillStyleCount)
                return baseStyle;
            if (ScheduleBuilderListViewColumnWidths.IsWorkbookTimeColumn(columnIndex))
                return baseStyle + fillStyleCount;
            return baseStyle;
        }

        private static int ResolveMergeBarStyle(
            ScheduleBuilderPreviewCsvExport.WorkbookTab.RowMergeBar mergeBar,
            IReadOnlyDictionary<Color, int> colorToStyle,
            int defaultStyle,
            int fillStyleCount,
            IReadOnlyDictionary<Color, int> fontToIndex,
            IReadOnlyDictionary<(int Fill, bool Center, int Font), int> mergeTextStyles)
        {
            int style = defaultStyle;
            int fillIdx = 0;
            if (colorToStyle != null
                && colorToStyle.TryGetValue(NormalizeColor(mergeBar.Color), out int styleIdx))
            {
                style = styleIdx;
                fillIdx = styleIdx;
            }

            if (mergeBar.TextColor.HasValue
                && fontToIndex != null
                && mergeTextStyles != null
                && fontToIndex.TryGetValue(NormalizeColor(mergeBar.TextColor.Value), out int fontIdx)
                && fontIdx > 0
                && mergeTextStyles.TryGetValue((fillIdx, mergeBar.CenterText, fontIdx), out int textStyle))
            {
                return textStyle;
            }

            if (mergeBar.CenterText && fillStyleCount > 0)
                style += fillStyleCount * 2;
            return style;
        }

        private static string BuildWorksheetXml(
            IReadOnlyList<List<string>> rows,
            IReadOnlyDictionary<(int Row, int Col), Color> fills,
            IReadOnlyList<ScheduleBuilderPreviewCsvExport.WorkbookTab.RowMergeBar> mergeBars,
            IReadOnlyDictionary<string, int> sharedIndex,
            IReadOnlyDictionary<Color, int> colorToStyle,
            int defaultStyle,
            double[] preferredColumnWidths = null,
            IReadOnlyDictionary<Color, int> fontToIndex = null,
            IReadOnlyDictionary<(int Fill, bool Center, int Font), int> mergeTextStyles = null)
        {
            int fillStyleCount = colorToStyle?.Count ?? 0;

            var mergeByRow = new Dictionary<int, ScheduleBuilderPreviewCsvExport.WorkbookTab.RowMergeBar>();
            if (mergeBars != null)
            {
                foreach (var bar in mergeBars)
                    mergeByRow[bar.RowIndex] = bar;
            }

            var sheetData = new XElement(Ns + "sheetData");
            for (int r = 0; r < rows.Count; r++)
            {
                var row = rows[r];
                if (row == null || row.Count == 0)
                {
                    sheetData.Add(new XElement(Ns + "row", new XAttribute("r", r + 1)));
                    continue;
                }

                var rowEl = new XElement(Ns + "row", new XAttribute("r", r + 1));
                if (mergeByRow.TryGetValue(r, out var mergeBar))
                {
                    string value = mergeBar.StartCol < row.Count ? (row[mergeBar.StartCol] ?? "") : "";
                    string cellRef = IndexToColumnLetters(mergeBar.StartCol + 1) + (r + 1);
                    int style = ResolveMergeBarStyle(
                        mergeBar, colorToStyle, defaultStyle, fillStyleCount, fontToIndex, mergeTextStyles);

                    rowEl.Add(new XElement(Ns + "c",
                        new XAttribute("r", cellRef),
                        new XAttribute("t", "s"),
                        new XAttribute("s", style),
                        new XElement(Ns + "v", sharedIndex[value])));

                    int colCount = Math.Max(row.Count, ScheduleBuilderPreviewCsvExport.WorkbookExportColumnCount);
                    for (int c = 0; c < colCount; c++)
                    {
                        if (c >= mergeBar.StartCol && c <= mergeBar.EndCol)
                            continue;

                        string metaValue = c < row.Count ? (row[c] ?? "") : "";
                        if (string.IsNullOrEmpty(metaValue))
                            continue;

                        string metaRef = IndexToColumnLetters(c + 1) + (r + 1);
                        int metaStyle = StyleIndexForCell(defaultStyle, c, fillStyleCount);
                        rowEl.Add(new XElement(Ns + "c",
                            new XAttribute("r", metaRef),
                            new XAttribute("t", "s"),
                            new XAttribute("s", metaStyle),
                            new XElement(Ns + "v", sharedIndex[metaValue])));
                    }
                }
                else
                {
                    int colCount = Math.Max(row.Count, ScheduleBuilderPreviewCsvExport.WorkbookExportColumnCount);
                    for (int c = 0; c < colCount; c++)
                    {
                        string value = c < row.Count ? (row[c] ?? "") : "";
                        // Skip empty cells except hidden metadata in column O.
                        if (string.IsNullOrEmpty(value))
                            continue;

                        string cellRef = IndexToColumnLetters(c + 1) + (r + 1);
                        int style = defaultStyle;
                        if (fills != null
                            && fills.TryGetValue((r, c), out Color fillColor)
                            && colorToStyle.TryGetValue(NormalizeColor(fillColor), out int styleIdx))
                            style = styleIdx;

                        style = StyleIndexForCell(style, c, fillStyleCount);

                        rowEl.Add(new XElement(Ns + "c",
                            new XAttribute("r", cellRef),
                            new XAttribute("t", "s"),
                            new XAttribute("s", style),
                            new XElement(Ns + "v", sharedIndex[value])));
                    }
                }
                sheetData.Add(rowEl);
            }

            var worksheet = new XElement(Ns + "worksheet");

            int lastRow = rows?.Count ?? 0;
            if (lastRow > 0)
            {
                string lastCol = IndexToColumnLetters(ScheduleBuilderPreviewCsvExport.WorkbookExportColumnCount);
                worksheet.Add(new XElement(Ns + "dimension",
                    new XAttribute("ref", "A1:" + lastCol + lastRow)));
            }

            worksheet.Add(new XElement(Ns + "sheetFormatPr",
                new XAttribute("defaultRowHeight", "15"),
                new XAttribute("defaultColWidth", DefaultColWidth.ToString("0.##", CultureInfo.InvariantCulture))));

            var colWidths = ResolveColumnWidths(
                rows,
                ScheduleBuilderPreviewCsvExport.WorkbookExportColumnCount,
                mergeByRow.Keys,
                preferredColumnWidths);
            colWidths[ScheduleBuilderPreviewCsvExport.WorkbookMetaColumnIndex] = 0;
            worksheet.Add(BuildColsElement(colWidths));
            worksheet.Add(sheetData);
            if (mergeBars != null && mergeBars.Count > 0)
            {
                var mergeCells = new XElement(Ns + "mergeCells",
                    new XAttribute("count", mergeBars.Count));
                foreach (var bar in mergeBars)
                {
                    string start = IndexToColumnLetters(bar.StartCol + 1) + (bar.RowIndex + 1);
                    string end = IndexToColumnLetters(bar.EndCol + 1) + (bar.RowIndex + 1);
                    mergeCells.Add(new XElement(Ns + "mergeCell", new XAttribute("ref", start + ":" + end)));
                }
                worksheet.Add(mergeCells);
            }

            var doc = new XDocument(worksheet);
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" + doc;
        }

        private static string IndexToColumnLetters(int index1Based)
        {
            var sb = new StringBuilder();
            int n = index1Based;
            while (n > 0)
            {
                n--;
                sb.Insert(0, (char)('A' + (n % 26)));
                n /= 26;
            }
            return sb.Length == 0 ? "A" : sb.ToString();
        }

        /// <summary>
        /// Prefer ListView / saved workbook widths where provided; fill phone/age columns from content.
        /// </summary>
        internal static double[] ResolveColumnWidths(
            IReadOnlyList<List<string>> rows,
            int columnCount,
            IEnumerable<int> mergedBarRowIndices = null,
            double[] preferredColumnWidths = null)
        {
            double[] computed = ComputeColumnWidths(rows, columnCount, mergedBarRowIndices);
            if (preferredColumnWidths == null || preferredColumnWidths.Length == 0)
                return computed;

            var widths = new double[columnCount];
            for (int c = 0; c < columnCount; c++)
            {
                if (c < preferredColumnWidths.Length && preferredColumnWidths[c] > 0)
                    widths[c] = preferredColumnWidths[c];
                else if (c < computed.Length)
                    widths[c] = computed[c];
                else
                    widths[c] = DefaultColWidth;
            }

            return widths;
        }

        /// <summary>Approximate Excel column width (Calibri 11 character units) from ListView pixels.</summary>
        internal static double PixelsToExcelColumnWidth(int pixels)
        {
            if (pixels <= 0)
                return DefaultColWidth;

            double width = (pixels - 5.0) / 7.0;
            if (width < MinColWidth)
                width = MinColWidth;
            if (width > MaxColWidth)
                width = MaxColWidth;
            return Math.Round(width, 2);
        }

        /// <summary>Approximate ListView pixels from an Excel column width.</summary>
        internal static int ExcelColumnWidthToPixels(double excelWidth)
        {
            if (excelWidth <= 0)
                return 0;

            int px = (int)Math.Round(excelWidth * 7.0 + 5.0);
            return Math.Max(24, px);
        }

        /// <summary>
        /// Size columns from trip/data rows only — merged group/reserve header rows span A–N
        /// and must not inflate column A from long notes.
        /// </summary>
        internal static double[] ComputeColumnWidths(
            IReadOnlyList<List<string>> rows,
            int columnCount,
            IEnumerable<int> mergedBarRowIndices = null)
        {
            var skipRows = mergedBarRowIndices != null
                ? new HashSet<int>(mergedBarRowIndices)
                : null;
            var maxChars = new int[columnCount];

            if (rows != null)
            {
                for (int r = 0; r < rows.Count; r++)
                {
                    if (skipRows != null && skipRows.Contains(r))
                        continue;

                    var row = rows[r];
                    if (row == null)
                        continue;

                    int limit = Math.Min(columnCount, row.Count);
                    for (int c = 0; c < limit; c++)
                    {
                        int len = VisibleLength(row[c]);
                        if (len > maxChars[c])
                            maxChars[c] = len;
                    }
                }
            }

            var widths = new double[columnCount];
            for (int c = 0; c < columnCount; c++)
                widths[c] = ColumnWidthFromMaxChars(maxChars[c], c);

            return widths;
        }

        private static int VisibleLength(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0)
                return 0;

            int len = 0;
            foreach (char ch in text)
            {
                if (char.IsControl(ch))
                    continue;
                len += ch < 128 ? 1 : 2;
            }

            return len;
        }

        private static double ColumnWidthFromMaxChars(int maxChars, int columnIndex = -1)
        {
            if (columnIndex >= 0 && columnIndex < MaxVisibleCharsByColumn.Length)
                maxChars = Math.Min(maxChars, MaxVisibleCharsByColumn[columnIndex]);

            if (maxChars <= 0)
                return DefaultColWidth;

            double width = maxChars + ColWidthPadding;
            if (width < MinColWidth)
                width = MinColWidth;
            if (width > MaxColWidth)
                width = MaxColWidth;
            return width;
        }

        private static XElement BuildColsElement(IReadOnlyList<double> widths)
        {
            var cols = new XElement(Ns + "cols");
            if (widths == null || widths.Count == 0)
                return cols;

            cols.Add(new XAttribute("count", widths.Count));
            for (int i = 0; i < widths.Count; i++)
            {
                var col = new XElement(Ns + "col",
                    new XAttribute("min", i + 1),
                    new XAttribute("max", i + 1),
                    new XAttribute("width", widths[i].ToString("0.##", CultureInfo.InvariantCulture)),
                    new XAttribute("customWidth", "1"));
                if (widths[i] <= 0)
                    col.Add(new XAttribute("hidden", "1"));
                cols.Add(col);
            }

            return cols;
        }
    }
}
