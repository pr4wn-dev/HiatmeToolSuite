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
        }

        /// <summary>
        /// Adds Tomcat <c>JSESSIONID</c> for portal APIs. Chrome stores it with <c>Path=/portal</c> (no trailing slash);
        /// Spring <c>SESSION</c> uses <c>/portal/</c>. <see cref="CookieContainer"/> path matching can miss one variant for
        /// <c>/portal/filterdata</c>, so we register both paths (same value).
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
                foreach (var path in new[] { "/portal", "/portal/" })
                {
                    try
                    {
                        jar.Add(new Cookie("JSESSIONID", v, path, host)
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
            if (WellRydeConfig.DebugPortalTraffic)
                WellRydeLog.WriteLine("WellRyde: removed synthetic JSESSIONID (SESSION UUID hex — not Tomcat); filterdata needs a real Set-Cookie JSESSIONID or WellRydeManualJsessionId.");
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

        /// <summary>Tomcat path rewriting: <c>/portal/filterdata;jsessionid=value</c> (semicolon must stay unescaped for some stacks).</summary>
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
            var ch = jar.GetCookieHeader(fu);
            if (!string.IsNullOrEmpty(ch) && ch.IndexOf("JSESSIONID=", StringComparison.OrdinalIgnoreCase) >= 0)
                return;
            TryAddPortalJSessionIdCookie(jar, manual);
        }

        /// <summary>Export jar to Netscape cookie file for <c>curl -b/-c</c> (PHP-style libcurl merge).</summary>
        public static void ExportJarToNetscapeCookieFile(CookieContainer jar, string path)
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
        /// <c>Cookie</c> header for <c>POST /portal/filterdata</c> (Chrome order): <c>AWSALB</c>; <c>AWSALBCORS</c>; <c>JSESSIONID</c> (if real); <c>SESSION</c>; <c>XSRF-TOKEN</c> (if enabled).
        /// </summary>
        public static string BuildChromeLikeFilterDataCookieHeader(CookieContainer jar)
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
            append("AWSALB");
            append("AWSALBCORS");
            var jSessionWire = valueFor("JSESSIONID");
            if (!string.IsNullOrEmpty(jSessionWire)
                && (!TryDecodeSpringSessionCookieToUuidHex(valueFor("SESSION"), out var synHex)
                    || !string.Equals(jSessionWire, synHex, StringComparison.OrdinalIgnoreCase)))
                parts.Add("JSESSIONID=" + jSessionWire);
            append("SESSION");
            if (WellRydeConfig.FilterDataCookieIncludeXsrfToken)
                append("XSRF-TOKEN");
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
                var parts = new List<string>();
                foreach (Cookie c in coll.Cast<Cookie>().OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                {
                    if (c.Expired)
                        continue;
                    parts.Add(c.Name + "=" + c.Value);
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
