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
                return "A|" + FacilityKey(t.DOStreet, t.DOCITY);
            return "BC|" + FacilityKey(t.PUStreet, t.PUCity);
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

        /// <summary>
        /// Sets <see cref="SupeyTripCluster.PickupOrder"/> and refines <see cref="SupeyTripCluster.DropoffOrder"/>
        /// using nearest-neighbor from first PU, then deadline-feasible DO refinement.
        /// </summary>
        public static void OptimizeClusterTour(SupeyTripCluster c)
        {
            int n = c.Trips.Count;
            c.PickupOrder.Clear();
            if (n == 0) return;
            if (n == 1)
            {
                c.PickupOrder.Add(0);
                if (c.DropoffOrder.Count == 0) c.DropoffOrder.Add(0);
                return;
            }

            // NN pickup order starting from geographically first scheduled PU (index 0 after sort).
            var remaining = new List<int>(n);
            for (int i = 0; i < n; i++) remaining.Add(i);
            int current = 0;
            c.PickupOrder.Add(current);
            remaining.RemoveAt(0);
            while (remaining.Count > 0)
            {
                int best = remaining[0];
                double bestDist = HaversineMeters(c.PickupPoints[current], c.PickupPoints[best]);
                for (int i = 1; i < remaining.Count; i++)
                {
                    int cand = remaining[i];
                    double d = HaversineMeters(c.PickupPoints[current], c.PickupPoints[cand]);
                    if (d < bestDist) { bestDist = d; best = cand; }
                }
                c.PickupOrder.Add(best);
                remaining.Remove(best);
                current = best;
            }

            // DO order: start from deadline sort already in DropoffOrder; try 2-opt to shorten tail
            // while preserving per-rider deadline feasibility at average speed.
            if (c.DropoffOrder.Count != n)
            {
                c.DropoffOrder.Clear();
                for (int i = 0; i < n; i++) c.DropoffOrder.Add(i);
                c.DropoffOrder.Sort((a, b) =>
                {
                    var da = SupeyTripTimes.TryParseDO(c.Trips[a]) ?? TimeSpan.MaxValue;
                    var db = SupeyTripTimes.TryParseDO(c.Trips[b]) ?? TimeSpan.MaxValue;
                    int cmp = da.CompareTo(db);
                    return cmp != 0 ? cmp : a.CompareTo(b);
                });
            }

            RefineDropoffOrderByDistance(c);
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

        private static GeoPoint Centroid(List<GeoPoint> pts)
        {
            if (pts == null || pts.Count == 0) return new GeoPoint(0, 0);
            double sLat = 0, sLng = 0;
            foreach (var p in pts) { sLat += p.Lat; sLng += p.Lng; }
            return new GeoPoint(sLat / pts.Count, sLng / pts.Count);
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
