using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;

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

        public static string TripSaveBillDataUrl => PortalOrigin + "/portal/trip/saveBillData";

        public static string TripGetAllDriversForTripAssignmentUrl => PortalOrigin + "/portal/trip/getAllDriversForTripAssignment?bpartnerId=0";

        public static string TripUnAssignValidationUrl => PortalOrigin + "/portal/trip/unAssignValidation";

        public static string TripUnassignUrl => PortalOrigin + "/portal/trip/unassign";

        public static string TripAssignTripDriverUrl => PortalOrigin + "/portal/trip/assignTripDriver";

        /// <summary>If true, logs extra portal HTTP detail (currently used alongside HTML/not-JSON traffic dumps).</summary>
        public static bool DebugPortalTraffic
        {
            get
            {
                var v = ReadAppSetting("WellRydeDebugPortalTraffic", "");
                return string.Equals(v, "true", StringComparison.OrdinalIgnoreCase) || v == "1";
            }
        }

        /// <summary>
        /// Optional Tomcat <c>JSESSIONID</c> from the browser (Chrome DevTools → Application → Cookies → <c>portal.app.wellryde.com</c>).
        /// Applied when the jar has no <c>JSESSIONID</c>. This is <b>not</b> the Spring <c>SESSION</c> UUID in hex — that value breaks <c>filterdata</c>.
        /// </summary>
        public static string ManualJsessionId => ReadAppSetting("WellRydeManualJsessionId", "").Trim();

        /// <summary>
        /// Include <c>XSRF-TOKEN</c> on the <c>filterdata</c> <c>Cookie</c> line (Spring double-submit). Default on; some HARs omit it.
        /// Set <c>WellRydeFilterDataCookieIncludeXsrf=false</c> to match a minimal Chrome cookie line.
        /// </summary>
        public static bool FilterDataCookieIncludeXsrfToken
        {
            get
            {
                var v = ReadAppSetting("WellRydeFilterDataCookieIncludeXsrf", "");
                if (string.IsNullOrWhiteSpace(v))
                    return true;
                return !string.Equals(v, "false", StringComparison.OrdinalIgnoreCase) && v != "0";
            }
        }

        /// <summary>
        /// When true, add <c>X-CSRF-TOKEN</c> / <c>X-XSRF-TOKEN</c> on <c>filterdata</c> and <c>listFilterDefsJson</c> prime (PHP scraper style). Chrome HAR omits these on those endpoints — default <c>false</c>. Trip <c>filterlist</c> always sends XHR + CSRF headers when a token is known, independent of this flag.
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
