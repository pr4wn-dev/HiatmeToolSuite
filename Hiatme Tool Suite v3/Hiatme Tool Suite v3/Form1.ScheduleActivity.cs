using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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

            _schedActFeed = new ScheduleActivityFeed(() => HiatmeAiSettings.Load())
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

            _schedActDrawer = new ScheduleActivityDrawer();
            _schedActDrawer.SetIdentity(_schedActFeed.ClientId);
            Controls.Add(_schedActDrawer);
            Resize += (_, __) => RepositionScheduleActivityDrawer();
            RepositionScheduleActivityDrawer();

            InstallSchedulePresencePill();

            if (hiatmeTabControl != null)
            {
                hiatmeTabControl.SelectedIndexChanged += (_, __) =>
                    _schedActFeed?.SetFastPolling(hiatmeTabControl.SelectedTab == tabPage6);
            }
            FormClosing += (_, __) => ScheduleActivityShutdown();

            _schedActFeed.Start();
            _schedActFeed.SetFastPolling(hiatmeTabControl?.SelectedTab == tabPage6);
        }

        private void InstallSchedulePresencePill()
        {
            if (_schedActPresencePill != null) return;
            _schedActPresencePill = new SchedulePresencePill
            {
                Margin = new Padding(12, 6, 0, 0),
                Visible = false,
            };
            _schedActPresencePill.Click += (_, __) => _ = OpenScheduleActivityDrawerAsync(-1);
            var parent = _fsAutoSaveHintLbl?.Parent;
            if (parent != null)
                parent.Controls.Add(_schedActPresencePill);
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
                Task.WhenAll(flush, bye).Wait(TimeSpan.FromSeconds(2));
            }
            catch { }
            finally
            {
                try { _schedActFeed?.Dispose(); } catch { }
                try { _schedActStack?.Dispose(); } catch { }
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

        private int ScheduleActivityLocalRevision()
        {
            try
            {
                string p = fsbuilder?.LastExportPath;
                if (string.IsNullOrWhiteSpace(p) || !File.Exists(p)) return 0;
                return ScheduleWorkbookResolver.ReadLocalRevision(p);
            }
            catch { return 0; }
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
                        var d = ev.Detail ?? new ScheduleActivityDetail();
                        string toTab = d.ToTab ?? d.Tab ?? "";
                        var t = new ScheduleActivityToast
                        {
                            Kind = sameDay ? ScheduleToastKind.Edit : ScheduleToastKind.Muted,
                            Who = ev.Dispatcher,
                            Verb = ev.Verb,
                            ServiceDate = ev.ServiceDate,
                            SourceClientId = ev.ClientId,
                            ToTab = toTab,
                            EventTs = ev.Ts,
                            Count = Math.Max(1, d.EffectiveCount),
                            Unsaved = !(ev.Saved ?? false),
                            LifetimeMs = sameDay ? 8000 : 5000,
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
                _schedActPresencePill.SetOthers(presence ?? new List<SchedulePresenceEntry>(), ScheduleActivityCurrentDayIso());
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
    /// Status-band pill: "● Remie · Sep 17 · 3 unsaved". Dot lit in the accent while that
    /// desk touched the day in the last ~12s, muted when idle, hollow when the panel is down.
    /// </summary>
    internal sealed class SchedulePresencePill : Control
    {
        private static readonly Font PillFont = new Font("Segoe UI", 8.25f);
        private readonly List<SchedulePresenceEntry> _others = new List<SchedulePresenceEntry>();
        private string _myDay = "";
        private int _mine;

        public bool Online { get; set; } = true;

        public SchedulePresencePill()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            DoubleBuffered = true;
            BackColor = SupeyTheme.SurfaceHeader;
            Cursor = Cursors.Hand;
            Height = 20;
            Width = 10;
            TabStop = false;
        }

        private string _lastText = "";
        private bool _lastActive;

        public void SetOthers(List<SchedulePresenceEntry> others, string myDay)
        {
            _others.Clear();
            if (others != null) _others.AddRange(others);
            _myDay = myDay ?? "";
            bool vis = _others.Count > 0;
            string text = Text_();
            bool active = _others.Any(o => o.Active);
            // Every poll lands here; only touch layout when what we show actually changed,
            // or the toolbar's FlowLayoutPanel re-lays out every 3 seconds for nothing.
            if (vis != Visible) Visible = vis;
            if (text != _lastText)
            {
                _lastText = text;
                _lastActive = active;
                Relayout();
            }
            else if (active != _lastActive)
            {
                _lastActive = active;
                Invalidate();
            }
        }

        public void SetMine(int unsaved)
        {
            _mine = unsaved;
            Invalidate();
        }

        private string Text_()
        {
            if (_others.Count == 0) return "";
            var sameDay = _others.Where(o => !string.IsNullOrEmpty(_myDay) && o.ServiceDate == _myDay).ToList();
            var show = sameDay.Count > 0 ? sameDay : _others;
            var parts = new List<string>();
            foreach (var o in show.Take(3))
            {
                string s = o.Dispatcher ?? "?";
                if (!string.IsNullOrEmpty(o.ServiceDate) && (sameDay.Count == 0 || show.Count > 1))
                    s += " \u00b7 " + ScheduleActivityFormat.ShortDate(o.ServiceDate);
                if (o.Unsaved > 0) s += " \u00b7 " + o.Unsaved + " unsaved";
                parts.Add(s);
            }
            if (show.Count > 3) parts.Add("+" + (show.Count - 3));
            return string.Join("   ", parts);
        }

        private void Relayout()
        {
            using (var g = CreateGraphics())
            {
                var sz = g.MeasureString(Text_(), PillFont, PointF.Empty, StringFormat.GenericTypographic);
                Width = (int)Math.Ceiling(sz.Width) + 30;
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            var fmt = StringFormat.GenericTypographic;
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            using (var b = new SolidBrush(SupeyTheme.Surface))
            using (var p = Rounded(r, Height / 2f))
                g.FillPath(b, p);
            using (var pen = new Pen(SupeyTheme.Divider))
            using (var p = Rounded(r, Height / 2f))
                g.DrawPath(pen, p);

            bool anyActive = _others.Any(o => o.Active);
            var dot = new RectangleF(9, (Height - 7) / 2f, 7, 7);
            if (!Online)
                using (var pen = new Pen(SupeyTheme.TextMuted, 1.2f)) g.DrawEllipse(pen, dot);
            else
                using (var b = new SolidBrush(anyActive ? SupeyTheme.AccentPrimary : SupeyTheme.TextMuted)) g.FillEllipse(b, dot);

            string text = Text_();
            float x = 22;
            // Color the "N unsaved" fragments amber.
            foreach (var seg in SplitUnsaved(text))
            {
                var sz = g.MeasureString(seg.Item1, PillFont, PointF.Empty, fmt);
                using (var b = new SolidBrush(seg.Item2 ? SupeyTheme.WarnText : SupeyTheme.TextSecondary))
                    g.DrawString(seg.Item1, PillFont, b, x, (Height - sz.Height) / 2f, fmt);
                x += sz.Width;
            }
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
