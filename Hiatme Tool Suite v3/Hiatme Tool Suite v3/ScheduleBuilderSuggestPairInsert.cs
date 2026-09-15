using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Jaw/Lu pair insert inside a desk group: all pickups, then all drops.
    /// Tries where the new pickup goes and where the new drop goes; keeps the
    /// pair that still hits windows, seats, ride time, and shift for every rider.
    /// </summary>
    internal sealed class ScheduleBuilderSuggestPairResult
    {
        public bool Feasible { get; set; }
        public int PickupInsertIndex { get; set; }
        public int DropoffInsertIndex { get; set; }
        public List<string> PickupTripKeys { get; } = new List<string>();
        public List<string> DropoffTripKeys { get; } = new List<string>();
        public double IntraClusterMeters { get; set; }
        public string FailureReason { get; set; } = "";
    }

    internal static class ScheduleBuilderSuggestPairInsert
    {
        internal static string TripKey(MCDownloadedTrip t)
        {
            string n = (t?.TripNumber ?? "").Trim();
            if (n.Length > 0)
                return n;
            string client = (t?.ClientFullName ?? "").Trim();
            var pu = SupeyTripTimes.TryParsePU(t);
            return client + "@" + (pu.HasValue ? pu.Value.ToString() : "");
        }

        internal static int IndexOfTrip(SupeyTripCluster cluster, MCDownloadedTrip trip)
        {
            if (cluster?.Trips == null || trip == null)
                return -1;
            string key = TripKey(trip);
            for (int i = 0; i < cluster.Trips.Count; i++)
            {
                var t = cluster.Trips[i];
                if (t == null)
                    continue;
                if (ReferenceEquals(t, trip))
                    return i;
                if (key.Length > 0 && string.Equals(TripKey(t), key, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        internal static async Task<ScheduleBuilderSuggestPairResult> TryBestPairAsync(
            SupeyTripCluster cluster,
            MCDownloadedTrip newTrip,
            GeoPoint? approachFrom,
            TimeSpan approachClock,
            TimeSpan shiftEnd,
            int capacityPassengers,
            CancellationToken token)
        {
            var result = new ScheduleBuilderSuggestPairResult();
            if (cluster?.Trips == null || cluster.Trips.Count == 0 || newTrip == null)
            {
                result.FailureReason = "No group to insert into.";
                return result;
            }

            if (cluster.RiderCount > capacityPassengers)
            {
                result.FailureReason = "Group would have " + cluster.RiderCount
                    + " riders — " + capacityPassengers + " passenger capacity.";
                return result;
            }

            int newIdx = IndexOfTrip(cluster, newTrip);
            if (newIdx < 0)
            {
                result.FailureReason = "New trip is not in the group.";
                return result;
            }

            if (newIdx >= cluster.PickupPoints.Count
                || newIdx >= cluster.DropoffPoints.Count
                || !SupeyOsrmLegs.IsRoutable(cluster.PickupPoints[newIdx])
                || !SupeyOsrmLegs.IsRoutable(cluster.DropoffPoints[newIdx]))
            {
                result.FailureReason = "no pickup/drop addresses";
                return result;
            }

            int n = cluster.Trips.Count;
            var basePu = BaseOrderExcluding(cluster.PickupOrder, n, newIdx);
            var baseDo = BaseOrderExcluding(cluster.DropoffOrder, n, newIdx);

            SupeyClusterOsrmTable table = null;
            if (n >= 2)
                table = await SupeyClusterOsrmTable.BuildAsync(cluster, token).ConfigureAwait(false);

            var approachMetersByFirst = new Dictionary<int, double>();
            var approachSecByFirst = new Dictionary<int, double>();
            if (approachFrom.HasValue && SupeyOsrmLegs.IsRoutable(approachFrom.Value))
            {
                for (int first = 0; first < n; first++)
                {
                    if (first >= cluster.PickupPoints.Count
                        || !SupeyOsrmLegs.IsRoutable(cluster.PickupPoints[first]))
                        continue;
                    var leg = await SupeyOsrmLegs.GetLegAsync(
                        approachFrom.Value, cluster.PickupPoints[first], token)
                        .ConfigureAwait(false);
                    approachMetersByFirst[first] = leg.Meters > 0
                        ? leg.Meters
                        : StraightMeters(approachFrom.Value, cluster.PickupPoints[first]);
                    approachSecByFirst[first] = leg.Seconds > 0
                        ? leg.Seconds
                        : EstimateLegSeconds(approachFrom.Value, cluster.PickupPoints[first]);
                }
            }

            ScheduleBuilderSuggestPairResult best = null;
            double bestCost = double.MaxValue;
            string lastWhy = "No pickup/drop pair still works for the other riders.";

            int slots = n;
            for (int i = 0; i < slots; i++)
            {
                var puOrder = InsertAt(basePu, newIdx, i);
                for (int j = 0; j < slots; j++)
                {
                    token.ThrowIfCancellationRequested();
                    var doOrder = InsertAt(baseDo, newIdx, j);
                    SupeyClusterRouting.ApplyOrdersPublic(cluster, puOrder, doOrder);

                    bool bound = false;
                    if (table != null)
                        bound = table.TryApplyTourMetrics(cluster);
                    if (!bound)
                    {
                        await ScheduleBuilderDriverSuggestRouting.BindClusterTourLegsAsync(
                            cluster, prepCache: null, token).ConfigureAwait(false);
                    }

                    int firstPu = cluster.PickupOrder.Count > 0 ? cluster.PickupOrder[0] : newIdx;
                    double approachM = 0;
                    TimeSpan arrival = approachClock;
                    if (approachSecByFirst.TryGetValue(firstPu, out double aSec))
                    {
                        arrival = approachClock.Add(TimeSpan.FromSeconds(aSec));
                        approachMetersByFirst.TryGetValue(firstPu, out approachM);
                    }

                    if (!PairWalks(cluster, arrival, shiftEnd, out string why))
                    {
                        lastWhy = why;
                        continue;
                    }

                    double cost = cluster.IntraClusterMeters + Math.Max(0, approachM);
                    if (cost + 0.5 < bestCost)
                    {
                        bestCost = cost;
                        best = Snapshot(cluster, i, j);
                    }
                }
            }

            if (best == null)
            {
                result.FailureReason = lastWhy;
                return result;
            }

            var winPu = MapKeysToIndices(cluster, best.PickupTripKeys);
            var winDo = MapKeysToIndices(cluster, best.DropoffTripKeys);
            if (winPu == null || winDo == null)
            {
                result.FailureReason = lastWhy;
                return result;
            }

            SupeyClusterRouting.ApplyOrdersPublic(cluster, winPu, winDo);
            cluster.SuggestTourLocked = true;
            if (table == null || !table.TryApplyTourMetrics(cluster))
            {
                await ScheduleBuilderDriverSuggestRouting.BindClusterTourLegsAsync(
                    cluster, prepCache: null, token).ConfigureAwait(false);
            }

            return best;
        }

        internal static List<int> MapKeysToIndices(SupeyTripCluster cluster, IList<string> keys)
        {
            if (cluster?.Trips == null || keys == null || keys.Count != cluster.Trips.Count)
                return null;
            var order = new List<int>(keys.Count);
            var seen = new bool[cluster.Trips.Count];
            foreach (string key in keys)
            {
                int idx = -1;
                for (int i = 0; i < cluster.Trips.Count; i++)
                {
                    if (string.Equals(TripKey(cluster.Trips[i]), key, StringComparison.OrdinalIgnoreCase))
                    {
                        idx = i;
                        break;
                    }
                }
                if (idx < 0 || seen[idx])
                    return null;
                seen[idx] = true;
                order.Add(idx);
            }
            return order;
        }

        private static ScheduleBuilderSuggestPairResult Snapshot(
            SupeyTripCluster cluster, int puInsert, int doInsert)
        {
            var snap = new ScheduleBuilderSuggestPairResult
            {
                Feasible = true,
                PickupInsertIndex = puInsert,
                DropoffInsertIndex = doInsert,
                IntraClusterMeters = cluster.IntraClusterMeters,
            };
            foreach (int idx in cluster.PickupOrder)
                snap.PickupTripKeys.Add(TripKey(cluster.Trips[idx]));
            foreach (int idx in cluster.DropoffOrder)
                snap.DropoffTripKeys.Add(TripKey(cluster.Trips[idx]));
            return snap;
        }

        private static bool PairWalks(
            SupeyTripCluster cluster,
            TimeSpan arrivalAtFirstPU,
            TimeSpan shiftEnd,
            out string why)
        {
            why = "";
            TimeSpan start = arrivalAtFirstPU > cluster.EffectiveEarliestPickup
                ? arrivalAtFirstPU
                : cluster.EffectiveEarliestPickup;

            if (!PickupWalkOnTime(cluster, start, out why))
                return false;

            double clusterDoCap = SupeyTripTimingPolicy.DoLateCapMinutesForCluster(cluster);
            var (ok, end, worstIdx, lateMin) = SupeyScheduleAlgorithm.ProjectClusterFeasibilityPublic(
                cluster, start, clusterDoCap);
            if (!ok && worstIdx >= 0 && worstIdx < cluster.Trips.Count && lateMin > 0)
            {
                double cap = SuggestRiderWindows.DoLateCap(cluster.Trips[worstIdx]);
                if (lateMin <= cap)
                    ok = true;
            }
            if (!ok)
            {
                why = lateMin > 0
                    ? "Drop-off would run about " + lateMin.ToString("0") + " min late for another rider."
                    : "Pickup/drop timing fails for a rider already on this van.";
                return false;
            }

            if (!RideTimesOk(cluster, start, out why))
                return false;

            if (end > shiftEnd)
            {
                why = "Would run past shift end "
                    + SupeyTripTimes.FormatTimeOfDay(shiftEnd) + ".";
                return false;
            }

            return true;
        }

        private static bool PickupWalkOnTime(SupeyTripCluster c, TimeSpan arrivalAtFirst, out string why)
        {
            why = "";
            if (c.PickupOrder == null || c.PickupOrder.Count == 0)
                return true;

            int first = c.PickupOrder[0];
            TimeSpan clock = arrivalAtFirst;
            if (TooLateForPickup(c, first, clock))
            {
                why = "Would arrive too late for the first pickup in this pair.";
                return false;
            }
            clock = SupeyDispatchDriveClock.AfterPickup(c, first, clock);

            for (int step = 1; step < c.PickupOrder.Count; step++)
            {
                int idx = c.PickupOrder[step];
                double sec = (step - 1) < c.PickupLegSeconds.Count
                    ? c.PickupLegSeconds[step - 1]
                    : 0;
                clock = clock.Add(TimeSpan.FromSeconds(sec));
                if (TooLateForPickup(c, idx, clock))
                {
                    why = "Would arrive too late for a pickup already on this van.";
                    return false;
                }
                clock = SupeyDispatchDriveClock.AfterPickup(c, idx, clock);
            }
            return true;
        }

        private static bool TooLateForPickup(SupeyTripCluster c, int idx, TimeSpan arrive)
        {
            SupeyDispatchDriveClock.PickupWindow(c, idx, out _, out _, out TimeSpan latest);
            return latest > TimeSpan.Zero && arrive > latest;
        }

        private static bool RideTimesOk(SupeyTripCluster c, TimeSpan arrivalAtFirst, out string why)
        {
            why = "";
            int n = c.Trips.Count;
            var puAt = new TimeSpan[n];
            var doAt = new TimeSpan[n];
            var sawPu = new bool[n];
            var sawDo = new bool[n];

            if (c.PickupOrder == null || c.PickupOrder.Count == 0)
                return true;

            TimeSpan start = arrivalAtFirst > c.EffectiveEarliestPickup
                ? arrivalAtFirst
                : c.EffectiveEarliestPickup;
            int first = c.PickupOrder[0];
            TimeSpan clock = SupeyDispatchDriveClock.AfterPickup(c, first, start);
            puAt[first] = clock;
            sawPu[first] = true;

            for (int step = 1; step < c.PickupOrder.Count; step++)
            {
                int idx = c.PickupOrder[step];
                double sec = (step - 1) < c.PickupLegSeconds.Count
                    ? c.PickupLegSeconds[step - 1]
                    : 0;
                clock = clock.Add(TimeSpan.FromSeconds(sec));
                clock = SupeyDispatchDriveClock.AfterPickup(c, idx, clock);
                puAt[idx] = clock;
                sawPu[idx] = true;
            }

            for (int i = 0; i < c.DropoffOrder.Count; i++)
            {
                double sec = i < c.DropoffLegSeconds.Count ? c.DropoffLegSeconds[i] : 0;
                clock = clock.Add(TimeSpan.FromSeconds(sec));
                int idx = c.DropoffOrder[i];
                var trip = c.Trips[idx];
                clock = SupeyDispatchDriveClock.AfterDropoff(trip, clock);
                doAt[idx] = clock;
                sawDo[idx] = true;
            }

            for (int i = 0; i < n; i++)
            {
                if (!sawPu[i] || !sawDo[i])
                    continue;
                var schedPu = SupeyTripTimes.TryParsePU(c.Trips[i]);
                var schedDo = SupeyTripTimes.TryParseDO(c.Trips[i]);
                if (!schedPu.HasValue || !schedDo.HasValue)
                    continue;
                double scheduledRide = (schedDo.Value - schedPu.Value).TotalMinutes;
                if (scheduledRide <= 0)
                    continue;
                double projected = (doAt[i] - puAt[i]).TotalMinutes;
                double cap = SuggestRiderWindows.DoLateCap(c.Trips[i]);
                if (projected > scheduledRide + cap + 0.6)
                {
                    why = "Would keep a rider on the van longer than their window allows.";
                    return false;
                }
            }
            return true;
        }

        private static List<int> BaseOrderExcluding(IList<int> existing, int n, int exclude)
        {
            var order = new List<int>(n - 1);
            if (existing != null && existing.Count == n && SupeyClusterRouting.IsValidVisitOrder(existing, n))
            {
                foreach (int idx in existing)
                {
                    if (idx != exclude)
                        order.Add(idx);
                }
                if (order.Count == n - 1)
                    return order;
                order.Clear();
            }
            for (int i = 0; i < n; i++)
            {
                if (i != exclude)
                    order.Add(i);
            }
            return order;
        }

        private static List<int> InsertAt(List<int> baseOrder, int newIdx, int pos)
        {
            var o = new List<int>(baseOrder.Count + 1);
            o.AddRange(baseOrder);
            int at = Math.Max(0, Math.Min(pos, o.Count));
            o.Insert(at, newIdx);
            return o;
        }

        private static double EstimateLegSeconds(GeoPoint from, GeoPoint to) =>
            StraightMeters(from, to) / 12.0;

        private static double StraightMeters(GeoPoint a, GeoPoint b)
        {
            if (!SupeyOsrmLegs.IsRoutable(a) || !SupeyOsrmLegs.IsRoutable(b))
                return 0;
            const double R = 6371000;
            double dLat = (b.Lat - a.Lat) * Math.PI / 180;
            double dLng = (b.Lng - a.Lng) * Math.PI / 180;
            double x = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(a.Lat * Math.PI / 180) * Math.Cos(b.Lat * Math.PI / 180)
                * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
            return R * 2 * Math.Atan2(Math.Sqrt(x), Math.Sqrt(1 - x)) * 1.25;
        }
    }
}
