using System;

using System.Collections.Generic;

using System.Threading;

using System.Threading.Tasks;



namespace Hiatme_Tool_Suite_v3

{

    /// <summary>OSRM (or straight fallback) miles for Schedule Builder map selection labels.</summary>

    internal static class ScheduleBuilderMapMileage

    {

        public static bool TryGetTripEndpointsFromGroup(

            SupeyTripCluster group,

            MCDownloadedTrip trip,

            out GeoPoint pu,

            out GeoPoint dof)

        {

            pu = dof = default;

            if (group?.Trips == null || trip == null) return false;



            string tn = (trip.TripNumber ?? "").Trim();

            for (int i = 0; i < group.Trips.Count; i++)

            {

                var t = group.Trips[i];

                if (!ReferenceEquals(t, trip)

                    && (string.IsNullOrEmpty(tn)

                        || !string.Equals(t?.TripNumber, tn, StringComparison.OrdinalIgnoreCase)))

                    continue;



                if (i < group.PickupPoints.Count)

                    pu = group.PickupPoints[i];

                if (i < group.DropoffPoints.Count)

                    dof = group.DropoffPoints[i];

                return IsValid(pu) && IsValid(dof);

            }



            return false;

        }



        public static async Task<(double? meters, bool approx)> ResolveTripPuDoMetersAsync(

            SupeyTripCluster group,

            MCDownloadedTrip trip,

            IReadOnlyDictionary<string, GeoPoint> pickupByTrip,

            IReadOnlyDictionary<string, GeoPoint> dropoffByTrip,

            GeoPoint? pinPu,

            GeoPoint? pinDo,

            CancellationToken token)

        {

            if (trip == null) return (null, false);



            GeoPoint pu, dof;

            if (TryGetTripEndpointsFromGroup(group, trip, out pu, out dof))

                return await LegMetersAsync(pu, dof, token).ConfigureAwait(false);



            if (pinPu.HasValue && pinDo.HasValue

                && IsValid(pinPu.Value) && IsValid(pinDo.Value))

                return await LegMetersAsync(pinPu.Value, pinDo.Value, token).ConfigureAwait(false);



            string key = (trip.TripNumber ?? "").Trim();

            if (key.Length > 0

                && pickupByTrip != null && dropoffByTrip != null

                && pickupByTrip.TryGetValue(key, out pu)

                && dropoffByTrip.TryGetValue(key, out dof)

                && IsValid(pu) && IsValid(dof))

                return await LegMetersAsync(pu, dof, token).ConfigureAwait(false);



            GeoPoint? puResolved = await ScheduleBuilderMapGeocode.ResolveEndpointAsync(

                trip.PUStreet, trip.PUCity, token).ConfigureAwait(false);

            GeoPoint? doResolved = await ScheduleBuilderMapGeocode.ResolveEndpointAsync(

                trip.DOStreet, trip.DOCITY, token).ConfigureAwait(false);

            if (puResolved.HasValue && doResolved.HasValue

                && IsValid(puResolved.Value) && IsValid(doResolved.Value))

                return await LegMetersAsync(puResolved.Value, doResolved.Value, token).ConfigureAwait(false);



            return (null, false);

        }



        public static async Task<(double? meters, bool approx)> LegMetersAsync(

            GeoPoint pu, GeoPoint dof, CancellationToken token)

        {

            if (!ScheduleOsrmGate.PreviewRoutingOk)
            {
                double offline = HaversineMeters(pu, dof);
                return (offline > 0 ? offline : (double?)null, true);
            }

            var path = new List<GeoPoint> { pu, dof };

            var route = await SupeyOsrmLegs.RouteAsync(path, token).ConfigureAwait(false);

            if (route.Ok && route.TotalMeters > 0)

                return (route.TotalMeters, route.IsStraightLineFallback);



            double straight = HaversineMeters(pu, dof);

            if (straight > 0)

                return (straight, true);



            return (null, false);

        }



        public static double GroupRouteMeters(SupeyTripCluster group)

        {

            if (group == null) return 0;

            if (group.IntraClusterMeters > 0)

                return group.IntraClusterMeters;

            var waypoints = ScheduleBuilderPreviewGroups.CollectDeskRouteWaypoints(group);

            if (waypoints.Count < 2) return 0;

            double sum = 0;

            for (int i = 1; i < waypoints.Count; i++)

                sum += HaversineMeters(waypoints[i - 1], waypoints[i]);

            return sum;

        }

        /// <summary>OSRM group tour when preview miles are missing (matches map routing).</summary>
        public static async Task<(double meters, bool approx)> ResolveGroupRouteMetersAsync(
            SupeyTripCluster group,
            CancellationToken token)
        {
            if (group == null) return (0, false);

            if (group.IntraClusterMeters > 0)
                return (group.IntraClusterMeters, group.IsStraightLineFallback);

            if (!ScheduleOsrmGate.PreviewRoutingOk)
                return (GroupRouteMeters(group), true);

            var waypoints = ScheduleBuilderPreviewGroups.CollectDeskRouteWaypoints(group);
            if (waypoints.Count < 2)
                return (0, false);

            var route = await SupeyOsrmLegs.RouteAsync(waypoints, token).ConfigureAwait(false);
            if (route.Ok && route.TotalMeters > 0)
                return (route.TotalMeters, route.IsStraightLineFallback);

            double sum = 0;
            for (int i = 1; i < waypoints.Count; i++)
                sum += HaversineMeters(waypoints[i - 1], waypoints[i]);
            return (sum, true);
        }

        /// <summary>Enumerate every trip order; above this use a bounded candidate set.</summary>
        private const int MaxExactPermutationTrips = 8;

        /// <summary>
        /// 100 = current trip list order is the shortest PU-then-DO tour for this group;
        /// lower = drag-reordering trips would save road miles.
        /// </summary>
        public static Task<(double? scorePercent, double currentMeters, double bestMeters, bool approx)> ComputeGroupEfficiencyAsync(
            SupeyTripCluster group,
            CancellationToken token)
        {
            return ComputeGroupEfficiencyAsync(
                group,
                null,
                ScheduleBuilderDriverMapRouting.GroupDayPosition.Middle,
                token);
        }

        /// <summary>
        /// When <paramref name="dayPosition"/> is first/last/sole, includes home→first PU / last DO→home in totals.
        /// </summary>
        public static async Task<(double? scorePercent, double currentMeters, double bestMeters, bool approx)> ComputeGroupEfficiencyAsync(
            SupeyTripCluster group,
            GeoPoint? homeGeo,
            ScheduleBuilderDriverMapRouting.GroupDayPosition dayPosition,
            CancellationToken token)
        {
            if (group?.Trips == null || group.Trips.Count == 0)
                return (null, 0, 0, false);

            int n = group.Trips.Count;
            bool includeHome = homeGeo.HasValue && IsValid(homeGeo.Value)
                && dayPosition != ScheduleBuilderDriverMapRouting.GroupDayPosition.Middle;

            if (n == 1 && !includeHome)
            {
                var solo = await ResolveGroupRouteMetersAsync(group, token).ConfigureAwait(false);
                double m = solo.meters;
                return (m > 0 ? (double?)100 : null, m, m, solo.approx);
            }

            if (!HasRoutableEndpoints(group, n))
                return (null, 0, 0, false);

            var currentOrder = IdentityOrder(n);

            SupeyClusterOsrmTable table = null;
            if (ScheduleOsrmGate.PreviewRoutingOk)
            {
                try
                {
                    table = await SupeyClusterOsrmTable.BuildAsync(group, token).ConfigureAwait(false);
                }
                catch
                {
                    // desk preview — fall back to haversine tour sums
                }
            }

            bool approx = table == null || group.IsStraightLineFallback;
            double? currentMeters = await TotalMetersForOrderAsync(
                group, currentOrder, table, homeGeo, dayPosition, token).ConfigureAwait(false);
            if (!currentMeters.HasValue || currentMeters.Value <= 0)
                return (null, 0, 0, true);

            double bestMeters = currentMeters.Value;
            List<int> bestOrder = new List<int>(currentOrder);
            var tripOrders = n <= MaxExactPermutationTrips
                ? AllPermutations(n)
                : BuildLinkedTripOrderCandidates(group, n);
            foreach (var tripOrder in tripOrders)
            {
                var m = await TotalMetersForOrderAsync(
                    group, tripOrder, table, homeGeo, dayPosition, token).ConfigureAwait(false);
                if (!m.HasValue)
                {
                    approx = true;
                    continue;
                }
                if (m.Value < bestMeters - 0.5)
                {
                    bestMeters = m.Value;
                    bestOrder = tripOrder;
                }
            }

            SupeyClusterOsrmTable.Clear();

            double score = Math.Min(100, Math.Round(100.0 * bestMeters / currentMeters.Value, 0));
            return (score, currentMeters.Value, bestMeters, approx);
        }

        /// <summary>Shortest PU-then-DO trip index order for a group (same search as route efficiency).</summary>
        public static async Task<(List<int> bestOrder, double? scorePercent, bool alreadyOptimal, bool approx)> FindBestTripOrderAsync(
            SupeyTripCluster group,
            GeoPoint? homeGeo,
            ScheduleBuilderDriverMapRouting.GroupDayPosition dayPosition,
            CancellationToken token)
        {
            if (group?.Trips == null || group.Trips.Count < 2)
                return (null, null, true, false);

            int n = group.Trips.Count;
            if (!HasRoutableEndpoints(group, n))
                return (null, null, false, false);

            var currentOrder = IdentityOrder(n);

            SupeyClusterOsrmTable table = null;
            if (ScheduleOsrmGate.PreviewRoutingOk)
            {
                try
                {
                    table = await SupeyClusterOsrmTable.BuildAsync(group, token).ConfigureAwait(false);
                }
                catch { }
            }

            bool approx = table == null || group.IsStraightLineFallback;
            double? currentMeters = await TotalMetersForOrderAsync(
                group, currentOrder, table, homeGeo, dayPosition, token).ConfigureAwait(false);
            if (!currentMeters.HasValue || currentMeters.Value <= 0)
            {
                SupeyClusterOsrmTable.Clear();
                return (null, null, false, true);
            }

            double bestMeters = currentMeters.Value;
            List<int> bestOrder = new List<int>(currentOrder);
            var tripOrders = n <= MaxExactPermutationTrips
                ? AllPermutations(n)
                : BuildLinkedTripOrderCandidates(group, n);
            foreach (var tripOrder in tripOrders)
            {
                var m = await TotalMetersForOrderAsync(
                    group, tripOrder, table, homeGeo, dayPosition, token).ConfigureAwait(false);
                if (!m.HasValue)
                {
                    approx = true;
                    continue;
                }
                if (m.Value < bestMeters - 0.5)
                {
                    bestMeters = m.Value;
                    bestOrder = tripOrder;
                }
            }

            SupeyClusterOsrmTable.Clear();

            bool alreadyOptimal = OrdersEqual(bestOrder, currentOrder);
            double score = Math.Min(100, Math.Round(100.0 * bestMeters / currentMeters.Value, 0));
            return (bestOrder, score, alreadyOptimal, approx);
        }

        private static bool OrdersEqual(IReadOnlyList<int> a, IReadOnlyList<int> b)
        {
            if (a == null || b == null || a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        private static async Task<double?> TotalMetersForOrderAsync(
            SupeyTripCluster group,
            List<int> tripOrder,
            SupeyClusterOsrmTable table,
            GeoPoint? homeGeo,
            ScheduleBuilderDriverMapRouting.GroupDayPosition dayPosition,
            CancellationToken token)
        {
            double? tour = TourMetersForOrder(group, tripOrder, tripOrder, table);
            if (!tour.HasValue) return null;

            double total = tour.Value;
            if (!homeGeo.HasValue || !IsValid(homeGeo.Value)
                || dayPosition == ScheduleBuilderDriverMapRouting.GroupDayPosition.Middle)
                return total;

            if (dayPosition == ScheduleBuilderDriverMapRouting.GroupDayPosition.First
                || dayPosition == ScheduleBuilderDriverMapRouting.GroupDayPosition.Sole)
            {
                GeoPoint pu = ScheduleBuilderDriverMapRouting.PickupForTripIndex(group, tripOrder[0]);
                var toFirst = await LegMetersAsync(homeGeo.Value, pu, token).ConfigureAwait(false);
                if (toFirst.meters.HasValue)
                    total += toFirst.meters.Value;
                else
                    total += HaversineMeters(homeGeo.Value, pu);
            }

            if (dayPosition == ScheduleBuilderDriverMapRouting.GroupDayPosition.Last
                || dayPosition == ScheduleBuilderDriverMapRouting.GroupDayPosition.Sole)
            {
                int lastIdx = tripOrder[tripOrder.Count - 1];
                GeoPoint dof = ScheduleBuilderDriverMapRouting.DropoffForTripIndex(group, lastIdx);
                var toHome = await LegMetersAsync(dof, homeGeo.Value, token).ConfigureAwait(false);
                if (toHome.meters.HasValue)
                    total += toHome.meters.Value;
                else
                    total += HaversineMeters(dof, homeGeo.Value);
            }

            return total;
        }

        private static bool HasRoutableEndpoints(SupeyTripCluster group, int n)
        {
            for (int i = 0; i < n; i++)
            {
                if (i >= group.PickupPoints.Count || i >= group.DropoffPoints.Count)
                    return false;
                if (!IsValid(group.PickupPoints[i]) || !IsValid(group.DropoffPoints[i]))
                    return false;
            }
            return true;
        }

        private static List<int> IdentityOrder(int n)
        {
            var order = new List<int>(n);
            for (int i = 0; i < n; i++) order.Add(i);
            return order;
        }

        private static List<List<int>> AllPermutations(int n)
        {
            var result = new List<List<int>>();
            if (n <= 0) return result;
            var work = IdentityOrder(n);
            Permute(work, 0, result);
            return result;
        }

        private static void Permute(List<int> items, int start, List<List<int>> output)
        {
            if (start >= items.Count)
            {
                output.Add(new List<int>(items));
                return;
            }
            for (int i = start; i < items.Count; i++)
            {
                int tmp = items[start];
                items[start] = items[i];
                items[i] = tmp;
                Permute(items, start + 1, output);
                tmp = items[start];
                items[start] = items[i];
                items[i] = tmp;
            }
        }

        private static List<List<int>> BuildLinkedTripOrderCandidates(SupeyTripCluster group, int n)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var list = new List<List<int>>();
            void Add(List<int> order)
            {
                if (order == null || order.Count != n) return;
                string key = string.Join(",", order);
                if (seen.Add(key)) list.Add(order);
            }

            Add(IdentityOrder(n));
            var reversed = IdentityOrder(n);
            reversed.Reverse();
            Add(reversed);
            Add(SupeyClusterRouting.BuildPickupOrderByPuTimePublic(group));

            var baseOrder = IdentityOrder(n);
            for (int i = 0; i < n - 1; i++)
            {
                var swapped = new List<int>(baseOrder);
                int tmp = swapped[i];
                swapped[i] = swapped[i + 1];
                swapped[i + 1] = tmp;
                Add(swapped);
            }
            return list;
        }

        /// <summary>All PUs in <paramref name="puOrder"/>, then all DOs in <paramref name="doOrder"/>.</summary>
        private static double? TourMetersForOrder(
            SupeyTripCluster group,
            List<int> puOrder,
            List<int> doOrder,
            SupeyClusterOsrmTable table)
        {
            if (table != null)
            {
                var matrixMeters = table.TourMeters(group, puOrder, doOrder);
                if (matrixMeters.HasValue)
                    return matrixMeters;
            }

            var path = SupeyOsrmLegs.BuildTourPath(group, puOrder, doOrder);
            if (path.Count < 2) return null;

            double sum = 0;
            for (int i = 1; i < path.Count; i++)
                sum += HaversineMeters(path[i - 1], path[i]);
            return sum > 0 ? sum : (double?)null;
        }

        public static string FormatRouteChangeDelta(double deltaMeters)
        {
            if (Math.Abs(deltaMeters) < 160.934) // ~0.1 mi
                return "No change from last move";
            string mi = SupeyTripTimes.FormatMiles(Math.Abs(deltaMeters));
            return deltaMeters > 0
                ? "+" + mi + " vs before move"
                : "−" + mi + " vs before move";
        }



        private static bool IsValid(GeoPoint p) => !(p.Lat == 0 && p.Lng == 0);



        private static double HaversineMeters(GeoPoint a, GeoPoint b)

        {

            const double r = 6371000;

            double dLat = (b.Lat - a.Lat) * Math.PI / 180;

            double dLng = (b.Lng - a.Lng) * Math.PI / 180;

            double lat1 = a.Lat * Math.PI / 180;

            double lat2 = b.Lat * Math.PI / 180;

            double h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)

                + Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);

            return 2 * r * Math.Asin(Math.Min(1, Math.Sqrt(h)));

        }

    }

}


