using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// HttpClient + cookie jar for WellRyde portal. Never follows HTTP redirects: <see cref="HttpClientHandler.AllowAutoRedirect"/> is false.
    /// Read <see cref="WellRydePortalLoginResult.Location"/> and <see cref="CookieJar"/> yourself if you want to open another URL later.
    /// </summary>
    internal sealed class WellRydePortalSession : IDisposable
    {
        public const string PortalOrigin = "https://portal.app.wellryde.com";
        private static readonly Uri PortalRootUri = new Uri(PortalOrigin + "/");
        private static readonly Uri SpringLoginUri = new Uri(PortalOrigin + "/portal/j_spring_security_check");
        private static readonly Uri PortalNuUri = new Uri(PortalOrigin + "/portal/nu");
        private static readonly Uri FilterDataUri = new Uri(PortalOrigin + "/portal/filterdata");
        private static readonly Uri SaveBillDataUri = new Uri(PortalOrigin + "/portal/trip/saveBillData");
        private static readonly Uri GetAllDriversForTripAssignmentUri =
            new Uri(PortalOrigin + "/portal/trip/getAllDriversForTripAssignment?bpartnerId=0");
        private static readonly Uri TripUnAssignValidationUri = new Uri(PortalOrigin + "/portal/trip/unAssignValidation");
        private static readonly Uri TripUnassignUri = new Uri(PortalOrigin + "/portal/trip/unassign");
        private static readonly Uri TripAssignTripsUri = new Uri(PortalOrigin + "/portal/trip/assignTrips");
        private static readonly Uri TripAssignValidationUri = new Uri(PortalOrigin + "/portal/trip/assignValidation");
        private static readonly Uri TripAssignTripDriverUri = new Uri(PortalOrigin + "/portal/trip/assignTripDriver");
        private static readonly Uri AvlInitiateUri = new Uri(PortalOrigin + "/portal/avl/avlinitiate");

        // Supey roster import: portal users list + per-user detail page. The list lives behind the
        // same /portal/filterdata pipeline as the trip list but with a different listDefId and a
        // 3-sequence filter shape; the detail page is a plain HTML view at /portal/users/{secId}.
        public const string SupeyUsersListDefId = "SEC-nPAmL7CyXxAhUHgE0x1NUA";
        private static readonly Uri PortalUsersBaseUri = new Uri(PortalOrigin + "/portal/users/");
        private static readonly Uri PortalShellUri = new Uri(PortalOrigin + "/portal/");
        private static readonly Uri NuUpdateUserUri = new Uri(PortalOrigin + "/portal/users/nuUpdateUser");
        private static readonly string[] PortalOutboundCookieNames =
            { "SESSION", "JSESSIONID", "AWSALB", "AWSALBCORS" };
        private static readonly Uri[] PortalCookieMirrorUris =
        {
            PortalRootUri,
            PortalShellUri,
            SpringLoginUri,
            PortalNuUri,
            FilterDataUri,
            PortalUsersBaseUri,
            NuUpdateUserUri,
        };
        private static readonly Regex UserSecIdRegex = new Regex(
            @"SEC-[A-Za-z0-9_\-]+",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex JSessionIdInTextRegex = new Regex(
            @"(?:;jsessionid=|(?:JSESSIONID|jsessionid)=)([A-F0-9]{32})",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        /// <summary>Browser users grid uses <c>maxResult=50</c> on first <c>filterdata</c> POST.</summary>
        public const int DefaultUsersFilterMaxResult = 50;

        /// <summary>Trip list grid (PU/DO addresses, schedule times, miles) from browser capture; tenant-specific if portal differs.</summary>
        public const string DefaultTripFilterListDefId = "SEC-J0JwBzGuni0ZopMPBRCNuQ";

        /// <summary>Older compact broker-style trip grid (fewer columns per row).</summary>
        public const string LegacyCompactTripFilterListDefId = "SEC-S_XoEZX6lDWauVBtgu7FHw";

        /// <summary>Default page size cap for <see cref="PostTripFilterDataAsync"/> (<c>maxResult</c> and <c>defaultSize</c> form fields). Portal may still cap lower.</summary>
        public const int DefaultTripFilterMaxResult = 500;

        private const int TripFilterMaxResultUpperBound = 10000;

        /// <summary>Embeddable ServiceNow VA URL (same host as captured browser traffic).</summary>
        public const string DefaultServiceNowEmbedUrl =
            "https://modivcare.service-now.com/sn_va_web_client_app_embed.do?sysparm_skip_load_history=true";

        /// <summary>ServiceNow chat script URL from captured browser traffic.</summary>
        public const string DefaultServiceNowChatScriptUrl =
            "https://modivcare.service-now.com/scripts/now-requestor-chat-popover-app/now-requestor-chat-popover-app.min.js?sysparm_substitute=false";

        private readonly HttpClientHandler _handler;
        private readonly HttpClient _client;
        private bool _disposed;

        /// <summary>HTML from the last successful <see cref="BootstrapMainPageAsync"/>; used to read Spring hidden fields.</summary>
        private string _lastBootstrapHtml;

        /// <summary>HTML from the last successful <see cref="GetPortalNuAsync"/>; used for AJAX <c>_csrf</c>.</summary>
        private string _lastPortalNuHtml;

        /// <summary>Last <c>JSESSIONID</c> from Set-Cookie (Tomcat often uses a narrow path until widened).</summary>
        private string _pinnedJSessionId;

        /// <summary>Authoritative outbound cookies (every Set-Cookie + jar sync) — matches browser <c>-b</c> header.</summary>
        private readonly Dictionary<string, string> _portalCookieStore =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>JSON body from the last successful <see cref="PostTripFilterDataAsync"/>.</summary>
        public string LastTripFilterDataJson { get; private set; }

        public CookieContainer CookieJar => _handler.CookieContainer;

        public HttpClient Client => _client;

        /// <summary>From the last successful <see cref="BootstrapMainPageAsync"/> HTML parse, if present.</summary>
        public string LastRequestVerificationToken { get; private set; }

        /// <summary>Cookie name/value for <see cref="PortalRootUri"/> after the last bootstrap attempt.</summary>
        public IReadOnlyDictionary<string, string> LastPortalCookies { get; private set; }

        /// <summary>Per-request timeout for interactive portal use (trip paging, billing).</summary>
        public const int DefaultHttpTimeoutSeconds = 25;

        /// <summary>Shorter timeout for background probes when the portal may be down.</summary>
        public const int BackgroundHttpTimeoutSeconds = 12;

        public WellRydePortalSession(int httpTimeoutSeconds = DefaultHttpTimeoutSeconds)
        {
            _handler = new HttpClientHandler
            {
                // Never follow Location. We only record status, Location header, and cookies; callers redirect manually if needed.
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                CookieContainer = new CookieContainer(),
                UseCookies = true,
            };

            int timeoutSeconds = Math.Max(5, httpTimeoutSeconds);
            _client = new HttpClient(_handler)
            {
                Timeout = TimeSpan.FromSeconds(timeoutSeconds),
            };

            // Do not set Accept on DefaultRequestHeaders — it merges with per-request Accept and
            // makes JSON endpoints return HTML (login shell). Set Accept per request instead.
            _client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/148.0.0.0 Safari/537.36");
            _client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
            _client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-CH-UA",
                "\"Chromium\";v=\"148\", \"Google Chrome\";v=\"148\", \"Not/A)Brand\";v=\"99\"");
            _client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-CH-UA-Mobile", "?0");
            _client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-CH-UA-Platform", "\"Windows\"");

            WellRydePortalLog.Info("SESSION", "WellRyde portal client started. Log file: " + WellRydePortalLog.LogFilePath);
        }

        /// <summary>
        /// Fast TCP connect to the portal host (443). Lets background callers detect "portal down"
        /// in a few seconds instead of burning a full HTTP timeout per request.
        /// </summary>
        public static async Task<bool> ProbePortalReachableAsync(int timeoutMs = 3000)
        {
            try
            {
                using (var tcp = new System.Net.Sockets.TcpClient())
                {
                    Task connect = tcp.ConnectAsync(PortalRootUri.Host, 443);
                    Task winner = await Task.WhenAny(connect, Task.Delay(timeoutMs)).ConfigureAwait(false);
                    if (winner != connect)
                        return false;
                    await connect.ConfigureAwait(false); // observe connect exceptions (refused, DNS)
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>GET portal root once (no redirect chain). Cookies accumulate in <see cref="CookieJar"/>.</summary>
        public async Task<WellRydePortalBootstrapResult> BootstrapMainPageAsync(CancellationToken cancellationToken = default)
        {
            _lastBootstrapHtml = null;
            LastRequestVerificationToken = null;
            LastPortalCookies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            HttpResponseMessage response = null;
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, PortalRootUri))
                {
                    SetDocumentNavigationHeaders(request);
                    request.Headers.TryAddWithoutValidation("Referer", "https://www.google.com/");
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "cross-site");
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-User", "?1");
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
                    request.Headers.TryAddWithoutValidation("Priority", "u=0, i");
                    ApplyPortalCookieHeader(request);

                    response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                return WellRydePortalBootstrapResult.Fail(null, ex.Message ?? "Request failed.");
            }

            string html;
            try
            {
                html = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var code = response.StatusCode;
                response.Dispose();
                return WellRydePortalBootstrapResult.Fail(code, ex.Message ?? "Failed to read response body.");
            }

            var statusCode = response.StatusCode;
            FinalizePortalResponse(response, PortalRootUri);
            response.Dispose();
            LastPortalCookies = SnapMergedPortalCookies();
            CaptureJSessionIdFromMergedCookies();
            SyncPortalCookieStoreFromJar();
            WellRydePortalLog.CookieState("after bootstrap GET /", _portalCookieStore, "status=" + (int)statusCode);

            if ((int)statusCode < 200 || (int)statusCode >= 300)
                return WellRydePortalBootstrapResult.Fail(statusCode,
                    "HTTP " + (int)statusCode + " from portal root.", LastPortalCookies, LastRequestVerificationToken);

            LastRequestVerificationToken = ExtractRequestVerificationToken(html);
            string csrf = ExtractHiddenInputValue(html, "_csrf");
            string logincsrf = ExtractHiddenInputValue(html, "_logincsrf");
            if (string.IsNullOrEmpty(csrf) || string.IsNullOrEmpty(logincsrf))
                return WellRydePortalBootstrapResult.Fail(null,
                    "Could not find _csrf or _logincsrf on the portal login page.",
                    LastPortalCookies, LastRequestVerificationToken);

            _lastBootstrapHtml = html;
            TryExtractJSessionIdFromText(html, "bootstrap /");
            return WellRydePortalBootstrapResult.Ok(statusCode, PortalRootUri, LastPortalCookies, LastRequestVerificationToken);
        }

        /// <summary>
        /// POST Spring Security login. Call <see cref="BootstrapMainPageAsync"/> first so session cookies and HTML tokens exist.
        /// Does not follow 3xx: cookies are stored in <see cref="CookieJar"/>; use <see cref="WellRydePortalLoginResult.Location"/> only if you will request that URL yourself.
        /// </summary>
        public async Task<WellRydePortalLoginResult> LoginSpringSecurityAsync(string userCompany, string username, string password,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(_lastBootstrapHtml))
                return WellRydePortalLoginResult.Fail(null, "Load the portal page first (bootstrap).");

            TryExtractJSessionIdFromText(_lastBootstrapHtml, "login form HTML");

            string csrf = ExtractHiddenInputValue(_lastBootstrapHtml, "_csrf");
            string logincsrf = ExtractHiddenInputValue(_lastBootstrapHtml, "_logincsrf");
            if (string.IsNullOrEmpty(csrf) || string.IsNullOrEmpty(logincsrf))
                return WellRydePortalLoginResult.Fail(null, "Could not find _csrf or _logincsrf in the portal HTML.");

            var pairs = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("deviceFingerPrint", ""),
                new KeyValuePair<string, string>("_logincsrf", logincsrf),
                new KeyValuePair<string, string>("geoLocationVal", "false"),
                new KeyValuePair<string, string>("userCompany", userCompany ?? ""),
                new KeyValuePair<string, string>("j_username", username ?? ""),
                new KeyValuePair<string, string>("j_password", password ?? ""),
                new KeyValuePair<string, string>("userLat", ""),
                new KeyValuePair<string, string>("userLong", ""),
                new KeyValuePair<string, string>("serviceNowURL", DefaultServiceNowEmbedUrl),
                new KeyValuePair<string, string>("serviceNowChatURL", DefaultServiceNowChatScriptUrl),
                new KeyValuePair<string, string>("_csrf", csrf),
            };

            HttpResponseMessage response = null;
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Post, SpringLoginUri))
                {
                    request.Content = new FormUrlEncodedContent(pairs);
                    SetDocumentNavigationHeaders(request);
                    request.Headers.TryAddWithoutValidation("Origin", PortalOrigin);
                    request.Headers.TryAddWithoutValidation("Referer", PortalRootUri.ToString());
                    request.Headers.TryAddWithoutValidation("Cache-Control", "max-age=0");
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-User", "?1");
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
                    request.Headers.TryAddWithoutValidation("Priority", "u=0, i");
                    ApplyPortalCookieHeader(request);

                    response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                return WellRydePortalLoginResult.Fail(null, ex.Message ?? "Login request failed.");
            }

            string html;
            try
            {
                html = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var code = response.StatusCode;
                response.Dispose();
                return WellRydePortalLoginResult.Fail(code, ex.Message ?? "Failed to read login response.");
            }

            string location = null;
            if (response.Headers.Location != null)
                location = response.Headers.Location.IsAbsoluteUri
                    ? response.Headers.Location.ToString()
                    : new Uri(SpringLoginUri, response.Headers.Location).ToString();

            var statusCode = response.StatusCode;
            FinalizePortalResponse(response, SpringLoginUri);
            response.Dispose();
            SyncPortalCookieStoreFromJar();
            TryExtractJSessionIdFromText(location, "login Location");
            TryExtractJSessionIdFromText(html, "login response body");
            WellRydePortalLog.CookieState("after login POST", _portalCookieStore,
                "status=" + (int)statusCode + " location=" + (location ?? "(none)"));
            bool ok = InterpretSpringLoginResponse(statusCode, location, html);
            if (!ok)
            {
                var hint = location != null ? " Location: " + location : "";
                return WellRydePortalLoginResult.Fail(statusCode,
                    "Login was not accepted (HTTP " + (int)statusCode + ")." + hint, location, LastPortalCookies);
            }

            _lastBootstrapHtml = null;
            return WellRydePortalLoginResult.Ok(statusCode, location, LastPortalCookies);
        }

        /// <summary>True when Spring <c>SESSION</c> cookie is present (required for portal APIs).</summary>
        public bool HasPortalSessionCookie() => HasSessionCookie();

        /// <summary>Legacy gate — only checks <c>SESSION</c>; save uses the edit-form chain, not cookie names.</summary>
        public Task<string> EnsurePortalSessionCookiesForApiAsync(
            CancellationToken cancellationToken = default)
        {
            if (!HasSessionCookie())
                return Task.FromResult(
                    "WellRyde SESSION cookie is missing after sign-in. Use Login → WellRyde, then save again."
                    + WellRydePortalLog.UserHintSuffix());
            return Task.FromResult<string>(null);
        }

        /// <summary>Best-effort JSESSIONID widen for driver save — never fails login.</summary>
        public async Task WarmPortalSessionCookiesAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await TryAcquireJSessionIdForUsersApiAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // ignore — save path will surface a clearer error
            }
        }

        /// <summary>
        /// Browser gets <c>JSESSIONID</c> on the users-list XHR, not on Spring login alone.
        /// Widen any narrow-path cookie so <c>/portal/users/*</c> requests send it.
        /// </summary>
        private async Task TryAcquireJSessionIdForUsersApiAsync(CancellationToken cancellationToken)
        {
            TryExtractJSessionIdFromText(_lastBootstrapHtml, "cached bootstrap");
            TryExtractJSessionIdFromText(_lastPortalNuHtml, "cached /portal/nu");
            PromoteJSessionCookieToPortalPaths();
            SyncSnapshotCookiesIntoJar();
            CaptureJSessionIdFromMergedCookies();
            SyncPortalCookieStoreFromJar();

            if (string.IsNullOrEmpty(_lastPortalNuHtml) || !IsPortalNuPageAuthenticated())
            {
                try
                {
                    await GetPortalNuAsync(cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    // ignore
                }
            }

            if (!HasSessionCookie())
                return;

            if (!HasJSessionIdForUsersApi())
                await TryEstablishServletSessionAsync(cancellationToken).ConfigureAwait(false);

            if (!HasJSessionIdForUsersApi())
                await TryWarmUsersListFilterDefsAsync(cancellationToken).ConfigureAwait(false);

            if (!HasJSessionIdForUsersApi())
            {
                try
                {
                    await PostUsersFilterDataAsync(
                            page: 1,
                            maxResults: DefaultUsersFilterMaxResult,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                }
                catch
                {
                    // ignore
                }
            }

            if (!HasJSessionIdForUsersApi())
                await TryEstablishServletSessionAsync(cancellationToken).ConfigureAwait(false);

            PromoteJSessionCookieToPortalPaths();
            SyncSnapshotCookiesIntoJar();
            CaptureJSessionIdFromMergedCookies();
            SyncPortalCookieStoreFromJar();
            WellRydePortalLog.CookieState("after TryAcquireJSessionId", _portalCookieStore,
                "hasJSESSION=" + (HasJSessionIdForUsersApi() ? "yes" : "no"));
        }

        /// <summary>One GET <c>/portal/</c> so Tomcat can issue <c>JSESSIONID</c> when <c>SESSION</c> exists (browser has both before filterdata).</summary>
        private async Task TryEstablishServletSessionAsync(CancellationToken cancellationToken)
        {
            if (HasJSessionIdForUsersApi() || !HasSessionCookie())
                return;

            HttpResponseMessage response = null;
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, PortalShellUri))
                {
                    SetDocumentNavigationHeaders(request);
                    request.Headers.TryAddWithoutValidation("Referer", PortalNuUri.ToString());
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
                    ApplyPortalCookieHeader(request);

                    response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                        .ConfigureAwait(false);
                }

                string shellHtml = null;
                try
                {
                    shellHtml = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
                catch
                {
                    // ignore body
                }

                FinalizePortalResponse(response, PortalShellUri);
                TryExtractJSessionIdFromText(shellHtml, "GET /portal/");
            }
            catch
            {
                // ignore
            }
            finally
            {
                response?.Dispose();
            }
        }

        /// <summary>
        /// GET the Spring login redirect target (usually <c>/portal/nu</c>) so <c>SESSION</c> / CSRF cookies
        /// match a real browser sign-in. HttpClient does not auto-follow redirects.
        /// </summary>
        public async Task<WellRydePortalNuResult> CompleteLoginNavigationAsync(
            string locationAfterLogin,
            CancellationToken cancellationToken = default)
        {
            var uri = ResolvePortalUri(locationAfterLogin);
            return await GetPortalDocumentAsync(uri, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// GET <c>/portal/nu</c> (Angular shell) after a successful Spring login. Same-origin headers; cookies sent from the jar.
        /// Does not follow redirects.
        /// </summary>
        public async Task<WellRydePortalNuResult> GetPortalNuAsync(CancellationToken cancellationToken = default)
        {
            return await GetPortalDocumentAsync(PortalNuUri, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Quick sync hint only — prefer <see cref="ProbePortalSessionAsync"/>.</summary>
        public bool IsPortalNuPageAuthenticated()
        {
            return !string.IsNullOrEmpty(_lastPortalNuHtml)
                && !PortalHtmlIndicatesLoginPage(_lastPortalNuHtml)
                && !string.IsNullOrEmpty(ResolveAjaxCsrfToken())
                && (HasSessionCookie() || HasSnapshotCookie("SESSION"));
        }

        /// <summary>Real check: POST <c>filterdata</c> and require JSON (not login HTML).</summary>
        public async Task<WellRydePortalSessionProbeResult> ProbePortalSessionAsync(
            CancellationToken cancellationToken = default)
        {
            SyncSnapshotCookiesIntoJar();

            if (string.IsNullOrEmpty(_lastPortalNuHtml))
            {
                var nu = await GetPortalNuAsync(cancellationToken).ConfigureAwait(false);
                if (!nu.IsSuccess)
                {
                    return WellRydePortalSessionProbeResult.Fail(
                        nu.ErrorMessage ?? "Could not load /portal/nu.");
                }
            }

            if (PortalHtmlIndicatesLoginPage(_lastPortalNuHtml))
            {
                return WellRydePortalSessionProbeResult.Fail(
                    "WellRyde returned the login page — check company code, username, and password.");
            }

            if (string.IsNullOrEmpty(ResolveAjaxCsrfToken()))
            {
                return WellRydePortalSessionProbeResult.Fail(
                    "Could not read portal CSRF token — sign in again from the Login bar.");
            }

            var trip = await PostTripFilterDataAsync(
                DateTime.Today,
                usePeriodDayFilter: true,
                maxResults: 1,
                page: 1,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (trip.IsSuccess && !ResponseBodyLooksLikeHtml(trip.JsonBody))
                return WellRydePortalSessionProbeResult.Ok();

            var users = await PostUsersFilterDataAsync(
                page: 1, maxResults: 1, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (users.IsSuccess && !ResponseBodyLooksLikeHtml(users.JsonBody))
                return WellRydePortalSessionProbeResult.Ok();

            string detail = trip.ErrorMessage ?? users.ErrorMessage ?? "Portal rejected the session.";
            if (ResponseBodyLooksLikeHtml(trip.JsonBody) || ResponseBodyLooksLikeHtml(users.JsonBody))
                detail = "WellRyde session is not valid (portal returned HTML instead of data). Sign in again.";

            return WellRydePortalSessionProbeResult.Fail(detail);
        }

        /// <summary>True when signed in for user-admin APIs.</summary>
        public bool IsUsersApiSessionReady() =>
            HasSessionCookie() && IsPortalNuPageAuthenticated();

        /// <summary>Alias for <see cref="IsPortalNuPageAuthenticated"/> — do not require JSESSIONID for trip billing.</summary>
        public bool IsPortalNuAuthenticated() => IsPortalNuPageAuthenticated();

        /// <summary>Verify users list API accepts the session (needed before <c>?form</c>). Does not clear CSRF cache.</summary>
        public async Task<WellRydePortalFilterDataResult> EnsureUsersAdminSessionAsync(
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(_lastPortalNuHtml))
            {
                var nu = await GetPortalNuAsync(cancellationToken).ConfigureAwait(false);
                if (!nu.IsSuccess)
                    return WellRydePortalFilterDataResult.Fail(nu.StatusCode,
                        nu.ErrorMessage ?? "Could not load /portal/nu.");
            }

            if (!HasSessionCookie())
                return WellRydePortalFilterDataResult.Fail(null,
                    "WellRyde SESSION cookie is missing — use Login → WellRyde.");

            var probe = await PostUsersFilterDataAsync(page: 1, maxResults: 1, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (!probe.IsSuccess)
                return probe;
            if (ResponseBodyLooksLikeHtml(probe.JsonBody))
                return WellRydePortalFilterDataResult.Fail(null,
                    "WellRyde users list returned HTML/XML instead of JSON — sign in again from the Login bar."
                    + DescribePortalSessionCookies());

            return probe;
        }

        /// <summary>Single GET to the given URL — never follows <c>Location</c> (callers use login Location or <c>/portal/nu</c> explicitly).</summary>
        private async Task<WellRydePortalNuResult> GetPortalDocumentAsync(
            Uri uri,
            CancellationToken cancellationToken)
        {
            Uri current = uri ?? PortalNuUri;

            HttpResponseMessage response = null;
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, current))
                {
                    SetDocumentNavigationHeaders(request);
                    request.Headers.TryAddWithoutValidation("Referer", PortalRootUri.ToString());
                    request.Headers.TryAddWithoutValidation("Cache-Control", "max-age=0");
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-User", "?1");
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
                    request.Headers.TryAddWithoutValidation("Sec-CH-UA",
                        "\"Chromium\";v=\"148\", \"Google Chrome\";v=\"148\", \"Not/A)Brand\";v=\"99\"");
                    request.Headers.TryAddWithoutValidation("Sec-CH-UA-Mobile", "?0");
                    request.Headers.TryAddWithoutValidation("Sec-CH-UA-Platform", "\"Windows\"");
                    request.Headers.TryAddWithoutValidation("Priority", "u=0, i");
                    ApplyPortalCookieHeader(request);

                    response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                return WellRydePortalNuResult.Fail(null, ex.Message ?? "GET portal document failed.");
            }

            string html;
            try
            {
                html = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var code = response.StatusCode;
                response.Dispose();
                return WellRydePortalNuResult.Fail(code, ex.Message ?? "Failed to read portal document body.");
            }

            var statusCode = response.StatusCode;
            FinalizePortalResponse(response, current);
            response.Dispose();
            CaptureJSessionIdFromMergedCookies();

            if ((int)statusCode >= 300 && (int)statusCode < 400)
            {
                return WellRydePortalNuResult.Fail(statusCode,
                    "HTTP redirect from " + current.AbsolutePath
                    + " (not followed — GET /portal/nu or the login Location URL explicitly).");
            }

            if ((int)statusCode < 200 || (int)statusCode >= 300)
                return WellRydePortalNuResult.Fail(statusCode, "HTTP " + (int)statusCode + " from " + current);

            if (IsPortalNuShellUri(current))
            {
                _lastPortalNuHtml = html;
                SyncPortalCookieStoreFromJar();
                TryExtractJSessionIdFromText(html, "/portal/nu");
                WellRydePortalLog.Info("NU",
                    "/portal/nu loaded csrfLen=" + (ResolveAjaxCsrfToken()?.Length ?? 0)
                    + " auth=" + (IsPortalNuPageAuthenticated() ? "yes" : "no")
                    + " jsession=" + (HasJSessionIdForUsersApi() ? "yes" : "no"));
            }

            return WellRydePortalNuResult.Ok(statusCode);
        }

        private static Uri ResolvePortalUri(string locationOrPath)
        {
            if (string.IsNullOrWhiteSpace(locationOrPath))
                return PortalNuUri;
            if (Uri.TryCreate(locationOrPath, UriKind.Absolute, out var abs))
            {
                if (!string.IsNullOrEmpty(abs.Fragment))
                    abs = new Uri(abs.GetLeftPart(UriPartial.Path) + abs.Query);
                return abs;
            }
            return new Uri(PortalRootUri, locationOrPath.TrimStart('/'));
        }

        /// <summary>Tomcat often embeds <c>;jsessionid=</c> in HTML/redirects instead of Set-Cookie (HttpClient never saw JSESSIONID in logs).</summary>
        private void TryExtractJSessionIdFromText(string text, string source)
        {
            if (string.IsNullOrWhiteSpace(text) || HasJSessionIdForUsersApi())
                return;
            Match m = JSessionIdInTextRegex.Match(text);
            if (!m.Success || m.Groups.Count < 2)
                return;
            PinJSessionId(m.Groups[1].Value);
            WellRydePortalLog.Info("SESSION", "JSESSIONID scraped from " + (source ?? "text"));
        }

        private async Task TryWarmUsersListFilterDefsAsync(CancellationToken cancellationToken)
        {
            if (HasJSessionIdForUsersApi())
                return;

            var uri = new Uri(PortalOrigin + "/portal/listFilterDefsJson?listDefId=" + SupeyUsersListDefId);
            HttpResponseMessage response = null;
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
                {
                    SetRequestAccept(request, "application/json, text/javascript, */*; q=0.01");
                    request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
                    request.Headers.TryAddWithoutValidation("Referer", PortalNuUri.ToString());
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "cors");
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "empty");
                    ApplyPortalApiCookieHeader(request);
                    response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                        .ConfigureAwait(false);
                }

                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                FinalizePortalResponse(response, uri);
                TryExtractJSessionIdFromText(body, "listFilterDefsJson");
            }
            catch
            {
                // ignore
            }
            finally
            {
                response?.Dispose();
            }
        }

        private static bool IsPortalNuShellUri(Uri uri)
        {
            if (uri == null) return false;
            string path = uri.AbsolutePath.TrimEnd('/');
            return string.Equals(path, "/portal/nu", StringComparison.OrdinalIgnoreCase);
        }

        private bool HasSessionCookie() =>
            FindStoredCookie("SESSION") != null || HasSnapshotCookie("SESSION");

        private bool HasSnapshotCookie(string name)
        {
            if (LastPortalCookies == null || string.IsNullOrEmpty(name))
                return false;
            foreach (var kv in LastPortalCookies)
            {
                if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(kv.Value))
                    return true;
            }
            return false;
        }

        /// <summary>Copy cookies from the last response snapshot into the jar (all portal auth cookies).</summary>
        public void SyncSnapshotCookiesIntoJar()
        {
            MirrorAllPortalCookiesToStandardPaths();
        }

        /// <summary>Push every known portal cookie onto path <c>/</c> for all API URIs we call.</summary>
        private void MirrorAllPortalCookiesToStandardPaths()
        {
            var merged = SnapMergedPortalCookies();
            if (merged == null || merged.Count == 0)
                return;

            foreach (var kv in merged)
            {
                if (string.IsNullOrWhiteSpace(kv.Key) || string.IsNullOrWhiteSpace(kv.Value))
                    continue;
                StampCookieOnPortalPaths(kv.Key, kv.Value);
            }
        }

        private void StampCookieOnPortalPaths(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(value))
                return;

            var cookie = new Cookie(name, value, "/", "portal.app.wellryde.com")
            {
                Secure = true,
                HttpOnly = true,
            };
            foreach (var uri in PortalCookieMirrorUris)
            {
                try
                {
                    _handler.CookieContainer.Add(uri, cookie);
                }
                catch
                {
                    // duplicate — ignore
                }
            }
        }

        /// <summary>Document navigation (bootstrap, login, <c>/portal/nu</c>) — jar only; do not override with a manual <c>Cookie</c> header.</summary>
        private void ApplyPortalCookieHeader(HttpRequestMessage request)
        {
            if (request == null) return;
            MirrorAllPortalCookiesToStandardPaths();
            CaptureJSessionIdFromMergedCookies();
            string jarCookies = _handler.CookieContainer.GetCookieHeader(request.RequestUri ?? PortalRootUri);
            WellRydePortalLog.HttpRequest(
                request.Method.ToString(),
                request.RequestUri?.ToString(),
                jarCookies,
                _portalCookieStore.Keys);
        }

        /// <summary>Portal XHR/API — send <see cref="_portalCookieStore"/> as the <c>Cookie</c> header (browser <c>-b</c>).</summary>
        private void ApplyPortalApiCookieHeader(HttpRequestMessage request)
        {
            SyncPortalCookieStoreFromJar();
            TryExtractJSessionIdFromText(_lastPortalNuHtml, "cached /portal/nu");
            if (!string.IsNullOrWhiteSpace(_pinnedJSessionId))
                RecordPortalCookie("JSESSIONID", _pinnedJSessionId);

            string cookieHeader = BuildOutboundCookieHeader();
            if (!string.IsNullOrEmpty(cookieHeader))
                request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
            WellRydePortalLog.HttpRequest(
                request.Method.ToString(),
                request.RequestUri?.ToString(),
                cookieHeader,
                _portalCookieStore.Keys);
        }

        private void RecordPortalCookie(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;
            _portalCookieStore[name.Trim()] = value ?? "";
            if (string.Equals(name, "JSESSIONID", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(value))
                _pinnedJSessionId = value.Trim();
        }

        private void IngestCookieHeaderString(string cookieHeader)
        {
            if (string.IsNullOrWhiteSpace(cookieHeader))
                return;
            foreach (string part in cookieHeader.Split(';'))
            {
                string piece = part.Trim();
                int eq = piece.IndexOf('=');
                if (eq <= 0) continue;
                RecordPortalCookie(piece.Substring(0, eq).Trim(), piece.Substring(eq + 1).Trim());
            }
        }

        private void SyncPortalCookieStoreFromJar()
        {
            MirrorAllPortalCookiesToStandardPaths();
            foreach (var uri in PortalCookieMirrorUris)
            {
                IngestCookieHeaderString(_handler.CookieContainer.GetCookieHeader(uri));
                foreach (Cookie c in _handler.CookieContainer.GetCookies(uri))
                    RecordPortalCookie(c.Name, c.Value);
            }
        }

        private string BuildOutboundCookieHeader()
        {
            var parts = new List<string>();
            foreach (string name in PortalOutboundCookieNames)
            {
                if (_portalCookieStore.TryGetValue(name, out string val) && !string.IsNullOrWhiteSpace(val))
                    parts.Add(name + "=" + val);
            }
            return parts.Count == 0 ? null : string.Join("; ", parts);
        }

        /// <summary>Whether <c>JSESSIONID</c> is in the outbound cookie store (browser always sends it on user APIs).</summary>
        private bool HasJSessionIdForUsersApi()
        {
            SyncPortalCookieStoreFromJar();
            if (!string.IsNullOrWhiteSpace(_pinnedJSessionId))
                return true;
            return _portalCookieStore.TryGetValue("JSESSIONID", out string js) && !string.IsNullOrWhiteSpace(js);
        }

        /// <summary>Quick check after sign-in: <c>SESSION</c> + loaded <c>/portal/nu</c>.</summary>
        public Task<bool> PrepareForUsersApiAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(HasSessionCookie() && IsPortalNuPageAuthenticated());

        private static bool CookieHeaderHasName(string cookieHeader, string cookieName)
        {
            if (string.IsNullOrEmpty(cookieHeader) || string.IsNullOrEmpty(cookieName))
                return false;
            return cookieHeader.IndexOf(cookieName + "=", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool CookieJarSendsJSessionIdTo(Uri requestUri)
        {
            if (requestUri == null) return false;
            foreach (Cookie c in _handler.CookieContainer.GetCookies(requestUri))
            {
                if (string.Equals(c.Name, "JSESSIONID", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(c.Value))
                    return true;
            }
            return false;
        }

        private bool HasAuthenticatedPortalCookies() => HasSessionCookie();

        private Cookie FindStoredCookie(string cookieName)
        {
            if (string.IsNullOrEmpty(cookieName)) return null;
            var probe = new[]
            {
                PortalRootUri,
                PortalShellUri,
                SpringLoginUri,
                PortalNuUri,
                FilterDataUri,
                PortalUsersBaseUri,
            };
            foreach (var uri in probe)
            {
                foreach (Cookie c in _handler.CookieContainer.GetCookies(uri))
                {
                    if (string.Equals(c.Name, cookieName, StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(c.Value))
                        return c;
                }
            }
            return null;
        }

        private void PromoteJSessionCookieToPortalPaths()
        {
            var js = FindStoredCookie("JSESSIONID");
            if (js != null && !string.IsNullOrWhiteSpace(js.Value))
                PinJSessionId(js.Value);
            else if (!string.IsNullOrWhiteSpace(_pinnedJSessionId))
                StampCookieOnPortalPaths("JSESSIONID", _pinnedJSessionId);
        }

        private void PinJSessionId(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            _pinnedJSessionId = value.Trim();
            RecordPortalCookie("JSESSIONID", _pinnedJSessionId);
            StampCookieOnPortalPaths("JSESSIONID", _pinnedJSessionId);
        }

        private void CaptureJSessionIdFromMergedCookies()
        {
            var merged = SnapMergedPortalCookies();
            if (merged.TryGetValue("JSESSIONID", out string js) && !string.IsNullOrWhiteSpace(js))
                PinJSessionId(js);
        }

        private void FinalizePortalResponse(HttpResponseMessage response, Uri defaultUri)
        {
            if (response == null) return;
            Uri uri = defaultUri ?? PortalRootUri;
            AbsorbSetCookieHeaders(response, uri);
            CaptureJSessionIdFromJar();
            LastPortalCookies = SnapMergedPortalCookies();
            MirrorAllPortalCookiesToStandardPaths();
            CaptureJSessionIdFromMergedCookies();
            SyncPortalCookieStoreFromJar();
            WellRydePortalLog.HttpResponse(
                uri.ToString(),
                (int)response.StatusCode,
                CollectSetCookieNames(response),
                _portalCookieStore.Keys,
                response.Headers.Location?.ToString());
            WellRydePortalLog.CookieState("after " + uri.AbsolutePath, _portalCookieStore, null);
        }

        private static List<string> CollectSetCookieNames(HttpResponseMessage response)
        {
            var names = new List<string>();
            if (response == null) return names;
            void Scan(System.Net.Http.Headers.HttpHeaders headers)
            {
                if (headers == null || !headers.TryGetValues("Set-Cookie", out IEnumerable<string> values))
                    return;
                foreach (string header in values)
                {
                    int eq = header.IndexOf('=');
                    if (eq > 0)
                        names.Add(header.Substring(0, eq).Trim());
                }
            }
            Scan(response.Headers);
            if (response.Content != null)
                Scan(response.Content.Headers);
            return names;
        }

        private void CaptureJSessionIdFromJar()
        {
            foreach (var uri in PortalCookieMirrorUris)
            {
                foreach (Cookie c in _handler.CookieContainer.GetCookies(uri))
                {
                    if (string.Equals(c.Name, "JSESSIONID", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(c.Value))
                        PinJSessionId(c.Value);
                }
            }
        }

        private void AbsorbSetCookieHeaders(HttpResponseMessage response, Uri defaultUri)
        {
            if (response == null || defaultUri == null) return;
            int setCookieCount = AbsorbSetCookieFromHeaders(response.Headers, defaultUri);
            if (response.Content != null)
                setCookieCount += AbsorbSetCookieFromHeaders(response.Content.Headers, defaultUri);
            if (setCookieCount > 0)
                WellRydePortalLog.Info("SET-COOKIE", defaultUri.AbsolutePath + " absorbed " + setCookieCount + " header(s)");
        }

        private int AbsorbSetCookieFromHeaders(System.Net.Http.Headers.HttpHeaders headers, Uri defaultUri)
        {
            if (headers == null || !headers.TryGetValues("Set-Cookie", out IEnumerable<string> setCookies))
                return 0;
            int count = 0;
            foreach (string header in setCookies)
            {
                count++;
                TryAddSetCookieHeader(header, defaultUri);
            }
            return count;
        }

        private void TryAddSetCookieHeader(string setCookieHeader, Uri defaultUri)
        {
            if (string.IsNullOrWhiteSpace(setCookieHeader)) return;
            try
            {
                _handler.CookieContainer.SetCookies(defaultUri, setCookieHeader);
            }
            catch
            {
                // ignore — fall back to manual parse
            }

            try
            {
                var cookie = ParseSetCookieHeader(setCookieHeader, defaultUri);
                if (cookie != null)
                {
                    RecordPortalCookie(cookie.Name, cookie.Value);
                    try
                    {
                        _handler.CookieContainer.Add(defaultUri, cookie);
                    }
                    catch
                    {
                        // duplicate path — store still has the value
                    }
                    if (string.Equals(cookie.Name, "JSESSIONID", StringComparison.OrdinalIgnoreCase))
                        PinJSessionId(cookie.Value);
                }
            }
            catch
            {
                // ignore malformed Set-Cookie
            }
        }

        private static Cookie ParseSetCookieHeader(string header, Uri defaultUri)
        {
            string[] parts = header.Split(';');
            if (parts.Length == 0) return null;
            int eq = parts[0].IndexOf('=');
            if (eq <= 0) return null;
            string name = parts[0].Substring(0, eq).Trim();
            string value = parts[0].Substring(eq + 1).Trim();
            if (name.Length == 0) return null;

            string path = "/";
            string domain = defaultUri.Host;
            bool httpOnly = false;
            bool secure = defaultUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);

            for (int i = 1; i < parts.Length; i++)
            {
                string attr = parts[i].Trim();
                if (attr.Length == 0) continue;
                int aeq = attr.IndexOf('=');
                string an = aeq > 0 ? attr.Substring(0, aeq).Trim() : attr;
                string av = aeq > 0 ? attr.Substring(aeq + 1).Trim() : "";
                if (string.Equals(an, "Path", StringComparison.OrdinalIgnoreCase) && av.Length > 0)
                    path = av;
                else if (string.Equals(an, "Domain", StringComparison.OrdinalIgnoreCase) && av.Length > 0)
                    domain = av.TrimStart('.');
                else if (string.Equals(an, "HttpOnly", StringComparison.OrdinalIgnoreCase))
                    httpOnly = true;
                else if (string.Equals(an, "Secure", StringComparison.OrdinalIgnoreCase))
                    secure = true;
            }

            return new Cookie(name, value, path, domain)
            {
                HttpOnly = httpOnly,
                Secure = secure,
            };
        }

        private static bool PortalHtmlIndicatesLoginPage(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return true;
            var lower = html.ToLowerInvariant();
            // Signed-in Angular shell also mentions "userCompany" in config — only match the real login form.
            if ((lower.Contains("name=\"j_username\"") || lower.Contains("name='j_username'"))
                && (lower.Contains("name=\"j_password\"") || lower.Contains("name='j_password'")))
                return true;
            if (lower.Contains("j_spring_security_check") && lower.Contains("action=")
                && lower.Contains("j_password") && lower.Contains("j_username"))
                return true;
            return false;
        }

        /// <summary>
        /// POST <c>/portal/filterdata</c> for the trip list (XHR). Requires a prior successful <see cref="GetPortalNuAsync"/> so <c>_csrf</c> can be resolved.
        /// Uses the same 10-sequence <c>filterList</c> shape as the portal trip list; sequence 7 carries <c>specificDate</c> (e.g. April 30, 2026).
        /// For the portal &quot;today&quot; slice only, pass <paramref name="usePeriodDayFilter"/> true (uses <c>{"period":"0d"}</c> instead of a calendar date).
        /// <paramref name="maxResults"/> sets both <c>maxResult</c> and <c>defaultSize</c> (clamped to 1–10000).
        /// <paramref name="page"/> selects the 1-based page when the portal caps results-per-page below <paramref name="maxResults"/>; iterate to collect a full day.
        /// </summary>
        public async Task<WellRydePortalFilterDataResult> PostTripFilterDataAsync(DateTime tripDate,
            string listDefId = null, bool usePeriodDayFilter = false, int maxResults = DefaultTripFilterMaxResult,
            int page = 1,
            CancellationToken cancellationToken = default)
        {
            LastTripFilterDataJson = null;
            if (maxResults < 1)
                maxResults = 1;
            if (maxResults > TripFilterMaxResultUpperBound)
                maxResults = TripFilterMaxResultUpperBound;
            if (page < 1) page = 1;
            string maxResultsStr = maxResults.ToString(CultureInfo.InvariantCulture);
            string pageStr = page.ToString(CultureInfo.InvariantCulture);
            if (string.IsNullOrEmpty(_lastPortalNuHtml))
            {
                var nu = await GetPortalNuAsync(cancellationToken).ConfigureAwait(false);
                if (!nu.IsSuccess)
                    return WellRydePortalFilterDataResult.Fail(nu.StatusCode,
                        nu.ErrorMessage ?? "GET /portal/nu required before filterdata.");
            }

            string csrf = ResolveAjaxCsrfToken();
            if (string.IsNullOrEmpty(csrf))
                return WellRydePortalFilterDataResult.Fail(null, "Could not find _csrf for filterdata. Sign in and load /portal/nu again.");

            listDefId = listDefId ?? DefaultTripFilterListDefId;
            string sequence7Value = usePeriodDayFilter
                ? JsonConvert.SerializeObject(new { period = "0d" })
                : JsonConvert.SerializeObject(new
                {
                    specificDate = tripDate.Date.ToString("MMMM d, yyyy", CultureInfo.GetCultureInfo("en-US"))
                });

            var filterList = new JArray(
                new JObject { ["sequence"] = "1", ["value"] = "-1" },
                new JObject { ["sequence"] = "2", ["value"] = "-1" },
                new JObject { ["sequence"] = "3", ["value"] = "-1" },
                new JObject { ["sequence"] = "4", ["value"] = "-1" },
                new JObject { ["sequence"] = "5", ["value"] = "-1" },
                new JObject { ["sequence"] = "6", ["value"] = "-1" },
                new JObject { ["sequence"] = "7", ["value"] = sequence7Value },
                new JObject { ["sequence"] = "8", ["value"] = "-1" },
                new JObject { ["sequence"] = "9", ["value"] = "-1" },
                new JObject { ["sequence"] = "10", ["value"] = "-1" }
            );
            string filterListStr = filterList.ToString(Formatting.None);
            const string filterArgsJson = "{}";

            var pairs = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("filterList", filterListStr),
                new KeyValuePair<string, string>("listDefId", listDefId),
                new KeyValuePair<string, string>("customListDefId", ""),
                new KeyValuePair<string, string>("canDelete", "false"),
                new KeyValuePair<string, string>("canEdit", "false"),
                new KeyValuePair<string, string>("canShow", "false"),
                new KeyValuePair<string, string>("canSelect", "true"),
                new KeyValuePair<string, string>("page", pageStr),
                new KeyValuePair<string, string>("currentPageSize", ""),
                new KeyValuePair<string, string>("maxResult", maxResultsStr),
                new KeyValuePair<string, string>("defaultSize", maxResultsStr),
                new KeyValuePair<string, string>("userDefaultFilter", "true"),
                new KeyValuePair<string, string>("filterArgsJson", filterArgsJson),
                new KeyValuePair<string, string>("filterValues", "[]"),
                new KeyValuePair<string, string>("_csrf", csrf),
            };

            HttpResponseMessage response = null;
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Post, FilterDataUri))
                {
                    request.Content = new FormUrlEncodedContent(pairs);
                    SetRequestAccept(request, "application/json, text/javascript, */*; q=0.01");
                    request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
                    request.Headers.TryAddWithoutValidation("Origin", PortalOrigin);
                    request.Headers.TryAddWithoutValidation("Referer", PortalNuUri.ToString());
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "cors");
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "empty");
                    request.Headers.TryAddWithoutValidation("Priority", "u=1, i");
                    ApplyAjaxCsrfHeaders(request, csrf);
                    ApplyPortalApiCookieHeader(request);

                    response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                return WellRydePortalFilterDataResult.Fail(null, ex.Message ?? "POST filterdata failed.");
            }

            string body;
            try
            {
                body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var code = response.StatusCode;
                response.Dispose();
                return WellRydePortalFilterDataResult.Fail(code, ex.Message ?? "Failed to read filterdata response.");
            }

            var statusCode = response.StatusCode;
            FinalizePortalResponse(response, FilterDataUri);
            response.Dispose();

            if ((int)statusCode < 200 || (int)statusCode >= 300)
                return WellRydePortalFilterDataResult.Fail(statusCode, "HTTP " + (int)statusCode + " from filterdata.", body);

            if (ResponseBodyLooksLikeHtml(body))
                return FailHtmlInsteadOfJson(statusCode, FilterDataUri.ToString(), body);

            LastTripFilterDataJson = body;
            return WellRydePortalFilterDataResult.Ok(statusCode, body);
        }

        /// <summary>
        /// POST <c>/portal/filterdata</c> for the WellRyde users list (Supey roster import).
        /// Same CSRF + cookie path as <see cref="PostTripFilterDataAsync"/> but with the user
        /// <c>listDefId</c> and the 3-sequence filter the portal sends for that page (Username /
        /// Email / CreatedDTTM all <c>"-1"</c> = no filter). Returns the raw JSON body for
        /// <see cref="WellRydeUserParser.ParseUsersList"/> to digest.
        /// </summary>
        /// <remarks>
        /// Pagination: <paramref name="maxResults"/> is the page size; <paramref name="page"/> is
        /// 1-based. Caller should look at <c>totalRecords</c> in the parsed result to decide
        /// whether to fetch more pages. <see cref="WellRydeRosterImporter"/> handles that loop.
        /// </remarks>
        public async Task<WellRydePortalFilterDataResult> PostUsersFilterDataAsync(
            int page = 1, int maxResults = DefaultUsersFilterMaxResult,
            CancellationToken cancellationToken = default)
        {
            if (maxResults < 1) maxResults = 1;
            if (maxResults > TripFilterMaxResultUpperBound) maxResults = TripFilterMaxResultUpperBound;
            if (page < 1) page = 1;
            string maxResultsStr = maxResults.ToString(CultureInfo.InvariantCulture);
            string pageStr = page.ToString(CultureInfo.InvariantCulture);

            var nu = await GetPortalNuAsync(cancellationToken).ConfigureAwait(false);
            if (!nu.IsSuccess)
                return WellRydePortalFilterDataResult.Fail(nu.StatusCode,
                    nu.ErrorMessage ?? "GET /portal/nu required before filterdata.");

            string csrf = ResolveAjaxCsrfToken();
            WellRydePortalLog.Info("FILTERDATA", "users filterdata POST csrfLen=" + (csrf?.Length ?? 0));
            if (string.IsNullOrEmpty(csrf))
                return WellRydePortalFilterDataResult.Fail(null,
                    "Could not find _csrf for users filterdata. Sign in and load /portal/nu again.");

            // Captured browser request uses 3 sequences (Username / Email / CreatedDTTM filters),
            // all set to "-1" meaning no filter. Order matters: the portal validates sequence ids.
            var filterList = new JArray(
                new JObject { ["sequence"] = "1", ["value"] = "-1" },
                new JObject { ["sequence"] = "2", ["value"] = "-1" },
                new JObject { ["sequence"] = "3", ["value"] = "-1" }
            );
            string filterListStr = filterList.ToString(Formatting.None);

            var pairs = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("filterList", filterListStr),
                new KeyValuePair<string, string>("listDefId", SupeyUsersListDefId),
                new KeyValuePair<string, string>("customListDefId", ""),
                new KeyValuePair<string, string>("canDelete", "false"),
                new KeyValuePair<string, string>("canEdit", "false"),
                new KeyValuePair<string, string>("canShow", "false"),
                new KeyValuePair<string, string>("canSelect", "true"),
                new KeyValuePair<string, string>("page", pageStr),
                new KeyValuePair<string, string>("currentPageSize", ""),
                new KeyValuePair<string, string>("maxResult", maxResultsStr),
                new KeyValuePair<string, string>("defaultSize", maxResultsStr),
                new KeyValuePair<string, string>("userDefaultFilter", "true"),
                new KeyValuePair<string, string>("filterArgsJson", "{}"),
                new KeyValuePair<string, string>("filterValues", "[]"),
                new KeyValuePair<string, string>("_csrf", csrf),
            };

            HttpResponseMessage response = null;
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Post, FilterDataUri))
                {
                    request.Content = new FormUrlEncodedContent(pairs);
                    SetRequestAccept(request, "application/json, text/javascript, */*; q=0.01");
                    request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
                    request.Headers.TryAddWithoutValidation("Origin", PortalOrigin);
                    request.Headers.TryAddWithoutValidation("Referer", PortalNuUri.ToString());
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "cors");
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "empty");
                    request.Headers.TryAddWithoutValidation("Priority", "u=1, i");
                    ApplyAjaxCsrfHeaders(request, csrf);
                    ApplyPortalApiCookieHeader(request);

                    response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                return WellRydePortalFilterDataResult.Fail(null, ex.Message ?? "POST users filterdata failed.");
            }

            string body;
            try
            {
                body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var code = response.StatusCode;
                response.Dispose();
                return WellRydePortalFilterDataResult.Fail(code, ex.Message ?? "Failed to read users filterdata response.");
            }

            var statusCode = response.StatusCode;
            FinalizePortalResponse(response, FilterDataUri);
            response.Dispose();
            string bodyPrefix = string.IsNullOrEmpty(body) ? "" : body.TrimStart().Substring(0, Math.Min(40, body.TrimStart().Length));
            WellRydePortalLog.Info("FILTERDATA",
                "users filterdata -> " + (int)statusCode + " html=" + (ResponseBodyLooksLikeHtml(body) ? "yes" : "no")
                + " body=" + bodyPrefix);

            if ((int)statusCode < 200 || (int)statusCode >= 300)
                return WellRydePortalFilterDataResult.Fail(statusCode,
                    "HTTP " + (int)statusCode + " from users filterdata.", body);

            if (ResponseBodyLooksLikeHtml(body))
                return FailHtmlInsteadOfJson(statusCode, FilterDataUri.ToString(), body);

            return WellRydePortalFilterDataResult.Ok(statusCode, body);
        }

        private List<string> CookieNamesForLog()
        {
            var names = new List<string>();
            foreach (var kv in SnapMergedPortalCookies())
                names.Add(kv.Key);
            return names;
        }

        /// <summary>GET <c>/portal/users/{secId}</c> (HTML detail page for one user). Caller hands the body
        /// to <see cref="WellRydeUserParser.ParseUserDetail"/>. We surface the raw HTML rather
        /// than parsing here so the network and parsing concerns stay separable / testable.
        /// </summary>
        /// <remarks>
        /// Requires a live session — the portal returns the login HTML otherwise. We don't
        /// auto-redirect because the caller's <c>EnsureWellRydePortalSessionForBillingAsync</c>
        /// already handles login flow before this endpoint is invoked. <paramref name="secId"/>
        /// is the opaque <c>SEC-...</c> key from the user list.
        /// </remarks>
        public async Task<WellRydePortalUserDetailResult> GetUserDetailHtmlAsync(string secId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(secId))
                return WellRydePortalUserDetailResult.Fail(null, "secId is required.");

            secId = NormalizeUserSecId(secId);
            if (secId.Length == 0)
                return WellRydePortalUserDetailResult.Fail(null, "A valid WellRyde SEC- id is required.");

            var uri = BuildUserDetailUri(secId);

            HttpResponseMessage response = null;
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
                {
                    SetRequestAccept(request, "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                    request.Headers.TryAddWithoutValidation("Referer", PortalNuUri.ToString());
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "cors");
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "empty");
                    ApplyPortalApiCookieHeader(request);

                    response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                return WellRydePortalUserDetailResult.Fail(null, ex.Message ?? "GET user detail failed.");
            }

            string body;
            try
            {
                body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var code = response.StatusCode;
                response.Dispose();
                return WellRydePortalUserDetailResult.Fail(code, ex.Message ?? "Failed to read user detail body.");
            }

            var statusCode = response.StatusCode;
            FinalizePortalResponse(response, uri);
            response.Dispose();

            if ((int)statusCode < 200 || (int)statusCode >= 300)
                return WellRydePortalUserDetailResult.Fail(statusCode, "HTTP " + (int)statusCode + " from user detail.", body);

            return WellRydePortalUserDetailResult.Ok(statusCode, body);
        }

        /// <summary>Clears cached <c>/portal/nu</c> HTML so the next portal call reloads CSRF/cookies.</summary>
        public void InvalidatePortalNuCache() => _lastPortalNuHtml = null;

        /// <summary>Extracts a bare <c>SEC-...</c> id from roster cells or portal link blobs.</summary>
        public static string NormalizeUserSecId(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "";
            raw = raw.Trim();
            if (raw.StartsWith("SEC-", StringComparison.OrdinalIgnoreCase) && raw.IndexOf('<') < 0)
            {
                int cut = raw.IndexOfAny(new[] { '?', '#', '/', ' ', '\r', '\n', '\t' });
                return cut > 0 ? raw.Substring(0, cut) : raw;
            }
            var m = UserSecIdRegex.Match(raw);
            return m.Success ? m.Value : "";
        }

        private static Uri BuildUserFormUri(string secId) =>
            new Uri(PortalOrigin + "/portal/users/" + secId + "?form");

        private static Uri BuildUserDetailUri(string secId) =>
            new Uri(PortalOrigin + "/portal/users/" + secId);

        private static bool ResponseBodyLooksLikeHtml(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return false;
            char c = body.TrimStart()[0];
            return c == '<';
        }

        private string DescribePortalSessionCookies() =>
            " (SESSION=" + (HasSessionCookie() ? "yes" : "no") + ")";

        private static void ApplyAjaxCsrfHeaders(HttpRequestMessage request, string csrf)
        {
            if (request == null || string.IsNullOrEmpty(csrf))
                return;
            request.Headers.TryAddWithoutValidation("X-XSRF-TOKEN", csrf);
        }

        private static void SetRequestAccept(HttpRequestMessage request, string acceptHeaderValue)
        {
            if (request == null) return;
            request.Headers.Accept.Clear();
            if (string.IsNullOrWhiteSpace(acceptHeaderValue)) return;
            foreach (var part in acceptHeaderValue.Split(','))
            {
                string p = part.Trim();
                if (p.Length > 0)
                    request.Headers.Accept.TryParseAdd(p);
            }
        }

        private static void SetDocumentNavigationHeaders(HttpRequestMessage request)
        {
            SetRequestAccept(request,
                "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
            request.Headers.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
        }

        private WellRydePortalFilterDataResult FailHtmlInsteadOfJson(
            HttpStatusCode? statusCode, string requestUrl, string body)
        {
            string hint = "WellRyde returned a login or HTML page instead of JSON";
            if (!string.IsNullOrWhiteSpace(body))
            {
                var lower = body.ToLowerInvariant();
                if (lower.StartsWith("<?xml", StringComparison.Ordinal))
                    hint = "WellRyde rejected the API call (XML error — sign in again)";
                else if (lower.Contains("j_spring_security_check") || lower.Contains("j_password"))
                    hint = "WellRyde session expired (login page)";
                else if (lower.Contains("access denied") || lower.Contains("forbidden"))
                    hint = "WellRyde denied access to this user";
                else if (lower.Contains("invalid csrf") || lower.Contains("csrf"))
                    hint = "WellRyde CSRF token rejected — sign in again from the Login bar";
            }
            WellRydePortalLog.Error("SAVE", hint + " for " + requestUrl + DescribePortalSessionCookies());
            return WellRydePortalFilterDataResult.Fail(statusCode,
                hint + " for " + requestUrl + DescribePortalSessionCookies()
                + ". Log out, use Login → WellRyde, then save again."
                + WellRydePortalLog.UserHintSuffix(),
                body);
        }

        /// <summary>GET <c>/portal/users/{secId}?form</c> — JSON used to populate nuUpdateUser.</summary>
        public async Task<WellRydePortalFilterDataResult> GetUserEditFormJsonAsync(
            string secId,
            CancellationToken cancellationToken = default)
        {
            secId = NormalizeUserSecId(secId);
            if (secId.Length == 0)
                return WellRydePortalFilterDataResult.Fail(null, "A valid WellRyde SEC- id is required.");

            var form = await TryFetchUserEditFormJsonAsync(secId, cancellationToken).ConfigureAwait(false);
            if (form.IsSuccess && HasUsableUserEditFormPayload(form.JsonBody))
                return form;

            var warm = await EnsureUsersAdminSessionAsync(cancellationToken).ConfigureAwait(false);
            if (!warm.IsSuccess)
                return form.IsSuccess ? form : warm;

            return await TryFetchUserEditFormJsonAsync(secId, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Browser loads <c>?form</c> plus <c>roles/selected</c> and <c>companies/selected</c> before save.
        /// </summary>
        public async Task<WellRydeUserEditContextResult> LoadUserEditContextAsync(
            string secId,
            CancellationToken cancellationToken = default)
        {
            secId = NormalizeUserSecId(secId);
            if (secId.Length == 0)
                return WellRydeUserEditContextResult.Fail(null, "A valid WellRyde SEC- id is required.");

            if (!HasSessionCookie())
                return WellRydeUserEditContextResult.Fail(null,
                    "WellRyde sign-in required — use Login → WellRyde.");

            if (string.IsNullOrEmpty(_lastPortalNuHtml) || !IsPortalNuPageAuthenticated())
            {
                var nu = await GetPortalNuAsync(cancellationToken).ConfigureAwait(false);
                if (!nu.IsSuccess)
                    return WellRydeUserEditContextResult.Fail(nu.StatusCode,
                        nu.ErrorMessage ?? "Could not load /portal/nu.");
            }

            // Browser edit flow: GET user page, ?form, roles/companies — not the users-grid filterdata POST.
            await PrimeUserEditSessionAsync(secId, cancellationToken).ConfigureAwait(false);

            var form = await GetUserEditFormJsonAsync(secId, cancellationToken).ConfigureAwait(false);
            if (!form.IsSuccess || string.IsNullOrWhiteSpace(form.JsonBody))
                return WellRydeUserEditContextResult.Fail(form.StatusCode,
                    form.ErrorMessage ?? "Could not load user edit form.");

            var roles = await GetUsersAdminSubresourceJsonAsync("roles/selected", secId, cancellationToken)
                .ConfigureAwait(false);
            var companies = await GetUsersAdminSubresourceJsonAsync("companies/selected", secId, cancellationToken)
                .ConfigureAwait(false);

            return WellRydeUserEditContextResult.Ok(
                form.JsonBody,
                roles.IsSuccess ? roles.JsonBody : null,
                companies.IsSuccess ? companies.JsonBody : null);
        }

        private async Task<WellRydePortalFilterDataResult> GetUsersAdminSubresourceJsonAsync(
            string subPath,
            string secId,
            CancellationToken cancellationToken)
        {
            string path = "/portal/users/" + (subPath ?? "").Trim().TrimStart('/');
            var uri = new Uri(PortalOrigin + path + "?id=" + secId);

            if (string.IsNullOrEmpty(_lastPortalNuHtml))
            {
                var nu = await GetPortalNuAsync(cancellationToken).ConfigureAwait(false);
                if (!nu.IsSuccess)
                    return WellRydePortalFilterDataResult.Fail(nu.StatusCode,
                        nu.ErrorMessage ?? "GET /portal/nu required before " + subPath);
            }

            HttpResponseMessage response = null;
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
                {
                    SetRequestAccept(request, "application/json, text/javascript, */*; q=0.01");
                    request.Headers.TryAddWithoutValidation("Referer", PortalNuUri.ToString());
                    request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "cors");
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "empty");
                    request.Headers.TryAddWithoutValidation("Priority", "u=1, i");
                    ApplyPortalApiCookieHeader(request);

                    response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                return WellRydePortalFilterDataResult.Fail(null, ex.Message ?? "GET " + subPath + " failed.");
            }

            string body;
            try
            {
                body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var code = response.StatusCode;
                response.Dispose();
                return WellRydePortalFilterDataResult.Fail(code, ex.Message ?? "Failed to read " + subPath);
            }

            var statusCode = response.StatusCode;
            FinalizePortalResponse(response, uri);
            response.Dispose();

            if ((int)statusCode < 200 || (int)statusCode >= 300)
                return WellRydePortalFilterDataResult.Fail(statusCode,
                    "HTTP " + (int)statusCode + " from " + subPath, body);

            if (ResponseBodyLooksLikeHtml(body))
                return FailHtmlInsteadOfJson(statusCode, uri.ToString(), body);

            return WellRydePortalFilterDataResult.Ok(statusCode, body);
        }

        private async Task<WellRydePortalFilterDataResult> TryFetchUserEditFormJsonAsync(
            string secId,
            CancellationToken cancellationToken)
        {
            var withForm = await FetchUserEditFormJsonOnceAsync(secId, withFormQuery: true, cancellationToken)
                .ConfigureAwait(false);
            if (withForm.IsSuccess && HasUsableUserEditFormPayload(withForm.JsonBody))
                return withForm;

            var plain = await FetchUserEditFormJsonOnceAsync(secId, withFormQuery: false, cancellationToken)
                .ConfigureAwait(false);
            if (plain.IsSuccess && HasUsableUserEditFormPayload(plain.JsonBody))
                return plain;

            return withForm.IsSuccess ? withForm : plain;
        }

        private async Task<WellRydePortalFilterDataResult> FetchUserEditFormJsonOnceAsync(
            string secId,
            bool withFormQuery,
            CancellationToken cancellationToken)
        {
            var uri = withFormQuery ? BuildUserFormUri(secId) : BuildUserDetailUri(secId);

            if (string.IsNullOrEmpty(_lastPortalNuHtml))
            {
                var nu = await GetPortalNuAsync(cancellationToken).ConfigureAwait(false);
                if (!nu.IsSuccess)
                    return WellRydePortalFilterDataResult.Fail(nu.StatusCode,
                        nu.ErrorMessage ?? "GET /portal/nu required before user edit form.");
            }

            PromoteJSessionCookieToPortalPaths();
            MirrorAllPortalCookiesToStandardPaths();

            var first = await FetchUserEditFormJsonHttpAsync(uri, cancellationToken).ConfigureAwait(false);
            if (first.IsSuccess && HasUsableUserEditFormPayload(first.JsonBody))
                return first;

            if (first.IsSuccess && ResponseBodyLooksLikeHtml(first.JsonBody)
                && !WellRydeUserEditFormHtmlParser.IsUserEditFormHtml(first.JsonBody))
            {
                await GetPortalNuAsync(cancellationToken).ConfigureAwait(false);
                var retry = await FetchUserEditFormJsonHttpAsync(uri, cancellationToken).ConfigureAwait(false);
                if (retry.IsSuccess && HasUsableUserEditFormPayload(retry.JsonBody))
                    return retry;
                return retry;
            }

            return first;
        }

        private async Task<WellRydePortalFilterDataResult> FetchUserEditFormJsonHttpAsync(
            Uri uri,
            CancellationToken cancellationToken)
        {
            HttpResponseMessage response = null;
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
                {
                    SetRequestAccept(request, "application/json, text/plain, */*");
                    request.Headers.TryAddWithoutValidation("Referer", PortalNuUri.ToString());
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "cors");
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "empty");
                    request.Headers.TryAddWithoutValidation("Priority", "u=1, i");
                    ApplyPortalApiCookieHeader(request);

                    response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                return WellRydePortalFilterDataResult.Fail(null, ex.Message ?? "GET user edit form failed.");
            }

            string body;
            try
            {
                body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var code = response.StatusCode;
                response.Dispose();
                return WellRydePortalFilterDataResult.Fail(code, ex.Message ?? "Failed to read user edit form.");
            }

            var statusCode = response.StatusCode;
            FinalizePortalResponse(response, uri);
            response.Dispose();

            WellRydePortalLog.Info("USER-FORM",
                uri.AbsolutePath + " -> " + (int)statusCode
                + " html=" + (ResponseBodyLooksLikeHtml(body) ? "yes" : "no")
                + " editForm=" + (WellRydeUserEditFormHtmlParser.IsUserEditFormHtml(body) ? "yes" : "no"));

            if ((int)statusCode < 200 || (int)statusCode >= 300)
                return WellRydePortalFilterDataResult.Fail(statusCode,
                    "HTTP " + (int)statusCode + " from user edit form.", body);

            return NormalizeUserEditFormBody(statusCode, uri.ToString(), body);
        }

        /// <summary>Browser <c>?form</c> returns HTML with <c>form#user</c>; convert to JSON for the save pipeline.</summary>
        private WellRydePortalFilterDataResult NormalizeUserEditFormBody(
            HttpStatusCode statusCode,
            string requestUrl,
            string body)
        {
            if (WellRydeUserEditFormHtmlParser.IsUserEditFormHtml(body))
            {
                string json = WellRydeUserEditFormHtmlParser.ToFormJson(body);
                if (!string.IsNullOrWhiteSpace(json) && json.StartsWith("{"))
                    return WellRydePortalFilterDataResult.Ok(statusCode, json);
            }

            if (ResponseBodyLooksLikeHtml(body))
                return FailHtmlInsteadOfJson(statusCode, requestUrl, body);

            return WellRydePortalFilterDataResult.Ok(statusCode, body);
        }

        private static bool HasUsableUserEditFormPayload(string body) =>
            !string.IsNullOrWhiteSpace(body)
            && !PortalHtmlIndicatesLoginPage(body)
            && (!ResponseBodyLooksLikeHtml(body) || WellRydeUserEditFormHtmlParser.IsUserEditFormHtml(body));

        /// <summary>Browser GET <c>/portal/users/{sec}</c> (no ?form) before opening the edit form.</summary>
        private async Task PrimeUserEditSessionAsync(string secId, CancellationToken cancellationToken)
        {
            var summaryUri = BuildUserDetailUri(secId);
            HttpResponseMessage response = null;
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, summaryUri))
                {
                    SetRequestAccept(request, "application/json, text/plain, */*");
                    request.Headers.TryAddWithoutValidation("Referer", PortalNuUri.ToString());
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "cors");
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "empty");
                    request.Headers.TryAddWithoutValidation("Priority", "u=1, i");
                    ApplyPortalApiCookieHeader(request);

                    response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch
            {
                return;
            }

            try
            {
                await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch
            {
                // ignore
            }

            FinalizePortalResponse(response, summaryUri);
            response.Dispose();
        }

        /// <summary>POST <c>/portal/users/nuUpdateUser</c> (multipart) — Supey driver home address sync.</summary>
        public async Task<WellRydePortalSaveBillResult> PostNuUpdateUserAsync(
            IReadOnlyDictionary<string, string> fields,
            CancellationToken cancellationToken = default)
        {
            if (fields == null || fields.Count == 0)
                return WellRydePortalSaveBillResult.Fail(null, "No form fields for nuUpdateUser.");

            if (string.IsNullOrEmpty(_lastPortalNuHtml))
            {
                var nu = await GetPortalNuAsync(cancellationToken).ConfigureAwait(false);
                if (!nu.IsSuccess)
                    return WellRydePortalSaveBillResult.Fail(nu.StatusCode,
                        nu.ErrorMessage ?? "GET /portal/nu required before nuUpdateUser.");
            }

            using (var multipart = new MultipartFormDataContent())
            {
                foreach (var kv in fields)
                {
                    if (string.IsNullOrEmpty(kv.Key))
                        continue;
                    if (string.Equals(kv.Key, "userProfilePicture", StringComparison.OrdinalIgnoreCase))
                        continue;
                    multipart.Add(new StringContent(kv.Value ?? ""), kv.Key);
                }

                // Browser sends an empty file part; .NET requires a non-empty fileName.
                multipart.Add(new ByteArrayContent(Array.Empty<byte>()), "userProfilePicture", "profile.png");

                HttpResponseMessage response = null;
                try
                {
                    using (var request = new HttpRequestMessage(HttpMethod.Post, NuUpdateUserUri))
                    {
                        request.Content = multipart;
                        request.Headers.TryAddWithoutValidation("Accept", "*/*");
                        request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
                        request.Headers.TryAddWithoutValidation("Origin", PortalOrigin);
                        request.Headers.TryAddWithoutValidation("Referer", PortalNuUri.ToString());
                        request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
                        request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "cors");
                        request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "empty");
                        ApplyPortalApiCookieHeader(request);

                        response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    return WellRydePortalSaveBillResult.Fail(null, ex.Message ?? "POST nuUpdateUser failed.");
                }

                string body;
                try
                {
                    body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    var code = response.StatusCode;
                    response.Dispose();
                    return WellRydePortalSaveBillResult.Fail(code, ex.Message ?? "Failed to read nuUpdateUser response.");
                }

                var statusCode = response.StatusCode;
                FinalizePortalResponse(response, NuUpdateUserUri);
                response.Dispose();

                string bodyNote = string.IsNullOrEmpty(body) ? "" : body.Trim();
                if (bodyNote.Length > 120)
                    bodyNote = bodyNote.Substring(0, 120) + "…";
                WellRydePortalLog.Info("NUUPDATE", "nuUpdateUser -> " + (int)statusCode + " body=" + bodyNote);

                if ((int)statusCode < 200 || (int)statusCode >= 300)
                    return WellRydePortalSaveBillResult.Fail(statusCode,
                        "HTTP " + (int)statusCode + " from nuUpdateUser.", body);

                return WellRydePortalSaveBillResult.Ok(statusCode, body);
            }
        }

        /// <summary>CSRF from last <c>/portal/nu</c> load — required for nuUpdateUser.</summary>
        public string GetAjaxCsrfToken() => ResolveAjaxCsrfToken();

        /// <summary>
        /// POST <c>/portal/trip/saveBillData</c> (XHR). Body matches browser: <c>formData</c> JSON array, <c>saveSubmit=true</c>, <c>_csrf</c>.
        /// </summary>
        public async Task<WellRydePortalSaveBillResult> PostSaveBillDataAsync(string formDataJson,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(_lastPortalNuHtml))
            {
                var nu = await GetPortalNuAsync(cancellationToken).ConfigureAwait(false);
                if (!nu.IsSuccess)
                    return WellRydePortalSaveBillResult.Fail(nu.StatusCode,
                        nu.ErrorMessage ?? "GET /portal/nu required before saveBillData.");
            }

            string csrf = ResolveAjaxCsrfToken();
            if (string.IsNullOrEmpty(csrf))
                return WellRydePortalSaveBillResult.Fail(null, "Could not find _csrf for saveBillData. Load /portal/nu again.");

            var pairs = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("formData", formDataJson ?? "[]"),
                new KeyValuePair<string, string>("saveSubmit", "true"),
                new KeyValuePair<string, string>("_csrf", csrf),
            };

            HttpResponseMessage response = null;
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Post, SaveBillDataUri))
                {
                    request.Content = new FormUrlEncodedContent(pairs);
                    request.Headers.TryAddWithoutValidation("Accept", "*/*");
                    request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
                    request.Headers.TryAddWithoutValidation("Origin", PortalOrigin);
                    request.Headers.TryAddWithoutValidation("Referer", PortalNuUri.ToString());
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "cors");
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "empty");
                    request.Headers.TryAddWithoutValidation("Priority", "u=0, i");
                    ApplyPortalApiCookieHeader(request);

                    response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                return WellRydePortalSaveBillResult.Fail(null, ex.Message ?? "POST saveBillData failed.");
            }

            string body;
            try
            {
                body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var code = response.StatusCode;
                response.Dispose();
                return WellRydePortalSaveBillResult.Fail(code, ex.Message ?? "Failed to read saveBillData response.");
            }

            var statusCode = response.StatusCode;
            FinalizePortalResponse(response, SaveBillDataUri);
            response.Dispose();

            if ((int)statusCode < 200 || (int)statusCode >= 300)
                return WellRydePortalSaveBillResult.Fail(statusCode, "HTTP " + (int)statusCode + " from saveBillData.", body);

            // Legacy UI compared body to "SUCCESS"; current portal may return other 2xx bodies—treat HTTP success as submit accepted.
            return WellRydePortalSaveBillResult.Ok(statusCode, body);
        }

        /// <summary>GET driver list for trip assignment (same endpoint as legacy <c>WRTripDownloader.GetAllDrivers</c>).</summary>
        public async Task<List<WRDrivers>> GetAllDriversForTripAssignmentAsync(CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(_lastPortalNuHtml))
            {
                var nu = await GetPortalNuAsync(cancellationToken).ConfigureAwait(false);
                if (!nu.IsSuccess)
                    return new List<WRDrivers>();
            }

            HttpResponseMessage response = null;
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, GetAllDriversForTripAssignmentUri))
                {
                    request.Headers.TryAddWithoutValidation("Accept", "application/json, text/javascript, */*; q=0.01");
                    request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
                    request.Headers.TryAddWithoutValidation("Referer", PortalNuUri.ToString());
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "cors");
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "empty");
                    ApplyPortalApiCookieHeader(request);

                    response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch
            {
                return new List<WRDrivers>();
            }

            string body;
            try
            {
                body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch
            {
                response?.Dispose();
                return new List<WRDrivers>();
            }

            var statusCode = response.StatusCode;
            FinalizePortalResponse(response, GetAllDriversForTripAssignmentUri);
            response.Dispose();
            LastPortalCookies = SnapMergedPortalCookies();

            if ((int)statusCode < 200 || (int)statusCode >= 300)
                return new List<WRDrivers>();

            try
            {
                return JsonConvert.DeserializeObject<List<WRDrivers>>(body) ?? new List<WRDrivers>();
            }
            catch
            {
                return new List<WRDrivers>();
            }
        }

        /// <summary>
        /// Unassign trips on the portal: <c>POST /portal/trip/unAssignValidation</c> then <c>POST /portal/trip/unassign</c> (browser order).
        /// </summary>
        public async Task<WellRydePortalTripMutationResult> PostUnassignTripsAsync(IReadOnlyList<string> tripUuids,
            CancellationToken cancellationToken = default)
        {
            string joined = JoinTripUuids(tripUuids);
            if (string.IsNullOrEmpty(joined))
                return WellRydePortalTripMutationResult.Fail(null, "No trip UUIDs to unassign.");

            var validationFields = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("tripUUIDs", joined),
            };
            var r1 = await PostTripAjaxFormAsync(TripUnAssignValidationUri, validationFields, cancellationToken).ConfigureAwait(false);
            if (!r1.IsSuccess)
                return r1;

            var unassignFields = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("vizzonIds", joined),
                new KeyValuePair<string, string>("viewName", "trip"),
            };
            return await PostTripAjaxFormAsync(TripUnassignUri, unassignFields, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Assign trips to a driver: <c>assignTrips</c> → <c>assignValidation</c> → <c>assignTripDriver</c> (browser order).
        /// </summary>
        public async Task<WellRydePortalTripMutationResult> PostAssignTripsToDriverAsync(string driverId,
            IReadOnlyList<string> tripUuids, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(driverId))
                return WellRydePortalTripMutationResult.Fail(null, "driverId is required.");

            string joined = JoinTripUuids(tripUuids);
            if (string.IsNullOrEmpty(joined))
                return WellRydePortalTripMutationResult.Fail(null, "No trip UUIDs to assign.");

            var tripOnly = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("tripUUIDs", joined),
            };
            var r1 = await PostTripAjaxFormAsync(TripAssignTripsUri, tripOnly, cancellationToken).ConfigureAwait(false);
            if (!r1.IsSuccess)
                return r1;

            var validationFields = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("driverId", driverId),
                new KeyValuePair<string, string>("tripUUIDs", joined),
                new KeyValuePair<string, string>("isProvider", "false"),
            };
            var r2 = await PostTripAjaxFormAsync(TripAssignValidationUri, validationFields, cancellationToken).ConfigureAwait(false);
            if (!r2.IsSuccess)
                return r2;

            var driverFields = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("tripUUIDs", joined),
                new KeyValuePair<string, string>("driverId", driverId),
                new KeyValuePair<string, string>("hasAssigned", "1"),
            };
            return await PostTripAjaxFormAsync(TripAssignTripDriverUri, driverFields, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// GET <c>/portal/avl/avlinitiate</c> — fetches every driver currently visible on the live map.
        /// The portal expects an <c>avlFilterCriteria</c> JSON with bounding box; we send a continent-sized box
        /// so a single call returns the full fleet (matches the field names captured from the browser request).
        /// </summary>
        public async Task<List<WRDriverPosition>> GetDriverPositionsAsync(CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(_lastPortalNuHtml))
            {
                var nu = await GetPortalNuAsync(cancellationToken).ConfigureAwait(false);
                if (!nu.IsSuccess)
                    return new List<WRDriverPosition>();
            }

            // Continent-sized bounds so the portal returns every driver in one shot.
            var avlFilter = new JObject
            {
                ["avlMapSearch"] = new JObject
                {
                    ["centerLatLng"] = new JObject { ["lat"] = "39.000000", ["lng"] = "-95.000000" },
                    ["northEastBoundsLatLng"] = new JObject { ["lat"] = "71.000000", ["lng"] = "-30.000000" },
                    ["southWestBoundsLatLng"] = new JObject { ["lat"] = "15.000000", ["lng"] = "-170.000000" },
                },
            };

            var qs = new System.Text.StringBuilder();
            qs.Append("avlFilterCriteria=").Append(Uri.EscapeDataString(avlFilter.ToString(Formatting.None)));
            qs.Append("&avlQueryDate=");
            qs.Append("&quickSearchJsonArr=").Append(Uri.EscapeDataString("[]"));
            qs.Append("&riderSearchJSON=");

            var requestUri = new Uri(AvlInitiateUri + "?" + qs);

            HttpResponseMessage response = null;
            string body;
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, requestUri))
                {
                    request.Headers.TryAddWithoutValidation("Accept", "application/json, text/javascript, */*; q=0.01");
                    request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
                    request.Headers.TryAddWithoutValidation("Referer", PortalOrigin + "/portal/avl/");
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "cors");
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "empty");
                    ApplyPortalApiCookieHeader(request);

                    response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                        .ConfigureAwait(false);
                }
                body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch
            {
                response?.Dispose();
                return new List<WRDriverPosition>();
            }

            var statusCode = response.StatusCode;
            FinalizePortalResponse(response, requestUri);
            response.Dispose();

            if ((int)statusCode < 200 || (int)statusCode >= 300 || string.IsNullOrWhiteSpace(body))
                return new List<WRDriverPosition>();

            try
            {
                var root = JObject.Parse(body);
                var arr = root["drivers"] as JArray;
                if (arr == null)
                    return new List<WRDriverPosition>();
                return arr.ToObject<List<WRDriverPosition>>() ?? new List<WRDriverPosition>();
            }
            catch
            {
                return new List<WRDriverPosition>();
            }
        }

        private static string JoinTripUuids(IEnumerable<string> tripUuids)
        {
            if (tripUuids == null)
                return null;
            var list = tripUuids.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList();
            return list.Count == 0 ? null : string.Join(",", list);
        }

        private async Task<WellRydePortalTripMutationResult> PostTripAjaxFormAsync(Uri requestUri,
            List<KeyValuePair<string, string>> fields, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(_lastPortalNuHtml))
            {
                var nu = await GetPortalNuAsync(cancellationToken).ConfigureAwait(false);
                if (!nu.IsSuccess)
                    return WellRydePortalTripMutationResult.Fail(nu.StatusCode,
                        nu.ErrorMessage ?? "GET /portal/nu required before trip mutation.");
            }

            string csrf = ResolveAjaxCsrfToken();
            if (string.IsNullOrEmpty(csrf))
                return WellRydePortalTripMutationResult.Fail(null, "Could not find _csrf. Load /portal/nu again.");

            var pairs = new List<KeyValuePair<string, string>>(fields);
            pairs.Add(new KeyValuePair<string, string>("_csrf", csrf));

            HttpResponseMessage response = null;
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Post, requestUri))
                {
                    request.Content = new FormUrlEncodedContent(pairs);
                    request.Headers.TryAddWithoutValidation("Accept", "*/*");
                    request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
                    request.Headers.TryAddWithoutValidation("Origin", PortalOrigin);
                    request.Headers.TryAddWithoutValidation("Referer", PortalNuUri.ToString());
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "cors");
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "empty");
                    request.Headers.TryAddWithoutValidation("Priority", "u=1, i");
                    ApplyPortalApiCookieHeader(request);

                    response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                return WellRydePortalTripMutationResult.Fail(null, ex.Message ?? "POST " + requestUri + " failed.");
            }

            string body;
            try
            {
                body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var code = response.StatusCode;
                response.Dispose();
                return WellRydePortalTripMutationResult.Fail(code, ex.Message ?? "Failed to read response.", null);
            }

            var statusCode = response.StatusCode;
            FinalizePortalResponse(response, requestUri);
            response.Dispose();

            if ((int)statusCode < 200 || (int)statusCode >= 300)
                return WellRydePortalTripMutationResult.Fail(statusCode, "HTTP " + (int)statusCode + " from " + requestUri.AbsolutePath + ".", body);

            return WellRydePortalTripMutationResult.Ok(statusCode, body);
        }

        /// <summary>Spring CSRF for portal AJAX: hidden field, meta, JSON snippet, or <c>XSRF-TOKEN</c> cookie.</summary>
        private string ResolveAjaxCsrfToken()
        {
            if (!string.IsNullOrEmpty(_lastPortalNuHtml))
            {
                string t = ExtractHiddenInputValue(_lastPortalNuHtml, "_csrf");
                if (!string.IsNullOrEmpty(t))
                    return t;
                Match m = Regex.Match(_lastPortalNuHtml, @"<meta\s+name=""_csrf""\s+content=""([^""]+)""",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (m.Success)
                    return WebUtility.HtmlDecode(m.Groups[1].Value);
                m = Regex.Match(_lastPortalNuHtml, @"""_csrf""\s*:\s*""([^""]+)""",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (m.Success)
                    return m.Groups[1].Value;
                m = Regex.Match(_lastPortalNuHtml, @"""csrf""\s*:\s*""([^""]+)""",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (m.Success)
                    return m.Groups[1].Value;
            }

            foreach (Cookie c in _handler.CookieContainer.GetCookies(PortalNuUri))
            {
                if (!string.Equals(c.Name, "XSRF-TOKEN", StringComparison.OrdinalIgnoreCase))
                    continue;
                try
                {
                    string decoded = Uri.UnescapeDataString(c.Value);
                    int pipe = decoded.IndexOf('|');
                    if (pipe > 0)
                        return decoded.Substring(0, pipe);
                    return decoded;
                }
                catch
                {
                    return c.Value;
                }
            }

            return null;
        }

        /// <summary>Union of cookies for portal paths we touch (root, <c>/portal/</c>, login POST, <c>/portal/nu</c>).</summary>
        private Dictionary<string, string> SnapMergedPortalCookies()
        {
            var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            void AddFrom(Uri uri)
            {
                foreach (Cookie c in _handler.CookieContainer.GetCookies(uri))
                    merged[c.Name] = c.Value;
            }
            AddFrom(PortalRootUri);
            AddFrom(new Uri(PortalOrigin + "/portal/"));
            AddFrom(FilterDataUri);
            AddFrom(PortalUsersBaseUri);
            AddFrom(SpringLoginUri);
            AddFrom(PortalNuUri);
            return merged;
        }

        /// <summary>Heuristic only: whether the server accepted credentials. Does not perform any HTTP redirect.</summary>
        private static bool InterpretSpringLoginResponse(HttpStatusCode status, string location, string html)
        {
            int code = (int)status;
            if (code == 401 || code == 403)
                return false;
            if (code >= 400)
                return false;

            if (code >= 300 && code < 400)
            {
                if (string.IsNullOrEmpty(location))
                    return true;
                var loc = location.ToLowerInvariant();
                if (loc.Contains("error=") || loc.Contains("error?") || loc.Contains("/error"))
                    return false;
                if (loc.Contains("login") && (loc.Contains("error") || loc.Contains("invalid") || loc.Contains("bad")))
                    return false;
                return true;
            }

            if (status == HttpStatusCode.OK && !string.IsNullOrEmpty(html))
            {
                var lower = html.ToLowerInvariant();
                if (lower.Contains("bad credentials") || lower.Contains("locked") || lower.Contains("invalid password"))
                    return false;
                if (lower.Contains("j_spring_security_check") && lower.Contains("j_password") &&
                    lower.Contains("name=\"j_username\""))
                    return false;
                return true;
            }

            return code >= 200 && code < 300;
        }

        private Dictionary<string, string> SnapCookiesForUri(Uri uri)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Cookie c in _handler.CookieContainer.GetCookies(uri))
                dict[c.Name] = c.Value;
            return dict;
        }

        private static string ExtractHiddenInputValue(string html, string name)
        {
            if (string.IsNullOrEmpty(html) || string.IsNullOrEmpty(name))
                return null;

            string escaped = Regex.Escape(name);

            Match m = Regex.Match(html,
                @"<input[^>]+name=""" + escaped + @"""[^>]+value=""([^""]*)""",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (m.Success)
                return WebUtility.HtmlDecode(m.Groups[1].Value);

            m = Regex.Match(html,
                @"<input[^>]+value=""([^""]*)""[^>]+name=""" + escaped + @"""",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (m.Success)
                return WebUtility.HtmlDecode(m.Groups[1].Value);

            m = Regex.Match(html,
                @"<input[^>]+name='" + escaped + @"'[^>]+value='([^']*)'",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (m.Success)
                return WebUtility.HtmlDecode(m.Groups[1].Value);

            m = Regex.Match(html,
                @"<input[^>]+value='([^']*)'[^>]+name='" + escaped + @"'",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (m.Success)
                return WebUtility.HtmlDecode(m.Groups[1].Value);

            return null;
        }

        private Dictionary<string, string> SnapCookiesForPortal()
        {
            return SnapCookiesForUri(PortalRootUri);
        }

        private static string ExtractRequestVerificationToken(string html)
        {
            if (string.IsNullOrEmpty(html))
                return null;

            Match m = Regex.Match(html, @"name=""__RequestVerificationToken""\s+type=""hidden""\s+value=""([^""]+)""",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (m.Success)
                return m.Groups[1].Value;

            m = Regex.Match(html, @"type=""hidden""\s+name=""__RequestVerificationToken""\s+value=""([^""]+)""",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (m.Success)
                return m.Groups[1].Value;

            m = Regex.Match(html, @"<input[^>]+name=""__RequestVerificationToken""[^>]+value=""([^""]+)""",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (m.Success)
                return m.Groups[1].Value;

            m = Regex.Match(html, @"<input[^>]+value=""([^""]+)""[^>]+name=""__RequestVerificationToken""",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (m.Success)
                return m.Groups[1].Value;

            m = Regex.Match(html, @"<meta\s+name=""csrf-token""\s+content=""([^""]+)""",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (m.Success)
                return m.Groups[1].Value;

            return null;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _lastBootstrapHtml = null;
            _lastPortalNuHtml = null;
            _pinnedJSessionId = null;
            _portalCookieStore.Clear();
            LastTripFilterDataJson = null;
            _client.Dispose();
            _handler.Dispose();
        }
    }

    internal sealed class WellRydePortalLoginResult
    {
        private WellRydePortalLoginResult(bool success, HttpStatusCode? statusCode, string errorMessage, string location,
            IReadOnlyDictionary<string, string> cookies)
        {
            IsSuccess = success;
            StatusCode = statusCode;
            ErrorMessage = errorMessage;
            Location = location;
            Cookies = cookies ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public bool IsSuccess { get; }
        public HttpStatusCode? StatusCode { get; }
        public string ErrorMessage { get; }
        /// <summary>Raw <c>Location</c> response header resolved to an absolute URL when relative. Not followed by HttpClient; use only for a manual next request if you choose.</summary>
        public string Location { get; }
        public IReadOnlyDictionary<string, string> Cookies { get; }

        public static WellRydePortalLoginResult Ok(HttpStatusCode statusCode, string location,
            IReadOnlyDictionary<string, string> cookies)
        {
            return new WellRydePortalLoginResult(true, statusCode, null, location, cookies);
        }

        public static WellRydePortalLoginResult Fail(HttpStatusCode? statusCode, string errorMessage, string location = null,
            IReadOnlyDictionary<string, string> cookies = null)
        {
            return new WellRydePortalLoginResult(false, statusCode, errorMessage, location, cookies);
        }
    }

    internal sealed class WellRydePortalNuResult
    {
        private WellRydePortalNuResult(bool success, HttpStatusCode? statusCode, string errorMessage)
        {
            IsSuccess = success;
            StatusCode = statusCode;
            ErrorMessage = errorMessage;
        }

        public bool IsSuccess { get; }
        public HttpStatusCode? StatusCode { get; }
        public string ErrorMessage { get; }

        public static WellRydePortalNuResult Ok(HttpStatusCode statusCode)
        {
            return new WellRydePortalNuResult(true, statusCode, null);
        }

        public static WellRydePortalNuResult Fail(HttpStatusCode? statusCode, string errorMessage)
        {
            return new WellRydePortalNuResult(false, statusCode, errorMessage);
        }
    }

    internal sealed class WellRydePortalFilterDataResult
    {
        private WellRydePortalFilterDataResult(bool success, HttpStatusCode? statusCode, string errorMessage, string jsonBody)
        {
            IsSuccess = success;
            StatusCode = statusCode;
            ErrorMessage = errorMessage;
            JsonBody = jsonBody;
        }

        public bool IsSuccess { get; }
        public HttpStatusCode? StatusCode { get; }
        public string ErrorMessage { get; }
        public string JsonBody { get; }

        public static WellRydePortalFilterDataResult Ok(HttpStatusCode statusCode, string jsonBody)
        {
            return new WellRydePortalFilterDataResult(true, statusCode, null, jsonBody);
        }

        public static WellRydePortalFilterDataResult Fail(HttpStatusCode? statusCode, string errorMessage, string jsonBody = null)
        {
            return new WellRydePortalFilterDataResult(false, statusCode, errorMessage, jsonBody);
        }
    }

    /// <summary>
    /// Result of a <c>GET /portal/users/{secId}</c> call. <see cref="HtmlBody"/> is the raw HTML
    /// (may be present even on a failure to help diagnostics — e.g. a 200 with the login form means
    /// session expired).
    /// </summary>
    internal sealed class WellRydePortalUserDetailResult
    {
        private WellRydePortalUserDetailResult(bool success, HttpStatusCode? statusCode, string errorMessage, string htmlBody)
        {
            IsSuccess = success;
            StatusCode = statusCode;
            ErrorMessage = errorMessage;
            HtmlBody = htmlBody;
        }

        public bool IsSuccess { get; }
        public HttpStatusCode? StatusCode { get; }
        public string ErrorMessage { get; }
        public string HtmlBody { get; }

        public static WellRydePortalUserDetailResult Ok(HttpStatusCode statusCode, string htmlBody)
        {
            return new WellRydePortalUserDetailResult(true, statusCode, null, htmlBody);
        }

        public static WellRydePortalUserDetailResult Fail(HttpStatusCode? statusCode, string errorMessage, string htmlBody = null)
        {
            return new WellRydePortalUserDetailResult(false, statusCode, errorMessage, htmlBody);
        }
    }

    internal sealed class WellRydePortalSaveBillResult
    {
        private WellRydePortalSaveBillResult(bool success, HttpStatusCode? statusCode, string errorMessage, string responseBody)
        {
            IsSuccess = success;
            StatusCode = statusCode;
            ErrorMessage = errorMessage;
            ResponseBody = responseBody;
        }

        public bool IsSuccess { get; }
        public HttpStatusCode? StatusCode { get; }
        public string ErrorMessage { get; }
        public string ResponseBody { get; }

        public static WellRydePortalSaveBillResult Ok(HttpStatusCode statusCode, string responseBody)
        {
            return new WellRydePortalSaveBillResult(true, statusCode, null, responseBody);
        }

        public static WellRydePortalSaveBillResult Fail(HttpStatusCode? statusCode, string errorMessage, string responseBody = null)
        {
            return new WellRydePortalSaveBillResult(false, statusCode, errorMessage, responseBody);
        }
    }

    internal sealed class WellRydePortalBootstrapResult
    {
        private WellRydePortalBootstrapResult(bool success, HttpStatusCode? statusCode, Uri finalUri, string errorMessage,
            IReadOnlyDictionary<string, string> cookies, string requestVerificationToken)
        {
            IsSuccess = success;
            StatusCode = statusCode;
            FinalUri = finalUri;
            ErrorMessage = errorMessage;
            Cookies = cookies ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            RequestVerificationToken = requestVerificationToken;
        }

        public bool IsSuccess { get; }
        public HttpStatusCode? StatusCode { get; }
        public Uri FinalUri { get; }
        public string ErrorMessage { get; }
        public IReadOnlyDictionary<string, string> Cookies { get; }
        public string RequestVerificationToken { get; }

        public static WellRydePortalBootstrapResult Ok(HttpStatusCode statusCode, Uri finalUri,
            IReadOnlyDictionary<string, string> cookies, string requestVerificationToken)
        {
            return new WellRydePortalBootstrapResult(true, statusCode, finalUri, null, cookies, requestVerificationToken);
        }

        public static WellRydePortalBootstrapResult Fail(HttpStatusCode? statusCode, string errorMessage,
            IReadOnlyDictionary<string, string> cookies = null, string requestVerificationToken = null)
        {
            return new WellRydePortalBootstrapResult(false, statusCode, null, errorMessage, cookies, requestVerificationToken);
        }
    }

    internal sealed class WellRydePortalTripMutationResult
    {
        private WellRydePortalTripMutationResult(bool success, HttpStatusCode? statusCode, string errorMessage, string responseBody)
        {
            IsSuccess = success;
            StatusCode = statusCode;
            ErrorMessage = errorMessage;
            ResponseBody = responseBody;
        }

        public bool IsSuccess { get; }
        public HttpStatusCode? StatusCode { get; }
        public string ErrorMessage { get; }
        public string ResponseBody { get; }

        public static WellRydePortalTripMutationResult Ok(HttpStatusCode statusCode, string responseBody)
        {
            return new WellRydePortalTripMutationResult(true, statusCode, null, responseBody);
        }

        public static WellRydePortalTripMutationResult Fail(HttpStatusCode? statusCode, string errorMessage, string responseBody = null)
        {
            return new WellRydePortalTripMutationResult(false, statusCode, errorMessage, responseBody);
        }
    }

    internal sealed class WellRydeUserEditContextResult
    {
        private WellRydeUserEditContextResult(
            bool success, HttpStatusCode? statusCode, string errorMessage,
            string formJson, string rolesSelectedJson, string companiesSelectedJson)
        {
            IsSuccess = success;
            StatusCode = statusCode;
            ErrorMessage = errorMessage;
            FormJson = formJson;
            RolesSelectedJson = rolesSelectedJson;
            CompaniesSelectedJson = companiesSelectedJson;
        }

        public bool IsSuccess { get; }
        public HttpStatusCode? StatusCode { get; }
        public string ErrorMessage { get; }
        public string FormJson { get; }
        public string RolesSelectedJson { get; }
        public string CompaniesSelectedJson { get; }

        public static WellRydeUserEditContextResult Ok(
            string formJson, string rolesSelectedJson, string companiesSelectedJson)
        {
            return new WellRydeUserEditContextResult(true, null, null, formJson, rolesSelectedJson, companiesSelectedJson);
        }

        public static WellRydeUserEditContextResult Fail(HttpStatusCode? statusCode, string errorMessage)
        {
            return new WellRydeUserEditContextResult(false, statusCode, errorMessage, null, null, null);
        }
    }

    internal sealed class WellRydePortalSessionProbeResult
    {
        private WellRydePortalSessionProbeResult(bool success, string errorMessage)
        {
            IsSuccess = success;
            ErrorMessage = errorMessage;
        }

        public bool IsSuccess { get; }
        public string ErrorMessage { get; }

        public static WellRydePortalSessionProbeResult Ok() =>
            new WellRydePortalSessionProbeResult(true, null);

        public static WellRydePortalSessionProbeResult Fail(string errorMessage) =>
            new WellRydePortalSessionProbeResult(false, errorMessage ?? "WellRyde session check failed.");
    }
}
