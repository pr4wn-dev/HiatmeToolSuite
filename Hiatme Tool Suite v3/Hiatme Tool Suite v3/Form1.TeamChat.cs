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
    /// to the copilot and its answer is posted back for every desk. Typing shows above the
    /// composer; while the dock is hidden, arrivals become HUD toasts and a badge on the
    /// title-bar AI chip.
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

        private Control BuildTeamChatTypingLine()
        {
            _teamChatTypingLbl = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 18,
                Visible = false,
                Text = "",
                ForeColor = SupeyTheme.TextMuted,
                BackColor = SupeyTheme.Surface,
                Font = SupeyTheme.CaptionFont,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(2, 0, 0, 0),
                UseMnemonic = false,
            };
            _teamChatDotsTimer = new Timer { Interval = 420 };
            _teamChatDotsTimer.Tick += (_, __) =>
            {
                _teamChatDots = (_teamChatDots + 1) % 4;
                TeamChatRenderTyping();
            };
            return _teamChatTypingLbl;
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
            _teamChatTypingLbl.Visible = show;
            TeamChatRenderTyping();
        }

        private void TeamChatRenderTyping()
        {
            if (_teamChatTypingLbl == null || _teamChatTypingLbl.IsDisposed || _teamChatTyping.Count == 0) return;
            string who;
            var n = _teamChatTyping;
            if (n.Count == 1) who = n[0] + " is typing";
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
            var fresh = new List<ChatMessage>();
            foreach (var m in msgs)
            {
                if (m == null) continue;
                // My own AI answers were drawn (and posted) here already.
                if (m.Kind == ChatMessageKind.Ai && string.Equals(m.ClientId, me, StringComparison.Ordinal)) continue;
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
    }
}
