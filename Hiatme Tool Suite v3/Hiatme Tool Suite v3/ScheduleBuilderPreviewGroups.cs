using System;
using System.Collections.Generic;
using System.Drawing;
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
        }

        /// <summary>PU stops in trip order, then DO stops — same waypoint order as Supey cluster tours.</summary>
        public static List<GeoPoint> CollectDeskRouteWaypoints(SupeyTripCluster g)
        {
            return CollectDeskRouteWaypoints(g, null, null);
        }

        /// <summary>Optional home (or other) bookends prepended/appended to the desk tour.</summary>
        public static List<GeoPoint> CollectDeskRouteWaypoints(
            SupeyTripCluster g, GeoPoint? routeStart, GeoPoint? routeEnd)
        {
            var waypoints = new List<GeoPoint>();
            if (g == null) return waypoints;
            AddWaypointIfValid(waypoints, routeStart);
            int n = Math.Min(g.Trips.Count, Math.Min(g.PickupPoints.Count, g.DropoffPoints.Count));
            for (int i = 0; i < n; i++)
                AddWaypointIfValid(waypoints, g.PickupPoints[i]);
            for (int i = 0; i < n; i++)
                AddWaypointIfValid(waypoints, g.DropoffPoints[i]);
            AddWaypointIfValid(waypoints, routeEnd);
            return waypoints;
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
        public static async Task<(int roadGroups, int straightGroups)> BuildOsrmRoutePolylinesAsync(
            IEnumerable<SupeyTripCluster> groups, GeoPoint? homeGeo, CancellationToken token)
        {
            int road = 0, straight = 0;
            if (groups == null) return (0, 0);
            var list = groups as IList<SupeyTripCluster> ?? new List<SupeyTripCluster>(groups);
            int count = list.Count;
            for (int i = 0; i < count; i++)
            {
                var g = list[i];
                if (g == null) continue;
                token.ThrowIfCancellationRequested();
                ScheduleBuilderDriverMapRouting.ResolveHomeRouteBookends(
                    i, count, homeGeo, out GeoPoint? routeStart, out GeoPoint? routeEnd);
                if (await PopulateGroupOsrmRouteAsync(g, token, routeStart, routeEnd).ConfigureAwait(false))
                    road++;
                else if (g.RoutePolyline.Count >= 2)
                    straight++;
            }
            return (road, straight);
        }

        /// <summary>PU→DO OSRM legs for each trip — drawn on the map when group colors are on.</summary>
        public static async Task BuildTripLegPolylinesAsync(
            IEnumerable<SupeyTripCluster> groups, CancellationToken token)
        {
            if (groups == null) return;
            foreach (var g in groups)
            {
                if (g == null) continue;
                token.ThrowIfCancellationRequested();
                await PopulateTripLegPolylinesAsync(g, token).ConfigureAwait(false);
            }
        }

        private static async Task PopulateTripLegPolylinesAsync(SupeyTripCluster g, CancellationToken token)
        {
            g.TripLegPolylines.Clear();
            int n = Math.Min(g.Trips.Count, Math.Min(g.PickupPoints.Count, g.DropoffPoints.Count));
            for (int i = 0; i < n; i++)
            {
                var pu = g.PickupPoints[i];
                var dof = g.DropoffPoints[i];
                if (!SupeyOsrmLegs.IsRoutable(pu) || !SupeyOsrmLegs.IsRoutable(dof))
                    continue;

                var leg = new SupeyTripLegPolyline
                {
                    TripNumber = (g.Trips[i]?.TripNumber ?? "").Trim(),
                };
                var waypoints = new List<GeoPoint> { pu, dof };
                var route = await SupeyOsrmLegs.RouteAsync(waypoints, token).ConfigureAwait(false);
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
            g.RoutePolyline.Clear();
            var waypoints = CollectDeskRouteWaypoints(g, routeStart, routeEnd);
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
            if (p.Lat == 0 && p.Lng == 0) return;
            if (waypoints.Count > 0)
            {
                var last = waypoints[waypoints.Count - 1];
                if (Math.Abs(last.Lat - p.Lat) < 1e-6 && Math.Abs(last.Lng - p.Lng) < 1e-6)
                    return;
            }
            waypoints.Add(p);
        }

    }
}
