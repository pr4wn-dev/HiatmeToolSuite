using System.Collections.Generic;

namespace Hiatme_Tool_Suite_v3
{
    internal static class ScheduleBuilderReroutedTrips
    {
        public static ScheduleBuilderPreviewLine FindLine(
            IList<ScheduleBuilderPreviewLine> lines,
            MCDownloadedTrip trip)
        {
            if (lines == null || trip == null)
                return null;

            foreach (var line in lines)
            {
                if (line?.Kind != ScheduleBuilderPreviewLine.LineKind.Trip || line.Trip == null)
                    continue;
                if (ScheduleBuilderPreviewDrag.TripEquals(line.Trip, trip))
                    return line;
            }

            return null;
        }

        public static bool IsMarked(IList<ScheduleBuilderPreviewLine> lines, MCDownloadedTrip trip) =>
            FindLine(lines, trip)?.ReroutedOnModivcare == true;

        public static bool MarkRerouted(IList<ScheduleBuilderPreviewLine> lines, MCDownloadedTrip trip)
        {
            var line = FindLine(lines, trip);
            if (line == null)
                return false;
            line.ReroutedOnModivcare = true;
            return true;
        }

        public static bool IsInReservesRerouteBucket(IList<MCDownloadedTrip> reroutes, MCDownloadedTrip trip)
        {
            if (reroutes == null || trip == null)
                return false;
            foreach (var t in reroutes)
            {
                if (ScheduleBuilderPreviewDrag.TripEquals(t, trip))
                    return true;
            }
            return false;
        }

        public static bool IsInReservesRerouteSection(
            IList<ScheduleBuilderPreviewLine> reserveLines,
            MCDownloadedTrip trip)
        {
            var line = FindLine(reserveLines, trip);
            if (line == null)
                return false;
            return line.ReserveBandColor.HasValue
                && line.ReserveBandColor.Value.ToArgb() == ScheduleBuilderReserveBuckets.RerouteBand.ToArgb();
        }
    }
}
