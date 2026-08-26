using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Template-order trip batches (between gap rows) as <see cref="SupeyTripCluster"/> groups
    /// for Schedule Builder list coloring and map legend/routes.
    /// </summary>
    internal static class ScheduleBuilderPreviewGroups
    {
        public static List<SupeyTripCluster> BuildFromPreviewLines(IEnumerable<ScheduleBuilderPreviewLine> lines)
        {
            var groups = new List<SupeyTripCluster>();
            if (lines == null) return groups;

            SupeyTripCluster current = null;
            int groupNum = 0;
            foreach (var line in lines)
            {
                if (line == null) continue;
                if (line.Kind == ScheduleBuilderPreviewLine.LineKind.Gap
                    || line.Kind == ScheduleBuilderPreviewLine.LineKind.GroupHeader)
                {
                    current = null;
                    continue;
                }
                if (line.Kind == ScheduleBuilderPreviewLine.LineKind.SectionHeader)
                    continue;
                if (line.Kind != ScheduleBuilderPreviewLine.LineKind.Trip || line.Trip == null)
                    continue;

                if (current == null)
                {
                    groupNum++;
                    current = new SupeyTripCluster
                    {
                        GroupNumber = groupNum,
                        GroupColor = SupeyGroupPalette.For(groupNum),
                    };
                    groups.Add(current);
                }
                current.Trips.Add(line.Trip);
            }

            foreach (var g in groups)
                FinalizePickupWindow(g);

            var lineList = lines as IList<ScheduleBuilderPreviewLine> ?? lines?.ToList();
            ScheduleBuilderGroupColors.ApplyOverridesFromLines(lineList, groups);
            return groups;
        }

        public static SupeyTripCluster FindGroupForTrip(IList<SupeyTripCluster> groups, MCDownloadedTrip trip)
        {
            if (groups == null || trip == null)
                return null;

            string tn = (trip.TripNumber ?? "").Trim();
            foreach (var g in groups)
            {
                if (g?.Trips == null)
                    continue;
                foreach (var t in g.Trips)
                {
                    if (t == null)
                        continue;
                    if (ReferenceEquals(t, trip)
                        || (!string.IsNullOrEmpty(tn)
                            && string.Equals(t.TripNumber, tn, StringComparison.OrdinalIgnoreCase)))
                        return g;
                }
            }

            return null;
        }

        /// <summary>
        /// One cluster per trip (PU→DO only on map). Used when group colors/groups are hidden in Settings.
        /// Gap rows are ignored — trips render as independent legs.
        /// </summary>
        public static List<SupeyTripCluster> BuildTripFlatClustersFromPreviewLines(
            IEnumerable<ScheduleBuilderPreviewLine> lines)
        {
            var groups = new List<SupeyTripCluster>();
            if (lines == null) return groups;

            int tripNum = 0;
            foreach (var line in lines)
            {
                if (line == null || line.Kind != ScheduleBuilderPreviewLine.LineKind.Trip || line.Trip == null)
                    continue;

                tripNum++;
                var cluster = new SupeyTripCluster
                {
                    GroupNumber = tripNum,
                    GroupColor = SupeyGroupPalette.For(tripNum),
                };
                cluster.Trips.Add(line.Trip);
                groups.Add(cluster);
                FinalizePickupWindow(cluster);
            }

            return groups;
        }

        internal static void FinalizePickupWindowPublic(SupeyTripCluster g) => FinalizePickupWindow(g);

        public static void ApplyGeocodes(
            SupeyTripCluster g,
            IReadOnlyDictionary<string, GeoPoint> pickupByTrip,
            IReadOnlyDictionary<string, GeoPoint> dropoffByTrip)
        {
            if (g == null) return;
            g.PickupPoints.Clear();
            g.DropoffPoints.Clear();
            foreach (var t in g.Trips)
            {
                string key = (t?.TripNumber ?? "").Trim();
                GeoPoint pu = default;
                GeoPoint dof = default;
                if (!string.IsNullOrEmpty(key))
                {
                    if (pickupByTrip != null && pickupByTrip.TryGetValue(key, out var p))
                        pu = p;
                    if (dropoffByTrip != null && dropoffByTrip.TryGetValue(key, out var d))
                        dof = d;
                }
                g.PickupPoints.Add(pu);
                g.DropoffPoints.Add(dof);
            }

            EnsureIdentityTourOrders(g);
        }

        /// <summary>PU then DO using tour order when present — same key as BUILD cluster routing.</summary>
        public static List<GeoPoint> CollectDeskRouteWaypoints(SupeyTripCluster g)
        {
            return CollectDeskRouteWaypoints(g, null, null);
        }

        /// <summary>Optional home bookends for straight-line preview only. OSRM group tours omit home so they share BUILD's cache key.</summary>
        public static List<GeoPoint> CollectDeskRouteWaypoints(
            SupeyTripCluster g, GeoPoint? routeStart, GeoPoint? routeEnd)
        {
            var waypoints = new List<GeoPoint>();
            if (g == null) return waypoints;
            AddWaypointIfValid(waypoints, routeStart);
            EnsureIdentityTourOrders(g);
            var core = SupeyOsrmLegs.BuildTourPath(g, g.PickupOrder, g.DropoffOrder);
            foreach (var p in core)
                AddWaypointIfValid(waypoints, p);
            AddWaypointIfValid(waypoints, routeEnd);
            return waypoints;
        }

        private static void EnsureIdentityTourOrders(SupeyTripCluster g)
        {
            if (g == null)
                return;
            int n = g.Trips?.Count ?? 0;
            if (n <= 0)
                return;
            if (g.PickupOrder.Count != n)
            {
                g.PickupOrder.Clear();
                for (int i = 0; i < n; i++)
                    g.PickupOrder.Add(i);
            }
            if (g.DropoffOrder.Count != n)
            {
                g.DropoffOrder.Clear();
                for (int i = 0; i < n; i++)
                    g.DropoffOrder.Add(i);
            }
        }

        /// <summary>Desk preview routes via OSRM (solid on map); straight dashed fallback when routing fails.</summary>
        public static Task<(int roadGroups, int straightGroups)> BuildOsrmRoutePolylinesAsync(
            IEnumerable<SupeyTripCluster> groups, CancellationToken token)
        {
            return BuildOsrmRoutePolylinesAsync(groups, null, token);
        }

        /// <summary>
        /// When <paramref name="homeGeo"/> is set: home starts the first group route and ends the last
        /// (both for a single-group day) — no separate deadhead overlay.
        /// </summary>
        public static Task<(int roadGroups, int straightGroups)> BuildOsrmRoutePolylinesAsync(
            IEnumerable<SupeyTripCluster> groups, GeoPoint? homeGeo, CancellationToken token)
        {
            return BuildOsrmRoutePolylinesAsync(groups, homeGeo, token, null);
        }

        public static async Task<(int roadGroups, int straightGroups)> BuildOsrmRoutePolylinesAsync(
            IEnumerable<SupeyTripCluster> groups,
            GeoPoint? homeGeo,
            CancellationToken token,
            IProgress<(int Done, int Total)> progress)
        {
            if (groups == null) return (0, 0);
            var list = groups as IList<SupeyTripCluster> ?? new List<SupeyTripCluster>(groups);
            int count = list.Count;
            if (count == 0) return (0, 0);

            int road = 0;
            int straight = 0;
            int done = 0;
            progress?.Report((0, count));

            var tasks = new Task[count];
            for (int i = 0; i < count; i++)
            {
                int index = i;
                tasks[i] = RouteOneGroupAsync(list, index, count, homeGeo, token, () =>
                {
                    int finished = Interlocked.Increment(ref done);
                    progress?.Report((finished, count));
                }, isRoad =>
                {
                    if (isRoad)
                        Interlocked.Increment(ref road);
                    else if (list[index]?.RoutePolyline.Count >= 2)
                        Interlocked.Increment(ref straight);
                });
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
            return (road, straight);
        }

        private static async Task RouteOneGroupAsync(
            IList<SupeyTripCluster> list,
            int index,
            int count,
            GeoPoint? homeGeo,
            CancellationToken token,
            Action onFinished,
            Action<bool> tally)
        {
            var g = list[index];
            if (g == null)
            {
                onFinished?.Invoke();
                return;
            }

            token.ThrowIfCancellationRequested();
            ScheduleBuilderDriverMapRouting.ResolveHomeRouteBookends(
                index, count, homeGeo, out GeoPoint? routeStart, out GeoPoint? routeEnd);
            bool isRoad = await PopulateGroupOsrmRouteAsync(g, token, routeStart, routeEnd).ConfigureAwait(false);
            tally?.Invoke(isRoad);
            onFinished?.Invoke();
        }

        /// <summary>PU→DO OSRM legs for each trip — drawn on the map when group colors are on.</summary>
        public static async Task BuildTripLegPolylinesAsync(
            IEnumerable<SupeyTripCluster> groups, CancellationToken token)
        {
            if (groups == null) return;
            var tasks = new List<Task>();
            foreach (var g in groups)
            {
                if (g == null) continue;
                tasks.Add(PopulateTripLegPolylinesAsync(g, token));
            }
            if (tasks.Count > 0)
                await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        private static async Task PopulateTripLegPolylinesAsync(SupeyTripCluster g, CancellationToken token)
        {
            int n = Math.Min(g.Trips.Count, Math.Min(g.PickupPoints.Count, g.DropoffPoints.Count));
            if (g.TripLegPolylines.Count == n && n > 0
                && g.TripLegPolylines.TrueForAll(leg => leg?.Points != null && leg.Points.Count >= 2))
            {
                return;
            }

            g.TripLegPolylines.Clear();
            var legs = new SupeyTripLegPolyline[n];
            var tasks = new List<Task>(n);
            for (int i = 0; i < n; i++)
            {
                int index = i;
                tasks.Add(Task.Run(async () =>
                {
                    token.ThrowIfCancellationRequested();
                    var pu = g.PickupPoints[index];
                    var dof = g.DropoffPoints[index];
                    var leg = new SupeyTripLegPolyline
                    {
                        TripNumber = (g.Trips[index]?.TripNumber ?? "").Trim(),
                    };
                    if (!SupeyOsrmLegs.IsRoutable(pu) || !SupeyOsrmLegs.IsRoutable(dof))
                    {
                        legs[index] = leg;
                        return;
                    }

                    var route = await SupeyOsrmLegs.RouteAsync(
                        new List<GeoPoint> { pu, dof }, token).ConfigureAwait(false);
                    if (route.Ok && route.Polyline != null && route.Polyline.Count >= 2)
                    {
                        leg.Points.AddRange(route.Polyline);
                        leg.IsStraightLineFallback = route.IsStraightLineFallback;
                    }
                    else
                    {
                        leg.Points.Add(pu);
                        leg.Points.Add(dof);
                        leg.IsStraightLineFallback = true;
                    }
                    legs[index] = leg;
                }, token));
            }

            if (tasks.Count > 0)
                await Task.WhenAll(tasks).ConfigureAwait(false);

            foreach (var leg in legs)
            {
                if (leg != null && leg.Points.Count >= 2)
                    g.TripLegPolylines.Add(leg);
            }
        }

        /// <summary>Straight PU→DO preview only (no OSRM).</summary>
        public static void BuildDeskRoutePolylines(IEnumerable<SupeyTripCluster> groups)
        {
            BuildDeskRoutePolylines(groups, null);
        }

        /// <summary>
        /// Straight-line group routes when OSRM is offline. Optional home bookends match the OSRM path.
        /// </summary>
        public static (int roadGroups, int straightGroups) BuildDeskRoutePolylines(
            IEnumerable<SupeyTripCluster> groups, GeoPoint? homeGeo)
        {
            int straight = 0;
            if (groups == null) return (0, 0);
            var list = groups as IList<SupeyTripCluster> ?? new List<SupeyTripCluster>(groups);
            int count = list.Count;
            for (int i = 0; i < count; i++)
            {
                var g = list[i];
                if (g == null) continue;
                ScheduleBuilderDriverMapRouting.ResolveHomeRouteBookends(
                    i, count, homeGeo, out GeoPoint? routeStart, out GeoPoint? routeEnd);
                var waypoints = CollectDeskRouteWaypoints(g, routeStart, routeEnd);
                ApplyStraightLineRoute(g, waypoints);
                if (g.RoutePolyline.Count >= 2)
                    straight++;
            }
            return (0, straight);
        }

        private static async Task<bool> PopulateGroupOsrmRouteAsync(
            SupeyTripCluster g, CancellationToken token, GeoPoint? routeStart = null, GeoPoint? routeEnd = null)
        {
            if (g.RoutePolyline.Count >= 2 && g.IntraClusterMeters > 0 && !g.IsStraightLineFallback)
                return true;

            g.RoutePolyline.Clear();
            // Home stays a pin. Putting it on the tour changes the OSRM key so BUILD/map never share cache.
            var waypoints = CollectDeskRouteWaypoints(g);
            if (waypoints.Count < 2)
            {
                g.IsStraightLineFallback = false;
                return false;
            }

            var route = await SupeyOsrmLegs.RouteAsync(waypoints, token).ConfigureAwait(false);
            if (route.Ok && route.Polyline != null && route.Polyline.Count >= 2)
            {
                g.RoutePolyline.AddRange(route.Polyline);
                g.IntraClusterMeters = route.TotalMeters;
                g.IsStraightLineFallback = route.IsStraightLineFallback;
                return !route.IsStraightLineFallback;
            }

            g.IntraClusterMeters = 0;
            ApplyStraightLineRoute(g, waypoints);
            return false;
        }

        private static void ApplyStraightLineRoute(SupeyTripCluster g, List<GeoPoint> waypoints)
        {
            g.RoutePolyline.Clear();
            if (waypoints == null || waypoints.Count < 2)
            {
                g.IsStraightLineFallback = false;
                return;
            }
            foreach (var p in waypoints)
                g.RoutePolyline.Add(p);
            g.IsStraightLineFallback = true;
        }

        private static void FinalizePickupWindow(SupeyTripCluster g)
        {
            if (g?.Trips == null || g.Trips.Count == 0) return;
            TimeSpan earliest = TimeSpan.MaxValue;
            TimeSpan latest = TimeSpan.MinValue;
            foreach (var t in g.Trips)
            {
                var pu = SupeyTripTimes.TryParsePU(t);
                if (!pu.HasValue) continue;
                if (pu.Value < earliest) earliest = pu.Value;
                if (pu.Value > latest) latest = pu.Value;
            }
            if (earliest == TimeSpan.MaxValue) return;
            g.EarliestPickup = earliest;
            g.LatestPickup = latest == TimeSpan.MinValue ? earliest : latest;
        }

        private static void AddWaypointIfValid(List<GeoPoint> waypoints, GeoPoint? point)
        {
            if (!point.HasValue) return;
            AddWaypointIfValid(waypoints, point.Value);
        }

        private static void AddWaypointIfValid(List<GeoPoint> waypoints, GeoPoint p)
        {
            SupeyOsrmLegs.AddWaypointIfValid(waypoints, p);
        }

    }
}
