using System;
using System.Collections.Generic;
using System.Drawing;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Per-group color overrides on driver preview tabs (stored on group header lines).</summary>
    internal static class ScheduleBuilderGroupColors
    {
        public static void ApplyOverridesFromLines(
            IList<ScheduleBuilderPreviewLine> lines,
            IList<SupeyTripCluster> groups)
        {
            if (lines == null || groups == null)
                return;

            foreach (var line in lines)
            {
                if (line?.Kind != ScheduleBuilderPreviewLine.LineKind.GroupHeader
                    || !line.GroupColorOverride.HasValue)
                    continue;

                Color color = line.GroupColorOverride.Value;
                foreach (var g in groups)
                {
                    if (g != null && g.GroupNumber == line.GroupNumber)
                        g.GroupColor = color;
                }
            }
        }

        public static Color? GetOverride(IList<ScheduleBuilderPreviewLine> lines, int groupNumber)
        {
            if (lines == null)
                return null;

            foreach (var line in lines)
            {
                if (line?.Kind != ScheduleBuilderPreviewLine.LineKind.GroupHeader)
                    continue;
                if (line.GroupNumber == groupNumber)
                    return line.GroupColorOverride;
            }

            return null;
        }

        /// <param name="color">Null clears the override and removes an empty header line.</param>
        public static void ApplyColor(
            IList<ScheduleBuilderPreviewLine> lines,
            IList<SupeyTripCluster> groups,
            SupeyTripCluster group,
            Color? color)
        {
            if (lines == null || group == null)
                return;

            int groupNumber = group.GroupNumber;
            int firstTripIdx = FindFirstTripLineIndex(lines, groups, groupNumber);
            if (firstTripIdx < 0)
                return;

            int headerIdx = firstTripIdx - 1;
            bool hasHeader = headerIdx >= 0
                && lines[headerIdx].Kind == ScheduleBuilderPreviewLine.LineKind.GroupHeader
                && lines[headerIdx].GroupNumber == groupNumber;

            if (!color.HasValue)
            {
                if (hasHeader)
                {
                    lines[headerIdx].GroupColorOverride = null;
                    if (string.IsNullOrWhiteSpace(lines[headerIdx].GroupNoteText))
                        lines.RemoveAt(headerIdx);
                }
                return;
            }

            if (hasHeader)
            {
                lines[headerIdx].GroupColorOverride = color;
                return;
            }

            lines.Insert(firstTripIdx, new ScheduleBuilderPreviewLine
            {
                Kind = ScheduleBuilderPreviewLine.LineKind.GroupHeader,
                GroupNumber = groupNumber,
                GroupColorOverride = color,
            });
        }

        private static int FindFirstTripLineIndex(
            IList<ScheduleBuilderPreviewLine> lines,
            IList<SupeyTripCluster> groups,
            int groupNumber)
        {
            if (lines == null || groups == null)
                return -1;

            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (line?.Kind != ScheduleBuilderPreviewLine.LineKind.Trip || line.Trip == null)
                    continue;

                var g = ScheduleBuilderPreviewGroups.FindGroupForTrip(groups, line.Trip);
                if (g != null && g.GroupNumber == groupNumber)
                    return i;
            }

            return -1;
        }
    }
}
