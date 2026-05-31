using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Warn when template locks put A/B legs of the same day on different drivers.</summary>
    internal static class SupeyTripLegConsistency
    {
        private static readonly Regex LegSuffix =
            new Regex(@"^(\d+-\d+)-[AB]$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static void AppendSplitLegWarnings(SupeyScheduleResult result)
        {
            if (result == null) return;

            var byBase = new Dictionary<string, Dictionary<string, string>>(
                StringComparer.OrdinalIgnoreCase);

            void NoteLeg(string tripNumber, string driverName)
            {
                string tn = (tripNumber ?? "").Trim();
                string driver = (driverName ?? "").Trim();
                if (string.IsNullOrEmpty(tn) || string.IsNullOrEmpty(driver))
                    return;
                string baseId = BaseTripId(tn);
                if (string.IsNullOrEmpty(baseId))
                    baseId = BaseTripIdFallback(tn);
                if (string.IsNullOrEmpty(baseId))
                    return;
                if (!byBase.TryGetValue(baseId, out var legs))
                {
                    legs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    byBase[baseId] = legs;
                }
                string leg = LegLetter(tn);
                if (!string.IsNullOrEmpty(leg))
                    legs[leg] = driver;
            }

            if (result.Locks != null)
            {
                foreach (var kv in result.Locks)
                    NoteLeg(kv.Key, kv.Value);
            }

            if (result.DriverPlans != null)
            {
                foreach (var plan in result.DriverPlans)
                {
                    if (plan?.Groups == null || string.IsNullOrWhiteSpace(plan.Driver?.Name))
                        continue;
                    string drv = plan.Driver.Name.Trim();
                    foreach (var g in plan.Groups)
                    {
                        if (g?.Trips == null) continue;
                        foreach (var t in g.Trips)
                            NoteLeg(t?.TripNumber, drv);
                    }
                }
            }

            var stillSplit = new List<string>();
            foreach (var kv in byBase)
            {
                if (!kv.Value.TryGetValue("A", out var drvA)
                    || !kv.Value.TryGetValue("B", out var drvB))
                    continue;
                if (string.Equals(drvA, drvB, StringComparison.OrdinalIgnoreCase))
                    continue;
                stillSplit.Add(kv.Key + " (A:" + drvA + ", B:" + drvB + ")");
            }
            if (stillSplit.Count == 0) return;
            string sample = string.Join(", ", stillSplit.Count > 6
                ? stillSplit.GetRange(0, 6)
                : stillSplit);
            if (stillSplit.Count > 6) sample += ", …";
            result.BuildWarnings.Add(new SupeyWarning(
                SupeyWarningKind.BuildDiagnostic,
                "",
                "A/B legs",
                stillSplit.Count + " trip(s) still have A/B on different drivers after auto-pair — "
                + sample + ". Fix Friday template tabs."));
        }

        private static string BaseTripId(string tripNumber)
        {
            var m = LegSuffix.Match((tripNumber ?? "").Trim());
            return m.Success ? m.Groups[1].Value : "";
        }

        /// <summary>e.g. 1-9431-A → 1-9431 when strict pattern misses.</summary>
        private static string BaseTripIdFallback(string tripNumber)
        {
            string s = (tripNumber ?? "").Trim();
            if (s.Length < 3) return "";
            char last = char.ToUpperInvariant(s[s.Length - 1]);
            if (last != 'A' && last != 'B') return "";
            if (s[s.Length - 2] != '-') return "";
            return s.Substring(0, s.Length - 2);
        }

        private static string LegLetter(string tripNumber)
        {
            string s = (tripNumber ?? "").Trim();
            if (s.Length < 2) return "";
            char c = s[s.Length - 1];
            if (c == 'A' || c == 'B') return c.ToString().ToUpperInvariant();
            return "";
        }
    }
}
