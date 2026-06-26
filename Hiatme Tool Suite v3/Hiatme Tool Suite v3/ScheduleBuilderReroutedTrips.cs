using System;
using System.Collections.Generic;

namespace Hiatme_Tool_Suite_v3
{
    internal static class ScheduleBuilderReroutedTrips
    {
        /// <summary>
        /// Rebuild wipes <see cref="ScheduleBuilderPreviewLine.ReroutedOnModivcare"/> — restore prior flags and mark the new reroute.
        /// </summary>
        public static void RestoreAndMarkRerouted(
            IList<ScheduleBuilderPreviewLine> target,
            IList<ScheduleBuilderPreviewLine> prior,
            MCDownloadedTrip justRerouted)
        {
            if (target == null)
                return;

            if (prior != null)
            {
                foreach (var oldLine in prior)
                {
                    if (oldLine?.Kind != ScheduleBuilderPreviewLine.LineKind.Trip
                        || !oldLine.ReroutedOnModivcare
                        || oldLine.Trip == null)
                        continue;

                    var line = FindLine(target, oldLine.Trip);
                    if (line != null)
                        line.ReroutedOnModivcare = true;
                }
            }

            ForceMarkRerouted(target, justRerouted);
        }

        public static bool ForceMarkRerouted(IList<ScheduleBuilderPreviewLine> lines, MCDownloadedTrip trip)
        {
            if (lines == null || trip == null)
                return false;

            bool marked = false;
            foreach (var line in lines)
            {
                if (line?.Kind != ScheduleBuilderPreviewLine.LineKind.Trip || line.Trip == null)
                    continue;
                if (!ScheduleBuilderPreviewDrag.TripEquals(line.Trip, trip))
                    continue;
                line.ReroutedOnModivcare = true;
                marked = true;
            }

            return marked;
        }

        public static bool ForceClearRerouted(IList<ScheduleBuilderPreviewLine> lines, MCDownloadedTrip trip)
        {
            if (lines == null || trip == null)
                return false;

            bool cleared = false;
            foreach (var line in lines)
            {
                if (line?.Kind != ScheduleBuilderPreviewLine.LineKind.Trip || line.Trip == null)
                    continue;
                if (!ScheduleBuilderPreviewDrag.TripEquals(line.Trip, trip))
                    continue;
                if (!line.ReroutedOnModivcare)
                    continue;
                line.ReroutedOnModivcare = false;
                cleared = true;
            }

            return cleared;
        }

        public static bool ClearReroutedAnyTab(
            IDictionary<string, List<ScheduleBuilderPreviewLine>> linesByTab,
            MCDownloadedTrip trip)
        {
            if (linesByTab == null || trip == null)
                return false;

            bool cleared = false;
            foreach (var kv in linesByTab)
            {
                if (ForceClearRerouted(kv.Value, trip))
                    cleared = true;
            }

            return cleared;
        }

        public static bool MarkReroutedAnyTab(
            IDictionary<string, List<ScheduleBuilderPreviewLine>> linesByTab,
            MCDownloadedTrip trip)
        {
            if (linesByTab == null || trip == null)
                return false;

            bool marked = false;
            foreach (var kv in linesByTab)
            {
                if (ForceMarkRerouted(kv.Value, trip))
                    marked = true;
            }

            return marked;
        }

        public static bool IsMarkedAnyTab(
            IDictionary<string, List<ScheduleBuilderPreviewLine>> linesByTab,
            MCDownloadedTrip trip)
        {
            if (linesByTab == null || trip == null)
                return false;

            foreach (var kv in linesByTab)
            {
                if (IsMarked(kv.Value, trip))
                    return true;
            }

            return false;
        }

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

        public static bool MarkRerouted(IList<ScheduleBuilderPreviewLine> lines, MCDownloadedTrip trip) =>
            ForceMarkRerouted(lines, trip);

        public static string TripNumberKey(string tripNumber) =>
            ScheduleBuilderPreviewDrag.NormalizeTripNumberKey(tripNumber);

        public static void AddTripNumberKey(ISet<string> keys, string tripNumber)
        {
            if (keys == null)
                return;
            string key = TripNumberKey(tripNumber);
            if (key.Length > 0)
                keys.Add(key);
        }

        public static bool TripNumberKeySetContains(ISet<string> keys, string tripNumber)
        {
            if (keys == null)
                return false;
            string key = TripNumberKey(tripNumber);
            return key.Length > 0 && keys.Contains(key);
        }

        public static bool TripNumberKeysMatch(string a, string b) =>
            string.Equals(TripNumberKey(a), TripNumberKey(b), StringComparison.OrdinalIgnoreCase)
            && TripNumberKey(a).Length > 0;

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

        /// <summary>Trips in Reserves → Reroutes (section header, reroute band, or bucket list).</summary>
        public static List<MCDownloadedTrip> EnumerateTripsInReroutesSection(
            IList<ScheduleBuilderPreviewLine> reserveLines,
            bool skipAlreadyMarked = true,
            IList<MCDownloadedTrip> bucketFallback = null)
        {
            var trips = new List<MCDownloadedTrip>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void TryAdd(MCDownloadedTrip trip)
            {
                if (trip == null)
                    return;
                if (skipAlreadyMarked && IsMarked(reserveLines, trip))
                    return;
                string key = TripNumberKey(trip.TripNumber);
                if (key.Length == 0 || !seen.Add(key))
                    return;
                trips.Add(trip);
            }

            if (reserveLines != null)
            {
                bool inReroutes = false;
                foreach (var line in reserveLines)
                {
                    if (line?.Kind == ScheduleBuilderPreviewLine.LineKind.SectionHeader)
                    {
                        string title = line.SectionTitle ?? "";
                        if (ScheduleBuilderReserveBuckets.TryParseSectionBucket(title, out var bucket))
                            inReroutes = bucket == ScheduleBuilderReserveBuckets.ReserveBucket.Reroute;
                        else
                        {
                            inReroutes = title.StartsWith("Reroutes", StringComparison.OrdinalIgnoreCase)
                                || title.StartsWith("Banned", StringComparison.OrdinalIgnoreCase);
                        }

                        continue;
                    }

                    if (line?.Kind != ScheduleBuilderPreviewLine.LineKind.Trip || line.Trip == null)
                        continue;

                    bool rerouteBand = line.ReserveBandColor.HasValue
                        && line.ReserveBandColor.Value.ToArgb() == ScheduleBuilderReserveBuckets.RerouteBand.ToArgb();
                    if (inReroutes || rerouteBand)
                        TryAdd(line.Trip);
                }
            }

            if (bucketFallback != null)
            {
                foreach (var trip in bucketFallback)
                    TryAdd(trip);
            }

            return trips;
        }

        public static int CountTripsInReroutesSection(
            IList<ScheduleBuilderPreviewLine> reserveLines,
            IList<MCDownloadedTrip> bucketFallback = null) =>
            EnumerateTripsInReroutesSection(reserveLines, skipAlreadyMarked: false, bucketFallback: bucketFallback).Count;

        public static bool AnyMarked(IList<ScheduleBuilderPreviewLine> lines)
        {
            if (lines == null)
                return false;
            foreach (var line in lines)
            {
                if (line?.Kind == ScheduleBuilderPreviewLine.LineKind.Trip
                    && line.Trip != null
                    && line.ReroutedOnModivcare)
                    return true;
            }
            return false;
        }
    }
}
