using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using Microsoft.Office.Interop.Excel;

namespace Hiatme_Tool_Suite_v3
{
    internal static class ScheduleBuilderExcelWorkbookColors
    {
        public static void ApplyTabColors(
            Workbook workbook,
            IReadOnlyList<ScheduleBuilderPreviewCsvExport.WorkbookTab> tabs)
        {
            if (workbook == null || tabs == null || tabs.Count == 0)
                return;

            foreach (var tab in tabs)
            {
                if (tab == null)
                    continue;
                if ((tab.CellFills == null || tab.CellFills.Count == 0)
                    && (tab.MergeBars == null || tab.MergeBars.Count == 0))
                    continue;

                Worksheet worksheet = null;
                try
                {
                    worksheet = FindWorksheet(workbook, tab.TabName);
                    if (worksheet == null)
                        continue;

                    if (tab.MergeBars != null)
                    {
                        foreach (var bar in tab.MergeBars)
                        {
                            ApplyMergeBar(worksheet, bar);
                        }
                    }

                    if (tab.CellFills == null)
                        continue;

                    foreach (var kv in tab.CellFills)
                    {
                        int row = kv.Key.Row + 1;
                        int col = kv.Key.Col + 1;
                        Range cell = null;
                        try
                        {
                            cell = (Range)worksheet.Cells[row, col];
                            cell.Interior.Color = ColorTranslator.ToOle(kv.Value);
                        }
                        finally
                        {
                            if (cell != null)
                                Marshal.ReleaseComObject(cell);
                        }
                    }
                }
                finally
                {
                    if (worksheet != null)
                        Marshal.ReleaseComObject(worksheet);
                }
            }
        }

        public static void AutoFitAllWorksheets(Workbook workbook)
        {
            if (workbook?.Worksheets == null)
                return;

            int count = workbook.Worksheets.Count;
            for (int i = 1; i <= count; i++)
            {
                Worksheet worksheet = null;
                Range columns = null;
                Range rows = null;
                try
                {
                    worksheet = (Worksheet)workbook.Worksheets[i];
                    columns = worksheet.Columns;
                    columns.AutoFit();
                    rows = worksheet.Rows;
                    rows.AutoFit();
                }
                finally
                {
                    if (rows != null) Marshal.ReleaseComObject(rows);
                    if (columns != null) Marshal.ReleaseComObject(columns);
                    if (worksheet != null) Marshal.ReleaseComObject(worksheet);
                }
            }
        }

        /// <summary>
        /// Set column widths from trip/data rows (merged header rows excluded) so long group notes
        /// do not widen column A after merge bars are applied.
        /// </summary>
        public static void ApplyColumnWidthsFromTabs(
            Workbook workbook,
            IReadOnlyList<ScheduleBuilderPreviewCsvExport.WorkbookTab> tabs,
            double[] preferredColumnWidths = null)
        {
            if (workbook == null || tabs == null || tabs.Count == 0)
                return;

            const int columnCount = ScheduleBuilderPreviewCsvExport.ColumnCount;

            foreach (var tab in tabs)
            {
                if (tab?.Rows == null)
                    continue;

                Worksheet worksheet = null;
                try
                {
                    worksheet = FindWorksheet(workbook, tab.TabName);
                    if (worksheet == null)
                        continue;

                    IEnumerable<int> mergeRows = tab.MergeBars != null
                        ? tab.MergeBars.ConvertAll(b => b.RowIndex)
                        : null;

                    double[] widths = ScheduleBuilderXlsxWriter.ResolveColumnWidths(
                        tab.Rows,
                        columnCount,
                        mergeRows,
                        preferredColumnWidths);

                    for (int c = 0; c < widths.Length; c++)
                    {
                        Range column = null;
                        try
                        {
                            column = (Range)worksheet.Columns[c + 1];
                            column.ColumnWidth = widths[c];
                        }
                        finally
                        {
                            if (column != null)
                                Marshal.ReleaseComObject(column);
                        }
                    }
                }
                finally
                {
                    if (worksheet != null)
                        Marshal.ReleaseComObject(worksheet);
                }
            }
        }

        /// <summary>Right-align PU time (G) and DO time (K) on every worksheet.</summary>
        public static void ApplyTripGridTimeColumnAlignment(Workbook workbook)
        {
            if (workbook?.Worksheets == null)
                return;

            int count = workbook.Worksheets.Count;
            for (int i = 1; i <= count; i++)
            {
                Worksheet worksheet = null;
                try
                {
                    worksheet = (Worksheet)workbook.Worksheets[i];
                    AlignWorksheetColumnRight(worksheet, 7);  // G PU time
                    AlignWorksheetColumnRight(worksheet, 11); // K DO time
                }
                finally
                {
                    if (worksheet != null)
                        Marshal.ReleaseComObject(worksheet);
                }
            }
        }

        private static void AlignWorksheetColumnRight(Worksheet worksheet, int columnIndex1Based)
        {
            Range column = null;
            try
            {
                column = (Range)worksheet.Columns[columnIndex1Based];
                column.HorizontalAlignment = XlHAlign.xlHAlignRight;
            }
            finally
            {
                if (column != null)
                    Marshal.ReleaseComObject(column);
            }
        }

        private static void ApplyMergeBar(
            Worksheet worksheet,
            ScheduleBuilderPreviewCsvExport.WorkbookTab.RowMergeBar bar)
        {
            if (worksheet == null || bar == null)
                return;

            int row = bar.RowIndex + 1;
            int startCol = bar.StartCol + 1;
            int endCol = bar.EndCol + 1;
            Range startCell = null;
            Range endCell = null;
            Range mergeRange = null;
            try
            {
                startCell = (Range)worksheet.Cells[row, startCol];
                endCell = (Range)worksheet.Cells[row, endCol];
                mergeRange = worksheet.get_Range(startCell, endCell);
                mergeRange.Merge();
                if (bar.Color != Color.Empty)
                    mergeRange.Interior.Color = ColorTranslator.ToOle(bar.Color);
                if (bar.CenterText)
                    mergeRange.HorizontalAlignment = XlHAlign.xlHAlignCenter;
            }
            finally
            {
                if (mergeRange != null) Marshal.ReleaseComObject(mergeRange);
                if (endCell != null) Marshal.ReleaseComObject(endCell);
                if (startCell != null) Marshal.ReleaseComObject(startCell);
            }
        }

        private static Worksheet FindWorksheet(Workbook workbook, string tabName)
        {
            if (workbook?.Worksheets == null || string.IsNullOrWhiteSpace(tabName))
                return null;

            foreach (Worksheet ws in workbook.Worksheets)
            {
                try
                {
                    if (string.Equals(ws.Name, tabName, StringComparison.OrdinalIgnoreCase))
                        return ws;
                }
                finally
                {
                    if (!string.Equals(ws.Name, tabName, StringComparison.OrdinalIgnoreCase))
                        Marshal.ReleaseComObject(ws);
                }
            }

            return null;
        }
    }
}
