using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Collections.Generic;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Console dumps when <c>filterdata</c> fails so output can be compared to browser DevTools / PHP <c>docs/WELLRYDE.md</c> curl flow.
    /// </summary>
    internal static class WellRydeHttpDiagnostics
    {
        /// <param name="requestFormBodySnapshot">Encoded <c>filterdata</c> body captured before send (request <see cref="HttpContent"/> is disposed after <c>SendAsync</c>).</param>
        public static void DumpFilterDataMismatch(HttpRequestMessage req, HttpResponseMessage resp, string responseBody, CookieContainer cookieJar, string requestFormBodySnapshot = null)
        {
            var sb = new StringBuilder(8192);
            sb.AppendLine("========== WellRyde filterdata — full HTTP snapshot (app) ==========");
            if (req != null)
            {
                sb.AppendLine("REQUEST " + req.Method + " " + req.RequestUri);
                foreach (var kv in req.Headers.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
                {
                    // User-Agent: HttpClient can store multiple parsed tokens; join with space so logs match a single browser string.
                    var sep = string.Equals(kv.Key, "User-Agent", StringComparison.OrdinalIgnoreCase) ? " " : ", ";
                    sb.AppendLine("  " + kv.Key + ": " + string.Join(sep, kv.Value));
                }
                if (req.Content != null)
                {
                    foreach (var kv in req.Content.Headers.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
                        sb.AppendLine("  " + kv.Key + ": " + string.Join(", ", kv.Value));
                    if (!string.IsNullOrEmpty(requestFormBodySnapshot))
                    {
                        sb.AppendLine("REQUEST body (truncated 2500):");
                        sb.AppendLine(requestFormBodySnapshot.Length <= 2500
                            ? requestFormBodySnapshot
                            : requestFormBodySnapshot.Substring(0, 2500) + "...");
                    }
                    else
                    {
                        try
                        {
                            var body = req.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                            sb.AppendLine("REQUEST body (truncated 2500):");
                            sb.AppendLine(body == null
                                ? "(null)"
                                : (body.Length <= 2500 ? body : body.Substring(0, 2500) + "..."));
                        }
                        catch (Exception ex)
                        {
                            sb.AppendLine("REQUEST body: (could not read: " + ex.Message + ")");
                        }
                    }
                }
                else if (!string.IsNullOrEmpty(requestFormBodySnapshot))
                {
                    sb.AppendLine("REQUEST body (truncated 2500):");
                    sb.AppendLine(requestFormBodySnapshot.Length <= 2500
                        ? requestFormBodySnapshot
                        : requestFormBodySnapshot.Substring(0, 2500) + "...");
                }
            }
            if (resp != null)
            {
                sb.AppendLine("RESPONSE " + (int)resp.StatusCode + " " + resp.ReasonPhrase);
                foreach (var kv in resp.Headers.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
                    sb.AppendLine("  " + kv.Key + ": " + string.Join(", ", kv.Value));
                if (resp.Content != null)
                {
                    foreach (var kv in resp.Content.Headers.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
                        sb.AppendLine("  " + kv.Key + ": " + string.Join(", ", kv.Value));
                }
            }
            sb.AppendLine("BuildChromeLikeFilterDataCookieHeader (Chrome-style ordering; compare to REQUEST Cookie above):");
            try
            {
                var chromeCookie = WellRydeCookieHelper.BuildChromeLikeFilterDataCookieHeader(cookieJar);
                sb.AppendLine("  " + (string.IsNullOrEmpty(chromeCookie) ? "(empty)" : chromeCookie));
            }
            catch (Exception ex)
            {
                sb.AppendLine("  (BuildChromeLikeFilterDataCookieHeader failed: " + ex.Message + ")");
            }
            sb.AppendLine("CookieContainer.GetCookieHeader for filterdata URL (name-sorted; often matches HttpClientHandler wire order):");
            try
            {
                var fu = new Uri(WellRydeConfig.FilterDataUrl);
                sb.AppendLine("  " + (cookieJar?.GetCookieHeader(fu) ?? "(null)"));
            }
            catch (Exception ex)
            {
                sb.AppendLine("  (GetCookieHeader failed: " + ex.Message + ")");
            }
            sb.AppendLine("CookieJar names @ portal root " + WellRydeConfig.PortalOrigin + "/ (SESSION often scoped to /portal — see next line):");
            try
            {
                DumpCookieNames(sb, cookieJar, new Uri(WellRydeConfig.PortalOrigin + "/"));
                sb.AppendLine("CookieJar names @ filterdata URL:");
                DumpCookieNames(sb, cookieJar, new Uri(WellRydeConfig.FilterDataUrl));
            }
            catch (Exception ex)
            {
                sb.AppendLine("  (enumeration failed: " + ex.Message + ")");
            }
            sb.AppendLine("CookieJar probe (GetCookies per URI — finds JSESSIONID stored under odd paths):");
            try
            {
                AppendCookieJarProbe(sb, cookieJar);
            }
            catch (Exception ex)
            {
                sb.AppendLine("  (probe failed: " + ex.Message + ")");
            }
            sb.AppendLine("Response body prefix (600 chars, whitespace collapsed):");
            sb.AppendLine(TrimBody(responseBody, 600));
            sb.AppendLine("--- Capture the same in the browser (gold reference) ---");
            sb.AppendLine("1) Chrome: F12 -> Network -> reload trips for that date -> click POST \"filterdata\".");
            sb.AppendLine("2) Request Headers: copy Referer, Cookie, Content-Type, Origin; Request Payload: listDefId, _csrf, filterList.");
            sb.AppendLine("3) Export: right-click the filterdata row -> Copy -> Copy all as HAR (save file).");
            sb.AppendLine("4) Repo doc: Hiatme-PHP-Website/docs/WELLRYDE.md -> \"Curl Reference\" (GET /portal/login -> POST login -> GET /portal/nu -> POST filterdata).");
            sb.AppendLine("================================================================");
            WellRydeLog.Write(sb.ToString());
        }

        private static void AppendCookieJarProbe(StringBuilder sb, CookieContainer jar)
        {
            if (jar == null)
            {
                sb.AppendLine("  (null jar)");
                return;
            }
            var uris = new List<string>
            {
                WellRydeConfig.PortalOrigin + "/",
                WellRydeConfig.PortalShellUrl,
                WellRydeConfig.TripsPageAbsoluteUrl,
                WellRydeConfig.FilterDataUrl,
            };
            foreach (var ustr in uris)
            {
                try
                {
                    var u = new Uri(ustr);
                    var coll = jar.GetCookies(u);
                    sb.AppendLine("  " + ustr + " → " + coll.Count + " cookie(s)");
                    foreach (Cookie c in coll.Cast<Cookie>().OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        if (c.Expired)
                            continue;
                        sb.AppendLine("    " + c.Name + "  path=" + c.Path + "  domain=" + c.Domain);
                    }
                }
                catch (Exception ex)
                {
                    sb.AppendLine("  " + ustr + " → error: " + ex.Message);
                }
            }
        }

        private static void DumpCookieNames(StringBuilder sb, CookieContainer cookieJar, Uri u)
        {
            var cookies = cookieJar?.GetCookies(u);
            if (cookies == null || cookies.Count == 0)
            {
                sb.AppendLine("  (none for " + u.AbsoluteUri + ")");
                return;
            }
            var list = new System.Collections.Generic.List<Cookie>();
            foreach (Cookie c in cookies)
                list.Add(c);
            foreach (Cookie c in list.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                sb.AppendLine("  " + c.Name + "  domain=" + c.Domain + " path=" + c.Path);
        }

        private static string TrimBody(string s, int max)
        {
            if (string.IsNullOrEmpty(s))
                return "(empty)";
            s = s.Replace('\r', ' ').Replace('\n', ' ');
            return s.Length <= max ? s : s.Substring(0, max) + "...";
        }
    }
}
