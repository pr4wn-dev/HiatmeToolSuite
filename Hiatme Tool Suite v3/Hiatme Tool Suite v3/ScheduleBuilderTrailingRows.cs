using System.Collections.Generic;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Workbook-style blank rows always shown at the bottom of each driver tab.</summary>
    internal static class ScheduleBuilderTrailingRows
    {
        public const int RowCount = 12;

        public static bool IsTrailingPad(ScheduleBuilderPreviewLine line)
            => line?.Kind == ScheduleBuilderPreviewLine.LineKind.Gap && line.TrailingPad;

        public static void StripTrailingPads(IList<ScheduleBuilderPreviewLine> lines)
        {
            if (lines == null)
                return;

            while (lines.Count > 0 && IsTrailingPad(lines[lines.Count - 1]))
                lines.RemoveAt(lines.Count - 1);
        }

        public static void EnsureAtEnd(IList<ScheduleBuilderPreviewLine> lines)
        {
            if (lines == null)
                return;

            StripTrailingPads(lines);

            for (int i = 0; i < RowCount; i++)
            {
                lines.Add(new ScheduleBuilderPreviewLine
                {
                    Kind = ScheduleBuilderPreviewLine.LineKind.Gap,
                    TrailingPad = true,
                });
            }
        }
    }
}
