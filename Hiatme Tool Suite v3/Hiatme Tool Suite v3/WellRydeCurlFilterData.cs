using System;
using System.Collections.Generic;
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
        /// <summary>Match <see cref="WRLoginHandler"/> filterdata XHR — omitting these can yield the HTML shell.</summary>
        const string SecChUaChrome147 =
            "\"Chromium\";v=\"147\", \"Not A(Brand\";v=\"24\", \"Google Chrome\";v=\"147\"";

        internal sealed class CurlPostResult
        {
            public int StatusCode { get; set; }
            public byte[] Body { get; set; }
            public string ResponseContentType { get; set; }
        }

        /// <summary>64-bit System32 curl when the app is 32-bit (WOW64); else standard System32 path.</summary>
        public static string TryResolveCurlPath()
        {
            try
            {
                var win = Environment.GetEnvironmentVariable("SystemRoot") ?? "C:\\Windows";
                var candidates = new List<string>();
                if (!Environment.Is64BitProcess)
                {
                    /* 32-bit EXE: System32 is redirected to SysWOW64 — use Sysnative for real Windows curl. */
                    candidates.Add(Path.Combine(win, "Sysnative", "curl.exe"));
                }
                candidates.Add(Path.Combine(win, "System32", "curl.exe"));
                foreach (var p in candidates)
                {
                    if (File.Exists(p))
                        return p;
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

        public static CurlPostResult TryPostFilterData(CookieContainer jar, string postUrl, byte[] body, string contentTypeHeader, string referer, string csrfToken, string userAgent)
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
                WellRydeCookieHelper.ExportJarToNetscapeCookieFile(jar, cookieFile);
                File.WriteAllBytes(bodyFile, body);
                var args = new StringBuilder();
                args.Append("-sS --compressed ");
                args.Append("-b ").Append(Win32Arg(ToCurlPath(cookieFile))).Append(' ');
                args.Append("-c ").Append(Win32Arg(ToCurlPath(cookieFile))).Append(' ');
                args.Append("-D ").Append(Win32Arg(ToCurlPath(hdrOut))).Append(' ');
                args.Append("-o ").Append(Win32Arg(ToCurlPath(bodyOut))).Append(' ');
                args.Append("-X POST ");
                args.Append("-H ").Append(Win32Arg("Content-Type: " + contentTypeHeader)).Append(' ');
                args.Append("-H ").Append(Win32Arg("Accept: application/json, text/javascript, */*;q=0.01")).Append(' ');
                args.Append("-H ").Append(Win32Arg("Accept-Language: en-US,en;q=0.9")).Append(' ');
                args.Append("-H ").Append(Win32Arg("Origin: " + WellRydeConfig.PortalOrigin)).Append(' ');
                args.Append("-H ").Append(Win32Arg("Referer: " + referer)).Append(' ');
                args.Append("-H ").Append(Win32Arg("X-Requested-With: XMLHttpRequest")).Append(' ');
                args.Append("-H ").Append(Win32Arg("Sec-Fetch-Site: same-origin")).Append(' ');
                args.Append("-H ").Append(Win32Arg("Sec-Fetch-Mode: cors")).Append(' ');
                args.Append("-H ").Append(Win32Arg("Sec-Fetch-Dest: empty")).Append(' ');
                args.Append("-H ").Append(Win32Arg("sec-ch-ua: " + SecChUaChrome147)).Append(' ');
                args.Append("-H ").Append(Win32Arg("sec-ch-ua-mobile: ?0")).Append(' ');
                args.Append("-H ").Append(Win32Arg("sec-ch-ua-platform: \"Windows\"")).Append(' ');
                if (!string.IsNullOrEmpty(csrfToken))
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
            var psi = new ProcessStartInfo(curlExe, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            };
            using (var p = Process.Start(psi))
            {
                if (p == null)
                    return false;
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
