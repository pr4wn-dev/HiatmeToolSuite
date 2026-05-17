using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Thin wrapper around the public Nominatim (OpenStreetMap) geocoding endpoint, used to turn
    /// a free-form trip address ("123 Main St", "Dayton") into a <see cref="GeoPoint"/> we can
    /// hand to the routing engine. Nominatim's usage policy is strict — 1 request/sec max and a
    /// unique User-Agent — so this class enforces both with a process-wide semaphore plus a
    /// session-level cache that also memoizes negative lookups (so a malformed address never
    /// gets hit again during the same run).
    /// </summary>
    /// <remarks>
    /// The cache is persisted to <c>%AppData%/HiatmeToolSuite/geocode-cache.json</c> so re-runs
    /// (and re-installs) skip the 1 req/sec Nominatim throttle for any address we've already
    /// resolved. Negative lookups (Nominatim returned nothing) are cached too so a malformed
    /// address never gets re-hammered. Address coordinates are stable — the cache effectively
    /// never goes stale unless a venue physically moves.
    /// </remarks>
    internal static class AddressGeocoder
    {
        private const string BaseUri = "https://nominatim.openstreetmap.org/search";
        private const string UserAgent = "HiatmeToolSuite/3.0 (+https://hiatme.app; ops@hiatme.app)";
        private const int CacheVersion = 1;

        // Nominatim Public Use Policy: max 1 absolute req/sec. Pad slightly to be a good citizen
        // even if the system clock jitters by a few ms between calls.
        private static readonly TimeSpan MinSpacing = TimeSpan.FromMilliseconds(1100);

        private static readonly HttpClient _http = CreateHttp();
        private static readonly Dictionary<string, GeoPoint?> _cache =
            new Dictionary<string, GeoPoint?>(StringComparer.OrdinalIgnoreCase);
        private static readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private static DateTime _nextCallAfterUtc = DateTime.MinValue;

        // Persistence machinery. _diskLoaded gates one-time load on first ResolveAsync call;
        // _saveLock serializes disk writes; _pendingSave coalesces concurrent saves so we don't
        // line up dozens of identical writes when a build geocodes hundreds of addresses back to
        // back. _disposed lets static cleanup short-circuit if the AppDomain is unloading.
        private static bool _diskLoaded;
        private static readonly object _saveLock = new object();
        private static Task _pendingSave;
        private static int _dirtyCount;
        private const int SaveBatchSize = 5; // flush every N new entries (or on Flush())

        private static HttpClient CreateHttp()
        {
            var h = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            h.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            h.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US");
            return h;
        }

        /// <summary>
        /// Path to the on-disk cache file. Lives under <c>%AppData%/HiatmeToolSuite/</c> so it
        /// survives uninstall/reinstall but is per-user (different operators don't share each
        /// other's misfires).
        /// </summary>
        private static string CacheFilePath
        {
            get
            {
                string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string folder = Path.Combine(root, "HiatmeToolSuite");
                Directory.CreateDirectory(folder);
                return Path.Combine(folder, "geocode-cache.json");
            }
        }

        /// <summary>Number of entries currently in the in-memory cache. Useful for diagnostics.</summary>
        public static int CacheCount
        {
            get { lock (_cache) return _cache.Count; }
        }

        /// <summary>
        /// Lazy-loads the persisted cache the first time a resolve happens. Idempotent — repeat
        /// calls after a successful load are no-ops. A corrupt file is logged-and-skipped so the
        /// app continues with an empty cache rather than crashing.
        /// </summary>
        private static void EnsureDiskCacheLoaded()
        {
            if (_diskLoaded) return;
            lock (_saveLock)
            {
                if (_diskLoaded) return;
                _diskLoaded = true; // set first so a load failure doesn't loop on re-entry
                try
                {
                    string path = CacheFilePath;
                    if (!File.Exists(path)) return;
                    string json = File.ReadAllText(path);
                    if (string.IsNullOrWhiteSpace(json)) return;
                    var doc = JObject.Parse(json);
                    var entries = doc["entries"] as JObject;
                    if (entries == null) return;
                    lock (_cache)
                    {
                        foreach (var prop in entries.Properties())
                        {
                            if (_cache.ContainsKey(prop.Name)) continue;
                            var val = prop.Value;
                            if (val == null || val.Type == JTokenType.Null)
                            {
                                _cache[prop.Name] = null; // remembered negative lookup
                                continue;
                            }
                            // entries are stored as [lat, lng] arrays for compactness
                            var arr = val as JArray;
                            if (arr != null && arr.Count == 2)
                            {
                                double lat = (double)arr[0];
                                double lng = (double)arr[1];
                                _cache[prop.Name] = new GeoPoint(lat, lng);
                            }
                        }
                    }
                }
                catch
                {
                    // Corrupt or unreadable cache file → start fresh. The next save overwrites it.
                }
            }
        }

        /// <summary>
        /// Schedules a background flush to disk. Multiple rapid mutations coalesce into a single
        /// save (only one save task is in flight at a time); after the in-flight save completes
        /// any newly accumulated dirty entries trigger another save. Caller never waits.
        /// </summary>
        private static void ScheduleSave()
        {
            // We don't strictly need this every time — but doing it batched (every N) avoids
            // hundreds of disk hits during a build. Final state is always flushed by Flush().
            if (Interlocked.Increment(ref _dirtyCount) < SaveBatchSize)
            {
                // Still kick off a single tail-save so the very last entry doesn't sit unsaved.
                StartSaveIfIdle();
                return;
            }
            Interlocked.Exchange(ref _dirtyCount, 0);
            StartSaveIfIdle();
        }

        private static void StartSaveIfIdle()
        {
            lock (_saveLock)
            {
                if (_pendingSave != null && !_pendingSave.IsCompleted) return;
                _pendingSave = Task.Run((Action)WriteCacheToDisk);
            }
        }

        private static void WriteCacheToDisk()
        {
            try
            {
                Dictionary<string, GeoPoint?> snapshot;
                lock (_cache) snapshot = new Dictionary<string, GeoPoint?>(_cache, StringComparer.OrdinalIgnoreCase);

                var entries = new JObject();
                foreach (var kv in snapshot)
                {
                    if (kv.Value.HasValue)
                    {
                        var pt = kv.Value.Value;
                        entries[kv.Key] = new JArray(pt.Lat, pt.Lng);
                    }
                    else
                    {
                        entries[kv.Key] = JValue.CreateNull();
                    }
                }
                var doc = new JObject
                {
                    ["version"] = CacheVersion,
                    ["savedUtc"] = DateTime.UtcNow.ToString("o"),
                    ["entries"] = entries,
                };

                string path = CacheFilePath;
                string tmp = path + ".tmp";
                // Atomic-ish write: write to .tmp then replace, so a crash mid-write can't leave
                // a half-written cache that we'd then refuse to load on next launch.
                File.WriteAllText(tmp, doc.ToString(Formatting.None));
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
            }
            catch
            {
                // Disk full / permission denied / antivirus lock — keep running with the
                // in-memory cache; the next save attempt will retry.
            }
        }

        /// <summary>
        /// Block until any outstanding background save has completed and ensure the latest cache
        /// state is persisted. Call on app shutdown so the freshest entries always make it to
        /// disk; safe to call any time.
        /// </summary>
        public static void Flush()
        {
            try
            {
                Task pending;
                lock (_saveLock) pending = _pendingSave;
                pending?.Wait(TimeSpan.FromSeconds(2));
            }
            catch { }
            // Force one synchronous final write so post-Flush callers see the freshest state.
            WriteCacheToDisk();
        }

        /// <summary>
        /// Resolve a free-form (street, city) tuple to a geographic point, or <c>null</c> if
        /// Nominatim could not place it. Repeated calls for the same address return the cached
        /// result instantly — including cached <c>null</c>s for unparseable addresses, so we
        /// never hammer the API on a busted address.
        /// </summary>
        public static Task<GeoPoint?> ResolveAsync(string street, string city, CancellationToken token = default)
        {
            return ResolveAsync(street, city, null, null, null, token);
        }

        /// <summary>
        /// Richer overload that lets the caller add state + zip + country to the query string.
        /// Critical for the Supey driver roster: the user told us all drivers are in Maine, so we
        /// pin the state and country code on every lookup to keep Nominatim from matching, e.g.,
        /// "112 Newbury St, Auburn, AL" when the driver lives in "112 Newbury St, Auburn, ME".
        /// </summary>
        /// <param name="countryCode">ISO 3166-1 alpha-2 (e.g. <c>"us"</c>) — sent as the
        /// <c>countrycodes</c> filter so Nominatim restricts matches to that country.</param>
        public static async Task<GeoPoint?> ResolveAsync(string street, string city, string state,
            string zip, string countryCode, CancellationToken token = default)
        {
            string key = NormalizeKey(street, city, state, zip, countryCode);
            if (string.IsNullOrEmpty(key)) return null;

            // First call lazily reloads previously-resolved coords from disk so re-runs and
            // re-installs hit Nominatim only for genuinely new addresses.
            EnsureDiskCacheLoaded();

            lock (_cache)
            {
                if (_cache.TryGetValue(key, out var cached)) return cached;
            }

            await _gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                // Re-check inside the gate in case another caller resolved this same address while
                // we were waiting in line.
                lock (_cache)
                {
                    if (_cache.TryGetValue(key, out var cached2)) return cached2;
                }

                // Honor the 1-req/sec policy globally across every concurrent lookup.
                var wait = _nextCallAfterUtc - DateTime.UtcNow;
                if (wait > TimeSpan.Zero) await Task.Delay(wait, token).ConfigureAwait(false);

                GeoPoint? result = null;
                try
                {
                    // Country code piggy-backs on the cache key (so cross-state Auburn doesn't
                    // collide), but is sent to the API as the dedicated countrycodes filter rather
                    // than smuggled into q — the dedicated filter is dramatically more reliable.
                    string ccFilter = "";
                    string queryKey = key;
                    int countryTag = key.IndexOf("|cc=", StringComparison.OrdinalIgnoreCase);
                    if (countryTag >= 0)
                    {
                        ccFilter = "&countrycodes=" + WebUtility.UrlEncode(key.Substring(countryTag + 4).Trim().ToLowerInvariant());
                        queryKey = key.Substring(0, countryTag).Trim();
                    }
                    string q = WebUtility.UrlEncode(queryKey);
                    string url = BaseUri + "?format=json&limit=1&addressdetails=0&q=" + q + ccFilter;
                    using (var resp = await _http.GetAsync(url, token).ConfigureAwait(false))
                    {
                        if (resp.IsSuccessStatusCode)
                        {
                            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                            var arr = JArray.Parse(body);
                            if (arr.Count > 0)
                            {
                                var first = arr[0];
                                double lat = (double)first["lat"];
                                double lon = (double)first["lon"];
                                result = new GeoPoint(lat, lon);
                            }
                        }
                    }
                }
                catch
                {
                    // Network / parse errors → cache null and move on. Caller falls back gracefully.
                }

                _nextCallAfterUtc = DateTime.UtcNow.Add(MinSpacing);
                lock (_cache) { _cache[key] = result; }
                // Schedule a background save so this entry survives an app restart. Negative
                // results are persisted too — re-asking Nominatim for "Apt 4" never helps.
                ScheduleSave();
                return result;
            }
            finally
            {
                _gate.Release();
            }
        }

        private static string NormalizeKey(string street, string city, string state = null,
            string zip = null, string countryCode = null)
        {
            string s = (street ?? "").Trim();
            string c = (city ?? "").Trim();
            string st = (state ?? "").Trim();
            string zp = (zip ?? "").Trim();
            string cc = (countryCode ?? "").Trim();

            if (s.Length == 0 && c.Length == 0 && st.Length == 0 && zp.Length == 0)
                return "";

            // Build "street, city ST 04210" matching the way Nominatim parses US addresses.
            string locality;
            if (st.Length > 0 && zp.Length > 0) locality = (c + ", " + st + " " + zp).TrimStart(',', ' ').Trim();
            else if (st.Length > 0) locality = (c + ", " + st).TrimStart(',', ' ').Trim();
            else if (zp.Length > 0) locality = (c + " " + zp).Trim();
            else locality = c;
            locality = locality.Trim();

            string baseKey;
            if (s.Length > 0 && locality.Length > 0) baseKey = s + ", " + locality;
            else if (s.Length > 0) baseKey = s;
            else baseKey = locality;

            // Country code is part of the cache key so the same street in a different country
            // doesn't share a cached result, but the actual API call separates it as a filter.
            if (cc.Length > 0) baseKey = baseKey + "|cc=" + cc.ToLowerInvariant();
            return baseKey;
        }
    }

    /// <summary>Plain (lat, lng) tuple. Decimal degrees, WGS84 — same as GMap.NET expects.</summary>
    internal readonly struct GeoPoint
    {
        public double Lat { get; }
        public double Lng { get; }
        public GeoPoint(double lat, double lng) { Lat = lat; Lng = lng; }
        public override string ToString() =>
            Lat.ToString("F6", System.Globalization.CultureInfo.InvariantCulture) + "," +
            Lng.ToString("F6", System.Globalization.CultureInfo.InvariantCulture);
    }
}
