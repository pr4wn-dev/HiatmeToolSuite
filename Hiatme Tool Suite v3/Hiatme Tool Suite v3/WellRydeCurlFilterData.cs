using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Uses Windows <c>curl.exe</c> (libcurl) with a Netscape cookie jar — same cookie merge behavior as PHP <c>WellRydeScraper</c>.
    /// Needed when <c>HttpClient</c> never stores <c>JSESSIONID</c> and <c>filterdata</c> returns the HTML shell.
    /// Does not use <c>-K</c> (config file): 32-bit WOW64 often resolves to an older <c>SysWOW64\curl.exe</c> without that flag.
    /// </summary>
    internal static class WellRydeCurlFilterData
    {
        /// <summary>Match Chrome 147 copy-as-curl for portal.app <c>filterdata</c>.</summary>
        const string SecChUaChrome147 =
            "\"Google Chrome\";v=\"147\", \"Not.A/Brand\";v=\"8\", \"Chromium\";v=\"147\"";

        internal sealed class CurlPostResult
        {
            public int StatusCode { get; set; }
            public byte[] Body { get; set; }
            public string ResponseContentType { get; set; }
        }

        /// <summary>
        /// Resolve <c>curl.exe</c> for WellRyde POSTs. Order: WOW64 Sysnative, System32, SysWOW64, Git installs, then <c>PATH</c>.
        /// 32-bit AnyCPU apps often miss System32 curl without Sysnative; Git for Windows is a common fallback.
        /// </summary>
        public static string TryResolveCurlPath()
        {
            try
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var candidates = new List<string>();
                var win = Environment.GetEnvironmentVariable("SystemRoot") ?? "C:\\Windows";
                if (!Environment.Is64BitProcess)
                    candidates.Add(Path.Combine(win, "Sysnative", "curl.exe"));
                candidates.Add(Path.Combine(win, "System32", "curl.exe"));
                candidates.Add(Path.Combine(win, "SysWOW64", "curl.exe"));

                var p6432 = Environment.GetEnvironmentVariable("ProgramW6432");
                if (!string.IsNullOrEmpty(p6432))
                {
                    candidates.Add(Path.Combine(p6432, "Git", "mingw64", "bin", "curl.exe"));
                    candidates.Add(Path.Combine(p6432, "Git", "usr", "bin", "curl.exe"));
                }
                var pf86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
                if (!string.IsNullOrEmpty(pf86))
                {
                    candidates.Add(Path.Combine(pf86, "Git", "mingw64", "bin", "curl.exe"));
                    candidates.Add(Path.Combine(pf86, "Git", "usr", "bin", "curl.exe"));
                }

                foreach (var segment in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var t = segment.Trim().Trim('"');
                    if (t.Length == 0)
                        continue;
                    try
                    {
                        candidates.Add(Path.Combine(t, "curl.exe"));
                    }
                    catch
                    {
                        /* ignore */
                    }
                }

                foreach (var p in candidates)
                {
                    if (string.IsNullOrEmpty(p))
                        continue;
                    string full;
                    try
                    {
                        full = Path.GetFullPath(p);
                    }
                    catch
                    {
                        continue;
                    }
                    if (!seen.Add(full))
                        continue;
                    if (File.Exists(full))
                        return full;
                }
            }
            catch
            {
                /* ignore */
            }
            return null;
        }

        public static bool BodyLooksLikeJson(byte[] b)
        {
            if (b == null || b.Length == 0)
                return false;
            for (var i = 0; i < b.Length; i++)
            {
                var c = b[i];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
                    continue;
                return c == (byte)'{';
            }
            return false;
        }

        /// <summary>GET SPA page with curl cookie jar to capture <c>JSESSIONID</c> from <c>Set-Cookie</c>.</summary>
        public static void TrySessionPrimingGet(CookieContainer jar, string url, string referer, string userAgent)
        {
            var curl = TryResolveCurlPath();
            if (curl == null || jar == null || string.IsNullOrEmpty(url))
                return;
            string cookieFile = null, bodyOut = null, hdrOut = null;
            try
            {
                cookieFile = Path.GetTempFileName();
                bodyOut = Path.GetTempFileName();
                hdrOut = Path.GetTempFileName();
                WellRydeCookieHelper.ExportJarToNetscapeCookieFile(jar, cookieFile);
                var args = new StringBuilder();
                args.Append("-sS --compressed ");
                args.Append("-L --max-redirs 10 ");
                args.Append("-b ").Append(Win32Arg(ToCurlPath(cookieFile))).Append(' ');
                args.Append("-c ").Append(Win32Arg(ToCurlPath(cookieFile))).Append(' ');
                args.Append("-D ").Append(Win32Arg(ToCurlPath(hdrOut))).Append(' ');
                args.Append("-o ").Append(Win32Arg(ToCurlPath(bodyOut))).Append(' ');
                args.Append("-H ").Append(Win32Arg("User-Agent: " + userAgent)).Append(' ');
                args.Append("-H ").Append(Win32Arg("Accept: text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8")).Append(' ');
                args.Append("-H ").Append(Win32Arg("Accept-Language: en-US,en;q=0.9")).Append(' ');
                args.Append("-H ").Append(Win32Arg("Sec-Fetch-Site: same-origin")).Append(' ');
                args.Append("-H ").Append(Win32Arg("Sec-Fetch-Mode: navigate")).Append(' ');
                args.Append("-H ").Append(Win32Arg("Sec-Fetch-Dest: document")).Append(' ');
                args.Append("-H ").Append(Win32Arg("Upgrade-Insecure-Requests: 1")).Append(' ');
                if (!string.IsNullOrEmpty(referer))
                    args.Append("-H ").Append(Win32Arg("Referer: " + referer)).Append(' ');
                args.Append(Win32Arg(url));
                if (RunCurl(curl, args.ToString()))
                    WellRydeCookieHelper.TryMergeNetscapeCookieFileIntoJar(jar, cookieFile);
            }
            catch (Exception ex)
            {
                WellRydeLog.WriteLine("WellRyde: curl session GET error: " + ex.Message);
            }
            finally
            {
                TryDeleteFiles(cookieFile, bodyOut, hdrOut);
            }
        }

        /// <param name="omitJsessionIdFromCookieFile">When <c>true</c>, export the jar without <c>JSESSIONID</c> lines (use with <c>…/filterdata;jsessionid=…</c> — <see cref="System.Uri"/> often drops matrix params from <c>HttpClient</c> wire URL).</param>
        public static CurlPostResult TryPostFilterData(CookieContainer jar, string postUrl, byte[] body, string contentTypeHeader, string referer, string csrfToken, string userAgent, bool omitJsessionIdFromCookieFile = false)
        {
            var curl = TryResolveCurlPath();
            if (curl == null || jar == null || body == null || string.IsNullOrEmpty(postUrl))
                return null;
            string cookieFile = null, bodyFile = null, bodyOut = null, hdrOut = null;
            try
            {
                cookieFile = Path.GetTempFileName();
                bodyFile = Path.GetTempFileName();
                bodyOut = Path.GetTempFileName();
                hdrOut = Path.GetTempFileName();
                ICollection<string> exportExcludes = omitJsessionIdFromCookieFile
                    ? new ReadOnlyCollection<string>(new[] { "JSESSIONID" })
                    : null;
                WellRydeCookieHelper.ExportJarToNetscapeCookieFile(jar, cookieFile, exportExcludes);
                File.WriteAllBytes(bodyFile, body);
                var args = new StringBuilder();
                args.Append("-sS --compressed ");
                args.Append("-b ").Append(Win32Arg(ToCurlPath(cookieFile))).Append(' ');
                args.Append("-c ").Append(Win32Arg(ToCurlPath(cookieFile))).Append(' ');
                args.Append("-D ").Append(Win32Arg(ToCurlPath(hdrOut))).Append(' ');
                args.Append("-o ").Append(Win32Arg(ToCurlPath(bodyOut))).Append(' ');
                args.Append("-X POST ");
                args.Append("-H ").Append(Win32Arg("Content-Type: " + contentTypeHeader)).Append(' ');
                args.Append("-H ").Append(Win32Arg("Accept: application/json, text/javascript, */*; q=0.01")).Append(' ');
                args.Append("-H ").Append(Win32Arg("Accept-Language: en-US,en;q=0.9")).Append(' ');
                args.Append("-H ").Append(Win32Arg("Origin: " + WellRydeConfig.PortalOrigin)).Append(' ');
                args.Append("-H ").Append(Win32Arg("Priority: u=1, i")).Append(' ');
                args.Append("-H ").Append(Win32Arg("Referer: " + referer)).Append(' ');
                args.Append("-H ").Append(Win32Arg("X-Requested-With: XMLHttpRequest")).Append(' ');
                args.Append("-H ").Append(Win32Arg("Sec-Fetch-Site: same-origin")).Append(' ');
                args.Append("-H ").Append(Win32Arg("Sec-Fetch-Mode: cors")).Append(' ');
                args.Append("-H ").Append(Win32Arg("Sec-Fetch-Dest: empty")).Append(' ');
                args.Append("-H ").Append(Win32Arg("sec-ch-ua: " + SecChUaChrome147)).Append(' ');
                args.Append("-H ").Append(Win32Arg("sec-ch-ua-mobile: ?0")).Append(' ');
                args.Append("-H ").Append(Win32Arg("sec-ch-ua-platform: \"Windows\"")).Append(' ');
                if (WellRydeConfig.FilterDataPhpStyleHeaders && !string.IsNullOrEmpty(csrfToken))
                {
                    args.Append("-H ").Append(Win32Arg("X-CSRF-TOKEN: " + csrfToken)).Append(' ');
                    args.Append("-H ").Append(Win32Arg("X-XSRF-TOKEN: " + csrfToken)).Append(' ');
                }
                args.Append("-H ").Append(Win32Arg("User-Agent: " + userAgent)).Append(' ');
                args.Append("--data-binary ").Append(Win32Arg("@" + ToCurlPath(bodyFile))).Append(' ');
                args.Append(Win32Arg(postUrl));
                if (!RunCurl(curl, args.ToString()))
                    return null;
                var responseBody = File.Exists(bodyOut) ? File.ReadAllBytes(bodyOut) : Array.Empty<byte>();
                var status = ParseHttpStatusFromCurlHeaderDump(hdrOut);
                var ct = ParseContentTypeFromCurlHeaderDump(hdrOut);
                WellRydeCookieHelper.TryMergeNetscapeCookieFileIntoJar(jar, cookieFile);
                return new CurlPostResult
                {
                    StatusCode = status,
                    Body = responseBody,
                    ResponseContentType = ct,
                };
            }
            catch (Exception ex)
            {
                WellRydeLog.WriteLine("WellRyde: curl filterdata error: " + ex.Message);
                return null;
            }
            finally
            {
                TryDeleteFiles(cookieFile, bodyFile, bodyOut, hdrOut);
            }
        }

        static string ToCurlPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;
            return path.Replace('\\', '/');
        }

        /// <summary>Quote/escape one argv token for <see cref="ProcessStartInfo.Arguments"/> (Win32 <c>CommandLineToArgvW</c> rules).</summary>
        static string Win32Arg(string arg)
        {
            if (string.IsNullOrEmpty(arg))
                return "\"\"";
            var needsQuotes = false;
            for (var i = 0; i < arg.Length; i++)
            {
                if (arg[i] <= ' ' || arg[i] == '"')
                {
                    needsQuotes = true;
                    break;
                }
            }
            if (!needsQuotes)
                return arg;
            var b = new StringBuilder();
            b.Append('"');
            for (var i = 0; i < arg.Length;)
            {
                if (arg[i] == '\\')
                {
                    var k = i;
                    while (k < arg.Length && arg[k] == '\\')
                        k++;
                    var run = k - i;
                    if (k < arg.Length && arg[k] == '"')
                    {
                        b.Append('\\', run * 2 + 1);
                        b.Append('"');
                        i = k + 1;
                    }
                    else
                    {
                        b.Append('\\', run);
                        i = k;
                    }
                }
                else if (arg[i] == '"')
                {
                    b.Append('\\');
                    b.Append('"');
                    i++;
                }
                else
                {
                    b.Append(arg[i]);
                    i++;
                }
            }
            b.Append('"');
            return b.ToString();
        }

        static bool RunCurl(string curlExe, string arguments)
        {
            Process p = null;
            try
            {
                var psi = new ProcessStartInfo(curlExe, arguments)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                };
                p = Process.Start(psi);
                if (p == null)
                {
                    WellRydeLog.WriteLine("WellRyde: curl Process.Start returned null for " + curlExe);
                    return false;
                }
                var err = p.StandardError.ReadToEnd();
                if (!p.WaitForExit(120000))
                {
                    try
                    {
                        p.Kill();
                    }
                    catch
                    {
                        /* ignore */
                    }
                    WellRydeLog.WriteLine("WellRyde: curl timed out");
                    return false;
                }
                if (p.ExitCode != 0)
                {
                    WellRydeLog.WriteLine("WellRyde: curl exit " + p.ExitCode + " stderr=" + (err ?? "").Trim());
                    return false;
                }
            }
            catch (Exception ex)
            {
                WellRydeLog.WriteLine("WellRyde: curl failed to start or run: " + ex.Message);
                return false;
            }
            finally
            {
                try
                {
                    p?.Dispose();
                }
                catch
                {
                    /* ignore */
                }
            }
            return true;
        }

        static int ParseHttpStatusFromCurlHeaderDump(string hdrPath)
        {
            try
            {
                foreach (var line in File.ReadLines(hdrPath))
                {
                    if (line.StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2 && int.TryParse(parts[1], out var code))
                            return code;
                    }
                }
            }
            catch
            {
                /* ignore */
            }
            return 0;
        }

        static string ParseContentTypeFromCurlHeaderDump(string hdrPath)
        {
            try
            {
                foreach (var line in File.ReadLines(hdrPath))
                {
                    if (line.StartsWith("Content-Type:", StringComparison.OrdinalIgnoreCase))
                        return line.Substring(13).Trim();
                }
            }
            catch
            {
                /* ignore */
            }
            return null;
        }

        static void TryDeleteFiles(params string[] paths)
        {
            foreach (var p in paths)
            {
                try
                {
                    if (!string.IsNullOrEmpty(p) && File.Exists(p))
                        File.Delete(p);
                }
                catch
                {
                    /* ignore */
                }
            }
        }
    }
}
