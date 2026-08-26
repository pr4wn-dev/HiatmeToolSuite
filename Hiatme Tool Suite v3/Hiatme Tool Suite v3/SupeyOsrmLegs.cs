using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Shared OSRM leg cache for one BUILD session (cluster tours, assign, corridor).</summary>
    internal static class SupeyOsrmLegs
    {
        private static readonly SupeyRouteCache Cache = new SupeyRouteCache();

        public readonly struct Leg
        {
            public readonly double Seconds;
            public readonly double Meters;
            public readonly bool Ok;

            public Leg(double seconds, double meters, bool ok)
            {
                Seconds = seconds;
                Meters = meters;
                Ok = ok;
            }

            public static Leg Unavailable => new Leg(0, 0, false);
        }

        /// <summary>Kept for call sites. Does not wipe the shared/disk route cache — that made the map re-fetch every tab.</summary>
        public static void BeginBuildSession() => Cache.Clear();

        public static SupeyRouteCache SharedCache => Cache;

        public static bool IsRoutable(GeoPoint p) => !(p.Lat == 0 && p.Lng == 0);

        /// <summary>Match <see cref="SupeyRouteCache"/> key precision (F5 ≈ 1.1 m).</summary>
        internal const double WaypointEpsilon = 1e-5;

        public static async Task<Leg> GetLegAsync(GeoPoint from, GeoPoint to, CancellationToken token)
        {
            if (!IsRoutable(from) || !IsRoutable(to))
                return Leg.Unavailable;

            var route = await Cache.GetAsync(new List<GeoPoint> { from, to }, token).ConfigureAwait(false);
            if (!route.Ok || route.IsStraightLineFallback || route.TotalSeconds <= 0)
                return Leg.Unavailable;

            return new Leg(route.TotalSeconds, route.TotalMeters, true);
        }

        public static async Task<RouteEstimator.RoutePolylineResult> RouteAsync(
            IList<GeoPoint> path, CancellationToken token)
        {
            if (path == null || path.Count < 2)
                return RouteEstimator.RoutePolylineResult.Fail("Not enough waypoints.");
            return await Cache.GetAsync(path, token).ConfigureAwait(false);
        }

        public static List<GeoPoint> BuildTourPath(SupeyTripCluster c, List<int> puOrder, List<int> doOrder)
        {
            var path = new List<GeoPoint>();
            if (c == null || puOrder == null || doOrder == null || puOrder.Count == 0)
                return path;

            foreach (int idx in puOrder)
            {
                if (idx >= 0 && idx < c.PickupPoints.Count)
                    AddWaypointIfValid(path, c.PickupPoints[idx]);
            }
            foreach (int idx in doOrder)
            {
                if (idx >= 0 && idx < c.DropoffPoints.Count)
                    AddWaypointIfValid(path, c.DropoffPoints[idx]);
            }
            return path;
        }

        internal static void AddWaypointIfValid(List<GeoPoint> waypoints, GeoPoint p)
        {
            if (waypoints == null) return;
            if (p.Lat == 0 && p.Lng == 0) return;
            if (waypoints.Count > 0)
            {
                var last = waypoints[waypoints.Count - 1];
                if (Math.Abs(last.Lat - p.Lat) < WaypointEpsilon
                    && Math.Abs(last.Lng - p.Lng) < WaypointEpsilon)
                    return;
            }
            waypoints.Add(p);
        }

        /// <summary>Full PU→DO tour distance/seconds from OSRM (null if unavailable).</summary>
        public static async Task<(double? meters, double? seconds)> TourMetricsAsync(
            SupeyTripCluster c, List<int> puOrder, List<int> doOrder, CancellationToken token)
        {
            var path = BuildTourPath(c, puOrder, doOrder);
            if (path.Count < 2)
                return (0, 0);

            var route = await RouteAsync(path, token).ConfigureAwait(false);
            if (!route.Ok || route.IsStraightLineFallback)
                return (null, null);

            return (route.TotalMeters, route.TotalSeconds);
        }

        /// <summary>
        /// Fetch (or hit cache for) the final group polyline so the map does not
        /// wait on OSRM after BUILD used the distance table only.
        /// </summary>
        public static async Task WarmTourGeometryAsync(SupeyTripCluster c, CancellationToken token)
        {
            if (c == null) return;
            var path = BuildTourPath(c, c.PickupOrder, c.DropoffOrder);
            if (path.Count < 2) return;
            try
            {
                await RouteAsync(path, token).ConfigureAwait(false);
                // Preview map lists trips in pickup-visit order, then DOs in that same
                // row order. Warm that key too when it differs from the road drop tour.
                if (c.PickupOrder != null && c.PickupOrder.Count > 0)
                {
                    var mapPath = BuildTourPath(c, c.PickupOrder, c.PickupOrder);
                    if (mapPath.Count >= 2 && !SamePath(path, mapPath))
                        await RouteAsync(mapPath, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // BUILD must still finish if one group polyline fails.
            }
        }

        private static bool SamePath(List<GeoPoint> a, List<GeoPoint> b)
        {
            if (a == null || b == null || a.Count != b.Count)
                return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (Math.Abs(a[i].Lat - b[i].Lat) > WaypointEpsilon
                    || Math.Abs(a[i].Lng - b[i].Lng) > WaypointEpsilon)
                    return false;
            }
            return true;
        }

        public static void FlushRouteCache() => Cache.Flush();
    }
}
