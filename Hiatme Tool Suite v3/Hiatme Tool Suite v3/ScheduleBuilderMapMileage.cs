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


