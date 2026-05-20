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

        // Persistence machinery.
        //
        // We coalesce rapid mutations (typical case: a build geocoding hundreds of addresses
        // back-to-back) into a single in-flight save using an epoch counter:
        //
        //   _dirtyEpoch  — incremented on every cache mutation
        //   _savedEpoch  — value of _dirtyEpoch as of the last *successful* save
        //
        // After a save finishes, we compare the two: if more mutations happened during the
        // write, we immediately schedule another save. That tail-recursion guarantees every
        // entry eventually lands on disk, fixing the race in the previous "if-idle-go" design
        // where new entries arriving during a write would never trigger their own save.
        private static bool _diskLoaded;
        private static readonly object _saveLock = new object();
        private static Task _pendingSave;
        private static long _dirtyEpoch;
        private static long _savedEpoch;

        // Diagnostics — visible to UI so users can confirm the cache is actually doing its job.
        private static long _hits;
        private static long _misses;

        /// <summary>Cumulative cache hits for the current process. Reset with <see cref="ResetCounters"/>.</summary>
        public static long CacheHits => Interlocked.Read(ref _hits);

        /// <summary>Cumulative cache misses (Nominatim calls) for the current process.</summary>
        public static long CacheMisses => Interlocked.Read(ref _misses);

        /// <summary>Zeros the hit/miss counters. Call before a build to get per-build stats.</summary>
        public static void ResetCounters()
        {
            Interlocked.Exchange(ref _hits, 0);
            Interlocked.Exchange(ref _misses, 0);
        }

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
        /// Marks the cache as dirty (a new entry was added) and schedules a background save.
        /// Saves are coalesced — only one write runs at a time — but if more mutations happen
        /// during the write, another save is automatically chained on completion so no entry is
        /// ever permanently lost in memory.
        /// </summary>
        private static void ScheduleSave()
        {
            Interlocked.Increment(ref _dirtyEpoch);
            StartSaveIfNeeded();
        }

        private static void StartSaveIfNeeded()
        {
            lock (_saveLock)
            {
                // If a save is currently writing we don't start another — but the worker's
                // continuation will re-check the epoch and chain a follow-up if needed.
                if (_pendingSave != null && !_pendingSave.IsCompleted) return;

                long target = Interlocked.Read(ref _dirtyEpoch);
                if (target == Interlocked.Read(ref _savedEpoch))
                {
                    // No outstanding mutations — nothing to do.
                    return;
                }

                _pendingSave = Task.Run(() =>
                {
                    bool ok = WriteCacheToDisk();
                    if (ok) Interlocked.Exchange(ref _savedEpoch, target);
                    // Trailing-save: if mutations slipped in while we were writing, we owe another
                    // pass. This is what guarantees every dirty entry eventually persists. Without
                    // it, addresses resolved during an in-flight save would sit only in memory and
                    // disappear on app close (modulo Flush()).
                    if (Interlocked.Read(ref _dirtyEpoch) != Interlocked.Read(ref _savedEpoch))
                    {
                        StartSaveIfNeeded();
                    }
                });
            }
        }

        /// <summary>
        /// Serializes the in-memory cache to disk via a temp-file + replace pattern so a crash
        /// mid-write can't leave a half-written file that we'd refuse to load. Returns true on
        /// success, false on any IO failure (the next mutation will trigger another retry).
        /// </summary>
        private static bool WriteCacheToDisk()
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
                File.WriteAllText(tmp, doc.ToString(Formatting.None));
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
                return true;
            }
            catch
            {
                // Disk full / permission denied / antivirus lock — keep running with the
                // in-memory cache; the next save attempt will retry.
                return false;
            }
        }

        /// <summary>
        /// Block until any outstanding background save has completed and ensure the latest cache
        /// state is persisted. Call on app shutdown so the freshest entries always make it to
        /// disk; safe to call any time.
        /// </summary>
        /// <summary>
        /// Save a dispatcher-confirmed pin to the company AI cache (and local cache when using server geo).
        /// </summary>
        public static async Task ConfirmPinAsync(
            string street,
            string city,
            string state,
            string zip,
            GeoPoint point,
            CancellationToken token = default)
        {
            if (HiatmeGeoSettings.UseServer)
            {
                var ai = HiatmeAiSettings.Load();
                string who = null;
                try
                {
                    who = (Properties.Settings.Default.wrUserName ?? "").Trim();
                }
                catch { }
                await HiatmeGeoClient.ConfirmGeocodeAsync(
                    ai, street, city, state, zip, point, who, token).ConfigureAwait(false);
            }

            string key = NormalizeKey(street, city, state, zip, "us");
            if (string.IsNullOrEmpty(key)) return;
            EnsureDiskCacheLoaded();
            lock (_cache)
            {
                _cache[key] = point;
            }
            ScheduleSave();
        }

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
        /// Trip PU/DO lookup: known Maine clinics first, then Nominatim with ME fallbacks.
        /// </summary>
        public static async Task<GeoPoint?> ResolveTripEndpointAsync(string street, string city,
            CancellationToken token = default)
        {
            GeoPoint known;
            if (SupeyKnownFacilities.TryResolve(street, city, out known))
                return known;

            return await ResolveWithFallbacksAsync(street, city, "ME", null, "us", token).ConfigureAwait(false);
        }

        /// <summary>
        /// Resolves with progressive simplification of the query — useful for hand-typed driver
        /// homes where a misspelled city ("Lvermore Falls") would otherwise leave the driver
        /// silently excluded from a build. Tries the full address first, then drops the
        /// (often-misspelled) city while keeping zip + state + country, then drops the zip too
        /// as a last-ditch street + state lookup. Returns the first match, or <c>null</c> if
        /// every variant came back empty.
        /// </summary>
        /// <remarks>
        /// Each variant is cached individually, so a successful fallback for one driver doesn't
        /// poison cache lookups for unrelated addresses sharing partial components. The 1
        /// req/sec Nominatim throttle still applies across variants — typically just one or two
        /// extra calls per driver, only on addresses that didn't match cleanly.
        /// </remarks>
        public static async Task<GeoPoint?> ResolveWithFallbacksAsync(string street, string city, string state,
            string zip, string countryCode, CancellationToken token = default)
        {
            var p = await ResolveAsync(street, city, state, zip, countryCode, token).ConfigureAwait(false);
            if (p.HasValue) return p;

            // Nominatim resolves US addresses very reliably from "street + zip + state" alone,
            // so dropping a misspelled city usually wins on the first fallback. Only worth
            // trying if we actually have a zip — otherwise the request would be too vague.
            if (!string.IsNullOrWhiteSpace(zip))
            {
                p = await ResolveAsync(street, "", state, zip, countryCode, token).ConfigureAwait(false);
                if (p.HasValue) return p;
            }

            // Final fallback: street + state. Loose enough to risk hitting another street with
            // the same name in a different city, but for driver homes where the user told us
            // every address is in Maine, the country/state filter keeps the match in-scope.
            if (!string.IsNullOrWhiteSpace(state))
            {
                p = await ResolveAsync(street, "", state, "", countryCode, token).ConfigureAwait(false);
                if (p.HasValue) return p;
            }

            return null;
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
                if (_cache.TryGetValue(key, out var cached))
                {
                    Interlocked.Increment(ref _hits);
                    return cached;
                }
            }

            if (HiatmeGeoSettings.UseServer)
            {
                try
                {
                    var ai = HiatmeAiSettings.Load();
                    var serverPt = await HiatmeGeoClient.ResolveAsync(
                        ai, street, city, state, zip, countryCode, token).ConfigureAwait(false);
                    lock (_cache) { _cache[key] = serverPt; }
                    ScheduleSave();
                    if (serverPt.HasValue) Interlocked.Increment(ref _hits);
                    else Interlocked.Increment(ref _misses);
                    return serverPt;
                }
                catch
                {
                    // fall through to local Nominatim
                }
            }

            await _gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                // Re-check inside the gate in case another caller resolved this same address while
                // we were waiting in line.
                lock (_cache)
                {
                    if (_cache.TryGetValue(key, out var cached2))
                    {
                        Interlocked.Increment(ref _hits);
                        return cached2;
                    }
                }
                // Past the cache — we're committed to a Nominatim call.
                Interlocked.Increment(ref _misses);

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
