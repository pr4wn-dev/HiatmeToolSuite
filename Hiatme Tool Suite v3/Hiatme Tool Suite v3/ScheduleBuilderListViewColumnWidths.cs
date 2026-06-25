using System;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Maps Schedule Builder trip-list column pixels to Excel trip-grid widths (A–N) and back.
    /// </summary>
    internal static class ScheduleBuilderListViewColumnWidths
    {
        private sealed class ColumnMap
        {
            public int ListViewIndex { get; set; }
            public int ExcelIndex { get; set; }
        }

        /// <summary>ListView trip columns that correspond to workbook columns A–N (excludes Grp).</summary>
        private static readonly ColumnMap[] TripListToExcel =
        {
            new ColumnMap { ListViewIndex = 1, ExcelIndex = 0 },  // A Trip #
            new ColumnMap { ListViewIndex = 2, ExcelIndex = 1 },  // B Date
            new ColumnMap { ListViewIndex = 3, ExcelIndex = 2 },  // C Client
            new ColumnMap { ListViewIndex = 5, ExcelIndex = 3 },  // D PU street
            new ColumnMap { ListViewIndex = 6, ExcelIndex = 4 },  // E PU city
            new ColumnMap { ListViewIndex = 4, ExcelIndex = 6 },  // G PU time
            new ColumnMap { ListViewIndex = 8, ExcelIndex = 7 },  // H DO street
            new ColumnMap { ListViewIndex = 9, ExcelIndex = 8 },  // I DO city
            new ColumnMap { ListViewIndex = 7, ExcelIndex = 10 }, // K DO time
            new ColumnMap { ListViewIndex = 10, ExcelIndex = 12 }, // M Miles
            new ColumnMap { ListViewIndex = 11, ExcelIndex = 13 }, // N Comments
        };

        /// <summary>Excel A–N widths in character units from the trips ListView (null if list missing).</summary>
        public static double[] CaptureFromTripsListView(ListView lv)
        {
            if (lv == null || lv.Columns.Count == 0)
                return null;

            var widths = new double[ScheduleBuilderPreviewCsvExport.ColumnCount];
            bool any = false;

            foreach (var map in TripListToExcel)
            {
                if (map.ListViewIndex >= lv.Columns.Count)
                    continue;

                int px = lv.Columns[map.ListViewIndex].Width;
                if (px <= 0)
                    continue;

                double excel = ScheduleBuilderXlsxWriter.PixelsToExcelColumnWidth(px);
                if (excel <= 0)
                    continue;

                widths[map.ExcelIndex] = excel;
                any = true;
            }

            return any ? widths : null;
        }

        /// <summary>Apply saved workbook column widths to the trips ListView and keep them through auto-fit.</summary>
        public static void ApplyToTripsListView(ListView lv, double[] excelWidthsAtoN)
        {
            if (lv == null || excelWidthsAtoN == null || excelWidthsAtoN.Length == 0)
                return;

            foreach (var map in TripListToExcel)
            {
                if (map.ListViewIndex >= lv.Columns.Count || map.ExcelIndex >= excelWidthsAtoN.Length)
                    continue;

                double excel = excelWidthsAtoN[map.ExcelIndex];
                if (excel <= 0)
                    continue;

                int px = ScheduleBuilderXlsxWriter.ExcelColumnWidthToPixels(excel);
                if (px <= 0)
                    continue;

                lv.Columns[map.ListViewIndex].Width = px;
                ListViewMinWidthEnforcer.SetColumnFloor(lv, map.ListViewIndex, px);
                ListViewMinWidthEnforcer.SetColumnCeiling(lv, map.ListViewIndex, px);
            }
        }
    }
}
