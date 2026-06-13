using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Rebuilds gap rows for map groups when a saved schedule has no route-break lines.</summary>
    internal static class ScheduleBuilderGroupInference
    {
        /// <summary>Default gap between pickups that starts a new group when inferring from times only.</summary>
        public static readonly TimeSpan DefaultPickupGap = TimeSpan.FromMinutes(90);

        public static List<ScheduleBuilderPreviewLine> BuildDriverLines(
            string csvPath,
            string driverTabName,
            string weekdayName,
            out string groupingNote)
        {
            groupingNote = "single group";
            if (string.IsNullOrEmpty(csvPath) || !File.Exists(csvPath))
                return new List<ScheduleBuilderPreviewLine>();

            var slots = SupeyTemplateCsvLoader.LoadSlotsFromFile(csvPath);
            if (slots.Any(s => s != null
                    && (s.Kind == SupeyTemplateSlot.SlotKind.Gap
                        || s.Kind == SupeyTemplateSlot.SlotKind.GroupHeader)))
            {
                groupingNote = "route breaks in file";
                var lines = SlotsToPreviewLines(slots);
                lines = ScheduleBuilderGroupHeaderRestore.ApplyLegacyRestoration(lines);
                return ScheduleBuilderGroupHeaderReconcile.Reconcile(lines);
            }

            var trips = ExtractTripsFromSlots(slots);
            if (trips.Count == 0)
                trips = ScheduleBuilderScheduleLoad.LoadTripsFromTripCsv(csvPath);

            if (trips.Count == 0)
                return new List<ScheduleBuilderPreviewLine>();

            var templateLines = TryBuildFromWeekdayTemplate(trips, driverTabName, weekdayName);
            if (templateLines != null)
            {
                groupingNote = "weekday template (" + weekdayName + ")";
                return templateLines;
            }

            groupingNote = "pickup time gaps (90+ min)";
            return InferPickupTimeGapLines(trips, DefaultPickupGap);
        }

        public static List<ScheduleBuilderPreviewLine> SlotsToPreviewLines(IList<SupeyTemplateSlot> slots)
        {
            var lines = new List<ScheduleBuilderPreviewLine>();
            if (slots == null) return lines;

            foreach (var slot in slots)
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

                if (slot.Kind == SupeyTemplateSlot.SlotKind.GroupHeader)
                {
                    lines.Add(new ScheduleBuilderPreviewLine
                    {
                        Kind = ScheduleBuilderPreviewLine.LineKind.GroupHeader,
                        GroupNumber = slot.GroupNumber,
                        GroupNoteText = slot.NoteText ?? "",
                    });
                    continue;
                }

                if (slot.Kind == SupeyTemplateSlot.SlotKind.Trip && slot.TemplateTrip != null)
                {
                    lines.Add(new ScheduleBuilderPreviewLine
                    {
                        Kind = ScheduleBuilderPreviewLine.LineKind.Trip,
                        Trip = slot.TemplateTrip,
                        ReroutedOnModivcare = slot.ReroutedOnModivcare,
                    });
                }
            }

            return lines;
        }

        public static List<ScheduleBuilderPreviewLine> InferPickupTimeGapLines(
            IList<MCDownloadedTrip> trips,
            TimeSpan gapThreshold)
        {
            var lines = new List<ScheduleBuilderPreviewLine>();
            if (trips == null || trips.Count == 0)
                return lines;

            TimeSpan? prevPu = null;
            foreach (var trip in trips)
            {
                if (trip == null) continue;
                var pu = SupeyTripTimes.TryParsePU(trip);
                if (prevPu.HasValue && pu.HasValue && pu.Value - prevPu.Value > gapThreshold)
                {
                    lines.Add(new ScheduleBuilderPreviewLine
                    {
                        Kind = ScheduleBuilderPreviewLine.LineKind.Gap,
                        GapNoteText = "Inferred break",
                    });
                }

                lines.Add(new ScheduleBuilderPreviewLine
                {
                    Kind = ScheduleBuilderPreviewLine.LineKind.Trip,
                    Trip = trip,
                });

                if (pu.HasValue)
                    prevPu = pu;
            }

            return ScheduleBuilderTemplateSlots.CollapseConsecutivePreviewGaps(lines);
        }

        private static List<MCDownloadedTrip> ExtractTripsFromSlots(IList<SupeyTemplateSlot> slots)
        {
            var trips = new List<MCDownloadedTrip>();
            if (slots == null) return trips;
            foreach (var slot in slots)
            {
                if (slot?.Kind == SupeyTemplateSlot.SlotKind.Trip && slot.TemplateTrip != null)
                    trips.Add(slot.TemplateTrip);
            }
            return trips;
        }

        private static List<ScheduleBuilderPreviewLine> TryBuildFromWeekdayTemplate(
            IList<MCDownloadedTrip> loadedTrips,
            string driverTabName,
            string weekdayName)
        {
            if (string.IsNullOrWhiteSpace(weekdayName) || string.IsNullOrWhiteSpace(driverTabName))
                return null;

            string dayDir = TemplateBuilder.GetDayTemplateDirectory(weekdayName);
            if (string.IsNullOrEmpty(dayDir))
                return null;

            string templatePath = Path.Combine(dayDir, driverTabName.Trim() + ".csv");
            if (!File.Exists(templatePath))
                return null;

            var templateSlots = ScheduleBuilderTemplateSlots.CollapseConsecutiveGaps(
                SupeyTemplateCsvLoader.LoadSlotsFromFile(templatePath));
            if (templateSlots == null || templateSlots.Count == 0)
                return null;

            var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var lines = ScheduleBuilderTemplateSlots.BuildPreviewLines(
                templateSlots, loadedTrips, matched);

            foreach (var trip in loadedTrips)
            {
                if (trip == null) continue;
                string tn = (trip.TripNumber ?? "").Trim();
                if (tn.Length > 0 && matched.Contains(tn))
                    continue;
                lines.Add(new ScheduleBuilderPreviewLine
                {
                    Kind = ScheduleBuilderPreviewLine.LineKind.Trip,
                    Trip = trip,
                });
            }

            return ScheduleBuilderTemplateSlots.CollapseConsecutivePreviewGaps(lines);
        }
    }
}
