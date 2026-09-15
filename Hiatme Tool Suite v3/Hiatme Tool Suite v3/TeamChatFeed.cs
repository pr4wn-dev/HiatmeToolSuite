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
    internal enum ChatMessageKind
    {
        Me,
        Team,
        Ai,
        AiThinking,
        System,
        Error,
    }

    /// <summary>One line in the dock transcript — a teammate, me, the AI, or a local note.</summary>
    internal sealed class ChatMessage
    {
        public long Seq { get; set; }
        public DateTime Time { get; set; } = DateTime.Now;
        public string Sender { get; set; } = "";
        public string ClientId { get; set; } = "";
        public ChatMessageKind Kind { get; set; } = ChatMessageKind.Team;
        public string Text { get; set; } = "";
        public bool MentionsAi { get; set; }
        public long? ReplyTo { get; set; }
        /// <summary>Local-only: true while a post is still in flight.</summary>
        public bool Pending { get; set; }
        /// <summary>Local-only: the post failed; drawn with an error tick.</summary>
        public bool Failed { get; set; }

        public static ChatMessage FromServer(TeamChatWire w, string myClientId)
        {
            if (w == null) return null;
            ChatMessageKind kind;
            if (string.Equals(w.Kind, "ai", StringComparison.OrdinalIgnoreCase)) kind = ChatMessageKind.Ai;
            else if (string.Equals(w.Kind, "system", StringComparison.OrdinalIgnoreCase)) kind = ChatMessageKind.System;
            else kind = string.Equals(w.ClientId, myClientId, StringComparison.Ordinal) ? ChatMessageKind.Me : ChatMessageKind.Team;
            return new ChatMessage
            {
                Seq = w.Seq,
                Time = w.LocalTime,
                Sender = kind == ChatMessageKind.Ai ? "AI" : (w.Dispatcher ?? ""),
                ClientId = w.ClientId ?? "",
                Kind = kind,
                Text = w.Text ?? "",
                MentionsAi = w.MentionsAi ?? false,
                ReplyTo = w.ReplyTo,
            };
        }
    }

    internal sealed class TeamChatWire
    {
        [JsonProperty("seq")] public long Seq { get; set; }
        [JsonProperty("ts")] public double Ts { get; set; }
        [JsonProperty("iso")] public string Iso { get; set; }
        [JsonProperty("kind")] public string Kind { get; set; }
        [JsonProperty("dispatcher")] public string Dispatcher { get; set; }
        [JsonProperty("client_id")] public string ClientId { get; set; }
        [JsonProperty("text")] public string Text { get; set; }
        [JsonProperty("reply_to")] public long? ReplyTo { get; set; }
        [JsonProperty("mentions_ai")] public bool? MentionsAi { get; set; }

        public DateTime LocalTime
        {
            get
            {
                try { return DateTimeOffset.FromUnixTimeMilliseconds((long)(Ts * 1000)).LocalDateTime; }
                catch { return DateTime.Now; }
            }
        }
    }

    internal sealed class TeamChatTyping
    {
        [JsonProperty("client_id")] public string ClientId { get; set; }
        [JsonProperty("dispatcher")] public string Dispatcher { get; set; }
    }

    /// <summary>
    /// Desk-side transport for the team room: post, poll (2s open / 8s hidden), typing
    /// heartbeat, history. UI-agnostic; Form1.TeamChat.cs subscribes and draws.
    /// </summary>
    internal sealed class TeamChatFeed : IDisposable
    {
        public const int PollFastMs = 2_000;
        public const int PollSlowMs = 8_000;
        private const int TypingPingMs = 2_000;

        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };

        private readonly Func<HiatmeAiSettings> _settingsProvider;
        private readonly System.Windows.Forms.Timer _pollTimer;
        private bool _pollInFlight;
        private long _cursor;
        private bool _disposed;
        private DateTime _lastTypingPingUtc = DateTime.MinValue;
        private bool _typingSent;
        private List<string> _typingNames = new List<string>();

        public event Action<List<ChatMessage>> MessagesArrived;
        public event Action<List<string>> TypingChanged;
        public event Action<bool> ConnectivityChanged;

        public bool Online { get; private set; } = true;
        public string DispatcherName => ScheduleActivityIdentity.DispatcherName();
        public string ClientId => ScheduleActivityIdentity.ClientId(Settings);
        public long Cursor => Interlocked.Read(ref _cursor);
        public IReadOnlyList<string> TypingNames => _typingNames;

        private HiatmeAiSettings Settings
        {
            get
            {
                try { return _settingsProvider?.Invoke() ?? HiatmeAiSettings.LoadNoProbe(); }
                catch { return null; }
            }
        }

        public TeamChatFeed(Func<HiatmeAiSettings> settingsProvider)
        {
            _settingsProvider = settingsProvider;
            _pollTimer = new System.Windows.Forms.Timer { Interval = PollSlowMs };
            _pollTimer.Tick += (_, __) => { _ = PollAsync(); };
        }

        public void Start()
        {
            if (_disposed) return;
            _pollTimer.Start();
            _ = PollAsync();
        }

        public void Stop() => _pollTimer.Stop();

        public void SetFastPolling(bool fast)
        {
            int want = fast ? PollFastMs : PollSlowMs;
            if (_pollTimer.Interval != want)
            {
                _pollTimer.Interval = want;
                if (fast) _ = PollAsync();
            }
        }

        // ---------------------------------------------------------------- post

        /// <summary>Post one message; returns the stored record (with seq) or null when offline.</summary>
        public async Task<ChatMessage> PostAsync(string text, string kind = "user", long? replyTo = null, bool mentionsAi = false)
        {
            if (_disposed || string.IsNullOrWhiteSpace(text)) return null;
            var settings = Settings;
            string baseUrl = BaseUrl(settings);
            if (baseUrl == null) return null;
            var body = new JObject
            {
                ["text"] = text.Trim(),
                ["kind"] = kind ?? "user",
                ["dispatcher"] = kind == "ai" ? "AI" : DispatcherName,
                ["client_id"] = ClientId,
                ["machine"] = ScheduleActivityIdentity.Machine(),
                ["mentions_ai"] = mentionsAi,
            };
            if (replyTo.HasValue) body["reply_to"] = replyTo.Value;
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/api/hiatme/chat/messages"))
                {
                    Auth(req, settings);
                    req.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
                    using (var resp = await Http.SendAsync(req).ConfigureAwait(true))
                    {
                        if (!resp.IsSuccessStatusCode) { SetOnline(false); return null; }
                        SetOnline(true);
                        _typingSent = false;
                        var root = JObject.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true));
                        var wire = root["message"]?.ToObject<TeamChatWire>();
                        long seq = root["seq"]?.Value<long>() ?? 0;
                        if (seq > Cursor) Interlocked.Exchange(ref _cursor, seq);
                        return ChatMessage.FromServer(wire, ClientId);
                    }
                }
            }
            catch
            {
                SetOnline(false);
                return null;
            }
        }

        // -------------------------------------------------------------- typing

        /// <summary>Call on every keystroke; pings the room at most every 2s. Call with false when the box empties.</summary>
        public void NotifyTyping(bool typing)
        {
            if (_disposed) return;
            var now = DateTime.UtcNow;
            if (typing)
            {
                if ((now - _lastTypingPingUtc).TotalMilliseconds < TypingPingMs) return;
                _lastTypingPingUtc = now;
                _typingSent = true;
                _ = SendTypingAsync(true);
            }
            else if (_typingSent)
            {
                _typingSent = false;
                _lastTypingPingUtc = DateTime.MinValue;
                _ = SendTypingAsync(false);
            }
        }

        private async Task SendTypingAsync(bool typing)
        {
            var settings = Settings;
            string baseUrl = BaseUrl(settings);
            if (baseUrl == null) return;
            var body = new JObject
            {
                ["dispatcher"] = DispatcherName,
                ["client_id"] = ClientId,
                ["typing"] = typing,
            };
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/api/hiatme/chat/typing"))
                {
                    Auth(req, settings);
                    req.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
                    using (var resp = await Http.SendAsync(req).ConfigureAwait(true))
                    {
                        if (!resp.IsSuccessStatusCode) { SetOnline(false); return; }
                        SetOnline(true);
                        var root = JObject.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true));
                        ApplyTyping(root["typing"]);
                    }
                }
            }
            catch { SetOnline(false); }
        }

        private void ApplyTyping(JToken token)
        {
            var names = new List<string>();
            try
            {
                var list = token?.ToObject<List<TeamChatTyping>>();
                if (list != null)
                    names = list.Select(t => (t.Dispatcher ?? "").Trim())
                        .Where(n => n.Length > 0).Distinct().ToList();
            }
            catch { }
            if (names.SequenceEqual(_typingNames)) return;
            _typingNames = names;
            try { TypingChanged?.Invoke(names); } catch { }
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
                string url = baseUrl + "/api/hiatme/chat/messages?since="
                    + Cursor.ToString(CultureInfo.InvariantCulture)
                    + "&client_id=" + Uri.EscapeDataString(ClientId);
                using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    Auth(req, settings);
                    using (var resp = await Http.SendAsync(req).ConfigureAwait(true))
                    {
                        if (!resp.IsSuccessStatusCode) { SetOnline(false); return; }
                        SetOnline(true);
                        var root = JObject.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true));
                        long seq = root["seq"]?.Value<long>() ?? 0;
                        var wires = root["messages"]?.ToObject<List<TeamChatWire>>() ?? new List<TeamChatWire>();
                        bool first = Cursor == 0;
                        if (seq > Cursor) Interlocked.Exchange(ref _cursor, seq);
                        // Cold start is handled by HistoryAsync; do not replay the morning as arrivals.
                        if (!first && wires.Count > 0)
                        {
                            var msgs = wires.Select(w => ChatMessage.FromServer(w, ClientId)).Where(m => m != null).ToList();
                            if (msgs.Count > 0) MessagesArrived?.Invoke(msgs);
                        }
                        ApplyTyping(root["typing"]);
                    }
                }
            }
            catch { SetOnline(false); }
            finally { _pollInFlight = false; }
        }

        public async Task<List<ChatMessage>> HistoryAsync(int limit = 200)
        {
            var settings = Settings;
            string baseUrl = BaseUrl(settings);
            if (baseUrl == null) return new List<ChatMessage>();
            try
            {
                string url = baseUrl + "/api/hiatme/chat/history?limit=" + limit.ToString(CultureInfo.InvariantCulture);
                using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    Auth(req, settings);
                    using (var resp = await Http.SendAsync(req).ConfigureAwait(true))
                    {
                        if (!resp.IsSuccessStatusCode) { SetOnline(false); return new List<ChatMessage>(); }
                        SetOnline(true);
                        var root = JObject.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true));
                        long seq = root["seq"]?.Value<long>() ?? 0;
                        if (seq > Cursor) Interlocked.Exchange(ref _cursor, seq);
                        var wires = root["messages"]?.ToObject<List<TeamChatWire>>() ?? new List<TeamChatWire>();
                        return wires.Select(w => ChatMessage.FromServer(w, ClientId)).Where(m => m != null).ToList();
                    }
                }
            }
            catch
            {
                SetOnline(false);
                return new List<ChatMessage>();
            }
        }

        // ------------------------------------------------------------- helpers

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
            _pollTimer.Dispose();
        }
    }
}
