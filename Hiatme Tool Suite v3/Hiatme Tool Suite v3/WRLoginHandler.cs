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
        /// <summary>Last successful <c>GET /portal/nu?date=…</c> URL (with optional hash). Used for Tomcat static-probe <c>Referer</c> in <see cref="TryAcquireTomcatJsessionViaNativeHandlerAsync"/> (dated SPA context). Trip <c>filterlist</c> uses bare <see cref="TripsRefererForWellRydeAjax"/> (Chrome Fiddler gold).</summary>
        private string _lastTripsNuRefererWithDate;
        /// <summary>After <see cref="TryRefreshTripsNuCsrfAsync"/> succeeds, <see cref="PostWellRydeFilterDataAsync"/> must not repeat static/native Tomcat bootstrap (same refresh already ran it). Cleared when a new refresh starts.</summary>
        private bool _skipPostFilterdataTomcatBootstrap;
        /// <summary>Tomcat id scraped from <c>;jsessionid=</c> in portal HTML/redirects when <c>Set-Cookie: JSESSIONID</c> is missing — hydrated into the cookie jar for <c>POST /portal/filterdata</c> only (Chrome never uses a matrix URL on filterdata).</summary>
        private string _jsessionIdUrlRewriteForFilterData;

        /// <summary>
        /// Next <c>POST /portal/filterdata</c> on <see cref="_filterDataClient"/> must omit <c>JSESSIONID</c> from the <c>Cookie</c> line (Tomcat id in URL only).
        /// Custom request headers are unreliable on some .NET Framework + HttpClient stacks; <see cref="AsyncLocal{T}"/> follows the logical async call into <see cref="WellRydePortalCookieInjectingHandler"/>.
        /// </summary>
        private static readonly AsyncLocal<bool> FilterDataOmitJsessionFromCookie = new AsyncLocal<bool>();

        internal static bool FilterDataOmitJsessionFromCookieActive => FilterDataOmitJsessionFromCookie.Value;

        /// <summary>
        /// Chrome portal.app: bare <c>Referer: …/portal/nu</c> for trip <c>filterlist</c>, <c>listFilterDefsJson</c>, and <c>filterdata</c> XHRs (Fiddler gold).
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
                // Must be false: trip GET …/trip/filterlist can return 302 → /portal/; default true follows and
                // returns 200 portal HTML so callers wrongly treat filterlist as primed (Fiddler still shows 302).
                AllowAutoRedirect = false,
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
                var postLoginMetaCsrf = GrabCSRFToken(response);
                var csrfMetaFromHtml = !string.IsNullOrEmpty(postLoginMetaCsrf);
                _CsrfToken = postLoginMetaCsrf ?? TryGetCsrfFromCookies();
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
                            var shellMetaCsrf = GrabCSRFToken(shellHtml);
                            if (!string.IsNullOrEmpty(shellMetaCsrf))
                                csrfMetaFromHtml = true;
                            _CsrfToken = shellMetaCsrf ?? TryGetCsrfFromCookies();
                        }
                    }
                    catch
                    {
                        // ignore; UpdateHandlerHeaders still runs with empty token
                    }
                }

                await EstablishPortalServletSessionAfterLoginAsync();
                if (WellRydeConfig.ServletCookieAutoHandlerEnabled && !PortalCookieHeaderHasJsessionId())
                    await TryServletCookiesUsingAutoHandlerDocumentGetsAsync(DateTime.Today, chromeDocumentChainAlreadyRan: true).ConfigureAwait(false);
                if (!csrfMetaFromHtml)
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
        private Task<HttpResponseMessage> GetPortalOriginDocumentAsync()
        {
            return SendFilterDataClientNavigateGetWithManualRedirectsAsync(
                WellRydeConfig.PortalOrigin + "/",
                referer: null,
                coldNavigationFirstHop: true);
        }

        /// <summary>
        /// Manual redirect chain for portal <c>GET</c> on <see cref="_filterDataClient"/> (inner <c>AllowAutoRedirect=false</c>).
        /// Merges <c>Set-Cookie</c> each hop so login matches Chrome; trip XHRs stay non-following so HTTP 302 is visible.
        /// </summary>
        private async Task<HttpResponseMessage> SendFilterDataClientNavigateGetWithManualRedirectsAsync(
            string startUrl,
            string referer,
            bool coldNavigationFirstHop,
            int maxRedirects = 16)
        {
            var url = NormalizePortalAbsoluteUrlString(startUrl) ?? startUrl;
            var refererForHop = string.IsNullOrEmpty(referer) ? WellRydeConfig.TripsPageAbsoluteUrl : referer;
            var seenNavigateKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            seenNavigateKeys.Add(CanonicalPortalNavigateUrlKey(url));
            for (var hop = 0; hop < maxRedirects; hop++)
            {
                using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    req.Headers.TryAddWithoutValidation("User-Agent", PortalBrowserUserAgent);
                    req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
                    req.Headers.TryAddWithoutValidation("Cache-Control", "max-age=0");
                    if (coldNavigationFirstHop && hop == 0)
                    {
                        req.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                        req.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "none");
                        req.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
                        req.Headers.TryAddWithoutValidation("Sec-Fetch-User", "?1");
                        req.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
                        req.Headers.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
                    }
                    else
                    {
                        req.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                        req.Headers.TryAddWithoutValidation("Referer", refererForHop);
                        req.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
                        req.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
                        req.Headers.TryAddWithoutValidation("Sec-Fetch-User", "?1");
                        req.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
                        req.Headers.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
                    }

                    var resp = await _filterDataClient.SendAsync(req).ConfigureAwait(false);
                    WellRydeCookieHelper.IngestSetCookieHeaders(resp, CookieJar);
                    WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
                    var code = (int)resp.StatusCode;
                    if (code >= 300 && code < 400 && resp.Headers.Location != null)
                    {
                        var loc = ResolveRedirectLocation(resp.RequestMessage.RequestUri, resp.Headers.Location);
                        refererForHop = url;
                        if (loc == null)
                            return resp;
                        var nextUrl = NormalizePortalAbsoluteUrlString(loc.AbsoluteUri) ?? loc.AbsoluteUri;
                        var nextKey = CanonicalPortalNavigateUrlKey(nextUrl);
                        if (!seenNavigateKeys.Add(nextKey))
                        {
                            if (WellRydeConfig.PortalLogVerbose)
                                WellRydeLog.WriteLine("WellRyde: manual navigate — stop redirect cycle (Location revisits) next=" + nextKey);
                            return resp;
                        }
                        try
                        {
                            await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                        }
                        catch
                        {
                            /* ignore */
                        }
                        resp.Dispose();
                        url = nextUrl;
                        continue;
                    }

                    return resp;
                }
            }

            using (var last = new HttpRequestMessage(HttpMethod.Get, url))
            {
                last.Headers.TryAddWithoutValidation("User-Agent", PortalBrowserUserAgent);
                last.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                last.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
                last.Headers.TryAddWithoutValidation("Referer", refererForHop);
                last.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
                last.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
                last.Headers.TryAddWithoutValidation("Sec-Fetch-User", "?1");
                last.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
                last.Headers.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
                var finalResp = await _filterDataClient.SendAsync(last).ConfigureAwait(false);
                WellRydeCookieHelper.IngestSetCookieHeaders(finalResp, CookieJar);
                WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
                return finalResp;
            }
        }

        /// <summary>
        /// After <see cref="SendFilterDataClientNavigateGetWithManualRedirectsAsync"/>, <c>GET /portal/nu?date=</c> may chain to <c>/portal/</c> with HTTP 200 —
        /// same success code but wrong document (dashboard HTML, wrong <c>_csrf</c>, no NU shell). Chrome keeps the trips SPA URL.
        /// </summary>
        private static bool PortalNavigateFinalUriLooksLikeTripsNu(HttpResponseMessage resp)
        {
            if (resp?.RequestMessage?.RequestUri == null)
                return false;
            var path = resp.RequestMessage.RequestUri.AbsolutePath ?? "";
            return path.EndsWith("/nu", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Loads <paramref name="nuDatedAbsoluteUrl"/> (trips <c>nu?date=</c>). If redirects end on a non-<c>/portal/nu</c> path with HTTP 200, replays
        /// <c>GET /portal/</c> → bare <c>/portal/nu</c> → dated nu so <c>Set-Cookie</c> and the final HTML match the billing shell — unless
        /// <paramref name="chromeDocumentChainAlreadyRan"/> (caller just ran <see cref="NavigateChromeOrderedPortalDocumentChainAsync"/>), in which case only one extra dated GET with bare-<c>nu</c> referer runs.
        /// </summary>
        private async Task<HttpResponseMessage> GetPortalHtmlDocumentForTripsNuDatedShellAsync(string nuDatedAbsoluteUrl, bool chromeDocumentChainAlreadyRan = false)
        {
            var resp = await GetPortalHtmlDocumentAsync(nuDatedAbsoluteUrl).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode || PortalNavigateFinalUriLooksLikeTripsNu(resp))
                return resp;
            try
            {
                if (WellRydeConfig.PortalLogVerbose)
                {
                    var landed = resp.RequestMessage?.RequestUri?.GetLeftPart(UriPartial.Path) ?? "?";
                    WellRydeLog.WriteLine("WellRyde: GET trips nu?date= landed on " + landed + " after redirects — replaying"
                        + (chromeDocumentChainAlreadyRan ? " nu?date= only (shell chain already ran)." : " portal/ → nu → nu?date=."));
                }
            }
            catch
            {
                /* ignore */
            }
            resp.Dispose();
            if (!chromeDocumentChainAlreadyRan)
            {
                using (var r0 = await GetPortalHtmlDocumentAsync(WellRydeConfig.PortalShellUrl, WellRydeConfig.PortalOrigin + "/").ConfigureAwait(false))
                    WellRydeCookieHelper.IngestSetCookieHeaders(r0, CookieJar);
                using (var r1 = await GetPortalHtmlDocumentAsync(WellRydeConfig.TripsPageAbsoluteUrl, WellRydeConfig.PortalShellUrl).ConfigureAwait(false))
                    WellRydeCookieHelper.IngestSetCookieHeaders(r1, CookieJar);
                WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
            }
            return await GetPortalHtmlDocumentAsync(nuDatedAbsoluteUrl, WellRydeConfig.TripsPageAbsoluteUrl).ConfigureAwait(false);
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
        private Task<HttpResponseMessage> GetPortalHtmlDocumentAsync(string requestUri, string referer = null)
        {
            return SendFilterDataClientNavigateGetWithManualRedirectsAsync(
                requestUri,
                string.IsNullOrEmpty(referer) ? WellRydeConfig.TripsPageAbsoluteUrl : referer,
                coldNavigationFirstHop: false);
        }

        /// <summary>
        /// Chrome document order on the same <see cref="_filterDataClient"/> stack as trip XHRs / <c>filterdata</c> (not <see cref="Client"/> API defaults):
        /// <c>GET /</c> cold → <c>GET /portal/</c> → <c>GET /portal/nu</c> with matching Referer and <c>Sec-Fetch-*</c>.
        /// After each response: merge <c>Set-Cookie</c>, promote Tomcat id, scrape <c>jsessionid</c> from HTML when present.
        /// </summary>
        private async Task NavigateChromeOrderedPortalDocumentChainAsync()
        {
            WellRydeCookieHelper.TryRemoveSyntheticSpringJsessionIdFromJar(CookieJar);
            var o = WellRydeConfig.PortalOrigin;
            using (var r0 = await GetPortalOriginDocumentAsync().ConfigureAwait(false))
            {
                WellRydeCookieHelper.IngestSetCookieHeaders(r0, CookieJar);
                WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
                if (r0.IsSuccessStatusCode)
                {
                    var h0 = await r0.Content.ReadAsStringAsync().ConfigureAwait(false);
                    WellRydeCookieHelper.TryIngestJSessionIdFromMarkupAndLocation(CookieJar, h0);
                    var rw0 = WellRydeCookieHelper.TryGetFirstJsessionIdFromTextForUrlRewrite(CookieJar, h0);
                    if (!string.IsNullOrEmpty(rw0))
                        _jsessionIdUrlRewriteForFilterData = rw0;
                }
            }
            using (var r1 = await GetPortalHtmlDocumentAsync(WellRydeConfig.PortalShellUrl, o + "/").ConfigureAwait(false))
            {
                WellRydeCookieHelper.IngestSetCookieHeaders(r1, CookieJar);
                WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
                if (r1.IsSuccessStatusCode)
                {
                    var h1 = await r1.Content.ReadAsStringAsync().ConfigureAwait(false);
                    WellRydeCookieHelper.TryIngestJSessionIdFromMarkupAndLocation(CookieJar, h1);
                    var rw1 = WellRydeCookieHelper.TryGetFirstJsessionIdFromTextForUrlRewrite(CookieJar, h1);
                    if (!string.IsNullOrEmpty(rw1))
                        _jsessionIdUrlRewriteForFilterData = rw1;
                }
            }
            using (var r2 = await GetPortalHtmlDocumentAsync(WellRydeConfig.TripsPageAbsoluteUrl, WellRydeConfig.PortalShellUrl).ConfigureAwait(false))
            {
                WellRydeCookieHelper.IngestSetCookieHeaders(r2, CookieJar);
                WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
                if (r2.IsSuccessStatusCode)
                {
                    var h2 = await r2.Content.ReadAsStringAsync().ConfigureAwait(false);
                    WellRydeCookieHelper.TryIngestJSessionIdFromMarkupAndLocation(CookieJar, h2);
                    var rw2 = WellRydeCookieHelper.TryGetFirstJsessionIdFromTextForUrlRewrite(CookieJar, h2);
                    if (!string.IsNullOrEmpty(rw2))
                        _jsessionIdUrlRewriteForFilterData = rw2;
                }
            }
            WellRydeCookieHelper.CollapseDuplicatePortalCookies(CookieJar);
        }

        /// <summary>
        /// Chrome <c>GET /portal/listFilterDefsJson?listDefId=…&amp;customListDefId=&amp;userDefaultFilter=true</c> runs immediately before <c>POST /portal/filterdata</c>.
        /// Primes list/session state so <c>filterdata</c> returns JSON instead of the NU HTML shell.
        /// <c>Referer</c> bare <c>/portal/nu</c> (Chrome). Do not add CSRF headers on this GET — some Spring stacks reject or 500 when XHR CSRF headers are sent on GET.
        /// When <c>Skipped</c> is <c>true</c>, list id was unset. Otherwise <c>JsonMedia</c> is <c>true</c> only for HTTP 2xx with a JSON content-type (coherent prime for ELB + servlet).
        /// </summary>
        private async Task<(int StatusCode, bool Skipped, bool JsonMedia)> TryPrimeListFilterDefsJsonBeforeFilterDataAsync()
        {
            var listId = WellRydeConfig.TripFilterListDefId ?? "";
            if (string.IsNullOrWhiteSpace(listId) || !listId.StartsWith("SEC-", StringComparison.Ordinal))
                return (-1, true, false);
            var url = WellRydeConfig.BuildListFilterDefsJsonUrl(listId);
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    req.Headers.TryAddWithoutValidation("Accept", "application/json, text/javascript, */*; q=0.01");
                    req.Headers.TryAddWithoutValidation("User-Agent", PortalBrowserUserAgent);
                    req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
                    req.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate");
                    req.Headers.TryAddWithoutValidation("Referer", TripsRefererForWellRydeAjax);
                    req.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
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
                        var ctPrime = resp.Content.Headers.ContentType?.MediaType ?? "";
                        var code = (int)resp.StatusCode;
                        string body = null;
                        if (code >= 200 && code < 300 && resp.Content != null)
                        {
                            try
                            {
                                body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                            }
                            catch
                            {
                                /* ignore */
                            }
                        }

                        var jsonMedia = code >= 200 && code < 300
                            && ctPrime.IndexOf("json", StringComparison.OrdinalIgnoreCase) >= 0
                            && !string.IsNullOrEmpty(body)
                            && !WellRydeTripParsing.LooksLikeNonJsonPayload(body)
                            && !WellRydeTripParsing.PortalHtmlBodyLooksLikeEnterpriseInternalErrorPage(body);
                        if (WellRydeConfig.PortalLogVerbose)
                            WellRydeLog.WriteLine("WellRyde: listFilterDefsJson prime HTTP " + code + " content-type=" + ctPrime + " listDefId=" + listId);
                        if (!resp.IsSuccessStatusCode)
                        {
                            WellRydeLog.WriteLine("WellRyde: listFilterDefsJson prime failed HTTP " + code + " listDefId=" + listId);
                        }
                        else if (WellRydeTripParsing.PortalHtmlBodyLooksLikeEnterpriseInternalErrorPage(body ?? ""))
                        {
                            WellRydeLog.WriteLine("WellRyde: listFilterDefsJson HTTP " + code + " but body is vendor internal-error HTML shell (treat as failed prime) listDefId=" + listId);
                        }
                        else if (ctPrime.IndexOf("json", StringComparison.OrdinalIgnoreCase) < 0 || !jsonMedia)
                        {
                            WellRydeLog.WriteLine("WellRyde: listFilterDefsJson expected JSON but got content-type=" + (string.IsNullOrEmpty(ctPrime) ? "(none)" : ctPrime)
                                + " len=" + (body?.Length.ToString() ?? "?") + " listDefId=" + listId
                                + " prefix=" + TrimForWellRydeLog(body ?? "", 180));
                        }
                        return (code, false, jsonMedia);
                    }
                }
            }
            catch (Exception ex)
            {
                WellRydeLog.WriteLine("WellRyde: listFilterDefsJson prime error: " + ex.Message);
                return (0, false, false);
            }
        }

        /// <summary>
        /// When <see cref="WellRydeConfig.ChromeShellPrimingEnabled"/>: light document GETs before static <c>JSESSIONID</c> mint / <c>trip/filterlist</c> (<c>currentPage</c>, AVL chain only).
        /// Omitted: root <c>/nps/timezone</c> (404 + ALB churn), insurance / ToU (vendor often returns <b>HTTP 200</b> HTML “Internal Error” shells), <c>/portal/gpsagent</c> (long-lived / 502).
        /// Internal-error HTML is never used to scrape <c>JSESSIONID</c> from markup. Non-fatal: failures are ignored.
        /// </summary>
        private async Task TryPrimeChromeNuShellBeforeTripFilterListAsync(string datedNuReferer)
        {
            if (!WellRydeConfig.ChromeShellPrimingEnabled)
                return;
            var refNu = string.IsNullOrEmpty(datedNuReferer) ? WellRydeConfig.TripsPageAbsoluteUrl : datedNuReferer;
            var steps = new (string Url, string Referer)[]
            {
                (WellRydeConfig.PortalCurrentPageNuUrl, refNu),
                (WellRydeConfig.AvlDoubleSlashEntryUrl, refNu),
                (WellRydeConfig.AvlInitializeDriverStopsUrl, refNu),
                (WellRydeConfig.AvlInitiateDefaultUrl, refNu),
            };
            try
            {
                foreach (var step in steps)
                {
                    using (var r = await GetPortalHtmlDocumentAsync(step.Url, step.Referer).ConfigureAwait(false))
                    {
                        WellRydeCookieHelper.IngestSetCookieHeaders(r, CookieJar);
                        string body = null;
                        try
                        {
                            body = await r.Content.ReadAsStringAsync().ConfigureAwait(false);
                        }
                        catch
                        {
                            /* ignore */
                        }
                        if (!string.IsNullOrEmpty(body) && WellRydeTripParsing.PortalHtmlBodyLooksLikeEnterpriseInternalErrorPage(body))
                        {
                            if (WellRydeConfig.PortalLogVerbose)
                                WellRydeLog.WriteLine("WellRyde: Chrome shell priming — skip servlet scrape (HTTP " + (int)r.StatusCode
                                    + " internal-error HTML shell) url=" + step.Url);
                        }
                        else if (!string.IsNullOrEmpty(body))
                        {
                            WellRydeCookieHelper.TryIngestJSessionIdFromMarkupAndLocation(CookieJar, body);
                            var rw = WellRydeCookieHelper.TryGetFirstJsessionIdFromTextForUrlRewrite(CookieJar, body);
                            if (!string.IsNullOrEmpty(rw))
                                _jsessionIdUrlRewriteForFilterData = rw;
                        }
                        WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
                    }
                }
            }
            catch (Exception ex)
            {
                if (WellRydeConfig.PortalLogVerbose)
                    WellRydeLog.WriteLine("WellRyde: Chrome shell priming (non-fatal): " + ex.Message);
            }
            WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
        }

        /// <summary>
        /// POST <c>/portal/filterdata</c>.
        /// Chrome XHR sends <c>Sec-Fetch-*</c> on same-origin POST; include them here (filter runs on <see cref="_filterDataClient"/> without API defaults).
        /// CSRF: form <c>_csrf</c> is always sent. Optional <c>X-CSRF-TOKEN</c> / <c>X-XSRF-TOKEN</c> only when <see cref="WellRydeConfig.FilterDataPhpStyleHeaders"/> — forcing them on every POST has been linked to HTTP 500 on some portal builds (Chrome HAR often omits them on <c>filterdata</c>).
        /// <paramref name="referer"/>: when null, uses bare <see cref="TripsRefererForWellRydeAjax"/> (Chrome <c>filterdata</c> / Fiddler gold), not <c>nu?date=…</c>.
        /// Uses <see cref="_filterDataClient"/> (<see cref="WellRydePortalCookieInjectingHandler"/> + jar) so <c>Cookie</c> matches Chrome order for <c>filterdata</c> without doubling ALB lines.
        /// When <c>listFilterDefsJson</c> fails (e.g. HTTP 401) or is non-JSON, ELB may have moved to a new node while <c>JSESSIONID</c> was minted earlier — expire Tomcat cookie, static-probe again, then retry <c>listFilterDefsJson</c> once before <c>filterdata</c>. POST URL is always plain <c>/portal/filterdata</c> (Chrome); scraped Tomcat tokens hydrate the <c>JSESSIONID</c> cookie only.
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
            // TryRefreshTripsNuCsrfAsync already ran static probes + TryAcquireTomcatJsessionViaNativeHandlerAsync on success.
            if (!PortalCookieHeaderHasJsessionId() && !_skipPostFilterdataTomcatBootstrap)
            {
                await TryPrimeJsessionIdViaPortalStaticResourceAsync(refererVal).ConfigureAwait(false);
                if (!PortalCookieHeaderHasJsessionId())
                    await TryAcquireTomcatJsessionViaNativeHandlerAsync(refererVal, skipChromeDocumentTriplet: _skipPostFilterdataTomcatBootstrap).ConfigureAwait(false);
            }
            WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
            var listPrime = await TryPrimeListFilterDefsJsonBeforeFilterDataAsync().ConfigureAwait(false);
            // Chrome Fiddler: listFilterDefsJson can return 401 and Set-Cookie a new AWSALB while JSESSIONID came from a prior
            // static 404 on the old node — filterdata then returns HTML / 500. Re-mint + re-prime list when the first GET failed or was not JSON.
            // Must not be gated on _skipPostFilterdataTomcatBootstrap (that only skips the initial static/native block above after TryRefresh).
            if (!listPrime.Skipped
                && (!listPrime.JsonMedia || listPrime.StatusCode < 200 || listPrime.StatusCode >= 300))
            {
                await TryRecoverTomcatStickyAfterElbJsessionMismatchAsync(refererVal).ConfigureAwait(false);
                await TryPrimeListFilterDefsJsonBeforeFilterDataAsync().ConfigureAwait(false);
            }
            // Required order (plan wellryde_fiddler_vs_app): Chrome POST is always …/portal/filterdata — never …/filterdata;jsessionid=…
            if (!PortalCookieHeaderHasJsessionId()
                && !string.IsNullOrEmpty(_jsessionIdUrlRewriteForFilterData)
                && !WellRydeCookieHelper.IsJsessionTokenSpringUuidHex(CookieJar, _jsessionIdUrlRewriteForFilterData))
            {
                WellRydeCookieHelper.TryAddPortalJSessionIdCookie(CookieJar, _jsessionIdUrlRewriteForFilterData);
                WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
                if (WellRydeConfig.PortalLogVerbose)
                    WellRydeLog.WriteLine("WellRyde: filterdata — set JSESSIONID cookie from scraped rewrite token (wire URL stays plain /portal/filterdata).");
                _jsessionIdUrlRewriteForFilterData = null;
            }
            var postUri = WellRydeConfig.FilterDataUrl;
            // WRTripDownloader form _csrf always uses _CsrfToken — curl + XHR headers must use the same value (cookie can be stale after nu?date= refresh).
            var csrfForAjax = !string.IsNullOrEmpty(_CsrfToken) ? _CsrfToken : TryGetCsrfFromCookies();

            var bodyBytes = await formContent.ReadAsByteArrayAsync().ConfigureAwait(false);
            var contentTypeForBody = formContent.Headers.ContentType != null
                ? formContent.Headers.ContentType.ToString()
                : "application/x-www-form-urlencoded; charset=utf-8";

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
                        if (WellRydeConfig.PortalLogVerbose)
                            WellRydeLog.WriteLine("WellRyde: filterdata via curl — JSON " + (curlRes.Body?.Length ?? 0) + " bytes");
                        return BuildSyntheticFilterDataResponseFromCurl(curlRes);
                    }
                }
                else if (curlRes != null && WellRydeConfig.PortalLogVerbose)
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
                // Chrome / plan: POST URI is plain /portal/filterdata only (no Tomcat ;jsessionid= matrix segment).
                var requestUri = new Uri(uri ?? WellRydeConfig.FilterDataUrl, UriKind.Absolute);
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

            var resp = await SendFilterDataOnceAsync(postUri).ConfigureAwait(false);

            if ((int)resp.StatusCode == 200)
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
                        var plainRetry = WellRydeConfig.FilterDataUrl;
                        if (WellRydeConfig.PortalLogVerbose)
                            WellRydeLog.WriteLine("WellRyde: filterdata NU HTML shell — curl recovery on plain POST /portal/filterdata (omit JSESSIONID vs full cookie).");
                        else if (!WellRydeConfig.PortalLogQuiet)
                            WellRydeLog.WriteLine("WellRyde: filterdata NU HTML shell — attempting curl recovery (set WellRydePortalLogLevel=Verbose for A/B detail).");
                        var curlPathShell = WellRydeCurlFilterData.TryResolveCurlPath();
                        if (string.IsNullOrEmpty(curlPathShell))
                        {
                            WellRydeLog.WriteLine("WellRyde: filterdata shell retry — curl.exe not found (checked Sysnative/System32/SysWOW64, Git, PATH). Install Windows 10+ curl or Git for Windows.");
                        }
                        else
                        {
                            if (WellRydeConfig.PortalLogVerbose)
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
                                if (WellRydeConfig.PortalLogVerbose)
                                {
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
                                }
                                else if (r == null)
                                    WellRydeLog.WriteLine("WellRyde: filterdata shell curl " + attemptTag + " — failed.");
                                return null;
                            }
                            var curlOk = tryShellCurl(true, plainRetry, "A (omit JSESSIONID cookie, plain URL)")
                                ?? tryShellCurl(false, plainRetry, "B (full cookie, plain URL)");
                            if (curlOk != null)
                                return BuildSyntheticFilterDataResponseFromCurl(curlOk);
                            WellRydeLog.WriteLine("WellRyde: filterdata shell retry — all curl attempts non-JSON; falling back to HttpClient.");
                        }
                        resp = await SendFilterDataOnceAsync(plainRetry, omitJsessionIdFromCookie: true).ConfigureAwait(false);
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
            // UTF-8 BOM (EF BB BF) → U+FEFF; without stripping, LooksLikeNonJsonPayload misses "<?xml" and we skip the plain-URL shell retry.
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

        /// <summary>When <c>POST /portal/filterdata</c> returns the SPA HTML shell, scrape Tomcat id for cookie hydration (next POST stays plain <c>/portal/filterdata</c>).</summary>
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
                _skipPostFilterdataTomcatBootstrap = false;
                // Same stack and visit order as Chrome before nu?date=: cold / → /portal/ → bare /portal/nu (then dated GET below).
                await NavigateChromeOrderedPortalDocumentChainAsync().ConfigureAwait(false);
                if (WellRydeConfig.ServletCookieAutoHandlerEnabled && !PortalCookieHeaderHasJsessionId())
                    await TryServletCookiesUsingAutoHandlerDocumentGetsAsync(tripDate, chromeDocumentChainAlreadyRan: true).ConfigureAwait(false);

                var q = tripDate.ToString("yyyy-MM-dd");
                var url = WellRydeConfig.TripsPageAbsoluteUrl + "?date=" + Uri.EscapeDataString(q);
                var hashFrag = WellRydeConfig.TripsNuRefererHashFragment;
                if (!string.IsNullOrEmpty(hashFrag))
                {
                    if (!hashFrag.StartsWith("#", StringComparison.Ordinal))
                        hashFrag = "#" + hashFrag.TrimStart('/');
                    url += hashFrag;
                }
                using (var resp = await GetPortalHtmlDocumentForTripsNuDatedShellAsync(url, chromeDocumentChainAlreadyRan: true))
                {
                    WellRydeCookieHelper.IngestSetCookieHeaders(resp, CookieJar);
                    if (WellRydeConfig.PortalLogVerbose)
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
                    if (!PortalNavigateFinalUriLooksLikeTripsNu(resp))
                    {
                        WellRydeLog.WriteLine("WellRyde: GET trips nu?date= still not on /portal/nu after shell replay (final " + (resp.RequestMessage?.RequestUri?.GetLeftPart(UriPartial.Path) ?? "?") + ") — session expired or blocked; log in again.");
                        return false;
                    }
                    var html = await resp.Content.ReadAsStringAsync();
                    WellRydeCookieHelper.TryIngestJSessionIdFromMarkupAndLocation(CookieJar, html);
                    var rwDated = WellRydeCookieHelper.TryGetFirstJsessionIdFromTextForUrlRewrite(CookieJar, html);
                    if (!string.IsNullOrEmpty(rwDated))
                        _jsessionIdUrlRewriteForFilterData = rwDated;
                    WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
                    var metaCsrf = GrabCSRFToken(html);
                    var token = metaCsrf ?? TryGetCsrfFromCookies();
                    if (string.IsNullOrEmpty(token))
                    {
                        var hint = html != null && html.IndexOf("login", StringComparison.OrdinalIgnoreCase) >= 0
                            ? " (page looks like login — log in again)"
                            : "";
                        WellRydeLog.WriteLine("WellRyde: no _csrf from nu?date page (html length=" + (html?.Length ?? 0) + ")" + hint);
                        return false;
                    }
                    _CsrfToken = token;
                    PreferPortalXsrfCookieUnlessFreshHtmlMeta(metaCsrf);
                    SyncXsrfTokenCookieFromCurrentCsrf();
                    UpdateHandlerHeaders();
                    _lastTripsNuRefererWithDate = url;
                    // Chrome POSTs nps/timezone after loading nu?date= (not only when JSESSIONID was missing). Skipping it when the cookie
                    // already exists left trip filterlist/filterdata on HTML shells — servlet chain expects this XHR for VTripBilling.
                    await PostAuthenticatedNpsTimezoneServletPrimeAsync(url).ConfigureAwait(false);
                    WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
                    WellRydeCookieHelper.CollapseDuplicatePortalCookies(CookieJar);
                    // Optional Chrome-ish document GETs before static JSESSIONID mint so Tomcat id is tied to the ALB node after redirect churn (when priming is enabled).
                    await TryPrimeChromeNuShellBeforeTripFilterListAsync(url).ConfigureAwait(false);
                    WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
                    // Chrome loads nu?date= shell assets (CSS/JS/.map) before trip XHRs; Tomcat often mints JSESSIONID there — do not hit filterlist without it.
                    if (!PortalCookieHeaderHasJsessionId())
                    {
                        var runCurl = WellRydeConfig.FilterDataUseCurl && !string.IsNullOrEmpty(WellRydeCurlFilterData.TryResolveCurlPath());
                        await TryPrimeJsessionStaticRelativePathsAsync(url, JsessionStaticProbePaths, runCurlFallback: runCurl).ConfigureAwait(false);
                        WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
                    }
                    await TryResolveVtTripBillingListDefIfNeededAsync();
                    // Explicit SEC-S_* already hit filterlist inside TryResolve — avoid doubling the same XHRs.
                    var listDef = WellRydeConfig.TripFilterListDefId ?? "";
                    var skipDuplicateFilterlistPrime = WellRydeConfig.HasExplicitTripListDefId
                        && listDef.StartsWith("SEC-S_", StringComparison.Ordinal);
                    if (!PortalCookieHeaderHasJsessionId() && !skipDuplicateFilterlistPrime)
                        await TryPrimeTripFilterListServletSessionAsync();
                    WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
                    if (!PortalCookieHeaderHasJsessionId())
                        await TryPrimeTripFilterListAsDocumentNavigateAsync();
                    WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
                    if (!PortalCookieHeaderHasJsessionId())
                        await EnsurePortalServletSessionFromShellDocumentGetAsync(url);
                    WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
                    if (!PortalCookieHeaderHasJsessionId())
                        await TryCaptureServletCookiesViaManualRedirectChainAsync(WellRydeConfig.TripsPageAbsoluteUrl, url);
                    WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
                    // Static probes also run at end of TryAcquireTomcatJsessionViaNativeHandlerAsync — skip duplicate GETs here.
                    if (WellRydeConfig.FilterDataUseCurl && !string.IsNullOrEmpty(WellRydeCurlFilterData.TryResolveCurlPath()) && !PortalCookieHeaderHasJsessionId())
                    {
                        WellRydeCurlFilterData.TrySessionPrimingGet(CookieJar, url, url, PortalBrowserUserAgent);
                        WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
                    }
                    if (!PortalCookieHeaderHasJsessionId())
                        await TryAcquireTomcatJsessionViaNativeHandlerAsync(url, skipChromeDocumentTriplet: true).ConfigureAwait(false);
                    WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
                    _skipPostFilterdataTomcatBootstrap = true;
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
        /// Used when <c>filterdata</c> already had a portal session but returned HTML / errors — runs the same Chrome document chain as <see cref="TryRefreshTripsNuCsrfAsync"/> (cold <c>/</c> → <c>/portal/</c> → bare <c>nu</c>), then <c>nu?date=</c>, CSRF, timezone, and static <c>JSESSIONID</c> probes only (no <see cref="TryAcquireTomcatJsessionViaNativeHandlerAsync"/>).
        /// </summary>
        public async Task<bool> TryLightRefreshTripsNuAfterFilterDataFailureAsync(DateTime tripDate)
        {
            try
            {
                _skipPostFilterdataTomcatBootstrap = false;
                await NavigateChromeOrderedPortalDocumentChainAsync().ConfigureAwait(false);
                var q = tripDate.ToString("yyyy-MM-dd");
                var url = WellRydeConfig.TripsPageAbsoluteUrl + "?date=" + Uri.EscapeDataString(q);
                var hashFrag = WellRydeConfig.TripsNuRefererHashFragment;
                if (!string.IsNullOrEmpty(hashFrag))
                {
                    if (!hashFrag.StartsWith("#", StringComparison.Ordinal))
                        hashFrag = "#" + hashFrag.TrimStart('/');
                    url += hashFrag;
                }
                using (var resp = await GetPortalHtmlDocumentForTripsNuDatedShellAsync(url, chromeDocumentChainAlreadyRan: true))
                {
                    WellRydeCookieHelper.IngestSetCookieHeaders(resp, CookieJar);
                    if (!resp.IsSuccessStatusCode)
                    {
                        WellRydeLog.WriteLine("WellRyde: light session refresh — GET nu?date= HTTP " + (int)resp.StatusCode);
                        return false;
                    }
                    if (!PortalNavigateFinalUriLooksLikeTripsNu(resp))
                    {
                        WellRydeLog.WriteLine("WellRyde: light session refresh — final URL is not /portal/nu after replay (got " + (resp.RequestMessage?.RequestUri?.GetLeftPart(UriPartial.Path) ?? "?") + ").");
                        return false;
                    }
                    var html = await resp.Content.ReadAsStringAsync();
                    WellRydeCookieHelper.TryIngestJSessionIdFromMarkupAndLocation(CookieJar, html);
                    var rwDated = WellRydeCookieHelper.TryGetFirstJsessionIdFromTextForUrlRewrite(CookieJar, html);
                    if (!string.IsNullOrEmpty(rwDated))
                        _jsessionIdUrlRewriteForFilterData = rwDated;
                    WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
                    var metaCsrfLight = GrabCSRFToken(html);
                    var token = metaCsrfLight ?? TryGetCsrfFromCookies();
                    if (string.IsNullOrEmpty(token))
                    {
                        WellRydeLog.WriteLine("WellRyde: light session refresh — no _csrf from nu?date page.");
                        return false;
                    }
                    _CsrfToken = token;
                    PreferPortalXsrfCookieUnlessFreshHtmlMeta(metaCsrfLight);
                    SyncXsrfTokenCookieFromCurrentCsrf();
                    UpdateHandlerHeaders();
                    _lastTripsNuRefererWithDate = url;
                    await PostAuthenticatedNpsTimezoneServletPrimeAsync(url).ConfigureAwait(false);
                    WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
                    WellRydeCookieHelper.CollapseDuplicatePortalCookies(CookieJar);
                    await TryPrimeChromeNuShellBeforeTripFilterListAsync(url).ConfigureAwait(false);
                    WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
                    if (!PortalCookieHeaderHasJsessionId())
                    {
                        var runCurl = WellRydeConfig.FilterDataUseCurl && !string.IsNullOrEmpty(WellRydeCurlFilterData.TryResolveCurlPath());
                        await TryPrimeJsessionStaticRelativePathsAsync(url, JsessionStaticProbePaths, runCurlFallback: runCurl).ConfigureAwait(false);
                        WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
                    }
                    await TryResolveVtTripBillingListDefIfNeededAsync();
                    if (!PortalCookieHeaderHasJsessionId())
                    {
                        var runCurl2 = WellRydeConfig.FilterDataUseCurl && !string.IsNullOrEmpty(WellRydeCurlFilterData.TryResolveCurlPath());
                        await TryPrimeJsessionStaticRelativePathsAsync(url, JsessionStaticProbePathsStickyResync, runCurlFallback: runCurl2).ConfigureAwait(false);
                    }
                    WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
                    _skipPostFilterdataTomcatBootstrap = true;
                    return true;
                }
            }
            catch (Exception ex)
            {
                WellRydeLog.WriteLine("WellRyde: TryLightRefreshTripsNuAfterFilterDataFailureAsync error: " + ex.Message);
            }
            return false;
        }

        /// <summary>
        /// Shared <see cref="CookieJar"/> with <c>HttpClientHandler.UseCookies=true</c> so redirect <c>Set-Cookie</c> (Tomcat <c>JSESSIONID</c>) merges like PHP libcurl — complements manual <see cref="WellRydeCookieHelper.IngestSetCookieHeaders"/>.
        /// </summary>
        private async Task TryServletCookiesUsingAutoHandlerDocumentGetsAsync(DateTime tripDate, bool chromeDocumentChainAlreadyRan = false)
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
                    AllowAutoRedirect = false,
                })
                using (var http = new HttpClient(handler, disposeHandler: true))
                {
                    http.Timeout = Client.Timeout;
                    async Task RunGet(string url, string referer, string secFetchSite)
                    {
                        var cold = string.IsNullOrEmpty(referer) && string.Equals(secFetchSite, "none", StringComparison.OrdinalIgnoreCase);
                        await HttpClientPortalDocumentGetWithManualRedirectsAsync(http, url, referer, secFetchSite, cold).ConfigureAwait(false);
                    }

                    var o = WellRydeConfig.PortalOrigin;
                    if (!chromeDocumentChainAlreadyRan)
                    {
                        await RunGet(o + "/", null, "none").ConfigureAwait(false);
                        if (PortalCookieHeaderHasJsessionId())
                            return;
                        await RunGet(WellRydeConfig.PortalShellUrl, o + "/", "same-origin").ConfigureAwait(false);
                        if (PortalCookieHeaderHasJsessionId())
                            return;
                        await RunGet(WellRydeConfig.TripsPageAbsoluteUrl, WellRydeConfig.PortalShellUrl, "same-origin").ConfigureAwait(false);
                        if (PortalCookieHeaderHasJsessionId())
                            return;
                    }
                    var q = tripDate.ToString("yyyy-MM-dd");
                    await RunGet(WellRydeConfig.TripsPageAbsoluteUrl + "?date=" + Uri.EscapeDataString(q),
                        WellRydeConfig.TripsPageAbsoluteUrl, "same-origin").ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                if (WellRydeConfig.PortalLogVerbose)
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
                // Do not call PreferPortalXsrfCookie here — login flow may have set _CsrfToken from HTML; stale XSRF-TOKEN cookie must not overwrite it before nps/timezone.
                SyncXsrfTokenCookieFromCurrentCsrf();
                await NavigateChromeOrderedPortalDocumentChainAsync().ConfigureAwait(false);
                if (!PortalCookieHeaderHasJsessionId())
                    await PostAuthenticatedNpsTimezoneServletPrimeAsync(WellRydeConfig.TripsPageAbsoluteUrl).ConfigureAwait(false);
                WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
                // Some stacks only attach Set-Cookie: JSESSIONID on an intermediate 302; default AllowAutoRedirect can drop it from our manual ingest.
                await TryCaptureServletCookiesViaManualRedirectChainAsync(WellRydeConfig.TripsPageAbsoluteUrl, WellRydeConfig.PortalShellUrl);
                if (!PortalCookieHeaderHasJsessionId())
                    await TryPrimeJsessionIdViaPortalStaticResourceAsync(WellRydeConfig.TripsPageAbsoluteUrl);
                if (!PortalCookieHeaderHasJsessionId())
                    await TryAcquireTomcatJsessionViaNativeHandlerAsync(WellRydeConfig.TripsPageAbsoluteUrl, skipChromeDocumentTriplet: true).ConfigureAwait(false);
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
            if (PortalCookieHeaderHasJsessionId())
                return;
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
                    var seenChainKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    for (var hop = 0; hop < 12; hop++)
                    {
                        var hopKey = CanonicalPortalNavigateUrlKey(url);
                        if (!seenChainKeys.Add(hopKey))
                        {
                            if (WellRydeConfig.PortalLogVerbose)
                                WellRydeLog.WriteLine("WellRyde: servlet redirect chain — stop cycle key=" + hopKey);
                            break;
                        }
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
                                    var nextUrl = loc.ToString();
                                    var nextKey = CanonicalPortalNavigateUrlKey(nextUrl);
                                    if (seenChainKeys.Contains(nextKey))
                                    {
                                        if (WellRydeConfig.PortalLogVerbose)
                                            WellRydeLog.WriteLine("WellRyde: servlet redirect chain — stop (Location repeats) next=" + nextKey);
                                        break;
                                    }
                                    chainReferer = url;
                                    url = nextUrl;
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
        /// When <paramref name="skipChromeDocumentTriplet"/> is <c>true</c>, skips <c>GET /</c>, <c>/portal/</c>, and bare <c>/portal/nu</c> (caller already ran <see cref="NavigateChromeOrderedPortalDocumentChainAsync"/>).
        /// </summary>
        private async Task TryAcquireTomcatJsessionViaNativeHandlerAsync(string datedOrBareNuReferer, bool skipChromeDocumentTriplet = false)
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
                    AllowAutoRedirect = false,
                })
                using (var http = new HttpClient(handler, disposeHandler: true))
                {
                    http.Timeout = Client.Timeout;

                    async Task NavigateGet(string requestUrl, string referer, string secFetchSite)
                    {
                        var cold = string.IsNullOrEmpty(referer) && string.Equals(secFetchSite, "none", StringComparison.OrdinalIgnoreCase);
                        await HttpClientPortalDocumentGetWithManualRedirectsAsync(http, requestUrl, referer, secFetchSite, cold).ConfigureAwait(false);
                    }

                    var o = WellRydeConfig.PortalOrigin;
                    if (!skipChromeDocumentTriplet)
                    {
                        await NavigateGet(o + "/", null, "none").ConfigureAwait(false);
                        if (PortalCookieHeaderHasJsessionId()) return;
                        await NavigateGet(WellRydeConfig.PortalShellUrl, o + "/", "same-origin").ConfigureAwait(false);
                        if (PortalCookieHeaderHasJsessionId()) return;
                        await NavigateGet(WellRydeConfig.TripsPageAbsoluteUrl, WellRydeConfig.PortalShellUrl, "same-origin").ConfigureAwait(false);
                        if (PortalCookieHeaderHasJsessionId()) return;
                    }
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
                            var isSourceMap = path.EndsWith(".map", StringComparison.OrdinalIgnoreCase);
                            var isCss = !isSourceMap && path.EndsWith(".css", StringComparison.OrdinalIgnoreCase);
                            var dest = isCss ? "style" : (isSourceMap ? "empty" : "script");
                            await HttpClientNoCorsGetWithManualRedirectsAsync(http, o + path, xhrReferer, isCss ? "text/css,*/*;q=0.1" : "*/*", dest).ConfigureAwait(false);
                        }
                        catch
                        {
                            /* next */
                        }
                    }
                }
                WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
                if (WellRydeConfig.PortalLogVerbose && PortalCookieHeaderHasJsessionId())
                    WellRydeLog.WriteLine("WellRyde: native cookie-handler chain stored JSESSIONID for filterdata.");
            }
            catch (Exception ex)
            {
                if (WellRydeConfig.PortalLogVerbose)
                    WellRydeLog.WriteLine("WellRyde: TryAcquireTomcatJsessionViaNativeHandlerAsync: " + ex.Message);
            }
        }

        /// <summary>Chrome post-<c>/portal/nu</c> waterfall (Fiddler): CSS map right after bootstrap bundle, then shell CSS/JS, then source maps after their scripts, then legacy font.</summary>
        private static readonly string[] JsessionStaticProbePaths =
        {
            "/portal/resources/bootstrap_3.3.6/css/bootstrap.min.css.map",
            "/portal/resources/styles/commonStyle.css",
            "/portal/resources/bootstrap_3.3.6/css/bootstrap.min.css",
            "/portal/resources/bootstrap_3.3.6/js/bootstrap.min.js",
            "/portal/resources/inspinia/js/jquery/jquery-2.1.1.min.js",
            "/portal/resources/bootstrap_3.2/js/bootstrap-tagsinput.min.js.map",
            "/portal/resources/scripts/mdm/avl/SlidingMarker.min.js.map",
            "/portal/resources/styles/fonts/fontawesome-webfont.eot?v=4.7.0",
        };

        /// <summary>Short sequence after ELB rotation: one early <c>.css.map</c> + shell CSS + font (avoids re-walking the full list).</summary>
        private static readonly string[] JsessionStaticProbePathsStickyResync =
        {
            "/portal/resources/bootstrap_3.3.6/css/bootstrap.min.css.map",
            "/portal/resources/styles/commonStyle.css",
            "/portal/resources/styles/fonts/fontawesome-webfont.eot?v=4.7.0",
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
            try
            {
                await TryPrimeJsessionStaticRelativePathsAsync(refererVal, JsessionStaticProbePaths, runCurlFallback: true).ConfigureAwait(false);
            }
            catch
            {
                /* non-fatal */
            }
        }

        /// <summary>GET each relative path in order until <c>JSESSIONID</c> appears in the jar (same order as Chrome shell).</summary>
        private async Task TryPrimeJsessionStaticRelativePathsAsync(string refererVal, string[] relativePaths, bool runCurlFallback)
        {
            if (relativePaths == null || relativePaths.Length == 0)
                return;
            var o = WellRydeConfig.PortalOrigin;
            foreach (var path in relativePaths)
            {
                if (PortalCookieHeaderHasJsessionId())
                    return;
                try
                {
                    var isSourceMap = path.EndsWith(".map", StringComparison.OrdinalIgnoreCase);
                    var isCss = !isSourceMap && path.EndsWith(".css", StringComparison.OrdinalIgnoreCase);
                    var isFont = path.IndexOf(".eot", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (isCss)
                        await TryOneStaticJsessionProbeAsync(refererVal, o + path, "text/css,*/*;q=0.1", "style").ConfigureAwait(false);
                    else if (isFont)
                        await TryOneStaticJsessionProbeAsync(refererVal, o + path, "*/*", "font", "cors", true).ConfigureAwait(false);
                    else if (isSourceMap)
                        await TryOneStaticJsessionProbeAsync(refererVal, o + path, "*/*", "empty").ConfigureAwait(false);
                    else
                        await TryOneStaticJsessionProbeAsync(refererVal, o + path, "*/*", "script").ConfigureAwait(false);
                }
                catch
                {
                    /* try next */
                }
            }

            if (!runCurlFallback || PortalCookieHeaderHasJsessionId())
                return;
            var curl = WellRydeCurlFilterData.TryResolveCurlPath();
            if (string.IsNullOrEmpty(curl))
                return;
            foreach (var path in relativePaths)
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

        private async Task TryOneStaticJsessionProbeAsync(string refererVal, string url, string accept, string secFetchDest, string secFetchMode = "no-cors", bool sendOrigin = false)
        {
            using (var req = new HttpRequestMessage(HttpMethod.Get, url))
            {
                req.Headers.TryAddWithoutValidation("User-Agent", PortalBrowserUserAgent);
                req.Headers.TryAddWithoutValidation("Accept", accept);
                req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
                req.Headers.TryAddWithoutValidation("Referer", refererVal);
                req.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
                req.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", secFetchMode);
                req.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", secFetchDest);
                if (sendOrigin)
                    req.Headers.TryAddWithoutValidation("Origin", WellRydeConfig.PortalOrigin);
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
        /// <c>JSESSIONID</c> from a prior response (e.g. static 404) may belong to a different ALB node than <c>trip/filterlist</c> / <c>listFilterDefsJson</c> — server returns <c>302 → /portal/</c> or HTTP 401. Expire Tomcat cookie and re-mint using the dated <c>nu?date=</c> referer when set (static probes + native servlet chain).
        /// </summary>
        /// <param name="filterDataRefererFallback">When <see cref="_lastTripsNuRefererWithDate"/> is empty, used for static-probe <c>Referer</c> (typically bare <c>nu</c> from <c>filterdata</c>).</param>
        private async Task TryRecoverTomcatStickyAfterElbJsessionMismatchAsync(string filterDataRefererFallback = null)
        {
            WellRydeCookieHelper.TryExpirePortalTomcatJsessionCookies(CookieJar);
            WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
            var refUrl = !string.IsNullOrEmpty(_lastTripsNuRefererWithDate)
                ? _lastTripsNuRefererWithDate
                : (!string.IsNullOrEmpty(filterDataRefererFallback)
                    ? filterDataRefererFallback
                    : WellRydeConfig.TripsPageAbsoluteUrl);
            await TryPrimeJsessionStaticRelativePathsAsync(refUrl, JsessionStaticProbePathsStickyResync, runCurlFallback: true).ConfigureAwait(false);
            if (!PortalCookieHeaderHasJsessionId())
                await TryPrimeJsessionStaticRelativePathsAsync(refUrl, JsessionStaticProbePaths, runCurlFallback: true).ConfigureAwait(false);
            if (!PortalCookieHeaderHasJsessionId())
                await TryAcquireTomcatJsessionViaNativeHandlerAsync(refUrl).ConfigureAwait(false);
            WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
        }

        /// <summary>
        /// Chrome <c>GET /portal//trip/filterlist</c> (portal.app 2026 gold): bare <c>Referer: …/portal/nu</c> via <see cref="TripsRefererForWellRydeAjax"/> (not <c>nu?date=…</c> — dated referer correlated with <c>302 → /portal/</c> in app Fiddler). <b>No</b> <c>X-Requested-With</c>, <b>no</b> CSRF duplicate headers, <b>no</b> <c>Priority</c>.
        /// <paramref name="useAlternateJsonAccept"/>: when <c>false</c>, <c>Accept: application/json, text/plain, */*</c>; when <c>true</c>, Dojo-style <c>application/json, text/javascript, */*; q=0.01</c> for a second attempt only.
        /// Trip <c>filterlist</c> uses <see cref="_filterDataClient"/>; <see cref="WellRydePortalCookieInjectingHandler"/> sends <see cref="WellRydeCookieHelper.BuildTripFilterListCookieHeaderChrome"/> (SESSION; JSESSIONID; ALB — no <c>XSRF-TOKEN</c> on this GET).
        /// </summary>
        private void ApplyWellRydeTripFilterlistXHRHeaders(HttpRequestMessage req, bool useAlternateJsonAccept)
        {
            req.Headers.TryAddWithoutValidation("Accept", useAlternateJsonAccept
                ? "application/json, text/javascript, */*; q=0.01"
                : "application/json, text/plain, */*");
            req.Headers.TryAddWithoutValidation("User-Agent", PortalBrowserUserAgent);
            req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
            req.Headers.TryAddWithoutValidation("Referer", TripsRefererForWellRydeAjax);
            req.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
            req.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "cors");
            req.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "empty");
            req.Headers.TryAddWithoutValidation("sec-ch-ua", PortalFilterDataSecChUa);
            req.Headers.TryAddWithoutValidation("sec-ch-ua-mobile", "?0");
            req.Headers.TryAddWithoutValidation("sec-ch-ua-platform", "\"Windows\"");
        }

        /// <summary>Alternate <c>Accept</c> on trip <c>filterlist</c> can help Dojo/JSON quirks — not redirects or auth failures.</summary>
        private static bool FilterListAlternateAcceptMightHelp(HttpResponseMessage resp)
        {
            if (resp == null)
                return false;
            var c = (int)resp.StatusCode;
            if (c >= 300 && c < 400)
                return false;
            if (c == 401 || c == 403 || c == 404)
                return false;
            return true;
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
                // Fiddler: static 404 can Set-Cookie JSESSIONID on node A while trip/filterlist hits node B → 302 Location /portal/ — re-mint Tomcat once then retry all filterlist URLs.
                var allowFilterlistStickyRestart = true;
            filterlistUrlsRestart:
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
                            var httpCode = (int)resp.StatusCode;
                            // Some stacks return HTTP 200 with an empty body for VTripBilling filterlist XHR — still counts as servlet prime (session bound).
                            if (resp.StatusCode == HttpStatusCode.OK && string.IsNullOrEmpty(body) && isVtSecS)
                            {
                                if (WellRydeConfig.PortalLogVerbose)
                                    WellRydeLog.WriteLine("WellRyde: trip filterlist HTTP 200 empty body — treating as servlet prime OK (SEC-S). url=" + url);
                                return;
                            }
                            if (resp.StatusCode != HttpStatusCode.OK || string.IsNullOrEmpty(body))
                            {
                                var redirect = httpCode >= 300 && httpCode < 400;
                                if (redirect && allowFilterlistStickyRestart)
                                {
                                    allowFilterlistStickyRestart = false;
                                    WellRydeLog.WriteLine("WellRyde: trip filterlist HTTP " + httpCode + " redirect (likely ALB vs JSESSIONID mismatch) — sticky recovery, then retry filterlist URLs. url=" + url);
                                    await TryRecoverTomcatStickyAfterElbJsessionMismatchAsync(TripsRefererForWellRydeAjax).ConfigureAwait(false);
                                    goto filterlistUrlsRestart;
                                }
                                WellRydeLog.WriteLine("WellRyde: trip filterlist HTTP " + httpCode + " content-type=" + ct + " len=" + (body?.Length ?? 0)
                                    + " setJsession=" + SetCookieHeadersMentionJsessionId(resp) + " url=" + url
                                    + " prefix=" + TrimForWellRydeLog(body, 200)
                                    + (attempt == 0 && FilterListAlternateAcceptMightHelp(resp) ? " — retrying with alternate Accept." : " — next URL or give up."));
                                if (attempt == 0 && FilterListAlternateAcceptMightHelp(resp))
                                    continue;
                                break;
                            }

                            var htmlShell = body.TrimStart().StartsWith("<", StringComparison.Ordinal);
                            if (htmlShell)
                            {
                                // Chrome HAR: GET trip filterlist often returns Dojo/XML HTML, not JSON — still primes servlet session before listFilterDefsJson/filterdata.
                                // When listDefId is already SEC-S_* (VTripBilling), we do not need JSON from this endpoint.
                                if (isVtSecS)
                                {
                                    if (WellRydeConfig.PortalLogVerbose)
                                        WellRydeLog.WriteLine("WellRyde: trip filterlist HTTP 200 HTML servlet prime OK (SEC-S list). len=" + body.Length + " url=" + url);
                                    return;
                                }
                                if (attempt == 0)
                                {
                                    if (WellRydeConfig.PortalLogVerbose)
                                        WellRydeLog.WriteLine("WellRyde: trip filterlist HTTP 200 HTML (Dojo shell, len=" + body.Length + ") url=" + url + " — retry alternate Accept.");
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
                                else if (WellRydeConfig.PortalLogVerbose)
                                    WellRydeLog.WriteLine("WellRyde: filterlist JSON parse found no SEC-* listDefId (prefix " +
                                                          (body.Length > 160 ? body.Substring(0, 160) + "…" : body) + ")");
                            }
                            else if (isVtSecS && WellRydeConfig.PortalLogVerbose)
                                WellRydeLog.WriteLine("WellRyde: trip filterlist XHR prime OK (explicit SEC-S listDefId — servlet session for filterdata). HTTP " + (int)resp.StatusCode + " len=" + body.Length + " url=" + url);
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (WellRydeConfig.PortalLogVerbose)
                    WellRydeLog.WriteLine("WellRyde: filterlist resolve error: " + ex.Message);
            }
        }

        /// <summary>
        /// GET <c>/portal/trip/filterlist</c> when <c>JSESSIONID</c> is still missing after <c>nu</c> (often <c>Set-Cookie</c> on this XHR only).
        /// For <c>SEC-S_*</c> lists, <see cref="TryResolveVtTripBillingListDefIfNeededAsync"/> also runs this XHR even when the cookie is already present.
        /// </summary>
        private async Task TryPrimeTripFilterListServletSessionAsync()
        {
            try
            {
                var allowFilterlistStickyRestart = true;
            servletPrimeFilterlistRestart:
                foreach (var url in WellRydeConfig.EnumerateTripFilterListRequestUrls())
                {
                    var req = new HttpRequestMessage(HttpMethod.Get, url);
                    ApplyWellRydeTripFilterlistXHRHeaders(req, useAlternateJsonAccept: false);

                    WellRydeCookieHelper.CollapseDuplicatePortalCookies(CookieJar);
                    using (var resp = await _filterDataClient.SendAsync(req).ConfigureAwait(false))
                    {
                        WellRydeCookieHelper.IngestSetCookieHeaders(resp, CookieJar);
                        WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
                        var code = (int)resp.StatusCode;
                        if (code >= 300 && code < 400 && allowFilterlistStickyRestart)
                        {
                            allowFilterlistStickyRestart = false;
                            WellRydeLog.WriteLine("WellRyde: trip filterlist servlet prime HTTP " + code + " redirect — sticky recovery then retry. url=" + url);
                            await TryRecoverTomcatStickyAfterElbJsessionMismatchAsync(TripsRefererForWellRydeAjax).ConfigureAwait(false);
                            goto servletPrimeFilterlistRestart;
                        }
                        if (WellRydeConfig.PortalLogVerbose)
                            WellRydeLog.WriteLine("WellRyde: trip filterlist XHR prime HTTP " + code + " list=" + WellRydeConfig.TripFilterListName
                                + " url=" + url + " setJsession=" + SetCookieHeadersMentionJsessionId(resp));
                    }
                    if (PortalCookieHeaderHasJsessionId())
                        break;
                }
            }
            catch (Exception ex)
            {
                if (WellRydeConfig.PortalLogVerbose)
                    WellRydeLog.WriteLine("WellRyde: filterlist servlet prime error: " + ex.Message);
            }
        }

        /// <summary>Fallback: full navigation-style GET (some stacks only <c>Set-Cookie: JSESSIONID</c> on <c>Sec-Fetch-Mode: navigate</c>).</summary>
        private async Task TryPrimeTripFilterListAsDocumentNavigateAsync()
        {
            try
            {
                string docUrl = null;
                foreach (var u in WellRydeConfig.EnumerateTripFilterListRequestUrls())
                {
                    docUrl = u;
                    break;
                }
                if (string.IsNullOrEmpty(docUrl))
                    return;
                var req = new HttpRequestMessage(HttpMethod.Get, docUrl);
                req.Headers.TryAddWithoutValidation("User-Agent", PortalBrowserUserAgent);
                req.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,application/json;q=0.8,*/*;q=0.7");
                req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
                req.Headers.TryAddWithoutValidation("Referer", TripsRefererForWellRydeAjax);
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
                    if (WellRydeConfig.PortalLogVerbose)
                        WellRydeLog.WriteLine("WellRyde: trip filterlist document prime HTTP " + (int)resp.StatusCode + " url=" + docUrl
                            + " setJsession=" + SetCookieHeadersMentionJsessionId(resp));
                }
            }
            catch (Exception ex)
            {
                if (WellRydeConfig.PortalLogVerbose)
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
                        var metaCsrfRefresh = GrabCSRFToken(html);
                        var token = metaCsrfRefresh ?? TryGetCsrfFromCookies();
                        if (!string.IsNullOrEmpty(token))
                        {
                            _CsrfToken = token;
                            PreferPortalXsrfCookieUnlessFreshHtmlMeta(metaCsrfRefresh);
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
                            if (WellRydeConfig.PortalLogVerbose && resp.Headers.TryGetValues("Set-Cookie", out var sc))
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

        /// <summary>
        /// Forces <see cref="WellRydeConfig.PortalOrigin"/> host URLs to <c>https</c> with default port so we never issue <c>http://…:443/…</c>
        /// (server <c>Location</c> quirks + automatic redirect otherwise duplicate hops in Fiddler and churn ELB stickiness).
        /// </summary>
        private static string NormalizePortalAbsoluteUrlString(string url)
        {
            if (string.IsNullOrEmpty(url))
                return url;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u))
                return url;
            try
            {
                var origin = new Uri(WellRydeConfig.PortalOrigin);
                if (!string.Equals(u.Host, origin.Host, StringComparison.OrdinalIgnoreCase))
                    return url;
                var ub = new UriBuilder(u)
                {
                    Scheme = Uri.UriSchemeHttps,
                    Port = -1,
                };
                return ub.Uri.AbsoluteUri;
            }
            catch
            {
                return url;
            }
        }

        private static Uri ResolveRedirectLocation(Uri requestUri, Uri location)
        {
            if (location == null || requestUri == null) return null;
            var resolved = location.IsAbsoluteUri ? location : new Uri(requestUri, location);
            try
            {
                var origin = new Uri(WellRydeConfig.PortalOrigin);
                if (!string.Equals(resolved.Host, origin.Host, StringComparison.OrdinalIgnoreCase))
                    return resolved;
                var ub = new UriBuilder(resolved)
                {
                    Scheme = Uri.UriSchemeHttps,
                    Port = -1,
                };
                return ub.Uri;
            }
            catch
            {
                return resolved;
            }
        }

        /// <summary>Stable key for redirect-loop detection (HTTPS, default port, path + query).</summary>
        private static string CanonicalPortalNavigateUrlKey(string url)
        {
            if (string.IsNullOrEmpty(url))
                return "";
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u))
                return url.Trim();
            try
            {
                var origin = new Uri(WellRydeConfig.PortalOrigin);
                var ub = new UriBuilder(u)
                {
                    Scheme = Uri.UriSchemeHttps,
                    Port = -1,
                };
                if (!string.Equals(ub.Host, origin.Host, StringComparison.OrdinalIgnoreCase))
                    return ub.Uri.AbsoluteUri;
                return ub.Uri.GetLeftPart(UriPartial.Path) + ub.Uri.Query;
            }
            catch
            {
                return u.AbsoluteUri;
            }
        }

        /// <summary>
        /// <see cref="HttpClientHandler"/> defaults to <c>AllowAutoRedirect=true</c>, which follows <c>http://…:443</c> from <c>Location</c> and multiplies
        /// <c>/portal/</c> ↔ <c>nu</c> hops in Fiddler. Use with <c>AllowAutoRedirect=false</c> + shared <see cref="CookieJar"/>.
        /// </summary>
        private async Task HttpClientPortalDocumentGetWithManualRedirectsAsync(
            HttpClient http,
            string startUrl,
            string referer,
            string secFetchSite,
            bool coldDocumentFirstHop,
            int maxRedirects = 16)
        {
            var url = NormalizePortalAbsoluteUrlString(startUrl) ?? startUrl;
            var refererForHop = string.IsNullOrEmpty(referer) ? WellRydeConfig.TripsPageAbsoluteUrl : referer;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            seen.Add(CanonicalPortalNavigateUrlKey(url));
            for (var hop = 0; hop < maxRedirects; hop++)
            {
                using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    req.Headers.TryAddWithoutValidation("User-Agent", PortalBrowserUserAgent);
                    req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
                    if (coldDocumentFirstHop && hop == 0)
                    {
                        req.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                        req.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "none");
                        req.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
                        req.Headers.TryAddWithoutValidation("Sec-Fetch-User", "?1");
                        req.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
                        req.Headers.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
                    }
                    else
                    {
                        req.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                        if (!string.IsNullOrEmpty(refererForHop))
                            req.Headers.TryAddWithoutValidation("Referer", refererForHop);
                        req.Headers.TryAddWithoutValidation("Sec-Fetch-Site", secFetchSite);
                        req.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
                        req.Headers.TryAddWithoutValidation("Sec-Fetch-User", "?1");
                        req.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
                        req.Headers.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
                    }
                    using (var resp = await http.SendAsync(req).ConfigureAwait(false))
                    {
                        WellRydeCookieHelper.IngestSetCookieHeaders(resp, CookieJar);
                        WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
                        var code = (int)resp.StatusCode;
                        if (code >= 300 && code < 400 && resp.Headers.Location != null)
                        {
                            var loc = ResolveRedirectLocation(resp.RequestMessage.RequestUri, resp.Headers.Location);
                            if (loc == null)
                            {
                                await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                                return;
                            }
                            var nextUrl = NormalizePortalAbsoluteUrlString(loc.AbsoluteUri) ?? loc.AbsoluteUri;
                            var nextKey = CanonicalPortalNavigateUrlKey(nextUrl);
                            if (!seen.Add(nextKey))
                            {
                                await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                                return;
                            }
                            refererForHop = url;
                            try
                            {
                                await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                            }
                            catch
                            {
                                /* ignore */
                            }
                            resp.Dispose();
                            url = nextUrl;
                            continue;
                        }
                        if (resp.IsSuccessStatusCode)
                        {
                            try
                            {
                                var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                                WellRydeCookieHelper.TryIngestJSessionIdFromMarkupAndLocation(CookieJar, body);
                            }
                            catch
                            {
                                await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                            }
                        }
                        else
                            await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                        return;
                    }
                }
            }
        }

        private async Task HttpClientNoCorsGetWithManualRedirectsAsync(
            HttpClient http,
            string startUrl,
            string referer,
            string accept,
            string dest,
            int maxRedirects = 10)
        {
            var url = NormalizePortalAbsoluteUrlString(startUrl) ?? startUrl;
            var refererForHop = string.IsNullOrEmpty(referer) ? WellRydeConfig.TripsPageAbsoluteUrl : referer;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            seen.Add(CanonicalPortalNavigateUrlKey(url));
            for (var hop = 0; hop < maxRedirects; hop++)
            {
                using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    req.Headers.TryAddWithoutValidation("User-Agent", PortalBrowserUserAgent);
                    req.Headers.TryAddWithoutValidation("Accept", accept);
                    req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
                    req.Headers.TryAddWithoutValidation("Referer", refererForHop);
                    req.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
                    req.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "no-cors");
                    req.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", dest);
                    using (var resp = await http.SendAsync(req).ConfigureAwait(false))
                    {
                        WellRydeCookieHelper.IngestSetCookieHeaders(resp, CookieJar);
                        WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(CookieJar);
                        var code = (int)resp.StatusCode;
                        if (code >= 300 && code < 400 && resp.Headers.Location != null)
                        {
                            var loc = ResolveRedirectLocation(resp.RequestMessage.RequestUri, resp.Headers.Location);
                            if (loc == null)
                            {
                                await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                                return;
                            }
                            var nextUrl = NormalizePortalAbsoluteUrlString(loc.AbsoluteUri) ?? loc.AbsoluteUri;
                            var nextKey = CanonicalPortalNavigateUrlKey(nextUrl);
                            if (!seen.Add(nextKey))
                            {
                                await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                                return;
                            }
                            refererForHop = url;
                            try
                            {
                                await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                            }
                            catch
                            {
                                /* ignore */
                            }
                            resp.Dispose();
                            url = nextUrl;
                            continue;
                        }
                        await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                        return;
                    }
                }
            }
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

        /// <summary>
        /// When the portal sets <c>XSRF-TOKEN</c> for <c>/portal</c>, prefer it over HTML meta for <c>_csrf</c> — only when we did <b>not</b> already read a token from page HTML.
        /// Preferring a stale cookie over a fresh <c>meta name="_csrf"</c> from <c>/portal/nu?date=</c> desyncs <c>POST filterdata</c> form <c>_csrf</c> from the Spring session (HTTP 401 / 500 / internal-error HTML shells).
        /// </summary>
        private void PreferPortalXsrfCookieForCsrfToken()
        {
            var fromCookie = TryGetCsrfFromCookies();
            if (!string.IsNullOrEmpty(fromCookie))
                _CsrfToken = fromCookie;
        }

        /// <summary>Calls <see cref="PreferPortalXsrfCookieForCsrfToken"/> only when <paramref name="csrfFromPageHtml"/> is empty (token came from cookie fallback only).</summary>
        private void PreferPortalXsrfCookieUnlessFreshHtmlMeta(string csrfFromPageHtml)
        {
            if (!string.IsNullOrEmpty(csrfFromPageHtml))
                return;
            PreferPortalXsrfCookieForCsrfToken();
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
                // Match browser Set-Cookie: raw token value (not URL-encoded) so Spring CookieCsrfTokenRepository matches form _csrf / header when used.
                // Match SESSION cookie path (/portal/) so CookieContainer applies XSRF to the same URLs as the session.
                CookieJar.Add(new Cookie("XSRF-TOKEN", _CsrfToken, "/portal/", host)
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
