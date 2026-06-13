using System;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Hidden column-O marker on trip rows rerouted to Modivcare (survives save/load).</summary>
    internal static class ScheduleBuilderRerouteMeta
    {
        public const string Marker = "__FSRR";

        public static string Encode() => Marker;

        public static bool IsMarker(string value) =>
            string.Equals((value ?? "").Trim(), Marker, StringComparison.Ordinal);

        public static bool RowIsRerouted(string[] rowValues)
        {
            if (rowValues == null || rowValues.Length == 0)
                return false;

            if (rowValues.Length > ScheduleBuilderPreviewCsvExport.WorkbookMetaColumnIndex
                && IsMarker(rowValues[ScheduleBuilderPreviewCsvExport.WorkbookMetaColumnIndex]))
                return true;

            if (rowValues.Length >= ScheduleBuilderPreviewCsvExport.ColumnCount
                && IsMarker(rowValues[ScheduleBuilderPreviewCsvExport.ColumnCount - 1]))
                return true;

            return false;
        }
    }
}
