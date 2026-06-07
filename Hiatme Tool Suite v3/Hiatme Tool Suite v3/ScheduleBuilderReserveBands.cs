using System;
using System.Collections.Generic;

using System.Drawing;

using System.Linq;



namespace Hiatme_Tool_Suite_v3

{

    /// <summary>Reserves tab sections and map groups — mirrors Supey reserve / reroute buckets.</summary>

    internal static class ScheduleBuilderReserveBuckets

    {

        internal enum ReserveBucket

        {

            Reserver,

            WillCall,

            Reroute,

            Banned,

        }



        /// <summary>Banned clients, no-go reroute, will call, else reservers. Banned → reroutes bucket.</summary>

        public static ReserveBucket Classify(MCDownloadedTrip trip)

        {

            if (trip == null) return ReserveBucket.Reserver;

            if (ScheduleBuilderBannedClients.IsBanned(trip))

                return ReserveBucket.Reroute;

            if (IsWillCallTrip(trip))

                return ReserveBucket.WillCall;

            if (SupeyOutOfArea.MatchTrip(trip) != null)

                return ReserveBucket.Reroute;

            return ReserveBucket.Reserver;

        }

        /// <summary>Legacy enum value — <see cref="Classify"/> maps banned clients to <see cref="ReserveBucket.Reroute"/>.</summary>
        // ReserveBucket.Banned kept for callers that still switch on it.

        /// <summary>Pull banned-client trips off driver preview tabs into the reroute list (deduped by trip #).</summary>
        public static int PullBannedTripsFromDriverLines(
            IDictionary<string, List<ScheduleBuilderPreviewLine>> driverLines,
            IList<MCDownloadedTrip> reroutes)
        {
            if (driverLines == null) return 0;
            var seen = BuildTripNumberSet(reroutes);
            int pulled = 0;

            foreach (string tab in driverLines.Keys.ToList())
            {
                if (tab.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!driverLines.TryGetValue(tab, out var lines) || lines == null)
                    continue;

                var kept = new List<ScheduleBuilderPreviewLine>();
                foreach (var line in lines)
                {
                    if (line?.Kind == ScheduleBuilderPreviewLine.LineKind.Trip
                        && line.Trip != null
                        && ScheduleBuilderBannedClients.IsBanned(line.Trip))
                    {
                        if (TryAddTripUnique(reroutes, seen, line.Trip))
                            pulled++;
                        continue;
                    }
                    kept.Add(line);
                }

                driverLines[tab] = ScheduleBuilderTemplateSlots.CollapseConsecutivePreviewGaps(kept);
            }

            return pulled;
        }

        /// <summary>Move any banned-client trips from other reserve buckets into reroutes.</summary>
        public static void RebucketBannedTripsIntoReroutes(
            IList<MCDownloadedTrip> reservers,
            IList<MCDownloadedTrip> reroutes,
            IList<MCDownloadedTrip> willCalls,
            IList<MCDownloadedTrip> legacyBanned = null)
        {
            var seen = BuildTripNumberSet(reroutes);
            MoveBannedTrips(reservers, reroutes, seen);
            MoveBannedTrips(willCalls, reroutes, seen);
            MoveBannedTrips(legacyBanned, reroutes, seen);
        }

        /// <summary>
        /// Re-sort every trip already on the Reserves tab into Will calls / Reservers / Reroutes
        /// using current banned-client and no-go rules (e.g. after removing a ban or town).
        /// </summary>
        public static void ReclassifyReserveBuckets(
            IList<MCDownloadedTrip> reservers,
            IList<MCDownloadedTrip> reroutes,
            IList<MCDownloadedTrip> willCalls)
        {
            var all = MergeTripLists(reservers, reroutes, willCalls);
            reservers?.Clear();
            reroutes?.Clear();
            willCalls?.Clear();
            if (reservers == null || reroutes == null || willCalls == null)
                return;

            foreach (var trip in all)
            {
                if (trip == null) continue;
                switch (Classify(trip))
                {
                    case ReserveBucket.WillCall:
                        willCalls.Add(trip);
                        break;
                    case ReserveBucket.Reroute:
                        reroutes.Add(trip);
                        break;
                    default:
                        reservers.Add(trip);
                        break;
                }
            }
        }

        private static void MoveBannedTrips(
            IList<MCDownloadedTrip> source,
            IList<MCDownloadedTrip> reroutes,
            HashSet<string> seen)
        {
            if (source == null || source.Count == 0) return;
            for (int i = source.Count - 1; i >= 0; i--)
            {
                var trip = source[i];
                if (trip == null || !ScheduleBuilderBannedClients.IsBanned(trip))
                    continue;
                source.RemoveAt(i);
                TryAddTripUnique(reroutes, seen, trip);
            }
        }

        private static HashSet<string> BuildTripNumberSet(IEnumerable<MCDownloadedTrip> trips)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (trips == null) return seen;
            foreach (var t in trips)
            {
                if (t == null) continue;
                string tn = (t.TripNumber ?? "").Trim();
                if (tn.Length > 0)
                    seen.Add(tn);
            }
            return seen;
        }

        private static bool TryAddTripUnique(IList<MCDownloadedTrip> list, HashSet<string> seen, MCDownloadedTrip trip)
        {
            if (list == null || trip == null) return false;
            string tn = (trip.TripNumber ?? "").Trim();
            if (tn.Length > 0)
            {
                if (!seen.Add(tn))
                    return false;
            }
            list.Add(trip);
            return true;
        }

        private static List<MCDownloadedTrip> MergeTripLists(params IList<MCDownloadedTrip>[] sources)
        {
            var merged = new List<MCDownloadedTrip>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (sources == null) return merged;
            foreach (var source in sources)
            {
                if (source == null) continue;
                foreach (var trip in source)
                    TryAddTripUnique(merged, seen, trip);
            }
            return merged;
        }

        /// <summary>Will call = scheduled pickup is midnight only (00:00 / 12:00 AM).</summary>

        public static bool IsWillCallTrip(MCDownloadedTrip trip) =>

            SupeyWillCallPickup.IsPickupWillCall(trip);

        /// <summary>Legacy name — use <see cref="IsWillCallTrip"/>.</summary>

        public static bool IsWillCallPickup(MCDownloadedTrip trip) => IsWillCallTrip(trip);

        public static bool HasWillCallComment(string comments)

        {

            if (string.IsNullOrWhiteSpace(comments)) return false;

            if (comments.IndexOf("will call", StringComparison.OrdinalIgnoreCase) >= 0)

                return true;

            string compact = comments.Replace(" ", "");

            return compact.IndexOf("willcall", StringComparison.OrdinalIgnoreCase) >= 0;

        }

        public static void CountWillCallsInDownload(

            IList<MCDownloadedTrip> trips,

            out int total,

            out int puMidnight,

            out int commentOnly)

        {

            total = puMidnight = commentOnly = 0;

            if (trips == null) return;

            foreach (var t in trips)

            {

                if (SupeyWillCallPickup.IsPickupWillCall(t))

                {

                    total++;

                    puMidnight++;

                    continue;

                }

                if (HasWillCallComment(t?.Comments))

                    commentOnly++;

            }

        }



        public static readonly Color WillCallBand = Color.FromArgb(120, 120, 160);

        public static readonly Color ReserversBand = Color.DimGray;

        public static readonly Color RerouteBand = Color.FromArgb(140, 90, 40);

        public static readonly Color BannedBand = Color.FromArgb(120, 55, 55);



        public static List<ScheduleBuilderPreviewLine> BuildReservePreviewLines(

            IList<MCDownloadedTrip> reservers,

            IList<MCDownloadedTrip> reroutes,

            IList<MCDownloadedTrip> banned = null,

            IList<MCDownloadedTrip> willCalls = null,

            int willCallsInDownloadCount = 0)

        {

            var lines = new List<ScheduleBuilderPreviewLine>();

            int wc = willCalls?.Count ?? 0;

            var allReroutes = MergeTripLists(reroutes, banned);

            if (wc > 0 || willCallsInDownloadCount > 0)

            {

                string title = wc > 0

                    ? "Will calls (" + wc + ")"

                    : "Will calls (0 placed — " + willCallsInDownloadCount

                        + " with 00:00 PU in download; check driver tabs / banned)";

                lines.Add(new ScheduleBuilderPreviewLine

                {

                    Kind = ScheduleBuilderPreviewLine.LineKind.SectionHeader,

                    SectionTitle = title,

                });

                if (wc > 0)

                {
                    foreach (var t in willCalls.OrderBy(x => x?.ClientFullName ?? ""))

                        lines.Add(TripLine(t, WillCallBand));
                }

            }

            if (reservers != null && reservers.Count > 0)

            {

                lines.Add(new ScheduleBuilderPreviewLine

                {

                    Kind = ScheduleBuilderPreviewLine.LineKind.SectionHeader,

                    SectionTitle = "Reservers (" + reservers.Count + ")",

                });

                foreach (var t in reservers.OrderBy(x => x?.PUTime ?? ""))

                    lines.Add(TripLine(t, ReserversBand));

            }

            if (allReroutes.Count > 0)

            {

                lines.Add(new ScheduleBuilderPreviewLine

                {

                    Kind = ScheduleBuilderPreviewLine.LineKind.SectionHeader,

                    SectionTitle = "Reroutes (" + allReroutes.Count + ")",

                });

                foreach (var t in allReroutes.OrderBy(x => x?.PUTime ?? ""))

                    lines.Add(TripLine(t, RerouteBand));

            }

            return lines;
        }



        public static List<SupeyTripCluster> BuildMapGroups(

            IList<MCDownloadedTrip> reservers,

            IList<MCDownloadedTrip> reroutes,

            IList<MCDownloadedTrip> banned = null,

            IList<MCDownloadedTrip> willCalls = null)

        {

            var groups = new List<SupeyTripCluster>();

            int n = 0;

            var allReroutes = MergeTripLists(reroutes, banned);

            if (willCalls != null && willCalls.Count > 0)

            {

                n++;

                var g = new SupeyTripCluster { GroupNumber = n, GroupColor = WillCallBand };

                foreach (var t in willCalls)

                    if (t != null) g.Trips.Add(t);

                groups.Add(g);

            }

            if (reservers != null && reservers.Count > 0)

            {

                n++;

                var g = new SupeyTripCluster { GroupNumber = n, GroupColor = ReserversBand };

                foreach (var t in reservers)

                    if (t != null) g.Trips.Add(t);

                groups.Add(g);

            }

            if (allReroutes.Count > 0)

            {

                n++;

                var g = new SupeyTripCluster { GroupNumber = n, GroupColor = RerouteBand };

                foreach (var t in allReroutes)

                    if (t != null) g.Trips.Add(t);

                groups.Add(g);

            }

            foreach (var g in groups)

                ScheduleBuilderPreviewGroups.FinalizePickupWindowPublic(g);

            return groups;

        }



        private static ScheduleBuilderPreviewLine TripLine(MCDownloadedTrip t, Color band) =>

            new ScheduleBuilderPreviewLine

            {

                Kind = ScheduleBuilderPreviewLine.LineKind.Trip,

                Trip = t,

                ReserveBandColor = band,

            };

    }

}


