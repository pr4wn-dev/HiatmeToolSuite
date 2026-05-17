using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// In-memory cache of OSRM route results keyed by an ordered waypoint sequence. The same dead
    /// head from "home → group 1 PU" is asked for once during scoring, again during sequencing,
    /// and again when the user clicks Rebuild — caching avoids hammering the public OSRM demo
    /// server (and the rate limit it enforces).
    /// </summary>
    /// <remarks>
    /// Cache lives for the duration of a single build / preview session. It is cleared at the
    /// start of every Build/Rebuild because OSRM road conditions never change, but the geocoded
    /// driver homes might (the user could edit a roster between builds).
    /// </remarks>
    internal sealed class SupeyRouteCache
    {
        private readonly Dictionary<string, RouteEstimator.RoutePolylineResult> _cache =
            new Dictionary<string, RouteEstimator.RoutePolylineResult>(StringComparer.Ordinal);
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

        /// <summary>
        /// Asks for the snap-to-street polyline + duration + distance for <paramref name="waypoints"/>;
        /// returns the cached result if one exists, otherwise calls OSRM and caches the result
        /// (success or failure) so a busted address pair doesn't get re-hit on every retry.
        /// </summary>
        public async Task<RouteEstimator.RoutePolylineResult> GetAsync(IList<GeoPoint> waypoints, CancellationToken token)
        {
            string key = BuildKey(waypoints);
            if (string.IsNullOrEmpty(key))
                return RouteEstimator.RoutePolylineResult.Fail("Not enough waypoints.");

            lock (_cache)
            {
                if (_cache.TryGetValue(key, out var cached))
                    return cached;
            }

            // Single-flight: only one OSRM call in flight at a time, both for politeness to the
            // public demo server and to avoid two concurrent identical calls cache-stampeding.
            await _gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                lock (_cache)
                {
                    if (_cache.TryGetValue(key, out var cached2))
                        return cached2;
                }

                var result = await RouteEstimator.GetRouteWithGeometryAsync(waypoints, token).ConfigureAwait(false);
                lock (_cache) { _cache[key] = result; }
                return result;
            }
            finally
            {
                _gate.Release();
            }
        }

        public void Clear()
        {
            lock (_cache) { _cache.Clear(); }
        }

        public int Count
        {
            get { lock (_cache) return _cache.Count; }
        }

        private static string BuildKey(IList<GeoPoint> waypoints)
        {
            if (waypoints == null || waypoints.Count < 2) return "";
            var sb = new System.Text.StringBuilder(waypoints.Count * 24);
            for (int i = 0; i < waypoints.Count; i++)
            {
                if (i > 0) sb.Append('|');
                sb.Append(waypoints[i].Lat.ToString("F5", System.Globalization.CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(waypoints[i].Lng.ToString("F5", System.Globalization.CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }
    }
}
