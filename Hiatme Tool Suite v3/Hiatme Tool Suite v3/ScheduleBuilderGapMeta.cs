using System;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Legacy gap markers in old saved files only — new saves use plain empty rows.</summary>
    internal static class ScheduleBuilderGapMeta
    {
        public const string Marker = "__FSGAP";

        public static bool IsMarker(string value) =>
            string.Equals((value ?? "").Trim(), Marker, StringComparison.Ordinal);

        public static bool RowHasGapMarker(string[] rowValues)
        {
            if (rowValues == null || rowValues.Length == 0)
                return false;

            if (rowValues.Length > ScheduleBuilderPreviewCsvExport.WorkbookMetaColumnIndex
                && IsMarker(rowValues[ScheduleBuilderPreviewCsvExport.WorkbookMetaColumnIndex]))
                return true;

            if (rowValues.Length >= ScheduleBuilderPreviewCsvExport.ColumnCount
                && IsMarker(rowValues[ScheduleBuilderPreviewCsvExport.ColumnCount - 1]))
                return true;

            return IsMarker(rowValues[0]);
        }
    }
}
