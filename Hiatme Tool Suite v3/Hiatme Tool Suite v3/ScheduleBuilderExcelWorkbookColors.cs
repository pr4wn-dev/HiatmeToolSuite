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
                mergeRange.Interior.Color = ColorTranslator.ToOle(bar.Color);
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
