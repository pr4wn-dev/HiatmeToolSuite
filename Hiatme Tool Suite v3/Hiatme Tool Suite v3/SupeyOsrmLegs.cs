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

        public static void BeginBuildSession() => Cache.Clear();

        public static SupeyRouteCache SharedCache => Cache;

        public static bool IsRoutable(GeoPoint p) => !(p.Lat == 0 && p.Lng == 0);

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
                    path.Add(c.PickupPoints[idx]);
            }
            foreach (int idx in doOrder)
            {
                if (idx >= 0 && idx < c.DropoffPoints.Count)
                    path.Add(c.DropoffPoints[idx]);
            }
            return path;
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
    }
}
