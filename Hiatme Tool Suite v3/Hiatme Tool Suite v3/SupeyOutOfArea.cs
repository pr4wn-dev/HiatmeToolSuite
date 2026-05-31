using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
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

        public static List<string> LoadLocalFallback()
        {
            foreach (var path in LocalFallbackPaths())
            {
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
