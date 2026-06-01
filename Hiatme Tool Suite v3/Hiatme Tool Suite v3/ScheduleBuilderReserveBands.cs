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



        /// <summary>Banned, will call, no-go reroute, else reservers. Driver template matches may keep no-go on-tab.</summary>

        public static ReserveBucket Classify(MCDownloadedTrip trip)

        {

            if (trip == null) return ReserveBucket.Reserver;

            if (ScheduleBuilderBannedClients.IsBanned(trip))

                return ReserveBucket.Banned;

            if (IsWillCallTrip(trip))

                return ReserveBucket.WillCall;

            if (SupeyOutOfArea.MatchTrip(trip) != null)

                return ReserveBucket.Reroute;

            return ReserveBucket.Reserver;

        }

        /// <summary>00:00 PU (legacy Analyzer) or Modivcare comments contain WILL CALL.</summary>

        public static bool IsWillCallTrip(MCDownloadedTrip trip)

        {

            if (trip == null) return false;

            if (SupeyWillCallPickup.IsPickupWillCall(trip)) return true;

            return HasWillCallComment(trip.Comments);

        }

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

                if (!IsWillCallTrip(t)) continue;

                total++;

                if (SupeyWillCallPickup.IsPickupWillCall(t))

                    puMidnight++;

                else

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

            if (reroutes != null && reroutes.Count > 0)

            {

                lines.Add(new ScheduleBuilderPreviewLine

                {

                    Kind = ScheduleBuilderPreviewLine.LineKind.SectionHeader,

                    SectionTitle = "Reroutes (" + reroutes.Count + ")",

                });

                foreach (var t in reroutes.OrderBy(x => x?.PUTime ?? ""))

                    lines.Add(TripLine(t, RerouteBand));

            }

            if (banned != null && banned.Count > 0)

            {

                lines.Add(new ScheduleBuilderPreviewLine

                {

                    Kind = ScheduleBuilderPreviewLine.LineKind.SectionHeader,

                    SectionTitle = "Banned clients (" + banned.Count + ")",

                });

                foreach (var t in banned.OrderBy(x => x?.PUTime ?? ""))

                    lines.Add(TripLine(t, BannedBand));

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

            if (reroutes != null && reroutes.Count > 0)

            {

                n++;

                var g = new SupeyTripCluster { GroupNumber = n, GroupColor = RerouteBand };

                foreach (var t in reroutes)

                    if (t != null) g.Trips.Add(t);

                groups.Add(g);

            }

            if (banned != null && banned.Count > 0)

            {

                n++;

                var g = new SupeyTripCluster { GroupNumber = n, GroupColor = BannedBand };

                foreach (var t in banned)

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


