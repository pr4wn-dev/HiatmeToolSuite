using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// After AI JSON is applied: geocode PU/DO for map pins, then OSRM polylines for display
    /// (trip order from the AI schedule — not a local build pass).
    /// </summary>
    internal static class SupeyScheduleGeocoder
    {
        public static async Task HydrateResultAsync(SupeyScheduleResult result, CancellationToken token = default)
        {
            if (result == null) return;
            if (result.DriverPlans != null)
            {
                foreach (var plan in result.DriverPlans)
                {
                    if (plan == null) continue;
                    await HydratePlanAsync(plan, token).ConfigureAwait(false);
                    await HydrateMapRoutesAsync(plan, token).ConfigureAwait(false);
                }
            }
        }

        public static async Task HydratePlanAsync(SupeyDriverPlan plan, CancellationToken token = default)
        {
            if (plan == null) return;

            if (plan.Driver != null && !plan.HomeGeo.HasValue)
            {
                var home = await AddressGeocoder.ResolveWithFallbacksAsync(
                    plan.Driver.HomeStreet,
                    plan.Driver.HomeCity,
                    plan.Driver.HomeState,
                    plan.Driver.HomeZip,
                    "us",
                    token).ConfigureAwait(false);
                if (home.HasValue)
                    plan.HomeGeo = home;
            }

            if (plan.Groups == null) return;
            foreach (var g in plan.Groups)
            {
                if (g == null) continue;
                g.PickupPoints.Clear();
                g.DropoffPoints.Clear();
                foreach (var t in g.Trips)
                {
                    token.ThrowIfCancellationRequested();
                    if (t == null) continue;
                    var pu = await AddressGeocoder.ResolveTripEndpointAsync(
                        t.PUStreet, t.PUCity, token).ConfigureAwait(false);
                    var dof = await AddressGeocoder.ResolveTripEndpointAsync(
                        t.DOStreet, t.DOCITY, token).ConfigureAwait(false);
                    g.PickupPoints.Add(pu ?? new GeoPoint(0, 0));
                    g.DropoffPoints.Add(dof ?? new GeoPoint(0, 0));
                }
            }
        }

        /// <summary>Draw group route lines on the map from AI trip order + geocoded pins.</summary>
        public static async Task HydrateMapRoutesAsync(SupeyDriverPlan plan, CancellationToken token = default)
        {
            if (plan?.Groups == null) return;
            foreach (var g in plan.Groups)
            {
                if (g == null) continue;
                token.ThrowIfCancellationRequested();
                g.RoutePolyline.Clear();
                var waypoints = new List<GeoPoint>();
                int n = Math.Min(g.Trips.Count, Math.Min(g.PickupPoints.Count, g.DropoffPoints.Count));
                for (int i = 0; i < n; i++)
                {
                    AddWaypointIfValid(waypoints, g.PickupPoints[i]);
                    AddWaypointIfValid(waypoints, g.DropoffPoints[i]);
                }
                if (waypoints.Count < 2) continue;

                var route = await OsrmRouteResolver.RouteBestEffortAsync(waypoints, token).ConfigureAwait(false);
                if (!route.Ok || route.Polyline == null) continue;
                foreach (var p in route.Polyline)
                    g.RoutePolyline.Add(p);
                g.IsStraightLineFallback = route.IsStraightLineFallback;
            }
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
