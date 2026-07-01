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
                return new TripScoutServerStatus { Ok = false, Error = ex.Message };
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
                return new TripScoutServerTrips { Ok = false, Error = ex.Message };
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
                return new WellRydeBellStatus { Ok = false, Error = ex.Message };
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
                return new TripScoutDayChanges { Ok = false, Error = ex.Message };
            }
        }
    }
}
