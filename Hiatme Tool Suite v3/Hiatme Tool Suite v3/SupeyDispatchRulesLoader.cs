using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using Newtonsoft.Json.Linq;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Loads accepted dispatch rules from disk — BUILD does not need the AI panel or a VPN.
    /// Copy <c>config/hiatme/dispatch_rules/accepted.json</c> from AIagent into
    /// <c>dispatch_rules/accepted.json</c> next to the exe (shipped with the app).
    /// </summary>
    internal static class SupeyDispatchRulesLoader
    {
        public static SupeyScheduleRules Load()
        {
            foreach (var path in CandidatePaths())
            {
                var ctx = TryBuildContext(path);
                if (ctx != null)
                    return SupeyScheduleRules.FromRulesContext(ctx);
            }
            return new SupeyScheduleRules();
        }

        public static string LoadedFromPath { get; private set; }

        private static IEnumerable<string> CandidatePaths()
        {
            var env = Environment.GetEnvironmentVariable("HIATME_RULES_PATH");
            if (!string.IsNullOrWhiteSpace(env))
                yield return env.Trim();

            var appSetting = ConfigurationManager.AppSettings["HiatmeDispatchRulesPath"];
            if (!string.IsNullOrWhiteSpace(appSetting))
                yield return appSetting.Trim();

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            yield return Path.Combine(baseDir, "dispatch_rules", "accepted.json");

            // Dev: AIagent clone beside HiatmeSuite on same drive.
            yield return @"F:\Projects\AIagent\config\hiatme\dispatch_rules\accepted.json";

            string appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "HiatmeToolSuite",
                "dispatch_rules",
                "accepted.json");
            yield return appData;
        }

        private static JObject TryBuildContext(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    return null;
                var root = JObject.Parse(File.ReadAllText(path));
                var rules = root["rules"] as JArray;
                if (rules == null || rules.Count == 0)
                    return null;

                var hard = new JArray();
                var load = new JArray();
                var pref = new JArray();

                foreach (var rec in rules)
                {
                    if (rec == null) continue;
                    if (rec["enabled"]?.Value<bool>() == false) continue;
                    string kind = (rec["kind"] ?? "").ToString().Trim().ToLowerInvariant();
                    var payload = rec["payload"] as JObject ?? new JObject();
                    string title = (rec["title"] ?? "").ToString();

                    if (kind == "driver_avoidance")
                    {
                        string client = (payload["client"] ?? "").ToString().Trim();
                        if (!string.IsNullOrEmpty(client) && client != "*")
                        {
                            hard.Add(new JObject
                            {
                                ["driver"] = payload["driver"],
                                ["client"] = client,
                                ["reason"] = title,
                            });
                        }
                        else
                        {
                            load.Add(new JObject
                            {
                                ["driver"] = payload["driver"],
                                ["max_cluster_riders"] = payload["max_cluster_riders"] ?? 4,
                                ["reason"] = title,
                            });
                        }
                    }
                    else if (kind == "driver_load_preference")
                    {
                        load.Add(new JObject
                        {
                            ["driver"] = payload["driver"],
                            ["max_cluster_riders"] = payload["max_cluster_riders"] ?? 4,
                            ["reason"] = title,
                        });
                    }
                    else if (kind == "preferred_pairing")
                    {
                        pref.Add(new JObject
                        {
                            ["client"] = payload["client"],
                            ["driver"] = payload["driver"],
                            ["reason"] = title,
                        });
                    }
                }

                LoadedFromPath = path;
                return new JObject
                {
                    ["hard_avoidances"] = hard,
                    ["driver_load_preferences"] = load,
                    ["preferred_pairings"] = pref,
                };
            }
            catch
            {
                return null;
            }
        }
    }
}
