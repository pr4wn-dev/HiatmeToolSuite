using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Loads the picked weekday's existing template CSVs and turns them into advisory indexes the
    /// schedule algorithm can use as soft scoring bonuses. The user owns the templates and uses
    /// them with the original Schedule Builder; here they're just hints — never overrides.
    /// </summary>
    /// <remarks>
    /// Two indexes are surfaced:
    /// <list type="bullet">
    /// <item><see cref="PreferredDriverByTrip"/> — exact trip-number match → driver name. The next
    /// build seeds that trip's score with a discount toward the historical driver.</item>
    /// <item><see cref="HistoricalPairs"/> — pairs of (clientFullName, clientFullName) that have
    /// historically ridden together on the same driver/day. Used to nudge clustering when the
    /// time/distance gates are borderline.</item>
    /// </list>
    /// Hints are weekday-scoped; loading Friday templates does not influence a Tuesday build.
    /// </remarks>
    internal sealed class SupeyTemplateHints
    {
        public string Weekday { get; }
        public IReadOnlyDictionary<string, string> PreferredDriverByTrip { get; }
        public ICollection<HistoricalPair> HistoricalPairs { get; }
        public IReadOnlyDictionary<string, int> DriverHistoricalLoad { get; }
        /// <summary>Ordered trip numbers per template driver name (PU sequence from CSV rows).</summary>
        public IReadOnlyDictionary<string, List<string>> DriverTripOrder { get; }
        public bool HasAnyTemplate { get; }

        public SupeyTemplateHints(string weekday)
        {
            Weekday = weekday ?? "";
            var preferredDriver = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var pairs = new HashSet<HistoricalPair>();
            var loads = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var tripOrder = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            HasAnyTemplate = false;

            string dir = TemplateBuilder.GetDayTemplateDirectory(weekday);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                PreferredDriverByTrip = preferredDriver;
                HistoricalPairs = pairs;
                DriverHistoricalLoad = loads;
                DriverTripOrder = tripOrder;
                return;
            }

            try
            {
                var csvs = Directory.GetFiles(dir, "*.csv");
                var parser = new Regex(",(?=(?:[^\"]*\"[^\"]*\")*(?![^\"]*\"))");
                foreach (var path in csvs)
                {
                    string driver = Path.GetFileNameWithoutExtension(path) ?? "";
                    if (string.IsNullOrWhiteSpace(driver)) continue;
                    if (driver.IndexOf("Reserves", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (driver.IndexOf("Schedule", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (driver.IndexOf("LGTC", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                    string[] lines;
                    try { lines = File.ReadAllLines(path); } catch { continue; }
                    if (lines == null || lines.Length == 0) continue;

                    var clientsThisDriver = new List<string>(lines.Length);
                    foreach (var raw in lines)
                    {
                        if (string.IsNullOrWhiteSpace(raw)) continue;
                        var values = parser.Split(raw);
                        if (values == null || values.Length < 14) continue;
                        if (TripTemplateCsvValidator.IsLikelyHeaderRow(values)) continue;

                        string trip = (values[0] ?? "").Replace("\"", "").Trim();
                        string client = (values[2] ?? "").Replace("\"", "").Trim();

                        if (!string.IsNullOrEmpty(trip))
                        {
                            preferredDriver[trip] = driver;
                            HasAnyTemplate = true;
                            if (!tripOrder.TryGetValue(driver, out var orderList))
                            {
                                orderList = new List<string>();
                                tripOrder[driver] = orderList;
                            }
                            orderList.Add(trip);
                        }
                        if (!string.IsNullOrEmpty(client))
                            clientsThisDriver.Add(client);
                    }

                    if (clientsThisDriver.Count > 0)
                    {
                        loads.TryGetValue(driver, out int prev);
                        loads[driver] = prev + clientsThisDriver.Count;
                    }

                    // Build pairwise history within this driver's day. The order doesn't matter; the
                    // pair struct normalizes A and B before hashing, so (Smith, Jones) == (Jones, Smith).
                    for (int i = 0; i < clientsThisDriver.Count; i++)
                    {
                        for (int j = i + 1; j < clientsThisDriver.Count; j++)
                            pairs.Add(new HistoricalPair(clientsThisDriver[i], clientsThisDriver[j]));
                    }
                }
            }
            catch
            {
                // Hints are advisory; never break a build because the template folder is sketchy.
            }

            PreferredDriverByTrip = preferredDriver;
            HistoricalPairs = pairs;
            DriverHistoricalLoad = loads;
            DriverTripOrder = tripOrder;
        }

        public bool RodeTogetherHistorically(string clientA, string clientB)
        {
            if (HistoricalPairs.Count == 0) return false;
            return HistoricalPairs.Contains(new HistoricalPair(clientA, clientB));
        }

        public string PreferredDriverFor(string tripNumber)
        {
            if (string.IsNullOrWhiteSpace(tripNumber)) return null;
            return PreferredDriverByTrip.TryGetValue(tripNumber, out var d) ? d : null;
        }

        /// <summary>
        /// Order-independent unordered pair of two client names. <see cref="GetHashCode"/> is
        /// symmetric so (A,B) and (B,A) hash the same.
        /// </summary>
        public readonly struct HistoricalPair : IEquatable<HistoricalPair>
        {
            public string A { get; }
            public string B { get; }
            public HistoricalPair(string a, string b)
            {
                a = (a ?? "").Trim();
                b = (b ?? "").Trim();
                if (string.CompareOrdinal(a, b) <= 0) { A = a; B = b; }
                else { A = b; B = a; }
            }
            public bool Equals(HistoricalPair other) =>
                string.Equals(A, other.A, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(B, other.B, StringComparison.OrdinalIgnoreCase);
            public override bool Equals(object obj) => obj is HistoricalPair p && Equals(p);
            public override int GetHashCode() =>
                StringComparer.OrdinalIgnoreCase.GetHashCode(A ?? "") ^
                StringComparer.OrdinalIgnoreCase.GetHashCode(B ?? "");
        }
    }
}
