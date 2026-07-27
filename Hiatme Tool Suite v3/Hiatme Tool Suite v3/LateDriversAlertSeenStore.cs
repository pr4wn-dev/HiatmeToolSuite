using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Persists Driver Habits blink/chirp "already announced" keys for the service day
    /// so restarting the Suite does not re-blink the same open late/early episode.
    /// </summary>
    internal static class LateDriversAlertSeenStore
    {
        private static readonly object FileLock = new object();

        private sealed class DayFile
        {
            public string ServiceDate { get; set; }
            public List<string> Keys { get; set; }
        }

        private static string StoreDir =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "HiatmeToolSuite");

        private static string StorePath =>
            Path.Combine(StoreDir, "late-drivers-alert-seen.json");

        public static string TodayIso() =>
            DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        public static HashSet<string> LoadForToday()
        {
            return LoadForDate(TodayIso());
        }

        public static HashSet<string> LoadForDate(string serviceDateIso)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            serviceDateIso = (serviceDateIso ?? "").Trim();
            if (string.IsNullOrEmpty(serviceDateIso))
                return result;

            lock (FileLock)
            {
                try
                {
                    if (!File.Exists(StorePath))
                        return result;
                    string json = File.ReadAllText(StorePath);
                    var doc = JsonConvert.DeserializeObject<DayFile>(json);
                    if (doc == null
                        || !string.Equals(
                            (doc.ServiceDate ?? "").Trim(),
                            serviceDateIso,
                            StringComparison.OrdinalIgnoreCase)
                        || doc.Keys == null)
                        return result;
                    foreach (string k in doc.Keys)
                    {
                        if (!string.IsNullOrWhiteSpace(k))
                            result.Add(k.Trim());
                    }
                }
                catch { }
            }
            return result;
        }

        public static void SaveForToday(IEnumerable<string> keys)
        {
            SaveForDate(TodayIso(), keys);
        }

        public static void SaveForDate(string serviceDateIso, IEnumerable<string> keys)
        {
            serviceDateIso = (serviceDateIso ?? "").Trim();
            if (string.IsNullOrEmpty(serviceDateIso))
                return;

            var list = (keys ?? Enumerable.Empty<string>())
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Select(k => k.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .ToList();

            lock (FileLock)
            {
                try
                {
                    Directory.CreateDirectory(StoreDir);
                    var doc = new DayFile
                    {
                        ServiceDate = serviceDateIso,
                        Keys = list,
                    };
                    File.WriteAllText(
                        StorePath,
                        JsonConvert.SerializeObject(doc, Formatting.Indented));
                }
                catch { }
            }
        }
    }
}
