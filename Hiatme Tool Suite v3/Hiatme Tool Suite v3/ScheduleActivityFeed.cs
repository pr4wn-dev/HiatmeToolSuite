using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Who this desk is, for the schedule activity feed.</summary>
    internal static class ScheduleActivityIdentity
    {
        /// <summary>Dispatcher display name: the WellRyde login, which is the person's name.</summary>
        public static string DispatcherName()
        {
            string user = "";
            try { user = (Properties.Settings.Default.wrUserName ?? "").Trim(); }
            catch { }
            if (string.IsNullOrEmpty(user))
            {
                try { user = (Environment.UserName ?? "").Trim(); }
                catch { }
            }
            if (string.IsNullOrEmpty(user))
                return "Someone";
            // "remie" -> "Remie"; leave mixed case alone.
            if (user == user.ToLowerInvariant() && user.Length > 1)
                return char.ToUpperInvariant(user[0]) + user.Substring(1);
            return user;
        }

        /// <summary>Stable per-desk id: two desks with the same Windows user must not collide.</summary>
        public static string ClientId(HiatmeAiSettings settings)
        {
            string id = "";
            try { id = settings?.ResolvedClientId() ?? ""; }
            catch { }
            if (string.IsNullOrWhiteSpace(id))
                id = "hiatme-" + Environment.UserName;
            string machine = "";
            try { machine = Environment.MachineName ?? ""; }
            catch { }
            return string.IsNullOrEmpty(machine) ? id : id + "@" + machine;
        }

        public static string Machine()
        {
            try { return Environment.MachineName ?? ""; }
            catch { return ""; }
        }

        public static void AddHeaders(HttpRequestMessage req, HiatmeAiSettings settings)
        {
            if (req == null) return;
            try
            {
                req.Headers.TryAddWithoutValidation("X-Hiatme-Dispatcher", DispatcherName());
                req.Headers.TryAddWithoutValidation("X-Hiatme-Client", ClientId(settings));
                string m = Machine();
                if (!string.IsNullOrEmpty(m))
                    req.Headers.TryAddWithoutValidation("X-Hiatme-Machine", m);
            }
            catch { }
        }
    }

    internal sealed class ScheduleActivityTrip
    {
        [JsonProperty("trip")] public string Trip { get; set; }
        [JsonProperty("client")] public string Client { get; set; }
        [JsonProperty("pu_time", NullValueHandling = NullValueHandling.Ignore)] public string PuTime { get; set; }
    }

    internal sealed class ScheduleActivityDetail
    {
        [JsonProperty("from_tab", NullValueHandling = NullValueHandling.Ignore)] public string FromTab { get; set; }
        [JsonProperty("to_tab", NullValueHandling = NullValueHandling.Ignore)] public string ToTab { get; set; }
        [JsonProperty("tab", NullValueHandling = NullValueHandling.Ignore)] public string Tab { get; set; }
        [JsonProperty("label", NullValueHandling = NullValueHandling.Ignore)] public string Label { get; set; }
        [JsonProperty("count", NullValueHandling = NullValueHandling.Ignore)] public int? Count { get; set; }
        [JsonProperty("revision", NullValueHandling = NullValueHandling.Ignore)] public int? Revision { get; set; }
        [JsonProperty("base_revision", NullValueHandling = NullValueHandling.Ignore)] public int? BaseRevision { get; set; }
        [JsonProperty("reason", NullValueHandling = NullValueHandling.Ignore)] public string Reason { get; set; }
        [JsonProperty("trips", NullValueHandling = NullValueHandling.Ignore)] public List<ScheduleActivityTrip> Trips { get; set; }

        public int EffectiveCount => Count ?? (Trips?.Count ?? 0);
    }

    internal sealed class ScheduleActivityEvent
    {
        [JsonProperty("seq")] public long Seq { get; set; }
        [JsonProperty("ts")] public double Ts { get; set; }
        [JsonProperty("iso")] public string Iso { get; set; }
        [JsonProperty("service_date")] public string ServiceDate { get; set; }
        [JsonProperty("verb")] public string Verb { get; set; }
        [JsonProperty("dispatcher")] public string Dispatcher { get; set; }
        [JsonProperty("client_id")] public string ClientId { get; set; }
        [JsonProperty("detail")] public ScheduleActivityDetail Detail { get; set; }
        [JsonProperty("saved")] public bool? Saved { get; set; }
        [JsonProperty("audience", NullValueHandling = NullValueHandling.Ignore)] public string Audience { get; set; }
        [JsonProperty("text")] public string Text { get; set; }

        public DateTime LocalTime
        {
            get
            {
                try { return DateTimeOffset.FromUnixTimeMilliseconds((long)(Ts * 1000)).LocalDateTime; }
                catch { return DateTime.Now; }
            }
        }

        public bool IsEdit =>
            !string.Equals(Verb, "opened", StringComparison.Ordinal)
            && !string.Equals(Verb, "closed", StringComparison.Ordinal)
            && !string.Equals(Verb, "saved", StringComparison.Ordinal)
            && !string.Equals(Verb, "rejected", StringComparison.Ordinal);
    }

    internal sealed class SchedulePresenceEntry
    {
        [JsonProperty("client_id")] public string ClientId { get; set; }
        [JsonProperty("dispatcher")] public string Dispatcher { get; set; }
        [JsonProperty("service_date")] public string ServiceDate { get; set; }
        [JsonProperty("unsaved")] public int Unsaved { get; set; }
        [JsonProperty("revision")] public int Revision { get; set; }
        [JsonProperty("age_s")] public double AgeSeconds { get; set; }
        [JsonProperty("active")] public bool Active { get; set; }
        [JsonProperty("machine")] public string Machine { get; set; }
    }

    internal sealed class DeskPokeMessage
    {
        [JsonProperty("emoji")] public string Emoji { get; set; }
        [JsonProperty("dispatcher")] public string Dispatcher { get; set; }
        [JsonProperty("client_id")] public string ClientId { get; set; }
        [JsonProperty("machine")] public string Machine { get; set; }
        [JsonProperty("ts")] public double Ts { get; set; }
    }

    internal sealed class SchedulePublishedHead
    {
        [JsonProperty("service_date")] public string ServiceDate { get; set; }
        [JsonProperty("revision")] public int Revision { get; set; }
        [JsonProperty("sha256")] public string Sha256 { get; set; }
        [JsonProperty("dispatcher")] public string Dispatcher { get; set; }
        [JsonProperty("client_id")] public string ClientId { get; set; }
    }

    internal sealed class ScheduleActivityPollResult
    {
        public bool Ok { get; set; }
        public string Error { get; set; }
        public long Seq { get; set; }
        public List<ScheduleActivityEvent> Events { get; set; } = new List<ScheduleActivityEvent>();
        public List<SchedulePresenceEntry> Presence { get; set; } = new List<SchedulePresenceEntry>();
        public SchedulePublishedHead Head { get; set; }
    }

    /// <summary>
    /// Desk-side transport for the schedule activity feed: batches this desk's edits up
    /// to the panel, heartbeats presence, and polls everyone else's events. UI-agnostic;
    /// Form1.ScheduleActivity.cs subscribes and draws.
    /// </summary>
    internal sealed class ScheduleActivityFeed : IDisposable
    {
        public const int PollFastMs = 3_000;
        public const int PollSlowMs = 15_000;
        public const int HeartbeatMs = 10_000;
        public const int FlushMs = 1_500;
        private const int FlushAtCount = 12;

        private static readonly HttpClient Http = HiatmePanelHttp.Create(TimeSpan.FromSeconds(8));

        private readonly object _gate = new object();
        private readonly List<JObject> _queue = new List<JObject>();
        private readonly System.Windows.Forms.Timer _flushTimer;
        private readonly System.Windows.Forms.Timer _heartbeatTimer;
        private readonly System.Windows.Forms.Timer _pollTimer;
        private bool _flushInFlight;
        private bool _pollInFlight;
        private bool _heartbeatInFlight;
        private long _cursor;
        private bool _disposed;

        private Func<HiatmeAiSettings> _settingsProvider;

        /// <summary>Set by the host: which day the builder has open ("" when closed).</summary>
        public Func<string> ServiceDateProvider { get; set; }
        /// <summary>Set by the host: edits since last successful save.</summary>
        public Func<int> UnsavedCountProvider { get; set; }
        /// <summary>Set by the host: local revision of the open workbook.</summary>
        public Func<int> LocalRevisionProvider { get; set; }
        /// <summary>Set by the host: is the Schedule Builder tab on screen right now.</summary>
        public Func<bool> BuilderVisibleProvider { get; set; }

        public event Action<List<ScheduleActivityEvent>> EventsArrived;
        public event Action<List<SchedulePresenceEntry>> PresenceUpdated;
        public event Action<SchedulePublishedHead> HeadUpdated;
        public event Action<List<DeskPokeMessage>> PokesArrived;
        public event Action<bool> ConnectivityChanged;

        public bool Online { get; private set; } = true;
        public string DispatcherName => ScheduleActivityIdentity.DispatcherName();
        public string ClientId => ScheduleActivityIdentity.ClientId(Settings);
        public long Cursor => Interlocked.Read(ref _cursor);

        private HiatmeAiSettings Settings
        {
            get
            {
                try { return _settingsProvider?.Invoke() ?? HiatmeAiSettings.LoadNoProbe(); }
                catch { return null; }
            }
        }

        public ScheduleActivityFeed(Func<HiatmeAiSettings> settingsProvider)
        {
            _settingsProvider = settingsProvider;
            _flushTimer = new System.Windows.Forms.Timer { Interval = FlushMs };
            _flushTimer.Tick += (_, __) => { _ = FlushAsync(); };
            _heartbeatTimer = new System.Windows.Forms.Timer { Interval = HeartbeatMs };
            _heartbeatTimer.Tick += (_, __) => { _ = HeartbeatAsync(); };
            _pollTimer = new System.Windows.Forms.Timer { Interval = PollSlowMs };
            _pollTimer.Tick += (_, __) => { _ = PollAsync(); };
        }

        public void Start()
        {
            if (_disposed) return;
            _flushTimer.Start();
            _heartbeatTimer.Start();
            _pollTimer.Start();
            _ = HeartbeatAsync();
            _ = PollAsync();
        }

        public void Stop()
        {
            _flushTimer.Stop();
            _heartbeatTimer.Stop();
            _pollTimer.Stop();
        }

        /// <summary>Speed the poll up while the builder is on screen; slow it down otherwise.</summary>
        public void SetFastPolling(bool fast)
        {
            int want = fast ? PollFastMs : PollSlowMs;
            if (_pollTimer.Interval != want)
            {
                _pollTimer.Interval = want;
                if (fast) _ = PollAsync();
            }
        }

        // ---------------------------------------------------------------- emit

        public void Emit(string verb, string serviceDateIso, ScheduleActivityDetail detail)
        {
            if (_disposed || string.IsNullOrWhiteSpace(verb) || string.IsNullOrWhiteSpace(serviceDateIso))
                return;
            var ev = new JObject
            {
                ["verb"] = verb.Trim(),
                ["service_date"] = serviceDateIso.Trim(),
                ["ts"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0,
            };
            if (detail != null)
                ev["detail"] = JObject.FromObject(detail);
            int count;
            lock (_gate)
            {
                _queue.Add(ev);
                count = _queue.Count;
            }
            if (count >= FlushAtCount)
                _ = FlushAsync();
        }

        /// <summary>Push whatever is queued right now (used on shutdown / before a save).</summary>
        public Task FlushNowAsync() => FlushAsync();

        private async Task FlushAsync()
        {
            if (_disposed || _flushInFlight) return;
            List<JObject> batch;
            lock (_gate)
            {
                if (_queue.Count == 0) return;
                batch = new List<JObject>(_queue);
                _queue.Clear();
            }
            _flushInFlight = true;
            try
            {
                var settings = Settings;
                string baseUrl = BaseUrl(settings);
                if (baseUrl == null) { Requeue(batch); return; }
                var body = new JObject
                {
                    ["dispatcher"] = DispatcherName,
                    ["client_id"] = ClientId,
                    ["machine"] = ScheduleActivityIdentity.Machine(),
                    ["events"] = new JArray(batch),
                };
                var sent = await SendOffUiAsync(
                    HttpMethod.Post,
                    baseUrl + "/api/hiatme/schedule/activity",
                    settings,
                    body.ToString(Formatting.None)).ConfigureAwait(true);
                if (!sent.Ok)
                {
                    SetOnline(false);
                    Requeue(batch);
                    return;
                }
                SetOnline(true);
            }
            catch
            {
                SetOnline(false);
                Requeue(batch);
            }
            finally
            {
                _flushInFlight = false;
            }
        }

        private void Requeue(List<JObject> batch)
        {
            lock (_gate)
            {
                _queue.InsertRange(0, batch);
                // Never let a dead panel grow the queue without bound.
                if (_queue.Count > 400)
                    _queue.RemoveRange(0, _queue.Count - 400);
            }
        }

        // ----------------------------------------------------------- heartbeat

        public async Task HeartbeatAsync(bool builderOpen = true)
        {
            if (_disposed || _heartbeatInFlight) return;
            _heartbeatInFlight = true;
            try
            {
                var settings = Settings;
                string baseUrl = BaseUrl(settings);
                if (baseUrl == null) return;
                JObject body;
                // Everything up to the first await runs on the UI thread, including the
                // revision sidecar read inside LocalRevisionProvider.
                using (UiStallWatch.Measure(UiScope.ActivityHeartbeat))
                {
                    string sd = "";
                    try { sd = ServiceDateProvider?.Invoke() ?? ""; } catch { }
                    // Stay listed while the app is running, even on other tools or
                    // with no schedule day loaded. Shutdown passes builderOpen: false.
                    body = new JObject
                    {
                        ["dispatcher"] = DispatcherName,
                        ["client_id"] = ClientId,
                        ["machine"] = ScheduleActivityIdentity.Machine(),
                        ["service_date"] = sd,
                        ["unsaved"] = SafeInt(UnsavedCountProvider),
                        ["revision"] = SafeInt(LocalRevisionProvider),
                        ["builder_open"] = builderOpen,
                    };
                }
                var sent = await SendOffUiAsync(
                    HttpMethod.Post,
                    baseUrl + "/api/hiatme/schedule/presence",
                    settings,
                    body.ToString(Formatting.None)).ConfigureAwait(true);
                if (!sent.Ok)
                {
                    SetOnline(false);
                    return;
                }
                SetOnline(true);
                var root = JObject.Parse(string.IsNullOrWhiteSpace(sent.Body) ? "{}" : sent.Body);
                var presence = root["presence"]?.ToObject<List<SchedulePresenceEntry>>()
                    ?? new List<SchedulePresenceEntry>();
                PresenceUpdated?.Invoke(presence);
                RaisePokes(root);
            }
            catch
            {
                SetOnline(false);
            }
            finally
            {
                _heartbeatInFlight = false;
            }
        }

        // ---------------------------------------------------------------- poll

        public async Task PollAsync()
        {
            if (_disposed || _pollInFlight) return;
            _pollInFlight = true;
            try
            {
                var settings = Settings;
                string baseUrl = BaseUrl(settings);
                if (baseUrl == null) return;
                string sd = "";
                try { sd = ServiceDateProvider?.Invoke() ?? ""; } catch { }
                var sb = new StringBuilder(baseUrl)
                    .Append("/api/hiatme/schedule/activity?since=")
                    .Append(Cursor.ToString(CultureInfo.InvariantCulture))
                    .Append("&client_id=").Append(Uri.EscapeDataString(ClientId));
                if (!string.IsNullOrEmpty(sd))
                    sb.Append("&service_date=").Append(Uri.EscapeDataString(sd));
                var sent = await SendOffUiAsync(
                    HttpMethod.Get, sb.ToString(), settings, null).ConfigureAwait(true);
                if (!sent.Ok)
                {
                    SetOnline(false);
                    return;
                }
                SetOnline(true);
                using (UiStallWatch.Measure(UiScope.ActivityPoll))
                {
                    var root = JObject.Parse(string.IsNullOrWhiteSpace(sent.Body) ? "{}" : sent.Body);
                    var result = new ScheduleActivityPollResult
                    {
                        Ok = root["ok"]?.Value<bool>() != false,
                        Seq = root["seq"]?.Value<long>() ?? 0,
                        Events = root["events"]?.ToObject<List<ScheduleActivityEvent>>()
                            ?? new List<ScheduleActivityEvent>(),
                        Presence = root["presence"]?.ToObject<List<SchedulePresenceEntry>>()
                            ?? new List<SchedulePresenceEntry>(),
                        Head = root["head"]?.Type == JTokenType.Object
                            ? root["head"].ToObject<SchedulePublishedHead>()
                            : null,
                    };
                    bool first = Cursor == 0;
                    if (result.Seq > Cursor)
                        Interlocked.Exchange(ref _cursor, result.Seq);
                    // Cold start: adopt the cursor but do not replay the morning as toasts.
                    if (!first && result.Events.Count > 0)
                        EventsArrived?.Invoke(result.Events);
                    PresenceUpdated?.Invoke(result.Presence);
                    RaisePokes(root);
                    if (result.Head != null)
                        HeadUpdated?.Invoke(result.Head);
                }
            }
            catch
            {
                SetOnline(false);
            }
            finally
            {
                _pollInFlight = false;
            }
        }

        public async Task<List<ScheduleActivityEvent>> HistoryAsync(string serviceDateIso, int limit = 500)
        {
            var settings = Settings;
            string baseUrl = BaseUrl(settings);
            if (baseUrl == null || string.IsNullOrWhiteSpace(serviceDateIso))
                return new List<ScheduleActivityEvent>();
            try
            {
                string url = baseUrl + "/api/hiatme/schedule/activity/history?service_date="
                    + Uri.EscapeDataString(serviceDateIso.Trim())
                    + "&limit=" + limit.ToString(CultureInfo.InvariantCulture);
                var sent = await SendOffUiAsync(HttpMethod.Get, url, settings, null).ConfigureAwait(true);
                if (!sent.Ok) return new List<ScheduleActivityEvent>();
                var root = JObject.Parse(string.IsNullOrWhiteSpace(sent.Body) ? "{}" : sent.Body);
                return root["events"]?.ToObject<List<ScheduleActivityEvent>>()
                    ?? new List<ScheduleActivityEvent>();
            }
            catch
            {
                return new List<ScheduleActivityEvent>();
            }
        }

        /// <summary>Push a full-screen emoji at another desk. Returns false if the panel said no.</summary>
        public async Task<bool> SendPokeAsync(string targetClientId, string emoji)
        {
            if (_disposed || string.IsNullOrWhiteSpace(targetClientId) || string.IsNullOrWhiteSpace(emoji))
                return false;
            try
            {
                var settings = Settings;
                string baseUrl = BaseUrl(settings);
                if (baseUrl == null) return false;
                var body = new JObject
                {
                    ["target_client_id"] = targetClientId.Trim(),
                    ["emoji"] = emoji.Trim(),
                    ["dispatcher"] = DispatcherName,
                    ["client_id"] = ClientId,
                    ["machine"] = ScheduleActivityIdentity.Machine(),
                };
                var sent = await SendOffUiAsync(
                    HttpMethod.Post,
                    baseUrl + "/api/hiatme/desk-poke",
                    settings,
                    body.ToString(Formatting.None)).ConfigureAwait(true);
                if (!sent.Ok) return false;
                var root = JObject.Parse(string.IsNullOrWhiteSpace(sent.Body) ? "{}" : sent.Body);
                return root["ok"]?.Value<bool>() != false;
            }
            catch
            {
                return false;
            }
        }

        private void RaisePokes(JObject root)
        {
            if (root == null) return;
            try
            {
                var pokes = root["pokes"]?.ToObject<List<DeskPokeMessage>>()
                    ?? new List<DeskPokeMessage>();
                if (pokes.Count > 0)
                    PokesArrived?.Invoke(pokes);
            }
            catch { }
        }

        // ------------------------------------------------------------- helpers

        /// <summary>
        /// .NET Framework HttpClient.SendAsync started on the WinForms UI thread can
        /// block for the entire TCP/proxy wait (8s timeout here). Hop off first.
        /// </summary>
        private async Task<(bool Ok, string Body)> SendOffUiAsync(
            HttpMethod method, string url, HiatmeAiSettings settings, string jsonBody)
        {
            return await Task.Run(async () =>
            {
                using (var req = new HttpRequestMessage(method, url))
                {
                    Auth(req, settings);
                    if (jsonBody != null)
                        req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                    using (var resp = await Http.SendAsync(req).ConfigureAwait(false))
                    {
                        if (!resp.IsSuccessStatusCode)
                            return (false, (string)null);
                        return (true, await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
                    }
                }
            }).ConfigureAwait(false);
        }

        private static string BaseUrl(HiatmeAiSettings settings)
        {
            var b = (settings?.BaseUrl ?? "").Trim().TrimEnd('/');
            return string.IsNullOrEmpty(b) ? null : b;
        }

        private void Auth(HttpRequestMessage req, HiatmeAiSettings settings)
        {
            if (settings != null && !string.IsNullOrWhiteSpace(settings.ApiToken))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiToken.Trim());
            ScheduleActivityIdentity.AddHeaders(req, settings);
        }

        private static int SafeInt(Func<int> f)
        {
            try { return Math.Max(0, f?.Invoke() ?? 0); }
            catch { return 0; }
        }

        private void SetOnline(bool on)
        {
            if (Online == on) return;
            Online = on;
            try { ConnectivityChanged?.Invoke(on); } catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            _flushTimer.Dispose();
            _heartbeatTimer.Dispose();
            _pollTimer.Dispose();
        }
    }
}
