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

        public sealed class Options
        {
            public bool IncludeGaps { get; set; }
            public bool IncludeGroupHeaders { get; set; }
            public bool IncludeReserveSections { get; set; } = true;

            public static Options TripsOnly => new Options();
        }

        public sealed class WorkbookTab
        {
            public string TabName { get; set; }
            public List<List<string>> Rows { get; set; } = new List<List<string>>();
            public Dictionary<(int Row, int Col), Color> CellFills { get; set; }
                = new Dictionary<(int Row, int Col), Color>();

            public int AddRow(string[] cells)
            {
                var row = new List<string>(ColumnCount);
                for (int i = 0; i < ColumnCount; i++)
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
        }

        public static IReadOnlyList<WorkbookTab> BuildWorkbookTabs(
            IReadOnlyDictionary<string, List<ScheduleBuilderPreviewLine>> linesByTab,
            Options options)
        {
            if (linesByTab == null || linesByTab.Count == 0)
                return Array.Empty<WorkbookTab>();

            options = options ?? Options.TripsOnly;
            var tabs = new List<WorkbookTab>();
            foreach (var kv in linesByTab.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(kv.Key))
                    continue;

                bool reserves = kv.Key.Equals("Reserves", StringComparison.OrdinalIgnoreCase);
                var lines = ScheduleBuilderTemplateSlots.CollapseConsecutivePreviewGaps(
                    kv.Value ?? new List<ScheduleBuilderPreviewLine>());
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
            lines = ScheduleBuilderTemplateSlots.CollapseConsecutivePreviewGaps(
                lines ?? Array.Empty<ScheduleBuilderPreviewLine>());

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

            foreach (var line in lines)
            {
                if (line == null)
                    continue;

                if (line.Kind == ScheduleBuilderPreviewLine.LineKind.Gap)
                {
                    lastHeaderGroup = null;
                    if (!options.IncludeGaps)
                        continue;

                    tab.AddRow(EmptyCells());
                    continue;
                }

                if (line.Kind != ScheduleBuilderPreviewLine.LineKind.Trip || line.Trip == null)
                    continue;

                if (options.IncludeGroupHeaders && groups != null)
                {
                    var g = ScheduleBuilderPreviewGroups.FindGroupForTrip(groups, line.Trip);
                    if (g != null && !ReferenceEquals(g, lastHeaderGroup))
                    {
                        int noteRow = tab.AddRow(EmptyCells());
                        Color swatch = g.DisplayColor;
                        Color header = ScheduleBuilderPreviewStyle.RouteHeaderBackColor(swatch);
                        tab.FillCell(noteRow, 0, swatch);
                        for (int c = 1; c < ColumnCount; c++)
                            tab.FillCell(noteRow, c, header);
                        lastHeaderGroup = g;
                    }
                }

                tab.AddRow(BuildTripCells(line.Trip));
            }
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
                    tab.FillRow(row, SupeyTheme.SurfaceHeader);
                    continue;
                }

                if (line.Kind == ScheduleBuilderPreviewLine.LineKind.Trip && line.Trip != null)
                {
                    int row = tab.AddRow(BuildTripCells(line.Trip));
                    if (line.ReserveBandColor.HasValue)
                        tab.FillCell(row, 0, line.ReserveBandColor.Value);
                }
            }
        }

        private static string[] BuildNoteCells(string colAText)
        {
            var cells = EmptyCells();
            cells[0] = colAText ?? "";
            return cells;
        }

        private static string[] BuildTripCells(MCDownloadedTrip trip)
        {
            if (trip == null)
                return EmptyCells();

            return new[]
            {
                trip.TripNumber ?? "",
                trip.Date ?? "",
                trip.ClientFullName ?? "",
                trip.PUStreet ?? "",
                trip.PUCity ?? "",
                trip.PUTelephone ?? "",
                trip.PUTime ?? "",
                trip.DOStreet ?? "",
                trip.DOCITY ?? "",
                trip.DOTelephone ?? "",
                trip.DOTime ?? "",
                trip.Age ?? "",
                trip.Miles ?? "",
                trip.Comments ?? "",
            };
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
            var parts = new string[ColumnCount];
            for (int i = 0; i < ColumnCount; i++)
                parts[i] = i < cells?.Count ? (cells[i] ?? "") : "";
            return string.Format(
                "\"{0}\",\"{1}\",\"{2}\",\"{3}\",\"{4}\",\"{5}\",\"{6}\",\"{7}\",\"{8}\",\"{9}\",\"{10}\",\"{11}\",\"{12}\",\"{13}\"",
                Escape(parts[0]), Escape(parts[1]), Escape(parts[2]), Escape(parts[3]),
                Escape(parts[4]), Escape(parts[5]), Escape(parts[6]), Escape(parts[7]),
                Escape(parts[8]), Escape(parts[9]), Escape(parts[10]), Escape(parts[11]),
                Escape(parts[12]), Escape(parts[13]));
        }

        private static string Escape(string value) => (value ?? "").Replace("\"", "\"\"");
    }
}
