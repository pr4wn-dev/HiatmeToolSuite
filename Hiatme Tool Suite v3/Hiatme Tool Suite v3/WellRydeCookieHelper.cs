using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// CookieContainer does not always mirror browser behavior on <c>302</c> + <c>Set-Cookie</c> (e.g. login hand-off).
    /// Chrome sends <c>SESSION</c> and a real Tomcat <c>JSESSIONID</c> on <c>POST /portal/filterdata</c>.
    /// Sending <c>JSESSIONID</c> equal to the Spring <c>SESSION</c> UUID as 32-char hex is <b>not</b> a valid servlet id — the portal returns HTTP 200 HTML shells instead of JSON.
    /// ALB often folds two cookies into one header: <c>AWSALB=…; Path=/,AWSALBCORS=…</c> — naive <c>;</c> parsing corrupts <c>Path</c> and duplicates cookies.
    /// </summary>
    internal static class WellRydeCookieHelper
    {
        /// <summary>Comma starts a <i>new</i> cookie, not <c>Expires=Tue, 05 May</c> (lookahead requires <c>name=</c>).</summary>
        private static readonly Regex SetCookieMultiSplit = new Regex(@",(?=\s*[A-Za-z][A-Za-z0-9_-]*\s*=)", RegexOptions.Compiled);

        private static readonly Regex JSessionIdInRaw = new Regex(@"JSESSIONID\s*=\s*([^;,\s]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Tomcat URL rewriting in markup — value may include <c>.</c> (e.g. <c>.node0</c>); not only <c>[A-Za-z0-9]</c>.</summary>
        private static readonly Regex[] JsessionIdInHtmlPatterns =
        {
            new Regex(@"(?:;|\?|&)jsessionid=([^;&\s""'<>]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex(@"[""']jsessionid[""']\s*:\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex(@"/portal/[^\s""']*;jsessionid=([^;&\s""']+)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex(@"""jsessionId""\s*:\s*""([^""]+)""", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex(@"""JSESSIONID""\s*:\s*""([^""]+)""", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex(@"jsessionid\s*=\s*['""]([^'""]+)['""]", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        };

        /// <summary>Merges <c>Set-Cookie</c> headers from a response into <paramref name="jar"/>.</summary>
        /// <remarks>
        /// Includes <c>AWSALB</c>/<c>AWSALBCORS</c>: portal HTTP uses <c>UseCookies=false</c> + manual injection — without these,
        /// ELB stickiness breaks and <c>POST /portal/filterdata</c> often returns HTTP 401 with an empty body.
        /// Duplicate <c>jar.Add</c> is ignored.
        /// </remarks>
        public static void IngestSetCookieHeaders(HttpResponseMessage response, CookieContainer jar)
        {
            if (response == null || jar == null)
                return;
            if (!response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string> headers))
                return;
            // Rare: RequestMessage null on some HttpResponseMessage paths — still parse Set-Cookie for JSESSIONID.
            var reqUri = response.RequestMessage?.RequestUri;
            if (reqUri == null)
            {
                try
                {
                    reqUri = new Uri(WellRydeConfig.PortalShellUrl);
                }
                catch
                {
                    return;
                }
            }
            foreach (var h in headers)
            {
                TryAddJSessionIdFromRawSetCookieHeader(h, reqUri, jar);
                foreach (var piece in SplitSetCookiePieces(h))
                {
                    var c = TryParseSetCookie(piece, reqUri);
                    if (c == null)
                        continue;
                    try
                    {
                        jar.Add(c);
                        if (string.Equals(c.Name, "JSESSIONID", StringComparison.OrdinalIgnoreCase))
                            TryAddPortalJSessionIdCookie(jar, c.Value);
                    }
                    catch
                    {
                        /* duplicate / version conflict — ignore */
                    }
                }
                // Merge full line again: catches JSESSIONID when manual split/attributes differ from RFC expectations.
                try
                {
                    jar.SetCookies(reqUri, h.Trim());
                }
                catch
                {
                    /* Expires commas / multi-cookie lines — manual pieces + TryAddJSessionIdFromRawSetCookieHeader are primary */
                }
            }
            if (response.Headers.Location != null)
                TryIngestJSessionIdFromMarkupAndLocation(jar, response.Headers.Location.ToString());
            CollapseDuplicatePortalCookies(jar);
        }

        /// <summary>
        /// After <see cref="IngestSetCookieHeaders"/> plus a shared <see cref="HttpClientHandler"/> on the same jar, the same
        /// <c>AWSALB</c> line can exist twice — some stacks then mis-route <c>filterdata</c> (HTML shell instead of JSON).
        /// </summary>
        public static void CollapseDuplicatePortalCookies(CookieContainer jar)
        {
            if (jar == null)
                return;
            Uri[] probes;
            try
            {
                probes = new[]
                {
                    new Uri(WellRydeConfig.PortalOrigin + "/"),
                    new Uri(WellRydeConfig.PortalShellUrl),
                    new Uri(WellRydeConfig.FilterDataUrl),
                    new Uri(WellRydeConfig.TripsPageAbsoluteUrl),
                };
            }
            catch
            {
                return;
            }
            foreach (var u in probes)
            {
                try
                {
                    var coll = jar.GetCookies(u);
                    if (coll == null || coll.Count <= 1)
                        continue;
                    var byKey = new Dictionary<string, List<Cookie>>(StringComparer.OrdinalIgnoreCase);
                    foreach (Cookie c in coll)
                    {
                        if (c.Expired)
                            continue;
                        var key = (c.Name ?? "") + "\0" + (c.Path ?? "");
                        if (!byKey.TryGetValue(key, out var list))
                        {
                            list = new List<Cookie>();
                            byKey[key] = list;
                        }
                        list.Add(c);
                    }
                    foreach (var kv in byKey)
                    {
                        var list = kv.Value;
                        if (list.Count <= 1)
                            continue;
                        for (var i = 0; i < list.Count - 1; i++)
                        {
                            var c = list[i];
                            try
                            {
                                jar.Add(new Cookie(c.Name, c.Value, c.Path, c.Domain)
                                {
                                    Expires = DateTime.UtcNow.AddDays(-1),
                                    Secure = c.Secure,
                                    HttpOnly = c.HttpOnly,
                                });
                            }
                            catch
                            {
                                /* ignore */
                            }
                        }
                    }
                }
                catch
                {
                    /* ignore */
                }
            }
            CollapsePortalPathSlashDuplicates(jar);
        }

        /// <summary>
        /// Spring may store <c>SESSION</c>/<c>XSRF-TOKEN</c> as <c>Path=/portal</c> and <c>/portal/</c>; both match <c>/portal/filterdata</c>
        /// and <see cref="CookieContainer.GetCookieHeader"/> then lists duplicates. Expire the <c>/portal</c> copy when values match (keep <c>/portal/</c>).
        /// </summary>
        static void CollapsePortalPathSlashDuplicates(CookieContainer jar)
        {
            if (jar == null)
                return;
            Uri fu;
            try
            {
                fu = new Uri(WellRydeConfig.FilterDataUrl);
            }
            catch
            {
                return;
            }
            CookieCollection coll;
            try
            {
                coll = jar.GetCookies(fu);
            }
            catch
            {
                return;
            }
            var byName = new Dictionary<string, List<Cookie>>(StringComparer.OrdinalIgnoreCase);
            foreach (Cookie c in coll)
            {
                if (c.Expired)
                    continue;
                var path = c.Path ?? "";
                if (path != "/portal" && path != "/portal/")
                    continue;
                var name = c.Name ?? "";
                if (string.IsNullOrEmpty(name))
                    continue;
                if (!byName.TryGetValue(name, out var list))
                {
                    list = new List<Cookie>();
                    byName[name] = list;
                }
                list.Add(c);
            }
            foreach (var kv in byName)
            {
                var list = kv.Value;
                if (list.Count < 2)
                    continue;
                var withSlash = list.FirstOrDefault(c => c.Path == "/portal/");
                var noSlash = list.FirstOrDefault(c => c.Path == "/portal");
                if (withSlash == null || noSlash == null)
                    continue;
                if (!string.Equals(withSlash.Value, noSlash.Value, StringComparison.Ordinal))
                    continue;
                TryExpireCookieWithPastExpiryTryingDomainVariants(jar, noSlash);
            }

            // HttpClientHandler (UseCookies) can still emit two JSESSIONID= lines when Path=/portal and /portal/ both
            // match and values differed transiently — some stacks return HTTP 401 on /portal/trip/*. Keep /portal/ only.
            Cookie jPortal = null, jPortalSlash = null;
            foreach (Cookie c in coll)
            {
                if (c.Expired || !string.Equals(c.Name, "JSESSIONID", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (c.Path == "/portal")
                    jPortal = c;
                else if (c.Path == "/portal/")
                    jPortalSlash = c;
            }
            if (jPortal != null && jPortalSlash != null)
                TryExpireCookieWithPastExpiryTryingDomainVariants(jar, jPortal);
        }

        /// <summary>
        /// <see cref="CookieContainer"/> only replaces an existing cookie when <c>Name</c>, <c>Path</c>, and <c>Domain</c> match how it was stored.
        /// Tomcat vs Spring sometimes differ on <c>Domain</c> (empty vs host vs leading dot) — a single <c>jar.Add(expired)</c> can silently fail and leave
        /// duplicate <c>Path=/portal</c> + <c>/portal/</c> cookies. <see cref="HttpClientHandler"/> then emits two <c>JSESSIONID=</c> on <c>Client.GetAsync</c> (trip filterlist).
        /// </summary>
        internal static void TryExpireCookieWithPastExpiryTryingDomainVariants(CookieContainer jar, Cookie c)
        {
            if (jar == null || c == null)
                return;
            var tried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void tryDom(string dom)
            {
                dom = dom ?? "";
                if (!tried.Add(dom))
                    return;
                try
                {
                    jar.Add(new Cookie(c.Name, c.Value, c.Path, dom)
                    {
                        Expires = DateTime.UtcNow.AddDays(-1),
                        Secure = c.Secure,
                        HttpOnly = c.HttpOnly,
                    });
                }
                catch
                {
                    /* ignore */
                }
            }
            if (!string.IsNullOrEmpty(c.Domain))
                tryDom(c.Domain);
            string host = null;
            try
            {
                host = new Uri(WellRydeConfig.PortalOrigin).Host;
            }
            catch
            {
                /* ignore */
            }
            if (!string.IsNullOrEmpty(host))
            {
                tryDom(host);
                if (!host.StartsWith(".", StringComparison.Ordinal))
                    tryDom("." + host);
            }
        }

        /// <summary>
        /// Adds Tomcat <c>JSESSIONID</c> for portal APIs with <c>Path=/portal/</c> so it matches <c>/portal/nu</c>, <c>/portal/filterdata</c>,
        /// and <c>/portal/trip/filterlist</c>. Registering both <c>/portal</c> and <c>/portal/</c> made <see cref="CookieContainer.GetCookieHeader"/>
        /// emit two <c>JSESSIONID=</c> pairs; some stacks then returned HTTP 401 (trip XHR) or HTML shells instead of JSON for <c>filterdata</c>.
        /// </summary>
        public static void TryAddPortalJSessionIdCookie(CookieContainer jar, string value)
        {
            if (string.IsNullOrEmpty(value) || jar == null)
                return;
            var v = value.Trim();
            if (v.Length < 8)
                return;
            try
            {
                var host = new Uri(WellRydeConfig.PortalOrigin).Host;
                try
                {
                    jar.Add(new Cookie("JSESSIONID", v, "/portal/", host)
                    {
                        Secure = true,
                        HttpOnly = true,
                    });
                }
                catch
                {
                    /* duplicate for this path — ignore */
                }
            }
            catch
            {
                /* ignore */
            }
        }

        /// <summary>
        /// Tomcat may store <c>JSESSIONID</c> with <c>Path=/</c> or <c>/portal</c>; <see cref="BuildChromeLikeFilterDataCookieHeader"/> reads via
        /// <see cref="CookieContainer.GetCookies(Uri)"/> for <c>/portal/filterdata</c>. Copy any discovered id onto <c>/portal/</c> so the trip XHR sends it.
        /// </summary>
        public static void TryPromoteJsessionIdToPortalPathForFilterData(CookieContainer jar)
        {
            if (jar == null)
                return;
            string found = null;
            foreach (var probe in EnumeratePortalCookieProbeUris())
            {
                try
                {
                    foreach (Cookie c in jar.GetCookies(probe))
                    {
                        if (c.Expired)
                            continue;
                        if (!string.Equals(c.Name, "JSESSIONID", StringComparison.OrdinalIgnoreCase))
                            continue;
                        var v = c.Value?.Trim();
                        if (string.IsNullOrEmpty(v) || v.Length < 8)
                            continue;
                        found = v;
                        break;
                    }
                }
                catch
                {
                    /* ignore */
                }
                if (found != null)
                    break;
            }
            if (found == null)
                return;
            if (JsessionIdLooksSynthesizedFromSpringSession(jar, new Uri(WellRydeConfig.FilterDataUrl)))
                return;
            TryAddPortalJSessionIdCookie(jar, found);
            CollapseDuplicatePortalCookies(jar);
        }

        private static IEnumerable<Uri> EnumeratePortalCookieProbeUris()
        {
            Uri fd, nu, shell, root, tripFilterList;
            try
            {
                fd = new Uri(WellRydeConfig.FilterDataUrl);
                nu = new Uri(WellRydeConfig.TripsPageAbsoluteUrl);
                shell = new Uri(WellRydeConfig.PortalShellUrl);
                root = new Uri(WellRydeConfig.PortalOrigin + "/");
                tripFilterList = new Uri(WellRydeConfig.PortalOrigin + "/portal/trip/filterlist");
            }
            catch
            {
                yield break;
            }
            yield return fd;
            yield return nu;
            yield return tripFilterList;
            yield return shell;
            yield return root;
        }

        /// <summary>Spring <c>SESSION</c> is often base64(UTF-8 UUID). The matching 32-char hex is <b>not</b> Tomcat&apos;s <c>JSESSIONID</c>.</summary>
        internal static bool TryDecodeSpringSessionCookieToUuidHex(string sessionB64, out string hex32Upper)
        {
            hex32Upper = null;
            if (string.IsNullOrEmpty(sessionB64))
                return false;
            try
            {
                var p = sessionB64.Trim();
                switch (p.Length % 4)
                {
                    case 2: p += "=="; break;
                    case 3: p += "="; break;
                }
                var utf8 = Encoding.UTF8.GetString(Convert.FromBase64String(p));
                if (!Guid.TryParse(utf8, out var g))
                    return false;
                hex32Upper = g.ToString("N").ToUpperInvariant();
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>True when the jar&apos;s <c>JSESSIONID</c> equals the UUID hex implied by <c>SESSION</c> (unsafe for <c>filterdata</c>).</summary>
        internal static bool JsessionIdLooksSynthesizedFromSpringSession(CookieContainer jar, Uri filterDataUri)
        {
            if (jar == null || filterDataUri == null)
                return false;
            string sessionB64 = null;
            string jVal = null;
            foreach (Cookie c in jar.GetCookies(filterDataUri))
            {
                if (string.Equals(c.Name, "SESSION", StringComparison.OrdinalIgnoreCase))
                    sessionB64 = c.Value;
                else if (string.Equals(c.Name, "JSESSIONID", StringComparison.OrdinalIgnoreCase))
                    jVal = c.Value?.Trim();
            }
            if (string.IsNullOrEmpty(sessionB64) || string.IsNullOrEmpty(jVal))
                return false;
            return TryDecodeSpringSessionCookieToUuidHex(sessionB64, out var hex)
                && string.Equals(jVal, hex, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Expires a poison <c>JSESSIONID</c> cookie so retries run servlet priming and the wire header stays clean.</summary>
        public static void TryRemoveSyntheticSpringJsessionIdFromJar(CookieContainer jar)
        {
            if (jar == null)
                return;
            Uri fu;
            try
            {
                fu = new Uri(WellRydeConfig.FilterDataUrl);
            }
            catch
            {
                return;
            }
            if (!JsessionIdLooksSynthesizedFromSpringSession(jar, fu))
                return;
            try
            {
                var host = new Uri(WellRydeConfig.PortalOrigin).Host;
                foreach (var path in new[] { "/portal/", "/portal" })
                {
                    try
                    {
                        jar.Add(new Cookie("JSESSIONID", "", path, host)
                        {
                            Expires = DateTime.UtcNow.AddDays(-1),
                            Secure = true,
                        });
                    }
                    catch
                    {
                        /* ignore */
                    }
                }
            }
            catch
            {
                /* ignore */
            }
            if (WellRydeConfig.PortalLogVerbose)
                WellRydeLog.WriteLine("WellRyde: removed synthetic JSESSIONID (SESSION UUID hex — not Tomcat); filterdata needs a real Set-Cookie JSESSIONID or WellRydeManualJsessionId.");
        }

        /// <summary>
        /// Expires Tomcat <c>JSESSIONID</c> copies on portal paths so the next static probe mints a servlet session on the <b>current</b> ELB target.
        /// A GET such as <c>listFilterDefsJson</c> may return new <c>AWSALB</c> cookies; the prior <c>JSESSIONID</c> can belong to another instance → HTTP 500 on <c>filterdata</c>.
        /// </summary>
        public static void TryExpirePortalTomcatJsessionCookies(CookieContainer jar)
        {
            if (jar == null)
                return;
            string host;
            try
            {
                host = new Uri(WellRydeConfig.PortalOrigin).Host;
            }
            catch
            {
                return;
            }
            foreach (var path in new[] { "/portal/", "/portal" })
            {
                try
                {
                    jar.Add(new Cookie("JSESSIONID", "", path, host)
                    {
                        Expires = DateTime.UtcNow.AddDays(-1),
                        Secure = true,
                    });
                }
                catch
                {
                    /* ignore */
                }
            }
        }

        /// <summary>Scan redirect URLs and HTML for Tomcat <c>jsessionid</c> (URL rewriting) when <c>Set-Cookie: JSESSIONID</c> never reaches the client.</summary>
        public static void TryIngestJSessionIdFromMarkupAndLocation(CookieContainer jar, string locationOrHtml)
        {
            if (jar == null || string.IsNullOrEmpty(locationOrHtml))
                return;
            foreach (var id in EnumerateJsessionIdsFromMarkupOrUrl(locationOrHtml))
            {
                if (IsJsessionTokenSpringUuidHex(jar, id))
                    continue;
                TryAddPortalJSessionIdCookie(jar, id);
            }
        }

        /// <summary>First non–Spring-UUID token from HTML/URL for <c>POST …/filterdata;jsessionid=…</c> when the cookie jar has no servlet id.</summary>
        public static string TryGetFirstJsessionIdFromTextForUrlRewrite(CookieContainer jar, string text)
        {
            if (jar == null || string.IsNullOrEmpty(text))
                return null;
            foreach (var id in EnumerateJsessionIdsFromMarkupOrUrl(text))
            {
                if (!IsJsessionTokenSpringUuidHex(jar, id))
                    return id.Trim();
            }
            return null;
        }

        /// <summary>Tomcat matrix URL helper (e.g. legacy PHP). Hiatme <c>PostWellRydeFilterDataAsync</c> posts plain <c>/portal/filterdata</c> and uses <see cref="TryAddPortalJSessionIdCookie"/> instead.</summary>
        public static string AppendTomcatJsessionUrlRewrite(string filterDataAbsoluteUrl, string jsessionId)
        {
            if (string.IsNullOrEmpty(filterDataAbsoluteUrl) || string.IsNullOrEmpty(jsessionId))
                return filterDataAbsoluteUrl;
            var id = jsessionId.Trim();
            if (id.Length < 8)
                return filterDataAbsoluteUrl;
            var u = new Uri(filterDataAbsoluteUrl);
            var path = u.AbsolutePath ?? "";
            if (path.IndexOf(";jsessionid=", StringComparison.OrdinalIgnoreCase) >= 0)
                return filterDataAbsoluteUrl;
            return u.GetLeftPart(UriPartial.Authority) + path + ";jsessionid=" + id;
        }

        /// <summary>Non–synthetic <c>JSESSIONID</c> from the jar for <c>https://host/portal/filterdata</c> (Cookie header / diagnostics).</summary>
        public static bool TryGetPortalJSessionIdCookieValue(CookieContainer jar, out string value)
        {
            value = null;
            if (jar == null)
                return false;
            Uri fu;
            try
            {
                fu = new Uri(WellRydeConfig.FilterDataUrl);
            }
            catch
            {
                return false;
            }
            if (JsessionIdLooksSynthesizedFromSpringSession(jar, fu))
                return false;
            foreach (Cookie c in jar.GetCookies(fu).Cast<Cookie>())
            {
                if (!string.Equals(c.Name, "JSESSIONID", StringComparison.OrdinalIgnoreCase))
                    continue;
                var v = c.Value?.Trim();
                if (string.IsNullOrEmpty(v) || v.Length < 8)
                    continue;
                if (IsJsessionTokenSpringUuidHex(jar, v))
                    continue;
                value = v;
                return true;
            }
            return false;
        }

        internal static bool IsJsessionTokenSpringUuidHex(CookieContainer jar, string candidate)
        {
            if (string.IsNullOrEmpty(candidate) || jar == null)
                return false;
            Uri fu;
            try
            {
                fu = new Uri(WellRydeConfig.FilterDataUrl);
            }
            catch
            {
                return false;
            }
            string sessionB64 = null;
            foreach (Cookie c in jar.GetCookies(fu))
            {
                if (string.Equals(c.Name, "SESSION", StringComparison.OrdinalIgnoreCase))
                {
                    sessionB64 = c.Value;
                    break;
                }
            }
            if (string.IsNullOrEmpty(sessionB64))
                return false;
            return TryDecodeSpringSessionCookieToUuidHex(sessionB64, out var hex)
                && string.Equals(candidate.Trim(), hex, StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> EnumerateJsessionIdsFromMarkupOrUrl(string text)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rx in JsessionIdInHtmlPatterns)
            {
                foreach (Match m in rx.Matches(text))
                {
                    var v = m.Groups[1].Value.Trim();
                    if (v.Length < 8 || v.Length > 256)
                        continue;
                    if (!seen.Add(v))
                        continue;
                    yield return v;
                }
            }
        }

        private static IEnumerable<string> SplitSetCookiePieces(string headerValue)
        {
            if (string.IsNullOrWhiteSpace(headerValue))
                yield break;
            var trimmed = headerValue.Trim();
            var parts = SetCookieMultiSplit.Split(trimmed);
            foreach (var p in parts)
            {
                var t = p.Trim();
                if (t.Length > 0)
                    yield return t;
            }
        }

        /// <summary>Failsafe: grab <c>JSESSIONID</c> from any raw <c>Set-Cookie</c> line (in case structured parse misses it).</summary>
        private static void TryAddJSessionIdFromRawSetCookieHeader(string raw, Uri requestUri, CookieContainer jar)
        {
            if (string.IsNullOrEmpty(raw) || jar == null || requestUri == null)
                return;
            var m = JSessionIdInRaw.Match(raw);
            if (!m.Success)
                return;
            var val = m.Groups[1].Value.Trim();
            if (string.IsNullOrEmpty(val))
                return;
            TryAddPortalJSessionIdCookie(jar, val);
        }

        private static Cookie TryParseSetCookie(string raw, Uri requestUri)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;
            var segments = raw.Split(new[] { ';' }, StringSplitOptions.None);
            if (segments.Length == 0)
                return null;
            var nv = segments[0].Trim();
            var eq = nv.IndexOf('=');
            if (eq <= 0)
                return null;
            var name = nv.Substring(0, eq).Trim();
            var value = nv.Substring(eq + 1).Trim();
            if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
                value = value.Substring(1, value.Length - 2).Replace("\\\"", "\"");
            if (string.IsNullOrEmpty(name))
                return null;

            string path = "/";
            string domain = requestUri.Host;
            var secure = string.Equals(requestUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
            var httpOnly = false;

            for (var i = 1; i < segments.Length; i++)
            {
                var p = segments[i].Trim();
                if (p.Length == 0)
                    continue;
                if (p.StartsWith("Path=", StringComparison.OrdinalIgnoreCase))
                    path = p.Substring(5).Trim();
                else if (p.StartsWith("Domain=", StringComparison.OrdinalIgnoreCase))
                    domain = p.Substring(7).Trim().TrimStart('.');
                else if (string.Equals(p, "Secure", StringComparison.OrdinalIgnoreCase))
                    secure = true;
                else if (string.Equals(p, "HttpOnly", StringComparison.OrdinalIgnoreCase))
                    httpOnly = true;
            }

            try
            {
                return new Cookie(name, value, path, domain)
                {
                    Secure = secure,
                    HttpOnly = httpOnly,
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// If <see cref="WellRydeConfig.ManualJsessionId"/> is set and the jar has no <c>JSESSIONID</c> for <c>filterdata</c>, add it.
        /// </summary>
        public static void TryApplyManualJsessionIdFromConfig(CookieContainer jar)
        {
            var manual = WellRydeConfig.ManualJsessionId;
            if (string.IsNullOrEmpty(manual) || jar == null)
                return;
            TryRemoveSyntheticSpringJsessionIdFromJar(jar);
            Uri fu;
            try
            {
                fu = new Uri(WellRydeConfig.FilterDataUrl);
            }
            catch
            {
                return;
            }
            var ch = GetCookieHeader(jar, fu);
            if (!string.IsNullOrEmpty(ch) && ch.IndexOf("JSESSIONID=", StringComparison.OrdinalIgnoreCase) >= 0)
                return;
            TryAddPortalJSessionIdCookie(jar, manual);
        }

        /// <summary>Export jar to Netscape cookie file for <c>curl -b/-c</c> (PHP-style libcurl merge).</summary>
        /// <param name="excludeCookieNames">When non-null, skip these cookie names (e.g. omit <c>JSESSIONID</c> when Tomcat id is only in the POST URL).</param>
        public static void ExportJarToNetscapeCookieFile(CookieContainer jar, string path, ICollection<string> excludeCookieNames = null)
        {
            if (jar == null || string.IsNullOrEmpty(path))
                return;
            Uri[] probes;
            try
            {
                probes = new[]
                {
                    new Uri(WellRydeConfig.PortalOrigin + "/"),
                    new Uri(WellRydeConfig.PortalShellUrl),
                    new Uri(WellRydeConfig.TripsPageAbsoluteUrl),
                    new Uri(WellRydeConfig.FilterDataUrl),
                };
            }
            catch
            {
                return;
            }
            var lines = new List<string>
            {
                "# Netscape HTTP Cookie File",
                "# Exported for curl.exe -b/-c",
            };
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var u in probes)
            {
                foreach (Cookie c in jar.GetCookies(u).Cast<Cookie>())
                {
                    if (c.Expired)
                        continue;
                    var dom = (c.Domain ?? new Uri(WellRydeConfig.PortalOrigin).Host).Trim().TrimStart('.');
                    var pathVal = string.IsNullOrEmpty(c.Path) ? "/" : c.Path;
                    var key = dom + "|" + pathVal + "|" + c.Name;
                    if (!seen.Add(key))
                        continue;
                    if (excludeCookieNames != null && excludeCookieNames.Count > 0)
                    {
                        var skip = false;
                        foreach (var ex in excludeCookieNames)
                        {
                            if (string.IsNullOrEmpty(ex))
                                continue;
                            if (string.Equals(c.Name, ex, StringComparison.OrdinalIgnoreCase))
                            {
                                skip = true;
                                break;
                            }
                        }
                        if (skip)
                            continue;
                    }
                    var includeSubdomains = "FALSE";
                    var secure = c.Secure ? "TRUE" : "FALSE";
                    long exp = 0;
                    try
                    {
                        if (c.Expires != DateTime.MinValue && c.Expires.Year > 1971)
                            exp = new DateTimeOffset(c.Expires.ToUniversalTime()).ToUnixTimeSeconds();
                    }
                    catch
                    {
                        exp = 0;
                    }
                    lines.Add(string.Join("\t", dom, includeSubdomains, pathVal, secure, exp.ToString(), c.Name, c.Value));
                }
            }
            File.WriteAllLines(path, lines, Encoding.UTF8);
        }

        /// <summary>Merge cookies from a Netscape file (e.g. after <c>curl -c</c>) into the jar.</summary>
        public static void TryMergeNetscapeCookieFileIntoJar(CookieContainer jar, string path)
        {
            if (jar == null || string.IsNullOrEmpty(path) || !File.Exists(path))
                return;
            foreach (var raw in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(raw) || raw[0] == '#')
                    continue;
                var p = raw.Split('\t');
                if (p.Length < 7)
                    continue;
                var domain = p[0].Trim().TrimStart('.');
                var pathAttr = string.IsNullOrEmpty(p[2]) ? "/" : p[2];
                var name = p[5];
                var value = p[6];
                try
                {
                    var cookie = new Cookie(name, value, pathAttr, domain)
                    {
                        Secure = string.Equals(p[3], "TRUE", StringComparison.OrdinalIgnoreCase),
                    };
                    jar.Add(cookie);
                }
                catch
                {
                    /* duplicate / invalid */
                }
                if (string.Equals(name, "JSESSIONID", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(value))
                    TryAddPortalJSessionIdCookie(jar, value);
            }
            CollapseDuplicatePortalCookies(jar);
        }

        /// <summary>
        /// <c>Cookie</c> header for <c>POST /portal/filterdata</c> — Chrome copy-as-curl order: <c>SESSION</c>; <c>JSESSIONID</c> (if real); <c>AWSALB</c>; <c>AWSALBCORS</c>; <c>XSRF-TOKEN</c> (if <see cref="WellRydeConfig.FilterDataCookieIncludeXsrfToken"/>).
        /// </summary>
        public static string BuildChromeLikeFilterDataCookieHeader(CookieContainer jar)
        {
            return BuildChromeLikeFilterDataCookieHeader(jar, includeJsessionIdInHeader: true, forceIncludeXsrfFromJar: false);
        }

        /// <summary>
        /// Some curl/HTML-shell retries omit <c>JSESSIONID</c> from the wire <c>Cookie</c> to avoid duplicate servlet binding — set <paramref name="includeJsessionIdInHeader"/> to <c>false</c>.
        /// </summary>
        /// <param name="forceIncludeXsrfFromJar">When <c>true</c>, append <c>XSRF-TOKEN</c> if present in the jar regardless of <see cref="WellRydeConfig.FilterDataCookieIncludeXsrfToken"/> (legacy; Chrome omits XSRF on <c>listFilterDefsJson</c>).</param>
        public static string BuildChromeLikeFilterDataCookieHeader(CookieContainer jar, bool includeJsessionIdInHeader, bool forceIncludeXsrfFromJar = false)
        {
            if (jar == null)
                return string.Empty;
            Uri fu;
            try
            {
                fu = new Uri(WellRydeConfig.FilterDataUrl);
            }
            catch
            {
                return string.Empty;
            }
            var coll = jar.GetCookies(fu);
            if (coll == null || coll.Count == 0)
                return string.Empty;
            string valueFor(string cookieName)
            {
                foreach (Cookie c in coll)
                {
                    if (c.Expired)
                        continue;
                    if (string.Equals(c.Name, cookieName, StringComparison.OrdinalIgnoreCase))
                        return c.Value;
                }
                return null;
            }
            var parts = new List<string>();
            void append(string name)
            {
                var v = valueFor(name);
                if (!string.IsNullOrEmpty(v))
                    parts.Add(name + "=" + v);
            }
            append("SESSION");
            if (includeJsessionIdInHeader)
            {
                var jSessionWire = valueFor("JSESSIONID");
                if (!string.IsNullOrEmpty(jSessionWire)
                    && (!TryDecodeSpringSessionCookieToUuidHex(valueFor("SESSION"), out var synHex)
                        || !string.Equals(jSessionWire, synHex, StringComparison.OrdinalIgnoreCase)))
                    parts.Add("JSESSIONID=" + jSessionWire);
            }
            append("AWSALB");
            append("AWSALBCORS");
            if (WellRydeConfig.FilterDataCookieIncludeXsrfToken || forceIncludeXsrfFromJar)
                append("XSRF-TOKEN");
            return string.Join("; ", parts);
        }

        /// <summary>
        /// Chrome <c>Copy as cURL</c> for <c>GET …/portal//trip/filterlist</c>: <c>SESSION</c>; <c>JSESSIONID</c> (real Tomcat only); <c>AWSALB</c>; <c>AWSALBCORS</c> — no <c>XSRF-TOKEN</c>.
        /// </summary>
        public static string BuildTripFilterListCookieHeaderChrome(CookieContainer jar)
        {
            if (jar == null)
                return string.Empty;
            Uri fu;
            try
            {
                fu = new Uri(WellRydeConfig.FilterDataUrl);
            }
            catch
            {
                return string.Empty;
            }
            var coll = jar.GetCookies(fu);
            if (coll == null || coll.Count == 0)
                return string.Empty;
            string valueFor(string cookieName)
            {
                foreach (Cookie c in coll)
                {
                    if (c.Expired)
                        continue;
                    if (string.Equals(c.Name, cookieName, StringComparison.OrdinalIgnoreCase))
                        return c.Value;
                }
                return null;
            }
            var parts = new List<string>();
            void append(string name)
            {
                var v = valueFor(name);
                if (!string.IsNullOrEmpty(v))
                    parts.Add(name + "=" + v);
            }
            append("SESSION");
            var jSessionWire = valueFor("JSESSIONID");
            if (!string.IsNullOrEmpty(jSessionWire)
                && (!TryDecodeSpringSessionCookieToUuidHex(valueFor("SESSION"), out var synHex)
                    || !string.Equals(jSessionWire, synHex, StringComparison.OrdinalIgnoreCase)))
                parts.Add("JSESSIONID=" + jSessionWire);
            append("AWSALB");
            append("AWSALBCORS");
            return string.Join("; ", parts);
        }

        /// <summary>Cookie header string that applies to <paramref name="uri"/> (diagnostics; matches browser ordering loosely).</summary>
        public static string GetCookieHeader(this CookieContainer jar, Uri uri)
        {
            if (jar == null || uri == null)
                return null;
            try
            {
                var coll = jar.GetCookies(uri);
                if (coll == null || coll.Count == 0)
                    return string.Empty;
                // One wire entry per cookie name: Path=/portal and /portal/ often both match → duplicate JSESSIONID breaks some APIs.
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var parts = new List<string>();
                foreach (Cookie c in coll.Cast<Cookie>()
                             .Where(x => !x.Expired)
                             .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                             .ThenByDescending(x => (x.Path ?? "").Length))
                {
                    var name = c.Name ?? "";
                    if (string.IsNullOrEmpty(name) || !seen.Add(name))
                        continue;
                    parts.Add(name + "=" + c.Value);
                }
                return string.Join("; ", parts);
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
