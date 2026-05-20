using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// OSRM routing with retries, local→public fallback, and segmented chains before straight-line.
    /// </summary>
    internal static class OsrmRouteResolver
    {
        private const int MaxUrlLength = 7500;
        private const int MaxWaypointsPerRequest = 12;
        private const int AttemptsPerEndpoint = 2;
        private const string RouteQuery =
            "overview=full&geometries=geojson&alternatives=false&steps=false&annotations=distance,duration";

        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        public static async Task<RouteEstimator.RoutePolylineResult> RouteBestEffortAsync(
            IList<GeoPoint> waypoints, CancellationToken token)
        {
            if (waypoints == null || waypoints.Count < 2)
                return RouteEstimator.RoutePolylineResult.Fail("Not enough waypoints to route.");

            string coordPath;
            string coordError;
            if (!TryBuildCoordinatePath(waypoints, out coordPath, out coordError))
                return RouteEstimator.RoutePolylineResult.Fail(coordError);

            if (EstimateUrlLength(coordPath) > MaxUrlLength)
                return await RouteSegmentedAsync(waypoints, token).ConfigureAwait(false);

            var lastError = "OSRM unavailable.";
            foreach (string endpoint in OsrmSettings.RouteEndpointsInOrder())
            {
                for (int attempt = 0; attempt < AttemptsPerEndpoint; attempt++)
                {
                    token.ThrowIfCancellationRequested();
                    if (attempt > 0)
                        await Task.Delay(400, token).ConfigureAwait(false);

                    var parsed = await TryFetchParseAsync(endpoint, coordPath, waypoints, token)
                        .ConfigureAwait(false);
                    if (parsed.Ok && !parsed.IsStraightLineFallback)
                        return parsed;
                    if (!string.IsNullOrEmpty(parsed.ErrorMessage))
                        lastError = parsed.ErrorMessage;
                }
            }

            return BuildStraightLineLastResort(waypoints, lastError);
        }

        private static async Task<RouteEstimator.RoutePolylineResult> RouteSegmentedAsync(
            IList<GeoPoint> waypoints, CancellationToken token)
        {
            var legDurations = new List<double>();
            var legDistances = new List<double>();
            var polyline = new List<GeoPoint>();
            double totalSeconds = 0;
            double totalMeters = 0;

            int start = 0;
            while (start < waypoints.Count - 1)
            {
                int end = Math.Min(waypoints.Count - 1, start + MaxWaypointsPerRequest);
                var slice = new List<GeoPoint>(end - start + 1);
                for (int i = start; i <= end; i++)
                    slice.Add(waypoints[i]);

                string slicePath;
                string sliceErr;
                if (!TryBuildCoordinatePath(slice, out slicePath, out sliceErr))
                    return RouteEstimator.RoutePolylineResult.Fail(sliceErr);

                RouteEstimator.RoutePolylineResult seg = null;
                string segError = "segment routing failed";
                foreach (string endpoint in OsrmSettings.RouteEndpointsInOrder())
                {
                    seg = await TryFetchParseAsync(endpoint, slicePath, slice, token).ConfigureAwait(false);
                    if (seg.Ok && !seg.IsStraightLineFallback)
                        break;
                    segError = seg.ErrorMessage ?? segError;
                }

                if (seg == null || !seg.Ok || seg.IsStraightLineFallback)
                    return BuildStraightLineLastResort(waypoints,
                        "Could not route a long trip chain (" + slice.Count + " stops in segment): " + segError);

                if (seg.LegDurations != null)
                {
                    legDurations.AddRange(seg.LegDurations);
                    if (seg.LegDistances != null)
                        legDistances.AddRange(seg.LegDistances);
                }
                totalSeconds += seg.TotalSeconds;
                totalMeters += seg.TotalMeters;

                if (polyline.Count == 0)
                    polyline.AddRange(seg.Polyline);
                else if (seg.Polyline != null)
                {
                    for (int i = 1; i < seg.Polyline.Count; i++)
                        polyline.Add(seg.Polyline[i]);
                }

                start = end;
            }

            if (polyline.Count == 0)
                polyline.AddRange(waypoints);

            return RouteEstimator.RoutePolylineResult.Success(
                legDurations, legDistances, polyline, totalSeconds, totalMeters);
        }

        private static async Task<RouteEstimator.RoutePolylineResult> TryFetchParseAsync(
            string routeBaseUri,
            string coordPath,
            IList<GeoPoint> waypoints,
            CancellationToken token)
        {
            string url = OsrmSettings.BuildRouteRequestUrl(routeBaseUri, coordPath, RouteQuery);
            try
            {
                using (var resp = await Http.GetAsync(url, token).ConfigureAwait(false))
                {
                    if (!resp.IsSuccessStatusCode)
                    {
                        int code = (int)resp.StatusCode;
                        return RouteEstimator.RoutePolylineResult.Fail(
                            "Routing HTTP " + code + " (" + routeBaseUri + ")");
                    }

                    string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var json = JObject.Parse(body);
                    return ParseOsrmOk(json, waypoints);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return RouteEstimator.RoutePolylineResult.Fail(ex.Message);
            }
        }

        private static RouteEstimator.RoutePolylineResult ParseOsrmOk(JObject json, IList<GeoPoint> waypoints)
        {
            string osrmCode = (string)json["code"];
            if (!string.Equals(osrmCode, "Ok", StringComparison.Ordinal))
            {
                string osrmMsg = (string)json["message"];
                return RouteEstimator.RoutePolylineResult.Fail(
                    "OSRM: " + (osrmCode ?? "?") +
                    (string.IsNullOrEmpty(osrmMsg) ? "" : " — " + osrmMsg));
            }

            var routes = json["routes"] as JArray;
            if (routes == null || routes.Count == 0)
                return RouteEstimator.RoutePolylineResult.Fail("OSRM returned no routes.");

            var route = routes[0];
            double totalDuration = (double?)route["duration"] ?? 0d;
            double totalDistance = (double?)route["distance"] ?? 0d;

            var legs = route["legs"] as JArray;
            var legDurations = new List<double>();
            var legDistances = new List<double>();
            if (legs != null)
            {
                foreach (var leg in legs)
                {
                    legDurations.Add((double?)leg["duration"] ?? 0d);
                    legDistances.Add((double?)leg["distance"] ?? 0d);
                }
            }

            var coords = route["geometry"]?["coordinates"] as JArray;
            var polyline = new List<GeoPoint>(coords?.Count ?? 0);
            if (coords != null)
            {
                foreach (var pair in coords)
                {
                    if (pair is JArray pa && pa.Count >= 2)
                    {
                        double lng = (double)pa[0];
                        double lat = (double)pa[1];
                        polyline.Add(new GeoPoint(lat, lng));
                    }
                }
            }
            if (polyline.Count == 0 && waypoints != null)
                polyline.AddRange(waypoints);

            return RouteEstimator.RoutePolylineResult.Success(
                legDurations, legDistances, polyline, totalDuration, totalDistance);
        }

        private static RouteEstimator.RoutePolylineResult BuildStraightLineLastResort(
            IList<GeoPoint> waypoints, string osrmError)
        {
            return RouteEstimator.BuildStraightLineFallback(waypoints,
                (osrmError ?? "OSRM failed") +
                " — straight-line used only after local + public OSRM retries.");
        }

        private static int EstimateUrlLength(string coordPath)
        {
            return OsrmSettings.LocalBaseUrl.Length + coordPath.Length + RouteQuery.Length + 8;
        }

        private static bool TryBuildCoordinatePath(IList<GeoPoint> waypoints, out string path, out string error)
        {
            path = null;
            error = null;
            if (waypoints == null || waypoints.Count < 2)
            {
                error = "Not enough waypoints to route.";
                return false;
            }

            var sb = new System.Text.StringBuilder(waypoints.Count * 24);
            for (int i = 0; i < waypoints.Count; i++)
            {
                double lat = waypoints[i].Lat;
                double lng = waypoints[i].Lng;
                if (double.IsNaN(lat) || double.IsNaN(lng) || double.IsInfinity(lat) || double.IsInfinity(lng))
                {
                    error = "Invalid coordinates for routing (missing geocode?).";
                    return false;
                }
                if (i > 0) sb.Append(';');
                sb.Append(lng.ToString("F6", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(lat.ToString("F6", CultureInfo.InvariantCulture));
            }

            if (sb.Length == 0)
            {
                error = "No routable coordinates.";
                return false;
            }

            path = sb.ToString();
            return true;
        }
    }
}
