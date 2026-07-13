using System;
using System.Drawing;
using System.Globalization;
using System.Text;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Hidden column-O markers for user note rows (gap / dispatcher notes).</summary>
    internal static class ScheduleBuilderNoteRowMeta
    {
        public const string GapMarker = "__FSNOTE";
        public const string CenterSuffix = ":C";
        public const string TextColorPrefix = ":T";
        public const string RowColorPrefix = ":R";

        public static string EncodeGapNote(
            bool centerText,
            Color? textColor = null,
            Color? rowColor = null) =>
            GapMarker + EncodeOptions(centerText, textColor, rowColor);

        public static string EncodeOptions(bool centerText, Color? textColor, Color? rowColor = null)
        {
            var sb = new StringBuilder();
            if (centerText)
                sb.Append(CenterSuffix);
            if (rowColor.HasValue)
            {
                sb.Append(RowColorPrefix);
                sb.Append(ColorToRgbHex(rowColor.Value));
            }
            if (textColor.HasValue)
            {
                sb.Append(TextColorPrefix);
                sb.Append(ColorToRgbHex(textColor.Value));
            }
            return sb.ToString();
        }

        public static bool TryParseGapNoteMeta(string columnO, out bool centerText)
            => TryParseGapNoteMeta(columnO, out centerText, out _, out _);

        public static bool TryParseGapNoteMeta(string columnO, out bool centerText, out Color? textColor)
            => TryParseGapNoteMeta(columnO, out centerText, out textColor, out _);

        public static bool TryParseGapNoteMeta(
            string columnO,
            out bool centerText,
            out Color? textColor,
            out Color? rowColor)
        {
            centerText = false;
            textColor = null;
            rowColor = null;
            string value = (columnO ?? "").Trim();
            if (!value.StartsWith(GapMarker, StringComparison.Ordinal))
                return false;

            StripOptions(value.Substring(GapMarker.Length), out centerText, out textColor, out rowColor);
            return true;
        }

        /// <summary>Strip :C / :Rrrggbb / :Trrggbb option suffixes; returns the base marker/id part.</summary>
        public static string StripOptions(string meta, out bool centerText, out Color? textColor)
            => StripOptions(meta, out centerText, out textColor, out _);

        public static string StripOptions(
            string meta,
            out bool centerText,
            out Color? textColor,
            out Color? rowColor)
        {
            centerText = false;
            textColor = null;
            rowColor = null;
            string value = (meta ?? "").Trim();
            if (value.Length == 0)
                return value;

            value = StripColorOption(value, TextColorPrefix, out textColor);
            value = StripColorOption(value, RowColorPrefix, out rowColor);

            if (value.EndsWith(CenterSuffix, StringComparison.Ordinal))
            {
                centerText = true;
                value = value.Substring(0, value.Length - CenterSuffix.Length);
            }

            return value.Trim();
        }

        private static string StripColorOption(string value, string prefix, out Color? color)
        {
            color = null;
            int idx = value.LastIndexOf(prefix, StringComparison.Ordinal);
            if (idx < 0 || idx + prefix.Length + 6 > value.Length)
                return value;

            string hex = value.Substring(idx + prefix.Length, 6);
            if (!TryParseRgbHex(hex, out Color parsed))
                return value;

            color = parsed;
            return value.Substring(0, idx)
                + (idx + prefix.Length + 6 < value.Length
                    ? value.Substring(idx + prefix.Length + 6)
                    : "");
        }

        /// <summary>Legacy helper — center only.</summary>
        public static string StripCenterSuffix(string meta, out bool centerText)
        {
            string baseMeta = StripOptions(meta, out centerText, out _, out _);
            return baseMeta;
        }

        public static string ColorToRgbHex(Color color) =>
            string.Format(CultureInfo.InvariantCulture, "{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);

        public static bool TryParseRgbHex(string hex, out Color color)
        {
            color = Color.Empty;
            if (string.IsNullOrWhiteSpace(hex) || hex.Length != 6)
                return false;

            if (!int.TryParse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int r)
                || !int.TryParse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int g)
                || !int.TryParse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int b))
            {
                return false;
            }

            color = Color.FromArgb(255, r, g, b);
            return true;
        }
    }
}
