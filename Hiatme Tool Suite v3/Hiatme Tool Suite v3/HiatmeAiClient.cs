using System;
using System.Globalization;
using System.IO;
using System.Linq;
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

        [JsonProperty("reroute_reserves")]
        public List<string> RerouteReserves { get; set; } = new List<string>();
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

        /// <summary>Server solve engine: greedy, pyvrp+greedy, local, etc.</summary>
        public string Solver { get; set; }

        [JsonProperty("build_stats")]
        public HiatmeBuildStats BuildStats { get; set; }

        [JsonProperty("warnings")]
        public List<string> Warnings { get; set; }

        [JsonProperty("build_log_lines")]
        public List<string> BuildLogLines { get; set; }
    }

    internal sealed class HiatmeBuildStats
    {
        [JsonProperty("trips_total")]
        public int TripsTotal { get; set; }

        [JsonProperty("trips_assigned")]
        public int TripsAssigned { get; set; }

        [JsonProperty("reserves_count")]
        public int ReservesCount { get; set; }

        [JsonProperty("no_geo_count")]
        public int NoGeoCount { get; set; }

        [JsonProperty("unassigned_groups_count")]
        public int UnassignedGroupsCount { get; set; }

        [JsonProperty("cluster_count")]
        public int ClusterCount { get; set; }

        [JsonProperty("trips_with_coords")]
        public int TripsWithCoords { get; set; }

        [JsonProperty("geocoded_new")]
        public int GeocodedNew { get; set; }

        [JsonProperty("reroute_count")]
        public int RerouteCount { get; set; }

        [JsonProperty("osrm_route_http")]
        public int OsrmRouteHttp { get; set; }

        [JsonProperty("osrm_route_cache_hits")]
        public int OsrmRouteCacheHits { get; set; }

        [JsonProperty("osrm_table_calls")]
        public int OsrmTableCalls { get; set; }

        [JsonProperty("osrm_pair_ram_hits")]
        public int OsrmPairRamHits { get; set; }

        [JsonProperty("trips_clustered")]
        public int TripsClustered { get; set; }

        [JsonProperty("trips_locked_skipped_cluster")]
        public int TripsLockedSkippedCluster { get; set; }

        [JsonProperty("solve_elapsed_ms")]
        public int SolveElapsedMs { get; set; }

        [JsonProperty("build_elapsed_ms")]
        public int BuildElapsedMs { get; set; }
    }

    internal sealed class HiatmeAiChatResponse
    {
        public string Message { get; set; }
        public string TraceId { get; set; }
    }

    internal sealed class HiatmeArchiveStatusResponse
    {
        [JsonProperty("enabled")]
        public bool Enabled { get; set; }

        [JsonProperty("ingested_days")]
        public int IngestedDays { get; set; }

        [JsonProperty("tracked_files")]
        public int TrackedFiles { get; set; }

        [JsonProperty("last_run")]
        public double? LastRunUnixSeconds { get; set; }

        [JsonProperty("desktop_mirror_dirs")]
        public List<string> DesktopMirrorDirs { get; set; } = new List<string>();

        [JsonProperty("source_dirs")]
        public List<string> SourceDirs { get; set; } = new List<string>();
    }

    internal sealed class HiatmeArchiveSyncResponse
    {
        [JsonProperty("ok")]
        public bool Ok { get; set; }

        [JsonProperty("new_or_changed_files")]
        public int NewOrChangedFiles { get; set; }

        [JsonProperty("updated_service_dates")]
        public List<string> UpdatedServiceDates { get; set; } = new List<string>();

        [JsonProperty("ingested_days_total")]
        public int IngestedDaysTotal { get; set; }

        [JsonProperty("errors")]
        public List<string> Errors { get; set; } = new List<string>();
    }

    internal sealed class HiatmeArchiveQueryResponse
    {
        [JsonProperty("ok")]
        public bool Ok { get; set; }

        [JsonProperty("total_day_matches")]
        public int TotalDayMatches { get; set; }

        [JsonProperty("matches")]
        public JArray Matches { get; set; }

        /// <summary>
        /// How past runs of this corridor and client actually went, per driver. Present
        /// only when pu_city/do_city or client were supplied. Absent on older servers,
        /// which is why ranking must still work without it.
        /// </summary>
        [JsonProperty("fit")]
        public JObject Fit { get; set; }
    }

    /// <summary>POST /api/hiatme/forecast/placements — would this trip be named late here?</summary>
    internal sealed class HiatmeForecastPlacementsResponse
    {
        [JsonProperty("ok")]
        public bool Ok { get; set; }

        [JsonProperty("reason")]
        public string Reason { get; set; }

        [JsonProperty("trip_number")]
        public string TripNumber { get; set; }

        [JsonProperty("call_threshold")]
        public double? CallThreshold { get; set; }

        [JsonProperty("placements")]
        public List<HiatmeForecastPlacement> Placements { get; set; }
    }

    internal sealed class HiatmeForecastPlacement
    {
        [JsonProperty("driver")]
        public string Driver { get; set; }

        [JsonProperty("predicted_late")]
        public double? PredictedLate { get; set; }

        [JsonProperty("called")]
        public bool Called { get; set; }

        [JsonProperty("why")]
        public List<string> Why { get; set; }
    }

    /// <summary>GET /api/hiatme/schedules/workbook/meta</summary>
    internal sealed class HiatmeScheduleWorkbookMeta
    {
        [JsonProperty("ok")]
        public bool Ok { get; set; }

        [JsonProperty("exists")]
        public bool Exists { get; set; }

        [JsonProperty("filename")]
        public string Filename { get; set; }

        [JsonProperty("service_date")]
        public string ServiceDate { get; set; }

        [JsonProperty("mtime")]
        public double? Mtime { get; set; }

        [JsonProperty("size")]
        public long? Size { get; set; }

        [JsonProperty("etag")]
        public string Etag { get; set; }

        [JsonProperty("source")]
        public string Source { get; set; }

        [JsonProperty("synced")]
        public bool Synced { get; set; }

        [JsonProperty("reason")]
        public string Reason { get; set; }

        [JsonProperty("error")]
        public string Error { get; set; }
    }

    internal sealed class HiatmeAiPreReviewResponse
    {
        public List<string> Warnings { get; set; } = new List<string>();
        public JObject RulesContext { get; set; }
    }

    internal sealed class HiatmeAiRuleItem
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Kind { get; set; }
        public string Rationale { get; set; }
        public bool Enabled { get; set; }
        public string Source { get; set; }
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

        /// <summary>Path B address-change proposal — set when chat detects "X moved to Y".</summary>
        [JsonProperty("proposed_address_change")]
        public HiatmeProposedAddressChange ProposedAddressChange { get; set; }
    }

    /// <summary>Global Tool Suite assistant response (chat help + structured drafts).</summary>
    internal sealed class HiatmeAssistantResponse
    {
        [JsonProperty("mode")]
        public string Mode { get; set; }

        [JsonProperty("intent")]
        public string Intent { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("thinking")]
        public string Thinking { get; set; }

        [JsonProperty("trace_id")]
        public string TraceId { get; set; }

        [JsonProperty("draft")]
        public JObject Draft { get; set; }

        [JsonProperty("missing_fields")]
        public List<string> MissingFields { get; set; }

        [JsonProperty("confidence")]
        public string Confidence { get; set; }

        [JsonProperty("preview")]
        public HiatmeAssistantDraftPreview Preview { get; set; }

        [JsonProperty("actions")]
        public List<HiatmeAssistantAction> Actions { get; set; }
    }

    internal sealed class HiatmeAssistantAction
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("label")]
        public string Label { get; set; }

        [JsonProperty("args")]
        public HiatmeAssistantActionArgs Args { get; set; }
    }

    internal sealed class HiatmeAssistantActionArgs
    {
        [JsonProperty("tab")]
        public string Tab { get; set; }

        [JsonProperty("driver")]
        public string Driver { get; set; }

        [JsonProperty("trip")]
        public string Trip { get; set; }
    }

    internal sealed class HiatmeAssistantDraftPreview
    {
        [JsonProperty("confidence")]
        public string Confidence { get; set; }

        [JsonProperty("ready")]
        public bool Ready { get; set; }

        [JsonProperty("missing_fields")]
        public List<string> MissingFields { get; set; }

        [JsonProperty("missing_labels")]
        public List<string> MissingLabels { get; set; }

        [JsonProperty("driver_name")]
        public string DriverName { get; set; }

        [JsonProperty("action_level")]
        public string ActionLevel { get; set; }

        [JsonProperty("incident_date")]
        public string IncidentDate { get; set; }

        [JsonProperty("violations")]
        public List<string> Violations { get; set; }

        [JsonProperty("narrative")]
        public string Narrative { get; set; }
    }

    /// <summary>Driver address-change proposal queued by the AI; awaits dispatcher confirmation.</summary>
    internal sealed class HiatmeProposedAddressChange
    {
        [JsonProperty("kind")] public string Kind { get; set; }
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("driver_name")] public string DriverName { get; set; }
        [JsonProperty("current_home_pretty")] public string CurrentHomePretty { get; set; }
        [JsonProperty("proposed_home_pretty")] public string ProposedHomePretty { get; set; }
        [JsonProperty("source_message")] public string SourceMessage { get; set; }
        [JsonProperty("ai_hint")] public string AiHint { get; set; }
        [JsonProperty("proposed_home")] public HiatmeProposedHome ProposedHome { get; set; }
    }

    internal sealed class HiatmeProposedHome
    {
        [JsonProperty("street")] public string Street { get; set; }
        [JsonProperty("city")] public string City { get; set; }
        [JsonProperty("state")] public string State { get; set; }
        [JsonProperty("zip")] public string Zip { get; set; }
        [JsonProperty("lat")] public double? Lat { get; set; }
        [JsonProperty("lon")] public double? Lon { get; set; }
    }

    internal static class HiatmeAiClient
    {
        internal sealed class GmailDefaultsDocument
        {
            public string Address { get; set; }
            public string AppPassword { get; set; }
            public bool Configured { get; set; }
        }

        private static readonly HttpClient SharedHttp = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(130),
        };

        /// <summary>Long-running schedule-build/revise calls (panel Ollama can take several minutes).</summary>
        private static readonly HttpClient ScheduleBuildHttp = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(60),
        };

        /// <summary>
        /// Turn opaque HttpClient failures ("An error occurred while sending the request.") into something
        /// actionable — almost always "AI panel is down / unreachable".
        /// </summary>
        internal static string DescribeRequestError(Exception ex, string baseUrl = null)
        {
            if (ex == null)
                return "unknown error";

            while (ex is AggregateException ae && ae.InnerException != null)
                ex = ae.InnerException;

            string where = string.IsNullOrWhiteSpace(baseUrl)
                ? "http://127.0.0.1:8787"
                : baseUrl.Trim().TrimEnd('/');

            if (ex is TaskCanceledException || ex is OperationCanceledException)
                return "AI panel timed out at " + where + " — is it busy or hung?";

            bool sendFail = ex is HttpRequestException
                || (ex.Message != null
                    && ex.Message.IndexOf("sending the request", StringComparison.OrdinalIgnoreCase) >= 0)
                || (ex.Message != null
                    && ex.Message.IndexOf("while sending", StringComparison.OrdinalIgnoreCase) >= 0)
                || (ex.InnerException is System.Net.Sockets.SocketException)
                || (ex.InnerException is System.Net.WebException);

            if (sendFail)
            {
                string detail = ex.InnerException?.Message ?? ex.Message;
                if (string.IsNullOrWhiteSpace(detail)
                    || detail.IndexOf("sending the request", StringComparison.OrdinalIgnoreCase) >= 0)
                    detail = "connection refused / panel not running";
                return "AI panel unreachable at " + where
                    + " — restart it (scripts\\restart-panel.ps1). (" + detail + ")";
            }

            return ex.Message;
        }

        public sealed class BuildReadyStatus
        {
            public bool Ok { get; set; }
            public bool OsrmOk { get; set; }
            public bool SolveOk { get; set; }
            public string Solver { get; set; }
            public string OsrmActiveEndpoint { get; set; }
            public List<string> Issues { get; set; }
        }

        /// <summary>Live server BUILD progress (poll during POST /api/hiatme/solve).</summary>
        public sealed class BuildProgressStatus
        {
            public bool Active { get; set; }
            public string Phase { get; set; }
            public string Label { get; set; }
            public int Done { get; set; }
            public int Total { get; set; }
            public string Detail { get; set; }
            public int? EtaSeconds { get; set; }

            [JsonProperty("log_lines")]
            public List<string> LogLines { get; set; }
        }

        /// <summary>GET /api/hiatme/trips/status — Trip Scout live panel change token.</summary>
        public sealed class TripScoutServerStatus
        {
            public bool Ok { get; set; }
            public bool Available { get; set; }

            [JsonProperty("service_date")]
            public string ServiceDate { get; set; }

            [JsonProperty("trip_count")]
            public int TripCount { get; set; }

            [JsonProperty("content_hash")]
            public string ContentHash { get; set; }

            [JsonProperty("pulled_at")]
            public double? PulledAt { get; set; }

            public string Error { get; set; }
        }

        /// <summary>GET /api/hiatme/trips — cached WellRyde rows for Trip Scout merge.</summary>
        public sealed class TripScoutServerTrips
        {
            public bool Ok { get; set; }
            public bool Available { get; set; }

            [JsonProperty("service_date")]
            public string ServiceDate { get; set; }

            [JsonProperty("trip_count")]
            public int TripCount { get; set; }

            [JsonProperty("content_hash")]
            public string ContentHash { get; set; }

            [JsonProperty("trips")]
            public List<TripScoutServerTripRow> Trips { get; set; }
            public string Error { get; set; }
        }

        public sealed class TripScoutServerTripRow
        {
            [JsonProperty("trip_no")]
            public string TripNo { get; set; }

            [JsonProperty("trip_uuid")]
            public string TripUuid { get; set; }

            public string Driver { get; set; }
            public string Client { get; set; }
            public string Status { get; set; }

            [JsonProperty("sched_pu_iso")]
            public string SchedPuIso { get; set; }

            [JsonProperty("sched_do_iso")]
            public string SchedDoIso { get; set; }

            [JsonProperty("actual_pu_iso")]
            public string ActualPuIso { get; set; }

            [JsonProperty("actual_do_iso")]
            public string ActualDoIso { get; set; }

            [JsonProperty("pu_street")]
            public string PuStreet { get; set; }

            [JsonProperty("pu_city")]
            public string PuCity { get; set; }

            [JsonProperty("do_street")]
            public string DoStreet { get; set; }

            [JsonProperty("do_city")]
            public string DoCity { get; set; }

            public double? Miles { get; set; }
            public List<string> Alerts { get; set; }
        }

        /// <summary>GET /api/hiatme/wellryde/bell/status — WR bell will-call ready alerts.</summary>
        public sealed class WellRydeBellStatus
        {
            public bool Ok { get; set; }
            public bool Available { get; set; }

            [JsonProperty("polled_at")]
            public double? PolledAt { get; set; }

            [JsonProperty("willcall_count")]
            public int WillcallCount { get; set; }

            [JsonProperty("total_count")]
            public int TotalCount { get; set; }

            [JsonProperty("content_hash")]
            public string ContentHash { get; set; }

            [JsonProperty("has_new")]
            public bool HasNew { get; set; }

            [JsonProperty("willcalls")]
            public List<WellRydeBellWillCall> Willcalls { get; set; }

            [JsonProperty("poll_error")]
            public string PollError { get; set; }

            public string Error { get; set; }
        }

        public sealed class WellRydeBellWillCall
        {
            [JsonProperty("message_id")]
            public int MessageId { get; set; }

            [JsonProperty("trip_no")]
            public string TripNo { get; set; }

            public string Rider { get; set; }

            [JsonProperty("pu_addr")]
            public string PuAddr { get; set; }

            [JsonProperty("do_addr")]
            public string DoAddr { get; set; }

            [JsonProperty("pu_schedule")]
            public string PuSchedule { get; set; }

            [JsonProperty("created_at")]
            public string CreatedAt { get; set; }

            public string Title { get; set; }
        }

        /// <summary>GET /api/hiatme/trips/changes/scout — filtered day changes for Trip Scout.</summary>
        public sealed class TripScoutDayChanges
        {
            public bool Ok { get; set; }
            public bool Available { get; set; }

            [JsonProperty("service_date")]
            public string ServiceDate { get; set; }

            public int Count { get; set; }

            [JsonProperty("content_hash")]
            public string ContentHash { get; set; }

            [JsonProperty("last_ts")]
            public double? LastTs { get; set; }

            public List<TripScoutChangeRow> Changes { get; set; }
            public string Error { get; set; }
        }

        public sealed class TripScoutChangeRow
        {
            public double? Ts { get; set; }

            [JsonProperty("service_date")]
            public string ServiceDate { get; set; }

            [JsonProperty("trip_no")]
            public string TripNo { get; set; }

            public string Client { get; set; }
            public string Driver { get; set; }
            public string Kind { get; set; }
            public List<string> Tags { get; set; }
            public string Summary { get; set; }
            public List<TripScoutChangeFieldRow> Fields { get; set; }
        }

        public sealed class TripScoutChangeFieldRow
        {
            public string Field { get; set; }

            [JsonProperty("before")]
            public object Before { get; set; }

            [JsonProperty("after")]
            public object After { get; set; }
        }

        public static async Task<BuildProgressStatus> GetBuildProgressAsync(
            HiatmeAiSettings settings,
            CancellationToken cancellationToken = default)
        {
            if (settings == null) return null;
            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl)) return null;

            string cid = Uri.EscapeDataString(settings.ResolvedClientId());
            string url = baseUrl + "/api/hiatme/build-progress?client_id=" + cid;
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                        req.Headers.Authorization = new AuthenticationHeaderValue(
                            "Bearer", settings.ApiToken.Trim());
                    using (var resp = await SharedHttp.SendAsync(req, cancellationToken)
                        .ConfigureAwait(false))
                    {
                        if (!resp.IsSuccessStatusCode) return null;
                        var root = JObject.Parse(
                            await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
                        if (root["active"]?.Value<bool>() != true)
                            return new BuildProgressStatus { Active = false };
                        return new BuildProgressStatus
                        {
                            Active = true,
                            Phase = root["phase"]?.ToString(),
                            Label = root["label"]?.ToString(),
                            Done = root["done"]?.Value<int>() ?? 0,
                            Total = root["total"]?.Value<int>() ?? 0,
                            Detail = root["detail"]?.ToString(),
                            EtaSeconds = root["eta_seconds"]?.Type == JTokenType.Null
                                ? (int?)null
                                : root["eta_seconds"]?.Value<int>(),
                            LogLines = root["log_lines"]?.ToObject<List<string>>(),
                        };
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Start/wait for Maine OSRM on the panel host — POST /api/hiatme/osrm/ensure.</summary>
        public static async Task<bool> EnsureOsrmAsync(
            HiatmeAiSettings settings,
            CancellationToken cancellationToken = default)
        {
            if (settings == null) return false;
            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl)) return false;

            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/api/hiatme/osrm/ensure"))
                {
                    req.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
                    if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                        req.Headers.Authorization = new AuthenticationHeaderValue(
                            "Bearer", settings.ApiToken.Trim());
                    using (var resp = await SharedHttp.SendAsync(req, cancellationToken)
                        .ConfigureAwait(false))
                    {
                        if (!resp.IsSuccessStatusCode) return false;
                        var root = JObject.Parse(
                            await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
                        return root["ok"]?.Value<bool>() == true;
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Pre-BUILD: OSRM + solve smoke — GET /api/hiatme/ready.</summary>
        public static async Task<BuildReadyStatus> BuildReadyAsync(
            HiatmeAiSettings settings,
            CancellationToken cancellationToken = default)
        {
            if (settings == null) return null;
            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl)) return null;

            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Get, baseUrl + "/api/hiatme/ready"))
                {
                    if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                        req.Headers.Authorization = new AuthenticationHeaderValue(
                            "Bearer", settings.ApiToken.Trim());
                    using (var resp = await SharedHttp.SendAsync(req, cancellationToken)
                        .ConfigureAwait(false))
                    {
                        if (!resp.IsSuccessStatusCode) return null;
                        var root = JObject.Parse(
                            await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
                        var issues = new List<string>();
                        foreach (var w in root["issues"] as JArray ?? new JArray())
                        {
                            var s = w?.ToString();
                            if (!string.IsNullOrWhiteSpace(s)) issues.Add(s.Trim());
                        }
                        return new BuildReadyStatus
                        {
                            Ok = root["ok"]?.Value<bool>() == true,
                            OsrmOk = root["osrm_ok"]?.Value<bool>() == true,
                            SolveOk = root["solve_ok"]?.Value<bool>() == true,
                            Solver = root["solver"]?.ToString() ?? "",
                            OsrmActiveEndpoint = root["osrm_active_endpoint"]?.ToString() ?? "",
                            Issues = issues,
                        };
                    }
                }
            }
            catch
            {
                return null;
            }
        }

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

        /// <summary>Standing-rule warnings before BUILD (accepted rules + memory).</summary>
        public static async Task<HiatmeAiPreReviewResponse> PreReviewAsync(
            HiatmeAiSettings settings,
            CancellationToken cancellationToken = default)
        {
            if (settings == null) return null;
            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl)) return null;
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/api/hiatme/pre-review"))
                {
                    req.Content = new StringContent("{}", Encoding.UTF8, "application/json");
                    if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                        req.Headers.Authorization = new AuthenticationHeaderValue(
                            "Bearer", settings.ApiToken.Trim());
                    using (var resp = await SharedHttp.SendAsync(req, cancellationToken).ConfigureAwait(false))
                    {
                        if (!resp.IsSuccessStatusCode) return null;
                        var root = JObject.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
                        var warnings = new List<string>();
                        foreach (var w in root["warnings"] as JArray ?? new JArray())
                        {
                            var s = w?.ToString();
                            if (!string.IsNullOrWhiteSpace(s)) warnings.Add(s.Trim());
                        }
                        return new HiatmeAiPreReviewResponse
                        {
                            Warnings = warnings,
                            RulesContext = root["rules_context"] as JObject,
                        };
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        public static async Task<List<HiatmeAiRuleItem>> GetRulesAsync(
            HiatmeAiSettings settings,
            CancellationToken cancellationToken = default)
        {
            var list = new List<HiatmeAiRuleItem>();
            if (settings == null) return list;
            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl)) return list;
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Get, baseUrl + "/api/hiatme/rules"))
                {
                    if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                        req.Headers.Authorization = new AuthenticationHeaderValue(
                            "Bearer", settings.ApiToken.Trim());
                    using (var resp = await SharedHttp.SendAsync(req, cancellationToken).ConfigureAwait(false))
                    {
                        if (!resp.IsSuccessStatusCode) return list;
                        var root = JObject.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
                        foreach (var item in root["items"] as JArray ?? new JArray())
                        {
                            list.Add(ParseRuleItem(item));
                        }
                    }
                }
            }
            catch { /* optional */ }
            return list;
        }

        public static Task<List<HiatmeAiRuleItem>> GetProposedRulesAsync(
            HiatmeAiSettings settings,
            CancellationToken cancellationToken = default)
        {
            return GetRulesAsync(settings, cancellationToken);
        }

        private static HiatmeAiRuleItem ParseRuleItem(JToken item)
        {
            return new HiatmeAiRuleItem
            {
                Id = item["id"]?.ToString(),
                Title = item["title"]?.ToString(),
                Kind = item["kind"]?.ToString(),
                Rationale = item["rationale"]?.ToString(),
                Enabled = item["enabled"]?.Value<bool>() ?? true,
                Source = item["source"]?.ToString(),
            };
        }

        public static async Task<IList<string>> GetOutOfAreaAsync(
            HiatmeAiSettings settings,
            CancellationToken cancellationToken = default)
        {
            if (settings == null) return SupeyOutOfArea.LoadLocalFallback();
            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl))
                return SupeyOutOfArea.LoadLocalFallback();
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Get, baseUrl + "/api/hiatme/out-of-area"))
                {
                    if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                        req.Headers.Authorization = new AuthenticationHeaderValue(
                            "Bearer", settings.ApiToken.Trim());
                    using (var resp = await SharedHttp.SendAsync(req, cancellationToken).ConfigureAwait(false))
                    {
                        if (!resp.IsSuccessStatusCode)
                            return SupeyOutOfArea.LoadLocalFallback();
                        var root = JObject.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
                        var arr = root["areas"] as JArray ?? new JArray();
                        var list = new List<string>();
                        foreach (var t in arr)
                        {
                            var s = (t?.ToString() ?? "").Trim();
                            if (s.Length > 0) list.Add(s);
                        }
                        var norm = SupeyOutOfArea.NormalizeAreas(list);
                        SupeyOutOfArea.SetCachedAreas(norm);
                        return norm;
                    }
                }
            }
            catch
            {
                return SupeyOutOfArea.LoadLocalFallback();
            }
        }

        public static async Task<List<ScheduleBuilderReroutedTripRecord>> GetReroutedTripsAsync(
            HiatmeAiSettings settings,
            DateTime serviceDate,
            CancellationToken cancellationToken = default)
        {
            if (settings == null)
                return null;
            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl))
                return null;
            try
            {
                string sd = ScheduleBuilderReroutedTripsRegistry.FormatServiceDate(serviceDate);
                string url = baseUrl + "/api/hiatme/rerouted-trips?service_date="
                    + Uri.EscapeDataString(sd);
                using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                        req.Headers.Authorization = new AuthenticationHeaderValue(
                            "Bearer", settings.ApiToken.Trim());
                    using (var resp = await SharedHttp.SendAsync(req, cancellationToken).ConfigureAwait(false))
                    {
                        if (!resp.IsSuccessStatusCode)
                            return null;
                        var root = JObject.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
                        var arr = root["trips"] as JArray ?? new JArray();
                        if (arr.Count == 0)
                            return new List<ScheduleBuilderReroutedTripRecord>();
                        return arr.ToObject<List<ScheduleBuilderReroutedTripRecord>>()
                            ?? new List<ScheduleBuilderReroutedTripRecord>();
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        public static async Task<bool> AddReroutedTripAsync(
            HiatmeAiSettings settings,
            DateTime serviceDate,
            ScheduleBuilderReroutedTripRecord trip,
            string reroutedBy = "",
            CancellationToken cancellationToken = default)
        {
            if (settings == null || trip == null)
                return false;
            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl))
                return false;
            try
            {
                var payload = new JObject
                {
                    ["service_date"] = ScheduleBuilderReroutedTripsRegistry.FormatServiceDate(serviceDate),
                    ["rerouted_by"] = reroutedBy ?? "",
                    ["trip"] = JObject.FromObject(trip),
                };
                using (var req = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/api/hiatme/rerouted-trips"))
                {
                    req.Content = new StringContent(
                        payload.ToString(Formatting.None),
                        Encoding.UTF8,
                        "application/json");
                    if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                        req.Headers.Authorization = new AuthenticationHeaderValue(
                            "Bearer", settings.ApiToken.Trim());
                    using (var resp = await SharedHttp.SendAsync(req, cancellationToken).ConfigureAwait(false))
                        return resp.IsSuccessStatusCode;
                }
            }
            catch
            {
                return false;
            }
        }

        public static async Task<List<DriverEmailRecord>> GetDriverEmailsAsync(
            HiatmeAiSettings settings,
            CancellationToken cancellationToken = default)
        {
            if (settings == null)
                return null;
            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl))
                return null;
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Get, baseUrl + "/api/hiatme/driver-emails"))
                {
                    if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                        req.Headers.Authorization = new AuthenticationHeaderValue(
                            "Bearer", settings.ApiToken.Trim());
                    using (var resp = await SharedHttp.SendAsync(req, cancellationToken).ConfigureAwait(false))
                    {
                        if (!resp.IsSuccessStatusCode)
                            return null;
                        var root = JObject.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
                        var arr = root["drivers"] as JArray ?? new JArray();
                        if (arr.Count == 0)
                            return new List<DriverEmailRecord>();
                        return arr.ToObject<List<DriverEmailRecord>>()
                            ?? new List<DriverEmailRecord>();
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        public static async Task<bool> MergeDriverEmailsAsync(
            HiatmeAiSettings settings,
            IList<DriverEmailRecord> drivers,
            string updatedBy = "",
            CancellationToken cancellationToken = default)
        {
            if (settings == null || drivers == null || drivers.Count == 0)
                return false;
            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl))
                return false;
            try
            {
                var payload = new JObject
                {
                    ["updated_by"] = updatedBy ?? "",
                    ["drivers"] = JArray.FromObject(drivers),
                };
                using (var req = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/api/hiatme/driver-emails"))
                {
                    req.Content = new StringContent(
                        payload.ToString(Formatting.None),
                        Encoding.UTF8,
                        "application/json");
                    if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                        req.Headers.Authorization = new AuthenticationHeaderValue(
                            "Bearer", settings.ApiToken.Trim());
                    using (var resp = await SharedHttp.SendAsync(req, cancellationToken).ConfigureAwait(false))
                        return resp.IsSuccessStatusCode;
                }
            }
            catch
            {
                return false;
            }
        }

        public static async Task<GmailDefaultsDocument> GetGmailDefaultsAsync(
            HiatmeAiSettings settings,
            CancellationToken cancellationToken = default)
        {
            if (settings == null)
                return null;
            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl))
                return null;
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Get, baseUrl + "/api/hiatme/gmail"))
                {
                    if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                        req.Headers.Authorization = new AuthenticationHeaderValue(
                            "Bearer", settings.ApiToken.Trim());
                    using (var resp = await SharedHttp.SendAsync(req, cancellationToken).ConfigureAwait(false))
                    {
                        if (!resp.IsSuccessStatusCode)
                            return null;
                        var root = JObject.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
                        return new GmailDefaultsDocument
                        {
                            Address = (root["address"] ?? "").ToString(),
                            AppPassword = (root["appPassword"] ?? root["app_password"] ?? "").ToString(),
                            Configured = root["configured"] != null
                                && root["configured"].Type == JTokenType.Boolean
                                && root["configured"].Value<bool>(),
                        };
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        public static async Task<bool> PutGmailDefaultsAsync(
            HiatmeAiSettings settings,
            string address,
            string appPassword,
            string updatedBy = "",
            CancellationToken cancellationToken = default)
        {
            if (settings == null)
                return false;
            address = (address ?? "").Trim();
            appPassword = appPassword ?? "";
            if (string.IsNullOrEmpty(address) || string.IsNullOrEmpty(appPassword))
                return false;
            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl))
                return false;
            try
            {
                var payload = new JObject
                {
                    ["address"] = address,
                    ["appPassword"] = appPassword,
                    ["updated_by"] = updatedBy ?? "",
                };
                using (var req = new HttpRequestMessage(HttpMethod.Put, baseUrl + "/api/hiatme/gmail"))
                {
                    req.Content = new StringContent(
                        payload.ToString(Formatting.None),
                        Encoding.UTF8,
                        "application/json");
                    if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                        req.Headers.Authorization = new AuthenticationHeaderValue(
                            "Bearer", settings.ApiToken.Trim());
                    using (var resp = await SharedHttp.SendAsync(req, cancellationToken).ConfigureAwait(false))
                        return resp.IsSuccessStatusCode;
                }
            }
            catch
            {
                return false;
            }
        }

        public static async Task<bool> SetOutOfAreaAsync(
            HiatmeAiSettings settings,
            IList<string> areas,
            string updatedBy = "",
            CancellationToken cancellationToken = default)
        {
            if (settings == null) return false;
            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl)) return false;
            var norm = SupeyOutOfArea.NormalizeAreas(areas);
            if (norm.Count == 0) return false;
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Put, baseUrl + "/api/hiatme/out-of-area"))
                {
                    req.Content = new StringContent(
                        JsonConvert.SerializeObject(new { areas = norm, updated_by = updatedBy ?? "" }),
                        Encoding.UTF8,
                        "application/json");
                    if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                        req.Headers.Authorization = new AuthenticationHeaderValue(
                            "Bearer", settings.ApiToken.Trim());
                    using (var resp = await SharedHttp.SendAsync(req, cancellationToken).ConfigureAwait(false))
                    {
                        if (!resp.IsSuccessStatusCode) return false;
                        SupeyOutOfArea.SetCachedAreas(norm);
                        return true;
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        public static async Task<bool> SetRuleEnabledAsync(
            HiatmeAiSettings settings,
            string ruleId,
            bool enabled,
            CancellationToken cancellationToken = default)
        {
            if (settings == null || string.IsNullOrWhiteSpace(ruleId)) return false;
            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl)) return false;
            var url = baseUrl + "/api/hiatme/rules/" + Uri.EscapeDataString(ruleId);
            try
            {
                using (var req = new HttpRequestMessage(new HttpMethod("PATCH"), url))
                {
                    req.Content = new StringContent(
                        JsonConvert.SerializeObject(new { enabled }),
                        Encoding.UTF8,
                        "application/json");
                    if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                        req.Headers.Authorization = new AuthenticationHeaderValue(
                            "Bearer", settings.ApiToken.Trim());
                    using (var resp = await SharedHttp.SendAsync(req, cancellationToken).ConfigureAwait(false))
                        return resp.IsSuccessStatusCode;
                }
            }
            catch
            {
                return false;
            }
        }

        public static async Task<bool> DeleteRuleAsync(
            HiatmeAiSettings settings,
            string ruleId,
            CancellationToken cancellationToken = default)
        {
            if (settings == null || string.IsNullOrWhiteSpace(ruleId)) return false;
            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl)) return false;
            var url = baseUrl + "/api/hiatme/rules/" + Uri.EscapeDataString(ruleId);
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Delete, url))
                {
                    if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                        req.Headers.Authorization = new AuthenticationHeaderValue(
                            "Bearer", settings.ApiToken.Trim());
                    using (var resp = await SharedHttp.SendAsync(req, cancellationToken).ConfigureAwait(false))
                        return resp.IsSuccessStatusCode;
                }
            }
            catch
            {
                return false;
            }
        }

        public static Task<bool> AcceptRuleAsync(
            HiatmeAiSettings settings,
            string ruleId,
            CancellationToken cancellationToken = default)
        {
            return SetRuleEnabledAsync(settings, ruleId, true, cancellationToken);
        }

        public static Task<bool> RejectRuleAsync(
            HiatmeAiSettings settings,
            string ruleId,
            CancellationToken cancellationToken = default)
        {
            return DeleteRuleAsync(settings, ruleId, cancellationToken);
        }

        private static async Task<bool> PostRuleActionAsync(
            HiatmeAiSettings settings,
            string ruleId,
            string action,
            CancellationToken cancellationToken)
        {
            if (settings == null || string.IsNullOrWhiteSpace(ruleId)) return false;
            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl)) return false;
            var url = baseUrl + "/api/hiatme/rules/" + Uri.EscapeDataString(ruleId) + "/" + action;
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Post, url))
                {
                    req.Content = new StringContent("{}", Encoding.UTF8, "application/json");
                    if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                        req.Headers.Authorization = new AuthenticationHeaderValue(
                            "Bearer", settings.ApiToken.Trim());
                    using (var resp = await SharedHttp.SendAsync(req, cancellationToken).ConfigureAwait(false))
                        return resp.IsSuccessStatusCode;
                }
            }
            catch
            {
                return false;
            }
        }

        public static async Task SendDispatchFeedbackKindAsync(
            HiatmeAiSettings settings,
            string kind,
            JObject payload,
            CancellationToken cancellationToken = default)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl)) return;
            var path = string.Equals(kind, "bad", StringComparison.OrdinalIgnoreCase)
                ? "/api/hiatme/feedback/bad"
                : "/api/hiatme/feedback/good";
            var body = payload ?? new JObject();
            body["client_id"] = settings.ResolvedClientId();
            using (var req = new HttpRequestMessage(HttpMethod.Post, baseUrl + path))
            {
                req.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
                if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                    req.Headers.Authorization = new AuthenticationHeaderValue(
                        "Bearer", settings.ApiToken.Trim());
                using (var resp = await SharedHttp.SendAsync(req, cancellationToken).ConfigureAwait(false))
                {
                    if (!resp.IsSuccessStatusCode)
                    {
                        var txt = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        throw new InvalidOperationException("Dispatch feedback failed: " + txt);
                    }
                }
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

        /// <summary>Approve a queued driver address change (Path B). Returns true on success.</summary>
        public static async Task<bool> ApproveAddressChangeAsync(
            HiatmeAiSettings settings,
            string changeId,
            string decidedBy = null,
            CancellationToken cancellationToken = default)
        {
            return await PostAddressChangeDecisionAsync(
                settings, changeId, "approve", decidedBy, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Reject a queued driver address change. Returns true on success.</summary>
        public static async Task<bool> RejectAddressChangeAsync(
            HiatmeAiSettings settings,
            string changeId,
            string decidedBy = null,
            CancellationToken cancellationToken = default)
        {
            return await PostAddressChangeDecisionAsync(
                settings, changeId, "reject", decidedBy, cancellationToken).ConfigureAwait(false);
        }

        private static async Task<bool> PostAddressChangeDecisionAsync(
            HiatmeAiSettings settings,
            string changeId,
            string action,
            string decidedBy,
            CancellationToken cancellationToken)
        {
            if (settings == null) return false;
            if (string.IsNullOrWhiteSpace(changeId)) return false;
            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl)) return false;

            var url = baseUrl + "/api/hiatme/driver/pending-address-changes/" +
                      Uri.EscapeDataString(changeId) + "/" + action;
            var body = new JObject
            {
                ["client_id"] = settings.ResolvedClientId(),
                ["decided_by"] = decidedBy ?? "",
            };
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Post, url))
                {
                    req.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
                    if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                        req.Headers.Authorization = new AuthenticationHeaderValue(
                            "Bearer", settings.ApiToken.Trim());
                    using (var resp = await SharedHttp.SendAsync(req, cancellationToken).ConfigureAwait(false))
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

        /// <summary>Permanent standing rules from server memory (timing, vans, home routing).</summary>
        public static async Task<List<string>> GetStandingRulesAsync(
            HiatmeAiSettings settings,
            CancellationToken cancellationToken = default)
        {
            var list = new List<string>();
            if (settings == null) return list;
            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl)) return list;
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
                        if (!resp.IsSuccessStatusCode) return list;
                        var root = JObject.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
                        foreach (var item in root["rules"] as JArray ?? new JArray())
                        {
                            var text = item["text"]?.ToString();
                            if (!string.IsNullOrWhiteSpace(text))
                                list.Add(text.Trim());
                        }
                    }
                }
            }
            catch { /* optional */ }
            return list;
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
                    HttpMethod.Get, baseUrl + "/api/hiatme/memory?limit=20"))
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

        /// <summary>GET /api/hiatme/schedules/workbook/meta — existence/etag for a day workbook.</summary>
        public static async Task<HiatmeScheduleWorkbookMeta> GetScheduleWorkbookMetaAsync(
            HiatmeAiSettings settings,
            string serviceDateIso,
            CancellationToken cancellationToken = default)
        {
            if (settings == null) return null;
            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl)) return null;
            string sd = (serviceDateIso ?? "").Trim();
            if (string.IsNullOrEmpty(sd)) return null;
            string url = baseUrl + "/api/hiatme/schedules/workbook/meta?service_date="
                + Uri.EscapeDataString(sd);
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                        req.Headers.Authorization = new AuthenticationHeaderValue(
                            "Bearer", settings.ApiToken.Trim());
                    using (var resp = await SharedHttp.SendAsync(req, cancellationToken)
                        .ConfigureAwait(false))
                    {
                        var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!resp.IsSuccessStatusCode)
                        {
                            try
                            {
                                return JsonConvert.DeserializeObject<HiatmeScheduleWorkbookMeta>(text)
                                    ?? new HiatmeScheduleWorkbookMeta
                                    {
                                        Ok = false,
                                        Exists = false,
                                        Error = "HTTP " + (int)resp.StatusCode,
                                    };
                            }
                            catch
                            {
                                return new HiatmeScheduleWorkbookMeta
                                {
                                    Ok = false,
                                    Exists = false,
                                    Error = "HTTP " + (int)resp.StatusCode,
                                };
                            }
                        }
                        return JsonConvert.DeserializeObject<HiatmeScheduleWorkbookMeta>(text);
                    }
                }
            }
            catch (Exception ex)
            {
                return new HiatmeScheduleWorkbookMeta
                {
                    Ok = false,
                    Exists = false,
                    Error = DescribeRequestError(ex),
                };
            }
        }

        /// <summary>
        /// Download day workbook bytes to <paramref name="destPath"/>.
        /// Returns meta headers when successful.
        /// </summary>
        public static async Task<HiatmeScheduleWorkbookMeta> DownloadScheduleWorkbookAsync(
            HiatmeAiSettings settings,
            string serviceDateIso,
            string destPath,
            CancellationToken cancellationToken = default)
        {
            if (settings == null || string.IsNullOrWhiteSpace(destPath))
                return null;
            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl)) return null;
            string sd = (serviceDateIso ?? "").Trim();
            if (string.IsNullOrEmpty(sd)) return null;
            string url = baseUrl + "/api/hiatme/schedules/workbook?service_date="
                + Uri.EscapeDataString(sd);
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                        req.Headers.Authorization = new AuthenticationHeaderValue(
                            "Bearer", settings.ApiToken.Trim());
                    using (var resp = await SharedHttp.SendAsync(req, cancellationToken)
                        .ConfigureAwait(false))
                    {
                        if (!resp.IsSuccessStatusCode)
                        {
                            return new HiatmeScheduleWorkbookMeta
                            {
                                Ok = false,
                                Exists = false,
                                Error = "HTTP " + (int)resp.StatusCode,
                            };
                        }

                        string dir = Path.GetDirectoryName(destPath);
                        if (!string.IsNullOrEmpty(dir))
                            Directory.CreateDirectory(dir);

                        string tmp = destPath + ".tmp";
                        using (var fs = new FileStream(
                            tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            await resp.Content.CopyToAsync(fs).ConfigureAwait(false);
                        }

                        if (File.Exists(destPath))
                        {
                            try { File.Delete(destPath); }
                            catch { }
                        }
                        File.Move(tmp, destPath);

                        string filename = null;
                        if (resp.Content.Headers.ContentDisposition != null)
                            filename = resp.Content.Headers.ContentDisposition.FileName?.Trim('"');
                        if (string.IsNullOrWhiteSpace(filename)
                            && resp.Headers.TryGetValues("X-Schedule-Filename", out var fnVals))
                            filename = FirstHeader(fnVals);
                        if (string.IsNullOrWhiteSpace(filename))
                            filename = Path.GetFileName(destPath);

                        string etag = null;
                        if (resp.Headers.ETag != null)
                            etag = resp.Headers.ETag.Tag;
                        if (string.IsNullOrWhiteSpace(etag)
                            && resp.Headers.TryGetValues("ETag", out var etVals))
                            etag = FirstHeader(etVals);

                        long? size = null;
                        if (resp.Headers.TryGetValues("X-Schedule-Size", out var szVals)
                            && long.TryParse(FirstHeader(szVals), out var sz))
                            size = sz;
                        double? mtime = null;
                        if (resp.Headers.TryGetValues("X-Schedule-Mtime", out var mtVals)
                            && double.TryParse(
                                FirstHeader(mtVals),
                                NumberStyles.Float,
                                CultureInfo.InvariantCulture,
                                out var mt))
                            mtime = mt;

                        return new HiatmeScheduleWorkbookMeta
                        {
                            Ok = true,
                            Exists = true,
                            Filename = filename,
                            ServiceDate = sd,
                            Etag = etag,
                            Size = size,
                            Mtime = mtime,
                            Source = "server",
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                return new HiatmeScheduleWorkbookMeta
                {
                    Ok = false,
                    Exists = false,
                    Error = DescribeRequestError(ex),
                };
            }
        }

        /// <summary>
        /// PUT /api/hiatme/schedules/workbook — push a saved day workbook to the server mirror.
        /// </summary>
        public static async Task<HiatmeScheduleWorkbookMeta> UploadScheduleWorkbookAsync(
            HiatmeAiSettings settings,
            string serviceDateIso,
            string workbookPath,
            string source = "desk_save",
            CancellationToken cancellationToken = default)
        {
            if (settings == null || string.IsNullOrWhiteSpace(workbookPath))
                return null;
            if (!File.Exists(workbookPath))
            {
                return new HiatmeScheduleWorkbookMeta
                {
                    Ok = false,
                    Exists = false,
                    Error = "workbook file not found",
                };
            }

            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl)) return null;
            string sd = (serviceDateIso ?? "").Trim();
            if (string.IsNullOrEmpty(sd)) return null;

            string url = baseUrl + "/api/hiatme/schedules/workbook?service_date="
                + Uri.EscapeDataString(sd);
            try
            {
                byte[] bytes = File.ReadAllBytes(workbookPath);
                using (var form = new MultipartFormDataContent())
                {
                    var fileContent = new ByteArrayContent(bytes);
                    fileContent.Headers.ContentType = new MediaTypeHeaderValue(
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
                    form.Add(fileContent, "file", Path.GetFileName(workbookPath));

                    using (var req = new HttpRequestMessage(HttpMethod.Put, url))
                    {
                        req.Content = form;
                        if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                            req.Headers.Authorization = new AuthenticationHeaderValue(
                                "Bearer", settings.ApiToken.Trim());

                        double mtime = FileUtcUnixSeconds(workbookPath);
                        req.Headers.TryAddWithoutValidation(
                            "X-Schedule-Client-Mtime",
                            mtime.ToString(CultureInfo.InvariantCulture));
                        if (!string.IsNullOrWhiteSpace(source))
                            req.Headers.TryAddWithoutValidation("X-Schedule-Source", source.Trim());

                        using (var resp = await SharedHttp.SendAsync(req, cancellationToken)
                            .ConfigureAwait(false))
                        {
                            var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                            if (!resp.IsSuccessStatusCode)
                            {
                                try
                                {
                                    return JsonConvert.DeserializeObject<HiatmeScheduleWorkbookMeta>(text)
                                        ?? new HiatmeScheduleWorkbookMeta
                                        {
                                            Ok = false,
                                            Exists = false,
                                            Error = "HTTP " + (int)resp.StatusCode,
                                        };
                                }
                                catch
                                {
                                    return new HiatmeScheduleWorkbookMeta
                                    {
                                        Ok = false,
                                        Exists = false,
                                        Error = "HTTP " + (int)resp.StatusCode,
                                    };
                                }
                            }
                            return JsonConvert.DeserializeObject<HiatmeScheduleWorkbookMeta>(text)
                                ?? new HiatmeScheduleWorkbookMeta { Ok = true, Exists = true };
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return new HiatmeScheduleWorkbookMeta
                {
                    Ok = false,
                    Exists = false,
                    Error = DescribeRequestError(ex),
                };
            }
        }

        /// <summary>Background upload after save — never block the UI thread.</summary>
        public static void UploadScheduleWorkbookFireAndForget(
            HiatmeAiSettings settings,
            string serviceDateIso,
            string workbookPath,
            string source = "desk_save")
        {
            if (settings == null
                || string.IsNullOrWhiteSpace(settings.BaseUrl)
                || string.IsNullOrWhiteSpace(workbookPath)
                || !File.Exists(workbookPath))
                return;

            Task.Run(async () =>
            {
                try
                {
                    await UploadScheduleWorkbookAsync(
                        settings, serviceDateIso, workbookPath, source).ConfigureAwait(false);
                }
                catch
                {
                    // best effort — resolver will retry on next load/save
                }
            });
        }

        private static double FileUtcUnixSeconds(string path)
        {
            var utc = File.GetLastWriteTimeUtc(path);
            return (utc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }

        private static string FirstHeader(IEnumerable<string> values)
        {
            if (values == null) return null;
            foreach (var v in values)
            {
                if (!string.IsNullOrWhiteSpace(v))
                    return v.Trim();
            }
            return null;
        }

        /// <summary>Historical archive status (Desktop mirror + ingested day count).</summary>
        public static async Task<HiatmeArchiveStatusResponse> GetArchiveStatusAsync(
            HiatmeAiSettings settings,
            CancellationToken cancellationToken = default)
        {
            if (settings == null) return null;
            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl)) return null;
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Get, baseUrl + "/api/hiatme/archive/status"))
                {
                    if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                        req.Headers.Authorization = new AuthenticationHeaderValue(
                            "Bearer", settings.ApiToken.Trim());
                    using (var resp = await SharedHttp.SendAsync(req, cancellationToken).ConfigureAwait(false))
                    {
                        if (!resp.IsSuccessStatusCode) return null;
                        var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        return JsonConvert.DeserializeObject<HiatmeArchiveStatusResponse>(text);
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Trigger archive ingest/reindex pass on AI panel.</summary>
        public static async Task<HiatmeArchiveSyncResponse> SyncArchiveAsync(
            HiatmeAiSettings settings,
            bool force = false,
            CancellationToken cancellationToken = default)
        {
            if (settings == null) return null;
            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl)) return null;
            string url = baseUrl + "/api/hiatme/archive/ingest";
            if (force) url += "?force=1";
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Post, url))
                {
                    req.Content = new StringContent("{}", Encoding.UTF8, "application/json");
                    if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                        req.Headers.Authorization = new AuthenticationHeaderValue(
                            "Bearer", settings.ApiToken.Trim());
                    using (var resp = await SharedHttp.SendAsync(req, cancellationToken).ConfigureAwait(false))
                    {
                        if (!resp.IsSuccessStatusCode) return null;
                        var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        return JsonConvert.DeserializeObject<HiatmeArchiveSyncResponse>(text);
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Query historical archive for day/driver/client/trip matches.</summary>
        public static async Task<HiatmeArchiveQueryResponse> QueryArchiveAsync(
            HiatmeAiSettings settings,
            string serviceDate = "",
            string weekday = "",
            string driver = "",
            string client = "",
            string tripNumber = "",
            string puCity = "",
            string doCity = "",
            int limit = 30,
            CancellationToken cancellationToken = default)
        {
            if (settings == null) return null;
            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl)) return null;

            var qs = new StringBuilder();
            AppendQuery(qs, "service_date", serviceDate);
            AppendQuery(qs, "weekday", weekday);
            AppendQuery(qs, "driver", driver);
            AppendQuery(qs, "client", client);
            AppendQuery(qs, "trip_number", tripNumber);
            AppendQuery(qs, "pu_city", puCity);
            AppendQuery(qs, "do_city", doCity);
            AppendQuery(qs, "limit", Math.Max(1, limit).ToString());

            string url = baseUrl + "/api/hiatme/archive/query";
            if (qs.Length > 0)
                url += "?" + qs;

            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                        req.Headers.Authorization = new AuthenticationHeaderValue(
                            "Bearer", settings.ApiToken.Trim());
                    using (var resp = await SharedHttp.SendAsync(req, cancellationToken).ConfigureAwait(false))
                    {
                        if (!resp.IsSuccessStatusCode) return null;
                        var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        return JsonConvert.DeserializeObject<HiatmeArchiveQueryResponse>(text);
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Would this trip be named late on each candidate driver's current route?
        /// Ranking signal only. Does not write a prediction to the ledger.
        /// </summary>
        public static async Task<HiatmeForecastPlacementsResponse> ScoreForecastPlacementsAsync(
            HiatmeAiSettings settings,
            object body,
            CancellationToken cancellationToken = default)
        {
            if (settings == null || body == null) return null;
            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl)) return null;
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/api/hiatme/forecast/placements"))
                {
                    req.Content = new StringContent(
                        JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
                    if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                        req.Headers.Authorization = new AuthenticationHeaderValue(
                            "Bearer", settings.ApiToken.Trim());
                    using (var resp = await SharedHttp.SendAsync(req, cancellationToken).ConfigureAwait(false))
                    {
                        if (!resp.IsSuccessStatusCode) return null;
                        var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        return JsonConvert.DeserializeObject<HiatmeForecastPlacementsResponse>(text);
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        private static void AppendQuery(StringBuilder sb, string key, string value)
        {
            if (sb == null || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                return;
            if (sb.Length > 0)
                sb.Append("&");
            sb.Append(Uri.EscapeDataString(key));
            sb.Append("=");
            sb.Append(Uri.EscapeDataString(value.Trim()));
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

        public static async Task<HiatmeAssistantResponse> SendAssistantAsync(
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

            using (var req = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/api/hiatme/assistant"))
            {
                req.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
                if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiToken.Trim());

                using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(55));
                    using (var resp = await SharedHttp.SendAsync(req, timeoutCts.Token).ConfigureAwait(false))
                    {
                        var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!resp.IsSuccessStatusCode)
                        {
                            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized
                                || resp.StatusCode == System.Net.HttpStatusCode.Forbidden)
                                throw new InvalidOperationException(
                                    "AI panel rejected the API token. Check ApiToken vs HIATME_API_TOKEN.");
                            throw new InvalidOperationException(
                                "AI assistant failed (HTTP " + (int)resp.StatusCode + ").");
                        }
                        return JsonConvert.DeserializeObject<HiatmeAssistantResponse>(text);
                    }
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

                using (var resp = await ScheduleBuildHttp.SendAsync(req, cancellationToken).ConfigureAwait(false))
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

        /// <summary>Deterministic VRP/greedy BUILD — preferred for Supey toolbar BUILD.</summary>
        public static async Task<HiatmeAiBuildResponse> ScheduleSolveAsync(
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

            using (var req = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/api/hiatme/solve"))
            {
                req.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
                if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiToken.Trim());

                using (var resp = await ScheduleBuildHttp.SendAsync(req, cancellationToken).ConfigureAwait(false))
                {
                    var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if ((int)resp.StatusCode == 202)
                    {
                        var accepted = JObject.Parse(text);
                        string jobId = accepted["job_id"]?.ToString();
                        if (string.IsNullOrWhiteSpace(jobId))
                            throw new InvalidOperationException("Server returned 202 without job_id.");
                        return await PollSolveJobAsync(settings, jobId, cancellationToken)
                            .ConfigureAwait(false);
                    }
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
                            "Server solve failed " + (int)resp.StatusCode + ": " + detail);
                    }

                    return ParseSolveResponse(JObject.Parse(text));
                }
            }
        }

        private static HiatmeAiBuildResponse ParseSolveResponse(JObject root)
        {
            var schedTok = root["schedule"] as JObject;
            return new HiatmeAiBuildResponse
            {
                Message = root["message"]?.ToString(),
                TraceId = root["trace_id"]?.ToString(),
                Schedule = schedTok?.ToObject<HiatmeAiScheduleBody>(),
                Solver = root["solver"]?.ToString(),
                BuildStats = (root["build_stats"] as JObject)?.ToObject<HiatmeBuildStats>(),
                Warnings = ParseStringList(root),
                BuildLogLines = ParseStringList(root, "build_log_lines"),
            };
        }

        private static async Task<HiatmeAiBuildResponse> PollSolveJobAsync(
            HiatmeAiSettings settings,
            string jobId,
            CancellationToken cancellationToken)
        {
            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            string url = baseUrl + "/api/hiatme/solve-job/" + Uri.EscapeDataString(jobId);
            for (int i = 0; i < 2400; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                        req.Headers.Authorization = new AuthenticationHeaderValue(
                            "Bearer", settings.ApiToken.Trim());
                    using (var resp = await ScheduleBuildHttp.SendAsync(req, cancellationToken)
                        .ConfigureAwait(false))
                    {
                        var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!resp.IsSuccessStatusCode)
                            throw new InvalidOperationException(
                                "Solve job poll failed " + (int)resp.StatusCode + ": " + text);
                        var root = JObject.Parse(text);
                        string status = root["status"]?.ToString() ?? "";
                        if (string.Equals(status, "done", StringComparison.OrdinalIgnoreCase))
                            return ParseSolveResponse(root);
                        if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException(
                                root["error"]?.ToString() ?? "Server BUILD job failed.");
                    }
                }
                await Task.Delay(1500, cancellationToken).ConfigureAwait(false);
            }
            throw new InvalidOperationException("Server BUILD job did not finish in time.");
        }

        private static List<string> ParseStringList(JToken root, string key = "warnings")
        {
            var arr = root?[key] as JArray;
            if (arr == null) return null;
            var list = new List<string>();
            foreach (var w in arr)
            {
                string s = (w ?? "").ToString().Trim();
                if (!string.IsNullOrEmpty(s)) list.Add(s);
            }
            return list.Count > 0 ? list : null;
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

                using (var resp = await ScheduleBuildHttp.SendAsync(req, cancellationToken).ConfigureAwait(false))
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
                        Thinking = root["thinking"]?.ToString(),
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

        public static async Task<TripScoutServerStatus> GetTripScoutServerStatusAsync(
            HiatmeAiSettings settings,
            string serviceDateIso,
            CancellationToken cancellationToken = default)
        {
            if (settings == null || string.IsNullOrWhiteSpace(serviceDateIso))
                return new TripScoutServerStatus { Ok = false, Error = "settings or date missing" };

            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl))
                return new TripScoutServerStatus { Ok = false, Error = "AI server URL not configured" };

            string url = baseUrl + "/api/hiatme/trips/status?service_date="
                + Uri.EscapeDataString(serviceDateIso.Trim());
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                        req.Headers.Authorization = new AuthenticationHeaderValue(
                            "Bearer", settings.ApiToken.Trim());
                    using (var resp = await SharedHttp.SendAsync(req, cancellationToken)
                        .ConfigureAwait(false))
                    {
                        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!resp.IsSuccessStatusCode)
                            return new TripScoutServerStatus
                            {
                                Ok = false,
                                Error = "HTTP " + (int)resp.StatusCode + ": " + body,
                            };
                        var root = JObject.Parse(body);
                        return new TripScoutServerStatus
                        {
                            Ok = root["ok"]?.Value<bool>() != false,
                            Available = root["available"]?.Value<bool>() == true,
                            ServiceDate = root["service_date"]?.ToString(),
                            TripCount = root["trip_count"]?.Value<int>() ?? 0,
                            ContentHash = root["content_hash"]?.ToString() ?? "",
                            PulledAt = root["pulled_at"]?.Type == JTokenType.Null
                                ? (double?)null
                                : root["pulled_at"]?.Value<double>(),
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                return new TripScoutServerStatus { Ok = false, Error = DescribeRequestError(ex) };
            }
        }

        public static async Task<TripScoutServerTrips> GetTripScoutServerTripsAsync(
            HiatmeAiSettings settings,
            string serviceDateIso,
            CancellationToken cancellationToken = default)
        {
            if (settings == null || string.IsNullOrWhiteSpace(serviceDateIso))
                return new TripScoutServerTrips { Ok = false, Error = "settings or date missing" };

            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl))
                return new TripScoutServerTrips { Ok = false, Error = "AI server URL not configured" };

            string url = baseUrl + "/api/hiatme/trips?service_date="
                + Uri.EscapeDataString(serviceDateIso.Trim());
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                        req.Headers.Authorization = new AuthenticationHeaderValue(
                            "Bearer", settings.ApiToken.Trim());
                    using (var resp = await SharedHttp.SendAsync(req, cancellationToken)
                        .ConfigureAwait(false))
                    {
                        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!resp.IsSuccessStatusCode)
                            return new TripScoutServerTrips
                            {
                                Ok = false,
                                Error = "HTTP " + (int)resp.StatusCode + ": " + body,
                            };
                        return JsonConvert.DeserializeObject<TripScoutServerTrips>(body)
                            ?? new TripScoutServerTrips { Ok = false, Error = "empty response" };
                    }
                }
            }
            catch (Exception ex)
            {
                return new TripScoutServerTrips { Ok = false, Error = DescribeRequestError(ex) };
            }
        }

        public static async Task<WellRydeBellStatus> GetWellRydeBellStatusAsync(
            HiatmeAiSettings settings,
            CancellationToken cancellationToken = default)
        {
            if (settings == null)
                return new WellRydeBellStatus { Ok = false, Error = "settings missing" };

            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl))
                return new WellRydeBellStatus { Ok = false, Error = "AI server URL not configured" };

            string url = baseUrl + "/api/hiatme/wellryde/bell/status";
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                        req.Headers.Authorization = new AuthenticationHeaderValue(
                            "Bearer", settings.ApiToken.Trim());
                    using (var resp = await SharedHttp.SendAsync(req, cancellationToken)
                        .ConfigureAwait(false))
                    {
                        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!resp.IsSuccessStatusCode)
                            return new WellRydeBellStatus
                            {
                                Ok = false,
                                Error = "HTTP " + (int)resp.StatusCode + ": " + body,
                            };
                        return JsonConvert.DeserializeObject<WellRydeBellStatus>(body)
                            ?? new WellRydeBellStatus { Ok = false, Error = "empty response" };
                    }
                }
            }
            catch (Exception ex)
            {
                return new WellRydeBellStatus { Ok = false, Error = DescribeRequestError(ex) };
            }
        }

        public static async Task<TripScoutDayChanges> GetTripScoutDayChangesAsync(
            HiatmeAiSettings settings,
            string serviceDateIso,
            string tripNo = null,
            CancellationToken cancellationToken = default)
        {
            if (settings == null || string.IsNullOrWhiteSpace(serviceDateIso))
                return new TripScoutDayChanges { Ok = false, Error = "settings or date missing" };

            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl))
                return new TripScoutDayChanges { Ok = false, Error = "AI server URL not configured" };

            string url = baseUrl + "/api/hiatme/trips/changes/scout?service_date="
                + Uri.EscapeDataString(serviceDateIso.Trim());
            if (!string.IsNullOrWhiteSpace(tripNo))
                url += "&trip_no=" + Uri.EscapeDataString(tripNo.Trim());

            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                        req.Headers.Authorization = new AuthenticationHeaderValue(
                            "Bearer", settings.ApiToken.Trim());
                    using (var resp = await SharedHttp.SendAsync(req, cancellationToken)
                        .ConfigureAwait(false))
                    {
                        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!resp.IsSuccessStatusCode)
                            return new TripScoutDayChanges
                            {
                                Ok = false,
                                Error = "HTTP " + (int)resp.StatusCode + ": " + body,
                            };
                        return JsonConvert.DeserializeObject<TripScoutDayChanges>(body)
                            ?? new TripScoutDayChanges { Ok = false, Error = "empty response" };
                    }
                }
            }
            catch (Exception ex)
            {
                return new TripScoutDayChanges { Ok = false, Error = DescribeRequestError(ex) };
            }
        }

        /// <summary>POST /api/hiatme/trips/changes/scout/simulate — dev journal inject.</summary>
        public static async Task<TripScoutDayChanges> SimulateTripScoutChangeAsync(
            HiatmeAiSettings settings,
            string serviceDateIso,
            string tripNo,
            string scenario,
            string client = null,
            string driver = null,
            CancellationToken cancellationToken = default)
        {
            if (settings == null || string.IsNullOrWhiteSpace(serviceDateIso))
                return new TripScoutDayChanges { Ok = false, Error = "settings or date missing" };

            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl))
                return new TripScoutDayChanges { Ok = false, Error = "AI server URL not configured" };

            var q = new List<string>
            {
                "service_date=" + Uri.EscapeDataString(serviceDateIso.Trim()),
            };
            if (!string.IsNullOrWhiteSpace(tripNo))
                q.Add("trip_no=" + Uri.EscapeDataString(tripNo.Trim()));
            if (!string.IsNullOrWhiteSpace(scenario))
                q.Add("scenario=" + Uri.EscapeDataString(scenario.Trim()));
            if (!string.IsNullOrWhiteSpace(client))
                q.Add("client=" + Uri.EscapeDataString(client.Trim()));
            if (!string.IsNullOrWhiteSpace(driver))
                q.Add("driver=" + Uri.EscapeDataString(driver.Trim()));

            string url = baseUrl + "/api/hiatme/trips/changes/scout/simulate?" + string.Join("&", q);

            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Post, url))
                {
                    if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                        req.Headers.Authorization = new AuthenticationHeaderValue(
                            "Bearer", settings.ApiToken.Trim());
                    using (var resp = await SharedHttp.SendAsync(req, cancellationToken)
                        .ConfigureAwait(false))
                    {
                        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!resp.IsSuccessStatusCode)
                            return new TripScoutDayChanges
                            {
                                Ok = false,
                                Error = "HTTP " + (int)resp.StatusCode + ": " + body,
                            };
                        return JsonConvert.DeserializeObject<TripScoutDayChanges>(body)
                            ?? new TripScoutDayChanges { Ok = false, Error = "empty response" };
                    }
                }
            }
            catch (Exception ex)
            {
                return new TripScoutDayChanges { Ok = false, Error = DescribeRequestError(ex) };
            }
        }

        // ── Driver Habits (API path /driver-habits; late-drivers kept as alias) ─

        public sealed class LateDriversStatus
        {
            public bool Ok { get; set; }
            public bool Available { get; set; }

            [JsonProperty("service_date")]
            public string ServiceDate { get; set; }

            [JsonProperty("open_count")]
            public int OpenCount { get; set; }

            [JsonProperty("event_count")]
            public int EventCount { get; set; }

            [JsonProperty("content_hash")]
            public string ContentHash { get; set; }

            public string Error { get; set; }

            [JsonProperty("modivcare_exists")]
            public bool ModivcareExists { get; set; }

            [JsonProperty("modivcare_trip_count")]
            public int ModivcareTripCount { get; set; }
        }

        public sealed class LateDriversEventRow
        {
            [JsonProperty("event_id")]
            public string EventId { get; set; }

            [JsonProperty("service_date")]
            public string ServiceDate { get; set; }

            [JsonProperty("trip_no")]
            public string TripNo { get; set; }

            public string Side { get; set; }
            public string Driver { get; set; }
            public string Client { get; set; }

            [JsonProperty("sched_iso")]
            public string SchedIso { get; set; }

            [JsonProperty("actual_iso")]
            public string ActualIso { get; set; }

            // Per-leg clocks. On ticket rows (Unfinished / Billed too soon) SchedIso is
            // the DROP-OFF and ActualIso the PICK-UP, so the grid must use these to
            // place each leg in its own column instead of showing a scheduled
            // drop-off under "Sched PU".
            [JsonProperty("sched_pu_iso")]
            public string SchedPuIso { get; set; }

            [JsonProperty("sched_do_iso")]
            public string SchedDoIso { get; set; }

            [JsonProperty("actual_pu_iso")]
            public string ActualPuIso { get; set; }

            [JsonProperty("actual_do_iso")]
            public string ActualDoIso { get; set; }

            /// <summary>Modivcare marks will-calls with a 00:00 pickup, so an empty
            /// SchedPuIso here means "not called in yet" rather than a missing schedule.</summary>
            [JsonProperty("will_call")]
            public bool WillCall { get; set; }

            [JsonProperty("grace_minutes")]
            public int GraceMinutes { get; set; }

            [JsonProperty("detected_at")]
            public double? DetectedAt { get; set; }

            [JsonProperty("resolved_at")]
            public double? ResolvedAt { get; set; }

            public bool Open { get; set; }
            public bool Excluded { get; set; }

            [JsonProperty("minutes_late")]
            public double MinutesLate { get; set; }

            [JsonProperty("status_latest")]
            public string StatusLatest { get; set; }

            [JsonProperty("statuses_seen")]
            public List<string> StatusesSeen { get; set; }

            // Habit kind when row is sourced from /habits (late_pu, early_do, unfinished_ticket, …)
            public string Habit { get; set; }
            public string Kind { get; set; }
        }

        public sealed class LateDriversDayPerformance
        {
            [JsonProperty("service_date")]
            public string ServiceDate { get; set; }

            public int Scored { get; set; }

            [JsonProperty("on_time")]
            public int OnTime { get; set; }

            public int Late { get; set; }
            public int Pending { get; set; }
            public int Excluded { get; set; }
            public double? Pct { get; set; }

            [JsonProperty("wellryde_trips")]
            public int WellrydeTrips { get; set; }

            [JsonProperty("modivcare_trips")]
            public int ModivcareTrips { get; set; }
        }

        public sealed class LateDriversLiveDoc
        {
            public bool Ok { get; set; }
            public string Mode { get; set; }

            [JsonProperty("service_date")]
            public string ServiceDate { get; set; }

            public int Count { get; set; }

            [JsonProperty("open_count")]
            public int OpenCount { get; set; }

            [JsonProperty("content_hash")]
            public string ContentHash { get; set; }

            public List<LateDriversEventRow> Events { get; set; }
            public string Error { get; set; }

            [JsonProperty("modivcare_exists")]
            public bool ModivcareExists { get; set; }

            [JsonProperty("modivcare_trip_count")]
            public int ModivcareTripCount { get; set; }

            [JsonProperty("day_performance")]
            public LateDriversDayPerformance DayPerformance { get; set; }
        }

        public sealed class LateDriversDayDoc
        {
            public bool Ok { get; set; }
            public string Mode { get; set; }

            [JsonProperty("service_date")]
            public string ServiceDate { get; set; }

            public int Count { get; set; }

            [JsonProperty("open_count")]
            public int OpenCount { get; set; }

            [JsonProperty("content_hash")]
            public string ContentHash { get; set; }

            public List<LateDriversEventRow> Events { get; set; }
            public string Error { get; set; }

            [JsonProperty("modivcare_exists")]
            public bool ModivcareExists { get; set; }

            [JsonProperty("modivcare_trip_count")]
            public int ModivcareTripCount { get; set; }

            [JsonProperty("day_performance")]
            public LateDriversDayPerformance DayPerformance { get; set; }
        }

        public sealed class LateDriversDriverSummary
        {
            public string Driver { get; set; }

            [JsonProperty("late_count")]
            public int LateCount { get; set; }

            [JsonProperty("pu_count")]
            public int PuCount { get; set; }

            [JsonProperty("do_count")]
            public int DoCount { get; set; }

            [JsonProperty("open_count")]
            public int OpenCount { get; set; }

            [JsonProperty("total_minutes")]
            public double TotalMinutes { get; set; }

            // Habit rollup (merged client-side from /driver-habits)
            [JsonProperty("early_pu")]
            public int EarlyPu { get; set; }

            [JsonProperty("early_do")]
            public int EarlyDo { get; set; }

            [JsonProperty("early_count")]
            public int EarlyCount { get; set; }

            public int Unfinished { get; set; }

            [JsonProperty("unfinished_open")]
            public int UnfinishedOpen { get; set; }

            public List<LateDriversEventRow> Trips { get; set; }
        }

        public sealed class LateDriversPeriodDoc
        {
            public bool Ok { get; set; }
            public string Mode { get; set; }

            [JsonProperty("from_date")]
            public string FromDate { get; set; }

            [JsonProperty("to_date")]
            public string ToDate { get; set; }

            [JsonProperty("driver_count")]
            public int DriverCount { get; set; }

            [JsonProperty("event_count")]
            public int EventCount { get; set; }

            [JsonProperty("content_hash")]
            public string ContentHash { get; set; }

            public List<LateDriversDriverSummary> Drivers { get; set; }
            public List<LateDriversEventRow> Events { get; set; }
            public string Error { get; set; }
        }

        public static async Task<LateDriversStatus> GetLateDriversStatusAsync(
            HiatmeAiSettings settings,
            string serviceDateIso,
            CancellationToken cancellationToken = default)
        {
            if (settings == null)
                return new LateDriversStatus { Ok = false, Error = "settings missing" };

            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl))
                return new LateDriversStatus { Ok = false, Error = "AI server URL not configured" };

            string url = baseUrl + "/api/hiatme/driver-habits/status";
            if (!string.IsNullOrWhiteSpace(serviceDateIso))
                url += "?service_date=" + Uri.EscapeDataString(serviceDateIso.Trim());

            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                        req.Headers.Authorization = new AuthenticationHeaderValue(
                            "Bearer", settings.ApiToken.Trim());
                    using (var resp = await SharedHttp.SendAsync(req, cancellationToken)
                        .ConfigureAwait(false))
                    {
                        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!resp.IsSuccessStatusCode)
                            return new LateDriversStatus
                            {
                                Ok = false,
                                Error = "HTTP " + (int)resp.StatusCode + ": " + body,
                            };
                        return JsonConvert.DeserializeObject<LateDriversStatus>(body)
                            ?? new LateDriversStatus { Ok = false, Error = "empty response" };
                    }
                }
            }
            catch (Exception ex)
            {
                return new LateDriversStatus { Ok = false, Error = DescribeRequestError(ex) };
            }
        }

        public static async Task<LateDriversLiveDoc> GetLateDriversLiveAsync(
            HiatmeAiSettings settings,
            string serviceDateIso,
            CancellationToken cancellationToken = default)
        {
            if (settings == null)
                return new LateDriversLiveDoc { Ok = false, Error = "settings missing" };

            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl))
                return new LateDriversLiveDoc { Ok = false, Error = "AI server URL not configured" };

            string url = baseUrl + "/api/hiatme/driver-habits/live";
            if (!string.IsNullOrWhiteSpace(serviceDateIso))
                url += "?service_date=" + Uri.EscapeDataString(serviceDateIso.Trim());

            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                        req.Headers.Authorization = new AuthenticationHeaderValue(
                            "Bearer", settings.ApiToken.Trim());
                    using (var resp = await SharedHttp.SendAsync(req, cancellationToken)
                        .ConfigureAwait(false))
                    {
                        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!resp.IsSuccessStatusCode)
                            return new LateDriversLiveDoc
                            {
                                Ok = false,
                                Error = "HTTP " + (int)resp.StatusCode + ": " + body,
                            };
                        return JsonConvert.DeserializeObject<LateDriversLiveDoc>(body)
                            ?? new LateDriversLiveDoc { Ok = false, Error = "empty response" };
                    }
                }
            }
            catch (Exception ex)
            {
                return new LateDriversLiveDoc { Ok = false, Error = DescribeRequestError(ex) };
            }
        }

        public static async Task<LateDriversDayDoc> GetLateDriversDayAsync(
            HiatmeAiSettings settings,
            string serviceDateIso,
            CancellationToken cancellationToken = default)
        {
            if (settings == null || string.IsNullOrWhiteSpace(serviceDateIso))
                return new LateDriversDayDoc { Ok = false, Error = "settings or date missing" };

            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl))
                return new LateDriversDayDoc { Ok = false, Error = "AI server URL not configured" };

            string url = baseUrl + "/api/hiatme/driver-habits/day?service_date="
                + Uri.EscapeDataString(serviceDateIso.Trim());

            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                        req.Headers.Authorization = new AuthenticationHeaderValue(
                            "Bearer", settings.ApiToken.Trim());
                    using (var resp = await SharedHttp.SendAsync(req, cancellationToken)
                        .ConfigureAwait(false))
                    {
                        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!resp.IsSuccessStatusCode)
                            return new LateDriversDayDoc
                            {
                                Ok = false,
                                Error = "HTTP " + (int)resp.StatusCode + ": " + body,
                            };
                        return JsonConvert.DeserializeObject<LateDriversDayDoc>(body)
                            ?? new LateDriversDayDoc { Ok = false, Error = "empty response" };
                    }
                }
            }
            catch (Exception ex)
            {
                return new LateDriversDayDoc { Ok = false, Error = DescribeRequestError(ex) };
            }
        }

        public static async Task<LateDriversPeriodDoc> GetLateDriversPeriodAsync(
            HiatmeAiSettings settings,
            string period,
            string serviceDateIso = null,
            string driver = null,
            string fromDateIso = null,
            string toDateIso = null,
            CancellationToken cancellationToken = default)
        {
            if (settings == null)
                return new LateDriversPeriodDoc { Ok = false, Error = "settings missing" };

            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl))
                return new LateDriversPeriodDoc { Ok = false, Error = "AI server URL not configured" };

            var q = new List<string>
            {
                "period=" + Uri.EscapeDataString((period ?? "week").Trim()),
            };
            if (!string.IsNullOrWhiteSpace(serviceDateIso))
                q.Add("service_date=" + Uri.EscapeDataString(serviceDateIso.Trim()));
            if (!string.IsNullOrWhiteSpace(driver))
                q.Add("driver=" + Uri.EscapeDataString(driver.Trim()));
            if (!string.IsNullOrWhiteSpace(fromDateIso))
                q.Add("from_date=" + Uri.EscapeDataString(fromDateIso.Trim()));
            if (!string.IsNullOrWhiteSpace(toDateIso))
                q.Add("to_date=" + Uri.EscapeDataString(toDateIso.Trim()));

            string url = baseUrl + "/api/hiatme/driver-habits/period?" + string.Join("&", q);

            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                        req.Headers.Authorization = new AuthenticationHeaderValue(
                            "Bearer", settings.ApiToken.Trim());
                    using (var resp = await SharedHttp.SendAsync(req, cancellationToken)
                        .ConfigureAwait(false))
                    {
                        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!resp.IsSuccessStatusCode)
                            return new LateDriversPeriodDoc
                            {
                                Ok = false,
                                Error = "HTTP " + (int)resp.StatusCode + ": " + body,
                            };
                        return JsonConvert.DeserializeObject<LateDriversPeriodDoc>(body)
                            ?? new LateDriversPeriodDoc { Ok = false, Error = "empty response" };
                    }
                }
            }
            catch (Exception ex)
            {
                return new LateDriversPeriodDoc { Ok = false, Error = DescribeRequestError(ex) };
            }
        }

        // ── Driver Habits scorecard (early/late + unfinished + billed skip) ─

        public sealed class LateDriversHabitEventRow
        {
            [JsonProperty("event_id")]
            public string EventId { get; set; }

            [JsonProperty("service_date")]
            public string ServiceDate { get; set; }

            [JsonProperty("trip_no")]
            public string TripNo { get; set; }

            public string Driver { get; set; }
            public string Client { get; set; }
            public string Kind { get; set; }
            public string Habit { get; set; }
            public string Side { get; set; }

            public double Minutes { get; set; }

            [JsonProperty("sched_iso")]
            public string SchedIso { get; set; }

            [JsonProperty("actual_iso")]
            public string ActualIso { get; set; }

            public string Status { get; set; }
            public bool Open { get; set; }

            [JsonProperty("detected_at")]
            public double? DetectedAt { get; set; }

            [JsonProperty("resolved_at")]
            public double? ResolvedAt { get; set; }

            public List<string> Tags { get; set; }
        }

        public sealed class LateDriversHabitDriverSummary
        {
            public string Driver { get; set; }

            [JsonProperty("late_pu")]
            public int LatePu { get; set; }

            [JsonProperty("late_do")]
            public int LateDo { get; set; }

            [JsonProperty("early_pu")]
            public int EarlyPu { get; set; }

            [JsonProperty("early_do")]
            public int EarlyDo { get; set; }

            public int Unfinished { get; set; }

            [JsonProperty("unfinished_open")]
            public int UnfinishedOpen { get; set; }

            [JsonProperty("late_count")]
            public int LateCount { get; set; }

            [JsonProperty("early_count")]
            public int EarlyCount { get; set; }

            [JsonProperty("late_minutes")]
            public double LateMinutes { get; set; }

            [JsonProperty("early_minutes")]
            public double EarlyMinutes { get; set; }

            [JsonProperty("event_count")]
            public int EventCount { get; set; }

            [JsonProperty("trip_count")]
            public int TripCount { get; set; }
        }

        public sealed class LateDriversHabitsDoc
        {
            public bool Ok { get; set; }
            public string Mode { get; set; }

            [JsonProperty("from_date")]
            public string FromDate { get; set; }

            [JsonProperty("to_date")]
            public string ToDate { get; set; }

            [JsonProperty("driver_count")]
            public int DriverCount { get; set; }

            [JsonProperty("event_count")]
            public int EventCount { get; set; }

            [JsonProperty("content_hash")]
            public string ContentHash { get; set; }

            public List<LateDriversHabitDriverSummary> Drivers { get; set; }
            public List<LateDriversHabitEventRow> Events { get; set; }
            public string Error { get; set; }
        }

        public static async Task<LateDriversHabitsDoc> GetLateDriversHabitsAsync(
            HiatmeAiSettings settings,
            string period,
            string serviceDateIso = null,
            string driver = null,
            string fromDateIso = null,
            string toDateIso = null,
            CancellationToken cancellationToken = default)
        {
            if (settings == null)
                return new LateDriversHabitsDoc { Ok = false, Error = "settings missing" };

            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl))
                return new LateDriversHabitsDoc { Ok = false, Error = "AI server URL not configured" };

            var q = new List<string>
            {
                "period=" + Uri.EscapeDataString((period ?? "day").Trim()),
            };
            if (!string.IsNullOrWhiteSpace(serviceDateIso))
                q.Add("service_date=" + Uri.EscapeDataString(serviceDateIso.Trim()));
            if (!string.IsNullOrWhiteSpace(driver))
                q.Add("driver=" + Uri.EscapeDataString(driver.Trim()));
            if (!string.IsNullOrWhiteSpace(fromDateIso))
                q.Add("from_date=" + Uri.EscapeDataString(fromDateIso.Trim()));
            if (!string.IsNullOrWhiteSpace(toDateIso))
                q.Add("to_date=" + Uri.EscapeDataString(toDateIso.Trim()));

            string url = baseUrl + "/api/hiatme/driver-habits?" + string.Join("&", q);

            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                        req.Headers.Authorization = new AuthenticationHeaderValue(
                            "Bearer", settings.ApiToken.Trim());
                    using (var resp = await SharedHttp.SendAsync(req, cancellationToken)
                        .ConfigureAwait(false))
                    {
                        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!resp.IsSuccessStatusCode)
                            return new LateDriversHabitsDoc
                            {
                                Ok = false,
                                Error = "HTTP " + (int)resp.StatusCode + ": " + body,
                            };
                        return JsonConvert.DeserializeObject<LateDriversHabitsDoc>(body)
                            ?? new LateDriversHabitsDoc { Ok = false, Error = "empty response" };
                    }
                }
            }
            catch (Exception ex)
            {
                return new LateDriversHabitsDoc { Ok = false, Error = DescribeRequestError(ex) };
            }
        }

        // ── Driver Habits day performance review (popup / EOD preview) ────

        public sealed class DriverHabitsReviewSummary
        {
            [JsonProperty("late_pu")]
            public int LatePu { get; set; }

            [JsonProperty("late_do")]
            public int LateDo { get; set; }

            [JsonProperty("early_pu")]
            public int EarlyPu { get; set; }

            [JsonProperty("early_do")]
            public int EarlyDo { get; set; }

            public int Unfinished { get; set; }

            [JsonProperty("unfinished_open")]
            public int UnfinishedOpen { get; set; }

            [JsonProperty("late_count")]
            public int LateCount { get; set; }

            [JsonProperty("early_count")]
            public int EarlyCount { get; set; }

            [JsonProperty("late_minutes")]
            public double LateMinutes { get; set; }

            [JsonProperty("early_minutes")]
            public double EarlyMinutes { get; set; }

            [JsonProperty("event_count")]
            public int EventCount { get; set; }

            [JsonProperty("trip_count")]
            public int TripCount { get; set; }

            [JsonProperty("admin_count")]
            public int AdminCount { get; set; }
        }

        public sealed class DriverHabitsReviewTrip
        {
            [JsonProperty("trip_no")]
            public string TripNo { get; set; }

            public string Client { get; set; }
            public string Habit { get; set; }

            [JsonProperty("habit_label")]
            public string HabitLabel { get; set; }

            public string Side { get; set; }
            public double Minutes { get; set; }
            public bool Open { get; set; }

            [JsonProperty("sched_time")]
            public string SchedTime { get; set; }

            [JsonProperty("actual_time")]
            public string ActualTime { get; set; }

            public string Status { get; set; }
            public string Note { get; set; }
        }

        public sealed class DriverHabitsReviewDoc
        {
            public bool Ok { get; set; }

            [JsonProperty("service_date")]
            public string ServiceDate { get; set; }

            [JsonProperty("date_label")]
            public string DateLabel { get; set; }

            public string Driver { get; set; }
            public string Headline { get; set; }
            public string Tone { get; set; }

            [JsonProperty("status_label")]
            public string StatusLabel { get; set; }

            [JsonProperty("total_issues")]
            public int TotalIssues { get; set; }

            [JsonProperty("email_intro")]
            public string EmailIntro { get; set; }

            [JsonProperty("preview_blurb")]
            public string PreviewBlurb { get; set; }

            public DriverHabitsReviewSummary Summary { get; set; }
            public List<DriverHabitsReviewTrip> Improve { get; set; }
            public string Error { get; set; }
        }

        public static async Task<DriverHabitsReviewDoc> GetDriverHabitsReviewAsync(
            HiatmeAiSettings settings,
            string serviceDateIso,
            string driver,
            CancellationToken cancellationToken = default)
        {
            if (settings == null)
                return new DriverHabitsReviewDoc { Ok = false, Error = "settings missing" };
            if (string.IsNullOrWhiteSpace(serviceDateIso))
                return new DriverHabitsReviewDoc { Ok = false, Error = "service_date required" };
            if (string.IsNullOrWhiteSpace(driver))
                return new DriverHabitsReviewDoc { Ok = false, Error = "driver required" };

            // Prefer the resolved session URL, then fall through configured backups.
            // A dead office/public host must not sit on SharedHttp's 130s timeout —
            // that is exactly what made the performance-review button look stuck.
            var bases = new List<string>();
            void addBase(string u)
            {
                u = (u ?? "").Trim().TrimEnd('/');
                if (string.IsNullOrEmpty(u)) return;
                if (!bases.Any(b => string.Equals(b, u, StringComparison.OrdinalIgnoreCase)))
                    bases.Add(u);
            }
            addBase(settings.BaseUrl);
            addBase(settings.LastResolvedBaseUrl);
            if (settings.FallbackBaseUrls != null)
            {
                foreach (var u in settings.FallbackBaseUrls)
                    addBase(u);
            }
            addBase("http://127.0.0.1:" + HiatmeAiSettings.DefaultPort);
            if (bases.Count == 0)
                return new DriverHabitsReviewDoc { Ok = false, Error = "AI server URL not configured" };

            string path = "/api/hiatme/driver-habits/review?service_date="
                + Uri.EscapeDataString(serviceDateIso.Trim())
                + "&driver=" + Uri.EscapeDataString(driver.Trim());

            var errors = new List<string>();
            foreach (var baseUrl in bases)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string url = baseUrl + path;
                try
                {
                    // Hard ceiling per host — fail over instead of spinning the status bar.
                    using (var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                    {
                        attemptCts.CancelAfter(TimeSpan.FromSeconds(8));
                        using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                        {
                            if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                                req.Headers.Authorization = new AuthenticationHeaderValue(
                                    "Bearer", settings.ApiToken.Trim());
                            using (var resp = await SharedHttp.SendAsync(req, attemptCts.Token)
                                .ConfigureAwait(false))
                            {
                                var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                                if (!resp.IsSuccessStatusCode)
                                {
                                    errors.Add(baseUrl + " → HTTP " + (int)resp.StatusCode);
                                    continue;
                                }
                                var doc = JsonConvert.DeserializeObject<DriverHabitsReviewDoc>(body)
                                    ?? new DriverHabitsReviewDoc { Ok = false, Error = "empty response" };
                                if (!doc.Ok)
                                {
                                    errors.Add(baseUrl + " → " + (doc.Error ?? "not ok"));
                                    continue;
                                }
                                // Stick the winner so the next click does not re-probe dead hosts first.
                                if (!string.Equals(settings.BaseUrl, baseUrl, StringComparison.OrdinalIgnoreCase))
                                    settings.BaseUrl = baseUrl;
                                return doc;
                            }
                        }
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    errors.Add(baseUrl + " → timed out");
                }
                catch (Exception ex)
                {
                    errors.Add(baseUrl + " → " + DescribeRequestError(ex, baseUrl));
                }
            }

            return new DriverHabitsReviewDoc
            {
                Ok = false,
                Error = "Could not reach AI panel for review. Tried: "
                    + string.Join("; ", errors),
            };
        }

        // ── Modivcare day snapshot (Driver Habits sched source) ───────────

        public sealed class ModivcareDayStatus
        {
            public bool Ok { get; set; }
            public bool Exists { get; set; }

            [JsonProperty("service_date")]
            public string ServiceDate { get; set; }

            [JsonProperty("trip_count")]
            public int TripCount { get; set; }

            [JsonProperty("content_hash")]
            public string ContentHash { get; set; }

            [JsonProperty("updated_at")]
            public double? UpdatedAt { get; set; }

            public string Source { get; set; }
            public string Error { get; set; }

            [JsonProperty("trips")]
            public List<ModivcareDayTripRow> Trips { get; set; }
        }

        public sealed class ModivcareDayTripRow
        {
            [JsonProperty("trip_number")]
            public string TripNumber { get; set; }

            [JsonProperty("pu_time")]
            public string PuTime { get; set; }

            [JsonProperty("do_time")]
            public string DoTime { get; set; }

            [JsonProperty("sched_do_time")]
            public string SchedDoTime { get; set; }

            public string Client { get; set; }
            public string Driver { get; set; }
        }

        public static async Task<ModivcareDayStatus> GetModivcareDayStatusAsync(
            HiatmeAiSettings settings,
            string serviceDateIso,
            CancellationToken cancellationToken = default)
        {
            return await GetModivcareDayStatusAsync(
                    settings, serviceDateIso, includeTrips: false, cancellationToken)
                .ConfigureAwait(false);
        }

        public static async Task<ModivcareDayStatus> GetModivcareDayStatusAsync(
            HiatmeAiSettings settings,
            string serviceDateIso,
            bool includeTrips,
            CancellationToken cancellationToken = default)
        {
            if (settings == null)
                return new ModivcareDayStatus { Ok = false, Error = "settings missing" };

            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl))
                return new ModivcareDayStatus { Ok = false, Error = "AI server URL not configured" };

            string url = baseUrl + "/api/hiatme/modivcare/day";
            var qs = new List<string>();
            if (!string.IsNullOrWhiteSpace(serviceDateIso))
                qs.Add("service_date=" + Uri.EscapeDataString(serviceDateIso.Trim()));
            if (includeTrips)
                qs.Add("include_trips=1");
            if (qs.Count > 0)
                url += "?" + string.Join("&", qs);

            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                        req.Headers.Authorization = new AuthenticationHeaderValue(
                            "Bearer", settings.ApiToken.Trim());
                    using (var resp = await SharedHttp.SendAsync(req, cancellationToken)
                        .ConfigureAwait(false))
                    {
                        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!resp.IsSuccessStatusCode)
                            return new ModivcareDayStatus
                            {
                                Ok = false,
                                Error = "HTTP " + (int)resp.StatusCode + ": " + body,
                            };
                        return JsonConvert.DeserializeObject<ModivcareDayStatus>(body)
                            ?? new ModivcareDayStatus { Ok = false, Error = "empty response" };
                    }
                }
            }
            catch (Exception ex)
            {
                return new ModivcareDayStatus { Ok = false, Error = DescribeRequestError(ex) };
            }
        }

        public static async Task<ModivcareDayStatus> PutModivcareDayAsync(
            HiatmeAiSettings settings,
            string serviceDateIso,
            IList<ModivcareDayTripRow> trips,
            string source = "",
            CancellationToken cancellationToken = default)
        {
            if (settings == null)
                return new ModivcareDayStatus { Ok = false, Error = "settings missing" };
            if (string.IsNullOrWhiteSpace(serviceDateIso))
                return new ModivcareDayStatus { Ok = false, Error = "service_date required" };

            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl))
                return new ModivcareDayStatus { Ok = false, Error = "AI server URL not configured" };

            string url = baseUrl + "/api/hiatme/modivcare/day";
            var payload = new
            {
                service_date = serviceDateIso.Trim(),
                source = source ?? "",
                trips = trips ?? new List<ModivcareDayTripRow>(),
            };

            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Put, url))
                {
                    if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                        req.Headers.Authorization = new AuthenticationHeaderValue(
                            "Bearer", settings.ApiToken.Trim());
                    req.Content = new StringContent(
                        JsonConvert.SerializeObject(payload),
                        Encoding.UTF8,
                        "application/json");
                    using (var resp = await SharedHttp.SendAsync(req, cancellationToken)
                        .ConfigureAwait(false))
                    {
                        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!resp.IsSuccessStatusCode)
                            return new ModivcareDayStatus
                            {
                                Ok = false,
                                Error = "HTTP " + (int)resp.StatusCode + ": " + body,
                            };
                        return JsonConvert.DeserializeObject<ModivcareDayStatus>(body)
                            ?? new ModivcareDayStatus { Ok = false, Error = "empty response" };
                    }
                }
            }
            catch (Exception ex)
            {
                return new ModivcareDayStatus { Ok = false, Error = DescribeRequestError(ex) };
            }
        }

        public static List<ModivcareDayTripRow> ModivcareDayTripsFromDownloaded(
            IEnumerable<MCDownloadedTrip> downloaded)
        {
            var rows = new List<ModivcareDayTripRow>();
            if (downloaded == null)
                return rows;
            foreach (MCDownloadedTrip t in downloaded)
            {
                if (t == null || string.IsNullOrWhiteSpace(t.TripNumber))
                    continue;
                rows.Add(new ModivcareDayTripRow
                {
                    TripNumber = t.TripNumber.Trim(),
                    PuTime = t.PUTime ?? "",
                    DoTime = t.DOTime ?? "",
                    SchedDoTime = t.SchedDOTime ?? "",
                    Client = t.ClientFullName ?? "",
                    Driver = t.DriverNameParsed ?? "",
                });
            }
            return rows;
        }

        // ── Workbook schedule-assign (timing blame ownership) ─────────────

        public sealed class ScheduleAssignDayStatus
        {
            public bool Ok { get; set; }
            public bool Exists { get; set; }

            [JsonProperty("service_date")]
            public string ServiceDate { get; set; }

            [JsonProperty("trip_count")]
            public int TripCount { get; set; }

            [JsonProperty("content_hash")]
            public string ContentHash { get; set; }

            [JsonProperty("updated_at")]
            public double? UpdatedAt { get; set; }

            public bool Unchanged { get; set; }
            public string Source { get; set; }
            public string Error { get; set; }
        }

        public sealed class ScheduleAssignTripRow
        {
            [JsonProperty("trip_number")]
            public string TripNumber { get; set; }

            public string Driver { get; set; }
        }

        public static async Task<ScheduleAssignDayStatus> PutScheduleAssignDayAsync(
            HiatmeAiSettings settings,
            string serviceDateIso,
            IList<ScheduleAssignTripRow> trips,
            string source = "",
            CancellationToken cancellationToken = default)
        {
            if (settings == null)
                return new ScheduleAssignDayStatus { Ok = false, Error = "settings missing" };
            if (string.IsNullOrWhiteSpace(serviceDateIso))
                return new ScheduleAssignDayStatus { Ok = false, Error = "service_date required" };

            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl))
                return new ScheduleAssignDayStatus { Ok = false, Error = "AI server URL not configured" };

            string url = baseUrl + "/api/hiatme/schedule-assign/day";
            var payload = new
            {
                service_date = serviceDateIso.Trim(),
                source = source ?? "",
                trips = trips ?? new List<ScheduleAssignTripRow>(),
            };

            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Put, url))
                {
                    if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                        req.Headers.Authorization = new AuthenticationHeaderValue(
                            "Bearer", settings.ApiToken.Trim());
                    req.Content = new StringContent(
                        JsonConvert.SerializeObject(payload),
                        Encoding.UTF8,
                        "application/json");
                    using (var resp = await SharedHttp.SendAsync(req, cancellationToken)
                        .ConfigureAwait(false))
                    {
                        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!resp.IsSuccessStatusCode)
                            return new ScheduleAssignDayStatus
                            {
                                Ok = false,
                                Error = "HTTP " + (int)resp.StatusCode + ": " + body,
                            };
                        return JsonConvert.DeserializeObject<ScheduleAssignDayStatus>(body)
                            ?? new ScheduleAssignDayStatus { Ok = false, Error = "empty response" };
                    }
                }
            }
            catch (Exception ex)
            {
                return new ScheduleAssignDayStatus { Ok = false, Error = DescribeRequestError(ex) };
            }
        }

        // ── ModivCare Market TP scorecard (panel-cached) ──────────────────

        public sealed class ModivcareMarketScoreSummary
        {
            [JsonProperty("tp_code")]
            public object TpCode { get; set; }

            public string State { get; set; }

            [JsonProperty("tp_score")]
            public double? TpScore { get; set; }

            public string Grade { get; set; }

            public double? Otp { get; set; }

            [JsonProperty("digital_level")]
            public double? DigitalLevel { get; set; }

            [JsonProperty("reroute_24h")]
            public double? Reroute24h { get; set; }

            [JsonProperty("driver_no_shows")]
            public double? DriverNoShows { get; set; }

            [JsonProperty("member_complaints")]
            public double? MemberComplaints { get; set; }

            [JsonProperty("serious_injury")]
            public double? SeriousInjury { get; set; }

            [JsonProperty("total_rides")]
            public int? TotalRides { get; set; }

            [JsonProperty("period_start")]
            public string PeriodStart { get; set; }

            [JsonProperty("period_end")]
            public string PeriodEnd { get; set; }

            [JsonProperty("regional_peers")]
            public double? RegionalPeers { get; set; }

            [JsonProperty("national_peers")]
            public double? NationalPeers { get; set; }

            [JsonProperty("otp_regional_peers")]
            public double? OtpRegionalPeers { get; set; }

            [JsonProperty("otp_national_peers")]
            public double? OtpNationalPeers { get; set; }

            [JsonProperty("digital_regional_peers")]
            public double? DigitalRegionalPeers { get; set; }

            [JsonProperty("digital_national_peers")]
            public double? DigitalNationalPeers { get; set; }

            [JsonProperty("reroute_regional_peers")]
            public double? RerouteRegionalPeers { get; set; }

            [JsonProperty("reroute_national_peers")]
            public double? RerouteNationalPeers { get; set; }

            [JsonProperty("created_dttm")]
            public string CreatedDttm { get; set; }
        }

        public sealed class ModivcareMarketScorecard
        {
            public bool Ok { get; set; } = true;

            [JsonProperty("has_data")]
            public bool HasData { get; set; }

            [JsonProperty("pull_date")]
            public string PullDate { get; set; }

            [JsonProperty("pulled_at")]
            public double? PulledAt { get; set; }

            [JsonProperty("pulled_at_iso")]
            public string PulledAtIso { get; set; }

            [JsonProperty("tp_code")]
            public object TpCode { get; set; }

            public string Source { get; set; }

            public ModivcareMarketScoreSummary Summary { get; set; }

            public string Error { get; set; }
        }

        public sealed class ModivcareMarketStatus
        {
            public bool Ok { get; set; } = true;

            public bool Enabled { get; set; } = true;

            [JsonProperty("scheduled_hours")]
            public List<int> ScheduledHours { get; set; }

            public string Today { get; set; }

            [JsonProperty("last_pull")]
            public JObject LastPull { get; set; }

            public ModivcareMarketScorecard Scorecard { get; set; }

            public string Error { get; set; }
        }

        public sealed class ModivcareMarketPullResult
        {
            public bool Ok { get; set; }

            [JsonProperty("pull_date")]
            public string PullDate { get; set; }

            [JsonProperty("tp_score")]
            public double? TpScore { get; set; }

            public string Grade { get; set; }

            public double? Otp { get; set; }

            [JsonProperty("total_rides")]
            public int? TotalRides { get; set; }

            [JsonProperty("elapsed_ms")]
            public int? ElapsedMs { get; set; }

            public string Error { get; set; }

            public ModivcareMarketStatus Status { get; set; }
        }

        public static async Task<ModivcareMarketScorecard> GetModivcareMarketScorecardAsync(
            HiatmeAiSettings settings,
            CancellationToken cancellationToken = default)
        {
            if (settings == null)
                return new ModivcareMarketScorecard { Ok = false, Error = "settings missing" };

            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl))
                return new ModivcareMarketScorecard { Ok = false, Error = "AI server URL not configured" };

            string url = baseUrl + "/api/hiatme/modivcare/market/scorecard";
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                        req.Headers.Authorization = new AuthenticationHeaderValue(
                            "Bearer", settings.ApiToken.Trim());
                    using (var resp = await SharedHttp.SendAsync(req, cancellationToken)
                        .ConfigureAwait(false))
                    {
                        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!resp.IsSuccessStatusCode)
                            return new ModivcareMarketScorecard
                            {
                                Ok = false,
                                Error = "HTTP " + (int)resp.StatusCode + ": " + body,
                            };
                        var doc = JsonConvert.DeserializeObject<ModivcareMarketScorecard>(body)
                            ?? new ModivcareMarketScorecard { Ok = false, Error = "empty response" };
                        doc.Ok = true;
                        return doc;
                    }
                }
            }
            catch (Exception ex)
            {
                return new ModivcareMarketScorecard { Ok = false, Error = DescribeRequestError(ex) };
            }
        }

        public static async Task<ModivcareMarketStatus> GetModivcareMarketStatusAsync(
            HiatmeAiSettings settings,
            CancellationToken cancellationToken = default)
        {
            if (settings == null)
                return new ModivcareMarketStatus { Ok = false, Error = "settings missing" };

            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl))
                return new ModivcareMarketStatus { Ok = false, Error = "AI server URL not configured" };

            string url = baseUrl + "/api/hiatme/modivcare/market/status";
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                        req.Headers.Authorization = new AuthenticationHeaderValue(
                            "Bearer", settings.ApiToken.Trim());
                    using (var resp = await SharedHttp.SendAsync(req, cancellationToken)
                        .ConfigureAwait(false))
                    {
                        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!resp.IsSuccessStatusCode)
                            return new ModivcareMarketStatus
                            {
                                Ok = false,
                                Error = "HTTP " + (int)resp.StatusCode + ": " + body,
                            };
                        var doc = JsonConvert.DeserializeObject<ModivcareMarketStatus>(body)
                            ?? new ModivcareMarketStatus { Ok = false, Error = "empty response" };
                        doc.Ok = true;
                        return doc;
                    }
                }
            }
            catch (Exception ex)
            {
                return new ModivcareMarketStatus { Ok = false, Error = DescribeRequestError(ex) };
            }
        }

        public static async Task<ModivcareMarketPullResult> PostModivcareMarketPullAsync(
            HiatmeAiSettings settings,
            CancellationToken cancellationToken = default)
        {
            if (settings == null)
                return new ModivcareMarketPullResult { Ok = false, Error = "settings missing" };

            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl))
                return new ModivcareMarketPullResult { Ok = false, Error = "AI server URL not configured" };

            string url = baseUrl + "/api/hiatme/modivcare/market/pull";
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Post, url))
                {
                    if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                        req.Headers.Authorization = new AuthenticationHeaderValue(
                            "Bearer", settings.ApiToken.Trim());
                    req.Content = new StringContent("{}", Encoding.UTF8, "application/json");
                    using (var resp = await SharedHttp.SendAsync(req, cancellationToken)
                        .ConfigureAwait(false))
                    {
                        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!resp.IsSuccessStatusCode)
                            return new ModivcareMarketPullResult
                            {
                                Ok = false,
                                Error = "HTTP " + (int)resp.StatusCode + ": " + body,
                            };
                        return JsonConvert.DeserializeObject<ModivcareMarketPullResult>(body)
                            ?? new ModivcareMarketPullResult { Ok = false, Error = "empty response" };
                    }
                }
            }
            catch (Exception ex)
            {
                return new ModivcareMarketPullResult { Ok = false, Error = DescribeRequestError(ex) };
            }
        }

        // ── Driver Discipline library ────────────────────────────────────

        public static async Task<DriverDisciplineListResult> ListDriverDisciplineAsync(
            HiatmeAiSettings settings,
            string driver = null,
            string employeeId = null,
            int limit = 200,
            CancellationToken cancellationToken = default)
        {
            if (settings == null)
                return new DriverDisciplineListResult { Ok = false, Error = "settings missing" };
            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl))
                return new DriverDisciplineListResult { Ok = false, Error = "AI server URL not configured" };

            var qs = new List<string>();
            if (!string.IsNullOrWhiteSpace(driver))
                qs.Add("driver=" + Uri.EscapeDataString(driver.Trim()));
            if (!string.IsNullOrWhiteSpace(employeeId))
                qs.Add("employee_id=" + Uri.EscapeDataString(employeeId.Trim()));
            if (limit > 0)
                qs.Add("limit=" + limit.ToString(CultureInfo.InvariantCulture));
            string url = baseUrl + "/api/hiatme/driver-discipline"
                + (qs.Count > 0 ? "?" + string.Join("&", qs) : "");

            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                        req.Headers.Authorization = new AuthenticationHeaderValue(
                            "Bearer", settings.ApiToken.Trim());
                    using (var resp = await SharedHttp.SendAsync(req, cancellationToken).ConfigureAwait(false))
                    {
                        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!resp.IsSuccessStatusCode)
                            return new DriverDisciplineListResult
                            {
                                Ok = false,
                                Error = "HTTP " + (int)resp.StatusCode + ": " + body,
                            };
                        var doc = JsonConvert.DeserializeObject<DriverDisciplineListResult>(body)
                            ?? new DriverDisciplineListResult { Ok = false, Error = "empty" };
                        doc.Ok = true;
                        if (doc.Items == null) doc.Items = new List<DriverDisciplineIndexItem>();
                        return doc;
                    }
                }
            }
            catch (Exception ex)
            {
                return new DriverDisciplineListResult { Ok = false, Error = DescribeRequestError(ex) };
            }
        }

        public static async Task<DriverDisciplinePriorsResult> GetDriverDisciplinePriorsAsync(
            HiatmeAiSettings settings,
            string driverName,
            string employeeId = null,
            CancellationToken cancellationToken = default)
        {
            if (settings == null || string.IsNullOrWhiteSpace(driverName))
                return new DriverDisciplinePriorsResult { Ok = false, Error = "driver required" };
            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl))
                return new DriverDisciplinePriorsResult { Ok = false, Error = "AI server URL not configured" };

            string url = baseUrl + "/api/hiatme/driver-discipline/driver/"
                + Uri.EscapeDataString(driverName.Trim());
            if (!string.IsNullOrWhiteSpace(employeeId))
                url += "?employee_id=" + Uri.EscapeDataString(employeeId.Trim());

            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                        req.Headers.Authorization = new AuthenticationHeaderValue(
                            "Bearer", settings.ApiToken.Trim());
                    using (var resp = await SharedHttp.SendAsync(req, cancellationToken).ConfigureAwait(false))
                    {
                        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!resp.IsSuccessStatusCode)
                            return new DriverDisciplinePriorsResult
                            {
                                Ok = false,
                                Error = "HTTP " + (int)resp.StatusCode + ": " + body,
                            };
                        var doc = JsonConvert.DeserializeObject<DriverDisciplinePriorsResult>(body)
                            ?? new DriverDisciplinePriorsResult { Ok = false, Error = "empty" };
                        doc.Ok = true;
                        if (doc.Items == null) doc.Items = new List<DriverDisciplineIndexItem>();
                        return doc;
                    }
                }
            }
            catch (Exception ex)
            {
                return new DriverDisciplinePriorsResult { Ok = false, Error = DescribeRequestError(ex) };
            }
        }

        public static async Task<DriverDisciplineMeta> GetDriverDisciplineMetaAsync(
            HiatmeAiSettings settings,
            string caseId,
            CancellationToken cancellationToken = default)
        {
            if (settings == null || string.IsNullOrWhiteSpace(caseId))
                return null;
            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl))
                return null;
            string url = baseUrl + "/api/hiatme/driver-discipline/"
                + Uri.EscapeDataString(caseId.Trim());
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                        req.Headers.Authorization = new AuthenticationHeaderValue(
                            "Bearer", settings.ApiToken.Trim());
                    using (var resp = await SharedHttp.SendAsync(req, cancellationToken).ConfigureAwait(false))
                    {
                        if (!resp.IsSuccessStatusCode) return null;
                        var root = JObject.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
                        var meta = root["meta"];
                        if (meta == null) return null;
                        return meta.ToObject<DriverDisciplineMeta>();
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        public static async Task<byte[]> DownloadDriverDisciplineDocxAsync(
            HiatmeAiSettings settings,
            string caseId,
            CancellationToken cancellationToken = default)
        {
            if (settings == null || string.IsNullOrWhiteSpace(caseId))
                return null;
            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl))
                return null;
            string url = baseUrl + "/api/hiatme/driver-discipline/"
                + Uri.EscapeDataString(caseId.Trim()) + "/docx";
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                        req.Headers.Authorization = new AuthenticationHeaderValue(
                            "Bearer", settings.ApiToken.Trim());
                    using (var resp = await SharedHttp.SendAsync(req, cancellationToken).ConfigureAwait(false))
                    {
                        if (!resp.IsSuccessStatusCode) return null;
                        return await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        public static async Task<DriverDisciplineServerDeleteResult> DeleteDriverDisciplineAsync(
            HiatmeAiSettings settings,
            string caseId,
            CancellationToken cancellationToken = default)
        {
            if (settings == null)
                return new DriverDisciplineServerDeleteResult { Ok = false, Error = "settings missing" };
            if (string.IsNullOrWhiteSpace(caseId))
                return new DriverDisciplineServerDeleteResult { Ok = false, Error = "case ID required" };

            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl))
                return new DriverDisciplineServerDeleteResult { Ok = false, Error = "AI server URL not configured" };

            string url = baseUrl + "/api/hiatme/driver-discipline/"
                + Uri.EscapeDataString(caseId.Trim());
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Delete, url))
                {
                    if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                        req.Headers.Authorization = new AuthenticationHeaderValue(
                            "Bearer", settings.ApiToken.Trim());
                    using (var resp = await SharedHttp.SendAsync(req, cancellationToken).ConfigureAwait(false))
                    {
                        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!resp.IsSuccessStatusCode)
                            return new DriverDisciplineServerDeleteResult
                            {
                                Ok = false,
                                Error = "HTTP " + (int)resp.StatusCode + ": " + body,
                            };
                        var root = JObject.Parse(body);
                        return new DriverDisciplineServerDeleteResult
                        {
                            Ok = root.Value<bool?>("ok") ?? true,
                            Id = root.Value<string>("id"),
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                return new DriverDisciplineServerDeleteResult { Ok = false, Error = DescribeRequestError(ex) };
            }
        }

        public static async Task<DriverDisciplineServerSaveResult> SaveDriverDisciplineAsync(
            HiatmeAiSettings settings,
            DriverDisciplineMeta meta,
            byte[] docxBytes,
            string updatedBy = "",
            CancellationToken cancellationToken = default)
        {
            if (settings == null)
                return new DriverDisciplineServerSaveResult { Ok = false, Error = "settings missing" };
            if (meta == null || docxBytes == null || docxBytes.Length == 0)
                return new DriverDisciplineServerSaveResult { Ok = false, Error = "meta/docx required" };

            var baseUrl = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl))
                return new DriverDisciplineServerSaveResult { Ok = false, Error = "AI server URL not configured" };

            string url = baseUrl + "/api/hiatme/driver-discipline";
            try
            {
                var payload = new JObject
                {
                    ["updated_by"] = updatedBy ?? "",
                    ["meta"] = JObject.FromObject(meta),
                    ["docx_base64"] = Convert.ToBase64String(docxBytes),
                };
                using (var req = new HttpRequestMessage(HttpMethod.Post, url))
                {
                    if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                        req.Headers.Authorization = new AuthenticationHeaderValue(
                            "Bearer", settings.ApiToken.Trim());
                    req.Content = new StringContent(
                        payload.ToString(Formatting.None), Encoding.UTF8, "application/json");
                    using (var resp = await SharedHttp.SendAsync(req, cancellationToken).ConfigureAwait(false))
                    {
                        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!resp.IsSuccessStatusCode)
                            return new DriverDisciplineServerSaveResult
                            {
                                Ok = false,
                                Error = "HTTP " + (int)resp.StatusCode + ": " + body,
                            };
                        var root = JObject.Parse(body);
                        return new DriverDisciplineServerSaveResult
                        {
                            Ok = root.Value<bool?>("ok") ?? true,
                            Id = root.Value<string>("id"),
                            Path = root.Value<string>("path"),
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                return new DriverDisciplineServerSaveResult { Ok = false, Error = DescribeRequestError(ex) };
            }
        }
    }
}
