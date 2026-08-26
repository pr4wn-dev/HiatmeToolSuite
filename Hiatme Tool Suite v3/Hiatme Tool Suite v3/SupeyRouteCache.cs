using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// In-memory + disk cache of OSRM route results keyed by an ordered waypoint sequence.
    /// Disk file lives next to the geocode cache so Schedule Builder map tabs stay warm after LOAD.
    /// </summary>
    internal sealed class SupeyRouteCache
    {
        private const int PersistVersion = 1;
        private const int MaxPersistedEntries = 4000;

        private sealed class CachedRoute
        {
            public RouteEstimator.RoutePolylineResult Route;
            public long SavedAt;
        }

        private readonly Dictionary<string, CachedRoute> _cache =
            new Dictionary<string, CachedRoute>(StringComparer.Ordinal);
        private readonly Dictionary<string, Task<RouteEstimator.RoutePolylineResult>> _inflight =
            new Dictionary<string, Task<RouteEstimator.RoutePolylineResult>>(StringComparer.Ordinal);
        private readonly object _inflightLock = new object();
        private readonly SemaphoreSlim _gate;
        private readonly object _persistLock = new object();
        private bool _loaded;
        private int _dirtyEpoch;
        private int _savedEpoch;
        private long _clock;

        public SupeyRouteCache()
        {
            int n = OsrmSettings.MaxConcurrent;
            _gate = new SemaphoreSlim(n, n);
        }

        public async Task<RouteEstimator.RoutePolylineResult> GetAsync(IList<GeoPoint> waypoints, CancellationToken token)
        {
            EnsureLoaded();
            string key = BuildKey(waypoints);
            if (string.IsNullOrEmpty(key))
                return RouteEstimator.RoutePolylineResult.Fail("Not enough waypoints.");

            lock (_cache)
            {
                if (_cache.TryGetValue(key, out var cached) && cached?.Route != null)
                {
                    cached.SavedAt = NextTick();
                    return cached.Route;
                }
            }

            Task<RouteEstimator.RoutePolylineResult> fetch;
            lock (_inflightLock)
            {
                if (!_inflight.TryGetValue(key, out fetch))
                {
                    var copy = new List<GeoPoint>(waypoints);
                    fetch = FetchAndStoreAsync(key, copy);
                    _inflight[key] = fetch;
                }
            }

            // One OSRM call fills every waiter. A cancelled tab must not abort that fill,
            // but it also must not sit on the result — tab switches need to move on.
            token.ThrowIfCancellationRequested();
            if (token.CanBeCanceled)
            {
                var cancelTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using (token.Register(() => cancelTcs.TrySetResult(true)))
                {
                    var completed = await Task.WhenAny(fetch, cancelTcs.Task).ConfigureAwait(false);
                    if (completed != fetch)
                        token.ThrowIfCancellationRequested();
                }
            }
            return await fetch.ConfigureAwait(false);
        }

        public void Clear()
        {
            // Keep persisted routes. Wiping memory used to make the Schedule Builder map
            // re-hit OSRM for every tab after a Supey BUILD.
            EnsureLoaded();
        }

        public void Flush()
        {
            EnsureLoaded();
            Interlocked.Increment(ref _dirtyEpoch);
            TrySaveToDisk();
        }

        public int Count
        {
            get { lock (_cache) return _cache.Count; }
        }

        private async Task<RouteEstimator.RoutePolylineResult> FetchAndStoreAsync(
            string key, IList<GeoPoint> waypoints)
        {
            try
            {
                await _gate.WaitAsync().ConfigureAwait(false);
                try
                {
                    lock (_cache)
                    {
                        if (_cache.TryGetValue(key, out var cached) && cached?.Route != null)
                        {
                            cached.SavedAt = NextTick();
                            return cached.Route;
                        }
                    }

                    var result = await RouteEstimator.GetRouteWithGeometryAsync(
                        waypoints, CancellationToken.None).ConfigureAwait(false);
                    if (result.Ok && !result.IsStraightLineFallback)
                    {
                        lock (_cache)
                        {
                            _cache[key] = new CachedRoute
                            {
                                Route = result,
                                SavedAt = NextTick(),
                            };
                        }
                        SchedulePersist();
                    }
                    return result;
                }
                finally
                {
                    _gate.Release();
                }
            }
            finally
            {
                lock (_inflightLock)
                    _inflight.Remove(key);
            }
        }

        private long NextTick() => Interlocked.Increment(ref _clock);

        private void EnsureLoaded()
        {
            lock (_persistLock)
            {
                if (_loaded)
                    return;
                _loaded = true;
                TryLoadFromDisk();
            }
        }

        private void SchedulePersist()
        {
            int epoch = Interlocked.Increment(ref _dirtyEpoch);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Thread.Sleep(400);
                if (epoch != _dirtyEpoch)
                    return;
                TrySaveToDisk();
            });
        }

        private void TryLoadFromDisk()
        {
            string path = CacheFilePath();
            if (!File.Exists(path))
                return;

            try
            {
                string json = File.ReadAllText(path);
                var file = JsonConvert.DeserializeObject<PersistFile>(json);
                if (file == null || file.Version != PersistVersion || file.Entries == null)
                    return;

                long maxTick = 0;
                lock (_cache)
                {
                    foreach (var entry in file.Entries)
                    {
                        if (entry == null || string.IsNullOrEmpty(entry.Key) || entry.Polyline == null || entry.Polyline.Count < 2)
                            continue;
                        if (_cache.ContainsKey(entry.Key))
                            continue;

                        var poly = new List<GeoPoint>(entry.Polyline.Count);
                        foreach (var pt in entry.Polyline)
                            poly.Add(new GeoPoint(pt.Lat, pt.Lng));

                        long savedAt = entry.SavedAt > 0 ? entry.SavedAt : NextTick();
                        if (savedAt > maxTick)
                            maxTick = savedAt;

                        _cache[entry.Key] = new CachedRoute
                        {
                            Route = RouteEstimator.RoutePolylineResult.Success(
                                entry.LegSeconds ?? (IReadOnlyList<double>)Array.Empty<double>(),
                                entry.LegMeters ?? (IReadOnlyList<double>)Array.Empty<double>(),
                                poly,
                                entry.TotalSeconds,
                                entry.TotalMeters),
                            SavedAt = savedAt,
                        };
                    }
                }

                // Hits/inserts use NextTick(). If the clock restarts at 0 after load,
                // brand-new routes look older than disk entries and get evicted first.
                if (maxTick > Interlocked.Read(ref _clock))
                    Interlocked.Exchange(ref _clock, maxTick);
            }
            catch
            {
                // corrupt cache is ignored; next successful route rewrite it
            }
        }

        private void TrySaveToDisk()
        {
            lock (_persistLock)
            {
                int epoch = _dirtyEpoch;
                if (epoch == _savedEpoch)
                    return;

                List<PersistEntry> entries;
                lock (_cache)
                {
                    entries = _cache
                        .Where(kv => kv.Value?.Route != null
                            && kv.Value.Route.Ok
                            && kv.Value.Route.Polyline != null
                            && kv.Value.Route.Polyline.Count >= 2)
                        .OrderByDescending(kv => kv.Value.SavedAt)
                        .Take(MaxPersistedEntries)
                        .Select(kv =>
                        {
                            var route = kv.Value.Route;
                            var poly = new List<PersistPoint>(route.Polyline.Count);
                            foreach (var p in route.Polyline)
                                poly.Add(new PersistPoint { Lat = p.Lat, Lng = p.Lng });
                            return new PersistEntry
                            {
                                Key = kv.Key,
                                SavedAt = kv.Value.SavedAt,
                                TotalSeconds = route.TotalSeconds,
                                TotalMeters = route.TotalMeters,
                                LegSeconds = route.LegDurations,
                                LegMeters = route.LegDistances,
                                Polyline = poly,
                            };
                        })
                        .ToList();
                }

                try
                {
                    string path = CacheFilePath();
                    string dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir))
                        Directory.CreateDirectory(dir);
                    var file = new PersistFile { Version = PersistVersion, Entries = entries };
                    string tmp = path + ".tmp";
                    File.WriteAllText(tmp, JsonConvert.SerializeObject(file));
                    if (File.Exists(path))
                        File.Replace(tmp, path, null);
                    else
                        File.Move(tmp, path);
                    _savedEpoch = epoch;
                }
                catch
                {
                    // best effort — next dirty write retries
                }
            }
        }

        private static string CacheFilePath()
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(root, "HiatmeToolSuite", "osrm-route-cache.json");
        }

        private static string BuildKey(IList<GeoPoint> waypoints)
        {
            if (waypoints == null || waypoints.Count < 2) return "";
            var sb = new StringBuilder(waypoints.Count * 24);
            for (int i = 0; i < waypoints.Count; i++)
            {
                if (i > 0) sb.Append('|');
                sb.Append(waypoints[i].Lat.ToString("F5", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(waypoints[i].Lng.ToString("F5", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        private sealed class PersistFile
        {
            public int Version { get; set; }
            public List<PersistEntry> Entries { get; set; }
        }

        private sealed class PersistEntry
        {
            public string Key { get; set; }
            public long SavedAt { get; set; }
            public double TotalSeconds { get; set; }
            public double TotalMeters { get; set; }
            public IReadOnlyList<double> LegSeconds { get; set; }
            public IReadOnlyList<double> LegMeters { get; set; }
            public List<PersistPoint> Polyline { get; set; }
        }

        private sealed class PersistPoint
        {
            public double Lat { get; set; }
            public double Lng { get; set; }
        }
    }
}
