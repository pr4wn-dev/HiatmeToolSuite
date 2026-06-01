using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Home → corridor (Auburn/Lewiston) → nearest-next group ordering for each driver day.
    /// Deadheads use OSRM; every append is checked with PU/DO window simulation.
    /// </summary>
    internal sealed partial class SupeyScheduleAlgorithm
    {
        private const double CorridorChainBonusCapMeters = 20000.0;
        private const double CorridorChainBonusPerMeter = 0.02;
        private const double CorridorZoneBonusSeconds = 240.0;
        private const double HomeTowardCorridorBonusSeconds = 360.0;

        private static readonly GeoPoint AuburnHub = new GeoPoint(44.0978, -70.2312);
        private static readonly GeoPoint LewistonHub = new GeoPoint(44.1004, -70.2148);

        private static async Task OrderDriverGroupsCorridorAsync(SupeyDriverPlan plan, CancellationToken token)
        {
            if (plan.Groups.Count <= 1) return;
            var ordered = await BuildCorridorGroupOrderAsync(plan, token).ConfigureAwait(false);
            plan.Groups.Clear();
            foreach (var g in ordered)
                plan.Groups.Add(g);
            await ReorderDriverGroupsAsync(plan, token).ConfigureAwait(false);
        }

        private static async Task<List<SupeyTripCluster>> BuildCorridorGroupOrderAsync(
            SupeyDriverPlan plan, CancellationToken token)
        {
            if (plan.Groups.Count == 0)
                return new List<SupeyTripCluster>();

            var shiftStart = plan.Driver.ParseShiftStart() ?? TimeSpan.Zero;
            var pool = new List<SupeyTripCluster>(plan.Groups);
            var ordered = new List<SupeyTripCluster>();
            GeoPoint loc = plan.HomeGeo ?? plan.Groups[0].PickupCentroid;

            while (pool.Count > 0)
            {
                SupeyTripCluster best = null;
                double bestScore = double.MaxValue;

                foreach (var c in pool)
                {
                    var trial = new List<SupeyTripCluster>(ordered) { c };
                    if (!await GroupsChronologicallyFeasibleAsync(plan, trial, shiftStart, token)
                        .ConfigureAwait(false))
                        continue;

                    var dhLeg = await SupeyOsrmLegs.GetLegAsync(loc, FirstPickupGeo(c), token)
                        .ConfigureAwait(false);
                    if (!dhLeg.Ok) continue;

                    double score = dhLeg.Meters
                        - CorridorChainDeadheadCredit(dhLeg.Meters)
                        - CorridorZoneBonusSeconds * CorridorZoneFraction(c)
                        - await HomeTowardCorridorCreditAsync(plan.HomeGeo, loc, c, token).ConfigureAwait(false);

                    score += SupeyClusterTimeSplit.MinPickupTime(c).TotalMinutes * 0.05;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = c;
                    }
                }

                if (best == null)
                {
                    pool.Sort((a, b) => SupeyClusterTimeSplit.MinPickupTime(a)
                        .CompareTo(SupeyClusterTimeSplit.MinPickupTime(b)));
                    foreach (var c in pool)
                    {
                        var trial = new List<SupeyTripCluster>(ordered) { c };
                        if (!await GroupsChronologicallyFeasibleAsync(plan, trial, shiftStart, token)
                            .ConfigureAwait(false))
                            continue;
                        best = c;
                        break;
                    }
                    if (best == null)
                    {
                        ordered.Add(pool[0]);
                        pool.RemoveAt(0);
                        loc = LastDropoffPoint(ordered[ordered.Count - 1]);
                        continue;
                    }
                }

                ordered.Add(best);
                pool.Remove(best);
                loc = LastDropoffPoint(best);
            }

            return ordered;
        }

        private static double CorridorChainDeadheadCredit(double deadheadMeters)
        {
            double capped = Math.Min(CorridorChainBonusCapMeters, Math.Max(0, deadheadMeters));
            return capped * CorridorChainBonusPerMeter;
        }

        private static double CorridorZoneFraction(SupeyTripCluster cluster)
        {
            if (cluster == null || cluster.Trips.Count == 0) return 0;
            int hits = 0;
            foreach (var t in cluster.Trips)
            {
                if (IsCorridorCity(t.PUCity) || IsCorridorCity(t.DOCITY))
                    hits++;
            }
            return hits / (double)cluster.Trips.Count;
        }

        private static async Task<double> HomeTowardCorridorCreditAsync(
            GeoPoint? home, GeoPoint fromLoc, SupeyTripCluster cluster, CancellationToken token)
        {
            if (!home.HasValue || cluster == null) return 0;
            var toAub = await SupeyOsrmLegs.GetLegAsync(home.Value, AuburnHub, token).ConfigureAwait(false);
            var toLew = await SupeyOsrmLegs.GetLegAsync(home.Value, LewistonHub, token).ConfigureAwait(false);
            if (!toAub.Ok && !toLew.Ok) return 0;
            double toHub = double.MaxValue;
            if (toAub.Ok) toHub = Math.Min(toHub, toAub.Meters);
            if (toLew.Ok) toHub = Math.Min(toHub, toLew.Meters);

            var puAub = await SupeyOsrmLegs.GetLegAsync(cluster.PickupCentroid, AuburnHub, token)
                .ConfigureAwait(false);
            var puLew = await SupeyOsrmLegs.GetLegAsync(cluster.PickupCentroid, LewistonHub, token)
                .ConfigureAwait(false);
            double pickupToHub = double.MaxValue;
            if (puAub.Ok) pickupToHub = Math.Min(pickupToHub, puAub.Meters);
            if (puLew.Ok) pickupToHub = Math.Min(pickupToHub, puLew.Meters);
            if (pickupToHub == double.MaxValue) pickupToHub = 0;

            var fromPu = await SupeyOsrmLegs.GetLegAsync(fromLoc, cluster.PickupCentroid, token)
                .ConfigureAwait(false);
            if (!fromPu.Ok) return 0;
            if (fromPu.Meters > toHub * 1.35 + 8000.0)
                return 0;

            double along = Math.Max(0, toHub - pickupToHub);
            return HomeTowardCorridorBonusSeconds * Math.Min(1.0, along / Math.Max(1.0, toHub));
        }

        private static bool IsCorridorCity(string city)
        {
            if (string.IsNullOrWhiteSpace(city)) return false;
            string c = city.Trim().ToUpperInvariant();
            return c.Contains("AUBURN") || c.Contains("LEWISTON") || c.Contains("LISBON")
                || c.Contains("GREENE") || c.Contains("TURNER") || c.Contains("POLAND")
                || c.Contains("NEW GLOUCESTER") || c.Contains("WALES") || c.Contains("SABATTUS")
                || c.Contains("MINOT") || c.Contains("MECHANIC FALLS");
        }

        private static async Task<double> CorridorAssignmentBonusAsync(
            SupeyDriverPlan plan,
            SupeyTripCluster cluster,
            GeoPoint lastLoc,
            int firstPu,
            CancellationToken token)
        {
            double bonus = CorridorZoneBonusSeconds * CorridorZoneFraction(cluster);
            if (plan.Groups.Count == 0)
            {
                if (plan.HomeGeo.HasValue)
                    bonus += await HomeTowardCorridorCreditAsync(plan.HomeGeo, plan.HomeGeo.Value, cluster, token)
                        .ConfigureAwait(false);
            }
            else if (firstPu >= 0 && cluster.PickupPoints != null && firstPu < cluster.PickupPoints.Count)
            {
                var leg = await SupeyOsrmLegs.GetLegAsync(lastLoc, cluster.PickupPoints[firstPu], token)
                    .ConfigureAwait(false);
                if (leg.Ok)
                    bonus += CorridorChainDeadheadCredit(leg.Meters);
            }
            return bonus;
        }

        private static async Task<bool> GroupsChronologicallyFeasibleAsync(
            SupeyDriverPlan plan,
            List<SupeyTripCluster> order,
            TimeSpan shiftStart,
            CancellationToken token)
        {
            var current = shiftStart;
            var loc = PlanAnchorGeo(plan);
            foreach (var c in order)
            {
                var leg = await SupeyOsrmLegs.GetLegAsync(loc, FirstPickupGeo(c), token).ConfigureAwait(false);
                if (!leg.Ok) return false;

                var arrival = current.Add(TimeSpan.FromSeconds(leg.Seconds));
                if (!SupeyClusterRouting.IsValidVisitOrder(c.PickupOrder, c.Trips.Count))
                    return false;
                if (!await SupeyClusterRouting.PickupOrderMeetsScheduledWindowsAsync(
                        c, new System.Collections.Generic.List<int>(c.PickupOrder), token, loc, arrival)
                    .ConfigureAwait(false))
                    return false;
                var (ok, end, _, _) = ProjectClusterFeasibility(c, arrival);
                if (!ok) return false;
                current = end;
                loc = LastDropoffPoint(c);
            }
            return true;
        }

        private static async Task ReorderDriverGroupsAsync(SupeyDriverPlan plan, CancellationToken token)
        {
            if (plan.Groups.Count <= 2) return;
            plan.Groups.Sort((a, b) => SupeyClusterTimeSplit.MinPickupTime(a)
                .CompareTo(SupeyClusterTimeSplit.MinPickupTime(b)));
            bool improved = true;
            int safety = plan.Groups.Count * 2;
            var shiftStart = plan.Driver.ParseShiftStart() ?? TimeSpan.Zero;
            while (improved && safety-- > 0)
            {
                improved = false;
                for (int i = 0; i < plan.Groups.Count - 1; i++)
                {
                    var a = plan.Groups[i];
                    var b = plan.Groups[i + 1];
                    var keepLeg = await SupeyOsrmLegs.GetLegAsync(LastDropoffPoint(a), FirstPickupGeo(b), token)
                        .ConfigureAwait(false);
                    var swapLeg = await SupeyOsrmLegs.GetLegAsync(LastDropoffPoint(b), FirstPickupGeo(a), token)
                        .ConfigureAwait(false);
                    if (!keepLeg.Ok || !swapLeg.Ok) continue;
                    if (swapLeg.Meters + 200 >= keepLeg.Meters) continue;
                    if (SupeyClusterTimeSplit.MinPickupTime(a) > SupeyClusterTimeSplit.MinPickupTime(b))
                        continue;
                    var trial = new List<SupeyTripCluster>(plan.Groups);
                    trial[i] = b;
                    trial[i + 1] = a;
                    if (!await GroupsChronologicallyFeasibleAsync(plan, trial, shiftStart, token)
                        .ConfigureAwait(false))
                        continue;
                    plan.Groups[i] = b;
                    plan.Groups[i + 1] = a;
                    improved = true;
                }
                plan.Groups.Sort((x, y) => SupeyClusterTimeSplit.MinPickupTime(x)
                    .CompareTo(SupeyClusterTimeSplit.MinPickupTime(y)));
            }
        }

        private static async Task<(TimeSpan time, GeoPoint loc)> ProjectedLastEventAsync(
            SupeyDriverPlan p, CancellationToken token)
        {
            var shiftStart = p.Driver.ParseShiftStart() ?? TimeSpan.Zero;
            if (p.Groups.Count == 0)
                return (shiftStart, PlanAnchorGeo(p));

            var visitOrder = await BuildCorridorGroupOrderAsync(p, token).ConfigureAwait(false);
            var current = shiftStart;
            var loc = PlanAnchorGeo(p);
            foreach (var c in visitOrder)
            {
                int firstPu = FirstPickupIndex(c);
                GeoPoint pu = firstPu >= 0 && c.PickupPoints != null && firstPu < c.PickupPoints.Count
                    ? c.PickupPoints[firstPu]
                    : FirstPickupGeo(c);
                var leg = await SupeyOsrmLegs.GetLegAsync(loc, pu, token).ConfigureAwait(false);
                if (!leg.Ok)
                    return (shiftStart, PlanAnchorGeo(p));

                var arrival = current.Add(TimeSpan.FromSeconds(leg.Seconds));
                current = ProjectClusterEnd(c, arrival);
                loc = LastDropoffPoint(c);
            }
            return (current, loc);
        }

    }
}
