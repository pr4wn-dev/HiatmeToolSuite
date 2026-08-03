using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Hiatme_Tool_Suite_v3
{
    internal sealed class HiatmeAiSettings
    {
        public const int DefaultPort = 8787;
        private const int DefaultProbeTimeoutSeconds = 6;

        public string BaseUrl { get; set; } = "http://127.0.0.1:" + DefaultPort;
        public string ApiToken { get; set; } = "";
        public string ClientId { get; set; } = "";

        /// <summary>Last panel URL that answered (speeds up next launch).</summary>
        public string LastResolvedBaseUrl { get; set; } = "";

        /// <summary>Extra panel hosts (optional). Office LAN is auto-discovered when not configured.</summary>
        public List<string> FallbackBaseUrls { get; set; }

        public bool RememberOnSave { get; set; } = true;
        public bool UseServerGeo { get; set; } = true;
        public bool UseServerSolve { get; set; } = true;
        public bool AllowLocalSolveFallback { get; set; } = false;
        public bool UseWeekdayTemplates { get; set; } = true;
        public bool FinishRemainingAfterTemplates { get; set; } = true;

        private static string BaseDir => AppDomain.CurrentDomain.BaseDirectory;
        private static string PersonalConfigPath => Path.Combine(BaseDir, "hiatme_ai.json");
        private static string DefaultsConfigPath => Path.Combine(BaseDir, "hiatme_ai.defaults.json");

        private static readonly object LoadLock = new object();
        private static HiatmeAiSettings _sessionCache;
        private static string _lastConnectionDetail = "";
        private static bool? _sessionPanelReachable;

        /// <summary>Human-readable result from the last panel probe (for map overlay / status).</summary>
        public static string LastConnectionDetail => _lastConnectionDetail ?? "";

        /// <summary>Result of the panel probe during the current session's <see cref="Load"/>.</summary>
        internal static bool? SessionPanelReachable => _sessionPanelReachable;

        public static HiatmeAiSettings Load()
        {
            lock (LoadLock)
            {
                if (_sessionCache != null) return _sessionCache;
                _sessionCache = LoadAndConfigureLocked();
                return _sessionCache;
            }
        }

        public static void InvalidateSessionCache()
        {
            lock (LoadLock)
            {
                _sessionCache = null;
                _sessionPanelReachable = null;
            }
            HiatmeGeoSettings.Invalidate();
        }

        /// <summary>Clear session settings only (keep LAN discovery cache).</summary>
        internal static void InvalidateSessionCacheOnly()
        {
            lock (LoadLock)
            {
                _sessionCache = null;
                _sessionPanelReachable = null;
            }
        }

        /// <summary>Re-probe the current panel URL without blocking LAN scan.</summary>
        public static async Task<bool> RefreshPanelConnectionAsync(CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                HiatmeAiSettings settings;
                bool ok;
                lock (LoadLock)
                {
                    settings = _sessionCache ?? LoadAndConfigureLocked(forceResolve: false);
                    ok = ProbePanelPublic(settings.BaseUrl, settings.ApiToken);
                    _sessionPanelReachable = ok;
                }
                HiatmeGeoSettings.Configure(settings, ok);
                return ok;
            }, cancellationToken).ConfigureAwait(false);
        }

        private static HiatmeAiSettings LoadAndConfigureLocked(bool forceResolve = false)
        {
            var merged = LoadMerged();
            if (forceResolve || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HIATME_AI_URL")))
                merged.BaseUrl = ResolvePanelBaseUrl(merged, out _);
            HiatmeGeoSettings.Configure(merged, _sessionPanelReachable);
            return merged;
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

        internal static bool ProbePanelPublic(string baseUrl, string apiToken) =>
            ProbePanelDetailed(baseUrl, apiToken).Ok;

        /// <summary>Configured URLs first, localhost last — parallel probe, first win.</summary>
        private static string ResolvePanelBaseUrl(HiatmeAiSettings merged, out string detail)
        {
            var candidates = CollectPanelCandidateUrls(merged);
            if (candidates.Count == 0)
            {
                detail = "No office AI panel found on the network.";
                _lastConnectionDetail = detail;
                return "http://127.0.0.1:" + DefaultPort;
            }

            var probe = ProbeFirstReachable(candidates, merged.ApiToken);
            _sessionPanelReachable = probe.Winner.Ok;
            if (probe.Winner.Ok)
            {
                detail = "Connected: " + probe.Winner.Url;
                _lastConnectionDetail = detail;
                if (!string.Equals(probe.Winner.Url, merged.LastResolvedBaseUrl, StringComparison.OrdinalIgnoreCase))
                    PersistLastResolved(probe.Winner.Url);
                return probe.Winner.Url;
            }

            detail = BuildProbeFailureMessage(candidates, probe.Errors, merged.ApiToken);
            _lastConnectionDetail = detail;
            HiatmePanelLanDiscovery.DiscoverInBackground();
            return candidates[0];
        }

        internal static IReadOnlyList<string> CollectPanelCandidateUrls(HiatmeAiSettings merged)
        {
            var candidates = new List<string>();
            void add(string u)
            {
                u = NormalizeBaseUrl(u);
                if (string.IsNullOrEmpty(u)) return;
                if (!candidates.Any(c => string.Equals(c, u, StringComparison.OrdinalIgnoreCase)))
                    candidates.Add(u);
            }

            // Last good URL first — one quick probe on relaunch instead of timing out every fallback.
            add(merged.LastResolvedBaseUrl);
            add(merged.BaseUrl);
            if (merged.FallbackBaseUrls != null)
            {
                foreach (var u in merged.FallbackBaseUrls)
                    add(u);
            }

            var extra = Environment.GetEnvironmentVariable("HIATME_AI_URLS");
            if (!string.IsNullOrWhiteSpace(extra))
            {
                foreach (var part in extra.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                    add(part);
            }

            foreach (var u in HiatmePanelLanDiscovery.GetCachedUrls())
                add(u);

            add("http://127.0.0.1:" + DefaultPort);

            // A desk that once ran in the office keeps the office LAN IP as its last
            // good URL. Off-network that address is dead, or worse, some stranger's
            // device on the same 192.168.x range — either way it should not get first
            // crack at the probe budget ahead of a routable public URL.
            return candidates
                .OrderBy(u => IsLanUrl(u) && !IsOnThisMachinesSubnet(u) ? 1 : 0)
                .ToList();
        }

        /// <summary>True for loopback and RFC1918 hosts — addresses only reachable on a local network.</summary>
        private static bool IsLanUrl(string url)
        {
            try
            {
                var host = new Uri(url).Host;
                if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
                    return true;

                IPAddress ip;
                if (!IPAddress.TryParse(host, out ip))
                    return false; // a DDNS/hostname is assumed routable

                if (IPAddress.IsLoopback(ip))
                    return true;

                var b = ip.GetAddressBytes();
                if (b.Length != 4)
                    return false;
                if (b[0] == 10) return true;
                if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
                if (b[0] == 192 && b[1] == 168) return true;
                if (b[0] == 169 && b[1] == 254) return true;
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>True when the URL's host shares a /24 with one of this machine's own addresses.</summary>
        private static bool IsOnThisMachinesSubnet(string url)
        {
            try
            {
                var host = new Uri(url).Host;
                IPAddress ip;
                if (!IPAddress.TryParse(host, out ip))
                    return false;
                if (IPAddress.IsLoopback(ip))
                    return true;

                var want = ip.GetAddressBytes();
                if (want.Length != 4)
                    return false;

                foreach (var local in Dns.GetHostAddresses(Dns.GetHostName()))
                {
                    if (local.AddressFamily != AddressFamily.InterNetwork)
                        continue;
                    var have = local.GetAddressBytes();
                    if (have[0] == want[0] && have[1] == want[1] && have[2] == want[2])
                        return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private sealed class PanelProbeResult
        {
            public bool Ok { get; private set; }
            public string Url { get; private set; }
            public string Message { get; private set; }

            public static PanelProbeResult Success(string url) =>
                new PanelProbeResult { Ok = true, Url = url, Message = "" };

            public static PanelProbeResult Fail(string url, string message) =>
                new PanelProbeResult { Ok = false, Url = url, Message = message ?? "" };
        }

        private sealed class ParallelProbeResult
        {
            public PanelProbeResult Winner { get; set; }
            public List<string> Errors { get; set; } = new List<string>();
        }

        private static ParallelProbeResult ProbeFirstReachable(IReadOnlyList<string> urls, string apiToken)
        {
            var errors = new List<string>();
            if (urls == null || urls.Count == 0)
            {
                return new ParallelProbeResult
                {
                    Winner = PanelProbeResult.Fail("", "No panel URLs configured."),
                    Errors = errors,
                };
            }

            // Sequential with early exit — parallel timeouts on dead fallbacks spam TaskCanceledException in the debugger.
            for (int i = 0; i < urls.Count; i++)
            {
                string url = urls[i];
                // A LAN address that is really there answers in milliseconds, so those
                // can be rushed. A WAN round trip needs the full budget — cutting a
                // public URL to 2s just because something else was tried first is what
                // made off-network desks fail while the panel was up and reachable.
                bool quick = i > 0 && IsLanUrl(url);
                var result = ProbePanelDetailed(url, apiToken, quickTimeout: quick);
                if (result.Ok)
                {
                    return new ParallelProbeResult { Winner = result, Errors = errors };
                }

                if (!string.IsNullOrWhiteSpace(result.Message))
                    errors.Add(result.Message);
            }

            return new ParallelProbeResult
            {
                Winner = PanelProbeResult.Fail(urls[0], "All panel URLs failed."),
                Errors = errors,
            };
        }

        private static PanelProbeResult ProbePanelDetailed(string baseUrl, string apiToken, bool quickTimeout = false)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                return PanelProbeResult.Fail(baseUrl, "Empty panel URL.");

            string url = baseUrl.TrimEnd('/') + "/api/hiatme/geo/status";
            int timeoutSec = quickTimeout ? 2 : DefaultProbeTimeoutSeconds;
            var rawTimeout = Environment.GetEnvironmentVariable("HIATME_AI_PROBE_TIMEOUT_SEC");
            if (!string.IsNullOrWhiteSpace(rawTimeout) && int.TryParse(rawTimeout.Trim(), out var t) && t >= 2 && t <= 30)
                timeoutSec = t;

            try
            {
                using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSec) })
                using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    if (IsUsableApiToken(apiToken))
                        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken.Trim());

                    using (var resp = http.SendAsync(req).GetAwaiter().GetResult())
                    {
                        if (resp.IsSuccessStatusCode)
                            return PanelProbeResult.Success(NormalizeBaseUrl(baseUrl));

                        if (resp.StatusCode == HttpStatusCode.Unauthorized || resp.StatusCode == HttpStatusCode.Forbidden)
                        {
                            return PanelProbeResult.Fail(
                                baseUrl,
                                baseUrl + ": invalid API token (HTTP " + (int)resp.StatusCode + "). "
                                + "ApiToken in hiatme_ai.defaults.json must match HIATME_API_TOKEN on the server.");
                        }

                        return PanelProbeResult.Fail(
                            baseUrl,
                            baseUrl + ": HTTP " + (int)resp.StatusCode + " " + resp.ReasonPhrase);
                    }
                }
            }
            catch (TaskCanceledException)
            {
                return PanelProbeResult.Fail(baseUrl, baseUrl + ": timed out after " + timeoutSec + "s.");
            }
            catch (OperationCanceledException)
            {
                return PanelProbeResult.Fail(baseUrl, baseUrl + ": cancelled.");
            }
            catch (HttpRequestException ex)
            {
                return PanelProbeResult.Fail(baseUrl, baseUrl + ": " + FlattenExceptionMessage(ex));
            }
            catch (Exception ex)
            {
                return PanelProbeResult.Fail(baseUrl, baseUrl + ": " + ex.Message);
            }
        }

        private static string BuildProbeFailureMessage(
            IReadOnlyList<string> candidates,
            List<string> errors,
            string apiToken)
        {
            var lines = new List<string>
            {
                "Could not find the office AI panel on your network.",
                "",
                "Checked:",
            };
            foreach (var c in candidates)
                lines.Add("  • " + c);
            lines.Add("");
            if (errors.Count > 0)
            {
                lines.Add("Details:");
                foreach (var e in errors.Distinct().Take(6))
                    lines.Add("  • " + e);
                lines.Add("");
            }
            lines.Add("On the server PC:");
            lines.Add("  • Start the AI panel (scripts\\restart-panel.ps1 in AIagent)");
            lines.Add("  • Docker OSRM running (tools\\osrm\\scripts\\start-osrm.ps1)");
            lines.Add("  • Port forward TCP 8787 if connecting from outside the LAN (see docs\\DEPLOYMENT.md)");
            lines.Add("  • Windows firewall allows inbound TCP 8787 on the server");
            lines.Add("");
            lines.Add("Desks: set PublicPanelUrl (DDNS/public IP) in setup-desk.ps1, plus office/home LAN fallbacks.");
            if (!IsUsableApiToken(apiToken))
            {
                lines.Add(
                    "This desk has no usable ApiToken. On the office LAN the server lets that "
                    + "pass, but from anywhere else every request is rejected — copy ApiToken "
                    + "from the server's hiatme_ai.defaults.json into this desk's copy.");
            }
            return string.Join("\r\n", lines);
        }

        private static string FlattenExceptionMessage(Exception ex)
        {
            if (ex == null) return "network error";
            var inner = ex.InnerException;
            if (inner != null && !string.IsNullOrWhiteSpace(inner.Message))
                return inner.Message;
            return ex.Message;
        }

        private static bool IsUsableApiToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return false;
            var t = token.Trim();
            if (t.Length < 8) return false;
            if (t.IndexOf("copy-from", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (t.IndexOf("change-me", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (string.Equals(t, "YOUR_TOKEN", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
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
                var jo = JObject.Parse(File.ReadAllText(path));
                if (jo["BaseUrl"] != null && !string.IsNullOrWhiteSpace(jo["BaseUrl"].ToString()))
                    target.BaseUrl = jo["BaseUrl"].ToString().Trim();
                if (jo["ApiToken"] != null && !string.IsNullOrWhiteSpace(jo["ApiToken"].ToString()))
                    target.ApiToken = jo["ApiToken"].ToString().Trim();
                if (jo["ClientId"] != null && !string.IsNullOrWhiteSpace(jo["ClientId"].ToString()))
                    target.ClientId = jo["ClientId"].ToString().Trim();
                if (jo["LastResolvedBaseUrl"] != null && !string.IsNullOrWhiteSpace(jo["LastResolvedBaseUrl"].ToString()))
                    target.LastResolvedBaseUrl = jo["LastResolvedBaseUrl"].ToString().Trim();
                if (jo["FallbackBaseUrls"] is JArray fb && fb.Count > 0)
                    target.FallbackBaseUrls = fb.ToObject<List<string>>();
                if (jo["RememberOnSave"] != null)
                    target.RememberOnSave = jo["RememberOnSave"].Value<bool>();
                if (jo["UseServerGeo"] != null)
                    target.UseServerGeo = jo["UseServerGeo"].Value<bool>();
                if (jo["UseServerSolve"] != null)
                    target.UseServerSolve = jo["UseServerSolve"].Value<bool>();
                if (jo["AllowLocalSolveFallback"] != null)
                    target.AllowLocalSolveFallback = jo["AllowLocalSolveFallback"].Value<bool>();
                if (jo["UseWeekdayTemplates"] != null)
                    target.UseWeekdayTemplates = jo["UseWeekdayTemplates"].Value<bool>();
                if (jo["FinishRemainingAfterTemplates"] != null)
                    target.FinishRemainingAfterTemplates = jo["FinishRemainingAfterTemplates"].Value<bool>();
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
            var solve = Environment.GetEnvironmentVariable("HIATME_USE_SERVER_SOLVE");
            if (!string.IsNullOrWhiteSpace(solve))
            {
                var v = solve.Trim().ToLowerInvariant();
                s.UseServerSolve = v != "0" && v != "false" && v != "no" && v != "off";
            }
            var localFb = Environment.GetEnvironmentVariable("HIATME_ALLOW_LOCAL_SOLVE_FALLBACK");
            if (!string.IsNullOrWhiteSpace(localFb))
            {
                var v = localFb.Trim().ToLowerInvariant();
                s.AllowLocalSolveFallback = v == "1" || v == "true" || v == "yes" || v == "on";
            }
            var geo = Environment.GetEnvironmentVariable("HIATME_USE_SERVER_GEO");
            if (!string.IsNullOrWhiteSpace(geo))
            {
                var v = geo.Trim().ToLowerInvariant();
                s.UseServerGeo = v != "0" && v != "false" && v != "no" && v != "off";
            }
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
