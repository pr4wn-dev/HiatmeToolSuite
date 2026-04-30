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

        /// <summary>JSON body from the last successful <see cref="PostTripFilterDataAsync"/>.</summary>
        public string LastTripFilterDataJson { get; private set; }

        public CookieContainer CookieJar => _handler.CookieContainer;

        public HttpClient Client => _client;

        /// <summary>From the last successful <see cref="BootstrapMainPageAsync"/> HTML parse, if present.</summary>
        public string LastRequestVerificationToken { get; private set; }

        /// <summary>Cookie name/value for <see cref="PortalRootUri"/> after the last bootstrap attempt.</summary>
        public IReadOnlyDictionary<string, string> LastPortalCookies { get; private set; }

        public WellRydePortalSession()
        {
            _handler = new HttpClientHandler
            {
                // Never follow Location. We only record status, Location header, and cookies; callers redirect manually if needed.
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                CookieContainer = new CookieContainer(),
                UseCookies = true,
            };

            _client = new HttpClient(_handler)
            {
                Timeout = TimeSpan.FromSeconds(60),
            };

            _client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/147.0.0.0 Safari/537.36");
            _client.DefaultRequestHeaders.TryAddWithoutValidation("Accept",
                "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7");
            _client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
            _client.DefaultRequestHeaders.TryAddWithoutValidation("Cache-Control", "max-age=0");
            _client.DefaultRequestHeaders.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
            _client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-CH-UA",
                "\"Google Chrome\";v=\"147\", \"Not.A/Brand\";v=\"8\", \"Chromium\";v=\"147\"");
            _client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-CH-UA-Mobile", "?0");
            _client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-CH-UA-Platform", "\"Windows\"");
        }

        /// <summary>GET portal root; cookies accumulate in <see cref="CookieJar"/>. Does not follow redirects.</summary>
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
                    request.Headers.TryAddWithoutValidation("Referer", "https://www.google.com/");
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "cross-site");
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-User", "?1");
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
                    request.Headers.TryAddWithoutValidation("Priority", "u=0, i");

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

            var cookies = SnapCookiesForPortal();
            LastPortalCookies = cookies;
            LastRequestVerificationToken = ExtractRequestVerificationToken(html);

            var statusCode = response.StatusCode;
            var responseUri = response.RequestMessage?.RequestUri ?? PortalRootUri;
            response.Dispose();

            if ((int)statusCode < 200 || (int)statusCode >= 300)
                return WellRydePortalBootstrapResult.Fail(statusCode,
                    "HTTP " + (int)statusCode + " — unexpected status.", cookies, LastRequestVerificationToken);

            _lastBootstrapHtml = html;
            return WellRydePortalBootstrapResult.Ok(statusCode, responseUri, cookies, LastRequestVerificationToken);
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
                    request.Headers.TryAddWithoutValidation("Origin", PortalOrigin);
                    request.Headers.TryAddWithoutValidation("Referer", PortalRootUri.ToString());
                    request.Headers.TryAddWithoutValidation("Cache-Control", "max-age=0");
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-User", "?1");
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
                    request.Headers.TryAddWithoutValidation("Priority", "u=0, i");

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
            response.Dispose();

            LastPortalCookies = SnapMergedPortalCookies();
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

        /// <summary>
        /// GET <c>/portal/nu</c> (Angular shell) after a successful Spring login. Same-origin headers; cookies sent from the jar.
        /// Does not follow redirects.
        /// </summary>
        public async Task<WellRydePortalNuResult> GetPortalNuAsync(CancellationToken cancellationToken = default)
        {
            HttpResponseMessage response = null;
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, PortalNuUri))
                {
                    request.Headers.TryAddWithoutValidation("Referer", PortalRootUri.ToString());
                    request.Headers.TryAddWithoutValidation("Cache-Control", "max-age=0");
                    request.Headers.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-User", "?1");
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
                    request.Headers.TryAddWithoutValidation("Sec-CH-UA",
                        "\"Google Chrome\";v=\"147\", \"Not.A/Brand\";v=\"8\", \"Chromium\";v=\"147\"");
                    request.Headers.TryAddWithoutValidation("Sec-CH-UA-Mobile", "?0");
                    request.Headers.TryAddWithoutValidation("Sec-CH-UA-Platform", "\"Windows\"");
                    request.Headers.TryAddWithoutValidation("Priority", "u=0, i");

                    response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                return WellRydePortalNuResult.Fail(null, ex.Message ?? "GET /portal/nu failed.");
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
                return WellRydePortalNuResult.Fail(code, ex.Message ?? "Failed to read /portal/nu body.");
            }

            var statusCode = response.StatusCode;
            response.Dispose();

            LastPortalCookies = SnapMergedPortalCookies();
            if ((int)statusCode < 200 || (int)statusCode >= 300)
                return WellRydePortalNuResult.Fail(statusCode, "HTTP " + (int)statusCode + " from /portal/nu.");

            _lastPortalNuHtml = html;
            return WellRydePortalNuResult.Ok(statusCode);
        }

        /// <summary>
        /// POST <c>/portal/filterdata</c> for the trip list (XHR). Requires a prior successful <see cref="GetPortalNuAsync"/> so <c>_csrf</c> can be resolved.
        /// Uses the same 10-sequence <c>filterList</c> shape as the portal trip list; sequence 7 carries <c>specificDate</c> (e.g. April 30, 2026).
        /// For the portal &quot;today&quot; slice only, pass <paramref name="usePeriodDayFilter"/> true (uses <c>{"period":"0d"}</c> instead of a calendar date).
        /// <paramref name="maxResults"/> sets both <c>maxResult</c> and <c>defaultSize</c> (clamped to 1–10000).
        /// </summary>
        public async Task<WellRydePortalFilterDataResult> PostTripFilterDataAsync(DateTime tripDate,
            string listDefId = null, bool usePeriodDayFilter = false, int maxResults = DefaultTripFilterMaxResult,
            CancellationToken cancellationToken = default)
        {
            LastTripFilterDataJson = null;
            if (maxResults < 1)
                maxResults = 1;
            if (maxResults > TripFilterMaxResultUpperBound)
                maxResults = TripFilterMaxResultUpperBound;
            string maxResultsStr = maxResults.ToString(CultureInfo.InvariantCulture);
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
                new KeyValuePair<string, string>("page", "1"),
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
                    request.Headers.TryAddWithoutValidation("Accept", "application/json, text/javascript, */*; q=0.01");
                    request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
                    request.Headers.TryAddWithoutValidation("Origin", PortalOrigin);
                    request.Headers.TryAddWithoutValidation("Referer", PortalNuUri.ToString());
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "cors");
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "empty");
                    request.Headers.TryAddWithoutValidation("Priority", "u=1, i");

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
            response.Dispose();
            LastPortalCookies = SnapMergedPortalCookies();

            if ((int)statusCode < 200 || (int)statusCode >= 300)
                return WellRydePortalFilterDataResult.Fail(statusCode, "HTTP " + (int)statusCode + " from filterdata.", body);

            LastTripFilterDataJson = body;
            return WellRydePortalFilterDataResult.Ok(statusCode, body);
        }

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
            response.Dispose();
            LastPortalCookies = SnapMergedPortalCookies();

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
            response.Dispose();
            LastPortalCookies = SnapMergedPortalCookies();

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
}
