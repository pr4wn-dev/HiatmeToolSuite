using System;

using System.Collections.Generic;



namespace Hiatme_Tool_Suite_v3

{

    /// <summary>

    /// Modivcare will-call = scheduled <b>pickup</b> is midnight (00:00). Matches legacy Analyzer

    /// (<c>PUTime.Replace(" ", "") == "00:00"</c>) plus batch time normalization.

    /// </summary>

    internal static class SupeyWillCallPickup

    {

        public static bool IsPickupWillCall(MCDownloadedTrip trip) =>

            trip != null && IsPickupWillCallTime(trip.PUTime);

        /// <summary>Return-leg placeholder on DO (not a will call). Modivcare often sends 00:00 dropoff on B/C legs.</summary>
        public static bool IsDropoffMidnightPlaceholder(MCDownloadedTrip trip)
        {
            if (trip == null) return false;
            return IsPickupWillCallTime(trip.DOTime) || IsPickupWillCallTime(trip.SchedDOTime);
        }

        public static int CountPickupWillCallsInList(IEnumerable<MCDownloadedTrip> trips)
        {
            if (trips == null) return 0;
            int n = 0;
            foreach (var t in trips)
                if (IsPickupWillCall(t)) n++;
            return n;
        }

        public static int CountDropoffMidnightInList(IEnumerable<MCDownloadedTrip> trips)
        {
            if (trips == null) return 0;
            int n = 0;
            foreach (var t in trips)
                if (IsDropoffMidnightPlaceholder(t)) n++;
            return n;
        }



        public static bool IsPickupWillCallTime(string puTime)

        {

            if (string.IsNullOrWhiteSpace(puTime))

                return false;



            string compact = CompactTime(puTime);

            if (compact.Length == 0)

                return false;



            if (string.Equals(compact, "00:00", StringComparison.OrdinalIgnoreCase)

                || string.Equals(compact, "0:00", StringComparison.OrdinalIgnoreCase)

                || string.Equals(compact, "00:00:00", StringComparison.OrdinalIgnoreCase)

                || string.Equals(compact, "0:00:00", StringComparison.OrdinalIgnoreCase))

                return true;



            string norm = MCTimeCorrection.NormalizeBatchTime(puTime);

            if (string.Equals(norm, "00:00", StringComparison.OrdinalIgnoreCase))

                return true;



            if (MCTimeCorrection.TryParseBatchTime(puTime, out var dt)

                && dt.TimeOfDay == TimeSpan.Zero)

                return true;



            return false;

        }



        /// <summary>

        /// Pull every 00:00-PU trip off driver tabs into <see cref="SupeyScheduleResult.ReservesWillCalls"/>

        /// (or reroute when out-of-area). Call after any BUILD path before binding the UI.

        /// </summary>

        public static void EnforceOnResult(SupeyScheduleResult result, IList<MCDownloadedTrip> allTrips = null)

        {

            if (result == null) return;



            if (result.DriverPlans != null)

            {

                foreach (var plan in result.DriverPlans)

                {

                    if (plan?.Groups == null) continue;

                    for (int gi = plan.Groups.Count - 1; gi >= 0; gi--)

                    {

                        var cluster = plan.Groups[gi];

                        if (cluster?.Trips == null) continue;

                        for (int ti = cluster.Trips.Count - 1; ti >= 0; ti--)

                        {

                            var t = cluster.Trips[ti];

                            if (!IsPickupWillCall(t)) continue;

                            cluster.Trips.RemoveAt(ti);

                            SupeyReserveBuckets.AddToReserves(result, t);

                        }

                        if (cluster.Trips.Count == 0)

                            plan.Groups.RemoveAt(gi);

                    }

                }

            }



            if (allTrips == null) return;

            foreach (var t in allTrips)

            {

                if (t == null || !IsPickupWillCall(t)) continue;

                if (IsInAnyReserveList(result, t)) continue;

                SupeyReserveBuckets.AddToReserves(result, t);

            }

        }



        internal static bool IsInAnyReserveList(SupeyScheduleResult result, MCDownloadedTrip t) =>

            result.Reserves.Contains(t)

            || result.ReservesWillCalls.Contains(t)

            || result.ReservesReroute.Contains(t);



        private static string CompactTime(string raw)

        {

            if (string.IsNullOrWhiteSpace(raw)) return "";

            string s = raw.Trim()

                .Replace(" ", "")

                .Replace("\u00a0", "");

            if (s.IndexOf("nbsp", StringComparison.OrdinalIgnoreCase) >= 0)

                s = s.Replace("nbsp", "").Replace("NBSP", "");

            return s;

        }

    }

}


