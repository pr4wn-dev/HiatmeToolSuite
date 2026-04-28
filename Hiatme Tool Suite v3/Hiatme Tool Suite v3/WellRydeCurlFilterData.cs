using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Uses Windows <c>curl.exe</c> (libcurl) with a Netscape cookie jar — same cookie merge behavior as PHP <c>WellRydeScraper</c>.
    /// Needed when <c>HttpClient</c> never stores <c>JSESSIONID</c> and <c>filterdata</c> returns the HTML shell.
    /// </summary>
    internal static class WellRydeCurlFilterData
    {
        internal sealed class CurlPostResult
        {
            public int StatusCode { get; set; }
            public byte[] Body { get; set; }
            public string ResponseContentType { get; set; }
        }

        public static string TryResolveCurlPath()
        {
            try
            {
                var sys = Path.Combine(Environment.GetEnvironmentVariable("SystemRoot") ?? "C:\\Windows", "System32", "curl.exe");
                if (File.Exists(sys))
                    return sys;
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
            string cookieFile = null, cfgFile = null, bodyOut = null, hdrOut = null;
            try
            {
                cookieFile = Path.GetTempFileName();
                bodyOut = Path.GetTempFileName();
                hdrOut = Path.GetTempFileName();
                cfgFile = Path.GetTempFileName();
                WellRydeCookieHelper.ExportJarToNetscapeCookieFile(jar, cookieFile);
                var cfg = new StringBuilder();
                cfg.AppendLine("request = GET");
                cfg.AppendLine("url = \"" + EscapeCurlConfigString(url) + "\"");
                cfg.AppendLine("header = \"" + EscapeCurlConfigString("User-Agent: " + userAgent) + "\"");
                cfg.AppendLine("header = \"" + EscapeCurlConfigString("Accept: text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8") + "\"");
                cfg.AppendLine("header = \"" + EscapeCurlConfigString("Accept-Language: en-US,en;q=0.9") + "\"");
                if (!string.IsNullOrEmpty(referer))
                    cfg.AppendLine("header = \"" + EscapeCurlConfigString("Referer: " + referer) + "\"");
                File.WriteAllText(cfgFile, cfg.ToString(), Encoding.UTF8);
                if (RunCurl(curl, cookieFile, cookieFile, hdrOut, bodyOut, cfgFile, followRedirects: true))
                    WellRydeCookieHelper.TryMergeNetscapeCookieFileIntoJar(jar, cookieFile);
            }
            catch (Exception ex)
            {
                WellRydeLog.WriteLine("WellRyde: curl session GET error: " + ex.Message);
            }
            finally
            {
                TryDeleteFiles(cookieFile, cfgFile, bodyOut, hdrOut);
            }
        }

        public static CurlPostResult TryPostFilterData(CookieContainer jar, string postUrl, byte[] body, string contentTypeHeader, string referer, string csrfToken, string userAgent)
        {
            var curl = TryResolveCurlPath();
            if (curl == null || jar == null || body == null || string.IsNullOrEmpty(postUrl))
                return null;
            string cookieFile = null, bodyFile = null, bodyOut = null, hdrOut = null, cfgFile = null;
            try
            {
                cookieFile = Path.GetTempFileName();
                bodyFile = Path.GetTempFileName();
                bodyOut = Path.GetTempFileName();
                hdrOut = Path.GetTempFileName();
                cfgFile = Path.GetTempFileName();
                WellRydeCookieHelper.ExportJarToNetscapeCookieFile(jar, cookieFile);
                File.WriteAllBytes(bodyFile, body);
                var bodyPathFwd = bodyFile.Replace('\\', '/');
                var cfg = new StringBuilder();
                cfg.AppendLine("request = POST");
                cfg.AppendLine("url = \"" + EscapeCurlConfigString(postUrl) + "\"");
                cfg.AppendLine("data-binary = \"@" + EscapeCurlConfigString(bodyPathFwd) + "\"");
                cfg.AppendLine("header = \"" + EscapeCurlConfigString("Content-Type: " + contentTypeHeader) + "\"");
                cfg.AppendLine("header = \"" + EscapeCurlConfigString("Accept: application/json, text/javascript, */*;q=0.01") + "\"");
                cfg.AppendLine("header = \"" + EscapeCurlConfigString("Accept-Language: en-US,en;q=0.9") + "\"");
                cfg.AppendLine("header = \"" + EscapeCurlConfigString("Origin: " + WellRydeConfig.PortalOrigin) + "\"");
                cfg.AppendLine("header = \"" + EscapeCurlConfigString("Referer: " + referer) + "\"");
                cfg.AppendLine("header = \"" + EscapeCurlConfigString("X-Requested-With: XMLHttpRequest") + "\"");
                if (!string.IsNullOrEmpty(csrfToken))
                {
                    cfg.AppendLine("header = \"" + EscapeCurlConfigString("X-CSRF-TOKEN: " + csrfToken) + "\"");
                    cfg.AppendLine("header = \"" + EscapeCurlConfigString("X-XSRF-TOKEN: " + csrfToken) + "\"");
                }
                cfg.AppendLine("header = \"" + EscapeCurlConfigString("User-Agent: " + userAgent) + "\"");
                File.WriteAllText(cfgFile, cfg.ToString(), Encoding.UTF8);
                if (!RunCurl(curl, cookieFile, cookieFile, hdrOut, bodyOut, cfgFile, followRedirects: false))
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
                TryDeleteFiles(cookieFile, bodyFile, bodyOut, hdrOut, cfgFile);
            }
        }

        static string EscapeCurlConfigString(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        static bool RunCurl(string curlExe, string cookieInOut, string cookieOut, string headerDump, string bodyOut, string cfgFile, bool followRedirects)
        {
            var args = "-sS --compressed " +
                       (followRedirects ? "-L --max-redirs 10 " : "") +
                       "-b \"" + cookieInOut + "\" " +
                       "-c \"" + cookieOut + "\" " +
                       "-D \"" + headerDump + "\" " +
                       "-o \"" + bodyOut + "\" " +
                       "-K \"" + cfgFile + "\"";
            var psi = new ProcessStartInfo(curlExe, args)
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
