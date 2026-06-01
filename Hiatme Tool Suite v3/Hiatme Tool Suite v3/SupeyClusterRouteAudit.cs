using System;
using System.Collections.Generic;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Post-build checks that pickup visit order matches dispatch rules.</summary>
    internal static class SupeyClusterRouteAudit
    {
        public static void AppendViolations(SupeyScheduleResult result)
        {
            if (result?.DriverPlans == null) return;

            foreach (var plan in result.DriverPlans)
            {
                if (plan?.Groups == null) continue;
                string driver = plan.Driver?.Name ?? "Driver";
                foreach (var g in plan.Groups)
                {
                    if (g == null || g.Trips.Count < 2) continue;
                    foreach (string msg in AuditCluster(driver, g))
                    {
                        result.BuildWarnings.Add(new SupeyWarning(
                            SupeyWarningKind.BuildDiagnostic,
                            "",
                            "Route audit",
                            msg));
                    }
                }
            }
        }

        private static IEnumerable<string> AuditCluster(string driverName, SupeyTripCluster g)
        {
            int n = g.Trips.Count;
            if (!SupeyClusterRouting.IsValidVisitOrder(g.PickupOrder, n))
            {
                yield return driverName + " Group " + g.GroupNumber
                    + ": PickupOrder missing or invalid — route notes cannot match the road.";
                yield break;
            }

            if (g.RoadTourOptimized && SupeyClusterDisplayOrder.ClusterRowsNeedReorder(g))
            {
                yield return driverName + " Group " + g.GroupNumber
                    + ": trip list order does not match road pickup order (re-run BUILD post-build).";
            }

            for (int i = 0; i < g.PickupOrder.Count; i++)
            {
                for (int j = i + 1; j < g.PickupOrder.Count; j++)
                {
                    int first = g.PickupOrder[i];
                    int second = g.PickupOrder[j];
                    if (PuMinutesApart(g, second, first) >= SupeyClusterRouteBuilder.PickupPrecedenceMinutes)
                    {
                        yield return driverName + " Group " + g.GroupNumber
                            + ": " + ClientLabel(g.Trips[second]) + " (earlier PU) scheduled after "
                            + ClientLabel(g.Trips[first]) + " in road order.";
                    }
                }
            }

            var cityFirstPu = new Dictionary<string, (int visitPos, TimeSpan pu)>(StringComparer.OrdinalIgnoreCase);
            for (int visitPos = 0; visitPos < g.PickupOrder.Count; visitPos++)
            {
                int idx = g.PickupOrder[visitPos];
                string city = NormalizeCity(g.Trips[idx]?.PUCity);
                TimeSpan pu = ScheduledPu(g, idx);
                if (!cityFirstPu.TryGetValue(city, out var prev) || pu < prev.pu)
                    cityFirstPu[city] = (visitPos, pu);
            }

            var cities = new List<KeyValuePair<string, (int visitPos, TimeSpan pu)>>(cityFirstPu);
            cities.Sort((a, b) =>
            {
                int cmp = a.Value.pu.CompareTo(b.Value.pu);
                return cmp != 0 ? cmp : a.Value.visitPos.CompareTo(b.Value.visitPos);
            });

            for (int c = 1; c < cities.Count; c++)
            {
                if (cities[c].Value.pu < cities[c - 1].Value.pu
                    && cities[c].Value.visitPos < cities[c - 1].Value.visitPos)
                {
                    yield return driverName + " Group " + g.GroupNumber
                        + ": city " + cities[c].Key + " (earlier PU window) visited after "
                        + cities[c - 1].Key + " on the road.";
                }
            }
        }

        private static int PuMinutesApart(SupeyTripCluster g, int earlierIdx, int laterIdx)
        {
            TimeSpan gap = ScheduledPu(g, laterIdx) - ScheduledPu(g, earlierIdx);
            return (int)Math.Round(gap.TotalMinutes);
        }

        private static TimeSpan ScheduledPu(SupeyTripCluster g, int idx)
        {
            var t = SupeyDeskScheduleTiming.ScheduledPickupForBuild(g.Trips[idx]);
            if (t != TimeSpan.Zero) return t;
            return SupeyTripTimes.TryParsePU(g.Trips[idx]) ?? g.EarliestPickup;
        }

        private static string NormalizeCity(string city)
        {
            string c = (city ?? "").Trim();
            return c.Length == 0 ? "?" : c.ToUpperInvariant();
        }

        private static string ClientLabel(MCDownloadedTrip t)
        {
            if (t == null) return "rider";
            string full = (t.ClientFullName ?? "").Trim();
            if (full.Length == 0) return t.TripNumber ?? "rider";
            int comma = full.IndexOf(',');
            return comma > 0 ? full.Substring(0, comma).Trim() : full;
        }
    }
}
