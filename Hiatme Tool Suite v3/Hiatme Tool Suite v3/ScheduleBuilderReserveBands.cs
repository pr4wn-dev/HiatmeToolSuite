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

                driverLines[tab] = kept;
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
                string key = ScheduleBuilderReroutedTrips.TripNumberKey(t.TripNumber);
                if (key.Length > 0)
                    seen.Add(key);
            }
            return seen;
        }

        private static bool RemoveTripFromList(IList<MCDownloadedTrip> list, MCDownloadedTrip trip)
        {
            if (list == null || trip == null)
                return false;

            bool removed = false;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (ScheduleBuilderPreviewDrag.TripEquals(list[i], trip))
                {
                    list.RemoveAt(i);
                    removed = true;
                }
            }

            return removed;
        }

        /// <summary>Move a trip into the Reserves → Reroutes bucket lists (deduped).</summary>
        public static bool MoveTripToReroutesBucket(
            MCDownloadedTrip trip,
            IList<MCDownloadedTrip> reservers,
            IList<MCDownloadedTrip> reroutes,
            IList<MCDownloadedTrip> willCalls)
        {
            if (trip == null || reroutes == null)
                return false;

            bool changed = RemoveTripFromList(reservers, trip);
            changed |= RemoveTripFromList(willCalls, trip);

            var seen = BuildTripNumberSet(reroutes);
            if (TryMergeTripIntoList(reroutes, trip))
                changed = true;
            else if (TryAddTripUnique(reroutes, seen, trip))
                changed = true;

            return changed;
        }

        /// <summary>If the trip number is already in the list, merge missing fields (e.g. phones onto registry ghosts).</summary>
        private static bool TryMergeTripIntoList(IList<MCDownloadedTrip> list, MCDownloadedTrip trip)
        {
            if (list == null || trip == null)
                return false;

            for (int i = 0; i < list.Count; i++)
            {
                var existing = list[i];
                if (!ScheduleBuilderPreviewDrag.TripEquals(existing, trip))
                    continue;

                existing.MergeMissingScheduleFieldsFrom(trip);
                return true;
            }

            return false;
        }

        /// <summary>Remove one trip from every driver preview tab (not Reserves).</summary>
        public static int PullTripFromDriverLines(
            IDictionary<string, List<ScheduleBuilderPreviewLine>> driverLines,
            MCDownloadedTrip trip)
        {
            if (driverLines == null || trip == null)
                return 0;

            int pulled = 0;
            foreach (string tab in driverLines.Keys.ToList())
            {
                if (tab.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!driverLines.TryGetValue(tab, out var lines) || lines == null)
                    continue;
                if (!ScheduleBuilderPreviewDrag.TryRemoveTrip(lines, trip))
                    continue;

                driverLines[tab] = ScheduleBuilderGroupHeaderReconcile.Reconcile(lines);
                pulled++;
            }

            return pulled;
        }

        private static bool TryAddTripUnique(IList<MCDownloadedTrip> list, HashSet<string> seen, MCDownloadedTrip trip)
        {
            if (list == null || trip == null) return false;
            string key = ScheduleBuilderReroutedTrips.TripNumberKey(trip.TripNumber);
            if (key.Length > 0)
            {
                if (!seen.Add(key))
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



        /// <summary>Reserve section / map colors — tuned for LibreOffice and dark preview.</summary>
        public static readonly Color WillCallBand = Color.FromArgb(88, 112, 188);   // indigo — will calls
        public static readonly Color ReserversBand = Color.FromArgb(52, 128, 124);  // teal — need driver
        public static readonly Color RerouteBand = Color.FromArgb(196, 118, 48);  // amber — reroute to MC
        public static readonly Color BannedBand = Color.FromArgb(168, 72, 88);    // rose — banned (legacy)

        /// <summary>Section header bar color from title (Will calls / Reservers / Reroutes).</summary>
        public static Color SectionColorForTitle(string title)
        {
            title = title ?? "";
            if (title.StartsWith("Will calls", StringComparison.OrdinalIgnoreCase))
                return WillCallBand;
            if (title.StartsWith("Reservers", StringComparison.OrdinalIgnoreCase))
                return ReserversBand;
            if (title.StartsWith("Reroutes", StringComparison.OrdinalIgnoreCase))
                return RerouteBand;
            return ReserversBand;
        }

        /// <summary>Parse a saved Reserves section header (e.g. "Reroutes (3)") into a bucket.</summary>
        public static bool TryParseSectionBucket(string title, out ReserveBucket bucket)
        {
            title = (title ?? "").Trim();
            if (title.StartsWith("Will calls", StringComparison.OrdinalIgnoreCase))
            {
                bucket = ReserveBucket.WillCall;
                return true;
            }

            if (title.StartsWith("Reservers", StringComparison.OrdinalIgnoreCase))
            {
                bucket = ReserveBucket.Reserver;
                return true;
            }

            if (title.StartsWith("Reroutes", StringComparison.OrdinalIgnoreCase)
                || title.StartsWith("Banned", StringComparison.OrdinalIgnoreCase))
            {
                bucket = ReserveBucket.Reroute;
                return true;
            }

            bucket = ReserveBucket.Reserver;
            return false;
        }

        /// <summary>
        /// Reserves tab rows from bucket lists. When <paramref name="preserveTripOrder"/> is false (fresh BUILD),
        /// trips are sorted by PU time / client name. When true (load or after user ordering), list order is kept.
        /// </summary>
        public static List<ScheduleBuilderPreviewLine> BuildReservePreviewLines(

            IList<MCDownloadedTrip> reservers,

            IList<MCDownloadedTrip> reroutes,

            IList<MCDownloadedTrip> banned = null,

            IList<MCDownloadedTrip> willCalls = null,

            int willCallsInDownloadCount = 0,

            bool preserveTripOrder = false)

        {

            var lines = new List<ScheduleBuilderPreviewLine>();

            int wc = willCalls?.Count ?? 0;

            var allReroutes = MergeTripLists(reroutes, banned);

            IEnumerable<MCDownloadedTrip> OrderWillCalls(IList<MCDownloadedTrip> list)
            {
                var source = list ?? Array.Empty<MCDownloadedTrip>();
                if (preserveTripOrder)
                    return source;
                return source.OrderBy(x => x?.ClientFullName ?? "");
            }

            IEnumerable<MCDownloadedTrip> OrderByPu(IList<MCDownloadedTrip> list)
            {
                var source = list ?? Array.Empty<MCDownloadedTrip>();
                if (preserveTripOrder)
                    return source;
                return source.OrderBy(x => x?.PUTime ?? "");
            }

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

                    ReserveBandColor = WillCallBand,

                });

                if (wc > 0)

                {
                    foreach (var t in OrderWillCalls(willCalls))

                        lines.Add(TripLine(t, WillCallBand));
                }

            }

            if (reservers != null && reservers.Count > 0)

            {

                lines.Add(new ScheduleBuilderPreviewLine

                {

                    Kind = ScheduleBuilderPreviewLine.LineKind.SectionHeader,

                    SectionTitle = "Reservers (" + reservers.Count + ")",

                    ReserveBandColor = ReserversBand,

                });

                foreach (var t in OrderByPu(reservers))

                    lines.Add(TripLine(t, ReserversBand));

            }

            if (allReroutes.Count > 0)

            {

                lines.Add(new ScheduleBuilderPreviewLine

                {

                    Kind = ScheduleBuilderPreviewLine.LineKind.SectionHeader,

                    SectionTitle = "Reroutes (" + allReroutes.Count + ")",

                    ReserveBandColor = RerouteBand,

                });

                foreach (var t in OrderByPu(allReroutes))

                    lines.Add(TripLine(t, RerouteBand));

            }

            return lines;
        }

        /// <summary>Rebuild Reserves preview rows in saved file order (section headers + trips as they appear on the sheet).</summary>
        public static List<ScheduleBuilderPreviewLine> BuildReservePreviewLinesFromSlots(
            IList<SupeyTemplateSlot> slots,
            IList<MCDownloadedTrip> willCallsAfterLoad = null,
            IList<MCDownloadedTrip> reserversAfterLoad = null,
            IList<MCDownloadedTrip> reroutesAfterLoad = null)
        {
            var lines = new List<ScheduleBuilderPreviewLine>();
            var tripKeysInLines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (slots == null || slots.Count == 0)
                return lines;

            Color currentBand = ReserversBand;

            foreach (var slot in slots)
            {
                if (slot == null)
                    continue;

                if (slot.Kind == SupeyTemplateSlot.SlotKind.Gap)
                {
                    string note = (slot.NoteText ?? "").Trim();
                    if (TryParseSectionBucket(note, out _))
                    {
                        currentBand = SectionColorForTitle(note);
                        lines.Add(new ScheduleBuilderPreviewLine
                        {
                            Kind = ScheduleBuilderPreviewLine.LineKind.SectionHeader,
                            SectionTitle = note,
                            ReserveBandColor = currentBand,
                        });
                    }

                    continue;
                }

                if (slot.Kind != SupeyTemplateSlot.SlotKind.Trip || slot.TemplateTrip == null)
                    continue;

                var trip = slot.TemplateTrip;
                TrackTripKey(tripKeysInLines, trip);
                lines.Add(new ScheduleBuilderPreviewLine
                {
                    Kind = ScheduleBuilderPreviewLine.LineKind.Trip,
                    Trip = trip,
                    ReserveBandColor = currentBand,
                    ReroutedOnModivcare = slot.ReroutedOnModivcare,
                });
            }

            AppendMissingReserveTrips(lines, tripKeysInLines, willCallsAfterLoad, WillCallBand);
            AppendMissingReserveTrips(lines, tripKeysInLines, reserversAfterLoad, ReserversBand);
            AppendMissingReserveTrips(lines, tripKeysInLines, reroutesAfterLoad, RerouteBand);

            return lines;
        }

        private static void TrackTripKey(HashSet<string> seen, MCDownloadedTrip trip)
        {
            if (trip == null || seen == null)
                return;
            string key = ScheduleBuilderReroutedTrips.TripNumberKey(trip.TripNumber);
            if (key.Length > 0)
                seen.Add(key);
        }

        /// <summary>Trips added after load (registry reroutes, banned pull) append at the end in bucket-list order.</summary>
        private static void AppendMissingReserveTrips(
            List<ScheduleBuilderPreviewLine> lines,
            HashSet<string> tripKeysInLines,
            IList<MCDownloadedTrip> trips,
            Color band)
        {
            if (lines == null || trips == null || trips.Count == 0)
                return;

            foreach (var trip in trips)
            {
                if (trip == null)
                    continue;
                string key = ScheduleBuilderReroutedTrips.TripNumberKey(trip.TripNumber);
                if (key.Length > 0 && !tripKeysInLines.Add(key))
                    continue;

                lines.Add(TripLine(trip, band));
            }
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


