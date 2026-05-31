using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Direct OSRM table when not using office panel (dev / local OSRM).</summary>
    internal static class OsrmTableLocal
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        internal static async Task<(double?[,] meters, double?[,] seconds)?> FetchAsync(
            IList<GeoPoint> points, CancellationToken token)
        {
            if (points == null || points.Count < 2) return null;

            var parts = new List<string>(points.Count);
            foreach (var p in points)
                parts.Add(p.Lng.ToString("F6", CultureInfo.InvariantCulture) + ","
                    + p.Lat.ToString("F6", CultureInfo.InvariantCulture));
            string coordPath = string.Join(";", parts);

            string baseUrl = TableBaseFromRoute(OsrmSettings.CurrentRouteBaseUri);
            string url = baseUrl + coordPath + "?annotations=distance,duration";

            try
            {
                using (var resp = await Http.GetAsync(url, token).ConfigureAwait(false))
                {
                    if (!resp.IsSuccessStatusCode) return null;
                    var json = JObject.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
                    if (!string.Equals((string)json["code"], "Ok", StringComparison.OrdinalIgnoreCase))
                        return null;
                    return ParseMatrix(json, points.Count);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return null;
            }
        }

        private static string TableBaseFromRoute(string routeBase)
        {
            string baseUri = (routeBase ?? "").Trim().TrimEnd('/');
            string low = baseUri.ToLowerInvariant();
            if (low.Contains("/route/v1/"))
            {
                int idx = low.IndexOf("/route/v1/", StringComparison.Ordinal);
                return baseUri.Substring(0, idx) + "/table/v1/driving/";
            }
            if (low.Contains("/table/v1/"))
                return baseUri.EndsWith("/") ? baseUri : baseUri + "/";
            return baseUri + "/table/v1/driving/";
        }

        private static (double?[,] meters, double?[,] seconds)? ParseMatrix(JObject json, int n)
        {
            var distRaw = json["distances"] as JArray;
            var durRaw = json["durations"] as JArray;
            if (distRaw == null || durRaw == null) return null;

            var meters = new double?[n, n];
            var seconds = new double?[n, n];
            for (int i = 0; i < n && i < distRaw.Count; i++)
            {
                var rowD = distRaw[i] as JArray;
                var rowT = durRaw[i] as JArray;
                if (rowD == null || rowT == null) continue;
                for (int j = 0; j < n && j < rowD.Count && j < rowT.Count; j++)
                {
                    meters[i, j] = PositiveOrNull(rowD[j]);
                    seconds[i, j] = PositiveOrNull(rowT[j]);
                }
            }
            return (meters, seconds);
        }

        private static double? PositiveOrNull(JToken tok)
        {
            if (tok == null || tok.Type == JTokenType.Null) return null;
            try
            {
                double v = (double)tok;
                return v >= 0 ? v : (double?)null;
            }
            catch
            {
                return null;
            }
        }
    }
}
