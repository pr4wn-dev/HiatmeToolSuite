using System;
using System.Collections.Generic;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Group header notes on driver preview tabs — stored as lines and exported as merged workbook cells.</summary>
    internal static class ScheduleBuilderGroupNotes
    {
        public static void ApplyNote(
            IList<ScheduleBuilderPreviewLine> lines,
            IList<SupeyTripCluster> groups,
            SupeyTripCluster group,
            string noteText)
        {
            if (lines == null || group == null)
                return;

            noteText = noteText?.Trim() ?? "";
            int groupNumber = group.GroupNumber;
            int firstTripIdx = FindFirstTripLineIndex(lines, groups, groupNumber);
            if (firstTripIdx < 0)
                return;

            int headerIdx = firstTripIdx - 1;
            bool hasHeader = headerIdx >= 0
                && lines[headerIdx].Kind == ScheduleBuilderPreviewLine.LineKind.GroupHeader
                && lines[headerIdx].GroupNumber == groupNumber;

            if (string.IsNullOrEmpty(noteText))
            {
                if (hasHeader)
                    lines.RemoveAt(headerIdx);
                return;
            }

            if (hasHeader)
            {
                lines[headerIdx].GroupNoteText = noteText;
                return;
            }

            lines.Insert(firstTripIdx, new ScheduleBuilderPreviewLine
            {
                Kind = ScheduleBuilderPreviewLine.LineKind.GroupHeader,
                GroupNumber = groupNumber,
                GroupNoteText = noteText,
            });
        }

        public static string GetNote(
            IList<ScheduleBuilderPreviewLine> lines,
            int groupNumber)
        {
            if (lines == null)
                return "";

            foreach (var line in lines)
            {
                if (line?.Kind != ScheduleBuilderPreviewLine.LineKind.GroupHeader)
                    continue;
                if (line.GroupNumber == groupNumber)
                    return line.GroupNoteText ?? "";
            }

            return "";
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
