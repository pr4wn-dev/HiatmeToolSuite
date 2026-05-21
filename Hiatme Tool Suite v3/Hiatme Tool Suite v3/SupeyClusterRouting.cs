using System;
using System.Collections.Generic;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Facility keys, pickup/dropoff tour ordering, and cluster split helpers for Supey scheduling.
    /// </summary>
    internal static class SupeyClusterRouting
    {
        private const double FacilityStreetNormalizeMeters = 200.0;
        private const double TourSplitMileageRatio = 1.35;

        public static string FacilityKey(string street, string city)
        {
            var s = (street ?? "").Trim().ToUpperInvariant();
            var c = (city ?? "").Trim().ToUpperInvariant();
            if (s.Length == 0 && c.Length == 0) return "|";
            // Collapse minor street variants — use first 30 chars + city.
            if (s.Length > 30) s = s.Substring(0, 30);
            return s + "|" + c;
        }

        public static string MergeKeyForTrip(MCDownloadedTrip t)
        {
            char leg = SupeyScheduleAlgorithm.DetectLegPublic(t.TripNumber);
            if (leg == 'A')
                return CanonicalMorningDropHubKey("A|" + FacilityKey(t.DOStreet, t.DOCITY));
            return "BC|" + FacilityKey(t.PUStreet, t.PUCity);
        }

        /// <summary>
        /// Collapses clinic variants (589 vs 1512 Minot, etc.) so morning merges and hub waves
        /// treat one dialysis stop as one hub.
        /// </summary>
        public static string CanonicalMorningDropHubKey(string facilityMergeKey)
        {
            if (string.IsNullOrWhiteSpace(facilityMergeKey)) return facilityMergeKey ?? "";
            string u = facilityMergeKey.ToUpperInvariant();
            if (u.IndexOf("MINOT", StringComparison.Ordinal) >= 0)
                return "A|MINOT CLINIC|AUBURN";
            if (u.IndexOf("23 CROSS", StringComparison.Ordinal) >= 0
                || (u.IndexOf("CROSS ST", StringComparison.Ordinal) >= 0
                    && u.IndexOf("AUBURN", StringComparison.Ordinal) >= 0))
                return "A|23 CROSS ST|AUBURN";
            if (u.IndexOf("646 MAIN", StringComparison.Ordinal) >= 0)
                return "A|646 MAIN|LEWISTON";
            if (u.IndexOf("618 MAIN", StringComparison.Ordinal) >= 0)
                return "A|618 MAIN|LEWISTON";
            if (u.IndexOf("FALCON", StringComparison.Ordinal) >= 0)
                return "A|FALCON|LEWISTON";
            if (u.IndexOf("MANLEY", StringComparison.Ordinal) >= 0)
                return "A|MANLEY|AUBURN";
            if (u.IndexOf("63 BROAD", StringComparison.Ordinal) >= 0)
                return "A|63 BROAD|AUBURN";
            return facilityMergeKey;
        }

        /// <summary>Normalized PU street+city — riders at the same home share this key.</summary>
        public static string PickupAddressKey(MCDownloadedTrip t) =>
            FacilityKey(t.PUStreet, t.PUCity);

        /// <summary>
        /// True when every rider in the cluster picks up at the same address (household / cohabitants).
        /// </summary>
        public static bool ClusterSharesSinglePickupAddress(SupeyTripCluster c)
        {
            if (c == null || c.Trips.Count <= 1) return true;
            string key = PickupAddressKey(c.Trips[0]);
            for (int i = 1; i < c.Trips.Count; i++)
            {
                if (!string.Equals(key, PickupAddressKey(c.Trips[i]), StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Merges clusters that share a pickup address and clinic hub so housemates stay in one group.
        /// </summary>
        public static List<SupeyTripCluster> MergeHouseholdClusters(
            List<SupeyTripCluster> clusters,
            int maxRidersPerCluster)
        {
            if (clusters == null || clusters.Count <= 1) return clusters;

            bool merged;
            do
            {
                merged = false;
                for (int i = 0; i < clusters.Count && !merged; i++)
                {
                    for (int j = i + 1; j < clusters.Count; j++)
                    {
                        if (!CanMergeHouseholdClusters(clusters[i], clusters[j], maxRidersPerCluster))
                            continue;
                        MergeClusterInto(clusters[i], clusters[j]);
                        clusters.RemoveAt(j);
                        merged = true;
                        break;
                    }
                }
            }
            while (merged);

            return clusters;
        }

        private static bool CanMergeHouseholdClusters(
            SupeyTripCluster a,
            SupeyTripCluster b,
            int maxRiders)
        {
            if (a.Trips.Count == 0 || b.Trips.Count == 0) return false;
            if (a.RiderCount + b.RiderCount > maxRiders) return false;

            char legA = SupeyScheduleAlgorithm.DetectLegPublic(a.Trips[0].TripNumber);
            char legB = SupeyScheduleAlgorithm.DetectLegPublic(b.Trips[0].TripNumber);
            if (legA != legB) return false;

            if (!string.Equals(a.FacilityMergeKey ?? "", b.FacilityMergeKey ?? "", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.Equals(PickupAddressKey(a.Trips[0]), PickupAddressKey(b.Trips[0]), StringComparison.OrdinalIgnoreCase))
                return false;

            if (!ClusterSharesSinglePickupAddress(a) || !ClusterSharesSinglePickupAddress(b))
                return false;

            TimeSpan spanStart = a.EarliestPickup < b.EarliestPickup ? a.EarliestPickup : b.EarliestPickup;
            TimeSpan spanEnd = a.LatestPickup > b.LatestPickup ? a.LatestPickup : b.LatestPickup;
            if (Math.Abs((spanEnd - spanStart).TotalMinutes) > SupeyScheduleAlgorithm.ClusterTimeWindowMinutesPublic)
                return false;

            return true;
        }

        private static void MergeClusterInto(SupeyTripCluster target, SupeyTripCluster source)
        {
            for (int i = 0; i < source.Trips.Count; i++)
                AppendTrip(target, source.Trips[i], source.PickupPoints[i], source.DropoffPoints[i]);
            if (source.HardestDropoff < target.HardestDropoff)
                target.HardestDropoff = source.HardestDropoff;
        }

        private static readonly TimeSpan MorningManleyMergeStart = new TimeSpan(6, 30, 0);
        private static readonly TimeSpan MorningManleyMergeEnd = new TimeSpan(9, 30, 0);

        /// <summary>
        /// Combines small morning A-leg clusters that share one clinic drop hub (Falcon, Manley, …)
        /// so one van can carry 4–6 riders instead of many solo groups.
        /// </summary>
        public static List<SupeyTripCluster> MergeMorningHubClusters(
            List<SupeyTripCluster> clusters,
            int maxRidersPerCluster,
            string hubToken)
        {
            if (clusters == null || clusters.Count <= 1 || string.IsNullOrWhiteSpace(hubToken))
                return clusters;

            var indices = new List<int>();
            for (int i = 0; i < clusters.Count; i++)
            {
                var c = clusters[i];
                if (c.Trips.Count == 0) continue;
                if (c.EarliestPickup < MorningManleyMergeStart || c.EarliestPickup >= MorningManleyMergeEnd)
                    continue;
                if (SupeyScheduleAlgorithm.DetectLegPublic(c.Trips[0].TripNumber) != 'A') continue;
                string hub = c.FacilityMergeKey ?? "";
                if (hub.IndexOf(hubToken, StringComparison.OrdinalIgnoreCase) < 0) continue;
                indices.Add(i);
            }
            if (indices.Count <= 1) return clusters;

            indices.Sort((ia, ib) => clusters[ia].EarliestPickup.CompareTo(clusters[ib].EarliestPickup));

            var remove = new HashSet<int>();
            for (int mi = 0; mi < indices.Count; mi++)
            {
                int ti = indices[mi];
                if (remove.Contains(ti)) continue;
                var target = clusters[ti];
                TimeSpan windowEnd = target.LatestPickup.Add(
                    TimeSpan.FromMinutes(SupeyScheduleAlgorithm.ClusterTimeWindowMinutesPublic));

                for (int mj = mi + 1; mj < indices.Count; mj++)
                {
                    int si = indices[mj];
                    if (remove.Contains(si)) continue;
                    var source = clusters[si];
                    if (!string.Equals(
                            CanonicalMorningDropHubKey(target.FacilityMergeKey ?? ""),
                            CanonicalMorningDropHubKey(source.FacilityMergeKey ?? ""),
                            StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (source.EarliestPickup > windowEnd) break;
                    if (target.RiderCount + source.RiderCount > maxRidersPerCluster) continue;

                    MergeClusterInto(target, source);
                    if (source.LatestPickup > target.LatestPickup) target.LatestPickup = source.LatestPickup;
                    remove.Add(si);
                    windowEnd = target.LatestPickup.Add(
                        TimeSpan.FromMinutes(SupeyScheduleAlgorithm.ClusterTimeWindowMinutesPublic));
                }
            }

            if (remove.Count == 0) return clusters;
            var result = new List<SupeyTripCluster>(clusters.Count - remove.Count);
            for (int i = 0; i < clusters.Count; i++)
                if (!remove.Contains(i)) result.Add(clusters[i]);
            return result;
        }

        /// <summary>
        /// Pickup/drop order inside one van load — geographic sweep, not zigzag by spreadsheet row index.
        /// A-leg → clinic: PUs from outside-in toward the clinic; B-leg → home: clinic PU then
        /// nearest-neighbor drops outward; mixed loads use deadline-feasible greedy drops.
        /// </summary>
        public static void OptimizeClusterTour(SupeyTripCluster c)
        {
            int n = c.Trips.Count;
            c.PickupOrder.Clear();
            c.DropoffOrder.Clear();
            if (n == 0) return;
            if (c.PickupPoints == null || c.PickupPoints.Count < n
                || c.DropoffPoints == null || c.DropoffPoints.Count < n)
            {
                for (int i = 0; i < n; i++)
                {
                    c.PickupOrder.Add(i);
                    c.DropoffOrder.Add(i);
                }
                return;
            }
            if (n == 1)
            {
                c.PickupOrder.Add(0);
                c.DropoffOrder.Add(0);
                return;
            }

            const double hubRadiusMeters = 6500.0;
            var dropHub = Centroid(c.DropoffPoints);
            var puHub = Centroid(c.PickupPoints);
            bool sameDropHub = AllPointsNear(dropHub, c.DropoffPoints, hubRadiusMeters);
            bool samePuHub = AllPointsNear(puHub, c.PickupPoints, hubRadiusMeters);

            if (c.IsAllALeg && sameDropHub)
            {
                // Morning dialysis: sweep pickups toward the clinic, then appt order at the door.
                int startPu = IndexFarthestFrom(c.PickupPoints, dropHub);
                BuildPickupOrderTowardHub(c, startPu, dropHub);
                for (int i = 0; i < n; i++) c.DropoffOrder.Add(i);
                c.DropoffOrder.Sort(CompareDropoffDeadlineIndex(c));
                return;
            }

            if (samePuHub)
            {
                // Afternoon clinic release: one PU, chain DOs by road miles from the clinic.
                for (int i = 0; i < n; i++) c.PickupOrder.Add(i);
                BuildDropoffOrderFromHub(c, puHub);
                return;
            }

            int start = IndexFarthestFrom(c.PickupPoints, dropHub);
            BuildPickupOrderTowardHub(c, start, dropHub);
            BuildDropoffOrderGreedy(c);
            RefineDropoffOrderByDistance(c);
        }

        private static void BuildPickupOrderTowardHub(SupeyTripCluster c, int startIdx, GeoPoint hub)
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
                    double leg = HaversineMeters(c.PickupPoints[current], c.PickupPoints[cand]);
                    double toward = HaversineMeters(c.PickupPoints[cand], hub);
                    double score = leg + toward * 0.35;
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

        /// <summary>B-leg from clinic: nearest-neighbor drops; timed appts before open-ended returns.</summary>
        private static void BuildDropoffOrderFromHub(SupeyTripCluster c, GeoPoint hub)
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
                double bestDist = HaversineMeters(currentPt, c.DropoffPoints[best]);
                for (int i = 1; i < pool.Count; i++)
                {
                    int cand = pool[i];
                    double d = HaversineMeters(currentPt, c.DropoffPoints[cand]);
                    if (d < bestDist) { bestDist = d; best = cand; }
                }
                c.DropoffOrder.Add(best);
                pool.Remove(best);
                currentPt = c.DropoffPoints[best];
            }
        }

        /// <summary>Mixed load: each next drop is closest feasible stop by road (desk mid-tour drops).</summary>
        private static void BuildDropoffOrderGreedy(SupeyTripCluster c)
        {
            int n = c.Trips.Count;
            var remaining = new List<int>(n);
            for (int i = 0; i < n; i++) remaining.Add(i);

            if (c.PickupOrder.Count == 0)
                for (int i = 0; i < n; i++) c.PickupOrder.Add(i);
            int lastPuIdx = c.PickupOrder[c.PickupOrder.Count - 1];
            if (lastPuIdx < 0 || lastPuIdx >= c.PickupPoints.Count) lastPuIdx = 0;
            var currentPt = c.PickupPoints[lastPuIdx];
            var currentTime = c.EffectiveLatestPickup.Add(TimeSpan.FromSeconds(PickupChainSeconds(c)));

            while (remaining.Count > 0)
            {
                int best = -1;
                double bestDist = double.MaxValue;
                foreach (int cand in remaining)
                {
                    double legSec = HaversineMeters(currentPt, c.DropoffPoints[cand]) / 13.4;
                    var arrive = currentTime.Add(TimeSpan.FromSeconds(legSec));
                    var deadline = SupeyTripTimes.TryParseDO(c.Trips[cand]);
                    if (deadline.HasValue && deadline.Value > TimeSpan.Zero && arrive >= deadline.Value)
                        continue;
                    double d = HaversineMeters(currentPt, c.DropoffPoints[cand]);
                    if (d < bestDist) { bestDist = d; best = cand; }
                }
                if (best < 0)
                {
                    best = remaining[0];
                    for (int i = 1; i < remaining.Count; i++)
                    {
                        int cand = remaining[i];
                        if (HaversineMeters(currentPt, c.DropoffPoints[cand])
                            < HaversineMeters(currentPt, c.DropoffPoints[best]))
                            best = cand;
                    }
                }
                double hopSec = HaversineMeters(currentPt, c.DropoffPoints[best]) / 13.4;
                currentTime = currentTime.Add(TimeSpan.FromSeconds(hopSec));
                currentPt = c.DropoffPoints[best];
                c.DropoffOrder.Add(best);
                remaining.Remove(best);
            }
        }

        private static double PickupChainSeconds(SupeyTripCluster c)
        {
            if (c.PickupOrder.Count <= 1) return 0;
            double sec = 0;
            for (int i = 1; i < c.PickupOrder.Count; i++)
                sec += HaversineMeters(
                    c.PickupPoints[c.PickupOrder[i - 1]],
                    c.PickupPoints[c.PickupOrder[i]]) / 13.4;
            return sec;
        }

        private static Comparison<int> CompareDropoffDeadlineIndex(SupeyTripCluster c) =>
            (a, b) =>
            {
                var ta = c == null ? TimeSpan.MaxValue : SupeyTripTimes.TryParseDO(c.Trips[a]) ?? TimeSpan.MaxValue;
                var tb = c == null ? TimeSpan.MaxValue : SupeyTripTimes.TryParseDO(c.Trips[b]) ?? TimeSpan.MaxValue;
                int cmp = ta.CompareTo(tb);
                return cmp != 0 ? cmp : a.CompareTo(b);
            };

        private static bool AllPointsNear(GeoPoint hub, List<GeoPoint> pts, double radiusMeters)
        {
            if (pts == null || pts.Count == 0) return false;
            foreach (var p in pts)
                if (HaversineMeters(hub, p) > radiusMeters) return false;
            return true;
        }

        private static GeoPoint Centroid(List<GeoPoint> pts)
        {
            if (pts == null || pts.Count == 0) return new GeoPoint(0, 0);
            double lat = 0, lng = 0;
            foreach (var p in pts) { lat += p.Lat; lng += p.Lng; }
            return new GeoPoint(lat / pts.Count, lng / pts.Count);
        }

        private static int IndexFarthestFrom(List<GeoPoint> pts, GeoPoint hub)
        {
            int best = 0;
            double bestD = -1;
            for (int i = 0; i < pts.Count; i++)
            {
                double d = HaversineMeters(pts[i], hub);
                if (d > bestD) { bestD = d; best = i; }
            }
            return best;
        }

        private static void RefineDropoffOrderByDistance(SupeyTripCluster c)
        {
            int n = c.DropoffOrder.Count;
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
                        var trial = new List<int>(c.DropoffOrder);
                        int tmp = trial[i];
                        trial[i] = trial[j];
                        trial[j] = tmp;
                        if (!DropoffOrderMeetsDeadlines(c, trial)) continue;
                        double before = DropoffPathMeters(c, c.DropoffOrder);
                        double after = DropoffPathMeters(c, trial);
                        if (after + 50 < before)
                        {
                            c.DropoffOrder.Clear();
                            c.DropoffOrder.AddRange(trial);
                            improved = true;
                        }
                    }
                }
            }
        }

        private static double DropoffPathMeters(SupeyTripCluster c, List<int> order)
        {
            if (order.Count == 0) return 0;
            int lastPu = c.PickupOrder.Count > 0 ? c.PickupOrder[c.PickupOrder.Count - 1] : c.Trips.Count - 1;
            double total = HaversineMeters(c.PickupPoints[lastPu], c.DropoffPoints[order[0]]);
            for (int i = 1; i < order.Count; i++)
                total += HaversineMeters(c.DropoffPoints[order[i - 1]], c.DropoffPoints[order[i]]);
            return total;
        }

        private static bool DropoffOrderMeetsDeadlines(SupeyTripCluster c, List<int> order)
        {
            var start = c.EffectiveLatestPickup;
            int lastPu = c.PickupOrder.Count > 0 ? c.PickupOrder[c.PickupOrder.Count - 1] : 0;
            double headSec = 0;
            if (c.PickupOrder.Count > 1)
            {
                for (int i = 1; i < c.PickupOrder.Count; i++)
                    headSec += HaversineMeters(c.PickupPoints[c.PickupOrder[i - 1]], c.PickupPoints[c.PickupOrder[i]]) / 13.4;
            }
            var current = start.Add(TimeSpan.FromSeconds(headSec));
            current = current.Add(TimeSpan.FromSeconds(
                HaversineMeters(c.PickupPoints[lastPu], c.DropoffPoints[order[0]]) / 13.4));
            for (int i = 0; i < order.Count; i++)
            {
                int tripIdx = order[i];
                var deadline = SupeyTripTimes.TryParseDO(c.Trips[tripIdx]);
                if (deadline.HasValue && current >= deadline.Value) return false;
                if (i + 1 < order.Count)
                {
                    current = current.Add(TimeSpan.FromSeconds(
                        HaversineMeters(c.DropoffPoints[order[i]], c.DropoffPoints[order[i + 1]]) / 13.4));
                }
            }
            return true;
        }

        /// <summary>
        /// Splits an oversized group for reassignment after all drivers rejected it.
        /// </summary>
        public static List<SupeyTripCluster> SplitClusterForAssignment(SupeyTripCluster c)
        {
            if (c == null || c.RiderCount <= 1)
                return new List<SupeyTripCluster> { c };

            if (ClusterSharesSinglePickupAddress(c))
                return new List<SupeyTripCluster> { c };

            var byFacility = BuildSubClusters(c);
            if (byFacility.Count > 1)
                return byFacility;

            if (c.RiderCount >= 3)
            {
                var byDeadline = SplitByDoDeadlineHalves(c);
                if (byDeadline.Count > 1)
                    return byDeadline;
            }

            return new List<SupeyTripCluster> { c };
        }

        private static List<SupeyTripCluster> BuildSubClusters(SupeyTripCluster c)
        {
            var result = new List<SupeyTripCluster>();
            var buckets = new Dictionary<string, SupeyTripCluster>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < c.Trips.Count; i++)
            {
                var t = c.Trips[i];
                string subKey = MergeKeyForTrip(t);
                if (!buckets.TryGetValue(subKey, out var sub))
                {
                    sub = NewSubCluster(c, t, subKey, c.PickupPoints[i], c.DropoffPoints[i]);
                    buckets[subKey] = sub;
                }
                else
                {
                    AppendTrip(sub, t, c.PickupPoints[i], c.DropoffPoints[i]);
                }
            }
            foreach (var sub in buckets.Values)
                result.Add(sub);
            return result;
        }

        private static List<SupeyTripCluster> SplitByDoDeadlineHalves(SupeyTripCluster c)
        {
            var indexed = new List<int>(c.Trips.Count);
            for (int i = 0; i < c.Trips.Count; i++) indexed.Add(i);
            indexed.Sort((a, b) =>
            {
                var da = SupeyTripTimes.TryParseDO(c.Trips[a]) ?? TimeSpan.MaxValue;
                var db = SupeyTripTimes.TryParseDO(c.Trips[b]) ?? TimeSpan.MaxValue;
                return da.CompareTo(db);
            });

            int mid = indexed.Count / 2;
            var early = new SupeyTripCluster { FacilityMergeKey = c.FacilityMergeKey, GroupNumber = c.GroupNumber, GroupColor = c.GroupColor };
            var late = new SupeyTripCluster { FacilityMergeKey = c.FacilityMergeKey, GroupNumber = c.GroupNumber, GroupColor = c.GroupColor };
            for (int i = 0; i < indexed.Count; i++)
            {
                int idx = indexed[i];
                var target = i < mid ? early : late;
                AppendTrip(target, c.Trips[idx], c.PickupPoints[idx], c.DropoffPoints[idx]);
            }
            var result = new List<SupeyTripCluster>();
            if (early.RiderCount > 0) result.Add(early);
            if (late.RiderCount > 0) result.Add(late);
            return result;
        }

        private static SupeyTripCluster NewSubCluster(SupeyTripCluster parent, MCDownloadedTrip t, string subKey, GeoPoint pu, GeoPoint dro)
        {
            var sub = new SupeyTripCluster
            {
                GroupNumber = parent.GroupNumber,
                GroupColor = parent.GroupColor,
                EarliestPickup = SupeyTripTimes.TryParsePU(t) ?? parent.EarliestPickup,
                LatestPickup = SupeyTripTimes.TryParsePU(t) ?? parent.LatestPickup,
                HardestDropoff = SupeyTripTimes.TryParseDO(t) ?? parent.HardestDropoff,
                FacilityMergeKey = subKey,
                PickupCentroid = pu,
                DropoffCentroid = dro,
            };
            sub.Trips.Add(t);
            sub.PickupPoints.Add(pu);
            sub.DropoffPoints.Add(dro);
            return sub;
        }

        private static void AppendTrip(SupeyTripCluster sub, MCDownloadedTrip t, GeoPoint pu, GeoPoint dro)
        {
            sub.Trips.Add(t);
            sub.PickupPoints.Add(pu);
            sub.DropoffPoints.Add(dro);
            var puTime = SupeyTripTimes.TryParsePU(t);
            if (puTime.HasValue)
            {
                if (puTime.Value < sub.EarliestPickup) sub.EarliestPickup = puTime.Value;
                if (puTime.Value > sub.LatestPickup) sub.LatestPickup = puTime.Value;
            }
            var doTime = SupeyTripTimes.TryParseDO(t);
            if (doTime.HasValue && doTime.Value < sub.HardestDropoff) sub.HardestDropoff = doTime.Value;
            sub.PickupCentroid = Centroid(sub.PickupPoints);
            sub.DropoffCentroid = Centroid(sub.DropoffPoints);
        }

        /// <summary>
        /// Breaks clusters larger than <paramref name="maxRiders"/> for reserve retry (avoids
        /// re-clustering the entire reserve pool into one oversized clinic load).
        /// </summary>
        public static List<SupeyTripCluster> SplitClustersExceedingRiders(
            List<SupeyTripCluster> clusters,
            int maxRiders)
        {
            if (clusters == null || maxRiders < 1) return clusters;
            var result = new List<SupeyTripCluster>();
            foreach (var c in clusters)
            {
                if (c == null) continue;
                if (c.RiderCount <= maxRiders)
                {
                    result.Add(c);
                    continue;
                }
                var parts = SplitClusterForAssignment(c);
                if (parts.Count <= 1 && c.RiderCount > maxRiders)
                {
                    for (int i = 0; i < c.Trips.Count; i++)
                    {
                        var t = c.Trips[i];
                        result.Add(NewSubCluster(c, t, MergeKeyForTrip(t), c.PickupPoints[i], c.DropoffPoints[i]));
                    }
                    continue;
                }
                foreach (var part in parts)
                {
                    if (part.RiderCount <= maxRiders)
                        result.Add(part);
                    else
                        result.AddRange(SplitClustersExceedingRiders(new List<SupeyTripCluster> { part }, maxRiders));
                }
            }
            return result;
        }

        /// <summary>
        /// Splits clusters whose tour mileage exceeds solo-trip sum * ratio (gas-waste guard).
        /// </summary>
        public static List<SupeyTripCluster> SplitInefficientClusters(List<SupeyTripCluster> clusters)
        {
            var result = new List<SupeyTripCluster>();
            foreach (var c in clusters)
            {
                if (c.RiderCount <= 1)
                {
                    result.Add(c);
                    continue;
                }
                double solo = 0;
                for (int i = 0; i < c.Trips.Count; i++)
                    solo += HaversineMeters(c.PickupPoints[i], c.DropoffPoints[i]);
                if (c.IntraClusterMeters <= 0 || c.IntraClusterMeters <= solo * TourSplitMileageRatio)
                {
                    result.Add(c);
                    continue;
                }

                foreach (var sub in BuildSubClusters(c))
                    result.Add(sub);
            }
            return result;
        }

        private static double HaversineMeters(GeoPoint a, GeoPoint b)
        {
            const double R = 6371000.0;
            double lat1 = a.Lat * Math.PI / 180.0;
            double lat2 = b.Lat * Math.PI / 180.0;
            double dLat = (b.Lat - a.Lat) * Math.PI / 180.0;
            double dLng = (b.Lng - a.Lng) * Math.PI / 180.0;
            double h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                     + Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
            double c = 2 * Math.Atan2(Math.Sqrt(h), Math.Sqrt(1 - h));
            return R * c;
        }
    }
}
