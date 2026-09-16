using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Schedule activity feed — the bottom-right toasts that narrate what other desks
    /// are doing in the Schedule Builder, the presence pill, and the history drawer.
    /// Edits are captured at <see cref="FsPushUndoSnapshot"/> (the one funnel every
    /// Schedule Builder mutation goes through); rich from/to detail is staged just
    /// before that call at the sites that know it.
    /// </summary>
    public partial class Form1
    {
        private ScheduleActivityFeed _schedActFeed;
        private ScheduleActivityToastStack _schedActStack;
        private ScheduleActivityDrawer _schedActDrawer;
        private SchedulePresencePill _schedActPresencePill;
        private int _schedActUnsaved;
        private string _schedActOpenDay = "";
        private string _schedActCutFromTab;
        private StagedScheduleActivity _schedActPending;
        private readonly List<ScheduleActivityEvent> _schedActRecent = new List<ScheduleActivityEvent>();
        private const int SchedActRecentCap = 200;

        private sealed class StagedScheduleActivity
        {
            public string Verb;
            public ScheduleActivityDetail Detail;
            public DateTime StagedUtc = DateTime.UtcNow;
        }

        // ------------------------------------------------------------------ init

        private void InitScheduleActivityFeed()
        {
            if (_schedActFeed != null) return;

            // Proves which scope is on the UI thread during a freeze, on this desk and
            // on every other one, instead of us reasoning about it from the source.
            UiStallWatch.Start();
            UiStallWatch.StartReporting(() => HiatmeAiSettings.LoadNoProbe());

            _schedActFeed = new ScheduleActivityFeed(() => HiatmeAiSettings.LoadNoProbe())
            {
                ServiceDateProvider = ScheduleActivityCurrentDayIso,
                UnsavedCountProvider = () => _schedActUnsaved,
                LocalRevisionProvider = ScheduleActivityLocalRevision,
                BuilderVisibleProvider = () => hiatmeTabControl?.SelectedTab == tabPage6,
            };
            _schedActFeed.EventsArrived += OnScheduleActivityEvents;
            _schedActFeed.PresenceUpdated += OnScheduleActivityPresence;
            _schedActFeed.HeadUpdated += OnScheduleActivityHead;
            _schedActFeed.ConnectivityChanged += on =>
            {
                if (_schedActPresencePill != null) _schedActPresencePill.Online = on;
            };

            _schedActStack = new ScheduleActivityToastStack(this, ScheduleActivityBottomInset);
            _schedActStack.ToastClicked += OnScheduleActivityToastClicked;
            _schedActStack.ToastSecondaryClicked += t =>
            {
                if (t != null && t.Kind == ScheduleToastKind.Question) AnswerPlaybookAsk(t, "skip");
            };
            _schedActStack.ToastExpired += t =>
            {
                if (t != null && t.Kind == ScheduleToastKind.Question) PlaybookAskExpired();
            };

            _schedActDrawer = new ScheduleActivityDrawer();
            _schedActDrawer.SetIdentity(_schedActFeed.ClientId);
            Controls.Add(_schedActDrawer);
            Resize += (_, __) =>
            {
                RepositionScheduleActivityDrawer();
                RepositionSchedulePresencePill();
            };
            RepositionScheduleActivityDrawer();

            InstallSchedulePresencePill();
            InitDeskPoke();
            InitPlaybookAsk();

            if (hiatmeTabControl != null)
            {
                hiatmeTabControl.SelectedIndexChanged += (_, __) =>
                    _schedActFeed?.SetFastPolling(hiatmeTabControl.SelectedTab == tabPage6);
            }
            FormClosing += (_, __) => ScheduleActivityShutdown();

            _schedActFeed.Start();
            _schedActFeed.SetFastPolling(hiatmeTabControl?.SelectedTab == tabPage6);
        }

        protected override Rectangle TitleBarAuxBounds
        {
            get
            {
                if (_schedActPresencePill == null || _schedActPresencePill.IsDisposed || !_schedActPresencePill.Visible)
                    return Rectangle.Empty;
                return _schedActPresencePill.Bounds;
            }
        }

        private void InstallSchedulePresencePill()
        {
            if (_schedActPresencePill != null) return;
            _schedActPresencePill = new SchedulePresencePill
            {
                Visible = true,
            };
            _schedActPresencePill.MouseUp += OnSchedulePresencePillMouseUp;
            _schedActPresencePill.LayoutChanged += (_, __) => RepositionSchedulePresencePill();
            Controls.Add(_schedActPresencePill);
            _schedActPresencePill.SetOthers(new List<SchedulePresenceEntry>(), null);
            RepositionSchedulePresencePill();
        }

        private void RepositionSchedulePresencePill()
        {
            if (_schedActPresencePill == null || _schedActPresencePill.IsDisposed) return;
            var theme = TitleBarThemeButtonBounds;
            var ai = TitleBarAiButtonBounds;
            int right = ClientSize.Width - (46 * 3) - 8;
            if (!ai.IsEmpty) right = ai.Left;
            if (!theme.IsEmpty) right = Math.Min(right, theme.Left);

            // Keep the pill near the app title instead of floating in the center gap.
            int titleLeft = 16 + TitleLeftInset;
            if (ShowNavMenuButton && TitleLeadingGutterWidth > 0)
            {
                int navX = Math.Max(4, (TitleLeadingGutterWidth - 38) / 2);
                titleLeft = Math.Max(titleLeft, navX + 38 + 8);
            }
            if (ShowIcon && Icon != null)
                titleLeft += 28;

            int titleW = TextRenderer.MeasureText(
                Text ?? string.Empty,
                SupeyTheme.HeaderFont,
                new Size(int.MaxValue, ChromeTitleHeight),
                TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Width;
            int preferredX = titleLeft + titleW + 16;
            int x = Math.Min(
                Math.Max(8, preferredX),
                Math.Max(8, right - 8 - Math.Max(10, _schedActPresencePill.Width)));
            int y = Math.Max(0, (ChromeTitleHeight - _schedActPresencePill.Height) / 2);
            var want = new Point(x, y);
            if (_schedActPresencePill.Location != want)
                _schedActPresencePill.Location = want;
            _schedActPresencePill.BringToFront();
            if (_deskPokeOverlay != null && _deskPokeOverlay.Visible)
                _deskPokeOverlay.BringToFront();
            Invalidate(new Rectangle(0, 0, Math.Max(1, ClientSize.Width), ChromeTitleHeight));
        }

        private void OnSchedulePresencePillMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left && e.Button != MouseButtons.Right) return;
            ShowDeskPokeMenu(_schedActPresencePill, e.Location);
        }

        private int ScheduleActivityBottomInset()
        {
            // Sit above the status band + update link on every tab (13 / 41 / 8 rhythm).
            return ScheduleActivityToastStack.InsetBottom;
        }

        private void RepositionScheduleActivityDrawer()
        {
            if (_schedActDrawer == null || _schedActDrawer.IsDisposed) return;
            try
            {
                _schedActDrawer.Reposition(this, ChromeTitleHeight, 0);
                if (_schedActDrawer.Visible) _schedActDrawer.BringToFront();
            }
            catch { }
        }

        private void ScheduleActivityShutdown()
        {
            try
            {
                if (_schedActFeed == null) return;
                if (!string.IsNullOrEmpty(_schedActOpenDay))
                    _schedActFeed.Emit("closed", _schedActOpenDay, null);
                var flush = _schedActFeed.FlushNowAsync();
                var bye = _schedActFeed.HeartbeatAsync(builderOpen: false);
                Task.WhenAll(flush, bye).Wait(TimeSpan.FromMilliseconds(400));
            }
            catch { }
            finally
            {
                try { ShutdownPlaybookAsk(); } catch { }
                try { _schedActFeed?.Dispose(); } catch { }
                try { _schedActStack?.Dispose(); } catch { }
                try { UiStallWatch.FlushReport("shutdown"); } catch { }
            }
        }

        // --------------------------------------------------------------- providers

        private string ScheduleActivityCurrentDayIso()
        {
            if (!_fsHasPreview) return "";
            DateTime d = fsbdatepicker?.Value.Date ?? DateTime.Today;
            if (fsbuilder != null)
            {
                try { d = fsbuilder.ServiceDate; } catch { }
            }
            return d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        private static readonly Stopwatch SchedActClock = Stopwatch.StartNew();
        private int _schedActRevValue;
        private string _schedActRevPath = "";
        private long _schedActRevAtMs = -1;
        private int _schedActRevReading;

        /// <summary>
        /// Our revision for the day on screen, read from memory only.
        ///
        /// The underlying read is File.Exists + a sidecar read against Desktop, which is
        /// OneDrive-synced on every desk, so it can block for seconds when the folder is
        /// syncing or the file is still a cloud placeholder. Callers are all on the UI thread
        /// and one of them runs per arriving save event, so a blocking read here showed up as
        /// the window freezing exactly while toasts appeared. The value only moves when we
        /// save or pull, so a slightly stale number is always better than a stalled window.
        /// </summary>
        private int ScheduleActivityLocalRevision()
        {
            string p = fsbuilder?.LastExportPath ?? "";
            if (!string.Equals(p, _schedActRevPath, StringComparison.OrdinalIgnoreCase))
            {
                _schedActRevPath = p;
                _schedActRevValue = 0;
                _schedActRevAtMs = -1;
            }
            if (!string.IsNullOrWhiteSpace(p) &&
                (_schedActRevAtMs < 0 || SchedActClock.ElapsedMilliseconds - _schedActRevAtMs > 4000))
                ScheduleActivityRefreshRevision(p);
            return _schedActRevValue;
        }

        /// <summary>Re-read the revision sidecar off the UI thread; one read in flight at a time.</summary>
        private void ScheduleActivityRefreshRevision(string path)
        {
            if (Interlocked.CompareExchange(ref _schedActRevReading, 1, 0) != 0) return;
            _ = Task.Run(() =>
            {
                int rev = 0;
                try
                {
                    if (File.Exists(path)) rev = ScheduleWorkbookResolver.ReadLocalRevision(path);
                }
                catch { rev = 0; }
                finally { Interlocked.Exchange(ref _schedActRevReading, 0); }

                try
                {
                    if (IsDisposed || !IsHandleCreated) return;
                    BeginInvoke((Action)(() =>
                    {
                        if (!string.Equals(path, _schedActRevPath, StringComparison.OrdinalIgnoreCase)) return;
                        _schedActRevValue = rev;
                        _schedActRevAtMs = SchedActClock.ElapsedMilliseconds;
                    }));
                }
                catch { }
            });
        }

        /// <summary>Called after we publish, so the cached revision reflects our own save at once.</summary>
        private void ScheduleActivityNoteLocalRevision(int rev)
        {
            if (rev <= 0) return;
            _schedActRevValue = rev;
            _schedActRevAtMs = SchedActClock.ElapsedMilliseconds;
        }

        /// <summary>Emit opened/closed when the day on screen changes; cheap, called from heartbeat/edits.</summary>
        private void ScheduleActivityTrackOpenDay()
        {
            if (_schedActFeed == null) return;
            string now = ScheduleActivityCurrentDayIso();
            if (string.Equals(now, _schedActOpenDay, StringComparison.Ordinal)) return;
            if (!string.IsNullOrEmpty(_schedActOpenDay))
                _schedActFeed.Emit("closed", _schedActOpenDay, null);
            if (!string.IsNullOrEmpty(now))
                _schedActFeed.Emit("opened", now, null);
            _schedActOpenDay = now;
            _schedActUnsaved = 0;
        }

        // -------------------------------------------------------------------- emit

        /// <summary>
        /// Called by the site that knows the real from/to right before it pushes the undo
        /// snapshot. Consumed by <see cref="ScheduleActivityOnEdit"/>; expires after 2s so a
        /// stage without a matching snapshot can never mislabel a later edit.
        /// </summary>
        private void ScheduleActivityStage(
            string verb,
            IEnumerable<MCDownloadedTrip> trips,
            string fromTab = null,
            string toTab = null,
            string label = null)
        {
            _schedActPending = new StagedScheduleActivity
            {
                Verb = verb,
                Detail = ScheduleActivityDetailFor(trips, fromTab, toTab, label),
            };
        }

        private static ScheduleActivityDetail ScheduleActivityDetailFor(
            IEnumerable<MCDownloadedTrip> trips, string fromTab, string toTab, string label)
        {
            var d = new ScheduleActivityDetail();
            if (!string.IsNullOrWhiteSpace(fromTab)) d.FromTab = fromTab.Trim();
            if (!string.IsNullOrWhiteSpace(toTab)) d.ToTab = toTab.Trim();
            if (!string.IsNullOrWhiteSpace(label)) d.Label = label.Trim();
            var list = (trips ?? Enumerable.Empty<MCDownloadedTrip>()).Where(t => t != null).ToList();
            if (list.Count > 0)
            {
                d.Count = list.Count;
                d.Trips = list.Take(12).Select(t => new ScheduleActivityTrip
                {
                    Trip = (t.TripNumber ?? "").Trim(),
                    Client = (t.ClientFullName ?? "").Trim(),
                    PuTime = string.IsNullOrWhiteSpace(t.PUTime) ? null : t.PUTime.Trim(),
                }).ToList();
            }
            return d;
        }

        /// <summary>Hook from <see cref="FsPushUndoSnapshot"/>: turn the undo label into a feed event.</summary>
        private void ScheduleActivityOnEdit(string undoLabel)
        {
            if (_schedActFeed == null) return;
            try
            {
                ScheduleActivityTrackOpenDay();
                string day = _schedActOpenDay;
                if (string.IsNullOrEmpty(day)) return;

                string verb;
                ScheduleActivityDetail detail;
                var staged = _schedActPending;
                _schedActPending = null;
                if (staged != null && (DateTime.UtcNow - staged.StagedUtc).TotalSeconds < 2)
                {
                    verb = staged.Verb;
                    detail = staged.Detail;
                }
                else
                {
                    verb = ScheduleActivityVerbFromLabel(undoLabel);
                    detail = ScheduleActivityDetailFor(
                        SafeSelectedTrips(), null, _fsActiveDriverTab, undoLabel);
                    if (verb == "suggest" && !string.IsNullOrEmpty(undoLabel))
                    {
                        int arrow = undoLabel.IndexOf('\u2192');
                        if (arrow >= 0 && arrow + 1 < undoLabel.Length)
                            detail.ToTab = undoLabel.Substring(arrow + 1).Trim();
                    }
                }
                _schedActUnsaved++;
                _schedActFeed.Emit(verb, day, detail);
                _schedActPresencePill?.SetMine(_schedActUnsaved);
            }
            catch { }
        }

        private void ScheduleActivityOnUndoRedo(bool undo, string label)
        {
            if (_schedActFeed == null) return;
            try
            {
                ScheduleActivityTrackOpenDay();
                if (string.IsNullOrEmpty(_schedActOpenDay)) return;
                _schedActUnsaved++;
                _schedActFeed.Emit(undo ? "undo" : "redo", _schedActOpenDay,
                    new ScheduleActivityDetail { Label = label, Tab = _fsActiveDriverTab });
            }
            catch { }
        }

        private List<MCDownloadedTrip> SafeSelectedTrips()
        {
            try { return FsCollectSelectedTrips() ?? new List<MCDownloadedTrip>(); }
            catch { return new List<MCDownloadedTrip>(); }
        }

        private static string ScheduleActivityVerbFromLabel(string label)
        {
            string l = (label ?? "").Trim().ToLowerInvariant();
            if (l.StartsWith("suggest driver")) return "suggest";
            if (l.StartsWith("merge")) return "merged";
            if (l.StartsWith("move")) return "moved";
            if (l.StartsWith("cut")) return "cut";
            if (l.StartsWith("delete blank") || l.StartsWith("insert blank")) return "blank_row";
            if (l.StartsWith("delete note") || l.StartsWith("edit note") || l.StartsWith("edit group note")
                || l.StartsWith("add note")) return "note";
            if (l.StartsWith("delete")) return "delete";
            if (l.StartsWith("insert")) return "paste";
            if (l.StartsWith("reroute") || l.Contains("to reroutes")) return "reroute";
            if (l.Contains("to cancels")) return "cancel";
            if (l.StartsWith("auto-sort")) return "sort";
            if (l.Contains("color")) return "color";
            return "edit";
        }

        /// <summary>Server accepted our workbook: everything emitted so far is now published.</summary>
        private void ScheduleActivityOnPublished(string iso, int revision)
        {
            _schedActUnsaved = 0;
            try
            {
                if (InvokeRequired) { BeginInvoke((Action)(() => ScheduleActivityOnPublished(iso, revision))); return; }
                ScheduleActivityNoteLocalRevision(revision);
                _schedActPresencePill?.SetMine(0);
                // A newer-revision warning for this day is moot once we have just published over it.
                _schedActStack?.DismissWhere(t =>
                    (t.Kind == ScheduleToastKind.Behind || t.Kind == ScheduleToastKind.Rejected)
                    && string.Equals(t.ServiceDate, iso, StringComparison.Ordinal));
            }
            catch { }
        }

        /// <summary>Server refused our workbook (someone published first). Sticky toast, click = LOAD.</summary>
        private void ScheduleActivityOnRejected(string iso, HiatmeScheduleWorkbookMeta result)
        {
            try
            {
                if (InvokeRequired) { BeginInvoke((Action)(() => ScheduleActivityOnRejected(iso, result))); return; }
                if (_schedActStack == null) return;
                _schedActStack.DismissWhere(t =>
                    t.Kind == ScheduleToastKind.Rejected && string.Equals(t.ServiceDate, iso, StringComparison.Ordinal));
                int serverRev = result?.Revision ?? 0;
                int mine = ScheduleActivityLocalRevision();
                var toast = new ScheduleActivityToast
                {
                    Kind = ScheduleToastKind.Rejected,
                    Who = "Save rejected",
                    Verb = "rejected",
                    ServiceDate = iso,
                    EventTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0,
                    LifetimeMs = 0,
                    ActionText = "Load, then redo",
                };
                var runs = new List<ScheduleToastRun>();
                if (mine <= 0)
                {
                    // Not a race with another desk — this desk cannot say which published version
                    // it started from, so the server has no way to tell a current save from one
                    // about to overwrite newer work, and refuses. Saying "someone beat you to it"
                    // here sends the dispatcher looking for a colleague who did nothing.
                    runs.Add(ScheduleToastRun.Strong("This desk"));
                    runs.Add(ScheduleToastRun.Body("lost track of which version of "
                        + ScheduleActivityFormat.ShortDate(iso) + " it started from, so the save"
                        + " was not written. Your edits are backed up. Load to get current."));
                }
                else
                {
                    string who = _schedActRecent
                        .Where(e => e.Verb == "saved" && e.ServiceDate == iso)
                        .OrderByDescending(e => e.Seq)
                        .Select(e => e.Dispatcher)
                        .FirstOrDefault();
                    runs.Add(ScheduleToastRun.Strong(string.IsNullOrEmpty(who) ? "Another desk" : who));
                    runs.Add(ScheduleToastRun.Body("published"));
                    if (serverRev > 0) runs.Add(ScheduleToastRun.Mono("rev " + serverRev));
                    runs.Add(ScheduleToastRun.Body("first. Your"));
                    runs.Add(ScheduleToastRun.Mono("rev " + mine));
                    runs.Add(ScheduleToastRun.Body("copy of " + ScheduleActivityFormat.ShortDate(iso) + " was not written."));
                }
                toast.SetRuns(runs);
                _schedActStack.Push(toast);
            }
            catch { }
        }

        // ----------------------------------------------------------------- receive

        private void OnScheduleActivityEvents(List<ScheduleActivityEvent> events)
        {
            if (events == null || events.Count == 0 || _schedActStack == null) return;
            if (InvokeRequired) { BeginInvoke((Action)(() => OnScheduleActivityEvents(events))); return; }
            using (UiStallWatch.Measure(UiScope.ActivityEvents))
                ApplyScheduleActivityEvents(events);
        }

        private void ApplyScheduleActivityEvents(List<ScheduleActivityEvent> events)
        {
            string myDay = ScheduleActivityCurrentDayIso();
            foreach (var ev in events)
            {
                _schedActRecent.Add(ev);
                if (_schedActRecent.Count > SchedActRecentCap)
                    _schedActRecent.RemoveRange(0, _schedActRecent.Count - SchedActRecentCap);
                try { ShowScheduleActivityToast(ev, myDay); } catch { }
            }
            if (_schedActDrawer != null && _schedActDrawer.IsOpen)
                _schedActDrawer.Prepend(events);
        }

        private void ShowScheduleActivityToast(ScheduleActivityEvent ev, string myDay)
        {
            if (ev == null) return;
            bool sameDay = !string.IsNullOrEmpty(myDay) && string.Equals(ev.ServiceDate, myDay, StringComparison.Ordinal);
            string shortDate = ScheduleActivityFormat.ShortDate(ev.ServiceDate);

            switch (ev.Verb)
            {
                case "saved":
                    {
                        _schedActStack.MarkSaved(ev.ClientId, ev.ServiceDate, ev.Ts);
                        int rev = ev.Detail?.Revision ?? 0;
                        int mine = sameDay ? ScheduleActivityLocalRevision() : 0;
                        bool behind = sameDay && rev > 0 && mine < rev;
                        var t = new ScheduleActivityToast
                        {
                            Kind = behind ? ScheduleToastKind.Behind : ScheduleToastKind.Saved,
                            Who = ev.Dispatcher,
                            Verb = ev.Verb,
                            ServiceDate = ev.ServiceDate,
                            OffDay = !sameDay,
                            SourceClientId = ev.ClientId,
                            EventTs = ev.Ts,
                            LifetimeMs = behind ? 0 : (sameDay ? 6000 : 5000),
                            ActionText = behind ? "Load rev " + rev : null,
                            Payload = ev,
                        };
                        var runs = new List<ScheduleToastRun>
                        {
                            ScheduleToastRun.Body("saved"),
                            ScheduleToastRun.Strong(shortDate),
                        };
                        if (rev > 0)
                        {
                            runs.Add(ScheduleToastRun.Body("\u00b7"));
                            runs.Add(ScheduleToastRun.Mono("rev " + rev));
                        }
                        if (behind)
                        {
                            runs.Add(ScheduleToastRun.Body(". You're on"));
                            runs.Add(ScheduleToastRun.Mono("rev " + mine));
                            runs.Add(ScheduleToastRun.Body("."));
                            // Only one "behind" toast per day at a time.
                            _schedActStack.DismissWhere(x =>
                                x.Kind == ScheduleToastKind.Behind && x.ServiceDate == ev.ServiceDate);
                        }
                        t.SetRuns(runs);
                        _schedActStack.Push(t);
                        return;
                    }
                case "rejected":
                    // Our own private event; the 409 path already showed the toast locally.
                    return;
                case "opened":
                case "closed":
                    {
                        var t = new ScheduleActivityToast
                        {
                            Kind = ScheduleToastKind.Presence,
                            Who = ev.Dispatcher,
                            Verb = ev.Verb,
                            ServiceDate = ev.ServiceDate,
                            OffDay = !sameDay,
                            SourceClientId = ev.ClientId,
                            EventTs = ev.Ts,
                            LifetimeMs = 5000,
                            Payload = ev,
                        };
                        t.SetRuns(new[] { ScheduleToastRun.Body(ev.Verb), ScheduleToastRun.Strong(shortDate) });
                        _schedActStack.Push(t, n => n.Kind == ScheduleToastKind.Presence
                            && n.SourceClientId == ev.ClientId && n.Verb == ev.Verb);
                        return;
                    }
                default:
                    {
                        // Edits on a schedule other than the one on screen never toast.
                        //
                        // One fires per undo snapshot, so this is effectively all of the feed's
                        // volume, and it is the one kind of event you cannot act on from here:
                        // knowing Remie moved a trip on the 18th while you are building the 17th
                        // changes nothing you are doing. Saves and presence still come through
                        // because they are a handful a day and do tell you something useful.
                        //
                        // Nothing is lost by dropping these. The caller has already added the
                        // event to _schedActRecent and hands the whole batch to the history
                        // drawer, which pulls any day in full from the server on demand.
                        if (!sameDay) return;

                        var d = ev.Detail ?? new ScheduleActivityDetail();
                        string toTab = d.ToTab ?? d.Tab ?? "";
                        var t = new ScheduleActivityToast
                        {
                            Kind = ScheduleToastKind.Edit,
                            Who = ev.Dispatcher,
                            Verb = ev.Verb,
                            ServiceDate = ev.ServiceDate,
                            SourceClientId = ev.ClientId,
                            ToTab = toTab,
                            EventTs = ev.Ts,
                            Count = Math.Max(1, d.EffectiveCount),
                            Unsaved = !(ev.Saved ?? false),
                            LifetimeMs = 8000,
                            Payload = new List<ScheduleActivityEvent> { ev },
                        };
                        t.SetRuns(BuildScheduleActivityRuns(new List<ScheduleActivityEvent> { ev }, sameDay));
                        var merged = _schedActStack.Push(t, n =>
                            n.Kind == t.Kind
                            && n.Verb == ev.Verb
                            && n.SourceClientId == ev.ClientId
                            && n.ServiceDate == ev.ServiceDate
                            && string.Equals(n.ToTab, toTab, StringComparison.Ordinal));
                        if (merged != null && !ReferenceEquals(merged, t))
                        {
                            var list = merged.Payload as List<ScheduleActivityEvent>;
                            if (list == null) merged.Payload = list = new List<ScheduleActivityEvent>();
                            list.Add(ev);
                            merged.Count = list.Sum(e => Math.Max(1, e.Detail?.EffectiveCount ?? 1));
                            merged.EventTs = ev.Ts;
                            if (ev.Saved == false) merged.Unsaved = true;
                            merged.SetRuns(BuildScheduleActivityRuns(list, sameDay));
                        }
                        return;
                    }
            }
        }

        private static List<ScheduleToastRun> BuildScheduleActivityRuns(List<ScheduleActivityEvent> evs, bool sameDay)
        {
            var runs = new List<ScheduleToastRun>();
            var first = evs[0];
            var d = first.Detail ?? new ScheduleActivityDetail();
            int total = evs.Sum(e => Math.Max(1, e.Detail?.EffectiveCount ?? 1));
            var trips = evs.SelectMany(e => e.Detail?.Trips ?? new List<ScheduleActivityTrip>()).ToList();
            string shortDate = ScheduleActivityFormat.ShortDate(first.ServiceDate);

            void TripPhrase()
            {
                if (trips.Count > 0)
                {
                    var t0 = trips[0];
                    if (!string.IsNullOrEmpty(t0.Trip)) runs.Add(ScheduleToastRun.Mono(t0.Trip));
                    if (!string.IsNullOrEmpty(t0.Client)) runs.Add(ScheduleToastRun.Strong(t0.Client));
                    if (total > 1) runs.Add(ScheduleToastRun.Body("+" + (total - 1)));
                    if (string.IsNullOrEmpty(t0.Trip) && string.IsNullOrEmpty(t0.Client))
                        runs.Add(ScheduleToastRun.Body(total > 1 ? total + " trips" : "a trip"));
                }
                else
                    runs.Add(ScheduleToastRun.Body(total > 1 ? total + " trips" : "a trip"));
            }

            void OtherDay()
            {
                if (!sameDay)
                {
                    runs.Add(ScheduleToastRun.Body("on"));
                    runs.Add(ScheduleToastRun.Strong(shortDate));
                }
            }

            string frm = d.FromTab ?? "";
            string to = d.ToTab ?? d.Tab ?? "";
            switch (first.Verb)
            {
                case "moved":
                case "merged":
                    runs.Add(ScheduleToastRun.Body(first.Verb));
                    TripPhrase();
                    if (!string.IsNullOrEmpty(frm) && !string.IsNullOrEmpty(to) && frm != to)
                    {
                        runs.Add(ScheduleToastRun.Chip(frm));
                        runs.Add(ScheduleToastRun.Arrow());
                        runs.Add(ScheduleToastRun.ChipAccent(to));
                    }
                    else if (!string.IsNullOrEmpty(to))
                    {
                        runs.Add(ScheduleToastRun.Body("on"));
                        runs.Add(ScheduleToastRun.Chip(to));
                    }
                    OtherDay();
                    break;
                case "suggest":
                    runs.Add(ScheduleToastRun.Body("put"));
                    TripPhrase();
                    runs.Add(ScheduleToastRun.Body("on"));
                    runs.Add(ScheduleToastRun.ChipAccent(string.IsNullOrEmpty(to) ? "a driver" : to));
                    runs.Add(ScheduleToastRun.Body("via Suggest"));
                    OtherDay();
                    break;
                case "cut":
                    runs.Add(ScheduleToastRun.Body("cut"));
                    TripPhrase();
                    if (!string.IsNullOrEmpty(frm)) { runs.Add(ScheduleToastRun.Body("from")); runs.Add(ScheduleToastRun.Chip(frm)); }
                    else if (!string.IsNullOrEmpty(to)) { runs.Add(ScheduleToastRun.Body("from")); runs.Add(ScheduleToastRun.Chip(to)); }
                    OtherDay();
                    break;
                case "paste":
                    runs.Add(ScheduleToastRun.Body("pasted"));
                    TripPhrase();
                    if (!string.IsNullOrEmpty(frm) && !string.IsNullOrEmpty(to) && frm != to)
                    {
                        runs.Add(ScheduleToastRun.Chip(frm));
                        runs.Add(ScheduleToastRun.Arrow());
                        runs.Add(ScheduleToastRun.ChipAccent(to));
                    }
                    else if (!string.IsNullOrEmpty(to))
                    {
                        runs.Add(ScheduleToastRun.Arrow());
                        runs.Add(ScheduleToastRun.ChipAccent(to));
                    }
                    OtherDay();
                    break;
                case "delete":
                    runs.Add(ScheduleToastRun.Body("deleted"));
                    TripPhrase();
                    if (!string.IsNullOrEmpty(to)) { runs.Add(ScheduleToastRun.Body("from")); runs.Add(ScheduleToastRun.Chip(to)); }
                    OtherDay();
                    break;
                case "reroute":
                    runs.Add(ScheduleToastRun.Body("sent"));
                    TripPhrase();
                    runs.Add(ScheduleToastRun.Body("to"));
                    runs.Add(ScheduleToastRun.ChipWarn("Reroutes"));
                    OtherDay();
                    break;
                case "cancel":
                    runs.Add(ScheduleToastRun.Body("sent"));
                    TripPhrase();
                    runs.Add(ScheduleToastRun.Body("to"));
                    runs.Add(ScheduleToastRun.ChipWarn("Cancels"));
                    OtherDay();
                    break;
                case "note":
                    runs.Add(ScheduleToastRun.Body("edited a note"));
                    if (!string.IsNullOrEmpty(to)) { runs.Add(ScheduleToastRun.Body("on")); runs.Add(ScheduleToastRun.Chip(to)); }
                    OtherDay();
                    break;
                case "blank_row":
                    runs.Add(ScheduleToastRun.Body("changed spacing"));
                    if (!string.IsNullOrEmpty(to)) { runs.Add(ScheduleToastRun.Body("on")); runs.Add(ScheduleToastRun.Chip(to)); }
                    OtherDay();
                    break;
                case "sort":
                    runs.Add(ScheduleToastRun.Body("auto-sorted a group"));
                    if (!string.IsNullOrEmpty(to)) { runs.Add(ScheduleToastRun.Body("on")); runs.Add(ScheduleToastRun.Chip(to)); }
                    OtherDay();
                    break;
                case "color":
                    runs.Add(ScheduleToastRun.Body("recolored a group"));
                    if (!string.IsNullOrEmpty(to)) { runs.Add(ScheduleToastRun.Body("on")); runs.Add(ScheduleToastRun.Chip(to)); }
                    OtherDay();
                    break;
                case "undo":
                case "redo":
                    runs.Add(ScheduleToastRun.Body(first.Verb == "undo" ? "undid" : "redid"));
                    runs.Add(ScheduleToastRun.Strong(string.IsNullOrEmpty(d.Label) ? "an edit" : d.Label));
                    OtherDay();
                    break;
                default:
                    runs.Add(ScheduleToastRun.Body(string.IsNullOrEmpty(first.Text) ? "edited the schedule" : first.Text));
                    break;
            }
            return runs;
        }

        private void OnScheduleActivityPresence(List<SchedulePresenceEntry> presence)
        {
            if (InvokeRequired) { BeginInvoke((Action)(() => OnScheduleActivityPresence(presence))); return; }
            if (_schedActPresencePill == null || _schedActPresencePill.IsDisposed) return;
            try
            {
                ScheduleActivityTrackOpenDay();
                _schedActOthers = presence ?? new List<SchedulePresenceEntry>();
                _schedActPresencePill.SetOthers(_schedActOthers, ScheduleActivityCurrentDayIso());
            }
            catch { }
        }

        private void OnScheduleActivityHead(SchedulePublishedHead head)
        {
            // Reserved for a future "you are behind" check on the poll itself; the saved
            // event already carries the revision, so nothing is needed here today.
        }

        // ------------------------------------------------------------------ click

        private void OnScheduleActivityToastClicked(ScheduleActivityToast toast)
        {
            if (toast == null) return;
            try
            {
                if (toast.Kind == ScheduleToastKind.Chat)
                {
                    OnTeamChatToastClicked(toast);
                    return;
                }
                if (toast.Kind == ScheduleToastKind.Question)
                {
                    AnswerPlaybookAsk(toast, "yes");
                    return;
                }
                if (toast.Kind == ScheduleToastKind.Behind || toast.Kind == ScheduleToastKind.Rejected)
                {
                    _schedActStack?.Dismiss(toast);
                    ScheduleActivityLoadDay(toast.ServiceDate);
                    return;
                }
                long seq = -1;
                if (toast.Payload is ScheduleActivityEvent single) seq = single.Seq;
                else if (toast.Payload is List<ScheduleActivityEvent> many && many.Count > 0) seq = many[many.Count - 1].Seq;
                _schedActStack?.Dismiss(toast);
                _ = OpenScheduleActivityDrawerAsync(seq, toast.ServiceDate);
            }
            catch { }
        }

        private void ScheduleActivityLoadDay(string iso)
        {
            if (!DateTime.TryParseExact(iso ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var day))
                return;
            try
            {
                if (hiatmeTabControl != null && tabPage6 != null)
                    hiatmeTabControl.SelectedTab = tabPage6;
                if (fsbdatepicker != null) fsbdatepicker.Value = day;
                fsLoadBtn_Click(this, EventArgs.Empty);
            }
            catch { }
        }

        private async Task OpenScheduleActivityDrawerAsync(long highlightSeq, string serviceDate = null)
        {
            if (_schedActDrawer == null || _schedActFeed == null) return;
            string day = string.IsNullOrEmpty(serviceDate) ? ScheduleActivityCurrentDayIso() : serviceDate;
            if (string.IsNullOrEmpty(day))
                day = (fsbdatepicker?.Value.Date ?? DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            RepositionScheduleActivityDrawer();
            _schedActDrawer.SetEvents(day, _schedActRecent.Where(e => e.ServiceDate == day), highlightSeq);
            _schedActDrawer.Open();
            var events = await _schedActFeed.HistoryAsync(day).ConfigureAwait(true);
            if (_schedActDrawer.IsOpen && string.Equals(_schedActDrawer.ServiceDate, day, StringComparison.Ordinal))
                _schedActDrawer.SetEvents(day, events, highlightSeq);
        }
    }

    /// <summary>
    /// Title-bar who's-online: one dot per desk. Accent = checked in within ~12s,
    /// muted = still in the app but quiet, hollow ring = this PC cannot reach the panel.
    /// Amber "N unsaved" is unpublished schedule edits — not busy/away.
    /// </summary>
    internal sealed class SchedulePresencePill : Control
    {
        private static readonly Font PillFont = new Font("Segoe UI Semibold", 8.5f);
        private static readonly Font MicroFont = new Font("Segoe UI", 7.5f);
        private const string OnlineTag = "ONLINE";
        private const string EmptyLabel = "Only you";
        private const int BubbleSize = 14;
        private const int BubbleGap = 4;
        private const int MaxBubbles = 3;
        private readonly List<SchedulePresenceEntry> _others = new List<SchedulePresenceEntry>();
        private readonly ToolTip _tip = new ToolTip
        {
            ShowAlways = true,
            AutoPopDelay = 14000,
            InitialDelay = 400,
        };
        private bool _online = true;
        private int _mine;
        private string _lastKey = "";

        public bool Online
        {
            get { return _online; }
            set
            {
                if (_online == value) return;
                _online = value;
                _tip.SetToolTip(this, TipText());
                Invalidate();
            }
        }

        public event EventHandler LayoutChanged;

        public SchedulePresencePill()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            DoubleBuffered = true;
            BackColor = SupeyTheme.SurfaceHeader;
            Cursor = Cursors.Hand;
            Height = 26;
            Width = 10;
            TabStop = false;
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            using (var b = new SolidBrush(SupeyTheme.SurfaceHeader))
                pevent.Graphics.FillRectangle(b, ClientRectangle);
        }

        public void SetOthers(List<SchedulePresenceEntry> others, string myDay)
        {
            using (UiStallWatch.Measure(UiScope.PresenceSetOthers))
                SetOthersCore(others);
        }

        private void SetOthersCore(List<SchedulePresenceEntry> others)
        {
            _others.Clear();
            if (others != null) _others.AddRange(others);
            // Always shown: alone it reads "Only you", so the chip (and its
            // send-emoji menu) is discoverable even when no one else is online.
            if (!Visible) Visible = true;
            string key = PresenceKey();
            if (key != _lastKey)
            {
                _lastKey = key;
                Relayout();
            }
            else
                Invalidate();
        }

        public void SetMine(int unsaved)
        {
            if (_mine == unsaved) return;
            _mine = unsaved;
            string key = PresenceKey();
            if (key != _lastKey)
            {
                _lastKey = key;
                Relayout();
            }
            else
            {
                _tip.SetToolTip(this, TipText());
                Invalidate();
            }
        }

        private string PresenceKey()
        {
            var sb = new StringBuilder();
            sb.Append(_online ? "1" : "0");
            sb.Append('|').Append(_mine);
            foreach (var o in _others)
            {
                sb.Append('|').Append(o.Dispatcher ?? "")
                    .Append('/').Append(o.ServiceDate ?? "")
                    .Append('/').Append(o.Unsaved)
                    .Append('/').Append(o.Active ? '1' : '0');
            }
            return sb.ToString();
        }

        private static string LabelFor(SchedulePresenceEntry o)
        {
            string s = string.IsNullOrWhiteSpace(o.Dispatcher) ? "?" : o.Dispatcher.Trim();
            if (!string.IsNullOrEmpty(o.ServiceDate))
                s += " \u00b7 " + ScheduleActivityFormat.ShortDate(o.ServiceDate);
            if (o.Unsaved > 0) s += " \u00b7 " + o.Unsaved + " unsaved";
            return s;
        }

        private void Relayout()
        {
            using (UiStallWatch.Measure(UiScope.PresenceRelayout))
            {
                int w = MeasureWidth();
                if (Width != w) Width = w;
                _tip.SetToolTip(this, TipText());
                Invalidate();
            }
            LayoutChanged?.Invoke(this, EventArgs.Empty);
        }

        private int MeasureWidth()
        {
            using (var g = CreateGraphics())
            {
                var fmt = StringFormat.GenericTypographic;
                float x = 8f;
                x += 10f; // panel-status dot + gap
                x += Math.Max(38f, g.MeasureString(OnlineTag, MicroFont, PointF.Empty, fmt).Width + 10f);
                x += 6f;
                string summary = SummaryText();
                x += g.MeasureString(summary, PillFont, PointF.Empty, fmt).Width;

                int show = Math.Min(MaxBubbles, _others.Count);
                if (show > 0)
                {
                    x += 8f;
                    x += show * BubbleSize + (show - 1) * BubbleGap;
                    if (_others.Count > show)
                    {
                        x += 6f;
                        string extra = "+" + (_others.Count - show);
                        float ew = g.MeasureString(extra, MicroFont, PointF.Empty, fmt).Width;
                        x += Math.Max(14f, ew + 6f);
                    }
                }

                if (_mine > 0)
                {
                    x += 8f;
                    string mine = _mine.ToString(CultureInfo.InvariantCulture);
                    float mw = g.MeasureString(mine, MicroFont, PointF.Empty, fmt).Width;
                    x += Math.Max(14f, mw + 6f);
                }
                return Math.Max(10, (int)Math.Ceiling(x) + 8);
            }
        }

        private string TipText()
        {
            var lines = new List<string>
            {
                "Who's in the Tool Suite — not busy / away / DND.",
                "Bright dot: that desk checked in within 12 seconds.",
                "Gray dot: still in the app, quiet (drops off after ~30s).",
                "Empty ring: this PC cannot reach the panel.",
                "Amber \"unsaved\": unpublished schedule edits.",
                "Click for poke/actions.",
            };
            if (_mine > 0)
                lines.Add("You have " + _mine + " unsaved.");
            if (_others.Count == 0)
            {
                lines.Add("");
                lines.Add(_online
                    ? "You're the only desk online right now."
                    : "This PC can't reach the panel right now.");
            }
            if (_others.Count > 0)
            {
                lines.Add("");
                foreach (var o in _others)
                {
                    string state = !_online ? "panel unreachable"
                        : o.Active ? "active" : "quiet";
                    string day = string.IsNullOrEmpty(o.ServiceDate)
                        ? "no schedule open"
                        : ScheduleActivityFormat.ShortDate(o.ServiceDate);
                    string extra = o.Unsaved > 0 ? ", " + o.Unsaved + " unsaved" : "";
                    lines.Add((o.Dispatcher ?? "?") + " — " + state + " · " + day + extra);
                }
            }
            return string.Join(Environment.NewLine, lines);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            var fmt = StringFormat.GenericTypographic;
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            using (var b = new SolidBrush(SupeyTheme.SurfaceHeader))
            using (var p = Rounded(r, Height / 2f))
                g.FillPath(b, p);
            using (var pen = new Pen(SupeyTheme.Divider))
            using (var p = Rounded(r, Height / 2f))
                g.DrawPath(pen, p);

            float x = 8f;
            var panelDot = new RectangleF(x, (Height - 8) / 2f, 8, 8);
            PaintPanelDot(g, panelDot);
            x += 12f;

            float tagW = Math.Max(38f, g.MeasureString(OnlineTag, MicroFont, PointF.Empty, fmt).Width + 10f);
            var tagRect = new RectangleF(x, (Height - 14f) / 2f, tagW, 14f);
            using (var bg = new SolidBrush(_online ? SupeyTheme.AccentPrimary : SupeyTheme.TextMuted))
            using (var p = Rounded(tagRect, tagRect.Height / 2f))
                g.FillPath(bg, p);
            var tagSz = g.MeasureString(OnlineTag, MicroFont, PointF.Empty, fmt);
            using (var br = new SolidBrush(Color.White))
                g.DrawString(OnlineTag, MicroFont, br,
                    tagRect.X + (tagRect.Width - tagSz.Width) / 2f,
                    tagRect.Y + (tagRect.Height - tagSz.Height) / 2f, fmt);
            x += tagW + 6f;

            string summary = SummaryText();
            var sumSize = g.MeasureString(summary, PillFont, PointF.Empty, fmt);
            using (var br = new SolidBrush(_online ? SupeyTheme.TextSecondary : SupeyTheme.TextMuted))
                g.DrawString(summary, PillFont, br, x, (Height - sumSize.Height) / 2f, fmt);
            x += sumSize.Width;

            int show = Math.Min(MaxBubbles, _others.Count);
            if (show > 0)
            {
                x += 8f;
                for (int i = 0; i < show; i++)
                {
                    if (i > 0) x += BubbleGap;
                    var bubble = new RectangleF(x, (Height - BubbleSize) / 2f, BubbleSize, BubbleSize);
                    PaintBubble(g, bubble, _others[i]);
                    x += BubbleSize;
                }
                if (_others.Count > show)
                {
                    x += 6f;
                    string extra = "+" + (_others.Count - show);
                    var extraSize = g.MeasureString(extra, MicroFont, PointF.Empty, fmt);
                    var extraRect = new RectangleF(
                        x, (Height - (extraSize.Height + 2f)) / 2f,
                        Math.Max(14f, extraSize.Width + 6f),
                        extraSize.Height + 2f);
                    using (var bg = new SolidBrush(SupeyTheme.SurfaceHeader))
                    using (var p = Rounded(extraRect, extraRect.Height / 2f))
                        g.FillPath(bg, p);
                    using (var pen = new Pen(SupeyTheme.Divider))
                    using (var p = Rounded(extraRect, extraRect.Height / 2f))
                        g.DrawPath(pen, p);
                    using (var br = new SolidBrush(SupeyTheme.TextMuted))
                        g.DrawString(extra, MicroFont, br,
                            extraRect.X + (extraRect.Width - extraSize.Width) / 2f,
                            extraRect.Y + (extraRect.Height - extraSize.Height) / 2f, fmt);
                    x += extraRect.Width;
                }
            }

            if (_mine > 0)
            {
                x += 8f;
                string mine = _mine.ToString(CultureInfo.InvariantCulture);
                var mineSize = g.MeasureString(mine, MicroFont, PointF.Empty, fmt);
                var mineRect = new RectangleF(
                    x, (Height - (mineSize.Height + 2f)) / 2f,
                    Math.Max(14f, mineSize.Width + 6f),
                    mineSize.Height + 2f);
                using (var bg = new SolidBrush(SupeyTheme.WarnText))
                using (var p = Rounded(mineRect, mineRect.Height / 2f))
                    g.FillPath(bg, p);
                using (var br = new SolidBrush(SupeyTheme.Surface))
                    g.DrawString(mine, MicroFont, br,
                        mineRect.X + (mineRect.Width - mineSize.Width) / 2f,
                        mineRect.Y + (mineRect.Height - mineSize.Height) / 2f, fmt);
            }
        }

        private string SummaryText()
        {
            if (!_online) return "PANEL OFFLINE";
            if (_others.Count <= 0) return "ONLY YOU";
            return _others.Count == 1 ? "1 DESK" : _others.Count + " DESKS";
        }

        private static string InitialFor(SchedulePresenceEntry o)
        {
            string name = (o?.Dispatcher ?? "").Trim();
            if (string.IsNullOrEmpty(name)) return "?";
            return name.Substring(0, 1).ToUpperInvariant();
        }

        private void PaintPanelDot(Graphics g, RectangleF dot)
        {
            if (!_online)
            {
                using (var pen = new Pen(SupeyTheme.TextMuted, 1.2f))
                    g.DrawEllipse(pen, dot);
                return;
            }
            using (var b = new SolidBrush(SupeyTheme.AccentPrimary))
                g.FillEllipse(b, dot);
        }

        private void PaintBubble(Graphics g, RectangleF rect, SchedulePresenceEntry o)
        {
            bool active = o != null && o.Active;
            if (!_online)
            {
                using (var b = new SolidBrush(SupeyTheme.SurfaceHeader))
                    g.FillEllipse(b, rect);
                using (var pen = new Pen(SupeyTheme.TextMuted, 1.1f))
                    g.DrawEllipse(pen, rect);
                return;
            }

            using (var b = new SolidBrush(active ? SupeyTheme.AccentPrimary : SupeyTheme.SurfaceHeader))
                g.FillEllipse(b, rect);
            using (var pen = new Pen(active ? SupeyTheme.AccentPrimary : SupeyTheme.Divider))
                g.DrawEllipse(pen, rect);

            string initial = InitialFor(o);
            var sz = g.MeasureString(initial, MicroFont, PointF.Empty, StringFormat.GenericTypographic);
            using (var br = new SolidBrush(active ? Color.White : SupeyTheme.TextMuted))
                g.DrawString(initial, MicroFont, br,
                    rect.X + (rect.Width - sz.Width) / 2f,
                    rect.Y + (rect.Height - sz.Height) / 2f,
                    StringFormat.GenericTypographic);

            if (o != null && o.Unsaved > 0)
            {
                float s = 4.4f;
                var badge = new RectangleF(rect.Right - s, rect.Y - 0.3f, s, s);
                using (var b = new SolidBrush(SupeyTheme.WarnText))
                    g.FillEllipse(b, badge);
                using (var p = new Pen(SupeyTheme.Surface, 0.8f))
                    g.DrawEllipse(p, badge);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _tip.Dispose();
            }
            base.Dispose(disposing);
        }

        private static IEnumerable<Tuple<string, bool>> SplitUnsaved(string text)
        {
            int i = 0;
            while (i < text.Length)
            {
                int k = text.IndexOf("unsaved", i, StringComparison.Ordinal);
                if (k < 0) { yield return Tuple.Create(text.Substring(i), false); break; }
                // walk back to the number
                int j = k - 1;
                while (j >= i && (text[j] == ' ')) j--;
                while (j >= i && char.IsDigit(text[j])) j--;
                j++;
                if (j > i) yield return Tuple.Create(text.Substring(i, j - i), false);
                yield return Tuple.Create(text.Substring(j, k + 7 - j), true);
                i = k + 7;
            }
        }

        private static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
