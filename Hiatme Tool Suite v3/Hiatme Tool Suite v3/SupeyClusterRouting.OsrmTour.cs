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
        /// Hubward collection: visit PU cities from far side of drop hub back toward hub (dispatch path).
        /// All PUs in a city while you are there; within city by scheduled PU time.
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

            var cityKeys = new List<string>(byCity.Keys);
            if (cityKeys.Count == 1)
            {
                await SortIndicesBestDriveInTownAsync(c, byCity[cityKeys[0]], approach, hub, token)
                    .ConfigureAwait(false);
                result.AddRange(byCity[cityKeys[0]]);
                return result;
            }

            // Cities far from the shared drop area first, then closer — no leave-town-and-return.
            var cityRank = new List<(string city, double farMeters)>();
            foreach (string city in cityKeys)
            {
                double far = 0;
                foreach (int idx in byCity[city])
                {
                    if (idx < 0 || idx >= c.PickupPoints.Count) continue;
                    var m = await RoadMetersAsync(c.PickupPoints[idx], hub, token).ConfigureAwait(false);
                    if (m.HasValue && m.Value > far)
                        far = m.Value;
                }
                cityRank.Add((city, far));
            }

            cityRank.Sort((a, b) =>
            {
                int cmp = b.farMeters.CompareTo(a.farMeters);
                if (cmp != 0) return cmp;
                return string.Compare(a.city, b.city, StringComparison.OrdinalIgnoreCase);
            });

            if (routeStart.HasValue && SupeyOsrmLegs.IsRoutable(routeStart.Value))
                cityRank = await ReorderCitiesFromVanPositionAsync(
                    cityRank, byCity, c, routeStart.Value, token).ConfigureAwait(false);

            foreach (var entry in cityRank)
            {
                var town = byCity[entry.city];
                await SortIndicesBestDriveInTownAsync(c, town, approach, hub, token)
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

        /// <summary>Order PUs in town along drive from <paramref name="approach"/> toward <paramref name="hub"/> (no hop-back).</summary>
        private static async Task SortIndicesBestDriveInTownAsync(
            SupeyTripCluster c,
            List<int> indices,
            GeoPoint approach,
            GeoPoint hub,
            CancellationToken token)
        {
            if (indices == null || indices.Count <= 1) return;

            SortIndicesByCorridorProjection(c, indices, approach, hub);

            if (indices.Count <= 7)
            {
                var best = await FindBestPickupPermutationAsync(c, indices, approach, hub, token)
                    .ConfigureAwait(false);
                if (best != null && best.Count == indices.Count)
                {
                    indices.Clear();
                    indices.AddRange(best);
                }
            }
        }

        /// <summary>Progress along approach→hub axis (pass-through), then PU time.</summary>
        private static void SortIndicesByCorridorProjection(
            SupeyTripCluster c,
            List<int> indices,
            GeoPoint approach,
            GeoPoint hub)
        {
            double dx = hub.Lng - approach.Lng;
            double dy = hub.Lat - approach.Lat;
            double len2 = dx * dx + dy * dy;
            if (len2 < 1e-12)
            {
                indices.Sort((a, b) => ScheduledPickup(c, a).CompareTo(ScheduledPickup(c, b)));
                return;
            }

            indices.Sort((a, b) =>
            {
                double pa = CorridorProjection(c.PickupPoints[a], approach, dx, dy, len2);
                double pb = CorridorProjection(c.PickupPoints[b], approach, dx, dy, len2);
                int cmp = pa.CompareTo(pb);
                if (cmp != 0) return cmp;
                cmp = ScheduledPickup(c, a).CompareTo(ScheduledPickup(c, b));
                return cmp != 0 ? cmp : a.CompareTo(b);
            });
        }

        private static double CorridorProjection(GeoPoint p, GeoPoint origin, double dx, double dy, double len2)
        {
            double px = p.Lng - origin.Lng;
            double py = p.Lat - origin.Lat;
            return (px * dx + py * dy) / len2;
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
            CancellationToken token)
        {
            var search = new PickupPermutationSearch();
            var current = new List<int>(indices.Count);
            var used = new bool[indices.Count];
            await TryPermutationDriveAsync(c, indices, used, 0, approach, hub, token, current, search)
                .ConfigureAwait(false);
            return search.BestOrder;
        }

        private static async Task TryPermutationDriveAsync(
            SupeyTripCluster c,
            List<int> pool,
            bool[] used,
            int depth,
            GeoPoint approach,
            GeoPoint hub,
            CancellationToken token,
            List<int> current,
            PickupPermutationSearch search)
        {
            if (depth >= pool.Count)
            {
                double dx = hub.Lng - approach.Lng;
                double dy = hub.Lat - approach.Lat;
                double len2 = dx * dx + dy * dy;
                double total = 0;
                bool feasible = true;
                bool corridorOk = len2 >= 1e-12;
                double prevProj = 0;
                GeoPoint pos = approach;
                TimeSpan clock = await EstimateClockEnteringTownAsync(c, pool, approach, token)
                    .ConfigureAwait(false);

                for (int i = 0; i < current.Count; i++)
                {
                    int idx = current[i];
                    var legM = await RoadMetersAsync(pos, c.PickupPoints[idx], token).ConfigureAwait(false);
                    var legS = await RoadSecondsAsync(pos, c.PickupPoints[idx], token).ConfigureAwait(false);
                    if (!legM.HasValue || !legS.HasValue) { feasible = false; break; }
                    total += legM.Value;
                    clock = clock.Add(TimeSpan.FromSeconds(legS.Value));
                    var scheduled = ScheduledPickup(c, idx);
                    if (clock < scheduled) clock = scheduled;
                    if (!PickupFeasibleAt(c, idx, clock)) feasible = false;
                    if (corridorOk)
                    {
                        double proj = CorridorProjection(c.PickupPoints[idx], approach, dx, dy, len2);
                        if (proj < prevProj - 1e-6) corridorOk = false;
                        prevProj = proj;
                    }
                    pos = c.PickupPoints[idx];
                }

                if (!corridorOk) feasible = false;

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
                await TryPermutationDriveAsync(c, pool, used, depth + 1, approach, hub, token, current, search)
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

        private static bool PickupFeasibleAt(SupeyTripCluster c, int idx, TimeSpan arrive)
        {
            var scheduled = ScheduledPickup(c, idx);
            double cap = 16;
            foreach (var trip in c.Trips)
            {
                string tn = trip?.TripNumber ?? "";
                if (tn.Length > 0 && char.ToUpperInvariant(tn[tn.Length - 1]) == 'A')
                {
                    cap = 16;
                    break;
                }
            }
            return arrive <= scheduled.Add(TimeSpan.FromMinutes(cap));
        }

        private static async Task<TimeSpan> EstimateClockEnteringTownAsync(
            SupeyTripCluster c,
            List<int> indices,
            GeoPoint approach,
            CancellationToken token)
        {
            TimeSpan minPu = TimeSpan.MaxValue;
            foreach (int idx in indices)
            {
                var pu = ScheduledPickup(c, idx);
                if (pu < minPu) minPu = pu;
            }
            if (minPu == TimeSpan.MaxValue)
                minPu = c.EffectiveEarliestPickup;

            int nearest = indices[0];
            double? bestM = null;
            foreach (int idx in indices)
            {
                if (idx < 0 || idx >= c.PickupPoints.Count) continue;
                var m = await RoadMetersAsync(approach, c.PickupPoints[idx], token).ConfigureAwait(false);
                if (!m.HasValue) continue;
                if (!bestM.HasValue || m.Value < bestM.Value) { bestM = m; nearest = idx; }
            }
            var sec = await RoadSecondsAsync(approach, c.PickupPoints[nearest], token).ConfigureAwait(false);
            var arrive = minPu;
            if (sec.HasValue)
            {
                var driveArrive = c.EffectiveEarliestPickup.Add(TimeSpan.FromSeconds(sec.Value));
                if (driveArrive > arrive) arrive = driveArrive;
            }
            return arrive;
        }

        /// <summary>Van already in the corridor: visit cities on the drive from here toward hub.</summary>
        private static async Task<List<(string city, double farMeters)>> ReorderCitiesFromVanPositionAsync(
            List<(string city, double farMeters)> cityRank,
            Dictionary<string, List<int>> byCity,
            SupeyTripCluster c,
            GeoPoint routeStart,
            CancellationToken token)
        {
            var scored = new List<(string city, double farMeters, double fromVan)>();
            foreach (var entry in cityRank)
            {
                var cent = CityCentroid(byCity[entry.city], c);
                var m = await RoadMetersAsync(routeStart, cent, token).ConfigureAwait(false);
                scored.Add((entry.city, entry.farMeters, m ?? double.MaxValue));
            }
            scored.Sort((a, b) =>
            {
                int cmp = a.fromVan.CompareTo(b.fromVan);
                if (cmp != 0) return cmp;
                return b.farMeters.CompareTo(a.farMeters);
            });
            var outList = new List<(string city, double farMeters)>(scored.Count);
            foreach (var s in scored)
                outList.Add((s.city, s.farMeters));
            return outList;
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
            var currentTime = c.EffectiveLatestPickup.Add(
                TimeSpan.FromSeconds(await PickupChainSecondsAsync(c, token).ConfigureAwait(false)));

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
                    var deadline = SupeyTripTimes.TryParseDO(c.Trips[cand]);
                    if (deadline.HasValue && deadline.Value > TimeSpan.Zero && arrive >= deadline.Value)
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
                    currentTime = currentTime.Add(TimeSpan.FromSeconds(hop.Value));
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

        private const double ALegPuLateMaxMinutes = 14.0;
        private const double BcLegPuLateMaxMinutes = 29.0;

        private static double LegPuLateCapMinutes(SupeyTripCluster cluster)
        {
            double cap = BcLegPuLateMaxMinutes;
            if (cluster?.Trips == null) return cap;
            foreach (var t in cluster.Trips)
            {
                string tn = t?.TripNumber ?? "";
                if (tn.Length > 0 && char.ToUpperInvariant(tn[tn.Length - 1]) == 'A')
                {
                    cap = ALegPuLateMaxMinutes;
                    break;
                }
            }
            return cap;
        }

        /// <summary>Simulate OSRM drive along <paramref name="puOrder"/>; each PU must be reachable on time.</summary>
        internal static async Task<bool> PickupOrderMeetsScheduledWindowsAsync(
            SupeyTripCluster c,
            List<int> puOrder,
            CancellationToken token,
            GeoPoint? routeStart)
        {
            if (c == null || puOrder == null || puOrder.Count == 0) return true;
            if (puOrder.Count == 1) return true;
            if (c.PickupPoints == null || c.PickupPoints.Count < c.Trips.Count)
                return false;

            double puCap = LegPuLateCapMinutes(c) + 2.0;
            int firstIdx = puOrder[0];
            var firstScheduled = SupeyDeskScheduleTiming.ScheduledPickupForBuild(c.Trips[firstIdx]);
            if (firstScheduled == TimeSpan.Zero)
                firstScheduled = SupeyTripTimes.TryParsePU(c.Trips[firstIdx]) ?? c.EarliestPickup;

            TimeSpan current;
            GeoPoint pos;
            if (routeStart.HasValue && SupeyOsrmLegs.IsRoutable(routeStart.Value))
            {
                var toFirst = await RoadSecondsAsync(routeStart.Value, c.PickupPoints[firstIdx], token)
                    .ConfigureAwait(false);
                if (!toFirst.HasValue) return false;
                current = firstScheduled.Subtract(TimeSpan.FromSeconds(toFirst.Value));
                if (current < c.EffectiveEarliestPickup)
                    current = c.EffectiveEarliestPickup;
                current = current.Add(TimeSpan.FromSeconds(toFirst.Value));
                pos = c.PickupPoints[firstIdx];
            }
            else
            {
                current = firstScheduled;
                pos = c.PickupPoints[firstIdx];
            }

            if (current > firstScheduled.Add(TimeSpan.FromMinutes(puCap)))
                return false;

            for (int step = 1; step < puOrder.Count; step++)
            {
                int idx = puOrder[step];
                if (idx < 0 || idx >= c.Trips.Count) return false;
                var leg = await RoadSecondsAsync(pos, c.PickupPoints[idx], token).ConfigureAwait(false);
                if (!leg.HasValue) return false;
                current = current.Add(TimeSpan.FromSeconds(leg.Value));
                var scheduled = SupeyDeskScheduleTiming.ScheduledPickupForBuild(c.Trips[idx]);
                if (scheduled == TimeSpan.Zero)
                    scheduled = SupeyTripTimes.TryParsePU(c.Trips[idx]) ?? c.EarliestPickup;
                if (current < scheduled)
                    current = scheduled;
                if (current > scheduled.Add(TimeSpan.FromMinutes(puCap)))
                    return false;
                pos = c.PickupPoints[idx];
            }
            return true;
        }

        private static async Task<bool> DropoffOrderMeetsDeadlinesAsync(
            SupeyTripCluster c, List<int> puOrder, List<int> doOrder, CancellationToken token)
        {
            if (doOrder == null || doOrder.Count == 0)
                return true;

            var start = c.EffectiveLatestPickup;
            int lastPu = puOrder != null && puOrder.Count > 0 ? puOrder[puOrder.Count - 1] : 0;
            double headSec = 0;
            if (puOrder != null && puOrder.Count > 1)
            {
                for (int i = 1; i < puOrder.Count; i++)
                {
                    var leg = await RoadSecondsAsync(
                        c.PickupPoints[puOrder[i - 1]],
                        c.PickupPoints[puOrder[i]], token).ConfigureAwait(false);
                    if (!leg.HasValue) return false;
                    headSec += leg.Value;
                }
            }
            var current = start.Add(TimeSpan.FromSeconds(headSec));
            var puToDo = await RoadSecondsAsync(
                c.PickupPoints[lastPu], c.DropoffPoints[doOrder[0]], token).ConfigureAwait(false);
            if (!puToDo.HasValue) return false;
            current = current.Add(TimeSpan.FromSeconds(puToDo.Value));
            for (int i = 0; i < doOrder.Count; i++)
            {
                int tripIdx = doOrder[i];
                var deadline = SupeyTripTimes.TryParseDO(c.Trips[tripIdx]);
                if (deadline.HasValue && deadline.Value > TimeSpan.Zero && current >= deadline.Value)
                    return false;
                if (i + 1 < doOrder.Count)
                {
                    var hop = await RoadSecondsAsync(
                        c.DropoffPoints[doOrder[i]], c.DropoffPoints[doOrder[i + 1]], token)
                        .ConfigureAwait(false);
                    if (!hop.HasValue) return false;
                    current = current.Add(TimeSpan.FromSeconds(hop.Value));
                }
            }
            return true;
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
