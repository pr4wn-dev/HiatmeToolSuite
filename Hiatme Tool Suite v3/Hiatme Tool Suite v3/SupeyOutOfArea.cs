using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Cities/towns Hiatme does not service — matched on PU or DO city (contains, case-insensitive).
    /// Canonical list lives on the office panel; local JSON is fallback when offline.
    /// </summary>
    internal static class SupeyOutOfArea
    {
        private static readonly object CacheLock = new object();
        private static List<string> _cached = new List<string>();
        private static DateTime _cachedAt = DateTime.MinValue;

        public static IReadOnlyList<string> CachedAreas
        {
            get
            {
                lock (CacheLock)
                {
                    return _cached.Count > 0 ? _cached : LoadLocalFallback();
                }
            }
        }

        public static void SetCachedAreas(IList<string> areas)
        {
            lock (CacheLock)
            {
                _cached = NormalizeAreas(areas);
                _cachedAt = DateTime.UtcNow;
            }
        }

        public static List<string> NormalizeAreas(IList<string> areas)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<string>();
            if (areas == null) return list;
            foreach (var raw in areas)
            {
                var n = (raw ?? "").Trim();
                if (n.Length == 0 || !seen.Add(n)) continue;
                list.Add(n);
            }
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        /// <summary>Returns matched area name if trip touches list, else null.</summary>
        public static string MatchTrip(MCDownloadedTrip trip, IReadOnlyList<string> areas = null)
        {
            if (trip == null) return null;
            var zones = areas ?? CachedAreas;
            if (zones == null || zones.Count == 0) return null;
            string pu = (trip.PUCity ?? "").Trim();
            string doCity = (trip.DOCITY ?? "").Trim();
            foreach (var area in zones)
            {
                if (string.IsNullOrWhiteSpace(area)) continue;
                if (CityContains(area, pu) || CityContains(area, doCity))
                    return area;
            }
            return null;
        }

        private static bool CityContains(string area, string city)
        {
            if (string.IsNullOrWhiteSpace(area) || string.IsNullOrWhiteSpace(city)) return false;
            return city.IndexOf(area, StringComparison.OrdinalIgnoreCase) >= 0
                || area.IndexOf(city, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Primary on-disk copy used when the office panel is offline.</summary>
        public static string PrimaryLocalConfigPath =>
            Path.Combine(AppContext.BaseDirectory ?? "", "hiatme_config", "out_of_area.json");

        /// <summary>Persist list to <see cref="PrimaryLocalConfigPath"/> and refresh cache.</summary>
        public static bool TrySaveLocalFallback(IList<string> areas)
        {
            var norm = NormalizeAreas(areas);
            SetCachedAreas(norm);
            try
            {
                string path = PrimaryLocalConfigPath;
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                var root = new JObject
                {
                    ["areas"] = new JArray(norm),
                };
                File.WriteAllText(path, root.ToString(Newtonsoft.Json.Formatting.Indented));
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Reads only <see cref="PrimaryLocalConfigPath"/> (no built-in defaults).</summary>
        public static bool TryReadLocalConfigFile(out List<string> areas)
        {
            areas = new List<string>();
            try
            {
                string path = PrimaryLocalConfigPath;
                if (!File.Exists(path)) return false;
                var root = JObject.Parse(File.ReadAllText(path));
                var arr = root["areas"] as JArray;
                if (arr == null || arr.Count == 0) return false;
                var list = new List<string>();
                foreach (var t in arr)
                {
                    var s = (t?.ToString() ?? "").Trim();
                    if (s.Length > 0) list.Add(s);
                }
                areas = NormalizeAreas(list);
                return areas.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        public static bool AreasEqual(IList<string> a, IList<string> b)
        {
            var left = new HashSet<string>(NormalizeAreas(a), StringComparer.OrdinalIgnoreCase);
            var right = new HashSet<string>(NormalizeAreas(b), StringComparer.OrdinalIgnoreCase);
            return left.SetEquals(right);
        }

        /// <summary>
        /// When the office panel is online, push the on-disk local list to the server if it differs
        /// (e.g. edits made while offline). Returns true if a push was performed.
        /// </summary>
        public static async Task<bool> TrySyncLocalFileToServerAsync(
            HiatmeAiSettings settings,
            CancellationToken cancellationToken = default)
        {
            if (!HiatmeGeoSettings.UseServer || settings == null) return false;
            if (!TryReadLocalConfigFile(out var local)) return false;

            IList<string> server;
            try
            {
                server = await HiatmeAiClient.GetOutOfAreaAsync(settings, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                return false;
            }

            if (AreasEqual(local, server)) return false;
            return await HiatmeAiClient.SetOutOfAreaAsync(settings, local, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public static List<string> LoadLocalFallback()
        {
            if (TryReadLocalConfigFile(out var fromPrimary) && fromPrimary.Count > 0)
                return fromPrimary;

            foreach (var path in LocalFallbackPaths())
            {
                if (string.Equals(path, PrimaryLocalConfigPath, StringComparison.OrdinalIgnoreCase))
                    continue;
                try
                {
                    if (!File.Exists(path)) continue;
                    var root = JObject.Parse(File.ReadAllText(path));
                    var arr = root["areas"] as JArray;
                    if (arr == null || arr.Count == 0) continue;
                    var list = new List<string>();
                    foreach (var t in arr)
                    {
                        var s = (t?.ToString() ?? "").Trim();
                        if (s.Length > 0) list.Add(s);
                    }
                    return NormalizeAreas(list);
                }
                catch { /* try next path */ }
            }
            return NormalizeAreas(DefaultAreas);
        }

        private static IEnumerable<string> LocalFallbackPaths()
        {
            string baseDir = AppContext.BaseDirectory ?? "";
            yield return Path.Combine(baseDir, "hiatme_config", "out_of_area.json");
            yield return Path.Combine(baseDir, "dispatch_rules", "out_of_area.json");
            var appSetting = ConfigurationManager.AppSettings["HiatmeOutOfAreaPath"];
            if (!string.IsNullOrWhiteSpace(appSetting))
                yield return appSetting.Trim();
            yield return @"F:\Projects\AIagent\config\hiatme\out_of_area.json";
            yield return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "AIagent", "config", "hiatme", "out_of_area.json");
        }

        private static readonly string[] DefaultAreas =
        {
            "Rumford", "Mexico", "Livermore Falls", "Farmington",
            "Rangeley", "Dixfield", "Jay", "Wilton", "Strong", "Eustis",
        };
    }
}
