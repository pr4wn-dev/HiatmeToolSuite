using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Manual <c>Cookie</c> header with inner <c>UseCookies=false</c> so ELB/portal <c>Set-Cookie</c> is merged only via
    /// <see cref="WellRydeCookieHelper.IngestSetCookieHeaders"/> — avoids doubling cookies when the same response is also
    /// processed by <see cref="CookieContainer"/> on a shared <see cref="HttpClientHandler"/>.
    /// </summary>
    internal sealed class WellRydePortalCookieInjectingHandler : DelegatingHandler
    {
        /// <summary>Removed before the request leaves the app — tells us to omit <c>JSESSIONID</c> from <c>Cookie</c> (Tomcat id only in URL path).</summary>
        internal const string FilterDataOmitJsessionIdCookieHeader = "X-Hiatme-FilterDataOmitJsession";

        /// <summary>Removed before send — use minimal <c>SESSION;JSESSIONID;ALB</c> cookie line (Chrome copy-as-cURL) instead of full filterdata-style jar.</summary>
        internal const string TripFilterlistBareChromeCookie = "X-Hiatme-TripFilterlistBareChromeCookie";

        private readonly CookieContainer _jar;

        public WellRydePortalCookieInjectingHandler(CookieContainer jar, HttpMessageHandler innerHandler)
            : base(innerHandler)
        {
            _jar = jar ?? throw new ArgumentNullException(nameof(jar));
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri;
            if (uri != null)
            {
                var path = uri.AbsolutePath ?? "";
                // Do not rely on path.StartsWith alone: some Uri canonicalizations drop or alter ";jsessionid=…",
                // leaving AbsolutePath not matching while the request is still POST filterdata.
                var uriText = (uri.OriginalString ?? "") + "\n" + (uri.AbsoluteUri ?? "") + "\n" + path;
                var isFilterDataPost = request.Method == HttpMethod.Post
                    && uriText.IndexOf("/portal/filterdata", StringComparison.OrdinalIgnoreCase) >= 0;
                // GET filterlist must not send two JSESSIONID= lines (CookieContainer matches both Path=/portal and /portal/).
                // Chrome sends one id; duplicate cookies have been linked to HTTP 401 on /portal/trip/filterlist.
                var isTripFilterListGet = request.Method == HttpMethod.Get
                    && uriText.IndexOf("trip/filterlist", StringComparison.OrdinalIgnoreCase) >= 0
                    && uriText.IndexOf("list_name=", StringComparison.OrdinalIgnoreCase) >= 0;
                var isListFilterDefsJsonGet = request.Method == HttpMethod.Get
                    && uriText.IndexOf("/portal/listFilterDefsJson", StringComparison.OrdinalIgnoreCase) >= 0;

                string line = null;
                if (isFilterDataPost)
                {
                    WellRydeCookieHelper.CollapseDuplicatePortalCookies(_jar);
                    // HttpRequestMessage.Properties survives the pipeline; AsyncLocal covers older stacks.
                    var omitFromProp = false;
                    try
                    {
                        if (request.Properties != null
                            && request.Properties.TryGetValue(WellRydeFilterDataRequestKeys.OmitJsessionFromCookie, out var pv)
                            && Equals(pv, true))
                            omitFromProp = true;
                    }
                    catch
                    {
                        /* ignore */
                    }
                    var omitJsessionCookie = omitFromProp || WRLoginHandler.FilterDataOmitJsessionFromCookieActive;
                    if (request.Headers.TryGetValues(FilterDataOmitJsessionIdCookieHeader, out _))
                    {
                        omitJsessionCookie = true;
                        request.Headers.Remove(FilterDataOmitJsessionIdCookieHeader);
                    }
                    var uriStr = request.RequestUri?.OriginalString ?? request.RequestUri?.AbsoluteUri ?? "";
                    var urlHasJsessionRewrite = uriStr.IndexOf(";jsessionid=", StringComparison.OrdinalIgnoreCase) >= 0
                        || path.IndexOf(";jsessionid=", StringComparison.OrdinalIgnoreCase) >= 0
                        || uriText.IndexOf(";jsessionid=", StringComparison.OrdinalIgnoreCase) >= 0
                        || uriText.IndexOf("%3bjsessionid%3d", StringComparison.OrdinalIgnoreCase) >= 0;
                    line = WellRydeCookieHelper.BuildChromeLikeFilterDataCookieHeader(_jar, includeJsessionIdInHeader: !omitJsessionCookie && !urlHasJsessionRewrite);
                }
                else if (isListFilterDefsJsonGet)
                {
                    WellRydeCookieHelper.CollapseDuplicatePortalCookies(_jar);
                    line = WellRydeCookieHelper.BuildChromeLikeFilterDataCookieHeader(_jar, includeJsessionIdInHeader: true);
                }
                else if (isTripFilterListGet)
                {
                    // Prefer path-scoped jar line. Must use WellRydeCookieHelper.GetCookieHeader — _jar.GetCookieHeader binds
                    // CookieContainer's built-in method (duplicate Path=/portal + /portal/ → two JSESSIONID= on wire → HTTP 401).
                    if (request.Headers.TryGetValues(TripFilterlistBareChromeCookie, out _))
                        request.Headers.Remove(TripFilterlistBareChromeCookie);
                    WellRydeCookieHelper.CollapseDuplicatePortalCookies(_jar);
                    line = WellRydeCookieHelper.GetCookieHeader(_jar, uri);
                    if (string.IsNullOrEmpty(line))
                        line = WellRydeCookieHelper.BuildTripFilterListCookieHeaderChrome(_jar);
                }
                // Never substitute GetCookieHeader for filterdata: it re-adds JSESSIONID and breaks URL-only Tomcat retries.
                if (string.IsNullOrEmpty(line) && !isFilterDataPost)
                    line = WellRydeCookieHelper.GetCookieHeader(_jar, uri);
                if (!string.IsNullOrEmpty(line))
                {
                    if (request.Headers.TryGetValues("Cookie", out _))
                        request.Headers.Remove("Cookie");
                    request.Headers.TryAddWithoutValidation("Cookie", line);
                }
            }

            return base.SendAsync(request, cancellationToken);
        }
    }
}
