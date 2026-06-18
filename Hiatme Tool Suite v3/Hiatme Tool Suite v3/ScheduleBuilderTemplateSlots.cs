using System.Collections.Generic;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Template CSV slot helpers for Schedule Builder (gap collapse, preview lines).</summary>
    internal static class ScheduleBuilderTemplateSlots
    {
        /// <summary>Consecutive gap rows become a single gap (first non-empty note wins).</summary>
        public static List<SupeyTemplateSlot> CollapseConsecutiveGaps(IList<SupeyTemplateSlot> slots)
        {
            var result = new List<SupeyTemplateSlot>();
            if (slots == null || slots.Count == 0)
                return result;

            bool inGapRun = false;
            string gapNote = "";

            foreach (var slot in slots)
            {
                if (slot != null && slot.Kind == SupeyTemplateSlot.SlotKind.Gap)
                {
                    if (!inGapRun)
                    {
                        inGapRun = true;
                        gapNote = slot.NoteText ?? "";
                    }
                    else if (string.IsNullOrWhiteSpace(gapNote)
                             && !string.IsNullOrWhiteSpace(slot.NoteText))
                    {
                        gapNote = slot.NoteText;
                    }
                    continue;
                }

                if (inGapRun)
                {
                    result.Add(MakeGap(gapNote));
                    inGapRun = false;
                    gapNote = "";
                }

                if (slot != null)
                    result.Add(slot);
            }

            if (inGapRun)
                result.Add(MakeGap(gapNote));

            return result;
        }

        public static List<ScheduleBuilderPreviewLine> BuildPreviewLines(
            IList<SupeyTemplateSlot> collapsedSlots,
            IList<MCDownloadedTrip> livePool,
            HashSet<string> matchedLiveTripNumbers,
            bool collapseGaps = true)
        {
            var lines = new List<ScheduleBuilderPreviewLine>();
            if (collapsedSlots == null)
                return lines;

            livePool = livePool ?? new List<MCDownloadedTrip>();
            matchedLiveTripNumbers = matchedLiveTripNumbers ?? new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

            foreach (var slot in collapsedSlots)
            {
                if (slot == null) continue;

                if (slot.Kind == SupeyTemplateSlot.SlotKind.Gap)
                {
                    lines.Add(new ScheduleBuilderPreviewLine
                    {
                        Kind = ScheduleBuilderPreviewLine.LineKind.Gap,
                        GapNoteText = slot.NoteText ?? "",
                    });
                    continue;
                }

                var templateTrip = slot.TemplateTrip;
                if (templateTrip == null) continue;

                MCDownloadedTrip match = null;
                foreach (var live in livePool)
                {
                    string liveTn = live?.TripNumber ?? "";
                    if (liveTn.Length > 0 && matchedLiveTripNumbers.Contains(liveTn))
                        continue;
                    if (TemplateTripMatchRules.TripsMatch(templateTrip, live))
                    {
                        match = live;
                        break;
                    }
                }

                if (match == null)
                    continue;

                string tn = match.TripNumber ?? "";
                if (tn.Length > 0)
                {
                    if (matchedLiveTripNumbers.Contains(tn))
                        continue;
                    matchedLiveTripNumbers.Add(tn);
                }

                lines.Add(new ScheduleBuilderPreviewLine
                {
                    Kind = ScheduleBuilderPreviewLine.LineKind.Trip,
                    Trip = match,
                });
            }

            return collapseGaps ? CollapseConsecutivePreviewGaps(lines) : lines;
        }

        /// <summary>
        /// After matching, unmatched template trips are omitted — consecutive gap lines in the
        /// preview can still stack; merge those runs to a single spacer row.
        /// </summary>
        public static List<ScheduleBuilderPreviewLine> CollapseConsecutivePreviewGaps(
            IList<ScheduleBuilderPreviewLine> lines)
        {
            var result = new List<ScheduleBuilderPreviewLine>();
            if (lines == null || lines.Count == 0)
                return result;

            bool inGapRun = false;
            string gapNote = "";

            foreach (var line in lines)
            {
                if (line != null && line.Kind == ScheduleBuilderPreviewLine.LineKind.Gap)
                {
                    if (!inGapRun)
                    {
                        inGapRun = true;
                        gapNote = line.GapNoteText ?? "";
                    }
                    else if (string.IsNullOrWhiteSpace(gapNote)
                             && !string.IsNullOrWhiteSpace(line.GapNoteText))
                    {
                        gapNote = line.GapNoteText;
                    }
                    continue;
                }

                if (inGapRun)
                {
                    result.Add(new ScheduleBuilderPreviewLine
                    {
                        Kind = ScheduleBuilderPreviewLine.LineKind.Gap,
                        GapNoteText = gapNote,
                    });
                    inGapRun = false;
                    gapNote = "";
                }

                if (line != null)
                    result.Add(line);
            }

            if (inGapRun)
            {
                result.Add(new ScheduleBuilderPreviewLine
                {
                    Kind = ScheduleBuilderPreviewLine.LineKind.Gap,
                    GapNoteText = gapNote,
                });
            }

            return result;
        }

        private static SupeyTemplateSlot MakeGap(string noteText) =>
            new SupeyTemplateSlot
            {
                Kind = SupeyTemplateSlot.SlotKind.Gap,
                NoteText = noteText ?? "",
            };
    }
}


