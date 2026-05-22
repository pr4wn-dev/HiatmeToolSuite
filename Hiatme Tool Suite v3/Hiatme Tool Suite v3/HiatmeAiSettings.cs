using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Hiatme_Tool_Suite_v3
{
    internal sealed class HiatmeAiSettings
    {
        public const int DefaultPort = 8787;

        public string BaseUrl { get; set; } = "http://127.0.0.1:" + DefaultPort;
        public string ApiToken { get; set; } = "";
        public string ClientId { get; set; } = "";

        /// <summary>Last panel URL that answered (speeds up next launch).</summary>
        public string LastResolvedBaseUrl { get; set; } = "";

        /// <summary>Optional extra panel hosts to try (office LAN IP, etc.).</summary>
        public List<string> FallbackBaseUrls { get; set; }

        /// <summary>After SAVE workbook, store a short approval note for memory.</summary>
        public bool RememberOnSave { get; set; } = true;

        /// <summary>
        /// When true (default), geocode and OSRM use only the office AI panel — no public/demo routing.
        /// Set false only for local dev without the server.
        /// </summary>
        public bool UseServerGeo { get; set; } = true;

        private static string BaseDir => AppDomain.CurrentDomain.BaseDirectory;
        private static string PersonalConfigPath => Path.Combine(BaseDir, "hiatme_ai.json");
        private static string DefaultsConfigPath => Path.Combine(BaseDir, "hiatme_ai.defaults.json");

        private static readonly object LoadLock = new object();
        private static HiatmeAiSettings _sessionCache;
        private static readonly HttpClient ProbeHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(2.5) };

        public static HiatmeAiSettings Load()
        {
            lock (LoadLock)
            {
                if (_sessionCache != null) return _sessionCache;
                var merged = LoadMerged();
                if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HIATME_AI_URL")))
                    merged.BaseUrl = ResolvePanelBaseUrl(merged);
                HiatmeGeoSettings.Configure(merged);
                _sessionCache = merged;
                return merged;
            }
        }

        public static void InvalidateSessionCache()
        {
            lock (LoadLock) { _sessionCache = null; }
            HiatmeGeoSettings.Invalidate();
        }

        private static HiatmeAiSettings LoadMerged()
        {
            var merged = new HiatmeAiSettings();
            TryMergeFile(merged, DefaultsConfigPath);
            TryMergeFile(merged, PersonalConfigPath);
            ApplyAppConfigOverrides(merged);
            ApplyEnvironmentOverrides(merged);
            return merged;
        }

        /// <summary>Try localhost, last-good, configured URL — no manual switching.</summary>
        private static string ResolvePanelBaseUrl(HiatmeAiSettings merged)
        {
            var candidates = new List<string>();
            void add(string u)
            {
                u = NormalizeBaseUrl(u);
                if (string.IsNullOrEmpty(u)) return;
                if (!candidates.Any(c => string.Equals(c, u, StringComparison.OrdinalIgnoreCase)))
                    candidates.Add(u);
            }

            add("http://127.0.0.1:" + DefaultPort);
            add(merged.LastResolvedBaseUrl);
            add(merged.BaseUrl);
            if (merged.FallbackBaseUrls != null)
            {
                foreach (var u in merged.FallbackBaseUrls)
                    add(u);
            }

            string winner = null;
            foreach (var url in candidates)
            {
                if (!ProbePanel(url, merged.ApiToken)) continue;
                winner = url;
                break;
            }

            if (string.IsNullOrEmpty(winner))
                winner = NormalizeBaseUrl(merged.BaseUrl) ?? ("http://127.0.0.1:" + DefaultPort);

            if (!string.Equals(winner, merged.LastResolvedBaseUrl, StringComparison.OrdinalIgnoreCase))
                PersistLastResolved(winner);

            return winner;
        }

        internal static bool ProbePanelPublic(string baseUrl, string apiToken) =>
            ProbePanel(baseUrl, apiToken);

        private static bool ProbePanel(string baseUrl, string apiToken)
        {
            if (string.IsNullOrWhiteSpace(baseUrl)) return false;
            string url = baseUrl.TrimEnd('/') + "/api/hiatme/geo/status";
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    if (!string.IsNullOrWhiteSpace(apiToken))
                        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken.Trim());
                    using (var resp = ProbeHttp.SendAsync(req).GetAwaiter().GetResult())
                    {
                        return resp.IsSuccessStatusCode;
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        private static void PersistLastResolved(string url)
        {
            try
            {
                JObject jobj;
                if (File.Exists(PersonalConfigPath))
                    jobj = JObject.Parse(File.ReadAllText(PersonalConfigPath));
                else
                    jobj = new JObject();
                jobj["LastResolvedBaseUrl"] = url;
                File.WriteAllText(PersonalConfigPath, jobj.ToString(Formatting.Indented));
            }
            catch { }
        }

        private static string NormalizeBaseUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            url = url.Trim().TrimEnd('/');
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;
            return uri.GetLeftPart(UriPartial.Authority);
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
                if (!string.IsNullOrWhiteSpace(part.LastResolvedBaseUrl))
                    target.LastResolvedBaseUrl = part.LastResolvedBaseUrl.Trim();
                if (part.FallbackBaseUrls != null && part.FallbackBaseUrls.Count > 0)
                    target.FallbackBaseUrls = part.FallbackBaseUrls;
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
            InvalidateSessionCache();
        }

        public string ResolvedClientId() =>
            string.IsNullOrWhiteSpace(ClientId) ? "hiatme-" + Environment.UserName : ClientId.Trim();
    }
}
