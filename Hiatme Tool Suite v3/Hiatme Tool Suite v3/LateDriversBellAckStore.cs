using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Persists Driver Habits will-call bell ack + per-message dismissals for the service day
    /// so dismissing stays quiet until the bell content hash changes (new messages).
    /// </summary>
    internal static class LateDriversBellAckStore
    {
        private static readonly object FileLock = new object();

        private sealed class DayFile
        {
            public string ServiceDate { get; set; }
            public string AckHash { get; set; }
            public List<string> DismissedKeys { get; set; }
        }

        private static string StoreDir =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "HiatmeToolSuite");

        private static string StorePath =>
            Path.Combine(StoreDir, "late-drivers-bell-ack.json");

        public static string TodayIso() =>
            DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        public static void LoadForToday(out string ackHash, out HashSet<string> dismissedKeys)
        {
            ackHash = null;
            dismissedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string today = TodayIso();

            lock (FileLock)
            {
                try
                {
                    if (!File.Exists(StorePath))
                        return;
                    string json = File.ReadAllText(StorePath);
                    var doc = JsonConvert.DeserializeObject<DayFile>(json);
                    if (doc == null
                        || !string.Equals(
                            (doc.ServiceDate ?? "").Trim(),
                            today,
                            StringComparison.OrdinalIgnoreCase))
                        return;

                    if (!string.IsNullOrWhiteSpace(doc.AckHash))
                        ackHash = doc.AckHash.Trim();

                    if (doc.DismissedKeys != null)
                    {
                        foreach (string k in doc.DismissedKeys)
                        {
                            if (!string.IsNullOrWhiteSpace(k))
                                dismissedKeys.Add(k.Trim());
                        }
                    }
                }
                catch { }
            }
        }

        public static void SaveForToday(string ackHash, IEnumerable<string> dismissedKeys)
        {
            string today = TodayIso();
            var list = (dismissedKeys ?? Enumerable.Empty<string>())
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
                        ServiceDate = today,
                        AckHash = string.IsNullOrWhiteSpace(ackHash) ? null : ackHash.Trim(),
                        DismissedKeys = list,
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
