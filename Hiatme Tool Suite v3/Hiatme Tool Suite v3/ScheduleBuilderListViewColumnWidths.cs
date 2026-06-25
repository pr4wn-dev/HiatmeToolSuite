using System;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Maps Schedule Builder trip-list column pixels to Excel trip-grid widths (A–N) and back.
    /// </summary>
    internal static class ScheduleBuilderListViewColumnWidths
    {
        /// <summary>Default trip-list column widths (Grp through Comments), matching <c>ConfigureFsTripsListViewColumns</c>.</summary>
        public static readonly int[] DefaultTripsListViewColumnWidthsPx =
        {
            34, 72, 68, 82, 72, 92, 58, 72, 92, 58, 42, 130,
        };

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

        /// <summary>Trips ListView column indices for PU / DO time.</summary>
        public static bool IsTripsListTimeColumn(int listViewColumnIndex)
            => listViewColumnIndex == 4 || listViewColumnIndex == 7;

        /// <summary>Workbook trip-grid column indices for PU (G) / DO (K) time.</summary>
        public static bool IsWorkbookTimeColumn(int excelColumnIndex)
            => excelColumnIndex == 6 || excelColumnIndex == 10;

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

        /// <summary>Current pixel width of every trips ListView column (null if list missing).</summary>
        public static int[] CaptureListViewColumnPixels(ListView lv)
        {
            if (lv == null || lv.Columns.Count == 0)
                return null;

            var widths = new int[lv.Columns.Count];
            for (int i = 0; i < widths.Length; i++)
                widths[i] = lv.Columns[i].Width;
            return widths;
        }

        /// <summary>Pin all trip-list columns to shared pixel widths (same on every driver tab).</summary>
        public static void PinTripsListViewWidths(ListView lv, int[] widthsPx)
        {
            if (lv == null || widthsPx == null || widthsPx.Length == 0)
                return;

            ListViewMinWidthEnforcer.PinColumnWidths(lv, widthsPx);
        }

        /// <summary>Apply saved workbook column widths to the trips ListView and pin them globally.</summary>
        public static int[] ApplyToTripsListView(ListView lv, double[] excelWidthsAtoN)
        {
            if (lv == null || excelWidthsAtoN == null || excelWidthsAtoN.Length == 0)
                return null;

            int[] px = CaptureListViewColumnPixels(lv);
            if (px == null)
            {
                px = (int[])DefaultTripsListViewColumnWidthsPx.Clone();
            }

            foreach (var map in TripListToExcel)
            {
                if (map.ListViewIndex >= px.Length || map.ExcelIndex >= excelWidthsAtoN.Length)
                    continue;

                double excel = excelWidthsAtoN[map.ExcelIndex];
                if (excel <= 0)
                    continue;

                int mappedPx = ScheduleBuilderXlsxWriter.ExcelColumnWidthToPixels(excel);
                if (mappedPx <= 0)
                    continue;

                px[map.ListViewIndex] = mappedPx;
            }

            PinTripsListViewWidths(lv, px);
            return px;
        }
    }
}
