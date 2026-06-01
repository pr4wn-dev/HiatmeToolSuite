using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Clients banned from Schedule Builder assignment (name + Modivcare age). Shared JSON on disk.
    /// </summary>
    internal sealed class ScheduleBuilderBannedClient
    {
        public string DisplayName { get; set; } = "";
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string Age { get; set; } = "";
        public string AddedFromTripNumber { get; set; } = "";
    }

    internal static class ScheduleBuilderBannedClients
    {
        private static readonly object CacheLock = new object();
        private static List<ScheduleBuilderBannedClient> _cached = new List<ScheduleBuilderBannedClient>();

        public static string PrimaryLocalConfigPath =>
            Path.Combine(AppContext.BaseDirectory ?? "", "hiatme_config", "banned_clients.json");

        public static IReadOnlyList<ScheduleBuilderBannedClient> CachedClients
        {
            get
            {
                lock (CacheLock)
                {
                    return _cached.Count > 0 ? _cached : LoadFromFile();
                }
            }
        }

        public static void ReloadCache()
        {
            lock (CacheLock)
            {
                _cached = LoadFromFile();
            }
        }

        public static List<ScheduleBuilderBannedClient> LoadFromFile()
        {
            try
            {
                if (!File.Exists(PrimaryLocalConfigPath))
                    return new List<ScheduleBuilderBannedClient>();
                var root = JObject.Parse(File.ReadAllText(PrimaryLocalConfigPath));
                var arr = root["clients"] as JArray;
                if (arr == null) return new List<ScheduleBuilderBannedClient>();
                var list = new List<ScheduleBuilderBannedClient>();
                foreach (var item in arr)
                {
                    if (item is JObject o)
                        list.Add(ParseClient(o));
                }
                return DeduplicateClients(list);
            }
            catch
            {
                return new List<ScheduleBuilderBannedClient>();
            }
        }

        public static bool SaveToFile(IList<ScheduleBuilderBannedClient> clients)
        {
            var norm = DeduplicateClients(clients ?? new List<ScheduleBuilderBannedClient>());
            lock (CacheLock)
                _cached = norm;
            try
            {
                string dir = Path.GetDirectoryName(PrimaryLocalConfigPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                var arr = new JArray();
                foreach (var c in norm)
                {
                    arr.Add(new JObject
                    {
                        ["displayName"] = c.DisplayName ?? "",
                        ["firstName"] = c.FirstName ?? "",
                        ["lastName"] = c.LastName ?? "",
                        ["age"] = c.Age ?? "",
                        ["tripNumber"] = c.AddedFromTripNumber ?? "",
                    });
                }
                File.WriteAllText(PrimaryLocalConfigPath,
                    new JObject { ["clients"] = arr }.ToString(Newtonsoft.Json.Formatting.Indented));
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static ScheduleBuilderBannedClient FromTrip(MCDownloadedTrip trip)
        {
            if (trip == null) return null;
            string display = NormalizeName(trip.ClientFullName);
            if (display.Length == 0)
            {
                string fn = NormalizeName(trip.ClientFirstName);
                string ln = NormalizeName(trip.ClientLastName);
                display = NormalizeName((fn + " " + ln).Trim());
            }
            if (display.Length == 0) return null;
            return new ScheduleBuilderBannedClient
            {
                DisplayName = display,
                FirstName = NormalizeName(trip.ClientFirstName),
                LastName = NormalizeName(trip.ClientLastName),
                Age = NormalizeAge(trip.Age),
                AddedFromTripNumber = (trip.TripNumber ?? "").Trim(),
            };
        }

        public static bool IsBanned(MCDownloadedTrip trip, IReadOnlyList<ScheduleBuilderBannedClient> clients = null)
        {
            var ban = FromTrip(trip);
            if (ban == null) return false;
            foreach (var c in clients ?? CachedClients)
            {
                if (Matches(ban, c)) return true;
            }
            return false;
        }

        public static bool TryAddFromTrip(MCDownloadedTrip trip)
        {
            var entry = FromTrip(trip);
            if (entry == null) return false;
            var list = new List<ScheduleBuilderBannedClient>(CachedClients);
            if (list.Any(c => Matches(entry, c))) return true;
            list.Add(entry);
            return SaveToFile(list);
        }

        public static bool TryRemoveFromTrip(MCDownloadedTrip trip)
        {
            var entry = FromTrip(trip);
            if (entry == null) return false;
            var list = new List<ScheduleBuilderBannedClient>(CachedClients);
            int idx = list.FindIndex(c => Matches(entry, c));
            if (idx < 0) return false;
            list.RemoveAt(idx);
            return SaveToFile(list);
        }

        public static bool RemoveAt(int index)
        {
            var list = new List<ScheduleBuilderBannedClient>(CachedClients);
            if (index < 0 || index >= list.Count) return false;
            list.RemoveAt(index);
            return SaveToFile(list);
        }

        public static string FormatListLabel(ScheduleBuilderBannedClient c)
        {
            if (c == null) return "";
            string age = string.IsNullOrWhiteSpace(c.Age) ? "age ?" : "age " + c.Age;
            return c.DisplayName + " · " + age;
        }

        private static bool Matches(ScheduleBuilderBannedClient a, ScheduleBuilderBannedClient b)
        {
            if (a == null || b == null) return false;
            if (!string.Equals(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase))
                return false;
            if (string.IsNullOrWhiteSpace(a.Age) || string.IsNullOrWhiteSpace(b.Age))
                return true;
            return string.Equals(a.Age, b.Age, StringComparison.OrdinalIgnoreCase);
        }

        private static ScheduleBuilderBannedClient ParseClient(JObject o) =>
            new ScheduleBuilderBannedClient
            {
                DisplayName = NormalizeName(o["displayName"]?.ToString()),
                FirstName = NormalizeName(o["firstName"]?.ToString()),
                LastName = NormalizeName(o["lastName"]?.ToString()),
                Age = NormalizeAge(o["age"]?.ToString()),
                AddedFromTripNumber = (o["tripNumber"]?.ToString() ?? "").Trim(),
            };

        private static List<ScheduleBuilderBannedClient> DeduplicateClients(IList<ScheduleBuilderBannedClient> input)
        {
            var result = new List<ScheduleBuilderBannedClient>();
            if (input == null) return result;
            foreach (var c in input)
            {
                if (c == null || string.IsNullOrWhiteSpace(c.DisplayName)) continue;
                if (result.Any(x => Matches(c, x))) continue;
                result.Add(c);
            }
            result.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        private static string NormalizeName(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            return System.Text.RegularExpressions.Regex.Replace(s.Trim(), @"\s+", " ");
        }

        private static string NormalizeAge(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            var digits = new string(s.Where(char.IsDigit).ToArray());
            return digits.Length > 0 ? digits : s.Trim();
        }
    }
}
