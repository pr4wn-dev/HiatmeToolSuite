using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Facility keys, pickup/dropoff tour ordering, and cluster split helpers for Supey scheduling.
    /// </summary>
    internal static partial class SupeyClusterRouting
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
        /// Pick the best in-group PU/DO order: schedule row order, PU-time order, geographic sweep,
        /// each refined with 2-opt. Keeps <see cref="SupeyTripCluster.Trips"/> list order unchanged.
        /// </summary>
        /// <summary>
        /// After manual drag in the preview list: PU order = trip row order; DO order = deadline order.
        /// </summary>
        public static void ApplyManualEditTour(SupeyTripCluster c)
        {
            if (c == null) return;
            int n = c.Trips.Count;
            if (n == 0)
            {
                c.PickupOrder.Clear();
                c.DropoffOrder.Clear();
                return;
            }
            if (n == 1)
            {
                ApplyOrders(c, IdentityOrder(1), IdentityOrder(1));
                return;
            }
            ApplyOrders(c, IdentityOrder(n), BuildDeadlineDropoffOrder(c));
        }

        /// <summary>Desk tour without OSRM: PU by scheduled time, DO by appointment deadline.</summary>
        public static void ApplyOrdersForSingleTrip(SupeyTripCluster c)
        {
            if (c == null || c.Trips.Count != 1) return;
            ApplyOrders(c, IdentityOrder(1), IdentityOrder(1));
        }

        public static void ApplyTemplateDeskTour(SupeyTripCluster c)
        {
            if (c == null) return;
            int n = c.Trips.Count;
            if (n == 0)
            {
                c.PickupOrder.Clear();
                c.DropoffOrder.Clear();
                return;
            }
            if (n == 1)
            {
                ApplyOrders(c, IdentityOrder(1), IdentityOrder(1));
                return;
            }
            ApplyOrders(c, IdentityOrder(n), BuildDeadlineDropoffOrder(c));
        }

        /// <summary>Post-build: same best-feasible OSRM tour as BUILD (table-backed when bound).</summary>
        public static Task OptimizeClusterTourPostBuildAsync(
            SupeyTripCluster c, CancellationToken token, GeoPoint? routeStart = null) =>
            OptimizeClusterTourBestAsync(c, token, routeStart);

        /// <summary>City-block pickups (all PUs per town, then next town) + OSRM drop chain.</summary>
        internal static async Task OptimizeClusterTourBestAsync(
            SupeyTripCluster c, CancellationToken token, GeoPoint? routeStart)
        {
            if (c == null) return;
            int n = c.Trips.Count;
            if (n == 0)
            {
                c.PickupOrder.Clear();
                c.DropoffOrder.Clear();
                c.RoadTourOptimized = false;
                return;
            }
            if (n == 1
                || c.PickupPoints == null || c.PickupPoints.Count < n
                || c.DropoffPoints == null || c.DropoffPoints.Count < n)
            {
                ApplyOrdersForSingleTrip(c);
                c.RoadTourOptimized = n == 1;
                return;
            }

            token.ThrowIfCancellationRequested();
            var puCity = await BuildPickupOrderByCityBlocksAsync(c, routeStart, token).ConfigureAwait(false);
            await ApplyGroupTourFromPickupOrderAsync(c, puCity, routeStart, token, requireFeasible: false)
                .ConfigureAwait(false);
        }

        /// <summary>Post-build uses the same city-block group tour as BUILD.</summary>
        public static Task OptimizeClusterTourForPostBuildAsync(
            SupeyTripCluster c, CancellationToken token, GeoPoint? routeStart = null) =>
            OptimizeClusterTourBestAsync(c, token, routeStart);

        /// <summary>Pick PU/DO visit order by OSRM tour length; refine and deadlines use OSRM legs.</summary>
        public static Task OptimizeClusterTourAsync(SupeyTripCluster c, CancellationToken token) =>
            OptimizeClusterTourAsync(c, token, null);

        /// <param name="routeStart">Van position before this group (home or last DO); improves first PU choice.</param>
        public static Task OptimizeClusterTourAsync(
            SupeyTripCluster c, CancellationToken token, GeoPoint? routeStart) =>
            OptimizeClusterTourBestAsync(c, token, routeStart);

        private static List<int> IdentityOrder(int n)
        {
            var list = new List<int>(n);
            for (int i = 0; i < n; i++) list.Add(i);
            return list;
        }

        private static List<int> BuildDeadlineDropoffOrder(SupeyTripCluster c)
        {
            var order = IdentityOrder(c.Trips.Count);
            order.Sort(CompareDropoffDeadlineIndex(c));
            return order;
        }

        private static List<int> BuildPickupOrderByPuTime(SupeyTripCluster c)
        {
            var order = IdentityOrder(c.Trips.Count);
            order.Sort((a, b) =>
            {
                var ta = SupeyTripTimes.TryParsePU(c.Trips[a]) ?? TimeSpan.MaxValue;
                var tb = SupeyTripTimes.TryParsePU(c.Trips[b]) ?? TimeSpan.MaxValue;
                int cmp = ta.CompareTo(tb);
                return cmp != 0 ? cmp : a.CompareTo(b);
            });
            return order;
        }

        internal static List<int> BuildPickupOrderByPuTimePublic(SupeyTripCluster c) =>
            BuildPickupOrderByPuTime(c);

        internal static List<int> BuildDeadlineDropoffOrderPublic(SupeyTripCluster c) =>
            BuildDeadlineDropoffOrder(c);

        internal static void ApplyOrdersPublic(SupeyTripCluster c, List<int> puOrder, List<int> doOrder) =>
            ApplyOrders(c, puOrder, doOrder);

        private static void ApplyOrders(SupeyTripCluster c, List<int> puOrder, List<int> doOrder)
        {
            c.PickupOrder.Clear();
            c.PickupOrder.AddRange(puOrder);
            c.DropoffOrder.Clear();
            c.DropoffOrder.AddRange(doOrder);
            NormalizeVisitOrders(c);
        }

        /// <summary>DropoffOrder/PickupOrder must be one permutation — no doubled entries from tour rebuild.</summary>
        internal static void NormalizeVisitOrders(SupeyTripCluster c)
        {
            if (c == null) return;
            int n = c.Trips.Count;
            if (n == 0)
            {
                c.PickupOrder.Clear();
                c.DropoffOrder.Clear();
                return;
            }
            var oldPu = new List<int>(c.PickupOrder);
            var oldDo = new List<int>(c.DropoffOrder);
            c.PickupOrder.Clear();
            c.PickupOrder.AddRange(CompletePermutation(oldPu, n));
            c.DropoffOrder.Clear();
            c.DropoffOrder.AddRange(CompletePermutation(oldDo, n, BuildDeadlineDropoffOrder(c)));
        }

        internal static bool IsValidVisitOrder(IList<int> order, int tripCount)
        {
            if (order == null || order.Count != tripCount || tripCount <= 0) return false;
            var seen = new bool[tripCount];
            foreach (int idx in order)
            {
                if (idx < 0 || idx >= tripCount || seen[idx]) return false;
                seen[idx] = true;
            }
            return true;
        }

        /// <summary>Keep visit order; append missing indices in row order — never substitute clock PU order.</summary>
        private static List<int> CompletePermutation(List<int> order, int n, List<int> fillMissingFrom = null)
        {
            var seen = new bool[n];
            var result = new List<int>(n);
            if (order != null)
            {
                foreach (int idx in order)
                {
                    if (idx < 0 || idx >= n || seen[idx]) continue;
                    seen[idx] = true;
                    result.Add(idx);
                }
            }
            if (fillMissingFrom != null)
            {
                foreach (int idx in fillMissingFrom)
                {
                    if (idx < 0 || idx >= n || seen[idx]) continue;
                    seen[idx] = true;
                    result.Add(idx);
                }
            }
            for (int i = 0; i < n; i++)
            {
                if (!seen[i])
                    result.Add(i);
            }
            return result;
        }

        private static void CopyTourContext(SupeyTripCluster from, SupeyTripCluster to)
        {
            to.Trips.Clear();
            to.Trips.AddRange(from.Trips);
            to.PickupPoints.Clear();
            to.PickupPoints.AddRange(from.PickupPoints);
            to.DropoffPoints.Clear();
            to.DropoffPoints.AddRange(from.DropoffPoints);
            to.PickupOrder.Clear();
            to.DropoffOrder.Clear();
            to.EarliestPickup = from.EarliestPickup;
            to.LatestPickup = from.LatestPickup;
            to.IsAllALeg = from.IsAllALeg;
        }

        private static Comparison<int> CompareDropoffDeadlineIndex(SupeyTripCluster c) =>
            (a, b) =>
            {
                var ta = c == null ? TimeSpan.MaxValue : SupeyTripTimes.TryParseDO(c.Trips[a]) ?? TimeSpan.MaxValue;
                var tb = c == null ? TimeSpan.MaxValue : SupeyTripTimes.TryParseDO(c.Trips[b]) ?? TimeSpan.MaxValue;
                int cmp = ta.CompareTo(tb);
                return cmp != 0 ? cmp : a.CompareTo(b);
            };

        private static GeoPoint Centroid(List<GeoPoint> pts)
        {
            if (pts == null || pts.Count == 0) return new GeoPoint(0, 0);
            double lat = 0, lng = 0;
            foreach (var p in pts) { lat += p.Lat; lng += p.Lng; }
            return new GeoPoint(lat / pts.Count, lng / pts.Count);
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
        /// Splits clusters whose OSRM tour mileage exceeds solo OSRM legs * ratio (gas-waste guard).
        /// </summary>
        public static async Task<List<SupeyTripCluster>> SplitInefficientClustersAsync(
            List<SupeyTripCluster> clusters, CancellationToken token)
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
                bool soloOk = true;
                for (int i = 0; i < c.Trips.Count; i++)
                {
                    var leg = await SupeyOsrmLegs.GetLegAsync(c.PickupPoints[i], c.DropoffPoints[i], token)
                        .ConfigureAwait(false);
                    if (!leg.Ok)
                    {
                        soloOk = false;
                        break;
                    }
                    solo += leg.Meters;
                }

                if (!soloOk || c.IntraClusterMeters <= 0
                    || c.IsStraightLineFallback
                    || c.IntraClusterMeters <= solo * TourSplitMileageRatio)
                {
                    result.Add(c);
                    continue;
                }

                foreach (var sub in BuildSubClusters(c))
                    result.Add(sub);
            }
            return result;
        }
    }
}
