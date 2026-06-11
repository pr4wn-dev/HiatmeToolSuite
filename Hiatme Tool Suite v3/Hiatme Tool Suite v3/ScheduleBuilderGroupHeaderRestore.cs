using System;
using System.Collections.Generic;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Rebuild group header lines when loading schedules saved before column-N metadata.
    /// </summary>
    internal static class ScheduleBuilderGroupHeaderRestore
    {
        public static List<ScheduleBuilderPreviewLine> ApplyLegacyRestoration(
            IList<ScheduleBuilderPreviewLine> lines)
        {
            if (lines == null || lines.Count == 0)
                return lines == null ? new List<ScheduleBuilderPreviewLine>() : new List<ScheduleBuilderPreviewLine>(lines);

            foreach (var line in lines)
            {
                if (line?.Kind == ScheduleBuilderPreviewLine.LineKind.GroupHeader)
                    return new List<ScheduleBuilderPreviewLine>(lines);
            }

            var groups = ScheduleBuilderPreviewGroups.BuildFromPreviewLines(lines);
            var result = new List<ScheduleBuilderPreviewLine>(lines.Count);

            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (line == null)
                    continue;

                if (line.Kind != ScheduleBuilderPreviewLine.LineKind.Gap)
                {
                    result.Add(line);
                    continue;
                }

                string note = (line.GapNoteText ?? "").Trim();
                if (note.Length == 0)
                {
                    result.Add(line);
                    continue;
                }

                int nextTripIdx = FindNextTripIndex(lines, i + 1);
                if (nextTripIdx < 0)
                {
                    result.Add(line);
                    continue;
                }

                var nextGroup = ScheduleBuilderPreviewGroups.FindGroupForTrip(
                    groups,
                    lines[nextTripIdx].Trip);
                if (nextGroup == null)
                {
                    result.Add(line);
                    continue;
                }

                int prevTripIdx = FindPrevTripIndex(lines, i - 1);
                var prevGroup = prevTripIdx >= 0
                    ? ScheduleBuilderPreviewGroups.FindGroupForTrip(groups, lines[prevTripIdx].Trip)
                    : null;

                if (prevGroup != null && prevGroup.GroupNumber == nextGroup.GroupNumber)
                {
                    result.Add(line);
                    continue;
                }

                result.Add(new ScheduleBuilderPreviewLine
                {
                    Kind = ScheduleBuilderPreviewLine.LineKind.GroupHeader,
                    GroupNumber = nextGroup.GroupNumber,
                    GroupNoteText = note,
                });
            }

            return result;
        }

        private static int FindNextTripIndex(IList<ScheduleBuilderPreviewLine> lines, int start)
        {
            for (int i = start; i < lines.Count; i++)
            {
                var line = lines[i];
                if (line?.Kind == ScheduleBuilderPreviewLine.LineKind.Trip && line.Trip != null)
                    return i;
            }

            return -1;
        }

        private static int FindPrevTripIndex(IList<ScheduleBuilderPreviewLine> lines, int start)
        {
            for (int i = start; i >= 0; i--)
            {
                var line = lines[i];
                if (line?.Kind == ScheduleBuilderPreviewLine.LineKind.Trip && line.Trip != null)
                    return i;
            }

            return -1;
        }
    }
}
