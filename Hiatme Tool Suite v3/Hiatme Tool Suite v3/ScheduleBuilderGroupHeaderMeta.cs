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
    }
}
