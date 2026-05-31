using System;
using System.Text.RegularExpressions;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Desk timing from 99× "Schedule for 2026" workbooks (Cherie/Remie dispatch patterns).
    /// Person + drop/pickup place rules drive BUILD feasibility — see Rules.cs (generated).
    /// </summary>
    internal static partial class SupeyDeskScheduleTiming
    {
        private static readonly Regex CannotArriveBefore = new Regex(
            @"CANNOT\s+ARRIVE\s+BEFORE\s+(?<t>\d{1,2}\s*:?\s*\d{2}\s*(?:AM|PM)?)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex CannotDropBefore = new Regex(
            @"CANNOT\s+BE\s+DROPPED\s+OFF\s+BEFORE\s+(?<t>\d{1,2}\s*:?\s*\d{2}\s*(?:AM|PM)?)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex RequestsLaterPu = new Regex(
            @"REQUESTS?\s+LATER\s+(?:A\s*LEG\s+)?P(?:/U|U)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public const double DefaultEarlyPuAllowanceMinutes = 29.0;
        private const double MaxEarlyPuAllowanceMinutes = 105.0;
        private const double MinEarlyPuAllowanceMinutes = 20.0;

        public static TimeSpan EffectiveEarliestPickup(SupeyTripCluster c)
        {
            if (c == null) return TimeSpan.Zero;
            if (!c.IsAllALeg) return c.EarliestPickup;
            double allow = EarlyPuAllowanceMinutesForCluster(c);
            var start = c.EarliestPickup.Subtract(TimeSpan.FromMinutes(allow));
            return start < TimeSpan.Zero ? TimeSpan.Zero : start;
        }

        public static TimeSpan EffectiveLatestPickup(SupeyTripCluster c)
        {
            if (c == null) return TimeSpan.Zero;
            if (!c.IsAllALeg) return c.LatestPickup;
            double allow = EarlyPuAllowanceMinutesForCluster(c);
            var start = c.LatestPickup.Subtract(TimeSpan.FromMinutes(allow));
            return start < TimeSpan.Zero ? TimeSpan.Zero : start;
        }

        /// <summary>
        /// Mixed A+B template groups: do not wait until afternoon B-leg PU before morning DO drops.
        /// </summary>
        public static TimeSpan EffectiveLatestPickupForFeasibility(SupeyTripCluster c)
        {
            if (c == null) return TimeSpan.Zero;
            if (c.IsAllALeg) return EffectiveLatestPickup(c);

            TimeSpan latestMorning = GetMorningPickupPhaseLatest(c, out int morningPuCount);
            if (morningPuCount <= 0 || latestMorning <= TimeSpan.Zero)
                return c.LatestPickup;

            double allow = EarlyPuAllowanceMinutesForCluster(c);
            var start = latestMorning.Subtract(TimeSpan.FromMinutes(allow));
            return start < TimeSpan.Zero ? TimeSpan.Zero : start;
        }

        /// <summary>PU-chain seconds before the drop run (excludes afternoon B return PU when split failed).</summary>
        public static double HeadPickupSecondsForFeasibility(SupeyTripCluster c)
        {
            double total = c.IntraClusterDriveSeconds - c.TailDriveSeconds;
            if (total < 0) total = 0;
            if (c == null || c.IsAllALeg || c.Trips.Count <= 1) return total;

            GetMorningPickupPhaseLatest(c, out int morningPuCount);
            int puCount = c.PickupOrder.Count > 0 ? c.PickupOrder.Count : c.Trips.Count;
            if (morningPuCount <= 0 || morningPuCount >= puCount) return total;
            return total * (morningPuCount / (double)puCount);
        }

        private static TimeSpan GetMorningPickupPhaseLatest(SupeyTripCluster c, out int morningPickupCount)
        {
            morningPickupCount = 0;
            if (c == null || c.Trips.Count == 0) return TimeSpan.Zero;

            var order = PickupVisitIndices(c);
            int stopAt = order.Count - 1;
            for (int i = 0; i < order.Count; i++)
            {
                int idx = order[i];
                if (idx < 0 || idx >= c.Trips.Count) continue;
                var t = c.Trips[idx];
                if (SupeyScheduleAlgorithm.DetectLegPublic(t.TripNumber) == 'B')
                {
                    var d = SupeyTripTimes.TryParseDO(t);
                    if (!d.HasValue || d.Value <= TimeSpan.Zero)
                    {
                        stopAt = i - 1;
                        break;
                    }
                }
            }
            if (stopAt < 0) stopAt = 0;

            TimeSpan latest = TimeSpan.Zero;
            for (int i = 0; i <= stopAt; i++)
            {
                int idx = order[i];
                if (idx < 0 || idx >= c.Trips.Count) continue;
                var pu = ScheduledPickupForBuild(c.Trips[idx]);
                if (pu > latest) latest = pu;
            }
            morningPickupCount = stopAt + 1;
            return latest;
        }

        private static System.Collections.Generic.List<int> PickupVisitIndices(SupeyTripCluster c)
        {
            var visit = SupeyClusterDisplayOrder.PickupVisitIndices(c);
            return visit is System.Collections.Generic.List<int> list
                ? list
                : new System.Collections.Generic.List<int>(visit);
        }

        public static double EarlyPuAllowanceMinutesForCluster(SupeyTripCluster c)
        {
            double max = DefaultEarlyPuAllowanceMinutes;
            if (c == null) return max;
            foreach (var t in c.Trips)
            {
                double a = EarlyPuAllowanceMinutesBeforeScheduledPu(t);
                if (a > max) max = a;
            }
            return max;
        }

        /// <summary>
        /// Minutes before scheduled PU the driver may start (from desk sheet PU→DO windows per person/place).
        /// </summary>
        public static double EarlyPuAllowanceMinutesBeforeScheduledPu(MCDownloadedTrip t)
        {
            if (t == null || SupeyScheduleAlgorithm.DetectLegPublic(t.TripNumber) != 'A')
                return 0;
            if (DisallowsEarlyPuBeforeScheduled(t))
                return 0;

            double allow = DefaultEarlyPuAllowanceMinutes;
            string clientKey = ClientTimingKey(t);
            if (!string.IsNullOrEmpty(clientKey)
                && ClientEarlyPuAllowanceMinutes.TryGetValue(clientKey, out double clientAllow))
                allow = clientAllow;

            string doHub = DropHubKey(t);
            if (!string.IsNullOrEmpty(doHub)
                && DoHubEarlyPuAllowanceMinutes.TryGetValue(doHub, out double doAllow)
                && doAllow > allow)
                allow = doAllow;

            string puHub = PickupHubKey(t);
            if (!string.IsNullOrEmpty(puHub)
                && PuHubEarlyPuAllowanceMinutes.TryGetValue(puHub, out double puAllow)
                && puAllow > allow)
                allow = puAllow;

            if (allow > MaxEarlyPuAllowanceMinutes) allow = MaxEarlyPuAllowanceMinutes;
            if (allow < MinEarlyPuAllowanceMinutes) allow = MinEarlyPuAllowanceMinutes;
            return allow;
        }

        public static bool DisallowsEarlyPuBeforeScheduled(MCDownloadedTrip t)
        {
            if (t == null) return false;
            if (RequestsLaterPu.IsMatch(t.Comments ?? ""))
                return true;
            string key = ClientTimingKey(t);
            if (!string.IsNullOrEmpty(key) && ClientNoEarlyPu.Contains(key))
                return true;
            string name = NormalizeClientName(t);
            if (name.Contains("BROWN") && name.Contains("JOSHUA"))
                return true;
            if (name.Contains("TUTTLE") && name.Contains("SIERRA"))
                return true;
            return false;
        }

        public static TimeSpan? EarliestPickupFloorFromComments(MCDownloadedTrip t)
        {
            var m = CannotArriveBefore.Match(t?.Comments ?? "");
            if (!m.Success) return null;
            return ParseTimeToken(m.Groups["t"].Value);
        }

        public static TimeSpan ScheduledPickupForBuild(MCDownloadedTrip t)
        {
            var sched = SupeyTripTimes.TryParsePU(t) ?? TimeSpan.Zero;
            var floor = EarliestPickupFloorFromComments(t);
            if (floor.HasValue && sched < floor.Value)
                return floor.Value;
            return sched;
        }

        public static TimeSpan? EarliestDropoffForFeasibility(MCDownloadedTrip t)
        {
            if (t == null) return null;
            var m = CannotDropBefore.Match(t.Comments ?? "");
            if (m.Success)
            {
                var fromComment = ParseTimeToken(m.Groups["t"].Value);
                if (fromComment.HasValue) return fromComment;
            }
            return SupeyTripTimes.TryParseEarliestDropoff(t);
        }

        public static string DropHubKey(MCDownloadedTrip t)
        {
            string street = NormalizeStreet(t?.DOStreet);
            string city = NormalizeStreet(t?.DOCITY);
            if (street.IndexOf("MANLEY", StringComparison.OrdinalIgnoreCase) >= 0) return "MANLEY";
            if (street.IndexOf("MINOT", StringComparison.OrdinalIgnoreCase) >= 0) return "MINOT";
            if (street.IndexOf("FALCON", StringComparison.OrdinalIgnoreCase) >= 0) return "FALCON";
            if (street.IndexOf("618 MAIN", StringComparison.OrdinalIgnoreCase) >= 0) return "618_MAIN";
            if (street.IndexOf("646 MAIN", StringComparison.OrdinalIgnoreCase) >= 0) return "646_MAIN";
            if (street.IndexOf("63 BROAD", StringComparison.OrdinalIgnoreCase) >= 0) return "63_BROAD";
            if (street.IndexOf("23 CROSS", StringComparison.OrdinalIgnoreCase) >= 0) return "23_CROSS";
            if (street.IndexOf("20 EAST", StringComparison.OrdinalIgnoreCase) >= 0
                || (street.IndexOf("EAST AVE", StringComparison.OrdinalIgnoreCase) >= 0
                    && city.IndexOf("LEWISTON", StringComparison.OrdinalIgnoreCase) >= 0))
                return "20_EAST";
            return "";
        }

        public static string PickupHubKey(MCDownloadedTrip t)
        {
            string street = NormalizeStreet(t?.PUStreet);
            string city = NormalizeStreet(t?.PUCity);
            if (street.IndexOf("MANLEY", StringComparison.OrdinalIgnoreCase) >= 0) return "PU_MANLEY";
            if (street.IndexOf("MINOT", StringComparison.OrdinalIgnoreCase) >= 0
                && city.IndexOf("AUBURN", StringComparison.OrdinalIgnoreCase) >= 0)
                return "PU_MINOT_AUBURN";
            if (street.IndexOf("FALCON", StringComparison.OrdinalIgnoreCase) >= 0) return "PU_FALCON";
            if (street.IndexOf("646 MAIN", StringComparison.OrdinalIgnoreCase) >= 0) return "PU_646_MAIN";
            if (street.IndexOf("618 MAIN", StringComparison.OrdinalIgnoreCase) >= 0) return "PU_618_MAIN";
            if (street.IndexOf("23 CROSS", StringComparison.OrdinalIgnoreCase) >= 0) return "PU_23_CROSS";
            if (street.IndexOf("63 BROAD", StringComparison.OrdinalIgnoreCase) >= 0) return "PU_63_BROAD";
            if (street.IndexOf("20 EAST", StringComparison.OrdinalIgnoreCase) >= 0
                || (street.IndexOf("EAST AVE", StringComparison.OrdinalIgnoreCase) >= 0
                    && city.IndexOf("LEWISTON", StringComparison.OrdinalIgnoreCase) >= 0))
                return "PU_20_EAST";
            if (street.IndexOf("10 FALCON", StringComparison.OrdinalIgnoreCase) >= 0) return "PU_10_FALCON";
            return "";
        }

        private static string ClientTimingKey(MCDownloadedTrip t) =>
            (t?.ClientFullName ?? "").Trim().ToUpperInvariant();

        private static string NormalizeClientName(MCDownloadedTrip t) =>
            ((t?.ClientLastName ?? "") + " " + (t?.ClientFullName ?? "")).Trim().ToUpperInvariant();

        private static string NormalizeStreet(string s) =>
            (s ?? "").Trim().ToUpperInvariant().Replace(".", "").Replace(",", "");

        private static TimeSpan? ParseTimeToken(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            string s = raw.Trim().Replace(" ", "");
            if (!s.Contains(":") && s.Length >= 3)
                s = s.Substring(0, s.Length - 2) + ":" + s.Substring(s.Length - 2);
            return SupeyTripTimes.TryParse(s);
        }
    }
}

