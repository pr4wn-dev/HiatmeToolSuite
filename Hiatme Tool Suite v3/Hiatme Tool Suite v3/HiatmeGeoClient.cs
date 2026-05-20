using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Geocode + OSRM via AIagent (<c>/api/hiatme/geo/*</c>) on the server host.</summary>
    internal static class HiatmeGeoClient
    {
        private static readonly HttpClient SharedHttp = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(60),
        };

        public sealed class GeoStatus
        {
            public bool OsrmLocalOk { get; set; }
            public string OsrmActiveEndpoint { get; set; }
            public int GeocodeCacheEntries { get; set; }
        }

        public static async Task<GeoStatus> GetStatusAsync(
            HiatmeAiSettings settings,
            CancellationToken cancellationToken = default)
        {
            if (settings == null) return null;
            string url = Base(settings) + "/api/hiatme/geo/status";
            try
            {
                using (var req = BuildGet(settings, url))
                using (var resp = await SharedHttp.SendAsync(req, cancellationToken).ConfigureAwait(false))
                {
                    if (!resp.IsSuccessStatusCode) return null;
                    var json = JObject.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
                    return new GeoStatus
                    {
                        OsrmLocalOk = json["osrm_local_ok"]?.Value<bool>() == true,
                        OsrmActiveEndpoint = json["osrm_active_endpoint"]?.ToString() ?? "",
                        GeocodeCacheEntries = json["geocode_cache_entries"]?.Value<int>() ?? 0,
                    };
                }
            }
            catch
            {
                return null;
            }
        }

        public static async Task ConfirmGeocodeAsync(
            HiatmeAiSettings settings,
            string street,
            string city,
            string state,
            string zip,
            GeoPoint point,
            string dispatcherName = null,
            CancellationToken cancellationToken = default)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            string url = Base(settings) + "/api/hiatme/geocode/confirm";
            var body = new JObject
            {
                ["street"] = street ?? "",
                ["city"] = city ?? "",
                ["state"] = string.IsNullOrWhiteSpace(state) ? "ME" : state,
                ["zip"] = zip ?? "",
                ["country_code"] = "us",
                ["lat"] = point.Lat,
                ["lon"] = point.Lng,
            };
            if (!string.IsNullOrWhiteSpace(dispatcherName))
                body["dispatcher_display_name"] = dispatcherName.Trim();
            using (var req = BuildPost(settings, url, body))
            using (var resp = await SharedHttp.SendAsync(req, cancellationToken).ConfigureAwait(false))
            {
                var txt = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    throw new InvalidOperationException("Geocode confirm failed: " + txt);
            }
        }

        public static async Task<GeoPoint?> ResolveAsync(
            HiatmeAiSettings settings,
            string street,
            string city,
            string state,
            string zip,
            string countryCode,
            CancellationToken cancellationToken = default)
        {
            if (settings == null) return null;
            string url = Base(settings) + "/api/hiatme/geocode";
            var body = new JObject
            {
                ["queries"] = new JArray
                {
                    new JObject
                    {
                        ["street"] = street ?? "",
                        ["city"] = city ?? "",
                        ["state"] = state ?? "",
                        ["zip"] = zip ?? "",
                        ["country_code"] = string.IsNullOrWhiteSpace(countryCode) ? "us" : countryCode,
                    },
                },
            };
            try
            {
                using (var req = BuildPost(settings, url, body))
                using (var resp = await SharedHttp.SendAsync(req, cancellationToken).ConfigureAwait(false))
                {
                    if (!resp.IsSuccessStatusCode) return null;
                    var json = JObject.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
                    var results = json["results"] as JArray;
                    if (results == null || results.Count == 0) return null;
                    var row = results[0] as JObject;
                    if (row?["ok"]?.Value<bool>() != true) return null;
                    double lat = row["lat"]?.Value<double>() ?? 0;
                    double lon = row["lon"]?.Value<double>() ?? 0;
                    return new GeoPoint(lat, lon);
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Null if the AIagent host is unreachable; otherwise parsed OSRM result.</summary>
        public static async Task<RouteEstimator.RouteResult> GetCumulativeDurationsAsync(
            HiatmeAiSettings settings,
            IList<GeoPoint> waypoints,
            CancellationToken cancellationToken = default)
        {
            var osrm = await FetchOsrmJsonAsync(settings, waypoints, geometry: false, cancellationToken)
                .ConfigureAwait(false);
            if (osrm == null) return null;
            return ParseDurationOsrm(osrm, waypoints?.Count ?? 0);
        }

        public static async Task<JObject> FetchOsrmJsonAsync(
            HiatmeAiSettings settings,
            IList<GeoPoint> waypoints,
            bool geometry,
            CancellationToken cancellationToken = default)
        {
            if (settings == null || waypoints == null || waypoints.Count < 2) return null;
            string url = Base(settings) + "/api/hiatme/route";
            var wps = new JArray();
            foreach (var p in waypoints)
                wps.Add(new JObject { ["lat"] = p.Lat, ["lon"] = p.Lng });
            var body = new JObject { ["waypoints"] = wps, ["geometry"] = geometry };
            try
            {
                using (var req = BuildPost(settings, url, body))
                using (var resp = await SharedHttp.SendAsync(req, cancellationToken).ConfigureAwait(false))
                {
                    if (!resp.IsSuccessStatusCode) return null;
                    var json = JObject.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
                    if (json["ok"]?.Value<bool>() != true) return null;
                    return json["osrm"] as JObject;
                }
            }
            catch
            {
                return null;
            }
        }

        private static RouteEstimator.RouteResult ParseDurationOsrm(JObject json, int waypointCount)
        {
            if (json == null) return RouteEstimator.RouteResult.Fail("Empty routing response.");
            string osrmCode = (string)json["code"];
            if (!string.Equals(osrmCode, "Ok", StringComparison.Ordinal))
            {
                string osrmMsg = (string)json["message"];
                return RouteEstimator.RouteResult.Fail(
                    "Routing service: " + (osrmCode ?? "(no code)") +
                    (string.IsNullOrEmpty(osrmMsg) ? "" : " — " + osrmMsg));
            }
            var routes = json["routes"] as JArray;
            if (routes == null || routes.Count == 0)
                return RouteEstimator.RouteResult.Fail("Routing service returned no routes.");
            var legs = routes[0]["legs"] as JArray;
            if (legs == null)
                return RouteEstimator.RouteResult.Fail("Routing service response had no legs.");
            var cumulative = new List<double>(legs.Count);
            double running = 0;
            foreach (var leg in legs)
            {
                var dur = leg["duration"];
                if (dur == null) return RouteEstimator.RouteResult.Fail("Routing leg missing duration.");
                running += (double)dur;
                cumulative.Add(running);
            }
            return RouteEstimator.RouteResult.Success(cumulative);
        }

        private static string Base(HiatmeAiSettings settings) =>
            (settings.BaseUrl ?? "").Trim().TrimEnd('/');

        private static HttpRequestMessage BuildGet(HiatmeAiSettings settings, string url)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiToken.Trim());
            return req;
        }

        private static HttpRequestMessage BuildPost(HiatmeAiSettings settings, string url, JObject body)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(body.ToString(Formatting.None), System.Text.Encoding.UTF8, "application/json"),
            };
            if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiToken.Trim());
            return req;
        }
    }
}
