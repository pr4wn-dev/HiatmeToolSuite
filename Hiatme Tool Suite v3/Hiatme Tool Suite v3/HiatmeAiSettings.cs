using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
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

        /// <summary>Office LAN panel — always tried from desks on the same network.</summary>
        public const string BuiltInOfficePanelUrl = "http://192.168.1.4:8787";

        /// <summary>Current public / port-forwarded panel (updates when ISP IP changes).</summary>
        public const string BuiltInPublicPanelUrl = "http://72.71.232.164:8787";

        /// <summary>Retired public IPs that must never win the probe race.</summary>
        private static readonly string[] DeadPanelHosts =
        {
            "24.59.64.222",
        };

        public string BaseUrl { get; set; } = BuiltInOfficePanelUrl;
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
        private static readonly object RefreshGate = new object();
        private static HiatmeAiSettings _sessionCache;
        private static string _lastConnectionDetail = "";
        private static bool? _sessionPanelReachable;
        private static DateTime _lastGoodProbeUtc = DateTime.MinValue;
        private static Task<bool> _refreshTask;
        private static List<byte[]> _localIpv4;

        /// <summary>Human-readable result from the last panel probe (for map overlay / status).</summary>
        public static string LastConnectionDetail => _lastConnectionDetail ?? "";

        /// <summary>Append one line to bin\ai-probe.log. "Panel offline" while the panel is
        /// demonstrably up is otherwise impossible to tell apart from a token or timeout miss.</summary>
        internal static void LogProbe(string line)
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(BaseDir, "ai-probe.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + "  " + (line ?? "") + Environment.NewLine);
            }
            catch { }
        }

        /// <summary>Result of the panel probe during the current session's <see cref="Load"/>.</summary>
        internal static bool? SessionPanelReachable => _sessionPanelReachable;

        public static HiatmeAiSettings Load()
        {
            lock (LoadLock)
            {
                if (_sessionCache != null) return _sessionCache;
            }
            HiatmeAiSettings built;
            using (UiStallWatch.Measure(UiScope.SettingsResolve))
                built = LoadAndConfigureUnlocked();
            lock (LoadLock)
            {
                if (_sessionCache != null) return _sessionCache;
                _sessionCache = built;
                return _sessionCache;
            }
        }

        private static HiatmeAiSettings _unresolvedCache;
        private static int _warming;

        /// <summary>
        /// Settings read that is safe on the UI thread: it never touches the network.
        ///
        /// <see cref="Load"/> on a cold cache walks the panel candidate list with synchronous
        /// 2–6s timeouts each (plus a DNS lookup), so calling it from a UI timer freezes the
        /// window for seconds. That is what desks away from the server PC felt every time the
        /// panel restarted: a background LAN discovery clears the session cache, then the next
        /// poll tick re-probes on the UI thread. The server PC never noticed because its first
        /// candidate is itself and always answers.
        ///
        /// Returns resolved settings when they are already warm, otherwise the file/env values
        /// as-is (correct URL in the normal case) and warms the real resolve off-thread.
        /// </summary>
        public static HiatmeAiSettings LoadNoProbe()
        {
            HiatmeAiSettings snap;
            lock (LoadLock)
            {
                if (_sessionCache != null) return _sessionCache;
                if (_unresolvedCache == null)
                    _unresolvedCache = LoadMerged();
                snap = _unresolvedCache;
            }
            WarmInBackground();
            return snap;
        }

        private static void WarmInBackground()
        {
            if (Interlocked.CompareExchange(ref _warming, 1, 0) != 0) return;
            _ = Task.Run(() =>
            {
                try { Load(); }
                catch { }
                finally { Interlocked.Exchange(ref _warming, 0); }
            });
        }

        public static void InvalidateSessionCache()
        {
            lock (LoadLock)
            {
                _sessionCache = null;
                _unresolvedCache = null;
                _sessionPanelReachable = null;
                _lastGoodProbeUtc = DateTime.MinValue;
            }
            HiatmeGeoSettings.Invalidate();
        }

        /// <summary>Clear session settings only (keep LAN discovery cache).</summary>
        internal static void InvalidateSessionCacheOnly()
        {
            lock (LoadLock)
            {
                _sessionCache = null;
                _unresolvedCache = null;
                _sessionPanelReachable = null;
                _lastGoodProbeUtc = DateTime.MinValue;
            }
            // Re-resolve here, off-thread, instead of leaving the next caller to do it. This
            // runs from background LAN discovery, and the next caller is usually a UI timer.
            WarmInBackground();
        }

        /// <summary>Re-probe panel URLs. Loopback first so the office server
        /// does not hairpin the public WAN IP and look "offline".</summary>
        public static Task<bool> RefreshPanelConnectionAsync(CancellationToken cancellationToken = default)
        {
            lock (LoadLock)
            {
                if (_sessionPanelReachable == true
                    && _sessionCache != null
                    && DateTime.UtcNow - _lastGoodProbeUtc < TimeSpan.FromSeconds(45)
                    && !cancellationToken.IsCancellationRequested)
                {
                    LogProbe("RefreshPanelConnectionAsync: reuse " + _sessionCache.BaseUrl
                        + " (probed " + (int)(DateTime.UtcNow - _lastGoodProbeUtc).TotalSeconds + "s ago)");
                    return Task.FromResult(true);
                }
            }
            lock (RefreshGate)
            {
                if (_refreshTask != null && !_refreshTask.IsCompleted)
                    return _refreshTask;
                LogProbe("RefreshPanelConnectionAsync: enter");
                _refreshTask = Task.Run(() => RefreshPanelConnectionCore(cancellationToken));
                return _refreshTask;
            }
        }

        private static bool RefreshPanelConnectionCore(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var settings = LoadMerged();
            string token = settings.ApiToken;
            bool ok;
            string resolveDetail = "";
            PanelProbeResult probe = PanelProbeResult.Fail("", "");
            string how = "";

            string last = NormalizeBaseUrl(settings.LastResolvedBaseUrl);
            if (!string.IsNullOrEmpty(last) && !IsLanUrl(last) && !IsLoopbackUrl(last))
            {
                probe = ProbePanelDetailed(last, token, quickTimeout: false);
                if (probe.Ok)
                {
                    settings.BaseUrl = probe.Url;
                    ok = true;
                    how = "last-resolved " + last;
                    ApplyRefreshResult(settings, ok, probe, resolveDetail, how);
                    return true;
                }
            }

            string loopbackUrl = "http://127.0.0.1:" + DefaultPort;
            probe = ProbePanelDetailed(loopbackUrl, token, quickTimeout: true);
            if (probe.Ok)
            {
                settings.BaseUrl = probe.Url;
                ok = true;
                how = "loopback";
            }
            else
            {
                settings.BaseUrl = ResolvePanelBaseUrl(settings, out resolveDetail);
                ok = _sessionPanelReachable == true;
                how = "loopback failed (" + probe.Message + "); resolved=" + settings.BaseUrl
                    + " detail=" + resolveDetail;
            }

            ApplyRefreshResult(settings, ok, probe, resolveDetail, how);
            return ok;
        }

        private static void ApplyRefreshResult(
            HiatmeAiSettings settings, bool ok, PanelProbeResult probe, string resolveDetail, string how)
        {
            lock (LoadLock)
            {
                _sessionPanelReachable = ok;
                if (ok) _lastGoodProbeUtc = DateTime.UtcNow;
                _lastConnectionDetail = ok
                    ? ("Connected: " + settings.BaseUrl)
                    : (string.IsNullOrWhiteSpace(probe.Message)
                        ? ("Unreachable: " + settings.BaseUrl)
                        : probe.Message);
                if (!ok && !string.IsNullOrWhiteSpace(resolveDetail))
                    _lastConnectionDetail = resolveDetail;
                _sessionCache = settings;
            }
            HiatmeGeoSettings.Configure(settings, ok);
            LogProbe("RefreshPanelConnectionAsync: " + how + " ok=" + ok + " base=" + settings.BaseUrl);
        }

        private static HiatmeAiSettings LoadAndConfigureUnlocked(bool forceResolve = false)
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
            SanitizePanelUrls(merged);
            return merged;
        }

        /// <summary>
        /// Drop retired public IPs, inject current office + public hosts, and rewrite
        /// personal config so desks stop pinning a dead address after an ISP change.
        /// </summary>
        private static void SanitizePanelUrls(HiatmeAiSettings merged)
        {
            if (merged == null) return;

            bool dirty = false;
            string scrub(string url)
            {
                url = NormalizeBaseUrl(url);
                if (string.IsNullOrEmpty(url)) return "";
                if (!IsDeadPanelUrl(url)) return url;
                dirty = true;
                return "";
            }

            merged.BaseUrl = scrub(merged.BaseUrl);
            merged.LastResolvedBaseUrl = scrub(merged.LastResolvedBaseUrl);

            var fallbacks = new List<string>();
            void addFb(string u)
            {
                u = scrub(u);
                if (string.IsNullOrEmpty(u)) return;
                if (!fallbacks.Any(x => string.Equals(x, u, StringComparison.OrdinalIgnoreCase)))
                    fallbacks.Add(u);
            }

            if (merged.FallbackBaseUrls != null)
            {
                foreach (var u in merged.FallbackBaseUrls)
                    addFb(u);
            }
            addFb(BuiltInPublicPanelUrl);
            addFb(BuiltInOfficePanelUrl);
            addFb("http://127.0.0.1:" + DefaultPort);
            merged.FallbackBaseUrls = fallbacks;

            if (string.IsNullOrWhiteSpace(merged.BaseUrl))
            {
                merged.BaseUrl = BuiltInOfficePanelUrl;
                dirty = true;
            }

            if (dirty || string.IsNullOrWhiteSpace(merged.LastResolvedBaseUrl))
            {
                try
                {
                    // Keep a working office URL as last-resolved when we just scrubbed a dead public IP.
                    if (string.IsNullOrWhiteSpace(merged.LastResolvedBaseUrl))
                        merged.LastResolvedBaseUrl = BuiltInOfficePanelUrl;
                    PersistSanitizedUrls(merged);
                }
                catch { }
            }
        }

        private static bool IsDeadPanelUrl(string url)
        {
            try
            {
                var host = new Uri(url).Host;
                foreach (var dead in DeadPanelHosts)
                {
                    if (string.Equals(host, dead, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch { }
            return false;
        }

        private static void PersistSanitizedUrls(HiatmeAiSettings settings)
        {
            string path = File.Exists(PersonalConfigPath) ? PersonalConfigPath : DefaultsConfigPath;
            JObject jobj;
            if (File.Exists(path))
            {
                try { jobj = JObject.Parse(File.ReadAllText(path)); }
                catch { jobj = new JObject(); }
            }
            else
            {
                jobj = new JObject();
            }

            jobj["BaseUrl"] = settings.BaseUrl ?? BuiltInOfficePanelUrl;
            jobj["LastResolvedBaseUrl"] = settings.LastResolvedBaseUrl ?? BuiltInOfficePanelUrl;
            jobj["FallbackBaseUrls"] = JArray.FromObject(
                settings.FallbackBaseUrls ?? new List<string> { BuiltInPublicPanelUrl });
            File.WriteAllText(path, jobj.ToString(Formatting.Indented));
            LogProbe("SanitizePanelUrls: rewrote " + path);
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
            // Keep the office URL so the next click retries WAN/LAN. Pinning
            // 127.0.0.1 makes every other desk look "offline" on their own PC.
            return NormalizeBaseUrl(merged.LastResolvedBaseUrl)
                ?? NormalizeBaseUrl(merged.BaseUrl)
                ?? ("http://127.0.0.1:" + DefaultPort);
        }

        internal static IReadOnlyList<string> CollectPanelCandidateUrls(HiatmeAiSettings merged)
        {
            var candidates = new List<string>();
            void add(string u)
            {
                u = NormalizeBaseUrl(u);
                if (string.IsNullOrEmpty(u)) return;
                if (IsDeadPanelUrl(u)) return;
                if (!candidates.Any(c => string.Equals(c, u, StringComparison.OrdinalIgnoreCase)))
                    candidates.Add(u);
            }

            // Same-subnet office LAN first so site desks never wait on a public IP.
            add(BuiltInOfficePanelUrl);
            add(merged.LastResolvedBaseUrl);
            add(merged.BaseUrl);
            if (merged.FallbackBaseUrls != null)
            {
                foreach (var u in merged.FallbackBaseUrls)
                    add(u);
            }

            add(BuiltInPublicPanelUrl);

            var extra = Environment.GetEnvironmentVariable("HIATME_AI_URLS");
            if (!string.IsNullOrWhiteSpace(extra))
            {
                foreach (var part in extra.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                    add(part);
            }

            foreach (var u in HiatmePanelLanDiscovery.GetCachedUrls())
                add(u);

            add("http://127.0.0.1:" + DefaultPort);

            // Last-known-good first (usually the public IP on desks). Same-subnet LAN
            // next. Foreign 192.168.x addresses (home router vs office LAN collision)
            // are last and get a 400ms budget — never 6s.
            return candidates
                .OrderBy(u =>
                {
                    if (!string.IsNullOrEmpty(merged.LastResolvedBaseUrl)
                        && string.Equals(u, NormalizeBaseUrl(merged.LastResolvedBaseUrl),
                            StringComparison.OrdinalIgnoreCase))
                        return 0;
                    if (IsLoopbackUrl(u)) return 4;
                    if (IsLanUrl(u) && IsOnThisMachinesSubnet(u)) return 2;
                    if (IsLanUrl(u)) return 3;
                    return 1;
                })
                .ToList();
        }

        private static bool IsLoopbackUrl(string url)
        {
            try
            {
                var host = new Uri(url).Host;
                if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
                    return true;
                IPAddress ip;
                return IPAddress.TryParse(host, out ip) && IPAddress.IsLoopback(ip);
            }
            catch
            {
                return false;
            }
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

                foreach (var have in LocalIpv4Bytes())
                {
                    if (have == null || have.Length != 4)
                        continue;
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

        private static List<byte[]> LocalIpv4Bytes()
        {
            if (_localIpv4 != null) return _localIpv4;
            var list = new List<byte[]>();
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        if (IPAddress.IsLoopback(ua.Address)) continue;
                        list.Add(ua.Address.GetAddressBytes());
                    }
                }
            }
            catch { }
            _localIpv4 = list;
            return _localIpv4;
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

            // Sequential with early exit. LAN/loopback get 400ms — an office panel on
            // the real LAN answers in milliseconds; a home-router 192.168.1.4 that is
            // not the office used to burn 6s per miss and freeze the window.
            for (int i = 0; i < urls.Count; i++)
            {
                string url = urls[i];
                bool quick = IsLanUrl(url) || IsLoopbackUrl(url);
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

            // /api/status is public. "Offline" must mean the process is down — not a
            // geo/token miss, and not an HttpClient deadlock on the WinForms UI thread.
            string url = baseUrl.TrimEnd('/') + "/api/status";
            int timeoutMs = DefaultProbeTimeoutSeconds * 1000;
            if (quickTimeout || IsLanUrl(baseUrl) || IsLoopbackUrl(baseUrl))
                timeoutMs = 400;
            var rawTimeout = Environment.GetEnvironmentVariable("HIATME_AI_PROBE_TIMEOUT_SEC");
            if (!string.IsNullOrWhiteSpace(rawTimeout) && int.TryParse(rawTimeout.Trim(), out var t) && t >= 2 && t <= 30)
                timeoutMs = t * 1000;

            var probeClock = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.Timeout = timeoutMs;
                req.ReadWriteTimeout = timeoutMs;
                req.Proxy = null;
                req.KeepAlive = false;
                if (req.ServicePoint != null && req.ServicePoint.ConnectionLimit < 16)
                    req.ServicePoint.ConnectionLimit = 16;
                req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                if (IsUsableApiToken(apiToken))
                    req.Headers[HttpRequestHeader.Authorization] = "Bearer " + apiToken.Trim();

                using (var resp = (HttpWebResponse)req.GetResponse())
                {
                    int code = (int)resp.StatusCode;
                    LogProbe("probe " + url + " -> HTTP " + code + " in " + probeClock.ElapsedMilliseconds
                        + "ms (timeout " + timeoutMs + "ms)");
                    if (code >= 200 && code < 300)
                        return PanelProbeResult.Success(NormalizeBaseUrl(baseUrl));
                    return PanelProbeResult.Fail(baseUrl, baseUrl + ": HTTP " + code);
                }
            }
            catch (WebException ex)
            {
                LogProbe("probe " + url + " -> WebException " + ex.Status + " in " + probeClock.ElapsedMilliseconds
                    + "ms (timeout " + timeoutMs + "ms): " + ex.Message);
                var http = ex.Response as HttpWebResponse;
                if (http != null)
                {
                    int code = (int)http.StatusCode;
                    if (code == 401 || code == 403)
                    {
                        return PanelProbeResult.Fail(
                            baseUrl,
                            baseUrl + ": invalid API token (HTTP " + code + "). "
                            + "ApiToken in hiatme_ai.defaults.json must match HIATME_API_TOKEN on the server.");
                    }
                    return PanelProbeResult.Fail(baseUrl, baseUrl + ": HTTP " + code);
                }
                if (ex.Status == WebExceptionStatus.Timeout)
                    return PanelProbeResult.Fail(baseUrl, baseUrl + ": timed out after " + timeoutMs + "ms.");
                return PanelProbeResult.Fail(baseUrl, baseUrl + ": " + (ex.Message ?? "network error"));
            }
            catch (Exception ex)
            {
                LogProbe("probe " + url + " -> " + ex.GetType().Name + " in " + probeClock.ElapsedMilliseconds
                    + "ms: " + ex.Message);
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
