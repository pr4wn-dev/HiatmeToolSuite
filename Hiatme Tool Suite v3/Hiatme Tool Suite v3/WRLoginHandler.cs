using System;
using System.Collections.Generic;
using System.Web;
using System.Net.Http;
using System.Net;
using Hiatme_Tools;
using System.Threading.Tasks;
using System.Net.Http.Headers;
using System.Windows.Forms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.ComponentModel;
using System.Security.Policy;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Threading;

namespace Hiatme_Tool_Suite_v3
{
    public class WRLoginHandler : INotifyPropertyChanged
    {
        public static WRCompanyResponse WRCompanyResponse { get; set; }
        public static WebProxy Proxy { get; set; }
        public static CookieContainer CookieJar { get; set; }
        public static HttpClientHandler Handler { get; set; }
        public HttpClient Client { get; set; }
        /// <summary>Portal HTML + trip XHR + <c>filterdata</c>: <see cref="WellRydePortalCookieInjectingHandler"/> + <c>UseCookies=false</c> so cookies are not doubled with <see cref="WellRydeCookieHelper.IngestSetCookieHeaders"/>.</summary>
        private readonly HttpClient _filterDataClient;
        public static string UserAgent { get; set; }
        public static string AWSALB { get; set; }
        public static string ContentType { get; set; }
        public string _CsrfToken { get; set; }
        public string SESSION { get; set; }
        public bool IntentionalLogout { get; set; }
        private int _loginGate;
        /// <summary>Last successful <c>GET /portal/nu?date=…</c> URL (with optional hash) — used for servlet primes / timezone POST, not for XHR <c>Referer</c> (Chrome HAR uses bare <c>/portal/nu</c>).</summary>
        private string _lastTripsNuRefererWithDate;
        /// <summary>Tomcat id from <c>;jsessionid=</c> in <c>/portal/nu</c> HTML when <c>Set-Cookie</c> never stores <c>JSESSIONID</c> — used as URL rewrite on <c>POST /portal/filterdata</c>.</summary>
        private string _jsessionIdUrlRewriteForFilterData;

        /// <summary>
        /// Next <c>POST /portal/filterdata</c> on <see cref="_filterDataClient"/> must omit <c>JSESSIONID</c> from the <c>Cookie</c> line (Tomcat id in URL only).
        /// Custom request headers are unreliable on some .NET Framework + HttpClient stacks; <see cref="AsyncLocal{T}"/> follows the logical async call into <see cref="WellRydePortalCookieInjectingHandler"/>.
        /// </summary>
        private static readonly AsyncLocal<bool> FilterDataOmitJsessionFromCookie = new AsyncLocal<bool>();

        internal static bool FilterDataOmitJsessionFromCookieActive => FilterDataOmitJsessionFromCookie.Value;

        /// <summary>
        /// Chrome portal.app HAR: trip XHRs (<c>filterlist</c>, <c>listFilterDefsJson</c>, <c>filterdata</c>) send <c>Referer: …/portal/nu</c> without <c>?date=</c>.
        /// Optional <see cref="WellRydeConfig.TripsNuRefererHashFragment"/> is appended when set.
        /// </summary>
        public string TripsRefererForWellRydeAjax => BuildTripsSpaXhrReferer();

        private static string BuildTripsSpaXhrReferer()
        {
            var u = WellRydeConfig.TripsPageAbsoluteUrl;
            var hashFrag = WellRydeConfig.TripsNuRefererHashFragment?.Trim() ?? "";
            if (string.IsNullOrEmpty(hashFrag))
                return u;
            if (!hashFrag.StartsWith("#", StringComparison.Ordinal))
                hashFrag = "#" + hashFrag.TrimStart('/');
            return u + hashFrag;
        }

        /// <summary>True while GetCompanyInfo/Login is running — avoids stacked auto-relogin from PropertyChanged.</summary>
        public bool IsLoginInProgress => Volatile.Read(ref _loginGate) != 0;

        /// <summary>
        /// Real Chrome-on-Windows UA for every portal request (login, <c>nu</c>, <c>filterdata</c>, curl priming).
        /// A shortened non-Chrome UA string previously matched logs but can trigger HTML SPA shells instead of JSON on <c>filterdata</c>.
        /// </summary>
        private static readonly string PortalBrowserUserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/147.0.0.0 Safari/537.36";

        /// <summary>Client hints matching Chrome 147 &quot;Copy as cURL&quot; from portal.app <c>filterdata</c>.</summary>
        private const string PortalFilterDataSecChUa =
            "\"Google Chrome\";v=\"147\", \"Not.A/Brand\";v=\"8\", \"Chromium\";v=\"147\"";

        public WRLoginHandler()
        {
            WRCompanyResponse = new WRCompanyResponse();
            Proxy = new WebProxy();
            CookieJar = new CookieContainer();
            Handler = new HttpClientHandler();
            Handler.CookieContainer = CookieJar;
            Handler.AutomaticDecompression = DecompressionMethods.GZip;
            Client = new HttpClient(Handler);
            Client.Timeout = TimeSpan.FromSeconds(60);
            var filterInner = new HttpClientHandler
            {
                UseCookies = false,
                AutomaticDecompression = DecompressionMethods.GZip,
            };
            _filterDataClient = new HttpClient(new WellRydePortalCookieInjectingHandler(CookieJar, filterInner), disposeHandler: true);
            _filterDataClient.Timeout = TimeSpan.FromSeconds(60);
            UserAgent = "";
            AWSALB = "";
            ContentType = "";
            Connected = false;
            _CsrfToken = "";
            SESSION = "";
            IntentionalLogout = false;
        }
        public async Task<bool> GetCompanyInfo(string code, string user, string pass)
        {
            if (Interlocked.CompareExchange(ref _loginGate, 1, 0) != 0)
                return false;

            string companycode = code;
            string username = user;
            string password = pass;

            try
            {
                Client.DefaultRequestHeaders.Clear();
                Client.DefaultRequestHeaders.Add("User-Agent", PortalBrowserUserAgent);
                Client.DefaultRequestHeaders.Add("Accept", "application/json, text/plain, */*");
                var regUrl = "https://appreg.app.wellryde.com/appregister/companyinfo/" + Uri.EscapeDataString(companycode);
                HttpResponseMessage resmsg = await Client.GetAsync(regUrl);
                var response = await resmsg.Content.ReadAsStringAsync();

                if (string.IsNullOrWhiteSpace(response) || response.TrimStart().StartsWith("<", StringComparison.Ordinal))
                {
                    MessageBox.Show("WellRyde company lookup returned an unexpected response. Check network/VPN or try again.");
                    return false;
                }

                //deserialize the object into company info object
                WRCompanyResponse = JsonConvert.DeserializeObject<WRCompanyResponse>(response);

                if (WRCompanyResponse.companyinfo == null)
                {
                    //Console.WriteLine("There was a problem returning info of company code.");
                    MessageBox.Show("Invalid company code!");
                    //Connected = false;
                    return false;
                }
                else
                {
                    Console.WriteLine(WRCompanyResponse.companyinfo.CompanyName);
                    Console.WriteLine(WRCompanyResponse.companyinfo.ContactName);
                    Console.WriteLine(WRCompanyResponse.companyinfo.Email);
                    return await Login(companycode, username, password);
                }

            }
            catch (JsonException)
            {
                MessageBox.Show("Incorrect login information!");
                //Console.WriteLine($"{e.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _loginGate, 0);
            }
            //Proceed to login after info grab completes.
            return false;
        }
        public async Task<bool> Login(string code, string user, string pass)
        {
            IntentionalLogout = false;
            string companycode = code;
            string username = user;
            string password = pass;

            try
            {
                WellRydeConfig.ResolvedTripListDefId = null;
                _jsessionIdUrlRewriteForFilterData = null;
                Client.DefaultRequestHeaders.Clear();
                Client.DefaultRequestHeaders.Add("User-Agent", PortalBrowserUserAgent);
                Client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");

                // Match PHP WellRydeScraper::login — GET / then GET /portal/login so Spring issues SESSION; GET /portal/nu first left only AWSALB cookies (no session).
                try
                {
                    using (var rootGet = await GetPortalOriginDocumentAsync())
                    {
                        WellRydeCookieHelper.IngestSetCookieHeaders(rootGet, CookieJar);
                    }
                }
                catch
                {
                    /* non-fatal */
                }

                string loginPageHtml;
                using (var loginGet = await GetPortalHtmlDocumentAsync(WellRydeConfig.LoginPageAbsoluteUrl, WellRydeConfig.PortalOrigin + "/"))
                {
                    loginGet.EnsureSuccessStatusCode();
                    WellRydeCookieHelper.IngestSetCookieHeaders(loginGet, CookieJar);
                    loginPageHtml = await loginGet.Content.ReadAsStringAsync();
                }
                WellRydeCookieHelper.TryIngestJSessionIdFromMarkupAndLocation(CookieJar, loginPageHtml);

                if (!TryExtractHiddenInputValue(loginPageHtml, "_logincsrf", out string logincsrf)
                    || string.IsNullOrEmpty(logincsrf)
                    || !TryExtractHiddenInputValue(loginPageHtml, "_csrf", out string loginCsrf)
                    || string.IsNullOrEmpty(loginCsrf))
                {
                    MessageBox.Show("Could not load WellRyde login page (missing security tokens). Try again or contact support.");
                    Connected = false;
                    return false;
                }

                string geoVal = "false";
                var geo = WRCompanyResponse?.companyinfo?.Geolocation;
                if (!string.IsNullOrEmpty(geo) && !string.Equals(geo, "undefined", StringComparison.OrdinalIgnoreCase))
                    geoVal = geo;

                Client.DefaultRequestHeaders.Remove("Accept");
                Client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
                Client.DefaultRequestHeaders.Add("Origin", WellRydeConfig.PortalOrigin);
                Client.DefaultRequestHeaders.Add("Referer", WellRydeConfig.LoginPageAbsoluteUrl);

                await PostWellrydeTimezoneAsync(loginCsrf);

                TryExtractHiddenInputValue(loginPageHtml, "serviceNowURL", out string serviceNowUrl);
                TryExtractHiddenInputValue(loginPageHtml, "serviceNowChatURL", out string serviceNowChatUrl);

                var formPairs = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("deviceFingerPrint", "hiatme-desktop"),
                    new KeyValuePair<string, string>("_logincsrf", logincsrf),
                    new KeyValuePair<string, string>("geoLocationVal", geoVal),
                    new KeyValuePair<string, string>("userCompany", companycode),
                    new KeyValuePair<string, string>("j_username", username),
                    new KeyValuePair<string, string>("j_password", password),
                    new KeyValuePair<string, string>("userLat", ""),
                    new KeyValuePair<string, string>("userLong", ""),
                    new KeyValuePair<string, string>("_csrf", loginCsrf),
                };
                if (!string.IsNullOrEmpty(serviceNowUrl))
                    formPairs.Add(new KeyValuePair<string, string>("serviceNowURL", serviceNowUrl));
                if (!string.IsNullOrEmpty(serviceNowChatUrl))
                    formPairs.Add(new KeyValuePair<string, string>("serviceNowChatURL", serviceNowChatUrl));

                var formContent = new FormUrlEncodedContent(formPairs);

                var loginPostUri = new Uri(WellRydeConfig.SpringSecurityCheckUrl);
                // .NET Framework: AllowAutoRedirect cannot be changed after the shared Handler has sent
                // any request. Use a one-off client with AllowAutoRedirect=false and the same CookieJar.
                // Read the response before disposing that client.
                string response;
                try
                {
                    using (var loginPostInner = new HttpClientHandler
                    {
                        UseCookies = false,
                        AutomaticDecompression = DecompressionMethods.GZip,
                        AllowAutoRedirect = false,
                    })
                    using (var loginPostClient = new HttpClient(new WellRydePortalCookieInjectingHandler(CookieJar, loginPostInner), disposeHandler: true))
                    {
                        loginPostClient.Timeout = Client.Timeout;
                        loginPostClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", PortalBrowserUserAgent);
                        loginPostClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                        loginPostClient.DefaultRequestHeaders.TryAddWithoutValidation("Origin", WellRydeConfig.PortalOrigin);
                        loginPostClient.DefaultRequestHeaders.TryAddWithoutValidation("Referer", WellRydeConfig.LoginPageAbsoluteUrl);

                        using (var res = await loginPostClient.PostAsync(loginPostUri, formContent))
                        {
                            WellRydeCookieHelper.IngestSetCookieHeaders(res, CookieJar);
                            var statusCode = (int)res.StatusCode;
                            if (statusCode >= 300 && statusCode < 400 && res.Headers.Location != null)
                            {
                                var loc = ResolveRedirectLocation(res.RequestMessage.RequestUri, res.Headers.Location);
                                if (loc == null)
                                {
                                    Connected = false;
                                    return false;
                                }

                                var locStr = loc.ToString();
                                if (RedirectIndicatesLoginFailure(locStr))
                                {
                                    MessageBox.Show("Incorrect Login Information!");
                                    IntentionalLogout = true;
                                    Connected = false;
                                    return false;
                                }

                                WellRydeCookieHelper.TryIngestJSessionIdFromMarkupAndLocation(CookieJar, locStr);

                                // Document navigation like Chrome (Sec-Fetch-Mode: navigate). Client.GetAsync omits those;
                                // some portals only emit JSESSIONID on full navigation responses.
                                using (var landing = await GetPortalHtmlDocumentAsync(locStr, WellRydeConfig.LoginPageAbsoluteUrl))
                                {
                                    WellRydeCookieHelper.IngestSetCookieHeaders(landing, CookieJar);
                                    landing.EnsureSuccessStatusCode();
                                    response = await landing.Content.ReadAsStringAsync();
                                }
                                WellRydeCookieHelper.TryIngestJSessionIdFromMarkupAndLocation(CookieJar, response);
                            }
                            else
                            {
                                response = await res.Content.ReadAsStringAsync();
                                res.EnsureSuccessStatusCode();

                                if (RedirectIndicatesLoginFailure(response))
                                {
                                    MessageBox.Show("Incorrect Login Information!");
                                    IntentionalLogout = true;
                                    Connected = false;
                                    return false;
                                }
                                WellRydeCookieHelper.TryIngestJSessionIdFromMarkupAndLocation(CookieJar, response);
                            }
                        }
                    }

                    Uri uri = new Uri(WellRydeConfig.SpringSecurityCheckUrl);
                    IEnumerable<Cookie> responseCookies = CookieJar.GetCookies(uri).Cast<Cookie>();
                    foreach (Cookie cookie in responseCookies)
                    {
                        if (cookie.Name == "SESSION")
                            SESSION = cookie.Value;
                    }
                }
                catch
                {
                    Connected = false;
                    return false;
                }
                _CsrfToken = GrabCSRFToken(response) ?? TryGetCsrfFromCookies();
                if (string.IsNullOrEmpty(_CsrfToken))
                {
                    try
                    {
                        using (var shell = await GetPortalHtmlDocumentAsync(WellRydeConfig.PortalShellUrl, WellRydeConfig.PortalOrigin + "/"))
                        {
                            WellRydeCookieHelper.IngestSetCookieHeaders(shell, CookieJar);
                            shell.EnsureSuccessStatusCode();
                            var shellHtml = await shell.Content.ReadAsStringAsync();
                            WellRydeCookieHelper.TryIngestJSessionIdFromMarkupAndLocation(CookieJar, shellHtml);
                            _CsrfToken = GrabCSRFToken(shellHtml) ?? TryGetCsrfFromCookies();
                        }
                    }
                    catch
                    {
                        // ignore; UpdateHandlerHeaders still runs with empty token
                    }
                }

                await EstablishPortalServletSessionAfterLoginAsync();
                if (WellRydeConfig.ServletCookieAutoHandlerEnabled && !PortalCookieHeaderHasJsessionId())
                    await TryServletCookiesUsingAutoHandlerDocumentGetsAsync(DateTime.Today).ConfigureAwait(false);
                PreferPortalXsrfCookieForCsrfToken();
                SyncXsrfTokenCookieFromCurrentCsrf();
            }
            catch
            {
                //Invalid email and/or password
                WellRydeLog.WriteLine("There was a problem logging in.");
                Connected = false;
                return false;
            }

            UpdateHandlerHeaders();
            try
            {
                var ch = WellRydeCookieHelper.GetCookieHeader(CookieJar, new Uri(WellRydeConfig.FilterDataUrl));
                if (string.IsNullOrEmpty(ch)
                    || (ch.IndexOf("SESSION=", StringComparison.OrdinalIgnoreCase) < 0
                        && ch.IndexOf("JSESSIONID=", StringComparison.OrdinalIgnoreCase) < 0))
                {
                    WellRydeLog.WriteLine("WellRyde: warning — Cookie header for filterdata has no SESSION/JSESSIONID (only LB cookies?). Trips API will return HTML shells until login establishes a portal session.");
                }
                else if (ch.IndexOf("SESSION=", StringComparison.OrdinalIgnoreCase) >= 0
                    && ch.IndexOf("JSESSIONID=", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    WellRydeLog.WriteLine("WellRyde: warning — SESSION without JSESSIONID; filterdata often returns HTML instead of JSON. Chrome sends both after login.");
                }
            }
            catch
            {
                /* ignore */
            }
            Connected = true;
            IntentionalLogout = false;
            return true;
        }
        public async Task<bool> Logout()
        {
            
            try
            {
                HttpResponseMessage resmsg = Client.GetAsync(WellRydeConfig.SpringSecurityLogoutUrl).Result;
                var response = await resmsg.Content.ReadAsStringAsync();
            }
            catch (NullReferenceException e)
            {
                Connected = false;
                return false;
            }
            IntentionalLogout = true;
            Connected = false;
            return true;
        }

        /// <summary>PHP-parity first hop: <c>GET /</c> with no Referer so ELB/session cookies match browser cold navigation.</summary>
        private async Task<HttpResponseMessage> GetPortalOriginDocumentAsync()
        {
            var req = new HttpRequestMessage(HttpMethod.Get, WellRydeConfig.PortalOrigin + "/");
            req.Headers.TryAddWithoutValidation("User-Agent", PortalBrowserUserAgent);
            req.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
            req.Headers.TryAddWithoutValidation("Cache-Control", "max-age=0");
            req.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "none");
            req.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
            req.Headers.TryAddWithoutValidation("Sec-Fetch-User", "?1");
            req.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
            req.Headers.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
            return await _filterDataClient.SendAsync(req);
        }

        /// <summary>
        /// GET portal HTML using browser-like navigation headers and shared cookies.
        /// Uses <see cref="_filterDataClient"/> (no <see cref="Client"/> default headers). After <see cref="UpdateHandlerHeaders"/>,
        /// <see cref="Client"/> carries JSON+XHR+cors defaults — merging those with a document GET yields duplicate/conflicting
        /// <c>Accept</c> and <c>Sec-Fetch-*</c> and Spring/CDN may respond with HTTP 401/403 to <c>/portal/nu?date=</c>.
        /// </summary>
        /// <param name="referer">
        /// Optional <c>Referer</c> for this GET (e.g. portal root when loading <c>/portal/login</c>). Defaults to trips page.
        /// </param>
        private async Task<HttpResponseMessage> GetPortalHtmlDocumentAsync(string requestUri, string referer = null)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, requestUri);
            req.Headers.TryAddWithoutValidation("User-Agent", PortalBrowserUserAgent);
            req.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
            req.Headers.TryAddWithoutValidation("Cache-Control", "max-age=0");
            req.Headers.TryAddWithoutValidation("Referer", string.IsNullOrEmpty(referer) ? WellRydeConfig.TripsPageAbsoluteUrl : referer);
            req.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
            req.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
            req.Headers.TryAddWithoutValidation("Sec-Fetch-User", "?1");
            req.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
            req.Headers.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
            return await _filterDataClient.SendAsync(req);
        }

        /// <summary>
        /// Chrome <c>GET /portal/listFilterDefsJson?listDefId=…&amp;customListDefId=&amp;userDefaultFilter=true</c> runs immediately before <c>POST /portal/filterdata</c>.
        /// Primes list/session state so <c>filterdata</c> returns JSON instead of the NU HTML shell.
        /// </summary>
        private async Task TryPrimeListFilterDefsJsonBeforeFilterDataAsync(string refererForNu, string csrfForAjax)
        {
            var listId = WellRydeConfig.TripFilterListDefId ?? "";
            if (string.IsNullOrWhiteSpace(listId) || !listId.StartsWith("SEC-", StringComparison.Ordinal))
                return;
            var url = WellRydeConfig.BuildListFilterDefsJsonUrl(listId);
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    req.Headers.TryAddWithoutValidation("Accept", "application/json, text/javascript, */*; q=0.01");
                    req.Headers.TryAddWithoutValidation("User-Agent", PortalBrowserUserAgent);
                    req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
                    req.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate");
                    req.Headers.TryAddWithoutValidation("Priority", "u=0, i");
                    req.Headers.TryAddWithoutValidation("Referer", string.IsNullOrEmpty(refererForNu) ? BuildTripsSpaXhrReferer() : refererForNu);
                    req.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
                    if (WellRydeConfig.FilterDataPhpStyleHeaders && !string.IsNullOrEmpty(csrfForAjax))
                    {
                        req.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", csrfForAjax);
                        req.Headers.TryAddWithoutValidation("X-XSRF-TOKEN", csrfForAjax);
                    }
                    req.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
                    req.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "cors");
                    req.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "empty");
                    req.Headers.TryAddWithoutValidation("sec-ch-ua", PortalFilterDataSecChUa);
                    req.Headers.TryAddWithoutValidation("sec-ch-ua-mobile", "?0");
                    req.Headers.TryAddWithoutValidation("sec-ch-ua-platform", "\"Windows\"");
                    using (var resp = await _filterDataClient.SendAsync(req).ConfigureAwait(false))
                    {
                        WellRydeCookieHelper.IngestSetCookieHeaders(resp, CookieJar);
                        WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
                        if (WellRydeConfig.DebugPortalTraffic)
                            WellRydeLog.WriteLine("WellRyde: listFilterDefsJson prime HTTP " + (int)resp.StatusCode + " listDefId=" + listId);
                    }
                }
            }
            catch (Exception ex)
            {
                if (WellRydeConfig.DebugPortalTraffic)
                    WellRydeLog.WriteLine("WellRyde: listFilterDefsJson prime error: " + ex.Message);
            }
        }

        /// <summary>
        /// POST <c>/portal/filterdata</c>.
        /// Chrome XHR sends <c>Sec-Fetch-*</c> on same-origin POST; include them here (filter runs on <see cref="_filterDataClient"/> without API defaults).
        /// CSRF: form <c>_csrf</c> is always sent. Optional <c>X-CSRF-TOKEN</c> / <c>X-XSRF-TOKEN</c> only when <see cref="WellRydeConfig.FilterDataPhpStyleHeaders"/> (Chrome HAR omits them on <c>filterdata</c>).
        /// <paramref name="referer"/>: when null, uses <see cref="TripsRefererForWellRydeAjax"/> (bare <c>/portal/nu</c> per Chrome HAR).
        /// Uses <see cref="_filterDataClient"/> (<see cref="WellRydePortalCookieInjectingHandler"/> + jar) so <c>Cookie</c> matches Chrome order for <c>filterdata</c> without doubling ALB lines.
        /// </summary>
        public async Task<HttpResponseMessage> PostWellRydeFilterDataAsync(HttpContent formContent, string referer = null)
        {
            WellRydeCookieHelper.CollapseDuplicatePortalCookies(CookieJar);
            WellRydeCookieHelper.TryRemoveSyntheticSpringJsessionIdFromJar(CookieJar);
            WellRydeCookieHelper.TryApplyManualJsessionIdFromConfig(CookieJar);
            WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
            var refererVal = !string.IsNullOrEmpty(referer)
                ? referer
                : TripsRefererForWellRydeAjax;
            if (!PortalCookieHeaderHasJsessionId())
                await TryPrimeJsessionIdViaPortalStaticResourceAsync(refererVal).ConfigureAwait(false);
            if (!PortalCookieHeaderHasJsessionId())
                await TryAcquireTomcatJsessionViaNativeHandlerAsync(refererVal).ConfigureAwait(false);
            WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
            var postUri = WellRydeConfig.FilterDataUrl;
            if (!PortalCookieHeaderHasJsessionId()
                && !string.IsNullOrEmpty(_jsessionIdUrlRewriteForFilterData)
                && !WellRydeCookieHelper.IsJsessionTokenSpringUuidHex(CookieJar, _jsessionIdUrlRewriteForFilterData))
            {
                postUri = WellRydeCookieHelper.AppendTomcatJsessionUrlRewrite(WellRydeConfig.FilterDataUrl, _jsessionIdUrlRewriteForFilterData);
                if (WellRydeConfig.DebugPortalTraffic)
                    WellRydeLog.WriteLine("WellRyde: filterdata POST with URL ;jsessionid= rewrite (no JSESSIONID cookie in jar).");
            }
            var xsrfFromCookie = TryGetCsrfFromCookies();
            var csrfForAjax = !string.IsNullOrEmpty(xsrfFromCookie) ? xsrfFromCookie : _CsrfToken;

            var bodyBytes = await formContent.ReadAsByteArrayAsync().ConfigureAwait(false);
            var contentTypeForBody = formContent.Headers.ContentType != null
                ? formContent.Headers.ContentType.ToString()
                : "application/x-www-form-urlencoded; charset=utf-8";

            await TryPrimeListFilterDefsJsonBeforeFilterDataAsync(refererVal, csrfForAjax).ConfigureAwait(false);

            var curlExe = WellRydeCurlFilterData.TryResolveCurlPath();
            if (WellRydeConfig.FilterDataUseCurl && !string.IsNullOrEmpty(curlExe))
            {
                var curlRes = WellRydeCurlFilterData.TryPostFilterData(
                    CookieJar,
                    postUri,
                    bodyBytes,
                    contentTypeForBody,
                    refererVal ?? string.Empty,
                    csrfForAjax ?? string.Empty,
                    PortalBrowserUserAgent);
                if (curlRes != null && WellRydeCurlFilterData.BodyLooksLikeJson(curlRes.Body))
                {
                    var httpCode = curlRes.StatusCode;
                    if (httpCode == 0)
                        httpCode = 200;
                    if (httpCode == 200)
                    {
                        if (WellRydeConfig.DebugPortalTraffic)
                            WellRydeLog.WriteLine("WellRyde: filterdata via curl — JSON " + (curlRes.Body?.Length ?? 0) + " bytes");
                        return BuildSyntheticFilterDataResponseFromCurl(curlRes);
                    }
                }
                else if (curlRes != null && WellRydeConfig.DebugPortalTraffic)
                {
                    var prefix = curlRes.Body != null && curlRes.Body.Length > 0
                        ? Encoding.UTF8.GetString(curlRes.Body, 0, Math.Min(120, curlRes.Body.Length))
                        : "";
                    WellRydeLog.WriteLine("WellRyde: filterdata curl non-JSON (HTTP " + curlRes.StatusCode + ", " + (curlRes.ResponseContentType ?? "") + ") — HttpClient retry. prefix=" + prefix);
                }
            }

            WellRydeCookieHelper.CollapseDuplicatePortalCookies(CookieJar);

            async Task<HttpResponseMessage> SendFilterDataOnceAsync(string uri, bool omitJsessionIdFromCookie = false)
            {
                Uri requestUri;
                if (!string.IsNullOrEmpty(uri) && uri.IndexOf(";jsessionid=", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // Default Uri parsing can drop Tomcat matrix params so the wire POST hits /portal/filterdata without ;jsessionid=.
#pragma warning disable CS0618 // Uri(string, bool dontEscape) — required on .NET Framework so ;jsessionid= reaches the server.
                    requestUri = new Uri(uri, true);
#pragma warning restore CS0618
                }
                else
                    requestUri = new Uri(uri, UriKind.Absolute);
                var req = new HttpRequestMessage(HttpMethod.Post, requestUri);
                try
                {
                    var p = req.Properties;
                    if (p != null)
                    {
                        p[WellRydeFilterDataRequestKeys.WireFilterDataUrl] = uri ?? string.Empty;
                        if (omitJsessionIdFromCookie)
                            p[WellRydeFilterDataRequestKeys.OmitJsessionFromCookie] = true;
                    }
                }
                catch
                {
                    /* ignore */
                }
                var prevOmitJ = FilterDataOmitJsessionFromCookie.Value;
                if (omitJsessionIdFromCookie)
                {
                    FilterDataOmitJsessionFromCookie.Value = true;
                    req.Headers.TryAddWithoutValidation(WellRydePortalCookieInjectingHandler.FilterDataOmitJsessionIdCookieHeader, "1");
                }
                req.Content = new ByteArrayContent(bodyBytes);
                req.Content.Headers.TryAddWithoutValidation("Content-Type", contentTypeForBody);
                req.Headers.Remove("User-Agent");
                req.Headers.TryAddWithoutValidation("User-Agent", PortalBrowserUserAgent);
                req.Headers.TryAddWithoutValidation("Accept", "application/json, text/javascript, */*; q=0.01");
                req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
                req.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate");
                req.Headers.TryAddWithoutValidation("Origin", WellRydeConfig.PortalOrigin);
                req.Headers.TryAddWithoutValidation("Priority", "u=1, i");
                req.Headers.TryAddWithoutValidation("Referer", refererVal);
                req.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
                if (WellRydeConfig.FilterDataPhpStyleHeaders && !string.IsNullOrEmpty(csrfForAjax))
                {
                    req.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", csrfForAjax);
                    req.Headers.TryAddWithoutValidation("X-XSRF-TOKEN", csrfForAjax);
                }
                req.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
                req.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "cors");
                req.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "empty");
                req.Headers.TryAddWithoutValidation("sec-ch-ua", PortalFilterDataSecChUa);
                req.Headers.TryAddWithoutValidation("sec-ch-ua-mobile", "?0");
                req.Headers.TryAddWithoutValidation("sec-ch-ua-platform", "\"Windows\"");
                HttpResponseMessage r;
                try
                {
                    r = await _filterDataClient.SendAsync(req).ConfigureAwait(false);
                }
                finally
                {
                    FilterDataOmitJsessionFromCookie.Value = prevOmitJ;
                }
                WellRydeCookieHelper.IngestSetCookieHeaders(r, CookieJar);
                return r;
            }

            var omitCookieFirst = postUri.IndexOf(";jsessionid=", StringComparison.OrdinalIgnoreCase) >= 0;
            var resp = await SendFilterDataOnceAsync(postUri, omitCookieFirst).ConfigureAwait(false);
            if ((int)resp.StatusCode == 500
                && postUri.IndexOf(";jsessionid=", StringComparison.OrdinalIgnoreCase) >= 0
                && PortalCookieHeaderHasJsessionId())
            {
                var plain = WellRydeConfig.FilterDataUrl;
                if (!string.Equals(postUri, plain, StringComparison.OrdinalIgnoreCase))
                {
                    if (WellRydeConfig.DebugPortalTraffic)
                        WellRydeLog.WriteLine("WellRyde: filterdata HTTP 500 with ;jsessionid= in URL while JSESSIONID cookie present — retrying once without URL rewrite (duplicate bind confuses Tomcat).");
                    try
                    {
                        await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                        /* ignore */
                    }
                    resp.Dispose();
                    resp = await SendFilterDataOnceAsync(plain).ConfigureAwait(false);
                }
            }

            if ((int)resp.StatusCode == 200
                && postUri.IndexOf(";jsessionid=", StringComparison.OrdinalIgnoreCase) < 0
                && WellRydeCookieHelper.TryGetPortalJSessionIdCookieValue(CookieJar, out var jsForUrlRewrite))
            {
                var media = resp.Content.Headers.ContentType?.MediaType ?? "";
                var looksHtmlCt = media.IndexOf("text/html", StringComparison.OrdinalIgnoreCase) >= 0;
                var cl = resp.Content.Headers.ContentLength;
                if (looksHtmlCt || (cl.HasValue && cl.Value >= 15000))
                {
                    var bodyBytesFirst = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    if (FilterDataBodyLooksLikeNuHtmlShell(bodyBytesFirst))
                    {
                        resp.Dispose();
                        var retryUri = WellRydeCookieHelper.AppendTomcatJsessionUrlRewrite(WellRydeConfig.FilterDataUrl, jsForUrlRewrite);
                        WellRydeLog.WriteLine("WellRyde: filterdata NU HTML shell — curl recovery (A: omit JSESSIONID + ;jsessionid= URL, B: full cookie + URL, C: full cookie + plain URL).");
                        if (WellRydeConfig.DebugPortalTraffic)
                            WellRydeLog.WriteLine("WellRyde: filterdata shell retry URI suffix=" + (retryUri.Length > 48 ? retryUri.Substring(retryUri.Length - 48) : retryUri));
                        // HttpClient + System.Uri canonicalize away ;jsessionid= on the wire — use curl for this POST so the Tomcat path survives (same as PHP).
                        var curlPathShell = WellRydeCurlFilterData.TryResolveCurlPath();
                        if (string.IsNullOrEmpty(curlPathShell))
                        {
                            WellRydeLog.WriteLine("WellRyde: filterdata shell retry — curl.exe not found (checked Sysnative/System32/SysWOW64, Git, PATH). Install Windows 10+ curl or Git for Windows; HttpClient cannot send ;jsessionid= in the path.");
                        }
                        else
                        {
                            WellRydeLog.WriteLine("WellRyde: filterdata shell retry — curl " + curlPathShell);
                            WellRydeCurlFilterData.CurlPostResult tryShellCurl(bool omitJsessionFromCookieFile, string postUrl, string attemptTag)
                            {
                                var r = WellRydeCurlFilterData.TryPostFilterData(
                                    CookieJar,
                                    postUrl,
                                    bodyBytes,
                                    contentTypeForBody,
                                    refererVal ?? string.Empty,
                                    csrfForAjax ?? string.Empty,
                                    PortalBrowserUserAgent,
                                    omitJsessionIdFromCookieFile: omitJsessionFromCookieFile);
                                if (r != null && WellRydeCurlFilterData.BodyLooksLikeJson(r.Body))
                                {
                                    WellRydeLog.WriteLine("WellRyde: filterdata recovered after NU HTML shell — curl " + attemptTag + " (" + (r.Body?.Length ?? 0) + " bytes JSON).");
                                    return r;
                                }
                                if (r == null)
                                    WellRydeLog.WriteLine("WellRyde: filterdata shell curl " + attemptTag + " — failed (see WellRyde: curl … lines above).");
                                else
                                {
                                    var ct = r.ResponseContentType ?? "";
                                    var prefix = r.Body != null && r.Body.Length > 0
                                        ? Encoding.UTF8.GetString(r.Body, 0, Math.Min(120, r.Body.Length)).Replace('\r', ' ').Replace('\n', ' ')
                                        : "";
                                    WellRydeLog.WriteLine("WellRyde: filterdata shell curl " + attemptTag + " — HTTP " + r.StatusCode + ", content-type=" + ct + ", len=" + (r.Body?.Length ?? 0) + ", prefix=" + prefix);
                                }
                                return null;
                            }
                            var curlOk = tryShellCurl(true, retryUri, "A (omit JSESSIONID + ;jsessionid=)")
                                ?? tryShellCurl(false, retryUri, "B (full cookie + ;jsessionid=)")
                                ?? tryShellCurl(false, WellRydeConfig.FilterDataUrl, "C (full cookie + plain /portal/filterdata)");
                            if (curlOk != null)
                                return BuildSyntheticFilterDataResponseFromCurl(curlOk);
                            WellRydeLog.WriteLine("WellRyde: filterdata shell retry — all curl attempts non-JSON; falling back to HttpClient.");
                        }
                        resp = await SendFilterDataOnceAsync(retryUri, omitJsessionIdFromCookie: true).ConfigureAwait(false);
                        if ((int)resp.StatusCode == 500
                            && retryUri.IndexOf(";jsessionid=", StringComparison.OrdinalIgnoreCase) >= 0
                            && PortalCookieHeaderHasJsessionId())
                        {
                            var plain = WellRydeConfig.FilterDataUrl;
                            if (WellRydeConfig.DebugPortalTraffic)
                                WellRydeLog.WriteLine("WellRyde: filterdata HTTP 500 after URL-only jsessionid retry — retrying once on plain URL.");
                            try
                            {
                                await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                            }
                            catch
                            {
                                /* ignore */
                            }
                            resp.Dispose();
                            resp = await SendFilterDataOnceAsync(plain).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        var ctSaved = resp.Content.Headers.ContentType?.ToString();
                        resp.Content = new ByteArrayContent(bodyBytesFirst);
                        if (!string.IsNullOrEmpty(ctSaved))
                            resp.Content.Headers.TryAddWithoutValidation("Content-Type", ctSaved);
                    }
                }
            }

            return resp;
        }

        /// <summary>SPA shell from <c>/portal/nu</c> stack (~47k) instead of JSON for <c>filterdata</c>.</summary>
        static bool FilterDataBodyLooksLikeNuHtmlShell(byte[] data)
        {
            if (data == null || data.Length < 15000)
                return false;
            // UTF-8 BOM (EF BB BF) → U+FEFF; without stripping, LooksLikeNonJsonPayload misses "<?xml" and we skip the ;jsessionid= retry.
            var n = Math.Min(2048, data.Length);
            var prefix = Encoding.UTF8.GetString(data, 0, n).TrimStart('\uFEFF', '\u200B').TrimStart();
            if (!WellRydeTripParsing.LooksLikeNonJsonPayload(prefix))
                return false;
            return prefix.IndexOf("about:legacy-compat", StringComparison.OrdinalIgnoreCase) >= 0
                || prefix.IndexOf("_csrf_parameter", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static HttpResponseMessage BuildSyntheticFilterDataResponseFromCurl(WellRydeCurlFilterData.CurlPostResult r)
        {
            var code = r.StatusCode >= 100 && r.StatusCode < 600
                ? (HttpStatusCode)r.StatusCode
                : HttpStatusCode.OK;
            var msg = new HttpResponseMessage(code);
            var content = new ByteArrayContent(r.Body ?? Array.Empty<byte>());
            if (!string.IsNullOrEmpty(r.ResponseContentType))
                content.Headers.TryAddWithoutValidation("Content-Type", r.ResponseContentType);
            else
                content.Headers.TryAddWithoutValidation("Content-Type", "application/json; charset=UTF-8");
            msg.Content = content;
            return msg;
        }

        /// <summary>When <c>POST /portal/filterdata</c> returns the SPA HTML shell, scrape Tomcat <c>jsessionid</c> and URL-rewrite token for an immediate retry.</summary>
        public void TryIngestJsessionFromFilterDataHtmlShell(string html)
        {
            if (string.IsNullOrEmpty(html))
                return;
            WellRydeCookieHelper.TryIngestJSessionIdFromMarkupAndLocation(CookieJar, html);
            WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
            var rw = WellRydeCookieHelper.TryGetFirstJsessionIdFromTextForUrlRewrite(CookieJar, html);
            if (!string.IsNullOrEmpty(rw) && !WellRydeCookieHelper.IsJsessionTokenSpringUuidHex(CookieJar, rw))
                _jsessionIdUrlRewriteForFilterData = rw;
        }

        /// <summary>
        /// Match PHP <c>WellRydeScraper::getTripsViaApi</c>: GET <c>/portal/nu?date=YYYY-MM-DD</c> for SPA date context and a fresh <c>_csrf</c> before <c>filterdata</c>.
        /// </summary>
        public async Task<bool> TryRefreshTripsNuCsrfAsync(DateTime tripDate)
        {
            try
            {
                WellRydeCookieHelper.TryRemoveSyntheticSpringJsessionIdFromJar(CookieJar);
                if (WellRydeConfig.ServletCookieAutoHandlerEnabled)
                    await TryServletCookiesUsingAutoHandlerDocumentGetsAsync(tripDate).ConfigureAwait(false);
                // Bare /portal/nu navigation (Chrome often has this in history before ?date=); Tomcat may Set-Cookie JSESSIONID here only.
                using (var bareNu = await GetPortalHtmlDocumentAsync(WellRydeConfig.TripsPageAbsoluteUrl, WellRydeConfig.PortalShellUrl))
                {
                    WellRydeCookieHelper.IngestSetCookieHeaders(bareNu, CookieJar);
                    if (bareNu.IsSuccessStatusCode)
                    {
                        var bareHtml = await bareNu.Content.ReadAsStringAsync();
                        WellRydeCookieHelper.TryIngestJSessionIdFromMarkupAndLocation(CookieJar, bareHtml);
                        var rwBare = WellRydeCookieHelper.TryGetFirstJsessionIdFromTextForUrlRewrite(CookieJar, bareHtml);
                        if (!string.IsNullOrEmpty(rwBare))
                            _jsessionIdUrlRewriteForFilterData = rwBare;
                    }
                    WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
                }

                var q = tripDate.ToString("yyyy-MM-dd");
                var url = WellRydeConfig.TripsPageAbsoluteUrl + "?date=" + Uri.EscapeDataString(q);
                var hashFrag = WellRydeConfig.TripsNuRefererHashFragment;
                if (!string.IsNullOrEmpty(hashFrag))
                {
                    if (!hashFrag.StartsWith("#", StringComparison.Ordinal))
                        hashFrag = "#" + hashFrag.TrimStart('/');
                    url += hashFrag;
                }
                using (var resp = await GetPortalHtmlDocumentAsync(url))
                {
                    WellRydeCookieHelper.IngestSetCookieHeaders(resp, CookieJar);
                    if (WellRydeConfig.DebugPortalTraffic)
                    {
                        if (resp.Headers.TryGetValues("Set-Cookie", out var setCookies))
                            WellRydeLog.WriteLine("WellRyde: GET nu?date= Set-Cookie: " + string.Join(" || ", setCookies));
                        else
                            WellRydeLog.WriteLine("WellRyde: GET nu?date= (response has no Set-Cookie)");
                    }
                    if (!resp.IsSuccessStatusCode)
                    {
                        WellRydeLog.WriteLine("WellRyde: GET trips page ?date= HTTP " + (int)resp.StatusCode + " — session or URL failed.");
                        return false;
                    }
                    var html = await resp.Content.ReadAsStringAsync();
                    WellRydeCookieHelper.TryIngestJSessionIdFromMarkupAndLocation(CookieJar, html);
                    var rwDated = WellRydeCookieHelper.TryGetFirstJsessionIdFromTextForUrlRewrite(CookieJar, html);
                    if (!string.IsNullOrEmpty(rwDated))
                        _jsessionIdUrlRewriteForFilterData = rwDated;
                    WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
                    var token = GrabCSRFToken(html) ?? TryGetCsrfFromCookies();
                    if (string.IsNullOrEmpty(token))
                    {
                        var hint = html != null && html.IndexOf("login", StringComparison.OrdinalIgnoreCase) >= 0
                            ? " (page looks like login — log in again)"
                            : "";
                        WellRydeLog.WriteLine("WellRyde: no _csrf from nu?date page (html length=" + (html?.Length ?? 0) + ")" + hint);
                        return false;
                    }
                    _CsrfToken = token;
                    PreferPortalXsrfCookieForCsrfToken();
                    SyncXsrfTokenCookieFromCurrentCsrf();
                    UpdateHandlerHeaders();
                    _lastTripsNuRefererWithDate = url;
                    // Chrome POSTs nps/timezone after loading nu?date= (not only when JSESSIONID was missing). Skipping it when the cookie
                    // already exists left trip filterlist/filterdata on HTML shells — servlet chain expects this XHR for VTripBilling.
                    await PostAuthenticatedNpsTimezoneServletPrimeAsync(url).ConfigureAwait(false);
                    WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
                    WellRydeCookieHelper.CollapseDuplicatePortalCookies(CookieJar);
                    await TryResolveVtTripBillingListDefIfNeededAsync();
                    if (!PortalCookieHeaderHasJsessionId())
                        await TryPrimeTripFilterListServletSessionAsync(url);
                    WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
                    if (!PortalCookieHeaderHasJsessionId())
                        await TryPrimeTripFilterListAsDocumentNavigateAsync(url);
                    WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
                    if (!PortalCookieHeaderHasJsessionId())
                        await EnsurePortalServletSessionFromShellDocumentGetAsync(url);
                    WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
                    if (!PortalCookieHeaderHasJsessionId())
                        await TryCaptureServletCookiesViaManualRedirectChainAsync(WellRydeConfig.TripsPageAbsoluteUrl, url);
                    WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
                    if (!PortalCookieHeaderHasJsessionId())
                        await TryPrimeJsessionIdViaPortalStaticResourceAsync(url);
                    WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
                    if (WellRydeConfig.FilterDataUseCurl && !string.IsNullOrEmpty(WellRydeCurlFilterData.TryResolveCurlPath()) && !PortalCookieHeaderHasJsessionId())
                    {
                        WellRydeCurlFilterData.TrySessionPrimingGet(CookieJar, url, url, PortalBrowserUserAgent);
                        WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
                    }
                    if (!PortalCookieHeaderHasJsessionId())
                        await TryAcquireTomcatJsessionViaNativeHandlerAsync(url).ConfigureAwait(false);
                    WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
                    return true;
                }
            }
            catch (Exception ex)
            {
                WellRydeLog.WriteLine("WellRyde: TryRefreshTripsNuCsrfAsync error: " + ex.Message);
            }
            return false;
        }

        /// <summary>
        /// Shared <see cref="CookieJar"/> with <c>HttpClientHandler.UseCookies=true</c> so redirect <c>Set-Cookie</c> (Tomcat <c>JSESSIONID</c>) merges like PHP libcurl — complements manual <see cref="WellRydeCookieHelper.IngestSetCookieHeaders"/>.
        /// </summary>
        private async Task TryServletCookiesUsingAutoHandlerDocumentGetsAsync(DateTime tripDate)
        {
            if (PortalCookieHeaderHasJsessionId())
                return;
            try
            {
                using (var handler = new HttpClientHandler
                {
                    CookieContainer = CookieJar,
                    UseCookies = true,
                    AutomaticDecompression = DecompressionMethods.GZip,
                })
                using (var http = new HttpClient(handler, disposeHandler: true))
                {
                    http.Timeout = Client.Timeout;
                    async Task RunGet(string url, string referer, string secFetchSite)
                    {
                        using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                        {
                            req.Headers.TryAddWithoutValidation("User-Agent", PortalBrowserUserAgent);
                            req.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                            req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
                            if (!string.IsNullOrEmpty(referer))
                                req.Headers.TryAddWithoutValidation("Referer", referer);
                            req.Headers.TryAddWithoutValidation("Sec-Fetch-Site", secFetchSite);
                            req.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
                            req.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
                            req.Headers.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
                            using (var resp = await http.SendAsync(req).ConfigureAwait(false))
                            {
                                WellRydeCookieHelper.IngestSetCookieHeaders(resp, CookieJar);
                                WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
                                if (resp.IsSuccessStatusCode)
                                {
                                    var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                                    WellRydeCookieHelper.TryIngestJSessionIdFromMarkupAndLocation(CookieJar, body);
                                }
                            }
                        }
                    }

                    var o = WellRydeConfig.PortalOrigin;
                    await RunGet(o + "/", null, "none").ConfigureAwait(false);
                    if (PortalCookieHeaderHasJsessionId())
                        return;
                    await RunGet(WellRydeConfig.PortalShellUrl, o + "/", "same-origin").ConfigureAwait(false);
                    if (PortalCookieHeaderHasJsessionId())
                        return;
                    await RunGet(WellRydeConfig.TripsPageAbsoluteUrl, WellRydeConfig.PortalShellUrl, "same-origin").ConfigureAwait(false);
                    if (PortalCookieHeaderHasJsessionId())
                        return;
                    var q = tripDate.ToString("yyyy-MM-dd");
                    await RunGet(WellRydeConfig.TripsPageAbsoluteUrl + "?date=" + Uri.EscapeDataString(q),
                        WellRydeConfig.TripsPageAbsoluteUrl, "same-origin").ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                if (WellRydeConfig.DebugPortalTraffic)
                    WellRydeLog.WriteLine("WellRyde: servlet auto-handler GET chain error: " + ex.Message);
            }
        }

        /// <summary>
        /// Chrome often carries both <c>SESSION</c> (Spring) and <c>JSESSIONID</c> (servlet). Document GETs to the webapp root establish the latter; XHR priming without it returns HTTP 401 and <c>filterdata</c> can still return HTML shells.
        /// </summary>
        private async Task EstablishPortalServletSessionAfterLoginAsync()
        {
            try
            {
                PreferPortalXsrfCookieForCsrfToken();
                SyncXsrfTokenCookieFromCurrentCsrf();
                if (!PortalCookieHeaderHasJsessionId())
                    await PostAuthenticatedNpsTimezoneServletPrimeAsync(WellRydeConfig.TripsPageAbsoluteUrl).ConfigureAwait(false);
                WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
                using (var r1 = await GetPortalHtmlDocumentAsync(WellRydeConfig.PortalShellUrl, WellRydeConfig.PortalOrigin + "/"))
                {
                    WellRydeCookieHelper.IngestSetCookieHeaders(r1, CookieJar);
                    if (r1.IsSuccessStatusCode)
                    {
                        var h1 = await r1.Content.ReadAsStringAsync();
                        WellRydeCookieHelper.TryIngestJSessionIdFromMarkupAndLocation(CookieJar, h1);
                    }
                }
                using (var r2 = await GetPortalHtmlDocumentAsync(WellRydeConfig.TripsPageAbsoluteUrl, WellRydeConfig.PortalShellUrl))
                {
                    WellRydeCookieHelper.IngestSetCookieHeaders(r2, CookieJar);
                    if (r2.IsSuccessStatusCode)
                    {
                        var h2 = await r2.Content.ReadAsStringAsync();
                        WellRydeCookieHelper.TryIngestJSessionIdFromMarkupAndLocation(CookieJar, h2);
                    }
                }
                // Some stacks only attach Set-Cookie: JSESSIONID on an intermediate 302; default AllowAutoRedirect can drop it from our manual ingest.
                await TryCaptureServletCookiesViaManualRedirectChainAsync(WellRydeConfig.TripsPageAbsoluteUrl, WellRydeConfig.PortalShellUrl);
                if (!PortalCookieHeaderHasJsessionId())
                    await TryPrimeJsessionIdViaPortalStaticResourceAsync(WellRydeConfig.TripsPageAbsoluteUrl);
                if (!PortalCookieHeaderHasJsessionId())
                    await TryAcquireTomcatJsessionViaNativeHandlerAsync(WellRydeConfig.TripsPageAbsoluteUrl).ConfigureAwait(false);
                WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
            }
            catch
            {
                /* non-fatal */
            }
        }

        /// <summary>
        /// Follow redirects manually so every <c>Set-Cookie</c> in the chain (including Tomcat <c>JSESSIONID</c>) is merged into <see cref="CookieJar"/>.
        /// </summary>
        private async Task TryCaptureServletCookiesViaManualRedirectChainAsync(string startUrl, string referer)
        {
            try
            {
                var chainInner = new HttpClientHandler
                {
                    UseCookies = false,
                    AutomaticDecompression = DecompressionMethods.GZip,
                    AllowAutoRedirect = false,
                };
                using (var chainClient = new HttpClient(new WellRydePortalCookieInjectingHandler(CookieJar, chainInner), disposeHandler: true))
                {
                    chainClient.Timeout = Client.Timeout;
                    var url = startUrl;
                    var chainReferer = string.IsNullOrEmpty(referer) ? WellRydeConfig.PortalOrigin + "/" : referer;
                    for (var hop = 0; hop < 12; hop++)
                    {
                        using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                        {
                            req.Headers.TryAddWithoutValidation("User-Agent", PortalBrowserUserAgent);
                            req.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                            req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
                            req.Headers.TryAddWithoutValidation("Referer", chainReferer);
                            req.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
                            req.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
                            req.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
                            req.Headers.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
                            using (var resp = await chainClient.SendAsync(req).ConfigureAwait(false))
                            {
                                WellRydeCookieHelper.IngestSetCookieHeaders(resp, CookieJar);
                                var code = (int)resp.StatusCode;
                                if (resp.IsSuccessStatusCode)
                                {
                                    var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                                    WellRydeCookieHelper.TryIngestJSessionIdFromMarkupAndLocation(CookieJar, body);
                                }
                                WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
                                if (code >= 300 && code < 400 && resp.Headers.Location != null)
                                {
                                    var loc = ResolveRedirectLocation(resp.RequestMessage.RequestUri, resp.Headers.Location);
                                    if (loc == null)
                                        break;
                                    chainReferer = url;
                                    url = loc.ToString();
                                    continue;
                                }
                                break;
                            }
                        }
                    }
                }
            }
            catch
            {
                /* non-fatal */
            }
        }

        /// <summary>
        /// Runs navigate/XHR/static GETs with <see cref="HttpClientHandler.UseCookies"/> on the shared <see cref="CookieJar"/> so the runtime merges
        /// <c>Set-Cookie: JSESSIONID</c> like Chrome (some stacks never surface that cookie to manual <see cref="WellRydeCookieHelper.IngestSetCookieHeaders"/> paths).
        /// </summary>
        private async Task TryAcquireTomcatJsessionViaNativeHandlerAsync(string datedOrBareNuReferer)
        {
            if (PortalCookieHeaderHasJsessionId())
                return;
            try
            {
                using (var handler = new HttpClientHandler
                {
                    CookieContainer = CookieJar,
                    UseCookies = true,
                    AutomaticDecompression = DecompressionMethods.GZip,
                })
                using (var http = new HttpClient(handler, disposeHandler: true))
                {
                    http.Timeout = Client.Timeout;

                    async Task NavigateGet(string requestUrl, string referer, string secFetchSite)
                    {
                        using (var req = new HttpRequestMessage(HttpMethod.Get, requestUrl))
                        {
                            req.Headers.TryAddWithoutValidation("User-Agent", PortalBrowserUserAgent);
                            req.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                            req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
                            if (!string.IsNullOrEmpty(referer))
                                req.Headers.TryAddWithoutValidation("Referer", referer);
                            req.Headers.TryAddWithoutValidation("Sec-Fetch-Site", secFetchSite);
                            req.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
                            req.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
                            req.Headers.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
                            using (var resp = await http.SendAsync(req).ConfigureAwait(false))
                                await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                        }
                    }

                    async Task NoCorsGet(string requestUrl, string referer, string accept, string dest)
                    {
                        using (var req = new HttpRequestMessage(HttpMethod.Get, requestUrl))
                        {
                            req.Headers.TryAddWithoutValidation("User-Agent", PortalBrowserUserAgent);
                            req.Headers.TryAddWithoutValidation("Accept", accept);
                            req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
                            req.Headers.TryAddWithoutValidation("Referer", referer);
                            req.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
                            req.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "no-cors");
                            req.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", dest);
                            using (var resp = await http.SendAsync(req).ConfigureAwait(false))
                                await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                        }
                    }

                    var o = WellRydeConfig.PortalOrigin;
                    await NavigateGet(o + "/", null, "none").ConfigureAwait(false);
                    if (PortalCookieHeaderHasJsessionId()) return;
                    await NavigateGet(WellRydeConfig.PortalShellUrl, o + "/", "same-origin").ConfigureAwait(false);
                    if (PortalCookieHeaderHasJsessionId()) return;
                    await NavigateGet(WellRydeConfig.TripsPageAbsoluteUrl, WellRydeConfig.PortalShellUrl, "same-origin").ConfigureAwait(false);
                    if (PortalCookieHeaderHasJsessionId()) return;
                    if (!string.IsNullOrEmpty(datedOrBareNuReferer)
                        && datedOrBareNuReferer.IndexOf("date=", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        await NavigateGet(datedOrBareNuReferer, WellRydeConfig.TripsPageAbsoluteUrl, "same-origin").ConfigureAwait(false);
                        if (PortalCookieHeaderHasJsessionId()) return;
                    }

                    if (!string.IsNullOrEmpty(_CsrfToken))
                    {
                        var tz = FormatWellrydeGmtOffset(TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow));
                        var tzForm = new FormUrlEncodedContent(new[]
                        {
                            new KeyValuePair<string, string>("timeZone", tz),
                            new KeyValuePair<string, string>("_csrf", _CsrfToken),
                        });
                        var tzReferer = !string.IsNullOrEmpty(datedOrBareNuReferer)
                            && datedOrBareNuReferer.IndexOf("date=", StringComparison.OrdinalIgnoreCase) >= 0
                            ? datedOrBareNuReferer
                            : WellRydeConfig.TripsPageAbsoluteUrl;
                        using (var req = new HttpRequestMessage(HttpMethod.Post, WellRydeConfig.NpsTimezoneUrl))
                        {
                            req.Content = tzForm;
                            req.Headers.TryAddWithoutValidation("User-Agent", PortalBrowserUserAgent);
                            req.Headers.TryAddWithoutValidation("Accept", "application/json, text/javascript, */*;q=0.01");
                            req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
                            req.Headers.TryAddWithoutValidation("Origin", o);
                            req.Headers.TryAddWithoutValidation("Referer", tzReferer);
                            req.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
                            req.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", _CsrfToken);
                            req.Headers.TryAddWithoutValidation("X-XSRF-TOKEN", _CsrfToken);
                            req.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
                            req.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "cors");
                            req.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "empty");
                            req.Headers.TryAddWithoutValidation("sec-ch-ua", PortalFilterDataSecChUa);
                            req.Headers.TryAddWithoutValidation("sec-ch-ua-mobile", "?0");
                            req.Headers.TryAddWithoutValidation("sec-ch-ua-platform", "\"Windows\"");
                            using (var resp = await http.SendAsync(req).ConfigureAwait(false))
                                await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                        }
                        if (PortalCookieHeaderHasJsessionId()) return;
                    }

                    var xhrReferer = !string.IsNullOrEmpty(datedOrBareNuReferer)
                        && datedOrBareNuReferer.IndexOf("date=", StringComparison.OrdinalIgnoreCase) >= 0
                        ? datedOrBareNuReferer
                        : WellRydeConfig.TripsPageAbsoluteUrl;
                    var flUrl = WellRydeConfig.TripFilterListRequestUrl;
                    using (var reqFl = new HttpRequestMessage(HttpMethod.Get, flUrl))
                    {
                        ApplyWellRydeTripFilterlistXHRHeaders(reqFl, useAlternateJsonAccept: false);
                        WellRydeCookieHelper.CollapseDuplicatePortalCookies(CookieJar);
                        using (var resp = await _filterDataClient.SendAsync(reqFl).ConfigureAwait(false))
                        {
                            WellRydeCookieHelper.IngestSetCookieHeaders(resp, CookieJar);
                            WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
                            await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                        }
                    }
                    if (PortalCookieHeaderHasJsessionId()) return;

                    foreach (var path in JsessionStaticProbePaths)
                    {
                        if (PortalCookieHeaderHasJsessionId()) return;
                        try
                        {
                            var isCss = path.EndsWith(".css", StringComparison.OrdinalIgnoreCase);
                            await NoCorsGet(o + path, xhrReferer, isCss ? "text/css,*/*;q=0.1" : "*/*", isCss ? "style" : "script").ConfigureAwait(false);
                        }
                        catch
                        {
                            /* next */
                        }
                    }
                }
                WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
                if (WellRydeConfig.DebugPortalTraffic && PortalCookieHeaderHasJsessionId())
                    WellRydeLog.WriteLine("WellRyde: native cookie-handler chain stored JSESSIONID for filterdata.");
            }
            catch (Exception ex)
            {
                if (WellRydeConfig.DebugPortalTraffic)
                    WellRydeLog.WriteLine("WellRyde: TryAcquireTomcatJsessionViaNativeHandlerAsync: " + ex.Message);
            }
        }

        /// <summary>Static URLs seen on the SPA HTML shell; Tomcat may <c>Set-Cookie: JSESSIONID</c> on one of these while <c>GET /portal/nu</c> does not.</summary>
        private static readonly string[] JsessionStaticProbePaths =
        {
            "/portal/resources/styles/commonStyle.css",
            "/portal/resources/bootstrap_3.3.6/css/bootstrap.min.css",
            "/portal/resources/bootstrap_3.3.6/js/bootstrap.min.js",
            "/portal/resources/inspinia/js/jquery/jquery-2.1.1.min.js",
            "/portal/resources/scripts/jquery/jquery-3.6.0.min.js",
        };

        /// <summary>
        /// Chrome loads many <c>/portal/resources/…</c> assets after <c>/portal/nu</c>; Tomcat sometimes issues <c>Set-Cookie: JSESSIONID</c> on the first
        /// servlet-mapped static hit while the SPA HTML GET does not. Mirror those fetches with <paramref name="referer"/> matching the SPA (e.g. <c>/portal/nu?date=</c>).
        /// </summary>
        private async Task TryPrimeJsessionIdViaPortalStaticResourceAsync(string referer)
        {
            if (PortalCookieHeaderHasJsessionId())
                return;
            var refererVal = string.IsNullOrEmpty(referer) ? WellRydeConfig.TripsPageAbsoluteUrl : referer;
            var o = WellRydeConfig.PortalOrigin;
            try
            {
                foreach (var path in JsessionStaticProbePaths)
                {
                    if (PortalCookieHeaderHasJsessionId())
                        return;
                    try
                    {
                        var isCss = path.EndsWith(".css", StringComparison.OrdinalIgnoreCase);
                        await TryOneStaticJsessionProbeAsync(refererVal, o + path,
                            isCss ? "text/css,*/*;q=0.1" : "*/*",
                            isCss ? "style" : "script").ConfigureAwait(false);
                    }
                    catch
                    {
                        /* try next */
                    }
                }

                if (PortalCookieHeaderHasJsessionId())
                    return;
                var curl = WellRydeCurlFilterData.TryResolveCurlPath();
                if (string.IsNullOrEmpty(curl))
                    return;
                foreach (var path in JsessionStaticProbePaths)
                {
                    if (PortalCookieHeaderHasJsessionId())
                        return;
                    try
                    {
                        WellRydeCurlFilterData.TrySessionPrimingGet(CookieJar, o + path, refererVal, PortalBrowserUserAgent);
                        WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
                    }
                    catch
                    {
                        /* next */
                    }
                }
            }
            catch
            {
                /* non-fatal */
            }
        }

        private async Task TryOneStaticJsessionProbeAsync(string refererVal, string url, string accept, string secFetchDest)
        {
            using (var req = new HttpRequestMessage(HttpMethod.Get, url))
            {
                req.Headers.TryAddWithoutValidation("User-Agent", PortalBrowserUserAgent);
                req.Headers.TryAddWithoutValidation("Accept", accept);
                req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
                req.Headers.TryAddWithoutValidation("Referer", refererVal);
                req.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
                req.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "no-cors");
                req.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", secFetchDest);
                using (var resp = await _filterDataClient.SendAsync(req).ConfigureAwait(false))
                {
                    WellRydeCookieHelper.IngestSetCookieHeaders(resp, CookieJar);
                    WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
                }
            }
        }

        private async Task EnsurePortalServletSessionFromShellDocumentGetAsync(string referer)
        {
            try
            {
                using (var r = await GetPortalHtmlDocumentAsync(WellRydeConfig.PortalShellUrl,
                    string.IsNullOrEmpty(referer) ? WellRydeConfig.PortalOrigin + "/" : referer))
                {
                    WellRydeCookieHelper.IngestSetCookieHeaders(r, CookieJar);
                    if (r.IsSuccessStatusCode)
                    {
                        var h = await r.Content.ReadAsStringAsync();
                        WellRydeCookieHelper.TryIngestJSessionIdFromMarkupAndLocation(CookieJar, h);
                    }
                }
            }
            catch
            {
                /* non-fatal */
            }
        }

        private bool PortalCookieHeaderHasJsessionId()
        {
            try
            {
                var fu = new Uri(WellRydeConfig.FilterDataUrl);
                if (WellRydeCookieHelper.JsessionIdLooksSynthesizedFromSpringSession(CookieJar, fu))
                    return false;
                var ch = WellRydeCookieHelper.GetCookieHeader(CookieJar, fu);
                return !string.IsNullOrEmpty(ch)
                    && ch.IndexOf("JSESSIONID=", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Chrome trip XHR for <c>/portal/trip/filterlist</c>: bare <c>/portal/nu</c> referer, <c>X-Requested-With: XMLHttpRequest</c>, and <c>X-CSRF-TOKEN</c> / <c>X-XSRF-TOKEN</c> when a token is known (required for JSON — without them the servlet returns the Dojo HTML shell).
        /// <paramref name="useAlternateJsonAccept"/>: when <c>false</c>, <c>Accept: application/json, text/plain, */*</c> (Chrome HAR); when <c>true</c>, Dojo-style <c>application/json, text/javascript, */*; q=0.01</c> for a second attempt only.
        /// Trip <c>filterlist</c> uses <see cref="_filterDataClient"/> so <c>Cookie</c> is built by <see cref="WellRydePortalCookieInjectingHandler"/> (deduped <c>JSESSIONID</c>).
        /// </summary>
        private void ApplyWellRydeTripFilterlistXHRHeaders(HttpRequestMessage req, bool useAlternateJsonAccept)
        {
            req.Headers.TryAddWithoutValidation("Accept", useAlternateJsonAccept
                ? "application/json, text/javascript, */*; q=0.01"
                : "application/json, text/plain, */*");
            req.Headers.TryAddWithoutValidation("User-Agent", PortalBrowserUserAgent);
            req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
            req.Headers.TryAddWithoutValidation("Priority", "u=1, i");
            var referer = TripsRefererForWellRydeAjax;
            req.Headers.TryAddWithoutValidation("Referer", referer);
            req.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
            var xsrfFromCookie = TryGetCsrfFromCookies();
            var csrfForAjax = !string.IsNullOrEmpty(xsrfFromCookie) ? xsrfFromCookie : _CsrfToken;
            if (!string.IsNullOrEmpty(csrfForAjax))
            {
                req.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", csrfForAjax);
                req.Headers.TryAddWithoutValidation("X-XSRF-TOKEN", csrfForAjax);
            }
            req.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
            req.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "cors");
            req.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "empty");
            req.Headers.TryAddWithoutValidation("sec-ch-ua", PortalFilterDataSecChUa);
            req.Headers.TryAddWithoutValidation("sec-ch-ua-mobile", "?0");
            req.Headers.TryAddWithoutValidation("sec-ch-ua-platform", "\"Windows\"");
        }

        /// <summary>
        /// GET <see cref="WellRydeConfig.TripFilterListRequestUrl"/> (default double slash per Chrome HAR; set <c>WellRydeTripFilterListDoubleSlash=false</c> for single-slash first): (1) learn <c>SEC-S_…</c> when <c>WellRydeListDefId</c> is not pinned in config, and/or
        /// (2) <b>prime servlet session</b> the way Chrome does before <c>POST /portal/filterdata</c>.
        /// When <c>WellRydeListDefId</c> is explicit <c>SEC-S_*</c>, we used to skip this GET entirely — Tomcat already had <c>JSESSIONID</c> from <c>nu</c>,
        /// but the portal still returned NU HTML for <c>filterdata</c> until this XHR runs (list metadata bound in session).
        /// </summary>
        private async Task TryResolveVtTripBillingListDefIfNeededAsync()
        {
            var listId = WellRydeConfig.TripFilterListDefId ?? "";
            var isVtSecS = listId.StartsWith("SEC-S_", StringComparison.Ordinal);
            // Legacy SEC-J: only GET when auto-resolving an unpinned id (PHP-style).
            if (!isVtSecS)
            {
                if (!WellRydeConfig.AutoResolveTripFilterListDef
                    || WellRydeConfig.HasExplicitTripListDefId
                    || !string.IsNullOrEmpty(WellRydeConfig.ResolvedTripListDefId))
                    return;
            }
            else
            {
                if (!WellRydeConfig.AutoResolveTripFilterListDef && !WellRydeConfig.HasExplicitTripListDefId)
                    return;
            }

            try
            {
                foreach (var url in WellRydeConfig.EnumerateTripFilterListRequestUrls())
                {
                    for (var attempt = 0; attempt < 2; attempt++)
                    {
                        var req = new HttpRequestMessage(HttpMethod.Get, url);
                        ApplyWellRydeTripFilterlistXHRHeaders(req, useAlternateJsonAccept: attempt != 0);

                        // Use _filterDataClient (WellRydePortalCookieInjectingHandler): one Cookie line, deduped JSESSIONID.
                        // Client + UseCookies can emit two JSESSIONID for Path=/portal vs /portal/ → HTTP 401 on /trip/filterlist.
                        WellRydeCookieHelper.CollapseDuplicatePortalCookies(CookieJar);
                        using (var resp = await _filterDataClient.SendAsync(req).ConfigureAwait(false))
                        {
                            WellRydeCookieHelper.IngestSetCookieHeaders(resp, CookieJar);
                            WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
                            var body = await resp.Content.ReadAsStringAsync();
                            var ct = resp.Content.Headers.ContentType?.ToString() ?? "";
                            if (resp.StatusCode != HttpStatusCode.OK || string.IsNullOrEmpty(body))
                            {
                                WellRydeLog.WriteLine("WellRyde: trip filterlist HTTP " + (int)resp.StatusCode + " content-type=" + ct + " len=" + (body?.Length ?? 0)
                                    + " setJsession=" + SetCookieHeadersMentionJsessionId(resp) + " url=" + url
                                    + " prefix=" + TrimForWellRydeLog(body, 200)
                                    + (attempt == 0 ? " — retrying with alternate Accept." : " — next URL or give up."));
                                if (attempt == 0)
                                    continue;
                                break;
                            }

                            var htmlShell = body.TrimStart().StartsWith("<", StringComparison.Ordinal);
                            if (htmlShell)
                            {
                                if (attempt == 0)
                                {
                                    if (WellRydeConfig.DebugPortalTraffic)
                                        WellRydeLog.WriteLine("WellRyde: trip filterlist HTTP 200 HTML (Dojo shell, len=" + body.Length + ") url=" + url + " — retry SPA headers.");
                                    continue;
                                }

                                WellRydeLog.WriteLine("WellRyde: trip filterlist resolve skipped HTTP " + (int)resp.StatusCode + " content-type=" + ct + " len=" + (body?.Length ?? 0)
                                    + " setJsession=" + SetCookieHeadersMentionJsessionId(resp) + " url=" + url + " prefix=" + TrimForWellRydeLog(body, 200));
                                break;
                            }

                            if (!WellRydeConfig.HasExplicitTripListDefId && string.IsNullOrEmpty(WellRydeConfig.ResolvedTripListDefId))
                            {
                                var id = ExtractListDefIdFromFilterListJson(body);
                                if (!string.IsNullOrEmpty(id))
                                {
                                    WellRydeConfig.ResolvedTripListDefId = id;
                                    WellRydeLog.WriteLine("WellRyde: resolved trip listDefId from filterlist API: " + id);
                                }
                                else if (WellRydeConfig.DebugPortalTraffic)
                                    WellRydeLog.WriteLine("WellRyde: filterlist JSON parse found no SEC-* listDefId (prefix " +
                                                          (body.Length > 160 ? body.Substring(0, 160) + "…" : body) + ")");
                            }
                            else if (isVtSecS && WellRydeConfig.DebugPortalTraffic)
                                WellRydeLog.WriteLine("WellRyde: trip filterlist XHR prime OK (explicit SEC-S listDefId — servlet session for filterdata). HTTP " + (int)resp.StatusCode + " len=" + body.Length + " url=" + url);
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (WellRydeConfig.DebugPortalTraffic)
                    WellRydeLog.WriteLine("WellRyde: filterlist resolve error: " + ex.Message);
            }
        }

        /// <summary>
        /// GET <c>/portal/trip/filterlist</c> when <c>JSESSIONID</c> is still missing after <c>nu</c> (often <c>Set-Cookie</c> on this XHR only).
        /// For <c>SEC-S_*</c> lists, <see cref="TryResolveVtTripBillingListDefIfNeededAsync"/> also runs this XHR even when the cookie is already present.
        /// </summary>
        /// <param name="tripsPageReferer">Unused; referer comes from <see cref="TripsRefererForWellRydeAjax"/> for SPA parity.</param>
        private async Task TryPrimeTripFilterListServletSessionAsync(string tripsPageReferer)
        {
            try
            {
                foreach (var url in WellRydeConfig.EnumerateTripFilterListRequestUrls())
                {
                    for (var attempt = 0; attempt < 2; attempt++)
                    {
                        var req = new HttpRequestMessage(HttpMethod.Get, url);
                        ApplyWellRydeTripFilterlistXHRHeaders(req, useAlternateJsonAccept: attempt != 0);

                        WellRydeCookieHelper.CollapseDuplicatePortalCookies(CookieJar);
                        using (var resp = await _filterDataClient.SendAsync(req).ConfigureAwait(false))
                        {
                            WellRydeCookieHelper.IngestSetCookieHeaders(resp, CookieJar);
                            WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
                            if (WellRydeConfig.DebugPortalTraffic)
                                WellRydeLog.WriteLine("WellRyde: trip filterlist XHR prime HTTP " + (int)resp.StatusCode + " list=" + WellRydeConfig.TripFilterListName
                                    + " url=" + url + " altAccept=" + (attempt != 0) + " setJsession=" + SetCookieHeadersMentionJsessionId(resp));
                        }
                        if (PortalCookieHeaderHasJsessionId())
                            break;
                    }
                    if (PortalCookieHeaderHasJsessionId())
                        break;
                }
            }
            catch (Exception ex)
            {
                if (WellRydeConfig.DebugPortalTraffic)
                    WellRydeLog.WriteLine("WellRyde: filterlist servlet prime error: " + ex.Message);
            }
        }

        /// <summary>Fallback: full navigation-style GET (some stacks only <c>Set-Cookie: JSESSIONID</c> on <c>Sec-Fetch-Mode: navigate</c>).</summary>
        private async Task TryPrimeTripFilterListAsDocumentNavigateAsync(string tripsPageReferer)
        {
            try
            {
                foreach (var url in WellRydeConfig.EnumerateTripFilterListRequestUrls())
                {
                    var req = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.TryAddWithoutValidation("User-Agent", PortalBrowserUserAgent);
                    req.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,application/json;q=0.8,*/*;q=0.7");
                    req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
                    req.Headers.TryAddWithoutValidation("Referer", string.IsNullOrEmpty(tripsPageReferer) ? TripsRefererForWellRydeAjax : tripsPageReferer);
                    req.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
                    req.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
                    req.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
                    WellRydeCookieHelper.CollapseDuplicatePortalCookies(CookieJar);
                    using (var resp = await _filterDataClient.SendAsync(req).ConfigureAwait(false))
                    {
                        WellRydeCookieHelper.IngestSetCookieHeaders(resp, CookieJar);
                        if (resp.IsSuccessStatusCode)
                        {
                            var body = await resp.Content.ReadAsStringAsync();
                            WellRydeCookieHelper.TryIngestJSessionIdFromMarkupAndLocation(CookieJar, body);
                            var rw = WellRydeCookieHelper.TryGetFirstJsessionIdFromTextForUrlRewrite(CookieJar, body);
                            if (!string.IsNullOrEmpty(rw))
                                _jsessionIdUrlRewriteForFilterData = rw;
                        }
                        WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
                        if (WellRydeConfig.DebugPortalTraffic)
                            WellRydeLog.WriteLine("WellRyde: trip filterlist document prime HTTP " + (int)resp.StatusCode + " url=" + url
                                + " setJsession=" + SetCookieHeadersMentionJsessionId(resp));
                    }
                }
            }
            catch (Exception ex)
            {
                if (WellRydeConfig.DebugPortalTraffic)
                    WellRydeLog.WriteLine("WellRyde: filterlist document prime error: " + ex.Message);
            }
        }

        private static string TrimForWellRydeLog(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s))
                return "(empty)";
            s = s.Replace('\r', ' ').Replace('\n', ' ');
            return s.Length <= maxLen ? s : s.Substring(0, maxLen) + "…";
        }

        private static bool SetCookieHeadersMentionJsessionId(HttpResponseMessage resp)
        {
            if (resp?.Headers == null)
                return false;
            if (!resp.Headers.TryGetValues("Set-Cookie", out var vals))
                return false;
            foreach (var v in vals)
            {
                if (v != null && v.IndexOf("JSESSIONID", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private static string ExtractListDefIdFromFilterListJson(string json)
        {
            try
            {
                var t = JToken.Parse(json);
                foreach (var path in new[] { "listDefId", "listDefinitionId", "listDef" })
                {
                    foreach (var tok in t.SelectTokens("$.." + path))
                    {
                        if (tok?.Type == JTokenType.String)
                        {
                            var s = tok.Value<string>();
                            if (!string.IsNullOrEmpty(s) && s.StartsWith("SEC-", StringComparison.Ordinal))
                                return s;
                        }
                        if (tok?.Type == JTokenType.Object && tok["id"]?.Type == JTokenType.String)
                        {
                            var s = tok["id"].Value<string>();
                            if (!string.IsNullOrEmpty(s) && s.StartsWith("SEC-", StringComparison.Ordinal))
                                return s;
                        }
                    }
                }

                foreach (var v in t.SelectTokens("$..*").OfType<JValue>())
                {
                    if (v.Type != JTokenType.String)
                        continue;
                    var s = v.Value<string>();
                    if (string.IsNullOrEmpty(s) || s.Length < 12)
                        continue;
                    if (s.StartsWith("SEC-S_", StringComparison.Ordinal))
                        return s;
                }

                foreach (var v in t.SelectTokens("$..*").OfType<JValue>())
                {
                    if (v.Type != JTokenType.String)
                        continue;
                    var s = v.Value<string>();
                    if (!string.IsNullOrEmpty(s) && s.StartsWith("SEC-", StringComparison.Ordinal) && s.Length >= 12)
                        return s;
                }
            }
            catch
            {
                /* ignore */
            }
            return null;
        }

        public async Task<bool> TryRefreshPortalCsrfAsync()
        {
            try
            {
                foreach (var url in new[]
                {
                    WellRydeConfig.PortalShellUrl,
                    WellRydeConfig.TripsPageAbsoluteUrl,
                })
                {
                    using (var resp = await GetPortalHtmlDocumentAsync(url))
                    {
                        if (!resp.IsSuccessStatusCode)
                            continue;
                        var html = await resp.Content.ReadAsStringAsync();
                        var token = GrabCSRFToken(html) ?? TryGetCsrfFromCookies();
                        if (!string.IsNullOrEmpty(token))
                        {
                            _CsrfToken = token;
                            PreferPortalXsrfCookieForCsrfToken();
                            SyncXsrfTokenCookieFromCurrentCsrf();
                            UpdateHandlerHeaders();
                            return true;
                        }
                    }
                }
                var cookieOnly = TryGetCsrfFromCookies();
                if (!string.IsNullOrEmpty(cookieOnly))
                {
                    _CsrfToken = cookieOnly;
                    PreferPortalXsrfCookieForCsrfToken();
                    SyncXsrfTokenCookieFromCurrentCsrf();
                    UpdateHandlerHeaders();
                    return true;
                }
            }
            catch
            {
            }
            return false;
        }

        public async Task ResetConnection()
        {
            this.IntentionalLogout = false;
            this.Connected = false;
            await Task.Run(() => {
                while (!this.Connected)
                {
                    // operation
                   
                    WellRydeLog.WriteLine("Wellryde: Waiting for connection..");
                    System.Threading.Thread.Sleep(1000);
                }
                //Console.WriteLine("connected");
            });

        }
        private async Task PostWellrydeTimezoneAsync(string csrf)
        {
            try
            {
                var tz = FormatWellrydeGmtOffset(TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow));
                var tzForm = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("timeZone", tz),
                    new KeyValuePair<string, string>("_csrf", csrf),
                });
                using (var req = new HttpRequestMessage(HttpMethod.Post, WellRydeConfig.NpsTimezoneUrl))
                {
                    req.Content = tzForm;
                    req.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
                    using (var tzResp = await Client.SendAsync(req))
                    {
                        WellRydeCookieHelper.IngestSetCookieHeaders(tzResp, CookieJar);
                        await tzResp.Content.ReadAsByteArrayAsync();
                    }
                }
            }
            catch
            {
                // Browser sends this before login; failure is non-fatal for some tenants.
            }
        }

        /// <summary>
        /// Chrome POSTs <c>/portal/nps/timezone</c> after loading the trips SPA with the current CSRF. Often when Tomcat first
        /// <c>Set-Cookie: JSESSIONID</c> binds to Spring <c>SESSION</c>; also primes servlet state for <c>trip/filterlist</c> / <c>filterdata</c> even if <c>JSESSIONID</c> was already set from <c>nu</c>.
        /// </summary>
        private async Task PostAuthenticatedNpsTimezoneServletPrimeAsync(string referer)
        {
            if (string.IsNullOrEmpty(_CsrfToken))
                return;
            try
            {
                var tz = FormatWellrydeGmtOffset(TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow));
                var tzForm = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("timeZone", tz),
                    new KeyValuePair<string, string>("_csrf", _CsrfToken),
                });
                var refererVal = string.IsNullOrEmpty(referer) ? WellRydeConfig.TripsPageAbsoluteUrl : referer;
                using (var req = new HttpRequestMessage(HttpMethod.Post, WellRydeConfig.NpsTimezoneUrl))
                {
                    req.Content = tzForm;
                    req.Headers.TryAddWithoutValidation("User-Agent", PortalBrowserUserAgent);
                    req.Headers.TryAddWithoutValidation("Accept", "application/json, text/javascript, */*;q=0.01");
                    req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
                    req.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate");
                    req.Headers.TryAddWithoutValidation("Origin", WellRydeConfig.PortalOrigin);
                    req.Headers.TryAddWithoutValidation("Referer", refererVal);
                    req.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
                    req.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", _CsrfToken);
                    req.Headers.TryAddWithoutValidation("X-XSRF-TOKEN", _CsrfToken);
                    req.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
                    req.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "cors");
                    req.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "empty");
                    req.Headers.TryAddWithoutValidation("sec-ch-ua", PortalFilterDataSecChUa);
                    req.Headers.TryAddWithoutValidation("sec-ch-ua-mobile", "?0");
                    req.Headers.TryAddWithoutValidation("sec-ch-ua-platform", "\"Windows\"");
                    using (var resp = await _filterDataClient.SendAsync(req).ConfigureAwait(false))
                    {
                        WellRydeCookieHelper.IngestSetCookieHeaders(resp, CookieJar);
                        WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
                        if (WellRydeConfig.DebugPortalTraffic && resp.Headers.TryGetValues("Set-Cookie", out var sc))
                            WellRydeLog.WriteLine("WellRyde: POST nps/timezone (authenticated) Set-Cookie: " + string.Join(" || ", sc));
                        await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    }
                }
            }
            catch
            {
                /* non-fatal */
            }
        }

        private static string FormatWellrydeGmtOffset(TimeSpan offset)
        {
            var totalMinutes = (int)Math.Round(offset.TotalMinutes);
            var sign = totalMinutes >= 0 ? "+" : "-";
            var abs = Math.Abs(totalMinutes);
            var h = abs / 60;
            var m = abs % 60;
            return string.Format(CultureInfo.InvariantCulture, "GMT{0}{1:D2}:{2:D2}", sign, h, m);
        }

        private static Uri ResolveRedirectLocation(Uri requestUri, Uri location)
        {
            if (location == null || requestUri == null) return null;
            return location.IsAbsoluteUri ? location : new Uri(requestUri, location);
        }

        private static bool RedirectIndicatesLoginFailure(string urlOrHtml)
        {
            if (string.IsNullOrEmpty(urlOrHtml)) return false;
            return urlOrHtml.IndexOf("error=t", StringComparison.OrdinalIgnoreCase) >= 0
                || urlOrHtml.IndexOf("error%3dt", StringComparison.OrdinalIgnoreCase) >= 0
                || urlOrHtml.IndexOf("error%3Dt", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool TryExtractHiddenInputValue(string html, string name, out string value)
        {
            value = null;
            if (string.IsNullOrEmpty(html) || string.IsNullOrEmpty(name)) return false;
            var esc = Regex.Escape(name);
            var patterns = new[]
            {
                $@"name=""{esc}""[^>]*value=""([^""]*)""",
                $@"value=""([^""]*)""[^>]*name=""{esc}""",
            };
            foreach (var p in patterns)
            {
                var m = Regex.Match(html, p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (m.Success)
                {
                    value = m.Groups[1].Value;
                    return true;
                }
            }
            return false;
        }

        //Update tokens and headers
        public string GrabCSRFToken(string resp)
        {
            try
            {
                if (string.IsNullOrEmpty(resp))
                    return null;
                string response = resp;

                var metaOrdered = Regex.Match(response,
                    @"<meta\s+name\s*=\s*[""']_csrf[""']\s+content\s*=\s*[""']([^""']+)[""']",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (metaOrdered.Success)
                    return metaOrdered.Groups[1].Value;

                var metaContentFirst = Regex.Match(response,
                    @"<meta\s+content\s*=\s*[""']([^""']+)[""']\s+name\s*=\s*[""']_csrf[""']",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (metaContentFirst.Success)
                    return metaContentFirst.Groups[1].Value;

                string decodedfilterValues = HttpUtility.UrlDecode("<meta name=\"_csrf\" content=\"");
                string decodedfilterValues2 = HttpUtility.UrlDecode("\" /><link rel=");

                int startPoint = response.IndexOf(decodedfilterValues);
                if (startPoint >= 0)
                {
                    startPoint += decodedfilterValues.Length;
                    int endPOint = response.LastIndexOf(decodedfilterValues2);
                    if (endPOint > startPoint)
                        return response.Substring(startPoint, endPOint - startPoint);
                }

                if (TryExtractHiddenInputValue(response, "_csrf", out var hidden))
                    return hidden;
            }
            catch (Exception ex)
            {
                //Console.WriteLine(ex.ToString());
            }
            return null;
        }

        /// <summary>
        /// Spring stores CSRF in the <c>XSRF-TOKEN</c> cookie with <c>Path=/portal</c> (or similar). <see cref="CookieContainer.GetCookies(Uri)"/>
        /// for <c>https://host/</c> does not return those cookies, so we must query <c>/portal/…</c> URIs or AJAX CSRF headers/body disagree with the browser.
        /// </summary>
        private string TryGetCsrfFromCookies()
        {
            try
            {
                var uris = new[]
                {
                    new Uri(WellRydeConfig.FilterDataUrl),
                    new Uri(WellRydeConfig.TripsPageAbsoluteUrl),
                    new Uri(WellRydeConfig.PortalShellUrl),
                    new Uri(WellRydeConfig.PortalOrigin + "/"),
                };
                foreach (var u in uris)
                {
                    foreach (Cookie c in CookieJar.GetCookies(u).Cast<Cookie>())
                    {
                        if (string.Equals(c.Name, "XSRF-TOKEN", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(c.Name, "xsrf-token", StringComparison.OrdinalIgnoreCase))
                        {
                            var v = c.Value;
                            if (!string.IsNullOrEmpty(v))
                                return Uri.UnescapeDataString(v);
                        }
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        /// <summary>When the portal sets <c>XSRF-TOKEN</c> for <c>/portal</c>, prefer it over HTML meta for <c>_csrf</c> (form + AJAX headers).</summary>
        private void PreferPortalXsrfCookieForCsrfToken()
        {
            var fromCookie = TryGetCsrfFromCookies();
            if (!string.IsNullOrEmpty(fromCookie))
                _CsrfToken = fromCookie;
        }

        /// <summary>
        /// Spring <c>CookieCsrfTokenRepository</c> expects the <c>XSRF-TOKEN</c> cookie on POST alongside <c>X-XSRF-TOKEN</c> / <c>_csrf</c>.
        /// The portal may not emit that cookie into our <see cref="CookieJar"/> (JS-only or parsing edge); without it, <c>filterdata</c> often returns HTTP 200 HTML shells.
        /// After we have the canonical token from meta or a real cookie, mirror it into the jar so outbound requests match Chrome.
        /// </summary>
        private void SyncXsrfTokenCookieFromCurrentCsrf()
        {
            if (string.IsNullOrEmpty(_CsrfToken) || CookieJar == null)
                return;
            try
            {
                var host = new Uri(WellRydeConfig.FilterDataUrl).Host;
                var cookieVal = Uri.EscapeDataString(_CsrfToken);
                // Match SESSION cookie path (/portal/) so CookieContainer applies XSRF to the same URLs as the session.
                CookieJar.Add(new Cookie("XSRF-TOKEN", cookieVal, "/portal/", host)
                {
                    Secure = string.Equals(new Uri(WellRydeConfig.FilterDataUrl).Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase),
                });
            }
            catch
            {
                /* duplicate or invalid — ignore */
            }
        }

        private void UpdateHandlerHeaders()//update headers to retrieve batches of calls
        {
            try {
                Client.DefaultRequestHeaders.Clear(); //JUST ADDED!!!
                Client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", PortalBrowserUserAgent);
                Client.DefaultRequestHeaders.TryAddWithoutValidation("sec-ch-ua", PortalFilterDataSecChUa);
            Client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
            if (!string.IsNullOrEmpty(_CsrfToken))
            {
                Client.DefaultRequestHeaders.TryAddWithoutValidation("X-CSRF-TOKEN", _CsrfToken);
                Client.DefaultRequestHeaders.TryAddWithoutValidation("X-XSRF-TOKEN", _CsrfToken);
            }
            Client.DefaultRequestHeaders.Add("sec-ch-ua-mobile", "?0");
            Client.DefaultRequestHeaders.TryAddWithoutValidation("sec-ch-ua-platform", "\"Windows\"");
            Client.DefaultRequestHeaders.Add("Origin", WellRydeConfig.PortalOrigin);
            Client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");
            Client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "cors");
            Client.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "empty");
            Client.DefaultRequestHeaders.Add("Referer", TripsRefererForWellRydeAjax);
            Client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");
            Client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
            Client.DefaultRequestHeaders.Remove("Accept");
            Client.DefaultRequestHeaders.Add("Accept", "application/json, text/javascript, */*; q=0.01");
            }
            catch (System.FormatException e)
            {
                //Console.WriteLine("wrLoginHandler: UpdateHandlerHeaders():" + e.Message);
            }
        }

        //Event handlers
        public event PropertyChangedEventHandler PropertyChanged;
        bool connected;
        public bool Connected
        {
            get { return connected; }
            set
            {
                connected = value;
                OnPropertyChanged(nameof(Connected));
            }
        }    
        protected void OnPropertyChanged(string name)
        {
            var handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(name));
        }

    }

    /// <summary>Keys on <see cref="HttpRequestMessage.Properties"/> for WellRyde <c>filterdata</c> (survives where AsyncLocal does not).</summary>
    internal static class WellRydeFilterDataRequestKeys
    {
        internal const string OmitJsessionFromCookie = "HiatmeWellRyde.OmitJsessionFromCookie";
        internal const string WireFilterDataUrl = "HiatmeWellRyde.WireFilterDataUrl";
    }
}
