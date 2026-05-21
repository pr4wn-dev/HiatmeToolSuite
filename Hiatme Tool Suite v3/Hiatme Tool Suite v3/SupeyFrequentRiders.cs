using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Clients who appear on weekday templates most days of the week — used only to prioritize
    /// getting them scheduled (coverage), not to pin them to a particular driver.
    /// </summary>
    internal sealed class SupeyFrequentRiders
    {
        private static readonly string[] Weekdays =
        {
            "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday",
        };

        /// <summary>Must appear on at least this many weekday template folders.</summary>
        private const int MinWeekdaysPresent = 4;

        private readonly HashSet<string> _names;

        public bool HasData => _names != null && _names.Count > 0;

        private SupeyFrequentRiders(HashSet<string> names)
        {
            _names = names ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        public static SupeyFrequentRiders Load()
        {
            var weekdayHits = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var parser = new Regex(",(?=(?:[^\"]*\"[^\"]*\")*(?![^\"]*\"))");

            foreach (string day in Weekdays)
            {
                string dir = TemplateBuilder.GetDayTemplateDirectory(day);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;

                var seenThisDay = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                string[] csvs;
                try { csvs = Directory.GetFiles(dir, "*.csv"); }
                catch { continue; }

                foreach (string path in csvs)
                {
                    string driver = Path.GetFileNameWithoutExtension(path) ?? "";
                    if (string.IsNullOrWhiteSpace(driver)) continue;
                    if (driver.IndexOf("Reserves", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (driver.IndexOf("Schedule", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (driver.IndexOf("LGTC", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                    string[] lines;
                    try { lines = File.ReadAllLines(path); }
                    catch { continue; }
                    if (lines == null) continue;

                    foreach (string raw in lines)
                    {
                        if (string.IsNullOrWhiteSpace(raw)) continue;
                        var values = parser.Split(raw);
                        if (values == null || values.Length < 3) continue;
                        if (TripTemplateCsvValidator.IsLikelyHeaderRow(values)) continue;

                        string client = (values[2] ?? "").Replace("\"", "").Trim();
                        if (client.Length < 2) continue;
                        seenThisDay.Add(NormalizeClient(client));
                    }
                }

                foreach (string c in seenThisDay)
                {
                    if (!weekdayHits.TryGetValue(c, out var days))
                    {
                        days = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        weekdayHits[c] = days;
                    }
                    days.Add(day);
                }
            }

            var frequent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in weekdayHits)
            {
                if (kv.Value.Count >= MinWeekdaysPresent)
                    frequent.Add(kv.Key);
            }

            return new SupeyFrequentRiders(frequent);
        }

        public bool IsFrequent(MCDownloadedTrip trip)
        {
            if (trip == null || _names.Count == 0) return false;
            string full = NormalizeClient(trip.ClientFullName);
            if (full.Length >= 2 && _names.Contains(full)) return true;
            string last = NormalizeClient(trip.ClientLastName);
            return last.Length >= 2 && _names.Contains(last);
        }

        public bool ClusterHasFrequent(SupeyTripCluster cluster)
        {
            if (cluster == null || _names.Count == 0) return false;
            foreach (var t in cluster.Trips)
            {
                if (IsFrequent(t)) return true;
            }
            return false;
        }

        internal static string NormalizeClient(string name) =>
            string.IsNullOrWhiteSpace(name) ? "" : Regex.Replace(name.Trim(), @"\s+", " ");
    }
}
