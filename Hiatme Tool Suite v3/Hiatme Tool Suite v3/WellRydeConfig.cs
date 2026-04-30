using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using Hiatme_Tool_Suite_v3.Properties;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Optional <c>App.config</c> <c>appSettings</c> matching PHP <c>Hiatme-PHP-Website/.env</c> and <c>docs/WELLRYDE.md</c>.
    /// </summary>
    internal static class WellRydeConfig
    {
        private const string DefaultPortalBase = "https://portal.app.wellryde.com";
        private const string DefaultTripsPath = "/portal/nu";
        private const string DefaultLoginPath = "/portal/login";
        private const string DefaultListDefId = "SEC-J0JwBzGuni0ZopMPBRCNuQ";

        /// <summary>PHP has no separate base override; reserved if the portal hostname ever changes.</summary>
        public static string PortalBaseUrl => ReadAppSetting("WellRydePortalBaseUrl", DefaultPortalBase);

        /// <summary>PHP <c>WELLRYDE_TRIPS_PATH</c> — SPA shell for CSRF and <c>filterdata</c> Referer.</summary>
        public static string TripsPath => NormalizePath(ReadAppSetting("WellRydeTripsPath", DefaultTripsPath), DefaultTripsPath);

        /// <summary>PHP <c>WELLRYDE_LOGIN_PATH</c> — documented for parity; login HTML flow may still use <see cref="TripsPageAbsoluteUrl"/>.</summary>
        public static string LoginPath => NormalizePath(ReadAppSetting("WellRydeLoginPath", DefaultLoginPath), DefaultLoginPath);

        /// <summary>PHP <c>WELLRYDE_LIST_DEF_ID</c> — trips <c>filterdata</c> <c>listDefId</c> when explicitly set in config (non-empty).</summary>
        public static string ListDefId
        {
            get
            {
                var v = ReadAppSetting("WellRydeListDefId", DefaultListDefId);
                return string.IsNullOrWhiteSpace(v) ? DefaultListDefId : v.Trim();
            }
        }

        /// <summary>True when <c>WellRydeListDefId</c> is set to a non-whitespace value (not “use default / auto-resolve”).</summary>
        public static bool HasExplicitTripListDefId
        {
            get
            {
                try
                {
                    var v = ConfigurationManager.AppSettings["WellRydeListDefId"];
                    return !string.IsNullOrWhiteSpace(v);
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>Set after GET <c>/portal/trip/filterlist?list_name=…</c> when config leaves list id open.</summary>
        public static string ResolvedTripListDefId { get; set; }

        /// <summary>
        /// Effective <c>listDefId</c> for <c>POST /portal/filterdata</c>: explicit config wins; else <see cref="ResolvedTripListDefId"/> from filterlist API;
        /// else legacy <see cref="DefaultListDefId"/>.
        /// </summary>
        public static string TripFilterListDefId
        {
            get
            {
                if (HasExplicitTripListDefId)
                {
                    try
                    {
                        return ConfigurationManager.AppSettings["WellRydeListDefId"].Trim();
                    }
                    catch
                    {
                        return DefaultListDefId;
                    }
                }
                if (!string.IsNullOrWhiteSpace(ResolvedTripListDefId))
                    return ResolvedTripListDefId.Trim();
                return DefaultListDefId;
            }
        }

        /// <summary>Query name for <c>/portal/trip/filterlist</c> (Modivcare: VTripBilling).</summary>
        public static string TripFilterListName => ReadAppSetting("WellRydeTripFilterListName", "VTripBilling");

        /// <summary>
        /// When <c>true</c> (default if App.config omits the key), use <c>/portal//trip/filterlist</c> first — matches Chrome 2026 HAR on portal.app.
        /// Set <c>WellRydeTripFilterListDoubleSlash=false</c> (or <c>0</c>) to prefer single slash <c>/portal/trip/filterlist</c> first for tenants that return HTTP 401 on the doubled path.
        /// </summary>
        public static bool TripFilterListDoubleSlashBeforeTrip
        {
            get
            {
                var v = ReadAppSetting("WellRydeTripFilterListDoubleSlash", "");
                if (string.IsNullOrWhiteSpace(v))
                    return true;
                if (string.Equals(v, "false", StringComparison.OrdinalIgnoreCase) || v == "0")
                    return false;
                return string.Equals(v, "true", StringComparison.OrdinalIgnoreCase) || v == "1";
            }
        }

        /// <summary>Full <c>GET</c> URL for trip filter list metadata (primes servlet session before <c>filterdata</c>).</summary>
        public static string TripFilterListRequestUrl =>
            PortalOrigin
            + (TripFilterListDoubleSlashBeforeTrip ? "/portal//trip/filterlist" : "/portal/trip/filterlist")
            + "?list_name=" + Uri.EscapeDataString(TripFilterListName);

        /// <summary>
        /// Yields the double-slash URL first when <see cref="TripFilterListDoubleSlashBeforeTrip"/> is true (default), else single-slash first. Some tenants return HTTP 401 on one path and succeed on the other.
        /// </summary>
        public static IEnumerable<string> EnumerateTripFilterListRequestUrls()
        {
            var q = "?list_name=" + Uri.EscapeDataString(TripFilterListName);
            var single = PortalOrigin + "/portal/trip/filterlist" + q;
            var dbl = PortalOrigin + "/portal//trip/filterlist" + q;
            if (TripFilterListDoubleSlashBeforeTrip)
            {
                yield return dbl;
                yield return single;
            }
            else
            {
                yield return single;
                yield return dbl;
            }
        }

        /// <summary>If false, skip auto-resolve when <c>WellRydeListDefId</c> is unset. Default on.</summary>
        public static bool AutoResolveTripFilterListDef
        {
            get
            {
                var v = ReadAppSetting("WellRydeAutoResolveTripFilterList", "");
                if (string.IsNullOrWhiteSpace(v))
                    return true;
                return !string.Equals(v, "false", StringComparison.OrdinalIgnoreCase) && v != "0";
            }
        }

        /// <summary>
        /// <c>maxResult</c> / <c>defaultSize</c> for trip <c>filterdata</c>. Default 200 (typical Chrome HAR); public GitHub <c>main</c> used 500 — set <c>WellRydeFilterDataPageSize=500</c> to match.
        /// </summary>
        public static int FilterDataPageSize
        {
            get
            {
                var v = ReadAppSetting("WellRydeFilterDataPageSize", "");
                if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= 1 && n <= 2000)
                    return n;
                return 200;
            }
        }

        /// <summary>PHP <c>WELLRYDE_USERS_LIST_DEF_ID</c> — users list via <c>filterdata</c> on the website; optional in this app.</summary>
        public static string UsersListDefId => ReadAppSetting("WellRydeUsersListDefId", "").Trim();

        /// <summary>No trailing slash — use as <c>Origin</c>.</summary>
        public static string PortalOrigin => PortalBaseUrl.TrimEnd('/');

        public static string TripsPageAbsoluteUrl => Combine(PortalBaseUrl, TripsPath);

        /// <summary>
        /// Optional fragment appended to <c>GET /portal/nu?date=…</c> stored referer (e.g. <c>#/vtripbilling</c>) so XHR <c>Referer</c> matches Chrome SPA routing in HARs.
        /// Empty by default; set <c>WellRydeTripsNuRefererHashFragment</c> in App.config if vendor requires hash parity.
        /// </summary>
        public static string TripsNuRefererHashFragment => ReadAppSetting("WellRydeTripsNuRefererHashFragment", "").Trim();

        public static string LoginPageAbsoluteUrl => Combine(PortalBaseUrl, LoginPath);

        public static string FilterDataUrl => PortalOrigin + "/portal/filterdata";

        /// <summary>Chrome loads this XHR immediately before <c>POST /portal/filterdata</c> (VTripBilling shell).</summary>
        public static string BuildListFilterDefsJsonUrl(string listDefId)
        {
            var id = string.IsNullOrWhiteSpace(listDefId) ? "" : listDefId.Trim();
            return PortalOrigin + "/portal/listFilterDefsJson?listDefId=" + Uri.EscapeDataString(id)
                   + "&customListDefId=&userDefaultFilter=true";
        }

        public static string SpringSecurityCheckUrl => PortalOrigin + "/portal/j_spring_security_check";

        public static string SpringSecurityLogoutUrl => PortalOrigin + "/portal/j_spring_security_logout";

        public static string PortalShellUrl => PortalOrigin + "/portal/";

        public static string NpsTimezoneUrl => PortalOrigin + "/portal/nps/timezone";

        /// <summary>
        /// Root <c>GET /nps/timezone</c> (no <c>/portal</c>) — often <c>404</c> on portal.app; not used in automatic shell priming (see <see cref="ChromeShellPrimingEnabled"/>).
        /// </summary>
        public static string RootNpsTimezoneGetUrl => PortalOrigin + "/nps/timezone";

        /// <summary>
        /// Optional document GETs before <c>trip/filterlist</c> (Chrome-ish). Default <b>off</b> — several vendor URLs return <b>HTTP 200</b> HTML “Internal Error” shells
        /// and churn <c>AWSALB</c> without helping trips; set <c>WellRydeChromeShellPriming=true</c> only when diagnosing stickiness.
        /// </summary>
        public static bool ChromeShellPrimingEnabled
        {
            get
            {
                var v = ReadAppSetting("WellRydeChromeShellPriming", "");
                if (string.IsNullOrWhiteSpace(v))
                    return false;
                return string.Equals(v, "true", StringComparison.OrdinalIgnoreCase) || v == "1";
            }
        }

        /// <summary>Chrome <c>GET</c> before AVL/trip widgets on NU shell.</summary>
        public static string PortalCurrentPageNuUrl => PortalOrigin + "/portal/currentPage?pageLabel=nu%23";

        public static string InsuranceExpiryNotificationUrl => PortalOrigin + "/portal//insurance/insuranceExpiryNotification";

        public static string TermsOfUseUnacceptedUrl => PortalOrigin + "/portal//termsofuse/getunacceptedtoudetails";

        public static string AvlDoubleSlashEntryUrl => PortalOrigin + "/portal//avl";

        public static string AvlInitializeDriverStopsUrl => PortalOrigin + "/portal/avl/initializeDriverStops";

        /// <summary>
        /// Live GPS proxy endpoint (long-lived; not replayed during shell priming — synchronous <c>GET</c> can hang ~60s and return HTTP 502).
        /// </summary>
        public static string GpsAgentUrl => PortalOrigin + "/portal/gpsagent";

        /// <summary>
        /// Chrome <c>GET /portal/avl/avlinitiate?…</c> with map bounds. Override full URL or query with <c>WellRydeAvlInitiateQuery</c> (query string after <c>?</c>, or absolute URL).
        /// </summary>
        public static string AvlInitiateDefaultUrl
        {
            get
            {
                var custom = ReadAppSetting("WellRydeAvlInitiateQuery", "").Trim();
                if (!string.IsNullOrEmpty(custom))
                {
                    if (custom.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                        || custom.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        return custom;
                    return PortalOrigin + "/portal/avl/avlinitiate?" + custom.TrimStart('?');
                }

                // Default bounds from Chrome HAR (Maine-ish viewport); empty avlQueryDate matches vendor capture.
                const string criteria =
                    "%7B%22avlMapSearch%22%3A%7B%22centerLatLng%22%3A%7B%22lat%22%3A%2244.141530%22%2C%22lng%22%3A%22-70.050160%22%7D%2C%22northEastBoundsLatLng%22%3A%7B%22lat%22%3A%2244.503571%22%2C%22lng%22%3A%22-68.964573%22%7D%2C%22southWestBoundsLatLng%22%3A%7B%22lat%22%3A%2243.777255%22%2C%22lng%22%3A%22-71.135747%22%7D%7D%7D";
                return PortalOrigin + "/portal/avl/avlinitiate?avlFilterCriteria=" + criteria
                       + "&avlQueryDate=&quickSearchJsonArr=%5B%5D&riderSearchJSON=";
            }
        }

        public static string TripSaveBillDataUrl => PortalOrigin + "/portal/trip/saveBillData";

        public static string TripGetAllDriversForTripAssignmentUrl => PortalOrigin + "/portal/trip/getAllDriversForTripAssignment?bpartnerId=0";

        public static string TripUnAssignValidationUrl => PortalOrigin + "/portal/trip/unAssignValidation";

        public static string TripUnassignUrl => PortalOrigin + "/portal/trip/unassign";

        public static string TripAssignTripDriverUrl => PortalOrigin + "/portal/trip/assignTripDriver";

        /// <summary>
        /// Portal / trips log verbosity from <c>WellRydePortalLogLevel</c> or <c>WellRydePortalLogVerbosity</c> (same meaning).
        /// <c>Quiet</c> (0) — outcomes and errors; <c>Normal</c> (1, default) — short summaries; <c>Verbose</c> (2) — curl per-attempt, longer prefixes;
        /// <c>Diagnostic</c> (3) — Verbose plus full request/cookie snapshot on <c>filterdata</c> failure (or set <c>WellRydePortalHttpDump=true</c>).
        /// UI: main app <c>WellRyde log</c> tab. Empty stored values fall back to <c>App.config</c> (<c>WellRydePortalLogLevel</c>, <c>WellRydeDebugPortalTraffic</c>, <c>WellRydePortalHttpDump</c>).
        /// </summary>
        internal enum PortalLogLevel
        {
            Quiet = 0,
            Normal = 1,
            Verbose = 2,
            Diagnostic = 3,
        }

        /// <summary>Parses <c>WellRydePortalLogLevel</c> / UI text (Quiet, Normal, Verbose, Diagnostic, 0–3).</summary>
        internal static PortalLogLevel ParsePortalLogLevel(string v)
        {
            if (string.IsNullOrWhiteSpace(v))
                return PortalLogLevel.Normal;
            v = v.Trim();
            if (string.Equals(v, "quiet", StringComparison.OrdinalIgnoreCase) || v == "0")
                return PortalLogLevel.Quiet;
            if (string.Equals(v, "normal", StringComparison.OrdinalIgnoreCase) || v == "1")
                return PortalLogLevel.Normal;
            if (string.Equals(v, "verbose", StringComparison.OrdinalIgnoreCase) || v == "2")
                return PortalLogLevel.Verbose;
            if (string.Equals(v, "diagnostic", StringComparison.OrdinalIgnoreCase) || string.Equals(v, "full", StringComparison.OrdinalIgnoreCase) || v == "3")
                return PortalLogLevel.Diagnostic;
            return PortalLogLevel.Normal;
        }

        internal static string FormatPortalLogLevel(PortalLogLevel level) => level.ToString();

        /// <summary>App.config only (empty UI override).</summary>
        internal static PortalLogLevel PortalLogLevelFromAppConfigOnly() =>
            ParsePortalLogLevel(ReadAppSetting("WellRydePortalLogLevel", ReadAppSetting("WellRydePortalLogVerbosity", "")));

        private static bool ParseAppSettingsFlag(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;
            return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || value.Trim() == "1";
        }

        private static bool ReadAppSettingsFlag(string key) => ParseAppSettingsFlag(ReadAppSetting(key, ""));

        /// <summary>App.config <c>WellRydeDebugPortalTraffic</c> only (no saved UI override).</summary>
        public static bool DebugPortalTrafficAppConfig => ReadAppSettingsFlag("WellRydeDebugPortalTraffic");

        /// <summary>App.config <c>WellRydePortalHttpDump</c> only (no saved UI override).</summary>
        public static bool PortalHttpDumpAppConfig => ReadAppSettingsFlag("WellRydePortalHttpDump");

        /// <summary>Resolved portal log level: non-empty user <c>wrPortalLogLevel</c> in application settings, else <c>App.config</c>.</summary>
        public static PortalLogLevel PortalLogLevelResolved
        {
            get
            {
                var ui = Settings.Default.wrPortalLogLevel?.Trim();
                if (!string.IsNullOrEmpty(ui))
                    return ParsePortalLogLevel(ui);
                return PortalLogLevelFromAppConfigOnly();
            }
        }

        /// <summary>Minimal portal noise — outcomes and errors only.</summary>
        public static bool PortalLogQuiet => PortalLogLevelResolved == PortalLogLevel.Quiet;

        /// <summary>Per-attempt curl lines, longer body prefixes, and other developer traces.</summary>
        public static bool PortalLogVerbose =>
            DebugPortalTraffic || PortalLogLevelResolved >= PortalLogLevel.Verbose;

        /// <summary>Full HTTP / cookie snapshot on <c>filterdata</c> mismatch (large; for comparing with browser/Fiddler).</summary>
        public static bool PortalLogHttpSnapshotDump
        {
            get
            {
                var level = PortalLogLevelResolved;
                if (level >= PortalLogLevel.Diagnostic)
                    return true;
                var ui = Settings.Default.wrPortalHttpDump?.Trim();
                if (!string.IsNullOrEmpty(ui))
                    return string.Equals(ui, "true", StringComparison.OrdinalIgnoreCase) || ui == "1";
                return ReadAppSettingsFlag("WellRydePortalHttpDump");
            }
        }

        /// <summary>Max chars of response body prefix in one-line errors. <see cref="PortalLogQuiet"/> yields 0 (callers omit prefix).</summary>
        public static int PortalLogBodyPrefixMax
        {
            get
            {
                if (PortalLogQuiet)
                    return 0;
                return PortalLogVerbose ? 350 : 120;
            }
        }

        /// <summary>If true, logs extra portal HTTP detail (same as raising level to <see cref="PortalLogLevel.Verbose"/> for trace gates).</summary>
        public static bool DebugPortalTraffic
        {
            get
            {
                var ui = Settings.Default.wrDebugPortalTraffic?.Trim();
                if (!string.IsNullOrEmpty(ui))
                    return string.Equals(ui, "true", StringComparison.OrdinalIgnoreCase) || ui == "1";
                return ReadAppSettingsFlag("WellRydeDebugPortalTraffic");
            }
        }

        /// <summary>
        /// Optional Tomcat <c>JSESSIONID</c> from the browser (Chrome DevTools → Application → Cookies → <c>portal.app.wellryde.com</c>).
        /// Applied when the jar has no <c>JSESSIONID</c>. This is <b>not</b> the Spring <c>SESSION</c> UUID in hex — that value breaks <c>filterdata</c>.
        /// </summary>
        public static string ManualJsessionId => ReadAppSetting("WellRydeManualJsessionId", "").Trim();

        /// <summary>
        /// Include <c>XSRF-TOKEN</c> on the <c>filterdata</c> <c>Cookie</c> line. Default <c>false</c> (Chrome HAR); set <c>WellRydeFilterDataCookieIncludeXsrf=true</c> if your tenant requires the cookie on the wire.
        /// </summary>
        public static bool FilterDataCookieIncludeXsrfToken
        {
            get
            {
                var v = ReadAppSetting("WellRydeFilterDataCookieIncludeXsrf", "");
                if (string.IsNullOrWhiteSpace(v))
                    return false;
                return string.Equals(v, "true", StringComparison.OrdinalIgnoreCase) || v == "1";
            }
        }

        /// <summary>
        /// When true, add <c>X-CSRF-TOKEN</c> / <c>X-XSRF-TOKEN</c> on <c>POST filterdata</c> and curl filterdata (PHP scraper style). Default <c>false</c> (Chrome HAR omits them on <c>filterdata</c>).
        /// </summary>
        public static bool FilterDataPhpStyleHeaders
        {
            get
            {
                var v = ReadAppSetting("WellRydeFilterDataPhpHeaders", "");
                if (string.IsNullOrWhiteSpace(v))
                    return false;
                return !string.Equals(v, "false", StringComparison.OrdinalIgnoreCase) && v != "0";
            }
        }

        /// <summary>
        /// Use <c>HttpClientHandler</c> with <c>UseCookies=true</c> and the shared jar for servlet HTML GETs so .NET merges <c>Set-Cookie: JSESSIONID</c> like libcurl.
        /// Default on; set <c>WellRydeServletCookieAutoHandler=false</c> to skip (diagnostics only).
        /// </summary>
        public static bool ServletCookieAutoHandlerEnabled
        {
            get
            {
                var v = ReadAppSetting("WellRydeServletCookieAutoHandler", "");
                if (string.IsNullOrWhiteSpace(v))
                    return true;
                return !string.Equals(v, "false", StringComparison.OrdinalIgnoreCase) && v != "0";
            }
        }

        /// <summary>
        /// POST <c>/portal/filterdata</c> via Windows <c>curl.exe</c> + Netscape cookie jar first (libcurl merges <c>JSESSIONID</c> like PHP). Default on when unset; set <c>WellRydeFilterDataUseCurl=false</c> to skip.
        /// </summary>
        public static bool FilterDataUseCurl
        {
            get
            {
                var v = ReadAppSetting("WellRydeFilterDataUseCurl", "");
                if (string.IsNullOrWhiteSpace(v))
                    return true;
                return !string.Equals(v, "false", StringComparison.OrdinalIgnoreCase) && v != "0";
            }
        }

        /// <summary>
        /// <b>6-slot VT shape</b> (date in sequence 2) for <c>SEC-S_*</c> trip lists — matches Chrome <c>filterdata</c> (see repo HAR / curl gold).
        /// Legacy <c>SEC-J…</c> uses 10 slots (date in sequence 7). Override: <c>WellRydeTripFilterShape</c> <c>Legacy</c> or <c>VtTripBilling</c>.
        /// </summary>
        public static bool UsesVtTripBillingFilterListShape()
        {
            var explicitShape = ReadAppSetting("WellRydeTripFilterShape", "").Trim();
            if (string.Equals(explicitShape, "Legacy", StringComparison.OrdinalIgnoreCase))
                return false;
            if (string.Equals(explicitShape, "VtTripBilling", StringComparison.OrdinalIgnoreCase))
                return true;
            return TripFilterListDefId.StartsWith("SEC-S_", StringComparison.Ordinal);
        }

        private static string ReadAppSetting(string key, string defaultValue)
        {
            try
            {
                var v = ConfigurationManager.AppSettings[key];
                return string.IsNullOrWhiteSpace(v) ? defaultValue : v.Trim();
            }
            catch
            {
                return defaultValue;
            }
        }

        private static string NormalizePath(string path, string fallback)
        {
            if (string.IsNullOrWhiteSpace(path))
                return fallback;
            path = path.Trim();
            if (!path.StartsWith("/", StringComparison.Ordinal))
                path = "/" + path;
            return path;
        }

        private static string Combine(string baseUrl, string absolutePath)
        {
            var b = baseUrl.TrimEnd('/');
            var p = absolutePath.StartsWith("/", StringComparison.Ordinal) ? absolutePath : "/" + absolutePath;
            return b + p;
        }
    }
}
