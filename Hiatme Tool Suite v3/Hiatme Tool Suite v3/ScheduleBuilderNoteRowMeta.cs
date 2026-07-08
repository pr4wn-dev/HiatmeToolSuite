using System;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Hidden column-O markers for user note rows (gap / dispatcher notes).</summary>
    internal static class ScheduleBuilderNoteRowMeta
    {
        public const string GapMarker = "__FSNOTE";
        public const string CenterSuffix = ":C";

        public static string EncodeGapNote(bool centerText) =>
            centerText ? GapMarker + CenterSuffix : GapMarker;

        public static bool TryParseGapNoteMeta(string columnO, out bool centerText)
        {
            centerText = false;
            string value = (columnO ?? "").Trim();
            if (!value.StartsWith(GapMarker, StringComparison.Ordinal))
                return false;

            centerText = value.EndsWith(CenterSuffix, StringComparison.Ordinal);
            return true;
        }

        public static string StripCenterSuffix(string meta, out bool centerText)
        {
            centerText = false;
            string value = (meta ?? "").Trim();
            if (value.EndsWith(CenterSuffix, StringComparison.Ordinal))
            {
                centerText = true;
                return value.Substring(0, value.Length - CenterSuffix.Length);
            }

            return value;
        }
    }
}
