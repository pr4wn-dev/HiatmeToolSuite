using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// In-memory cache of OSRM route results keyed by an ordered waypoint sequence.
    /// </summary>
    internal sealed class SupeyRouteCache
    {
        private readonly Dictionary<string, RouteEstimator.RoutePolylineResult> _cache =
            new Dictionary<string, RouteEstimator.RoutePolylineResult>(StringComparer.Ordinal);
        private readonly SemaphoreSlim _gate;

        public SupeyRouteCache()
        {
            int n = OsrmSettings.MaxConcurrent;
            _gate = new SemaphoreSlim(n, n);
        }

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
