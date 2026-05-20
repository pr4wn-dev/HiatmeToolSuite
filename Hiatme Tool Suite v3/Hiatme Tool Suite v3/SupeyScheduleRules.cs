using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Accepted dispatch rules from AIagent — enforced locally on BUILD.</summary>
    internal sealed class SupeyScheduleRules
    {
        public List<SupeyDriverAvoidance> HardAvoidances { get; } = new List<SupeyDriverAvoidance>();
        public List<SupeyPreferredPairing> PreferredPairings { get; } = new List<SupeyPreferredPairing>();

        public static SupeyScheduleRules FromRulesContext(JToken ctx)
        {
            var rules = new SupeyScheduleRules();
            if (ctx == null) return rules;
            var root = ctx as JObject ?? new JObject();
            foreach (var a in root["hard_avoidances"] as JArray ?? new JArray())
            {
                var drv = (a["driver"] ?? "").ToString().Trim();
                if (string.IsNullOrEmpty(drv)) continue;
                rules.HardAvoidances.Add(new SupeyDriverAvoidance
                {
                    Driver = drv,
                    Client = (a["client"] ?? "").ToString().Trim(),
                    Reason = (a["reason"] ?? "").ToString().Trim(),
                });
            }
            foreach (var p in root["preferred_pairings"] as JArray ?? new JArray())
            {
                var client = (p["client"] ?? "").ToString().Trim();
                var driver = (p["driver"] ?? "").ToString().Trim();
                if (string.IsNullOrEmpty(client) || string.IsNullOrEmpty(driver)) continue;
                rules.PreferredPairings.Add(new SupeyPreferredPairing
                {
                    Client = client,
                    Driver = driver,
                    Reason = (p["reason"] ?? "").ToString().Trim(),
                });
            }
            return rules;
        }

        public bool IsDriverBlockedForCluster(string driverName, SupeyTripCluster cluster)
        {
            if (string.IsNullOrWhiteSpace(driverName) || cluster == null) return false;
            foreach (var a in HardAvoidances)
            {
                if (!NamesMatch(a.Driver, driverName)) continue;
                if (string.IsNullOrWhiteSpace(a.Client))
                    return true;
                foreach (var t in cluster.Trips)
                {
                    if (ClientMatchesTrip(a.Client, t))
                        return true;
                }
            }
            return false;
        }

        public double PreferredPairingBonusSeconds(SupeyTripCluster cluster, string driverName)
        {
            if (cluster == null || string.IsNullOrWhiteSpace(driverName)) return 0;
            double bonus = 0;
            foreach (var p in PreferredPairings)
            {
                if (!NamesMatch(p.Driver, driverName)) continue;
                foreach (var t in cluster.Trips)
                {
                    if (ClientMatchesTrip(p.Client, t))
                    {
                        bonus += 300.0;
                        break;
                    }
                }
            }
            return bonus;
        }

        private static bool NamesMatch(string a, string b) =>
            string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

        private static bool ClientMatchesTrip(string ruleClient, MCDownloadedTrip trip)
        {
            if (trip == null || string.IsNullOrWhiteSpace(ruleClient)) return false;
            var needle = ruleClient.Trim();
            if (NamesMatch(trip.ClientFullName, needle)) return true;
            if (NamesMatch(trip.ClientLastName, needle)) return true;
            var full = (trip.ClientFullName ?? "").Trim();
            return full.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    internal sealed class SupeyDriverAvoidance
    {
        public string Driver { get; set; }
        public string Client { get; set; }
        public string Reason { get; set; }
    }

    internal sealed class SupeyPreferredPairing
    {
        public string Client { get; set; }
        public string Driver { get; set; }
        public string Reason { get; set; }
    }
}
