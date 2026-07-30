using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Fire-and-forget telemetry: the Tool Suite tells the AI server what it's doing
    /// (features used, emails sent, builds run) and — most importantly — when it breaks
    /// (errors / crashes). Feeds <c>/api/hiatme/toolsuite/event</c> so the company AI can
    /// recall Suite activity and answer "was the tool acting up?".
    ///
    /// Every path here swallows its own errors: telemetry must NEVER disrupt the app.
    /// </summary>
    internal static class HiatmeEventReporter
    {
        private static readonly HttpClient Http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(8),
        };

        // One id per app run, so the server can group a session's events.
        private static readonly string SessionId =
            Guid.NewGuid().ToString("N").Substring(0, 12);

        private static string _version;

        private static string AppVersion()
        {
            if (_version == null)
            {
                try
                {
                    _version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "";
                }
                catch
                {
                    _version = "";
                }
            }
            return _version;
        }

        /// <summary>Report one event. Fire-and-forget; never throws.</summary>
        public static void Report(
            string kind,
            string source,
            string message,
            string detail = null,
            JObject extra = null)
        {
            try
            {
                var body = new JObject
                {
                    ["kind"] = string.IsNullOrWhiteSpace(kind) ? "info" : kind.Trim(),
                    ["source"] = string.IsNullOrWhiteSpace(source) ? "toolsuite" : source.Trim(),
                    ["message"] = message ?? "",
                    ["session"] = SessionId,
                    ["version"] = AppVersion(),
                    ["machine"] = SafeMachineName(),
                };
                if (!string.IsNullOrWhiteSpace(detail))
                    body["detail"] = detail;
                if (extra != null)
                    body["extra"] = extra;

                // Fire and forget. Observe the task to avoid unobserved-exception noise.
                _ = Task.Run(() => PostAsync(body));
            }
            catch
            {
                /* telemetry must never disrupt the app */
            }
        }

        /// <summary>Convenience for feature-usage pings (e.g. a tab was opened).</summary>
        public static void ReportFeature(string feature, string message = null)
        {
            Report("feature_used", feature, message ?? feature);
        }

        /// <summary>Convenience for exceptions — captures type, message, and full stack.</summary>
        public static void ReportError(string source, Exception ex, string context = null)
        {
            if (ex == null)
                return;
            try
            {
                string head = string.IsNullOrWhiteSpace(context) ? "" : context.Trim() + ": ";
                string msg = head + ex.GetType().Name + ": " + ex.Message;
                Report("error", source, msg, ExceptionDetail(ex));
            }
            catch
            {
                /* swallow */
            }
        }

        /// <summary>Report a crash (unhandled/fatal). Sent synchronously-ish so it isn't lost on exit.</summary>
        public static void ReportCrash(string source, Exception ex, string context = null)
        {
            if (ex == null)
                return;
            try
            {
                string head = string.IsNullOrWhiteSpace(context) ? "" : context.Trim() + ": ";
                string msg = head + ex.GetType().Name + ": " + ex.Message;
                var body = new JObject
                {
                    ["kind"] = "crash",
                    ["source"] = string.IsNullOrWhiteSpace(source) ? "toolsuite" : source.Trim(),
                    ["message"] = msg,
                    ["detail"] = ExceptionDetail(ex),
                    ["session"] = SessionId,
                    ["version"] = AppVersion(),
                    ["machine"] = SafeMachineName(),
                };
                // On a fatal path the process may be tearing down; give the POST a brief
                // window to land instead of firing into a dying task pool.
                Task.Run(() => PostAsync(body)).Wait(TimeSpan.FromSeconds(5));
            }
            catch
            {
                /* swallow — we're already crashing */
            }
        }

        private static async Task PostAsync(JObject body)
        {
            try
            {
                var settings = HiatmeAiSettings.Load();
                if (settings == null || string.IsNullOrWhiteSpace(settings.BaseUrl))
                    return;

                string url = settings.BaseUrl.Trim().TrimEnd('/') + "/api/hiatme/toolsuite/event";
                using (var req = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(
                        body.ToString(Formatting.None), Encoding.UTF8, "application/json"),
                })
                {
                    if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                    {
                        req.Headers.Authorization =
                            new AuthenticationHeaderValue("Bearer", settings.ApiToken.Trim());
                    }
                    using (await Http.SendAsync(req).ConfigureAwait(false))
                    {
                        /* response ignored — best effort */
                    }
                }
            }
            catch
            {
                /* swallow — never disrupt the app */
            }
        }

        private static string ExceptionDetail(Exception ex)
        {
            try
            {
                var sb = new StringBuilder(2048);
                for (var e = ex; e != null; e = e.InnerException)
                {
                    sb.AppendLine(e.GetType().FullName + ": " + e.Message);
                    sb.AppendLine(e.StackTrace ?? "(no stack)");
                    sb.AppendLine();
                }
                return sb.ToString();
            }
            catch
            {
                return ex?.ToString() ?? "";
            }
        }

        private static string SafeMachineName()
        {
            try
            {
                return Environment.MachineName ?? "";
            }
            catch
            {
                return "";
            }
        }
    }
}
