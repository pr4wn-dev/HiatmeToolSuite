using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Team chat inside the AI dock: the room is the default target, "@ai …" hands a line
    /// to the copilot and its answer is posted back for every desk. When the panel reports
    /// autochat, that answer is the server's and the desk does not call the copilot again.
    /// Typing shows above the composer; while the dock is hidden, arrivals become HUD toasts
    /// and a badge on the title-bar AI chip.
    /// </summary>
    public partial class Form1
    {
        private static readonly Regex TeamChatAiPrefix = new Regex(
            @"^@(ai|supey|copilot)\b[\s:,\-\u2014]*", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private const int TeamChatToastMaxChars = 160;

        private TeamChatFeed _teamChat;
        private Label _teamChatTypingLbl;
        private Timer _teamChatDotsTimer;
        private List<string> _teamChatTyping = new List<string>();
        private int _teamChatDots;
        private int _teamChatUnread;
        private bool _teamChatHistoryLoaded;

        // ------------------------------------------------------------------ init

        private void InitTeamChat()
        {
            if (_teamChat != null) return;
            _teamChat = new TeamChatFeed(() => HiatmeAiSettings.LoadNoProbe());
            _teamChat.MessagesArrived += OnTeamChatMessages;
            _teamChat.TypingChanged += OnTeamChatTyping;
            _teamChat.AutochatChanged += OnTeamChatAutochatChanged;
            _teamChat.ConnectivityChanged += on =>
            {
                if (!on && _globalAiExpanded)
                    SetGlobalAiStatus("Chat offline \u2014 panel unreachable.", SupeyTheme.WarnText);
            };
            FormClosing += (_, __) =>
            {
                try { _teamChat?.NotifyTyping(false); _teamChat?.Dispose(); } catch { }
            };
            _teamChat.Start();
            _teamChat.SetFastPolling(_globalAiExpanded);
            _ = TeamChatLoadHistoryAsync();
        }

        private Panel _teamChatTypingHost;
        private JojoThinkingHead _teamChatJojoHead;

        private Control BuildTeamChatTypingLine()
        {
            // Host row: [Jojo head when Jojo is thinking] [who is typing ...]
            _teamChatTypingHost = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 36,
                Visible = false,
                BackColor = SupeyTheme.Surface,
                Padding = new Padding(14, 4, 14, 4),
            };
            _teamChatJojoHead = new JojoThinkingHead
            {
                Dock = DockStyle.Left,
                Width = 28,
                Visible = false,
                BackColor = SupeyTheme.Surface,
            };
            var headGap = new Panel { Dock = DockStyle.Left, Width = 10, BackColor = SupeyTheme.Surface };
            _teamChatTypingLbl = new Label
            {
                Dock = DockStyle.Fill,
                Text = "",
                AutoEllipsis = true,
                ForeColor = SupeyTheme.TextMuted,
                BackColor = SupeyTheme.Surface,
                Font = SupeyTheme.CaptionFont,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(0),
                UseMnemonic = false,
            };
            _teamChatTypingHost.Controls.Add(_teamChatTypingLbl);
            _teamChatTypingHost.Controls.Add(headGap);
            _teamChatTypingHost.Controls.Add(_teamChatJojoHead);
            headGap.Tag = "jojo-gap";
            _teamChatDotsTimer = new Timer { Interval = 420 };
            _teamChatDotsTimer.Tick += (_, __) =>
            {
                _teamChatDots = (_teamChatDots + 1) % 4;
                TeamChatRenderTyping();
            };
            return _teamChatTypingHost;
        }
        private void TeamChatApplyTheme()
        {
            if (_teamChatTypingLbl == null || _teamChatTypingLbl.IsDisposed) return;
            _teamChatTypingLbl.ForeColor = SupeyTheme.TextMuted;
            _teamChatTypingLbl.BackColor = SupeyTheme.Surface;
        }

        private string TeamChatMyName() => _teamChat?.DispatcherName ?? ScheduleActivityIdentity.DispatcherName();

        // --------------------------------------------------------------- history

        private async Task TeamChatLoadHistoryAsync()
        {
            if (_teamChatHistoryLoaded || _teamChat == null || _globalAiTranscript == null) return;
            var msgs = await _teamChat.HistoryAsync(200).ConfigureAwait(true);
            if (_globalAiTranscript == null || _globalAiTranscript.IsDisposed) return;
            if (msgs.Count == 0 && !_teamChat.Online) return; // try again next time the dock opens
            _teamChatHistoryLoaded = true;
            _globalAiTranscript.AddRange(msgs);
            _globalAiTranscript.ScrollToBottom();
        }

        // ---------------------------------------------------------------- routing

        /// <summary>"@ai what's late" → true, "what's late". Anything else stays in the room.</summary>
        private static bool TeamChatIsAiAddressed(string raw, out string forAi)
        {
            forAi = null;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            var m = TeamChatAiPrefix.Match(raw.TrimStart());
            if (!m.Success) return false;
            forAi = raw.TrimStart().Substring(m.Length).Trim();
            return true;
        }

        private ChatMessage TeamChatEchoMine(string text, bool mentionsAi)
        {
            var mine = new ChatMessage
            {
                Kind = ChatMessageKind.Me,
                Sender = TeamChatMyName(),
                ClientId = _teamChat?.ClientId ?? "",
                Text = (text ?? "").Trim(),
                Time = DateTime.Now,
                MentionsAi = mentionsAi,
                Pending = true,
            };
            _globalAiTranscript?.Add(mine);
            return mine;
        }

        /// <summary>Post my echoed line to the room and settle its pending state.</summary>
        private async Task<ChatMessage> TeamChatSendMineAsync(ChatMessage mine, bool mentionsAi)
        {
            _teamChat?.NotifyTyping(false);
            ChatMessage posted = null;
            try
            {
                if (_teamChat != null)
                    posted = await _teamChat.PostAsync(mine.Text, "user", null, mentionsAi).ConfigureAwait(true);
            }
            catch { posted = null; }
            _globalAiTranscript?.Resolve(mine, posted);
            return posted;
        }

        private async Task SendTeamMessageAsync(string text)
        {
            var mine = TeamChatEchoMine(text, mentionsAi: false);
            var posted = await TeamChatSendMineAsync(mine, mentionsAi: false).ConfigureAwait(true);
            if (posted == null)
                SetGlobalAiStatus("Not sent \u2014 panel unreachable.", SupeyTheme.ErrorText);
            else if (_globalAiCts == null)
                SetGlobalAiStatus("Ready", SupeyTheme.SuccessText);
        }

        /// <summary>The copilot answered an @ai line: put the answer in the room under the question.</summary>
        private async Task TeamChatPublishAiReplyAsync(Task<ChatMessage> askTask, ChatMessage aiLine)
        {
            if (_teamChat == null || aiLine == null || string.IsNullOrWhiteSpace(aiLine.Text)) return;
            long? replyTo = null;
            try
            {
                var asked = askTask != null ? await askTask.ConfigureAwait(true) : null;
                if (asked != null && asked.Seq > 0) replyTo = asked.Seq;
            }
            catch { }
            ChatMessage posted = null;
            try { posted = await _teamChat.PostAsync(aiLine.Text, "ai", replyTo, false).ConfigureAwait(true); }
            catch { }
            if (posted != null && _globalAiTranscript != null && !_globalAiTranscript.IsDisposed)
            {
                aiLine.Seq = posted.Seq;
                _globalAiTranscript.Resolve(aiLine, posted);
            }
        }

        // ---------------------------------------------------------------- typing

        private void TeamChatOnPromptChanged(string text)
        {
            if (_teamChat == null) return;
            bool typing = !string.IsNullOrWhiteSpace(text);
            // Talking to the copilot isn't "typing to the room" once the prefix is in.
            string _;
            if (typing && TeamChatIsAiAddressed(text, out _)) typing = false;
            _teamChat.NotifyTyping(typing);
        }

        private void OnTeamChatAutochatChanged(bool on)
        {
            if (InvokeRequired)
            {
                BeginInvoke((Action)(() => OnTeamChatAutochatChanged(on)));
                return;
            }
            ApplyTeamChatAutochatCopy(on);
        }

        private void OnTeamChatTyping(List<string> names)
        {
            if (InvokeRequired)
            {
                BeginInvoke((Action)(() => OnTeamChatTyping(names)));
                return;
            }
            _teamChatTyping = names ?? new List<string>();
            if (_teamChatTypingLbl == null || _teamChatTypingLbl.IsDisposed) return;
            bool show = _teamChatTyping.Count > 0;
            if (show && !_teamChatDotsTimer.Enabled) { _teamChatDots = 0; _teamChatDotsTimer.Start(); }
            if (!show && _teamChatDotsTimer.Enabled) _teamChatDotsTimer.Stop();
            bool jojo = show && _teamChatTyping.Any(x => string.Equals((x ?? "").Trim(), "Jojo", StringComparison.OrdinalIgnoreCase));
            if (_teamChatTypingHost != null && !_teamChatTypingHost.IsDisposed)
            {
                foreach (Control c in _teamChatTypingHost.Controls)
                    if (Equals(c.Tag, "jojo-gap")) c.Visible = jojo;
                _teamChatTypingHost.Visible = show;
            }
            if (_teamChatJojoHead != null && !_teamChatJojoHead.IsDisposed) _teamChatJojoHead.Visible = jojo;
            _teamChatTypingLbl.Visible = show;
            TeamChatRenderTyping();
        }

        private void TeamChatRenderTyping()
        {
            if (_teamChatTypingLbl == null || _teamChatTypingLbl.IsDisposed || _teamChatTyping.Count == 0) return;
            string who;
            var n = _teamChatTyping;
            if (n.Count == 1 && string.Equals((n[0] ?? "").Trim(), "Jojo", StringComparison.OrdinalIgnoreCase)) who = "Jojo is thinking";
            else if (n.Count == 1) who = n[0] + " is typing";
            else if (n.Count == 2) who = n[0] + " and " + n[1] + " are typing";
            else who = n[0] + ", " + n[1] + " and " + (n.Count - 2) + " more are typing";
            string dots = new string('\u00b7', _teamChatDots) + new string(' ', 3 - _teamChatDots);
            _teamChatTypingLbl.Text = who + " " + dots;
        }

        // -------------------------------------------------------------- arrivals

        private void OnTeamChatMessages(List<ChatMessage> msgs)
        {
            if (msgs == null || msgs.Count == 0 || _globalAiTranscript == null || _globalAiTranscript.IsDisposed) return;
            if (InvokeRequired) { BeginInvoke((Action)(() => OnTeamChatMessages(msgs))); return; }
            using (UiStallWatch.Measure(UiScope.ChatPoll))
                ApplyTeamChatMessages(msgs);
        }

        private void ApplyTeamChatMessages(List<ChatMessage> msgs)
        {
            string me = _teamChat?.ClientId ?? "";
            // While autochat is on the panel posts the only AI line, so keep it even if
            // it is stamped with this desk. Otherwise that line was drawn here already.
            bool panelAnswers = _teamChat != null && _teamChat.Autochat;
            var fresh = new List<ChatMessage>();
            foreach (var m in msgs)
            {
                if (m == null) continue;
                if (!panelAnswers && m.Kind == ChatMessageKind.Ai && string.Equals(m.ClientId, me, StringComparison.Ordinal)) continue;
                if (m.Kind == ChatMessageKind.Me) continue;
                if (_globalAiTranscript.Contains(m.Seq)) continue;
                fresh.Add(m);
            }
            if (fresh.Count == 0) return;
            foreach (var m in fresh)
                _globalAiTranscript.Add(m);

            if (_globalAiExpanded) return;

            _teamChatUnread += fresh.Count;
            TitleBarAiBadge = _teamChatUnread;
            foreach (var m in fresh)
                TeamChatToast(m);
        }

        private void TeamChatToast(ChatMessage m)
        {
            if (_schedActStack == null || m == null) return;
            string body = (m.Text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            if (body.Length > TeamChatToastMaxChars) body = body.Substring(0, TeamChatToastMaxChars - 1) + "\u2026";
            var t = new ScheduleActivityToast
            {
                Kind = ScheduleToastKind.Chat,
                Who = m.Kind == ChatMessageKind.Ai ? "AI" : (string.IsNullOrEmpty(m.Sender) ? "Someone" : m.Sender),
                Verb = "chat",
                SourceClientId = m.ClientId ?? "",
                EventTs = new DateTimeOffset(m.Time).ToUnixTimeMilliseconds() / 1000.0,
                LifetimeMs = 10000,
                ActionText = "Open",
                Payload = m,
            };
            t.SetRuns(new[] { ScheduleToastRun.Body(body) });
            var merged = _schedActStack.Push(t, n =>
                n.Kind == ScheduleToastKind.Chat
                && string.Equals(n.SourceClientId, t.SourceClientId, StringComparison.Ordinal)
                && string.Equals(n.Who, t.Who, StringComparison.Ordinal));
            if (merged != null && !ReferenceEquals(merged, t))
            {
                merged.Count += 1;
                merged.EventTs = t.EventTs;
                merged.Payload = m;
                merged.SetRuns(new[] { ScheduleToastRun.Body(body) });
            }
        }

        private void OnTeamChatToastClicked(ScheduleActivityToast toast)
        {
            if (toast == null) return;
            _schedActStack?.Dismiss(toast);
            SetGlobalAiDockExpanded(true);
            var m = toast.Payload as ChatMessage;
            if (m != null && m.Seq > 0) _globalAiTranscript?.ScrollToSeq(m.Seq);
        }

        // ---------------------------------------------------------------- dock

        private void TeamChatOnDockToggled(bool expanded)
        {
            _teamChat?.SetFastPolling(expanded);
            if (!expanded)
            {
                _teamChat?.NotifyTyping(false);
                return;
            }
            _teamChatUnread = 0;
            TitleBarAiBadge = 0;
            _schedActStack?.DismissWhere(x => x.Kind == ScheduleToastKind.Chat);
            if (!_teamChatHistoryLoaded) _ = TeamChatLoadHistoryAsync();
            else _globalAiTranscript?.ScrollToBottom();
        }

        /// <summary>
        /// Little animated Jojo avatar (white round head, two slanted eyes) that glances
        /// around while Jojo is working on an answer in the team chat.
        /// </summary>
        private sealed class JojoThinkingHead : Control
        {
            private readonly Timer _tick = new Timer { Interval = 16 };
            private readonly Random _rng = new Random();
            private float _gx, _gy, _tx, _ty, _tilt, _ttilt;
            private DateTime _nextLook = DateTime.MinValue, _blinkStart = DateTime.MinValue, _nextBlink = DateTime.MinValue;
            private readonly DateTime _t0 = DateTime.UtcNow;

            public JojoThinkingHead()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                       | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
                _tick.Tick += (_, __) => Step();
            }

            // Animate whenever it is actually on screen.
            protected override void OnVisibleChanged(EventArgs e)
            {
                base.OnVisibleChanged(e);
                if (Visible) { _nextLook = DateTime.MinValue; _nextBlink = DateTime.UtcNow.AddMilliseconds(900); _tick.Start(); }
                else { _tick.Stop(); _gx = _gy = _tx = _ty = _tilt = _ttilt = 0; }
            }
            protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); if (Visible) _tick.Start(); }

            private void Step()
            {
                if (!IsHandleCreated || IsDisposed) return;
                var now = DateTime.UtcNow;
                if (now >= _nextLook)
                {
                    int pick = _rng.Next(6);
                    if (pick == 0) { _tx = 0; _ty = 0; }
                    else
                    {
                        _tx = (pick % 2 == 0 ? -1f : 1f) * (0.55f + (float)_rng.NextDouble() * 0.45f);
                        _ty = (float)(_rng.NextDouble() * 1.4 - 0.8);
                    }
                    _ttilt = _tx * 7f;
                    _nextLook = now.AddMilliseconds(500 + _rng.Next(1100));
                }
                if (now >= _nextBlink)
                {
                    _blinkStart = now;
                    _nextBlink = now.AddMilliseconds(_rng.Next(5) == 0 ? 260 : 2000 + _rng.Next(2800));
                }
                // eased follow
                _gx += (_tx - _gx) * 0.16f;
                _gy += (_ty - _gy) * 0.16f;
                _tilt += (_ttilt - _tilt) * 0.10f;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(BackColor);
                float d = Math.Min(ClientSize.Width, ClientSize.Height) - 4f;
                if (d < 8) return;
                double ms = (DateTime.UtcNow - _t0).TotalMilliseconds;
                float bob = (float)Math.Sin(ms / 320.0) * 1.1f;
                float cxH = ClientSize.Width / 2f, cyH = ClientSize.Height / 2f + bob - 0.5f;

                var state = g.Save();
                g.TranslateTransform(cxH, cyH);
                g.RotateTransform(_tilt);

                // soft shadow + head
                using (var sh = new SolidBrush(Color.FromArgb(55, 0, 0, 0)))
                    g.FillEllipse(sh, -d / 2f + 0.6f, -d / 2f + 1.4f, d, d);
                var head = new RectangleF(-d / 2f, -d / 2f, d, d);
                using (var lg = new System.Drawing.Drawing2D.LinearGradientBrush(head, Color.White, Color.FromArgb(222, 224, 228), 90f))
                    g.FillEllipse(lg, head);
                using (var rim = new Pen(Color.FromArgb(70, 0, 0, 0), 1f))
                    g.DrawEllipse(rim, head);

                // eyes: slanted rounded strokes upper-right, squash on blink
                float blink = 1f;
                double bt = (DateTime.UtcNow - _blinkStart).TotalMilliseconds;
                if (bt >= 0 && bt < 150) blink = (float)Math.Abs(Math.Cos(bt / 150.0 * Math.PI)) * 0.85f + 0.15f;
                float ex = d * 0.12f + _gx * d * 0.12f;
                float ey = -d * 0.16f + _gy * d * 0.08f;
                float len = d * 0.20f * blink;
                float w = Math.Max(2f, d * 0.12f);
                using (var pen = new Pen(Color.FromArgb(28, 30, 34), w)
                { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round })
                {
                    DrawEye(g, pen, ex - d * 0.12f, ey + d * 0.04f, len);
                    DrawEye(g, pen, ex + d * 0.12f, ey - d * 0.03f, len);
                }
                g.Restore(state);
            }

            private static void DrawEye(Graphics g, Pen pen, float x, float y, float len)
            {
                float dx = len * 0.50f, dy = len * 0.87f;   // ~30 degree slant
                g.DrawLine(pen, x - dx / 2, y - dy / 2, x + dx / 2, y + dy / 2);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) { _tick.Stop(); _tick.Dispose(); }
                base.Dispose(disposing);
            }
        }
    }
}