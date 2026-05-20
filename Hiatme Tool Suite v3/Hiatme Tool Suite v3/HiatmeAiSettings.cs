using System;
using System.Configuration;
using System.IO;
using Newtonsoft.Json;

namespace Hiatme_Tool_Suite_v3
{
    internal sealed class HiatmeAiSettings
    {
        public const int DefaultPort = 8787;

        public string BaseUrl { get; set; } = "http://127.0.0.1:" + DefaultPort;
        public string ApiToken { get; set; } = "";
        public string ClientId { get; set; } = "";

        /// <summary>After SAVE workbook, store a short approval note for memory.</summary>
        public bool RememberOnSave { get; set; } = true;

        /// <summary>Route geocode + OSRM through AIagent (<c>/api/hiatme/geo/*</c>) on the server PC.</summary>
        public bool UseServerGeo { get; set; } = true;

        private static string BaseDir => AppDomain.CurrentDomain.BaseDirectory;
        private static string PersonalConfigPath => Path.Combine(BaseDir, "hiatme_ai.json");
        private static string DefaultsConfigPath => Path.Combine(BaseDir, "hiatme_ai.defaults.json");

        public static HiatmeAiSettings Load()
        {
            var merged = new HiatmeAiSettings();
            TryMergeFile(merged, DefaultsConfigPath);
            TryMergeFile(merged, PersonalConfigPath);
            ApplyAppConfigOverrides(merged);
            ApplyEnvironmentOverrides(merged);
            return merged;
        }

        private static void TryMergeFile(HiatmeAiSettings target, string path)
        {
            try
            {
                if (!File.Exists(path)) return;
                var part = JsonConvert.DeserializeObject<HiatmeAiSettings>(File.ReadAllText(path));
                if (part == null) return;
                if (!string.IsNullOrWhiteSpace(part.BaseUrl))
                    target.BaseUrl = part.BaseUrl.Trim();
                if (!string.IsNullOrWhiteSpace(part.ApiToken))
                    target.ApiToken = part.ApiToken.Trim();
                if (!string.IsNullOrWhiteSpace(part.ClientId))
                    target.ClientId = part.ClientId.Trim();
                target.RememberOnSave = part.RememberOnSave;
                target.UseServerGeo = part.UseServerGeo;
            }
            catch { }
        }

        private static void ApplyAppConfigOverrides(HiatmeAiSettings s)
        {
            var url = ConfigurationManager.AppSettings["HiatmeAiBaseUrl"];
            if (!string.IsNullOrWhiteSpace(url))
                s.BaseUrl = url.Trim();
            var tok = ConfigurationManager.AppSettings["HiatmeAiApiToken"];
            if (!string.IsNullOrWhiteSpace(tok))
                s.ApiToken = tok.Trim();
        }

        private static void ApplyEnvironmentOverrides(HiatmeAiSettings s)
        {
            var url = Environment.GetEnvironmentVariable("HIATME_AI_URL");
            if (!string.IsNullOrWhiteSpace(url))
                s.BaseUrl = url.Trim();
            var tok = Environment.GetEnvironmentVariable("HIATME_AI_TOKEN");
            if (!string.IsNullOrWhiteSpace(tok))
                s.ApiToken = tok.Trim();
        }

        public void Save()
        {
            File.WriteAllText(PersonalConfigPath, JsonConvert.SerializeObject(this, Formatting.Indented));
        }

        public string ResolvedClientId() =>
            string.IsNullOrWhiteSpace(ClientId) ? "hiatme-" + Environment.UserName : ClientId.Trim();
    }
}
