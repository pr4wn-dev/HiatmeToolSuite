using System;
using System.Globalization;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Column-N marker on exported group header rows (merged A–N — only col A is visible).
    /// Must use XML-safe text (no control characters) so xlsx shared strings validate.
    /// </summary>
    internal static class ScheduleBuilderGroupHeaderMeta
    {
        private const string Prefix = "__FSGH:";
        private const char LegacyPrefix = '\u001E';

        public static string Encode(int groupNumber)
        {
            if (groupNumber <= 0)
                return "";
            return Prefix + groupNumber.ToString(CultureInfo.InvariantCulture);
        }

        public static bool TryDecode(string columnN, out int groupNumber)
        {
            groupNumber = 0;
            columnN = (columnN ?? "").Trim();
            if (columnN.Length == 0)
                return false;

            if (columnN.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return int.TryParse(
                    columnN.Substring(Prefix.Length),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out groupNumber)
                    && groupNumber > 0;
            }

            if (columnN.Length >= 3
                && columnN[0] == LegacyPrefix
                && columnN[1] == 'G')
            {
                return int.TryParse(
                    columnN.Substring(2),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out groupNumber)
                    && groupNumber > 0;
            }

            return false;
        }

        /// <summary>Decode group number from hidden column O (legacy: column N or A).</summary>
        public static bool TryDecodeRow(string[] rowValues, out int groupNumber, out string noteText)
        {
            groupNumber = 0;
            noteText = "";
            if (rowValues == null || rowValues.Length == 0)
                return false;

            string colA = (rowValues[0] ?? "").Trim();
            string colN = rowValues.Length >= ScheduleBuilderPreviewCsvExport.ColumnCount
                ? (rowValues[ScheduleBuilderPreviewCsvExport.ColumnCount - 1] ?? "").Trim()
                : "";
            string colO = rowValues.Length > ScheduleBuilderPreviewCsvExport.WorkbookMetaColumnIndex
                ? (rowValues[ScheduleBuilderPreviewCsvExport.WorkbookMetaColumnIndex] ?? "").Trim()
                : "";

            if (!TryDecode(colO, out groupNumber)
                && !TryDecode(colN, out groupNumber)
                && !TryDecode(colA, out groupNumber))
                return false;

            noteText = colA;
            if (TryDecode(colA, out _))
                noteText = "";
            return true;
        }
    }
}
