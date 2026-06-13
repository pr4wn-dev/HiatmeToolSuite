using System;
using System.Collections.Generic;
using System.Drawing;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Keeps group header notes aligned with trip segments (gaps / headers delimit groups).
    /// Notes stay with their segment position — they are not moved when trips join another group.
    /// </summary>
    internal static class ScheduleBuilderGroupHeaderReconcile
    {
        public static List<ScheduleBuilderPreviewLine> Reconcile(IList<ScheduleBuilderPreviewLine> lines)
        {
            if (lines == null || lines.Count == 0)
                return lines == null ? new List<ScheduleBuilderPreviewLine>() : new List<ScheduleBuilderPreviewLine>();

            var result = new List<ScheduleBuilderPreviewLine>(lines.Count);
            var segmentTrips = new List<ScheduleBuilderPreviewLine>();
            string pendingNote = null;
            Color? pendingColorOverride = null;
            int groupNumber = 0;

            void FlushSegment()
            {
                if (segmentTrips.Count == 0)
                {
                    pendingNote = null;
                    pendingColorOverride = null;
                    return;
                }

                groupNumber++;
                string note = (pendingNote ?? "").Trim();
                if (note.Length > 0 || pendingColorOverride.HasValue)
                {
                    result.Add(new ScheduleBuilderPreviewLine
                    {
                        Kind = ScheduleBuilderPreviewLine.LineKind.GroupHeader,
                        GroupNumber = groupNumber,
                        GroupNoteText = note,
                        GroupColorOverride = pendingColorOverride,
                    });
                }

                result.AddRange(segmentTrips);
                segmentTrips.Clear();
                pendingNote = null;
                pendingColorOverride = null;
            }

            foreach (var line in lines)
            {
                if (line == null)
                    continue;

                switch (line.Kind)
                {
                    case ScheduleBuilderPreviewLine.LineKind.GroupHeader:
                        FlushSegment();
                        pendingNote = line.GroupNoteText;
                        pendingColorOverride = line.GroupColorOverride;
                        break;

                    case ScheduleBuilderPreviewLine.LineKind.Gap:
                        FlushSegment();
                        result.Add(line);
                        break;

                    case ScheduleBuilderPreviewLine.LineKind.SectionHeader:
                        FlushSegment();
                        result.Add(line);
                        break;

                    case ScheduleBuilderPreviewLine.LineKind.Trip:
                        if (line.Trip != null)
                            segmentTrips.Add(line);
                        break;
                }
            }

            FlushSegment();
            return result;
        }

        public static void ReconcileInPlace(IList<ScheduleBuilderPreviewLine> lines)
        {
            if (lines == null)
                return;

            var reconciled = Reconcile(lines);
            lines.Clear();
            foreach (var line in reconciled)
                lines.Add(line);
        }
    }
}
