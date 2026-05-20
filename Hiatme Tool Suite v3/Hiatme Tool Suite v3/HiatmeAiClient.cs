using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Hiatme_Tool_Suite_v3
{
    internal sealed class HiatmeAiScheduleBody
    {
        [JsonProperty("drivers")]
        public List<HiatmeAiDriverAssignment> Drivers { get; set; } = new List<HiatmeAiDriverAssignment>();

        [JsonProperty("reserves")]
        public List<string> Reserves { get; set; } = new List<string>();
    }

    internal sealed class HiatmeAiDriverAssignment
    {
        [JsonProperty("driver_name")]
        public string DriverName { get; set; }

        [JsonProperty("trip_numbers")]
        public List<string> TripNumbers { get; set; } = new List<string>();

        /// <summary>Optional route groups; when set, overrides flat <see cref="TripNumbers"/>.</summary>
        [JsonProperty("groups")]
        public List<List<string>> Groups { get; set; }
    }

    internal sealed class HiatmeAiAssistResponse
    {
        public string Message { get; set; }
        public HiatmeSchedulePatch Patch { get; set; }
        public string TraceId { get; set; }
    }

    internal sealed class HiatmeAiBuildResponse
    {
        public string Message { get; set; }

        [JsonProperty("thinking")]
        public string Thinking { get; set; }

        public HiatmeAiScheduleBody Schedule { get; set; }
        public string TraceId { get; set; }
    }

    internal sealed class HiatmeAiChatResponse
    {
        public string Message { get; set; }
        public string TraceId { get; set; }
    }

    /// <summary>Unified Send response — chat or schedule update.</summary>
    internal sealed class HiatmeAiMessageResponse
    {
        [JsonProperty("mode")]
        public string Mode { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("thinking")]
        public string Thinking { get; set; }

        [JsonProperty("patch")]
        public HiatmeSchedulePatch Patch { get; set; }

        [JsonProperty("schedule")]
        public HiatmeAiScheduleBody Schedule { get; set; }

        [JsonProperty("trace_id")]
        public string TraceId { get; set; }

        [JsonProperty("remembered")]
        public bool Remembered { get; set; }

        [JsonProperty("warnings")]
        public List<string> Warnings { get; set; }
    }

    internal static class HiatmeAiClient
    {
        private static readonly HttpClient SharedHttp = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(130),
        };

        /// <summary>Quick health check — GET /api/status.</summary>
        public static async Task<bool> PingAsync(
            HiatmeAiSettings settings,
            CancellationToken cancellationToken = default)
        {
            if (settings == null) return false;
            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl)) return false;

            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Get, baseUrl + "/api/status"))
                {
                    if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                        req.Headers.Authorization = new AuthenticationHeaderValue(
                            "Bearer", settings.ApiToken.Trim());
                    using (var resp = await SharedHttp.SendAsync(req, cancellationToken)
                        .ConfigureAwait(false))
                    {
                        return resp.IsSuccessStatusCode;
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        public static async Task SendFeedbackAsync(
            HiatmeAiSettings settings,
            int rating,
            string note,
            string traceId = null,
            CancellationToken cancellationToken = default)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            var body = new JObject
            {
                ["client_id"] = settings.ResolvedClientId(),
                ["rating"] = rating,
                ["note"] = note ?? "",
                ["trace_id"] = traceId ?? "",
            };
            using (var req = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/api/hiatme/feedback"))
            {
                req.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
                if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiToken.Trim());
                using (var resp = await SharedHttp.SendAsync(req, cancellationToken).ConfigureAwait(false))
                {
                    if (!resp.IsSuccessStatusCode)
                    {
                        var txt = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        throw new InvalidOperationException("Feedback failed: " + txt);
                    }
                }
            }
        }

        public static async Task<int> GetMemoryCountAsync(
            HiatmeAiSettings settings,
            CancellationToken cancellationToken = default)
        {
            if (settings == null) return 0;
            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl)) return 0;
            try
            {
                using (var req = new HttpRequestMessage(
                    HttpMethod.Get, baseUrl + "/api/hiatme/memory?limit=200"))
                {
                    if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                        req.Headers.Authorization = new AuthenticationHeaderValue(
                            "Bearer", settings.ApiToken.Trim());
                    using (var resp = await SharedHttp.SendAsync(req, cancellationToken).ConfigureAwait(false))
                    {
                        if (!resp.IsSuccessStatusCode) return 0;
                        var root = JObject.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
                        return root["count"]?.Value<int>() ?? 0;
                    }
                }
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>Push the on-screen schedule to the server working copy for this service date.</summary>
        public static async Task SyncScheduleAsync(
            HiatmeAiSettings settings,
            JObject context,
            string source = "sync",
            CancellationToken cancellationToken = default)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl)) return;

            var body = new JObject
            {
                ["client_id"] = settings.ResolvedClientId(),
                ["context"] = context ?? new JObject(),
                ["source"] = source ?? "sync",
            };

            using (var req = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/api/hiatme/schedule-sync"))
            {
                req.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
                if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiToken.Trim());

                using (var resp = await SharedHttp.SendAsync(req, cancellationToken).ConfigureAwait(false))
                {
                    if (!resp.IsSuccessStatusCode)
                    {
                        var txt = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        throw new InvalidOperationException("Schedule sync failed: " + txt);
                    }
                }
            }
        }

        public static void SyncScheduleFireAndForget(
            HiatmeAiSettings settings,
            JObject context,
            string source = "sync")
        {
            if (settings == null || context == null) return;
            _ = Task.Run(async () =>
            {
                try
                {
                    await SyncScheduleAsync(settings, context, source).ConfigureAwait(false);
                }
                catch
                {
                    // non-fatal background sync
                }
            });
        }

        private static readonly string[] _WeekdayNames =
            { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };

        /// <summary>
        /// Upload every weekday template CSV to the server so the AI keeps a synced copy of
        /// the "usual layout" for each day. Fire-and-forget — runs at app startup.
        /// </summary>
        public static async Task SyncTemplatesAsync(
            HiatmeAiSettings settings,
            bool purgeMissing = false,
            CancellationToken cancellationToken = default)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl)) return;

            var arr = new JArray();
            foreach (var weekday in _WeekdayNames)
            {
                string dir;
                try { dir = TemplateBuilder.GetDayTemplateDirectory(weekday); }
                catch { continue; }
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;

                foreach (var path in Directory.GetFiles(dir, "*.csv", SearchOption.TopDirectoryOnly))
                {
                    string fn = Path.GetFileName(path) ?? "";
                    if (string.IsNullOrEmpty(fn)) continue;
                    string content;
                    try { content = File.ReadAllText(path, Encoding.UTF8); }
                    catch { continue; }
                    if (string.IsNullOrEmpty(content)) continue;

                    arr.Add(new JObject
                    {
                        ["weekday"] = weekday,
                        ["filename"] = fn,
                        ["content"] = content,
                    });
                }
            }

            if (arr.Count == 0) return;

            var body = new JObject
            {
                ["templates"] = arr,
                ["purge_missing"] = purgeMissing,
            };

            using (var req = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/api/hiatme/templates/sync"))
            {
                req.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
                if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiToken.Trim());

                using (var resp = await SharedHttp.SendAsync(req, cancellationToken).ConfigureAwait(false))
                {
                    if (!resp.IsSuccessStatusCode)
                    {
                        var txt = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        throw new InvalidOperationException("Templates sync failed: " + txt);
                    }
                }
            }
        }

        public static void SyncTemplatesFireAndForget(HiatmeAiSettings settings)
        {
            if (settings == null) return;
            _ = Task.Run(async () =>
            {
                try { await SyncTemplatesAsync(settings).ConfigureAwait(false); }
                catch { /* non-fatal */ }
            });
        }

        public static async Task AddMemoryAsync(
            HiatmeAiSettings settings,
            string text,
            JObject dispatcherContext = null,
            CancellationToken cancellationToken = default)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            var note = (text ?? "").Trim();
            if (string.IsNullOrEmpty(note))
                throw new ArgumentException("text is required");

            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            var body = new JObject
            {
                ["client_id"] = settings.ResolvedClientId(),
                ["text"] = note,
                ["scope"] = "org",
                ["source"] = "tool_suite",
            };
            CopyDispatcherFields(body, dispatcherContext);

            using (var req = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/api/hiatme/memory"))
            {
                req.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
                if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiToken.Trim());

                using (var resp = await SharedHttp.SendAsync(req, cancellationToken).ConfigureAwait(false))
                {
                    var txt = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode)
                        throw new InvalidOperationException("Remember failed: " + txt);
                }
            }
        }

        public static async Task<HiatmeAiMessageResponse> SendMessageAsync(
            HiatmeAiSettings settings,
            JObject context,
            string message,
            CancellationToken cancellationToken = default)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl))
                throw new InvalidOperationException("AI server URL is not configured.");

            var body = new JObject
            {
                ["client_id"] = settings.ResolvedClientId(),
                ["context"] = context ?? new JObject(),
                ["message"] = message ?? "",
            };

            using (var req = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/api/hiatme/message"))
            {
                req.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
                if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiToken.Trim());

                using (var resp = await SharedHttp.SendAsync(req, cancellationToken).ConfigureAwait(false))
                {
                    var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode)
                        throw new InvalidOperationException("AI message failed: " + text);
                    return JsonConvert.DeserializeObject<HiatmeAiMessageResponse>(text);
                }
            }
        }

        public static async Task<HiatmeAiChatResponse> ChatAsync(
            HiatmeAiSettings settings,
            JObject context,
            string message,
            CancellationToken cancellationToken = default)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl))
                throw new InvalidOperationException("AI server URL is not configured.");

            var body = new JObject
            {
                ["client_id"] = settings.ResolvedClientId(),
                ["context"] = context ?? new JObject(),
                ["message"] = message ?? "",
            };

            using (var req = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/api/hiatme/chat"))
            {
                req.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
                if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiToken.Trim());

                using (var resp = await SharedHttp.SendAsync(req, cancellationToken).ConfigureAwait(false))
                {
                    var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode)
                        throw new InvalidOperationException("AI chat failed: " + text);
                    var root = JObject.Parse(text);
                    return new HiatmeAiChatResponse
                    {
                        Message = root["message"]?.ToString(),
                        TraceId = root["trace_id"]?.ToString(),
                    };
                }
            }
        }

        public static async Task<HiatmeAiBuildResponse> ScheduleReviseAsync(
            HiatmeAiSettings settings,
            JObject context,
            string feedback,
            CancellationToken cancellationToken = default)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl))
                throw new InvalidOperationException("AI server URL is not configured.");

            var body = new JObject
            {
                ["client_id"] = settings.ResolvedClientId(),
                ["context"] = context ?? new JObject(),
                ["feedback"] = feedback ?? "",
            };

            using (var req = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/api/hiatme/schedule-revise"))
            {
                req.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
                if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiToken.Trim());

                using (var resp = await SharedHttp.SendAsync(req, cancellationToken).ConfigureAwait(false))
                {
                    var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode)
                        throw new InvalidOperationException("AI revise failed: " + text);

                    var root = JObject.Parse(text);
                    var schedTok = root["schedule"] as JObject;
                    return new HiatmeAiBuildResponse
                    {
                        Message = root["message"]?.ToString(),
                        TraceId = root["trace_id"]?.ToString(),
                        Schedule = schedTok?.ToObject<HiatmeAiScheduleBody>(),
                    };
                }
            }
        }

        public static async Task<HiatmeAiBuildResponse> ScheduleBuildAsync(
            HiatmeAiSettings settings,
            JObject context,
            CancellationToken cancellationToken = default)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl))
                throw new InvalidOperationException("AI server URL is not configured.");

            var body = new JObject
            {
                ["client_id"] = settings.ResolvedClientId(),
                ["context"] = context ?? new JObject(),
            };

            using (var req = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/api/hiatme/schedule-build"))
            {
                req.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
                if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiToken.Trim());

                using (var resp = await SharedHttp.SendAsync(req, cancellationToken).ConfigureAwait(false))
                {
                    var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode)
                    {
                        string detail = text;
                        try
                        {
                            var err = JObject.Parse(text);
                            detail = err["detail"]?.ToString() ?? text;
                        }
                        catch { }
                        throw new InvalidOperationException(
                            "AI build failed " + (int)resp.StatusCode + ": " + detail);
                    }

                    var root = JObject.Parse(text);
                    var schedTok = root["schedule"] as JObject;
                    var schedule = schedTok?.ToObject<HiatmeAiScheduleBody>();
                    return new HiatmeAiBuildResponse
                    {
                        Message = root["message"]?.ToString(),
                        TraceId = root["trace_id"]?.ToString(),
                        Schedule = schedule,
                    };
                }
            }
        }

        public static async Task<HiatmeAiAssistResponse> ScheduleAssistAsync(
            HiatmeAiSettings settings,
            JObject context,
            string message,
            CancellationToken cancellationToken = default)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl))
                throw new InvalidOperationException("AI server URL is not configured.");

            var body = new JObject
            {
                ["client_id"] = settings.ResolvedClientId(),
                ["context"] = context ?? new JObject(),
                ["message"] = message ?? "",
            };

            using (var req = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/api/hiatme/schedule-assist"))
            {
                req.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
                if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiToken.Trim());

                using (var resp = await SharedHttp.SendAsync(req, cancellationToken).ConfigureAwait(false))
                {
                    var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode)
                    {
                        string detail = text;
                        try
                        {
                            var err = JObject.Parse(text);
                            detail = err["detail"]?.ToString() ?? text;
                        }
                        catch { }
                        throw new InvalidOperationException(
                            "AI server returned " + (int)resp.StatusCode + ": " + detail);
                    }

                    var root = JObject.Parse(text);
                    var patchTok = root["patch"] as JObject;
                    return new HiatmeAiAssistResponse
                    {
                        Message = root["message"]?.ToString(),
                        TraceId = root["trace_id"]?.ToString(),
                        Patch = patchTok != null
                            ? patchTok.ToObject<HiatmeSchedulePatch>()
                            : null,
                    };
                }
            }
        }

        private static void CopyDispatcherFields(JObject body, JObject dispatcherContext)
        {
            if (body == null || dispatcherContext == null) return;
            foreach (var key in new[]
            {
                "dispatcher_username",
                "dispatcher_display_name",
                "dispatcher_company_code",
                "dispatcher_source",
            })
            {
                var v = dispatcherContext[key]?.ToString();
                if (!string.IsNullOrWhiteSpace(v))
                    body[key] = v.Trim();
            }
        }
    }
}
