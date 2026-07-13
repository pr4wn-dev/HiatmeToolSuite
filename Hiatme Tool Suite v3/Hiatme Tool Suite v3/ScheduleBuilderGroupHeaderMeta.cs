using System;
using System.Drawing;
using System.Globalization;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Column-O marker on exported group header rows (merged A–N — only col A is visible).
    /// Must use XML-safe text (no control characters) so xlsx shared strings validate.
    /// </summary>
    internal static class ScheduleBuilderGroupHeaderMeta
    {
        private const string Prefix = "__FSGH:";
        private const char LegacyPrefix = '\u001E';

        public static string Encode(
            int groupNumber,
            bool centerText = false,
            Color? textColor = null,
            Color? rowColor = null)
        {
            if (groupNumber <= 0)
                return "";
            return Prefix
                + groupNumber.ToString(CultureInfo.InvariantCulture)
                + ScheduleBuilderNoteRowMeta.EncodeOptions(centerText, textColor, rowColor);
        }

        public static bool TryDecode(string columnN, out int groupNumber)
            => TryDecode(columnN, out groupNumber, out _, out _, out _);

        public static bool TryDecode(string columnN, out int groupNumber, out bool centerText)
            => TryDecode(columnN, out groupNumber, out centerText, out _, out _);

        public static bool TryDecode(
            string columnN,
            out int groupNumber,
            out bool centerText,
            out Color? textColor)
            => TryDecode(columnN, out groupNumber, out centerText, out textColor, out _);

        public static bool TryDecode(
            string columnN,
            out int groupNumber,
            out bool centerText,
            out Color? textColor,
            out Color? rowColor)
        {
            groupNumber = 0;
            centerText = false;
            textColor = null;
            rowColor = null;
            columnN = ScheduleBuilderNoteRowMeta.StripOptions(columnN, out centerText, out textColor, out rowColor);
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

        public static bool TryDecodeRow(string[] rowValues, out int groupNumber, out string noteText)
            => TryDecodeRow(rowValues, out groupNumber, out noteText, out _, out _, out _);

        public static bool TryDecodeRow(
            string[] rowValues,
            out int groupNumber,
            out string noteText,
            out bool centerText)
            => TryDecodeRow(rowValues, out groupNumber, out noteText, out centerText, out _, out _);

        public static bool TryDecodeRow(
            string[] rowValues,
            out int groupNumber,
            out string noteText,
            out bool centerText,
            out Color? textColor)
            => TryDecodeRow(rowValues, out groupNumber, out noteText, out centerText, out textColor, out _);

        public static bool TryDecodeRow(
            string[] rowValues,
            out int groupNumber,
            out string noteText,
            out bool centerText,
            out Color? textColor,
            out Color? rowColor)
        {
            groupNumber = 0;
            noteText = "";
            centerText = false;
            textColor = null;
            rowColor = null;
            if (rowValues == null || rowValues.Length == 0)
                return false;

            string colA = (rowValues[0] ?? "").Trim();
            string colN = rowValues.Length >= ScheduleBuilderPreviewCsvExport.ColumnCount
                ? (rowValues[ScheduleBuilderPreviewCsvExport.ColumnCount - 1] ?? "").Trim()
                : "";
            string colO = rowValues.Length > ScheduleBuilderPreviewCsvExport.WorkbookMetaColumnIndex
                ? (rowValues[ScheduleBuilderPreviewCsvExport.WorkbookMetaColumnIndex] ?? "").Trim()
                : "";

            if (!TryDecode(colO, out groupNumber, out centerText, out textColor, out rowColor)
                && !TryDecode(colN, out groupNumber, out centerText, out textColor, out rowColor)
                && !TryDecode(colA, out groupNumber, out centerText, out textColor, out rowColor))
                return false;

            noteText = colA;
            if (TryDecode(colA, out _))
                noteText = "";
            return true;
        }
    }
}
