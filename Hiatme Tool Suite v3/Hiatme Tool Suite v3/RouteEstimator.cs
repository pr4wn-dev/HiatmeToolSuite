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
    /// Asks the public OSRM demo server (Open Source Routing Machine) for ideal driving times
    /// along a sequence of waypoints, in order. We use the <c>/route</c> endpoint (not
    /// <c>/table</c>) because dispatch's chain is sequential — driver → drop A → drop B → pick C
    /// — and <c>/route</c> hands back per-leg durations in one shot.
    /// </summary>
    /// <remarks>
    /// This hits <c>router.project-osrm.org</c>, which is documented as a demo server intended for
    /// experimentation. It's free and key-less, but if Hiatme ever generates real traffic we
    /// should self-host an OSRM instance — the API surface is identical, only the base URL
    /// changes. Travel times are "ideal" and do not include live traffic.
    /// </remarks>
    internal static class RouteEstimator
    {
        private const string BaseUri = "https://router.project-osrm.org/route/v1/driving/";

        /// <summary>
        /// Conservative URL-length cap. The OSRM demo server tends to reject extremely long URIs
        /// with HTTP 414, and proxies/CDNs can truncate even earlier. Catching it client-side
        /// gives the user an actionable message ("too many stops, try shorter horizon") instead
        /// of a cryptic "OSRM HTTP 414".
        /// </summary>
        private const int MaxUrlLength = 7500;

        // 25s is generous for a 50-stop chain on a slow OSRM day; longer than that and the user
        // is just staring at a frozen UI.
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };

        /// <summary>
        /// Outcome of a route request — either a list of cumulative durations (one per leg) or a
        /// human-readable explanation of why we couldn't compute one. The error message is meant
        /// to be shown directly to the user, so it includes status codes and actionable hints
        /// where we can derive them.
        /// </summary>
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

        /// <summary>
        /// Route result enriched with snap-to-street polyline geometry plus per-leg distances.
        /// Used by the Supey schedule builder to draw real road shapes on the map and to build a
        /// straight-line fallback when OSRM can't help.
        /// </summary>
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

        /// <summary>
        /// Asks OSRM for a single route through <paramref name="waypoints"/> in order and returns
        /// either the cumulative travel time (in seconds) from the first waypoint to each
        /// subsequent one, or a structured error explaining why we couldn't.
        /// </summary>
        public static async Task<RouteResult> GetCumulativeDurationsAsync(
            IList<GeoPoint> waypoints, CancellationToken token = default)
        {
            if (waypoints == null || waypoints.Count < 2)
                return RouteResult.Fail("Not enough waypoints to route.");

            // Build the OSRM-style "lng,lat;lng,lat;..." path. Invariant culture so a system set
            // to a comma-decimal locale doesn't produce "39,7,-95,0" instead of "39.7,-95.0".
            var sb = new StringBuilder(waypoints.Count * 24);
            for (int i = 0; i < waypoints.Count; i++)
            {
                if (i > 0) sb.Append(';');
                sb.Append(waypoints[i].Lng.ToString("F6", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(waypoints[i].Lat.ToString("F6", CultureInfo.InvariantCulture));
            }

            // overview=false skips polyline payload (we only want durations); alternatives=false
            // and steps=false keep the response tiny so it parses fast.
            string url = BaseUri + sb + "?overview=false&alternatives=false&steps=false";

            // Pre-check the URL length so we can give a chain-specific hint instead of letting
            // the server return a generic 414. The server's actual cap is shorter than the spec
            // suggests on busy days.
            if (url.Length > MaxUrlLength)
            {
                return RouteResult.Fail(
                    "Too many stops in the chain (" + waypoints.Count + " waypoints, " +
                    url.Length + " chars). Pick a shorter \"Show\" range to keep the chain manageable.");
            }

            try
            {
                using (var resp = await _http.GetAsync(url, token).ConfigureAwait(false))
                {
                    if (!resp.IsSuccessStatusCode)
                    {
                        int code = (int)resp.StatusCode;
                        string suggestion =
                            code == 414 ? " — chain too long for one URL; try a shorter \"Show\" range." :
                            code == 429 ? " — public OSRM throttled us; wait a moment and retry." :
                            code >= 500 ? " — OSRM demo server hiccup; try again." :
                            "";
                        return RouteResult.Fail(
                            "Routing service returned HTTP " + code + " " + (resp.ReasonPhrase ?? "") + suggestion);
                    }

                    string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var json = JObject.Parse(body);
                    string osrmCode = (string)json["code"];
                    if (!string.Equals(osrmCode, "Ok", StringComparison.Ordinal))
                    {
                        // OSRM uses a fixed vocabulary for the "code" field — NoRoute / NoSegment
                        // / InvalidQuery / etc — paired with a human-readable "message".
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
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // User-initiated cancel — let the caller decide what to do (we already reset UI).
                throw;
            }
            catch (OperationCanceledException)
            {
                // HttpClient.Timeout fires this without canceling the token. Rephrase as a
                // friendly timeout message rather than the framework's internal wording.
                return RouteResult.Fail("Routing service timed out (slow network or busy public server).");
            }
            catch (HttpRequestException ex)
            {
                return RouteResult.Fail("Routing service unreachable: " + (ex.Message ?? "(no detail)"));
            }
            catch (Exception ex)
            {
                return RouteResult.Fail("Routing service failed: " + ex.GetType().Name +
                                        (string.IsNullOrEmpty(ex.Message) ? "" : " — " + ex.Message));
            }
        }

        /// <summary>
        /// Like <see cref="GetCumulativeDurationsAsync"/> but also returns per-leg distances and
        /// the snap-to-street polyline (decoded from OSRM's GeoJSON). Used by the Supey schedule
        /// builder to draw real road shapes on the preview map and to compute total mileage.
        /// On routing failure the result is a straight-line fallback (waypoints become the
        /// polyline) so the map still renders, with <see cref="RoutePolylineResult.IsStraightLineFallback"/>
        /// flagged so the UI can shade those legs differently.
        /// </summary>
        public static async Task<RoutePolylineResult> GetRouteWithGeometryAsync(
            IList<GeoPoint> waypoints, CancellationToken token = default)
        {
            if (waypoints == null || waypoints.Count < 2)
                return RoutePolylineResult.Fail("Not enough waypoints to route.");

            var sb = new StringBuilder(waypoints.Count * 24);
            for (int i = 0; i < waypoints.Count; i++)
            {
                if (i > 0) sb.Append(';');
                sb.Append(waypoints[i].Lng.ToString("F6", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(waypoints[i].Lat.ToString("F6", CultureInfo.InvariantCulture));
            }

            // overview=full + geometries=geojson gives us the polyline as [[lng,lat],...]; steps=false
            // keeps the response compact (we don't render turn-by-turn).
            string url = BaseUri + sb + "?overview=full&geometries=geojson&alternatives=false&steps=false&annotations=distance,duration";

            if (url.Length > MaxUrlLength)
            {
                return BuildStraightLineFallback(waypoints,
                    "Too many stops in the chain (" + waypoints.Count + " waypoints, " +
                    url.Length + " chars). Built a straight-line fallback for this leg.");
            }

            try
            {
                using (var resp = await _http.GetAsync(url, token).ConfigureAwait(false))
                {
                    if (!resp.IsSuccessStatusCode)
                    {
                        int code = (int)resp.StatusCode;
                        string suggestion =
                            code == 414 ? " (chain too long — straight-line fallback used)" :
                            code == 429 ? " (OSRM throttled — straight-line fallback used)" :
                            code >= 500 ? " (OSRM hiccup — straight-line fallback used)" :
                            "";
                        return BuildStraightLineFallback(waypoints,
                            "Routing service returned HTTP " + code + " " + (resp.ReasonPhrase ?? "") + suggestion);
                    }

                    string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var json = JObject.Parse(body);
                    string osrmCode = (string)json["code"];
                    if (!string.Equals(osrmCode, "Ok", StringComparison.Ordinal))
                    {
                        string osrmMsg = (string)json["message"];
                        return BuildStraightLineFallback(waypoints,
                            "Routing service: " + (osrmCode ?? "(no code)") +
                            (string.IsNullOrEmpty(osrmMsg) ? "" : " — " + osrmMsg));
                    }

                    var routes = json["routes"] as JArray;
                    if (routes == null || routes.Count == 0)
                        return BuildStraightLineFallback(waypoints, "Routing service returned no routes.");

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
                            // GeoJSON: [lng, lat]
                            if (pair is JArray pa && pa.Count >= 2)
                            {
                                double lng = (double)pa[0];
                                double lat = (double)pa[1];
                                polyline.Add(new GeoPoint(lat, lng));
                            }
                        }
                    }
                    if (polyline.Count == 0)
                    {
                        // No geometry came back even though OSRM said "Ok" — degrade to waypoints
                        // so the caller still has *some* shape to draw.
                        polyline.AddRange(waypoints);
                    }

                    return RoutePolylineResult.Success(legDurations, legDistances, polyline,
                        totalDuration, totalDistance);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                return BuildStraightLineFallback(waypoints,
                    "Routing service timed out (slow network or busy public server).");
            }
            catch (HttpRequestException ex)
            {
                return BuildStraightLineFallback(waypoints,
                    "Routing service unreachable: " + (ex.Message ?? "(no detail)"));
            }
            catch (Exception ex)
            {
                return BuildStraightLineFallback(waypoints,
                    "Routing service failed: " + ex.GetType().Name +
                    (string.IsNullOrEmpty(ex.Message) ? "" : " — " + ex.Message));
            }
        }

        /// <summary>
        /// Builds a straight-line fallback "polyline" from the original waypoints plus an
        /// approximate distance/duration so the schedule keeps rendering even when OSRM is
        /// unreachable. Distance comes from haversine; duration is haversine-distance / 35 mph.
        /// </summary>
        private static RoutePolylineResult BuildStraightLineFallback(IList<GeoPoint> waypoints, string osrmError)
        {
            var distances = new List<double>(waypoints.Count - 1);
            double totalMeters = 0;
            for (int i = 1; i < waypoints.Count; i++)
            {
                double m = HaversineMeters(waypoints[i - 1], waypoints[i]);
                distances.Add(m);
                totalMeters += m;
            }
            // ~35 mph average — close enough for "we couldn't compute it but the user needs *something*".
            double seconds = totalMeters / 15.6464;
            return RoutePolylineResult.StraightLine(new List<GeoPoint>(waypoints), distances, seconds, totalMeters, osrmError);
        }

        private static double HaversineMeters(GeoPoint a, GeoPoint b)
        {
            const double R = 6371000.0; // earth radius in meters
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
