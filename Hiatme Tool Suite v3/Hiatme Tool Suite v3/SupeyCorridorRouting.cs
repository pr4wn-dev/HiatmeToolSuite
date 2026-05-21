using System;
using System.Collections.Generic;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Home → corridor (Auburn/Lewiston) → nearest-next group ordering for each driver day.
    /// Every append is checked with the same PU/DO window simulation as greedy assignment.
    /// </summary>
    internal sealed partial class SupeyScheduleAlgorithm
    {
        private const double CorridorChainBonusCapMeters = 20000.0;
        private const double CorridorChainBonusPerMeter = 0.02;
        private const double CorridorZoneBonusSeconds = 240.0;
        private const double HomeTowardCorridorBonusSeconds = 360.0;

        private static readonly GeoPoint AuburnHub = new GeoPoint(44.0978, -70.2312);
        private static readonly GeoPoint LewistonHub = new GeoPoint(44.1004, -70.2148);

        /// <summary>
        /// Reorders <see cref="SupeyDriverPlan.Groups"/> as a feasible nearest-next chain from home,
        /// biased toward the Auburn/Lewiston corridor, then runs a short adjacent-swap polish.
        /// </summary>
        private static void OrderDriverGroupsCorridor(SupeyDriverPlan plan)
        {
            if (plan.Groups.Count <= 1) return;
            var ordered = BuildCorridorGroupOrder(plan);
            plan.Groups.Clear();
            foreach (var g in ordered)
                plan.Groups.Add(g);
            ReorderDriverGroups(plan);
        }

        /// <summary>Feasible home → nearest-next chain used for scoring and final sequencing.</summary>
        private static List<SupeyTripCluster> BuildCorridorGroupOrder(SupeyDriverPlan plan)
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
                    if (!GroupsChronologicallyFeasible(plan, trial, shiftStart))
                        continue;

                    double dh = HaversineMeters(loc, FirstPickupGeo(c));
                    double score = dh
                        - CorridorChainDeadheadCredit(dh)
                        - CorridorZoneBonusSeconds * CorridorZoneFraction(c)
                        - HomeTowardCorridorCredit(plan.HomeGeo, loc, c);

                    score += c.EarliestPickup.TotalMinutes * 0.05;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = c;
                    }
                }

                if (best == null)
                {
                    pool.Sort((a, b) => a.EarliestPickup.CompareTo(b.EarliestPickup));
                    foreach (var c in pool)
                    {
                        var trial = new List<SupeyTripCluster>(ordered) { c };
                        if (!GroupsChronologicallyFeasible(plan, trial, shiftStart))
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

        private static double HomeTowardCorridorCredit(GeoPoint? home, GeoPoint fromLoc, SupeyTripCluster cluster)
        {
            if (!home.HasValue || cluster == null) return 0;
            double toHub = Math.Min(
                HaversineMeters(home.Value, AuburnHub),
                HaversineMeters(home.Value, LewistonHub));
            double pickupToHub = Math.Min(
                HaversineMeters(cluster.PickupCentroid, AuburnHub),
                HaversineMeters(cluster.PickupCentroid, LewistonHub));
            double fromToPickup = HaversineMeters(fromLoc, cluster.PickupCentroid);

            if (fromToPickup > toHub * 1.35 + 8000.0)
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

        private static double CorridorAssignmentBonus(SupeyDriverPlan plan, SupeyTripCluster cluster, GeoPoint lastLoc, int firstPu)
        {
            double bonus = CorridorZoneBonusSeconds * CorridorZoneFraction(cluster);
            if (plan.Groups.Count == 0)
            {
                if (plan.HomeGeo.HasValue)
                    bonus += HomeTowardCorridorCredit(plan.HomeGeo, plan.HomeGeo.Value, cluster);
            }
            else if (firstPu >= 0 && cluster.PickupPoints != null && firstPu < cluster.PickupPoints.Count)
                bonus += CorridorChainDeadheadCredit(HaversineMeters(lastLoc, cluster.PickupPoints[firstPu]));
            return bonus;
        }
    }
}
