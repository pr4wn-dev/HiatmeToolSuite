using System;

using System.Collections.Generic;

using System.Globalization;

using System.Net.Http;

using System.Text;

using System.Threading;

using System.Threading.Tasks;

using Newtonsoft.Json.Linq;



namespace Hiatme_Tool_Suite_v3

{

    /// <summary>

    /// OSRM driving routes (local Docker by default; optional public demo fallback).

    /// Uses <c>/route</c> for sequential chains (driver → stops → …).

    /// </summary>

    internal static class RouteEstimator

    {

        private static volatile string _preferredBaseUri = OsrmSettings.CurrentRouteBaseUri;



        private const int MaxUrlLength = 7500;



        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };



        public sealed class RouteResult

        {

            public IReadOnlyList<double> Durations { get; }

            public string ErrorMessage { get; }

            public bool Ok => Durations != null;



            private RouteResult(IReadOnlyList<double> durations, string error)

            {

                Durations = durations;

                ErrorMessage = error;

            }



            public static RouteResult Success(IReadOnlyList<double> d) => new RouteResult(d, null);

            public static RouteResult Fail(string msg) => new RouteResult(null, msg);

        }



        public sealed class RoutePolylineResult

        {

            public IReadOnlyList<double> LegDurations { get; }

            public IReadOnlyList<double> LegDistances { get; }

            public IReadOnlyList<GeoPoint> Polyline { get; }

            public double TotalSeconds { get; }

            public double TotalMeters { get; }

            public bool IsStraightLineFallback { get; }

            public string ErrorMessage { get; }

            public bool Ok => Polyline != null;



            private RoutePolylineResult(IReadOnlyList<double> legDurations, IReadOnlyList<double> legDistances,

                IReadOnlyList<GeoPoint> polyline, double totalSeconds, double totalMeters,

                bool fallback, string error)

            {

                LegDurations = legDurations;

                LegDistances = legDistances;

                Polyline = polyline;

                TotalSeconds = totalSeconds;

                TotalMeters = totalMeters;

                IsStraightLineFallback = fallback;

                ErrorMessage = error;

            }



            public static RoutePolylineResult Success(IReadOnlyList<double> durs, IReadOnlyList<double> dists,

                IReadOnlyList<GeoPoint> polyline, double totalSeconds, double totalMeters) =>

                new RoutePolylineResult(durs, dists, polyline, totalSeconds, totalMeters, false, null);



            public static RoutePolylineResult StraightLine(IReadOnlyList<GeoPoint> waypoints,

                IReadOnlyList<double> dists, double totalSeconds, double totalMeters, string osrmError) =>

                new RoutePolylineResult(null, dists, waypoints, totalSeconds, totalMeters, true, osrmError);



            public static RoutePolylineResult Fail(string msg) =>

                new RoutePolylineResult(null, null, null, 0, 0, false, msg);

        }



        public static async Task<RouteResult> GetCumulativeDurationsAsync(

            IList<GeoPoint> waypoints, CancellationToken token = default)

        {

            if (waypoints == null || waypoints.Count < 2)

                return RouteResult.Fail("Not enough waypoints to route.");

            if (HiatmeGeoSettings.UseServer)
            {
                try
                {
                    var via = await HiatmeGeoClient.GetCumulativeDurationsAsync(
                        HiatmeAiSettings.Load(), waypoints, token).ConfigureAwait(false);
                    if (via != null && via.Ok) return via;
                }
                catch { /* try local below */ }
            }

            var poly = await OsrmRouteResolver.RouteBestEffortAsync(waypoints, token).ConfigureAwait(false);
            if (poly.Ok && !poly.IsStraightLineFallback && poly.LegDurations != null)
            {
                var cumulative = new List<double>(poly.LegDurations.Count);
                double running = 0;
                foreach (double leg in poly.LegDurations)
                {
                    running += leg;
                    cumulative.Add(running);
                }
                return RouteResult.Success(cumulative);
            }

            string coordPath;
            string coordError;
            if (!TryBuildCoordinatePath(waypoints, out coordPath, out coordError))
                return RouteResult.Fail(coordError);

            string requestUrl = OsrmSettings.BuildRouteRequestUrl(
                coordPath, "overview=false&alternatives=false&steps=false");

            int totalLen = requestUrl.Length;

            if (totalLen > MaxUrlLength)

            {

                return RouteResult.Fail(

                    "Too many stops in the chain (" + waypoints.Count + " waypoints, " +

                    totalLen + " chars). Pick a shorter \"Show\" range to keep the chain manageable.");

            }



            try

            {

                using (var resp = await FetchOsrmAsync(requestUrl, token).ConfigureAwait(false))
                    return await ParseDurationResponseAsync(resp, waypoints.Count).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                return RouteResult.Fail("Routing service timed out.");
            }
            catch (HttpRequestException ex)
            {
                return RouteResult.Fail(UnreachableMessage(ex));
            }
            catch (Exception ex)
            {
                return RouteResult.Fail("Routing service failed: " + ex.GetType().Name +
                                        (string.IsNullOrEmpty(ex.Message) ? "" : " — " + ex.Message));
            }
        }

        public static async Task<RoutePolylineResult> GetRouteWithGeometryAsync(
            IList<GeoPoint> waypoints, CancellationToken token = default)
        {
            if (waypoints == null || waypoints.Count < 2)
                return RoutePolylineResult.Fail("Not enough waypoints to route.");

            if (HiatmeGeoSettings.UseServer)
            {
                try
                {
                    var osrm = await HiatmeGeoClient.FetchOsrmJsonAsync(
                        HiatmeAiSettings.Load(), waypoints, geometry: true, token).ConfigureAwait(false);
                    if (osrm != null)
                    {
                        var parsed = TryParseOsrmPolyline(osrm, waypoints);
                        if (parsed != null && parsed.Ok && !parsed.IsStraightLineFallback)
                            return parsed;
                    }
                }
                catch { /* try local below */ }
            }

            return await OsrmRouteResolver.RouteBestEffortAsync(waypoints, token).ConfigureAwait(false);
        }



        private static async Task<HttpResponseMessage> FetchOsrmAsync(string requestUrl, CancellationToken token)

        {

            string primary = requestUrl;

            _preferredBaseUri = OsrmSettings.CurrentRouteBaseUri;



            try

            {

                return await _http.GetAsync(primary, token).ConfigureAwait(false);

            }

            catch (HttpRequestException) when (TryGetHttpFallback(primary, out string httpFallback))

            {

                return await _http.GetAsync(httpFallback, token).ConfigureAwait(false);

            }

            catch (OperationCanceledException) when (!token.IsCancellationRequested

                                                     && TryGetHttpFallback(primary, out string httpFallback2))

            {

                return await _http.GetAsync(httpFallback2, token).ConfigureAwait(false);

            }

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



            var sb = new StringBuilder(waypoints.Count * 24);

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



        private static async Task<RouteResult> ParseDurationResponseAsync(HttpResponseMessage resp, int waypointCount)

        {

            if (!resp.IsSuccessStatusCode)

            {

                int code = (int)resp.StatusCode;

                return RouteResult.Fail(

                    "Routing service returned HTTP " + code + " " + (resp.ReasonPhrase ?? "") +

                    HttpErrorSuggestion(code));

            }



            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

            var json = JObject.Parse(body);

            string osrmCode = (string)json["code"];

            if (!string.Equals(osrmCode, "Ok", StringComparison.Ordinal))

            {

                string osrmMsg = (string)json["message"];

                return RouteResult.Fail(

                    "Routing service: " + (osrmCode ?? "(no code)") +

                    (string.IsNullOrEmpty(osrmMsg) ? "" : " — " + osrmMsg));

            }



            var routes = json["routes"] as JArray;

            if (routes == null || routes.Count == 0)

                return RouteResult.Fail("Routing service returned no routes.");

            var legs = routes[0]["legs"] as JArray;

            if (legs == null)

                return RouteResult.Fail("Routing service response had no legs.");



            var cumulative = new List<double>(legs.Count);

            double running = 0;

            foreach (var leg in legs)

            {

                var dur = leg["duration"];

                if (dur == null) return RouteResult.Fail("Routing leg missing duration.");

                running += (double)dur;

                cumulative.Add(running);

            }

            return RouteResult.Success(cumulative);

        }



        private static bool TryGetHttpFallback(string requestUrl, out string httpFallback)

        {

            httpFallback = null;

            if (string.IsNullOrEmpty(requestUrl)

                || OsrmSettings.IsLocalEndpoint(requestUrl))

                return false;



            if (requestUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))

            {

                httpFallback = "http://" + requestUrl.Substring("https://".Length);

                return true;

            }

            return false;

        }



        private static string HttpErrorSuggestion(int code, bool forFallback = false)

        {

            string suffix = forFallback ? " — straight-line fallback used" : "";

            if (code == 414)

                return " — chain too long for one URL; try a shorter range." + suffix;

            if (code == 429)

                return " — routing service throttled; use local OSRM or retry." + suffix;

            if (code >= 500)

                return " — routing server error; try again." + suffix;

            return suffix;

        }



        private static string UnreachableMessage(HttpRequestException ex)

        {

            string detail = ex.Message ?? "(no detail)";

            if (OsrmSettings.IsLocalEndpoint(OsrmSettings.LocalBaseUrl)

                && string.Equals(OsrmSettings.CurrentRouteBaseUri, OsrmSettings.LocalBaseUrl,

                    StringComparison.OrdinalIgnoreCase))

            {

                return "Local OSRM unreachable: " + detail + ". " + OsrmSettings.LocalOfflineHint;

            }

            return "Routing service unreachable: " + detail;

        }



        private static RoutePolylineResult TryParseOsrmPolyline(JObject json, IList<GeoPoint> waypoints)
        {
            if (json == null || waypoints == null) return null;
            string osrmCode = (string)json["code"];
            if (!string.Equals(osrmCode, "Ok", StringComparison.Ordinal))
            {
                string osrmMsg = (string)json["message"];
                return RoutePolylineResult.Fail(
                    "Routing service: " + (osrmCode ?? "(no code)") +
                    (string.IsNullOrEmpty(osrmMsg) ? "" : " — " + osrmMsg));
            }
            var routes = json["routes"] as JArray;
            if (routes == null || routes.Count == 0)
                return RoutePolylineResult.Fail("Routing service returned no routes.");
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
            if (polyline.Count == 0)
                polyline.AddRange(waypoints);
            return RoutePolylineResult.Success(legDurations, legDistances, polyline, totalDuration, totalDistance);
        }

        internal static RoutePolylineResult BuildStraightLineFallback(IList<GeoPoint> waypoints, string osrmError)

        {

            var distances = new List<double>(waypoints.Count - 1);

            double totalMeters = 0;

            for (int i = 1; i < waypoints.Count; i++)

            {

                double m = HaversineMeters(waypoints[i - 1], waypoints[i]);

                distances.Add(m);

                totalMeters += m;

            }

            double seconds = totalMeters / 15.6464;

            return RoutePolylineResult.StraightLine(new List<GeoPoint>(waypoints), distances, seconds, totalMeters, osrmError);

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


