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

            Cancel,

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

            for (int i = 0; i < list.Count; i++)
            {
                if (ReferenceEquals(list[i], trip))
                {
                    list.RemoveAt(i);
                    return true;
                }
            }

            for (int i = 0; i < list.Count; i++)
            {
                if (ScheduleBuilderPreviewDrag.TripEquals(list[i], trip))
                {
                    list.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }

        /// <summary>Move a trip into the Reserves → Reroutes bucket lists (deduped).</summary>
        public static bool MoveTripToReroutesBucket(
            MCDownloadedTrip trip,
            IList<MCDownloadedTrip> reservers,
            IList<MCDownloadedTrip> reroutes,
            IList<MCDownloadedTrip> willCalls,
            IList<MCDownloadedTrip> cancels = null)
        {
            if (trip == null || reroutes == null)
                return false;

            bool changed = RemoveTripFromList(reservers, trip);
            changed |= RemoveTripFromList(willCalls, trip);
            if (cancels != null)
                changed |= RemoveTripFromList(cancels, trip);

            var seen = BuildTripNumberSet(reroutes);
            if (TryMergeTripIntoList(reroutes, trip))
                changed = true;
            else if (TryAddTripUnique(reroutes, seen, trip))
                changed = true;

            return changed;
        }

        /// <summary>Move a trip into the Reserves → Cancels bucket lists (deduped).</summary>
        public static bool MoveTripToCancelsBucket(
            MCDownloadedTrip trip,
            IList<MCDownloadedTrip> reservers,
            IList<MCDownloadedTrip> reroutes,
            IList<MCDownloadedTrip> willCalls,
            IList<MCDownloadedTrip> cancels)
        {
            if (trip == null || cancels == null)
                return false;

            bool changed = RemoveTripFromList(reservers, trip);
            changed |= RemoveTripFromList(willCalls, trip);
            changed |= RemoveTripFromList(reroutes, trip);

            var seen = BuildTripNumberSet(cancels);
            if (TryMergeTripIntoList(cancels, trip))
                changed = true;
            else if (TryAddTripUnique(cancels, seen, trip))
                changed = true;

            return changed;
        }

        public static bool IsInCancelBucket(IList<MCDownloadedTrip> cancels, MCDownloadedTrip trip)
        {
            if (cancels == null || trip == null)
                return false;
            foreach (var t in cancels)
            {
                if (ScheduleBuilderPreviewDrag.TripEquals(t, trip))
                    return true;
            }
            return false;
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
        public static readonly Color CancelBand = Color.FromArgb(108, 92, 148); // violet — manual cancels

        public static bool IsRerouteBand(Color band)
            => band.ToArgb() == RerouteBand.ToArgb();

        /// <summary>Section header bar color from title (Will calls / Reservers / Cancels / Reroutes).</summary>
        public static Color SectionColorForTitle(string title)
        {
            title = title ?? "";
            if (title.StartsWith("Will calls", StringComparison.OrdinalIgnoreCase))
                return WillCallBand;
            if (title.StartsWith("Reservers", StringComparison.OrdinalIgnoreCase))
                return ReserversBand;
            if (title.StartsWith("Reroutes", StringComparison.OrdinalIgnoreCase))
                return RerouteBand;
            if (title.StartsWith("Cancels", StringComparison.OrdinalIgnoreCase))
                return CancelBand;
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

            if (title.StartsWith("Cancels", StringComparison.OrdinalIgnoreCase))
            {
                bucket = ReserveBucket.Cancel;
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

            bool preserveTripOrder = false,

            IList<MCDownloadedTrip> cancels = null)

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

            if (cancels != null && cancels.Count > 0)

            {

                lines.Add(new ScheduleBuilderPreviewLine

                {

                    Kind = ScheduleBuilderPreviewLine.LineKind.SectionHeader,

                    SectionTitle = "Cancels (" + cancels.Count + ")",

                    ReserveBandColor = CancelBand,

                });

                foreach (var t in OrderByPu(cancels))

                    lines.Add(TripLine(t, CancelBand));

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
            IList<MCDownloadedTrip> reroutesAfterLoad = null,
            IList<MCDownloadedTrip> cancelsAfterLoad = null)
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

            AppendMissingReserveTrips(lines, tripKeysInLines, willCallsAfterLoad, ReserveBucket.WillCall, WillCallBand);
            AppendMissingReserveTrips(lines, tripKeysInLines, reserversAfterLoad, ReserveBucket.Reserver, ReserversBand);
            AppendMissingReserveTrips(lines, tripKeysInLines, cancelsAfterLoad, ReserveBucket.Cancel, CancelBand);
            AppendMissingReserveTrips(lines, tripKeysInLines, reroutesAfterLoad, ReserveBucket.Reroute, RerouteBand);

            ReorderCancelsAboveReroutes(lines);
            // Saved headers keep their old "(N)" until refreshed — recount against actual trip rows.
            RefreshReserveSectionHeaderCounts(lines);

            return lines;
        }

        /// <summary>Move Cancels section block above Reroutes when a saved workbook had the old order.</summary>
        private static void ReorderCancelsAboveReroutes(List<ScheduleBuilderPreviewLine> lines)
        {
            if (lines == null || lines.Count < 2)
                return;

            int cancelsStart = -1;
            int reroutesStart = -1;
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (line?.Kind != ScheduleBuilderPreviewLine.LineKind.SectionHeader)
                    continue;
                if (!TryParseSectionBucket(line.SectionTitle, out var bucket))
                    continue;
                if (bucket == ReserveBucket.Cancel && cancelsStart < 0)
                    cancelsStart = i;
                else if (bucket == ReserveBucket.Reroute && reroutesStart < 0)
                    reroutesStart = i;
            }

            if (cancelsStart < 0 || reroutesStart < 0 || cancelsStart < reroutesStart)
                return;

            int cancelsEnd = NextSectionStart(lines, cancelsStart + 1);
            var cancelsBlock = lines.GetRange(cancelsStart, cancelsEnd - cancelsStart);
            lines.RemoveRange(cancelsStart, cancelsEnd - cancelsStart);

            reroutesStart = -1;
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (line?.Kind != ScheduleBuilderPreviewLine.LineKind.SectionHeader)
                    continue;
                if (TryParseSectionBucket(line.SectionTitle, out var bucket) && bucket == ReserveBucket.Reroute)
                {
                    reroutesStart = i;
                    break;
                }
            }

            if (reroutesStart < 0)
                lines.AddRange(cancelsBlock);
            else
                lines.InsertRange(reroutesStart, cancelsBlock);
        }

        private static int NextSectionStart(IList<ScheduleBuilderPreviewLine> lines, int fromIndex)
        {
            for (int i = fromIndex; i < lines.Count; i++)
            {
                var line = lines[i];
                if (line?.Kind == ScheduleBuilderPreviewLine.LineKind.SectionHeader
                    && TryParseSectionBucket(line.SectionTitle, out _))
                    return i;
            }

            return lines.Count;
        }

        private static void TrackTripKey(HashSet<string> seen, MCDownloadedTrip trip)
        {
            if (trip == null || seen == null)
                return;
            string key = ScheduleBuilderReroutedTrips.TripNumberKey(trip.TripNumber);
            if (key.Length > 0)
                seen.Add(key);
        }

        /// <summary>
        /// Trips added after load (registry reroutes, banned pull) go under the matching section —
        /// never dumped at the end of the sheet (that skews the previous section's count).
        /// </summary>
        private static void AppendMissingReserveTrips(
            List<ScheduleBuilderPreviewLine> lines,
            HashSet<string> tripKeysInLines,
            IList<MCDownloadedTrip> trips,
            ReserveBucket bucket,
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

                InsertTripAtSectionEnd(lines, trip, bucket, band);
            }
        }



        public static List<SupeyTripCluster> BuildMapGroups(

            IList<MCDownloadedTrip> reservers,

            IList<MCDownloadedTrip> reroutes,

            IList<MCDownloadedTrip> banned = null,

            IList<MCDownloadedTrip> willCalls = null,

            IList<MCDownloadedTrip> cancels = null)

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

            if (cancels != null && cancels.Count > 0)

            {

                n++;

                var g = new SupeyTripCluster { GroupNumber = n, GroupColor = CancelBand };

                foreach (var t in cancels)

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

        /// <summary>
        /// Move trips between reserve sections in-place so bucket rebuilds do not reshuffle neighbors.
        /// Also strips unconfirmed partner legs out of Reroutes (ghost rows that never hit the bucket).
        /// </summary>
        public static List<ScheduleBuilderPreviewLine> PatchPriorReserveLinesForCancelMove(
            IList<ScheduleBuilderPreviewLine> priorLines,
            MCDownloadedTrip movedToCancel,
            IEnumerable<MCDownloadedTrip> demotedFromReroutes,
            ISet<string> confirmedReroutedLegKeys = null,
            Func<MCDownloadedTrip, bool> isOnDriverTab = null)
        {
            if (priorLines == null || priorLines.Count == 0)
                return null;

            var lines = new List<ScheduleBuilderPreviewLine>(priorLines.Count);
            foreach (var line in priorLines)
            {
                if (line == null)
                    continue;
                lines.Add(CopyReservePreviewLine(line));
            }

            if (movedToCancel != null)
                StripUnconfirmedPartnerLegsFromReroutesSection(
                    lines, movedToCancel, confirmedReroutedLegKeys, isOnDriverTab);

            if (demotedFromReroutes != null)
            {
                foreach (var partner in demotedFromReroutes)
                {
                    if (partner == null)
                        continue;
                    RemoveTripFromReserveLines(lines, partner);
                    if (isOnDriverTab != null && isOnDriverTab(partner))
                        continue;
                    InsertTripAtSectionEnd(lines, partner, ReserveBucket.Reserver, ReserversBand);
                }
            }

            if (movedToCancel != null)
            {
                RemoveTripFromReserveLines(lines, movedToCancel);
                InsertTripAtSectionEnd(lines, movedToCancel, ReserveBucket.Cancel, CancelBand);
            }

            ReassignBandsAndRefreshSectionCounts(lines);
            return lines;
        }

        /// <summary>
        /// Partner B-leg ghosts in the Reroutes band must disappear when A-leg goes to Cancels.
        /// </summary>
        private static void StripUnconfirmedPartnerLegsFromReroutesSection(
            List<ScheduleBuilderPreviewLine> lines,
            MCDownloadedTrip anchor,
            ISet<string> confirmedReroutedLegKeys,
            Func<MCDownloadedTrip, bool> isOnDriverTab)
        {
            if (lines == null || anchor == null)
                return;

            var partners = CollectPartnerTripsInReroutesSection(lines, anchor, confirmedReroutedLegKeys);
            foreach (var partner in partners)
            {
                RemoveTripFromReserveLines(lines, partner);
                if (isOnDriverTab != null && isOnDriverTab(partner))
                    continue;
                InsertTripAtSectionEnd(lines, partner, ReserveBucket.Reserver, ReserversBand);
            }
        }

        private static List<MCDownloadedTrip> CollectPartnerTripsInReroutesSection(
            IList<ScheduleBuilderPreviewLine> lines,
            MCDownloadedTrip anchor,
            ISet<string> confirmedReroutedLegKeys)
        {
            var partners = new List<MCDownloadedTrip>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (lines == null || anchor == null)
                return partners;

            bool inReroutes = false;
            foreach (var line in lines)
            {
                if (line?.Kind == ScheduleBuilderPreviewLine.LineKind.SectionHeader)
                {
                    if (TryParseSectionBucket(line.SectionTitle, out var bucket))
                        inReroutes = bucket == ReserveBucket.Reroute;
                    else
                        inReroutes = (line.SectionTitle ?? "").StartsWith("Reroutes", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (!inReroutes
                    || line?.Kind != ScheduleBuilderPreviewLine.LineKind.Trip
                    || line.Trip == null)
                {
                    continue;
                }

                if (!ScheduleBuilderPreviewDrag.IsPartnerLeg(anchor.TripNumber, line.Trip.TripNumber))
                    continue;
                if (confirmedReroutedLegKeys != null
                    && ScheduleBuilderReroutedTrips.TripNumberKeySetContains(
                        confirmedReroutedLegKeys, line.Trip.TripNumber))
                {
                    continue;
                }

                string key = ScheduleBuilderReroutedTrips.TripNumberKey(line.Trip.TripNumber);
                if (key.Length == 0 || !seen.Add(key))
                    continue;
                partners.Add(line.Trip);
            }

            return partners;
        }

        /// <summary>
        /// Move ONE trip into the Reserves → Reroutes section in an existing line list.
        /// Only the given trip moves; every other row keeps its exact position.
        /// </summary>
        public static void MoveTripIntoReroutesSectionInPlace(
            List<ScheduleBuilderPreviewLine> reserveLines,
            MCDownloadedTrip trip)
        {
            if (reserveLines == null || trip == null)
                return;

            bool reroutedOnModivcare = false;
            bool cancelledOnWellRyde = false;
            foreach (var line in reserveLines)
            {
                if (line?.Kind != ScheduleBuilderPreviewLine.LineKind.Trip
                    || !ScheduleBuilderPreviewDrag.TripEquals(line.Trip, trip))
                {
                    continue;
                }

                reroutedOnModivcare = line.ReroutedOnModivcare;
                cancelledOnWellRyde = line.CancelledOnWellRyde;
                break;
            }

            RemoveTripFromReserveLines(reserveLines, trip);
            InsertTripAtSectionEnd(reserveLines, trip, ReserveBucket.Reroute, RerouteBand);

            for (int i = reserveLines.Count - 1; i >= 0; i--)
            {
                var line = reserveLines[i];
                if (line?.Kind != ScheduleBuilderPreviewLine.LineKind.Trip
                    || !ScheduleBuilderPreviewDrag.TripEquals(line.Trip, trip))
                {
                    continue;
                }

                line.ReroutedOnModivcare = reroutedOnModivcare;
                line.CancelledOnWellRyde = cancelledOnWellRyde;
                break;
            }

            RefreshReserveSectionHeaderCounts(reserveLines);
        }

        /// <summary>
        /// Move ONE trip into the Reserves → Cancels section in an existing line list.
        /// Only the given trip moves; every other row keeps its exact position.
        /// </summary>
        public static void MoveTripIntoCancelsSectionInPlace(
            List<ScheduleBuilderPreviewLine> reserveLines,
            MCDownloadedTrip trip)
        {
            if (reserveLines == null || trip == null)
                return;

            RemoveTripFromReserveLines(reserveLines, trip);
            InsertTripAtSectionEnd(reserveLines, trip, ReserveBucket.Cancel, CancelBand);
            RefreshReserveSectionHeaderCounts(reserveLines);
        }

        /// <summary>
        /// Add a trip from a fresh Modivcare download to the correct Reserves section in-place
        /// (will call / reroute / reservers via current rules). Existing rows keep their positions.
        /// Returns the bucket the trip was placed in so the caller can update the matching bucket list.
        /// </summary>
        public static ReserveBucket InsertNewDownloadTripIntoReserveLines(
            List<ScheduleBuilderPreviewLine> reserveLines,
            MCDownloadedTrip trip)
        {
            if (reserveLines == null || trip == null)
                return ReserveBucket.Reserver;

            ReserveBucket bucket = Classify(trip);
            InsertTripAtSectionEnd(reserveLines, trip, bucket, BandForBucket(bucket));
            RefreshReserveSectionHeaderCounts(reserveLines);
            return bucket;
        }

        private static Color BandForBucket(ReserveBucket bucket)
        {
            switch (bucket)
            {
                case ReserveBucket.WillCall: return WillCallBand;
                case ReserveBucket.Reroute: return RerouteBand;
                case ReserveBucket.Cancel: return CancelBand;
                default: return ReserversBand;
            }
        }

        private static ScheduleBuilderPreviewLine CopyReservePreviewLine(ScheduleBuilderPreviewLine line)
        {
            if (line == null)
                return null;

            return new ScheduleBuilderPreviewLine
            {
                Kind = line.Kind,
                Trip = line.Trip,
                SectionTitle = line.SectionTitle,
                ReserveBandColor = line.ReserveBandColor,
                ReroutedOnModivcare = line.ReroutedOnModivcare,
                CancelledOnWellRyde = line.CancelledOnWellRyde,
                GroupNumber = line.GroupNumber,
                GroupNoteText = line.GroupNoteText,
                GroupColorOverride = line.GroupColorOverride,
                GroupNoteRowColor = line.GroupNoteRowColor,
                GroupNoteCenterText = line.GroupNoteCenterText,
                GapNoteText = line.GapNoteText,
                GapNoteRowColor = line.GapNoteRowColor,
                GapNoteCenterText = line.GapNoteCenterText,
            };
        }

        private static void RemoveTripFromReserveLines(
            IList<ScheduleBuilderPreviewLine> lines,
            MCDownloadedTrip trip)
        {
            if (lines == null || trip == null)
                return;

            for (int i = lines.Count - 1; i >= 0; i--)
            {
                var line = lines[i];
                if (line?.Kind == ScheduleBuilderPreviewLine.LineKind.Trip
                    && ScheduleBuilderPreviewDrag.TripEquals(line.Trip, trip))
                {
                    lines.RemoveAt(i);
                }
            }
        }

        private static void InsertTripAtSectionEnd(
            List<ScheduleBuilderPreviewLine> lines,
            MCDownloadedTrip trip,
            ReserveBucket bucket,
            Color band)
        {
            if (lines == null || trip == null)
                return;

            int headerIdx = FindSectionHeaderIndex(lines, bucket);
            if (headerIdx < 0)
                headerIdx = CreateSectionHeader(lines, bucket, band);

            int insertAt = NextSectionStart(lines, headerIdx + 1);
            lines.Insert(insertAt, TripLine(trip, band));
        }

        private static int FindSectionHeaderIndex(IList<ScheduleBuilderPreviewLine> lines, ReserveBucket bucket)
        {
            if (lines == null)
                return -1;

            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (line?.Kind != ScheduleBuilderPreviewLine.LineKind.SectionHeader)
                    continue;
                if (TryParseSectionBucket(line.SectionTitle, out var parsed) && parsed == bucket)
                    return i;
            }

            return -1;
        }

        private static int CreateSectionHeader(
            List<ScheduleBuilderPreviewLine> lines,
            ReserveBucket bucket,
            Color band)
        {
            int insertAt = lines.Count;
            if (bucket == ReserveBucket.Cancel)
            {
                for (int i = 0; i < lines.Count; i++)
                {
                    var line = lines[i];
                    if (line?.Kind != ScheduleBuilderPreviewLine.LineKind.SectionHeader)
                        continue;
                    if (TryParseSectionBucket(line.SectionTitle, out var parsed)
                        && parsed == ReserveBucket.Reroute)
                    {
                        insertAt = i;
                        break;
                    }
                }
            }

            string title = SectionTitleForBucket(bucket, 0);
            lines.Insert(insertAt, new ScheduleBuilderPreviewLine
            {
                Kind = ScheduleBuilderPreviewLine.LineKind.SectionHeader,
                SectionTitle = title,
                ReserveBandColor = band,
            });
            return insertAt;
        }

        private static string SectionTitleForBucket(ReserveBucket bucket, int count)
        {
            switch (bucket)
            {
                case ReserveBucket.WillCall:
                    return "Will calls (" + count + ")";
                case ReserveBucket.Reroute:
                    return "Reroutes (" + count + ")";
                case ReserveBucket.Cancel:
                    return "Cancels (" + count + ")";
                default:
                    return "Reservers (" + count + ")";
            }
        }

        /// <summary>
        /// After drag/cut/load, keep each trip's band color in sync with the section it sits under,
        /// then rewrite headers as "Reroutes (N)" etc. from the trips actually in that block.
        /// </summary>
        public static void ReassignBandsAndRefreshSectionCounts(List<ScheduleBuilderPreviewLine> lines)
        {
            if (lines == null || lines.Count == 0)
                return;

            Color? currentBand = null;
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (line == null)
                    continue;

                if (line.Kind == ScheduleBuilderPreviewLine.LineKind.SectionHeader)
                {
                    currentBand = line.ReserveBandColor
                        ?? SectionColorForTitle(line.SectionTitle);
                    line.ReserveBandColor = currentBand;
                    continue;
                }

                if (line.Kind == ScheduleBuilderPreviewLine.LineKind.Trip && currentBand.HasValue)
                    line.ReserveBandColor = currentBand;
            }

            RefreshReserveSectionHeaderCounts(lines);
        }

        public static void RefreshReserveSectionHeaderCounts(List<ScheduleBuilderPreviewLine> lines)
        {
            if (lines == null)
                return;

            ReserveBucket? current = null;
            int headerIdx = -1;
            int tripCount = 0;

            void Flush()
            {
                if (headerIdx < 0 || !current.HasValue)
                    return;
                lines[headerIdx].SectionTitle = SectionTitleForBucket(current.Value, tripCount);
            }

            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (line?.Kind == ScheduleBuilderPreviewLine.LineKind.SectionHeader
                    && TryParseSectionBucket(line.SectionTitle, out var bucket))
                {
                    Flush();
                    current = bucket;
                    headerIdx = i;
                    tripCount = 0;
                    continue;
                }

                if (line?.Kind == ScheduleBuilderPreviewLine.LineKind.Trip && current.HasValue)
                    tripCount++;
            }

            Flush();
        }

    }

}


