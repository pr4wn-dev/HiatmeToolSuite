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
            var leg = await SupeyOsrmLegs.GetLegAsync(a, b, token).ConfigureAwait(false);
            return leg.Ok ? (double?)leg.Meters : null;
        }

        private static async Task<double?> RoadSecondsAsync(
            GeoPoint a, GeoPoint b, CancellationToken token)
        {
            var leg = await SupeyOsrmLegs.GetLegAsync(a, b, token).ConfigureAwait(false);
            return leg.Ok ? (double?)leg.Seconds : null;
        }

        private static async Task<double?> FullTourMetersAsync(
            SupeyTripCluster c, List<int> puOrder, List<int> doOrder, CancellationToken token)
        {
            var m = await SupeyOsrmLegs.TourMetricsAsync(c, puOrder, doOrder, token).ConfigureAwait(false);
            return m.meters;
        }

        private static async Task<(List<int> pu, List<int> doOrd)> RefineTourCopyAsync(
            SupeyTripCluster c, List<int> pu, List<int> doOrder, CancellationToken token)
        {
            ApplyOrders(c, new List<int>(pu), new List<int>(doOrder));
            await RefinePickupOrder2OptAsync(c, token).ConfigureAwait(false);
            await RefineDropoffOrderByDistanceAsync(c, token).ConfigureAwait(false);
            return (new List<int>(c.PickupOrder), new List<int>(c.DropoffOrder));
        }

        private static async Task BuildGeographicTourAsync(
            SupeyTripCluster c, List<int> puOut, List<int> doOut, CancellationToken token)
        {
            puOut.Clear();
            doOut.Clear();
            int n = c.Trips.Count;

            const double hubRadiusMeters = 6500.0;
            var dropHub = Centroid(c.DropoffPoints);
            var puHub = Centroid(c.PickupPoints);
            bool sameDropHub = await AllPointsNearOsrmAsync(dropHub, c.DropoffPoints, hubRadiusMeters, token)
                .ConfigureAwait(false);
            bool samePuHub = await AllPointsNearOsrmAsync(puHub, c.PickupPoints, hubRadiusMeters, token)
                .ConfigureAwait(false);

            var scratch = new SupeyTripCluster();
            CopyTourContext(c, scratch);

            if (c.IsAllALeg && sameDropHub)
            {
                int startPu = await IndexFarthestFromOsrmAsync(c.PickupPoints, dropHub, token)
                    .ConfigureAwait(false);
                await BuildPickupOrderTowardHubAsync(scratch, startPu, dropHub, token).ConfigureAwait(false);
                for (int i = 0; i < n; i++) scratch.DropoffOrder.Add(i);
                scratch.DropoffOrder.Sort(CompareDropoffDeadlineIndex(scratch));
            }
            else if (samePuHub)
            {
                for (int i = 0; i < n; i++) scratch.PickupOrder.Add(i);
                await BuildDropoffOrderFromHubAsync(scratch, puHub, token).ConfigureAwait(false);
            }
            else
            {
                int start = await IndexFarthestFromOsrmAsync(c.PickupPoints, dropHub, token)
                    .ConfigureAwait(false);
                await BuildPickupOrderTowardHubAsync(scratch, start, dropHub, token).ConfigureAwait(false);
                await BuildDropoffOrderGreedyAsync(scratch, token).ConfigureAwait(false);
            }

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

        private static async Task BuildPickupOrderTowardHubAsync(
            SupeyTripCluster c, int startIdx, GeoPoint hub, CancellationToken token)
        {
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
            int n = c.Trips.Count;
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

        private static async Task RefinePickupOrder2OptAsync(SupeyTripCluster c, CancellationToken token)
        {
            int n = c.PickupOrder.Count;
            if (n <= 2) return;

            bool improved = true;
            int safety = n * n;
            while (improved && safety-- > 0)
            {
                improved = false;
                for (int i = 0; i < n - 1; i++)
                {
                    for (int j = i + 1; j < n; j++)
                    {
                        var trialPu = new List<int>(c.PickupOrder);
                        int tmp = trialPu[i];
                        trialPu[i] = trialPu[j];
                        trialPu[j] = tmp;
                        if (!await DropoffOrderMeetsDeadlinesAsync(c, trialPu, c.DropoffOrder, token)
                            .ConfigureAwait(false))
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

        private static async Task RefineDropoffOrderByDistanceAsync(SupeyTripCluster c, CancellationToken token)
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
                        if (!await DropoffOrderMeetsDeadlinesAsync(c, c.PickupOrder, trial, token)
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
