using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    internal static partial class SupeyClusterRouting
    {
        private static async Task<double?> RoadMetersAsync(
            GeoPoint a, GeoPoint b, CancellationToken token)
        {
            var table = SupeyClusterOsrmTable.Current;
            if (table != null)
            {
                var m = table.Meters(a, b);
                if (m.HasValue) return m;
            }
            var leg = await SupeyOsrmLegs.GetLegAsync(a, b, token).ConfigureAwait(false);
            return leg.Ok ? (double?)leg.Meters : null;
        }

        private static async Task<double?> RoadSecondsAsync(
            GeoPoint a, GeoPoint b, CancellationToken token)
        {
            var table = SupeyClusterOsrmTable.Current;
            if (table != null)
            {
                var s = table.Seconds(a, b);
                if (s.HasValue) return s;
            }
            var leg = await SupeyOsrmLegs.GetLegAsync(a, b, token).ConfigureAwait(false);
            return leg.Ok ? (double?)leg.Seconds : null;
        }

        private static async Task<double?> FullTourMetersAsync(
            SupeyTripCluster c, List<int> puOrder, List<int> doOrder, CancellationToken token)
        {
            var table = SupeyClusterOsrmTable.Current;
            if (table != null)
            {
                var m = table.TourMeters(c, puOrder, doOrder);
                if (m.HasValue) return m;
            }
            var metrics = await SupeyOsrmLegs.TourMetricsAsync(c, puOrder, doOrder, token)
                .ConfigureAwait(false);
            return metrics.meters;
        }

        /// <summary>
        /// Visit each PU city once, in earliest-scheduled-PU order; within city by drive-in toward drop hub.
        /// </summary>
        internal static async Task<List<int>> BuildPickupOrderByCityBlocksAsync(
            SupeyTripCluster c, GeoPoint? routeStart, CancellationToken token)
        {
            int n = c?.Trips.Count ?? 0;
            var result = new List<int>(n);
            if (n == 0) return result;
            if (n == 1) { result.Add(0); return result; }

            GeoPoint hub = Centroid(c.DropoffPoints);
            if (hub.Lat == 0 && hub.Lng == 0)
                hub = c.DropoffCentroid;

            var byCity = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < n; i++)
            {
                string city = NormalizePickupCity(c.Trips[i]?.PUCity);
                if (!byCity.TryGetValue(city, out var list))
                {
                    list = new List<int>();
                    byCity[city] = list;
                }
                list.Add(i);
            }

            GeoPoint approach = routeStart.HasValue && SupeyOsrmLegs.IsRoutable(routeStart.Value)
                ? routeStart.Value
                : c.PickupCentroid;
            TimeSpan clockAtApproach = c.EffectiveEarliestPickup;

            var cityKeys = new List<string>(byCity.Keys);
            if (cityKeys.Count == 1)
            {
                clockAtApproach = await SortIndicesBestDriveInTownAsync(
                    c, byCity[cityKeys[0]], approach, hub, clockAtApproach, token)
                    .ConfigureAwait(false);
                result.AddRange(byCity[cityKeys[0]]);
                return result;
            }

            var cityRank = await OrderCitiesForPickupWindowsAsync(
                cityKeys, byCity, c, approach, token).ConfigureAwait(false);

            foreach (var city in cityRank)
            {
                var town = byCity[city];
                clockAtApproach = await SortIndicesBestDriveInTownAsync(
                    c, town, approach, hub, clockAtApproach, token)
                    .ConfigureAwait(false);
                result.AddRange(town);
                if (town.Count > 0)
                {
                    int last = town[town.Count - 1];
                    if (last >= 0 && last < c.PickupPoints.Count)
                        approach = c.PickupPoints[last];
                }
            }
            return result;
        }

        /// <summary>Shortest feasible drive in town; strict PU order only when appointments are 20+ min apart.</summary>
        private static async Task<TimeSpan> SortIndicesBestDriveInTownAsync(
            SupeyTripCluster c,
            List<int> indices,
            GeoPoint approach,
            GeoPoint hub,
            TimeSpan clockAtApproach,
            CancellationToken token)
        {
            if (indices == null || indices.Count <= 1)
                return clockAtApproach;

            if (indices.Count <= 7)
            {
                var best = await FindBestPickupPermutationAsync(
                    c, indices, approach, hub, clockAtApproach, token)
                    .ConfigureAwait(false);
                if (best != null && best.Count == indices.Count)
                {
                    indices.Clear();
                    indices.AddRange(best);
                    return await DepartureAfterOrderedTownAsync(
                        c, indices, approach, clockAtApproach, token).ConfigureAwait(false);
                }
            }

            var greedy = await GreedyDriveOrderInTownAsync(
                c, indices, approach, hub, clockAtApproach, token)
                .ConfigureAwait(false);
            if (greedy != null && greedy.Count == indices.Count)
            {
                indices.Clear();
                indices.AddRange(greedy);
            }
            return await DepartureAfterOrderedTownAsync(
                c, indices, approach, clockAtApproach, token).ConfigureAwait(false);
        }

        private static async Task<TimeSpan> DepartureAfterOrderedTownAsync(
            SupeyTripCluster c,
            List<int> ordered,
            GeoPoint approach,
            TimeSpan clockAtApproach,
            CancellationToken token)
        {
            if (ordered == null || ordered.Count == 0)
                return clockAtApproach;
            var arriveFirst = await EstimateArrivalAtTownFirstAsync(
                c, ordered, approach, clockAtApproach, token).ConfigureAwait(false);
            return SupeyDispatchDriveClock.DepartureAfterLastPickup(c, ordered, arriveFirst);
        }

        /// <summary>Earlier rider must be picked up first when PU times are 20+ minutes apart (same town).</summary>
        private static bool MustBePickedUpBefore(SupeyTripCluster c, int earlierIdx, int laterIdx)
        {
            TimeSpan gap = ScheduledPickup(c, laterIdx) - ScheduledPickup(c, earlierIdx);
            return gap >= TimeSpan.FromMinutes(SupeyClusterRouteBuilder.PickupPrecedenceMinutes);
        }

        private static bool PermutationRespectsPuPrecedence(SupeyTripCluster c, List<int> visitOrder)
        {
            for (int i = 0; i < visitOrder.Count; i++)
            {
                for (int j = i + 1; j < visitOrder.Count; j++)
                {
                    int first = visitOrder[i];
                    int second = visitOrder[j];
                    if (MustBePickedUpBefore(c, second, first))
                        return false;
                }
            }
            return true;
        }

        private sealed class PickupPermutationSearch
        {
            public List<int> BestOrder;
            public double BestMeters = double.MaxValue;
            public bool BestFeasible;
        }

        private static async Task<List<int>> FindBestPickupPermutationAsync(
            SupeyTripCluster c,
            List<int> indices,
            GeoPoint approach,
            GeoPoint hub,
            TimeSpan clockAtApproach,
            CancellationToken token)
        {
            var search = new PickupPermutationSearch();
            var current = new List<int>(indices.Count);
            var used = new bool[indices.Count];
            await TryPermutationDriveAsync(
                c, indices, used, 0, approach, hub, clockAtApproach, token, current, search)
                .ConfigureAwait(false);

            if (search.BestOrder != null && search.BestOrder.Count == indices.Count)
                return search.BestOrder;

            return await GreedyDriveOrderInTownAsync(
                c, indices, approach, hub, clockAtApproach, token).ConfigureAwait(false);
        }

        /// <summary>NN chain toward hub — never appointment-clock sort as fallback.</summary>
        private static async Task<List<int>> GreedyDriveOrderInTownAsync(
            SupeyTripCluster c,
            List<int> indices,
            GeoPoint approach,
            GeoPoint hub,
            TimeSpan clockAtApproach,
            CancellationToken token)
        {
            var remaining = new List<int>(indices);
            var ordered = new List<int>(indices.Count);
            GeoPoint pos = approach;
            TimeSpan clock = await EstimateArrivalAtTownFirstAsync(
                c, indices, approach, clockAtApproach, token).ConfigureAwait(false);

            while (remaining.Count > 0)
            {
                int best = -1;
                double bestM = double.MaxValue;
                foreach (int cand in remaining)
                {
                    var m = await RoadMetersAsync(pos, c.PickupPoints[cand], token).ConfigureAwait(false);
                    if (!m.HasValue) continue;
                    if (m.Value < bestM) { bestM = m.Value; best = cand; }
                }
                if (best < 0) { ordered.AddRange(remaining); break; }
                ordered.Add(best);
                remaining.Remove(best);
                var sec = await RoadSecondsAsync(pos, c.PickupPoints[best], token).ConfigureAwait(false);
                if (sec.HasValue)
                {
                    clock = clock.Add(TimeSpan.FromSeconds(sec.Value));
                    clock = ClockAfterPickupArrival(c, best, clock);
                }
                pos = c.PickupPoints[best];
            }
            return ordered;
        }

        private static async Task TryPermutationDriveAsync(
            SupeyTripCluster c,
            List<int> pool,
            bool[] used,
            int depth,
            GeoPoint approach,
            GeoPoint hub,
            TimeSpan clockAtApproach,
            CancellationToken token,
            List<int> current,
            PickupPermutationSearch search)
        {
            if (depth >= pool.Count)
            {
                if (!PermutationRespectsPuPrecedence(c, current))
                    return;

                double total = 0;
                bool feasible = true;
                GeoPoint pos = approach;
                TimeSpan clock = await EstimateArrivalAtTownFirstAsync(
                    c, current, approach, clockAtApproach, token).ConfigureAwait(false);

                for (int i = 0; i < current.Count; i++)
                {
                    int idx = current[i];
                    var legM = await RoadMetersAsync(pos, c.PickupPoints[idx], token).ConfigureAwait(false);
                    var legS = await RoadSecondsAsync(pos, c.PickupPoints[idx], token).ConfigureAwait(false);
                    if (!legM.HasValue || !legS.HasValue) { feasible = false; break; }
                    total += legM.Value;
                    clock = clock.Add(TimeSpan.FromSeconds(legS.Value));
                    if (!PickupFeasibleAt(c, idx, clock)) feasible = false;
                    clock = ClockAfterPickupArrival(c, idx, clock);
                    pos = c.PickupPoints[idx];
                }

                if (current.Count > 0)
                {
                    int last = current[current.Count - 1];
                    var tail = await RoadMetersAsync(c.PickupPoints[last], hub, token).ConfigureAwait(false);
                    if (tail.HasValue) total += tail.Value;
                }

                bool better = feasible && !search.BestFeasible
                    || (feasible == search.BestFeasible && total < search.BestMeters);
                if (better)
                {
                    search.BestMeters = total;
                    search.BestFeasible = feasible;
                    search.BestOrder = new List<int>(current);
                }
                return;
            }

            for (int i = 0; i < pool.Count; i++)
            {
                if (used[i]) continue;
                used[i] = true;
                current.Add(pool[i]);
                await TryPermutationDriveAsync(
                    c, pool, used, depth + 1, approach, hub, clockAtApproach, token, current, search)
                    .ConfigureAwait(false);
                current.RemoveAt(current.Count - 1);
                used[i] = false;
            }
        }

        private static TimeSpan ScheduledPickup(SupeyTripCluster c, int idx)
        {
            var t = SupeyDeskScheduleTiming.ScheduledPickupForBuild(c.Trips[idx]);
            if (t != TimeSpan.Zero) return t;
            return SupeyTripTimes.TryParsePU(c.Trips[idx]) ?? c.EarliestPickup;
        }

        private static bool PickupFeasibleAt(SupeyTripCluster c, int idx, TimeSpan arrive) =>
            SupeyDispatchDriveClock.FitsPickupWindow(c, idx, arrive);

        private static TimeSpan ClockAfterPickupArrival(SupeyTripCluster c, int idx, TimeSpan arrive) =>
            SupeyDispatchDriveClock.AfterPickup(c, idx, arrive);

        /// <summary>Drive arrival at first index in town — not anchored to scheduled PU time.</summary>
        private static async Task<TimeSpan> EstimateArrivalAtTownFirstAsync(
            SupeyTripCluster c,
            List<int> indices,
            GeoPoint approach,
            TimeSpan clockAtApproach,
            CancellationToken token)
        {
            if (indices == null || indices.Count == 0)
                return clockAtApproach;

            int firstIdx = indices[0];
            double? bestM = null;
            foreach (int idx in indices)
            {
                if (idx < 0 || idx >= c.PickupPoints.Count) continue;
                var m = await RoadMetersAsync(approach, c.PickupPoints[idx], token).ConfigureAwait(false);
                if (!m.HasValue) continue;
                if (!bestM.HasValue || m.Value < bestM.Value) { bestM = m; firstIdx = idx; }
            }

            TimeSpan arrive = clockAtApproach;
            var sec = await RoadSecondsAsync(approach, c.PickupPoints[firstIdx], token).ConfigureAwait(false);
            if (sec.HasValue)
                arrive = clockAtApproach.Add(TimeSpan.FromSeconds(sec.Value));
            return SupeyDispatchDriveClock.ArrivalAtPickup(c, firstIdx, arrive);
        }

        /// <summary>City order: earliest PU window first; ties broken by OSRM drive from current position.</summary>
        private static async Task<List<string>> OrderCitiesForPickupWindowsAsync(
            List<string> cityKeys,
            Dictionary<string, List<int>> byCity,
            SupeyTripCluster c,
            GeoPoint approach,
            CancellationToken token)
        {
            var remaining = new List<string>(cityKeys);
            var ordered = new List<string>(cityKeys.Count);
            GeoPoint pos = approach;

            while (remaining.Count > 0)
            {
                TimeSpan bestEarliest = TimeSpan.MaxValue;
                foreach (string city in remaining)
                {
                    TimeSpan min = EarliestPickupInCity(c, byCity[city]);
                    if (min < bestEarliest) bestEarliest = min;
                }

                var tier = new List<string>();
                foreach (string city in remaining)
                {
                    if (EarliestPickupInCity(c, byCity[city]) == bestEarliest)
                        tier.Add(city);
                }

                string pick;
                if (tier.Count == 1)
                    pick = tier[0];
                else
                    pick = await PickNearestCityFromApproachAsync(tier, byCity, c, pos, token)
                        .ConfigureAwait(false);

                ordered.Add(pick);
                remaining.Remove(pick);
                pos = CityCentroid(byCity[pick], c);
            }

            return ordered;
        }

        private static TimeSpan EarliestPickupInCity(SupeyTripCluster c, List<int> indices)
        {
            TimeSpan min = TimeSpan.MaxValue;
            foreach (int idx in indices)
            {
                var pu = ScheduledPickup(c, idx);
                if (pu < min) min = pu;
            }
            return min == TimeSpan.MaxValue ? c.EffectiveEarliestPickup : min;
        }

        private static async Task<string> PickNearestCityFromApproachAsync(
            List<string> tier,
            Dictionary<string, List<int>> byCity,
            SupeyTripCluster c,
            GeoPoint approach,
            CancellationToken token)
        {
            string best = tier[0];
            double bestM = double.MaxValue;
            foreach (string city in tier)
            {
                var cent = CityCentroid(byCity[city], c);
                var m = await RoadMetersAsync(approach, cent, token).ConfigureAwait(false);
                if (!m.HasValue) continue;
                if (m.Value < bestM)
                {
                    bestM = m.Value;
                    best = city;
                }
            }
            return best;
        }

        private static GeoPoint CityCentroid(List<int> indices, SupeyTripCluster c)
        {
            var pts = new List<GeoPoint>();
            foreach (int idx in indices)
            {
                if (idx >= 0 && idx < c.PickupPoints.Count)
                    pts.Add(c.PickupPoints[idx]);
            }
            return pts.Count > 0 ? Centroid(pts) : c.PickupCentroid;
        }

        private static string NormalizePickupCity(string city)
        {
            string c = (city ?? "").Trim();
            return c.Length == 0 ? "?" : c.ToUpperInvariant();
        }

        /// <summary>Apply PU order, OSRM drop chain, feasibility — no cross-city 2-opt.</summary>
        internal static async Task<bool> ApplyGroupTourFromPickupOrderAsync(
            SupeyTripCluster c,
            List<int> pu,
            GeoPoint? routeStart,
            CancellationToken token,
            bool requireFeasible)
        {
            int n = c?.Trips.Count ?? 0;
            if (pu == null || pu.Count != n) return false;

            ApplyOrders(c, pu, BuildDeadlineDropoffOrder(c));
            await BuildDropoffOrderGreedyAsync(c, token).ConfigureAwait(false);

            if (requireFeasible)
            {
                if (!await DropoffOrderMeetsDeadlinesAsync(c, c.PickupOrder, c.DropoffOrder, token)
                        .ConfigureAwait(false))
                    return false;
                if (!await PickupOrderMeetsScheduledWindowsAsync(c, c.PickupOrder, token, routeStart)
                        .ConfigureAwait(false))
                    return false;
            }

            c.RoadTourOptimized = await TryMarkRoadTourOptimizedAsync(c, token).ConfigureAwait(false);
            return c.RoadTourOptimized || !requireFeasible;
        }

        private static Task<(List<int> pu, List<int> doOrd)> RefineTourCopyAsync(
            SupeyTripCluster c, List<int> pu, List<int> doOrder, CancellationToken token) =>
            RefineTourCopyAsync(c, pu, doOrder, token, enforceDeadlines: true);

        private static async Task<(List<int> pu, List<int> doOrd)> RefineTourCopyAsync(
            SupeyTripCluster c, List<int> pu, List<int> doOrder, CancellationToken token,
            bool enforceDeadlines)
        {
            ApplyOrders(c, new List<int>(pu), new List<int>(doOrder));
            await RefinePickupOrder2OptAsync(c, token, enforceDeadlines).ConfigureAwait(false);
            await BuildDropoffOrderGreedyAsync(c, token).ConfigureAwait(false);
            await RefineDropoffOrderByDistanceAsync(c, token, enforceDeadlines).ConfigureAwait(false);
            return (new List<int>(c.PickupOrder), new List<int>(c.DropoffOrder));
        }

        internal static async Task<bool> TryMarkRoadTourOptimizedAsync(
            SupeyTripCluster c, CancellationToken token)
        {
            if (c == null || c.PickupOrder.Count == 0 || c.DropoffOrder.Count == 0)
                return false;
            var table = SupeyClusterOsrmTable.Current;
            if (table != null)
            {
                var m = table.TourMeters(c, c.PickupOrder, c.DropoffOrder);
                if (m.HasValue) return true;
            }
            var full = await FullTourMetersAsync(c, c.PickupOrder, c.DropoffOrder, token)
                .ConfigureAwait(false);
            if (full.HasValue) return true;
            var metrics = await SupeyOsrmLegs.TourMetricsAsync(c, c.PickupOrder, c.DropoffOrder, token)
                .ConfigureAwait(false);
            return metrics.meters.HasValue;
        }

        /// <summary>OSRM nearest-neighbor drop chain after the last pickup in <paramref name="puOrder"/>.</summary>
        internal static async Task<List<int>> BuildGreedyDropoffForPickupOrderAsync(
            SupeyTripCluster c, List<int> puOrder, CancellationToken token)
        {
            if (c == null || c.Trips.Count == 0)
                return new List<int>();
            ApplyOrders(c, new List<int>(puOrder), IdentityOrder(c.Trips.Count));
            await BuildDropoffOrderGreedyAsync(c, token).ConfigureAwait(false);
            return new List<int>(c.DropoffOrder);
        }

        private static async Task BuildGeographicTourAsync(
            SupeyTripCluster c, List<int> puOut, List<int> doOut, CancellationToken token,
            GeoPoint? routeStart)
        {
            puOut.Clear();
            doOut.Clear();
            int n = c.Trips.Count;

            var scratch = new SupeyTripCluster();
            CopyTourContext(c, scratch);

            scratch.PickupOrder.Clear();
            scratch.PickupOrder.AddRange(
                await BuildPickupOrderByCityBlocksAsync(scratch, routeStart, token).ConfigureAwait(false));

            await BuildDropoffOrderGreedyAsync(scratch, token).ConfigureAwait(false);

            puOut.AddRange(scratch.PickupOrder);
            doOut.AddRange(scratch.DropoffOrder);
        }

        private static async Task<bool> AllPointsNearOsrmAsync(
            GeoPoint hub, List<GeoPoint> pts, double radiusMeters, CancellationToken token)
        {
            if (pts == null || pts.Count == 0) return false;
            foreach (var p in pts)
            {
                var m = await RoadMetersAsync(hub, p, token).ConfigureAwait(false);
                if (!m.HasValue || m.Value > radiusMeters) return false;
            }
            return true;
        }

        private static async Task<int> IndexFarthestFromOsrmAsync(
            List<GeoPoint> pts, GeoPoint hub, CancellationToken token)
        {
            int best = 0;
            double bestD = -1;
            for (int i = 0; i < pts.Count; i++)
            {
                var d = await RoadMetersAsync(pts[i], hub, token).ConfigureAwait(false);
                if (!d.HasValue) continue;
                if (d.Value > bestD) { bestD = d.Value; best = i; }
            }
            return best;
        }

        /// <summary>First pickup on the van's approach path (home or previous group DO).</summary>
        private static async Task<int> IndexNearestPickupFromAsync(
            List<GeoPoint> pts, GeoPoint from, CancellationToken token)
        {
            int best = 0;
            double? bestD = null;
            for (int i = 0; i < pts.Count; i++)
            {
                var d = await RoadMetersAsync(from, pts[i], token).ConfigureAwait(false);
                if (!d.HasValue) continue;
                if (!bestD.HasValue || d.Value < bestD.Value) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>Scattered home pickups: each next stop is closest OSRM leg from current.</summary>
        private static async Task BuildPickupOrderNearestNeighborAsync(
            SupeyTripCluster c, int startIdx, CancellationToken token)
        {
            c.PickupOrder.Clear();
            int n = c.Trips.Count;
            var remaining = new List<int>(n);
            for (int i = 0; i < n; i++) remaining.Add(i);
            int current = remaining.Contains(startIdx) ? startIdx : remaining[0];
            c.PickupOrder.Add(current);
            remaining.Remove(current);
            while (remaining.Count > 0)
            {
                int best = remaining[0];
                double? bestDist = null;
                foreach (int cand in remaining)
                {
                    var d = await RoadMetersAsync(c.PickupPoints[current], c.PickupPoints[cand], token)
                        .ConfigureAwait(false);
                    if (!d.HasValue) continue;
                    if (!bestDist.HasValue || d.Value < bestDist.Value)
                    {
                        bestDist = d;
                        best = cand;
                    }
                }
                c.PickupOrder.Add(best);
                remaining.Remove(best);
                current = best;
            }
        }

        private static async Task BuildPickupOrderTowardHubAsync(
            SupeyTripCluster c, int startIdx, GeoPoint hub, CancellationToken token)
        {
            c.PickupOrder.Clear();
            int n = c.Trips.Count;
            var remaining = new List<int>(n);
            for (int i = 0; i < n; i++) remaining.Add(i);
            int current = remaining.Contains(startIdx) ? startIdx : remaining[0];
            c.PickupOrder.Add(current);
            remaining.Remove(current);
            while (remaining.Count > 0)
            {
                int best = remaining[0];
                double bestScore = double.MaxValue;
                foreach (int cand in remaining)
                {
                    var leg = await RoadMetersAsync(c.PickupPoints[current], c.PickupPoints[cand], token)
                        .ConfigureAwait(false);
                    var toward = await RoadMetersAsync(c.PickupPoints[cand], hub, token).ConfigureAwait(false);
                    if (!leg.HasValue || !toward.HasValue) continue;
                    double score = leg.Value + toward.Value * 0.35;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = cand;
                    }
                }
                c.PickupOrder.Add(best);
                remaining.Remove(best);
                current = best;
            }
        }

        private static async Task BuildDropoffOrderFromHubAsync(
            SupeyTripCluster c, GeoPoint hub, CancellationToken token)
        {
            c.DropoffOrder.Clear();
            int n = c.Trips.Count;
            var remaining = new List<int>(n);
            for (int i = 0; i < n; i++) remaining.Add(i);

            var timed = new List<int>();
            var open = new List<int>();
            foreach (int i in remaining)
            {
                var d = SupeyTripTimes.TryParseDO(c.Trips[i]);
                if (d.HasValue && d.Value > TimeSpan.Zero) timed.Add(i);
                else open.Add(i);
            }
            timed.Sort(CompareDropoffDeadlineIndex(c));

            if (c.PickupOrder.Count == 0)
                for (int i = 0; i < n; i++) c.PickupOrder.Add(i);
            int puIdx = c.PickupOrder[0];
            if (puIdx < 0 || puIdx >= c.PickupPoints.Count) puIdx = 0;
            var currentPt = c.PickupPoints[puIdx];

            foreach (int t in timed)
            {
                c.DropoffOrder.Add(t);
                remaining.Remove(t);
                currentPt = c.DropoffPoints[t];
            }

            var pool = open.Count > 0 ? open : new List<int>(remaining);
            while (pool.Count > 0)
            {
                int best = pool[0];
                double? bestDist = await RoadMetersAsync(currentPt, c.DropoffPoints[best], token)
                    .ConfigureAwait(false);
                if (!bestDist.HasValue) bestDist = double.MaxValue;
                for (int i = 1; i < pool.Count; i++)
                {
                    int cand = pool[i];
                    var d = await RoadMetersAsync(currentPt, c.DropoffPoints[cand], token)
                        .ConfigureAwait(false);
                    if (d.HasValue && d.Value < bestDist.Value) { bestDist = d; best = cand; }
                }
                c.DropoffOrder.Add(best);
                pool.Remove(best);
                currentPt = c.DropoffPoints[best];
            }
        }

        private static async Task BuildDropoffOrderGreedyAsync(SupeyTripCluster c, CancellationToken token)
        {
            c.DropoffOrder.Clear();
            int n = c.Trips.Count;

            const double hubRadiusMeters = 6500.0;
            var dropHub = Centroid(c.DropoffPoints);
            if (await AllPointsNearOsrmAsync(dropHub, c.DropoffPoints, hubRadiusMeters, token)
                    .ConfigureAwait(false))
            {
                c.DropoffOrder.AddRange(SupeyClusterRouting.BuildDeadlineDropoffOrderPublic(c));
                return;
            }

            var remaining = new List<int>(n);
            for (int i = 0; i < n; i++) remaining.Add(i);

            if (c.PickupOrder.Count == 0)
                for (int i = 0; i < n; i++) c.PickupOrder.Add(i);
            int lastPuIdx = c.PickupOrder[c.PickupOrder.Count - 1];
            if (lastPuIdx < 0 || lastPuIdx >= c.PickupPoints.Count) lastPuIdx = 0;
            var currentPt = c.PickupPoints[lastPuIdx];
            int firstPuIdx = c.PickupOrder[0];
            TimeSpan firstArrive = await EstimateArrivalAtTownFirstAsync(
                c, c.PickupOrder, c.PickupCentroid, c.EffectiveEarliestPickup, token).ConfigureAwait(false);
            var currentTime = SupeyDispatchDriveClock.DepartureAfterLastPickup(
                c, c.PickupOrder, firstArrive);

            while (remaining.Count > 0)
            {
                int best = -1;
                double bestDist = double.MaxValue;
                foreach (int cand in remaining)
                {
                    var legSec = await RoadSecondsAsync(currentPt, c.DropoffPoints[cand], token)
                        .ConfigureAwait(false);
                    if (!legSec.HasValue) continue;
                    var arrive = currentTime.Add(TimeSpan.FromSeconds(legSec.Value));
                    if (!SupeyDispatchDriveClock.FitsDropWindow(c.Trips[cand], arrive))
                        continue;
                    var d = await RoadMetersAsync(currentPt, c.DropoffPoints[cand], token).ConfigureAwait(false);
                    if (!d.HasValue) continue;
                    if (d.Value < bestDist) { bestDist = d.Value; best = cand; }
                }
                if (best < 0)
                {
                    best = remaining[0];
                    double nearest = double.MaxValue;
                    for (int i = 0; i < remaining.Count; i++)
                    {
                        int cand = remaining[i];
                        var d = await RoadMetersAsync(currentPt, c.DropoffPoints[cand], token)
                            .ConfigureAwait(false);
                        if (d.HasValue && d.Value < nearest) { nearest = d.Value; best = cand; }
                    }
                }
                var hop = await RoadSecondsAsync(currentPt, c.DropoffPoints[best], token).ConfigureAwait(false);
                if (hop.HasValue)
                {
                    currentTime = currentTime.Add(TimeSpan.FromSeconds(hop.Value));
                    currentTime = SupeyDispatchDriveClock.AfterDropoff(c.Trips[best], currentTime);
                }
                currentPt = c.DropoffPoints[best];
                c.DropoffOrder.Add(best);
                remaining.Remove(best);
            }
        }

        private static async Task<double> PickupChainSecondsAsync(SupeyTripCluster c, CancellationToken token)
        {
            if (c.PickupOrder.Count <= 1) return 0;
            double sec = 0;
            for (int i = 1; i < c.PickupOrder.Count; i++)
            {
                var leg = await RoadSecondsAsync(
                    c.PickupPoints[c.PickupOrder[i - 1]],
                    c.PickupPoints[c.PickupOrder[i]], token).ConfigureAwait(false);
                if (leg.HasValue) sec += leg.Value;
            }
            return sec;
        }

        private static Task RefinePickupOrder2OptAsync(SupeyTripCluster c, CancellationToken token) =>
            RefinePickupOrder2OptAsync(c, token, enforceDeadlines: true);

        private static async Task RefinePickupOrder2OptAsync(
            SupeyTripCluster c, CancellationToken token, bool enforceDeadlines)
        {
            int n = c.PickupOrder.Count;
            if (n <= 2) return;

            bool improved = true;
            int safety = n * n;
            while (improved && safety-- > 0)
            {
                token.ThrowIfCancellationRequested();
                improved = false;
                for (int i = 0; i < n - 1; i++)
                {
                    for (int j = i + 1; j < n; j++)
                    {
                        var trialPu = new List<int>(c.PickupOrder);
                        int tmp = trialPu[i];
                        trialPu[i] = trialPu[j];
                        trialPu[j] = tmp;
                        if (enforceDeadlines
                            && (!await DropoffOrderMeetsDeadlinesAsync(c, trialPu, c.DropoffOrder, token)
                                    .ConfigureAwait(false)
                                || !await PickupOrderMeetsScheduledWindowsAsync(c, trialPu, token, routeStart: null)
                                    .ConfigureAwait(false)))
                            continue;
                        var before = await FullTourMetersAsync(c, c.PickupOrder, c.DropoffOrder, token)
                            .ConfigureAwait(false);
                        var after = await FullTourMetersAsync(c, trialPu, c.DropoffOrder, token)
                            .ConfigureAwait(false);
                        if (!before.HasValue || !after.HasValue) continue;
                        if (after.Value + 50 < before.Value)
                        {
                            c.PickupOrder.Clear();
                            c.PickupOrder.AddRange(trialPu);
                            improved = true;
                        }
                    }
                }
            }
        }

        private static Task RefineDropoffOrderByDistanceAsync(SupeyTripCluster c, CancellationToken token) =>
            RefineDropoffOrderByDistanceAsync(c, token, enforceDeadlines: true);

        private static async Task RefineDropoffOrderByDistanceAsync(
            SupeyTripCluster c, CancellationToken token, bool enforceDeadlines)
        {
            int n = c.DropoffOrder.Count;
            if (n <= 2) return;
            if (await DropoffsShareSingleHubOsrmAsync(c, 150.0, token).ConfigureAwait(false)) return;

            bool improved = true;
            int safety = n * n;
            while (improved && safety-- > 0)
            {
                improved = false;
                for (int i = 0; i < n - 1; i++)
                {
                    for (int j = i + 1; j < n; j++)
                    {
                        var trial = new List<int>(c.DropoffOrder);
                        int tmp = trial[i];
                        trial[i] = trial[j];
                        trial[j] = tmp;
                        if (enforceDeadlines
                            && !await DropoffOrderMeetsDeadlinesAsync(c, c.PickupOrder, trial, token)
                                .ConfigureAwait(false))
                            continue;
                        var before = await DropoffPathMetersAsync(c, c.DropoffOrder, token).ConfigureAwait(false);
                        var after = await DropoffPathMetersAsync(c, trial, token).ConfigureAwait(false);
                        if (!before.HasValue || !after.HasValue) continue;
                        if (after.Value + 50 < before.Value)
                        {
                            c.DropoffOrder.Clear();
                            c.DropoffOrder.AddRange(trial);
                            improved = true;
                        }
                    }
                }
            }
        }

        private static async Task<bool> DropoffsShareSingleHubOsrmAsync(
            SupeyTripCluster c, double radiusMeters, CancellationToken token)
        {
            if (c?.DropoffPoints == null || c.DropoffPoints.Count <= 1)
                return true;
            var hub = Centroid(c.DropoffPoints);
            return await AllPointsNearOsrmAsync(hub, c.DropoffPoints, radiusMeters, token)
                .ConfigureAwait(false);
        }

        private static async Task<double?> DropoffPathMetersAsync(
            SupeyTripCluster c, List<int> order, CancellationToken token)
        {
            if (order.Count == 0) return 0;
            int lastPu = c.PickupOrder.Count > 0 ? c.PickupOrder[c.PickupOrder.Count - 1] : c.Trips.Count - 1;
            double total = 0;
            var first = await RoadMetersAsync(c.PickupPoints[lastPu], c.DropoffPoints[order[0]], token)
                .ConfigureAwait(false);
            if (!first.HasValue) return null;
            total += first.Value;
            for (int i = 1; i < order.Count; i++)
            {
                var leg = await RoadMetersAsync(
                    c.DropoffPoints[order[i - 1]], c.DropoffPoints[order[i]], token).ConfigureAwait(false);
                if (!leg.HasValue) return null;
                total += leg.Value;
            }
            return total;
        }

        /// <summary>
        /// Simulate OSRM drive along <paramref name="puOrder"/>; each PU must land in desk window.
        /// <paramref name="arrivalAtFirstPickup"/> is the real time at stop 1 (deadhead) — not forced on-time/early.
        /// </summary>
        internal static async Task<bool> PickupOrderMeetsScheduledWindowsAsync(
            SupeyTripCluster c,
            List<int> puOrder,
            CancellationToken token,
            GeoPoint? routeStart,
            TimeSpan? arrivalAtFirstPickup = null)
        {
            if (c == null || puOrder == null || puOrder.Count == 0) return true;
            if (puOrder.Count == 1) return true;
            if (c.PickupPoints == null || c.PickupPoints.Count < c.Trips.Count)
                return false;

            int firstIdx = puOrder[0];
            TimeSpan current;
            GeoPoint pos;
            if (arrivalAtFirstPickup.HasValue)
            {
                current = arrivalAtFirstPickup.Value;
                pos = c.PickupPoints[firstIdx];
            }
            else if (routeStart.HasValue && SupeyOsrmLegs.IsRoutable(routeStart.Value))
            {
                var toFirst = await RoadSecondsAsync(routeStart.Value, c.PickupPoints[firstIdx], token)
                    .ConfigureAwait(false);
                if (!toFirst.HasValue) return false;
                current = await EstimateArrivalAtTownFirstAsync(
                    c, puOrder, routeStart.Value, c.EffectiveEarliestPickup, token)
                    .ConfigureAwait(false);
                pos = c.PickupPoints[firstIdx];
            }
            else
            {
                current = await EstimateArrivalAtTownFirstAsync(
                    c, puOrder, c.PickupCentroid, c.EffectiveEarliestPickup, token)
                    .ConfigureAwait(false);
                pos = c.PickupPoints[firstIdx];
            }

            if (!PickupFeasibleAt(c, firstIdx, current))
                return false;
            current = ClockAfterPickupArrival(c, firstIdx, current);

            for (int step = 1; step < puOrder.Count; step++)
            {
                int idx = puOrder[step];
                if (idx < 0 || idx >= c.Trips.Count) return false;
                var leg = await RoadSecondsAsync(pos, c.PickupPoints[idx], token).ConfigureAwait(false);
                if (!leg.HasValue) return false;
                current = current.Add(TimeSpan.FromSeconds(leg.Value));
                if (!PickupFeasibleAt(c, idx, current))
                    return false;
                current = ClockAfterPickupArrival(c, idx, current);
                pos = c.PickupPoints[idx];
            }
            return true;
        }

        private static async Task<bool> DropoffOrderMeetsDeadlinesAsync(
            SupeyTripCluster c, List<int> puOrder, List<int> doOrder, CancellationToken token)
        {
            if (doOrder == null || doOrder.Count == 0)
                return true;
            if (puOrder == null || puOrder.Count == 0)
                return true;

            int lastPu = puOrder[puOrder.Count - 1];
            TimeSpan firstArrive = await EstimateArrivalAtTownFirstAsync(
                c, puOrder, c.PickupCentroid, c.EffectiveEarliestPickup, token).ConfigureAwait(false);
            TimeSpan depart = SupeyDispatchDriveClock.DepartureAfterLastPickup(c, puOrder, firstArrive);
            var puToDo = await RoadSecondsAsync(
                c.PickupPoints[lastPu], c.DropoffPoints[doOrder[0]], token).ConfigureAwait(false);
            if (!puToDo.HasValue) return false;
            depart = depart.Add(TimeSpan.FromSeconds(puToDo.Value));

            var scratch = new SupeyTripCluster();
            CopyTourContext(c, scratch);
            scratch.DropoffOrder.Clear();
            scratch.DropoffOrder.AddRange(doOrder);
            var result = SupeyDispatchDriveClock.ProjectDropRun(scratch, depart);
            return result.feasible;
        }

        public static async Task ApplySharedDropHubLegTimesAsync(SupeyTripCluster c, CancellationToken token)
        {
            if (c == null || c.DropoffOrder == null || c.DropoffOrder.Count <= 1
                || c.DropoffPoints == null || c.DropoffLegSeconds == null)
                return;

            const double sameHubMeters = 150.0;
            const double boardingSeconds = 180.0;

            for (int i = 0; i < c.DropoffOrder.Count; i++)
            {
                if (i == 0) continue;
                int prevIdx = c.DropoffOrder[i - 1];
                int idx = c.DropoffOrder[i];
                if (prevIdx < 0 || idx < 0
                    || prevIdx >= c.DropoffPoints.Count || idx >= c.DropoffPoints.Count)
                    continue;
                var m = await RoadMetersAsync(c.DropoffPoints[prevIdx], c.DropoffPoints[idx], token)
                    .ConfigureAwait(false);
                if (!m.HasValue || m.Value > sameHubMeters)
                    continue;
                if (i < c.DropoffLegSeconds.Count)
                    c.DropoffLegSeconds[i] = boardingSeconds;
            }

            double tail = 0;
            for (int i = 0; i < c.DropoffLegSeconds.Count; i++)
                tail += c.DropoffLegSeconds[i];
            c.TailDriveSeconds = tail;
        }
    }
}
