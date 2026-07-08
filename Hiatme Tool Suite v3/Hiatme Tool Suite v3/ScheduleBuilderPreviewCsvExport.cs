using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Writes Template Temps CSVs and colored workbook sheets from Schedule Builder preview lines.</summary>
    internal static class ScheduleBuilderPreviewCsvExport
    {
        public const int ColumnCount = 14;
        /// <summary>Hidden column O (0-based index 14) — save/load metadata only, not part of the trip grid.</summary>
        public const int WorkbookMetaColumnIndex = 14;
        public const int WorkbookExportColumnCount = 15;
        /// <summary>Merge A–M; column N stays empty in the visible schedule.</summary>
        public const int MergeBarLastCol = ColumnCount - 2;

        public sealed class Options
        {
            public bool IncludeGaps { get; set; }
            public bool IncludeGroupHeaders { get; set; }
            public bool IncludeReserveSections { get; set; } = true;

            public static Options TripsOnly => new Options();
        }

        /// <summary>Reserves first, then driver tabs A–Z (legacy fallback when no explicit order).</summary>
        public static int CompareWorkbookTabNames(string a, string b)
        {
            a = a ?? "";
            b = b ?? "";
            bool aRes = a.Equals("Reserves", StringComparison.OrdinalIgnoreCase);
            bool bRes = b.Equals("Reserves", StringComparison.OrdinalIgnoreCase);
            if (aRes != bRes)
                return aRes ? -1 : 1;
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }

        public sealed class WorkbookTab
        {
            public string TabName { get; set; }
            public List<List<string>> Rows { get; set; } = new List<List<string>>();
            public Dictionary<(int Row, int Col), Color> CellFills { get; set; }
                = new Dictionary<(int Row, int Col), Color>();
            /// <summary>Full-width colored bars (merged A–N in xlsx).</summary>
            public List<RowMergeBar> MergeBars { get; set; } = new List<RowMergeBar>();

            public sealed class RowMergeBar
            {
                public int RowIndex { get; set; }
                public int StartCol { get; set; }
                public int EndCol { get; set; }
                public Color Color { get; set; }
            }

            public int AddRow(string[] cells)
            {
                int width = WorkbookExportColumnCount;
                if (cells != null && cells.Length > width)
                    width = cells.Length;

                var row = new List<string>(width);
                for (int i = 0; i < width; i++)
                    row.Add(i < cells?.Length ? (cells[i] ?? "") : "");
                Rows.Add(row);
                return Rows.Count - 1;
            }

            public void FillRow(int rowIndex, Color color)
            {
                for (int c = 0; c < ColumnCount; c++)
                    FillCell(rowIndex, c, color);
            }

            public void FillCell(int rowIndex, int col, Color color)
            {
                if (rowIndex < 0 || col < 0 || col >= ColumnCount)
                    return;
                CellFills[(rowIndex, col)] = color;
            }

            public void AddMergeBar(int rowIndex, Color color, int startCol = 0, int endCol = ColumnCount - 1)
            {
                MergeBars.Add(new RowMergeBar
                {
                    RowIndex = rowIndex,
                    StartCol = startCol,
                    EndCol = endCol,
                    Color = color,
                });
            }
        }

        public static IReadOnlyList<WorkbookTab> BuildWorkbookTabs(
            IReadOnlyDictionary<string, List<ScheduleBuilderPreviewLine>> linesByTab,
            Options options)
        {
            return BuildWorkbookTabs(linesByTab, options, tabOrder: null);
        }

        public static IReadOnlyList<WorkbookTab> BuildWorkbookTabs(
            IReadOnlyDictionary<string, List<ScheduleBuilderPreviewLine>> linesByTab,
            Options options,
            IReadOnlyList<string> tabOrder)
        {
            if (linesByTab == null || linesByTab.Count == 0)
                return Array.Empty<WorkbookTab>();

            options = options ?? Options.TripsOnly;
            var tabs = new List<WorkbookTab>();

            IEnumerable<KeyValuePair<string, List<ScheduleBuilderPreviewLine>>> ordered;
            if (tabOrder == null || tabOrder.Count == 0)
            {
                ordered = linesByTab.OrderBy(
                    x => x.Key,
                    Comparer<string>.Create(CompareWorkbookTabNames));
            }
            else
            {
                ordered = ScheduleBuilderTabOrder.OrderedKeys(linesByTab, tabOrder)
                    .Select(key => new KeyValuePair<string, List<ScheduleBuilderPreviewLine>>(
                        key,
                        linesByTab[key] ?? new List<ScheduleBuilderPreviewLine>()));
            }

            foreach (var kv in ordered)
            {
                if (string.IsNullOrWhiteSpace(kv.Key))
                    continue;

                bool reserves = kv.Key.Equals("Reserves", StringComparison.OrdinalIgnoreCase);
                var lines = kv.Value ?? new List<ScheduleBuilderPreviewLine>();
                var tab = new WorkbookTab { TabName = kv.Key };
                if (reserves)
                    AppendReservesContent(tab, lines, options);
                else
                    AppendDriverContent(tab, lines, options);
                tabs.Add(tab);
            }

            return tabs;
        }

        public static void WriteTabCsv(
            string csvPath,
            IList<ScheduleBuilderPreviewLine> lines,
            Options options,
            bool isReservesTab)
        {
            if (string.IsNullOrWhiteSpace(csvPath))
                throw new ArgumentException("CSV path is required.", nameof(csvPath));

            options = options ?? Options.TripsOnly;
            lines = lines ?? Array.Empty<ScheduleBuilderPreviewLine>();

            var tab = new WorkbookTab
            {
                TabName = Path.GetFileNameWithoutExtension(csvPath) ?? "Sheet",
            };
            if (isReservesTab)
                AppendReservesContent(tab, lines, options);
            else
                AppendDriverContent(tab, lines, options);

            var sb = new StringBuilder();
            foreach (var row in tab.Rows)
                sb.AppendLine(FormatCsvRow(row));

            string dir = Path.GetDirectoryName(csvPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(csvPath, sb.ToString());
        }

        private static void AppendDriverContent(WorkbookTab tab, IList<ScheduleBuilderPreviewLine> lines, Options options)
        {
            List<SupeyTripCluster> groups = options.IncludeGroupHeaders
                ? ScheduleBuilderPreviewGroups.BuildFromPreviewLines(lines)
                : null;
            SupeyTripCluster lastHeaderGroup = null;
            bool sawTripRow = false;

            foreach (var line in lines)
            {
                if (line == null)
                    continue;

                    if (line.Kind == ScheduleBuilderPreviewLine.LineKind.Gap)
                    {
                        lastHeaderGroup = null;
                        if (ScheduleBuilderTrailingRows.IsTrailingPad(line))
                            continue;
                        if (ScheduleBuilderGapNotes.HasNoteContent(line))
                        {
                            int noteRow = tab.AddRow(BuildNoteCells(line.GapNoteText ?? ""));
                            if (line.GapNoteRowColor.HasValue)
                                tab.AddMergeBar(noteRow, line.GapNoteRowColor.Value, endCol: MergeBarLastCol);
                            sawTripRow = true;
                        }
                        else if (options.IncludeGaps && sawTripRow)
                        {
                            tab.AddRow(BuildGapCells());
                        }

                        continue;
                    }

                if (line.Kind == ScheduleBuilderPreviewLine.LineKind.GroupHeader)
                {
                    bool exportNoteRow = options.IncludeGroupHeaders
                        || !string.IsNullOrWhiteSpace(line.GroupNoteText)
                        || line.GroupNoteRowColor.HasValue;

                    if (!exportNoteRow)
                    {
                        if (options.IncludeGaps && sawTripRow)
                        {
                            lastHeaderGroup = null;
                            tab.AddRow(BuildGapCells());
                        }
                        continue;
                    }

                    var headerGroup = options.IncludeGroupHeaders
                        ? FindGroupByNumber(groups, line.GroupNumber)
                        : null;
                    Color? barColor = line.GroupNoteRowColor;
                    if (!barColor.HasValue && options.IncludeGroupHeaders)
                        barColor = headerGroup?.DisplayColor ?? SupeyGroupPalette.For(line.GroupNumber);

                    int noteRow = tab.AddRow(BuildGroupHeaderCells(line.GroupNumber, line.GroupNoteText ?? ""));
                    if (barColor.HasValue)
                        tab.AddMergeBar(noteRow, barColor.Value, endCol: MergeBarLastCol);
                    lastHeaderGroup = headerGroup;
                    continue;
                }

                if (line.Kind != ScheduleBuilderPreviewLine.LineKind.Trip || line.Trip == null)
                    continue;

                if (options.IncludeGroupHeaders && groups != null)
                {
                    var g = ScheduleBuilderPreviewGroups.FindGroupForTrip(groups, line.Trip);
                    if (g != null && !ReferenceEquals(g, lastHeaderGroup))
                    {
                        // Show the group-color header for every group, including the first.
                        // (Leading blank gap rows are still skipped above.)
                        int noteRow = tab.AddRow(BuildGroupHeaderCells(g.GroupNumber, ""));
                        tab.AddMergeBar(noteRow, g.DisplayColor, endCol: MergeBarLastCol);
                        lastHeaderGroup = g;
                    }
                }

                int tripRow = tab.AddRow(BuildTripCells(line));
                if (line.ReroutedOnModivcare)
                    tab.FillRow(tripRow, ScheduleBuilderPreviewStyle.ReroutedTripBackColor);
                sawTripRow = true;
            }
        }

        private static SupeyTripCluster FindGroupByNumber(IList<SupeyTripCluster> groups, int groupNumber)
        {
            if (groups == null || groupNumber <= 0)
                return null;

            foreach (var g in groups)
            {
                if (g != null && g.GroupNumber == groupNumber)
                    return g;
            }

            return null;
        }

        private static void AppendReservesContent(WorkbookTab tab, IList<ScheduleBuilderPreviewLine> lines, Options options)
        {
            foreach (var line in lines)
            {
                if (line == null)
                    continue;

                if (line.Kind == ScheduleBuilderPreviewLine.LineKind.SectionHeader)
                {
                    if (!options.IncludeReserveSections
                        || string.IsNullOrWhiteSpace(line.SectionTitle))
                        continue;

                    int row = tab.AddRow(BuildNoteCells(line.SectionTitle.Trim()));
                    Color sectionColor = line.ReserveBandColor
                        ?? ScheduleBuilderReserveBuckets.SectionColorForTitle(line.SectionTitle);
                    tab.AddMergeBar(row, sectionColor);
                    continue;
                }

                if (line.Kind == ScheduleBuilderPreviewLine.LineKind.Trip && line.Trip != null)
                {
                    int tripRow = tab.AddRow(BuildTripCells(line));
                    if (line.ReroutedOnModivcare)
                        tab.FillRow(tripRow, ScheduleBuilderPreviewStyle.ReroutedTripBackColor);
                }
            }
        }

        private static string[] BuildNoteCells(string colAText)
        {
            var cells = EmptyCells();
            cells[0] = colAText ?? "";
            return cells;
        }

        private static string[] BuildGroupHeaderCells(int groupNumber, string noteText)
        {
            var cells = EmptyWorkbookRow();
            cells[0] = noteText ?? "";
            cells[WorkbookMetaColumnIndex] = ScheduleBuilderGroupHeaderMeta.Encode(groupNumber);
            return cells;
        }

        private static string[] BuildGapCells()
        {
            var cells = EmptyWorkbookRow();
            cells[WorkbookMetaColumnIndex] = ScheduleBuilderGapMeta.Marker;
            return cells;
        }

        private static string[] EmptyWorkbookRow()
        {
            var cells = new string[WorkbookExportColumnCount];
            for (int i = 0; i < WorkbookExportColumnCount; i++)
                cells[i] = "";
            return cells;
        }

        private static string[] BuildTripCells(ScheduleBuilderPreviewLine line)
        {
            if (line?.Trip == null)
                return EmptyCells();

            var cells = BuildTripCells(line.Trip);
            if (line.ReroutedOnModivcare)
                cells[WorkbookMetaColumnIndex] = ScheduleBuilderRerouteMeta.Encode();
            return cells;
        }

        private static string[] BuildTripCells(MCDownloadedTrip trip)
        {
            if (trip == null)
                return EmptyCells();

            var cells = EmptyWorkbookRow();
            cells[0] = trip.TripNumber ?? "";
            cells[1] = SupeyTripTimes.FormatDateForSchedule(trip.Date);
            cells[2] = trip.ClientFullName ?? "";
            cells[3] = trip.PUStreet ?? "";
            cells[4] = trip.PUCity ?? "";
            cells[5] = trip.PUTelephone ?? "";
            // Always write Modivcare 24h times so save/email never stick "12:00 AM" in the file.
            cells[6] = SupeyTripTimes.FormatForSchedule(trip.PUTime);
            cells[7] = trip.DOStreet ?? "";
            cells[8] = trip.DOCITY ?? "";
            cells[9] = trip.DOTelephone ?? "";
            cells[10] = SupeyTripTimes.FormatForSchedule(trip.DOTime);
            cells[11] = trip.Age ?? "";
            cells[12] = trip.Miles ?? "";
            cells[13] = trip.Comments ?? "";
            return cells;
        }

        private static string[] EmptyCells()
        {
            var cells = new string[ColumnCount];
            for (int i = 0; i < ColumnCount; i++)
                cells[i] = "";
            return cells;
        }

        private static string FormatCsvRow(IReadOnlyList<string> cells)
        {
            int width = WorkbookExportColumnCount;
            var parts = new string[width];
            for (int i = 0; i < width; i++)
                parts[i] = i < cells?.Count ? (cells[i] ?? "") : "";

            var quoted = new string[width];
            for (int i = 0; i < width; i++)
                quoted[i] = "\"" + Escape(parts[i]) + "\"";

            return string.Join(",", quoted);
        }

        private static string Escape(string value) => (value ?? "").Replace("\"", "\"\"");
    }
}
