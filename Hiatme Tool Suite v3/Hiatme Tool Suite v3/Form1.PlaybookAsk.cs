using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Playbook questions asked in the corner, where the dispatcher is already looking.
    ///
    /// The panel can only learn what a client's real timing tolerance is by being told, and the
    /// only place it ever asked was inside the AI dock. Nobody building a schedule has the dock
    /// open, so the question sat on a panel nobody was looking at: hundreds of clients had enough
    /// history to be asked about and not one had ever been answered. Moving the ask to the toast
    /// corner puts it in the same place as everything else that interrupts, and makes it
    /// answerable in one click without leaving the board.
    ///
    /// The gates below all exist to keep this from becoming a thing people learn to dismiss. It
    /// asks only when the app is in front, never on top of another toast, and backs off hard when
    /// a question is ignored rather than asking again on the next tick.
    /// </summary>
    public partial class Form1
    {
        private System.Windows.Forms.Timer _askTimer;
        private HiatmeAssistantQuestion _askPending;
        private ScheduleActivityToast _askToast;
        private int _askFetching;
        private readonly System.Diagnostics.Stopwatch _askClock = System.Diagnostics.Stopwatch.StartNew();
        private long _askNextAtMs;

        // A question is worth asking, not worth pressing. The first gap is short enough that a
        // working session gets through a few; ignoring one means the dispatcher is busy, so the
        // next gap is long enough to be out of the way rather than one tick later.
        private const int AskGapMs = 5 * 60_000;
        private const int AskGapAfterIgnoredMs = 25 * 60_000;
        private const int AskTickMs = 20_000;

        // Long enough to read a sentence and decide, short enough that an unanswered one is gone
        // before it becomes scenery. Hovering pauses this, so reading it does not race the fuse.
        private const int AskToastLifetimeMs = 30_000;

        private void InitPlaybookAsk()
        {
            if (_askTimer != null) return;
            _askNextAtMs = _askClock.ElapsedMilliseconds + AskGapMs;
            _askTimer = new System.Windows.Forms.Timer { Interval = AskTickMs };
            _askTimer.Tick += (_, __) => MaybeAskPlaybookQuestion();
            _askTimer.Start();
        }

        private void ShutdownPlaybookAsk()
        {
            try { _askTimer?.Stop(); _askTimer?.Dispose(); } catch { }
            _askTimer = null;
        }

        private void MaybeAskPlaybookQuestion()
        {
            if (_askPending != null || _askToast != null) return;
            if (_schedActStack == null) return;
            if (_askClock.ElapsedMilliseconds < _askNextAtMs) return;

            // Not in front: a question asked at an unattended desk expires unread and burns its
            // slot. Waiting costs nothing — the client will still be there later.
            if (!ContainsFocus) return;

            // The dock asks for itself when it is open, and two copies of the same question with
            // two Skip buttons is a way to record an answer twice.
            if (_globalAiExpanded || _globalAiPendingQuestion != null) return;

            // Never interrupt something already being read. Saves, conflicts and chat all matter
            // more in the moment than confirming a client's tolerance.
            if (_schedActStack.VisibleCount > 0) return;

            if (Interlocked.CompareExchange(ref _askFetching, 1, 0) != 0) return;
            _ = Task.Run(async () =>
            {
                HiatmeAssistantQuestion q = null;
                try
                {
                    // LoadNoProbe: this runs on a timer, and the probing Load walks the panel
                    // candidate list with multi-second timeouts on the calling thread.
                    var s = _globalAiSettings ?? HiatmeAiSettings.LoadNoProbe();
                    q = await HiatmeAiClient.GetAssistantQuestionAsync(
                        s, ScheduleActivityCurrentDayIso() ?? "").ConfigureAwait(false);
                }
                catch { }
                finally { Interlocked.Exchange(ref _askFetching, 0); }

                if (q == null || string.IsNullOrWhiteSpace(q.Text))
                {
                    // Nothing to ask is a normal answer, not a failure. Wait a full gap so an
                    // empty queue is not re-polled every 20 seconds all day.
                    try
                    {
                        if (!IsDisposed && IsHandleCreated)
                            BeginInvoke((Action)(() => _askNextAtMs = _askClock.ElapsedMilliseconds + AskGapMs));
                    }
                    catch { }
                    return;
                }

                try
                {
                    if (IsDisposed || !IsHandleCreated) return;
                    BeginInvoke((Action)(() => ShowPlaybookAskToast(q)));
                }
                catch { }
            });
        }

        private void ShowPlaybookAskToast(HiatmeAssistantQuestion q)
        {
            if (_askPending != null || _askToast != null) return;
            if (_schedActStack == null || q == null) return;
            // Conditions can change between the fetch starting and the reply arriving.
            if (_globalAiExpanded || _schedActStack.VisibleCount > 0)
            {
                _askNextAtMs = _askClock.ElapsedMilliseconds + AskGapMs;
                return;
            }

            _askPending = q;
            var toast = new ScheduleActivityToast
            {
                Kind = ScheduleToastKind.Question,
                Who = "Playbook",
                Verb = "asks",
                EventTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0,
                LifetimeMs = AskToastLifetimeMs,
                ActionText = string.IsNullOrWhiteSpace(q.YesLabel) ? "Yes" : q.YesLabel.Trim(),
                SecondaryActionText = string.IsNullOrWhiteSpace(q.SkipLabel) ? "Not sure" : q.SkipLabel.Trim(),
                RequireActionClick = true,
                Payload = q,
            };

            var runs = new List<ScheduleToastRun>();
            if (!string.IsNullOrWhiteSpace(q.Client))
                runs.Add(ScheduleToastRun.Chip(q.Client.Trim()));
            runs.Add(ScheduleToastRun.Body(q.Text.Trim()));
            toast.SetRuns(runs);

            _askToast = toast;
            _schedActStack.Push(toast);
        }

        /// <summary>Yes or Skip from the corner toast. Same recording path as the dock card.</summary>
        private void AnswerPlaybookAsk(ScheduleActivityToast toast, string action)
        {
            var q = (toast?.Payload as HiatmeAssistantQuestion) ?? _askPending;
            ClearPlaybookAsk();
            if (q == null) return;

            // Answering means they are engaged, so the next question comes at the normal gap even
            // if this one was a Skip — "I don't know about this client" is not "stop asking me".
            _askNextAtMs = _askClock.ElapsedMilliseconds + AskGapMs;

            _ = Task.Run(async () =>
            {
                bool ok = false;
                try
                {
                    var s = _globalAiSettings ?? HiatmeAiSettings.LoadNoProbe();
                    await HiatmeAiClient.AnswerAssistantQuestionAsync(s, q, action ?? "skip")
                        .ConfigureAwait(false);
                    ok = true;
                }
                catch { }

                try
                {
                    if (IsDisposed || !IsHandleCreated) return;
                    BeginInvoke((Action)(() => ConfirmPlaybookAsk(q, action, ok)));
                }
                catch { }
            });
        }

        private void ConfirmPlaybookAsk(HiatmeAssistantQuestion q, string action, bool ok)
        {
            if (_schedActStack == null) return;
            bool yes = string.Equals(action, "yes", StringComparison.OrdinalIgnoreCase);

            var toast = new ScheduleActivityToast
            {
                Kind = ok ? (yes ? ScheduleToastKind.Saved : ScheduleToastKind.Muted)
                          : ScheduleToastKind.Rejected,
                Who = "Playbook",
                Verb = ok ? (yes ? "learned" : "will ask later") : "not saved",
                EventTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0,
                LifetimeMs = ok ? 4000 : 8000,
            };
            var runs = new List<ScheduleToastRun>();
            if (!string.IsNullOrWhiteSpace(q?.Client))
                runs.Add(ScheduleToastRun.Chip(q.Client.Trim()));
            runs.Add(ScheduleToastRun.Body(
                !ok ? "Could not reach the panel — nothing was recorded."
                    : yes ? "Saved. This will be honoured when placing this client."
                          : "Left alone. You will be asked again another day."));
            toast.SetRuns(runs);
            _schedActStack.Push(toast);
        }

        private void ClearPlaybookAsk()
        {
            var t = _askToast;
            _askToast = null;
            _askPending = null;
            if (t != null)
            {
                try { _schedActStack?.Dismiss(t); } catch { }
            }
        }

        /// <summary>
        /// The question timed out on screen. Nothing is recorded — an unanswered question is not
        /// a Skip, and writing one would put an opinion in the playbook that nobody held.
        /// </summary>
        private void PlaybookAskExpired()
        {
            _askToast = null;
            _askPending = null;
            _askNextAtMs = _askClock.ElapsedMilliseconds + AskGapAfterIgnoredMs;
        }
    }
}
