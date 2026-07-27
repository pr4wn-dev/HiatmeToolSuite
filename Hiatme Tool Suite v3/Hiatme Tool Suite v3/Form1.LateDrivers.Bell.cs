using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Driver Habits will-call bell + message strip (same WellRyde bell API as Trip Scout).
    /// </summary>
    partial class Form1
    {
        private const int LateDriversBellAlertBarHeight = 54;
        private const int LateDriversBellAlertButtonStripWidth = 188;
        private const int LateDriversBellAlertTextMinWidth = 100;
        private static readonly Color LateDriversBellAlertAccent = Color.FromArgb(108, 148, 220);
        private static readonly Color LateDriversWillCallRowColor = Color.FromArgb(88, 112, 188);

        private Panel ldBellAlertHost;
        private Panel _ldBellAlertBar;
        private Panel _ldBellAlertBarAccent;
        private Label _ldBellAlertLine1;
        private Label _ldBellAlertLine2;
        private FlowLayoutPanel _ldBellAlertBtnFlow;
        private SupeyButton _ldBellAlertShowBtn;
        private SupeyButton _ldBellAlertPrevBtn;
        private SupeyButton _ldBellAlertNextBtn;
        private SupeyButton _ldBellAlertDismissBtn;

        private TripScoutLiveBellControl _ldLiveBell;
        private Panel _ldLiveBellCard;

        private List<HiatmeAiClient.WellRydeBellWillCall> _ldWillCalls
            = new List<HiatmeAiClient.WellRydeBellWillCall>();
        private readonly HashSet<string> _ldWillCallTripNos =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private HiatmeAiClient.WellRydeBellStatus _ldLastBellStatus;
        private string _ldLiveBellHash;
        private string _ldBellAckHash;
        private bool _ldBellAckLoaded;
        private readonly HashSet<string> _ldBellDismissedKeys =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private readonly List<LateDriversBellAlertItem> _ldBellAlertQueue =
            new List<LateDriversBellAlertItem>();
        private int _ldBellAlertIndex;

        private sealed class LateDriversBellAlertItem
        {
            public string TripNo;
            public HiatmeAiClient.WellRydeBellWillCall WillCall;
            public string Key;
        }

        private void ClearLateDriversBellUiRefs()
        {
            ldBellAlertHost = null;
            _ldBellAlertBar = null;
            _ldBellAlertBarAccent = null;
            _ldBellAlertLine1 = null;
            _ldBellAlertLine2 = null;
            _ldBellAlertBtnFlow = null;
            _ldBellAlertShowBtn = null;
            _ldBellAlertPrevBtn = null;
            _ldBellAlertNextBtn = null;
            _ldBellAlertDismissBtn = null;
            _ldLiveBell = null;
            _ldLiveBellCard = null;
        }

        private void ResetLateDriversBellState()
        {
            _ldWillCalls.Clear();
            _ldWillCallTripNos.Clear();
            _ldLastBellStatus = null;
            _ldLiveBellHash = null;
            // Keep ack / dismissed / sounded — UI rebuild must not re-nag the same messages.
            _ldBellAlertQueue.Clear();
            _ldBellAlertIndex = 0;
            HideLateDriversBellAlertBar();
            _ldLiveBell?.SetNotificationState(0, false);
        }

        private void EnsureLateDriversBellAckLoaded()
        {
            if (_ldBellAckLoaded)
                return;
            _ldBellAckLoaded = true;
            LateDriversBellAckStore.LoadForToday(out string ack, out var dismissed);
            if (!string.IsNullOrWhiteSpace(ack) && string.IsNullOrEmpty(_ldBellAckHash))
                _ldBellAckHash = ack.Trim();
            if (dismissed != null)
            {
                foreach (string k in dismissed)
                {
                    if (!string.IsNullOrWhiteSpace(k))
                        _ldBellDismissedKeys.Add(k.Trim());
                }
            }
        }

        private void PersistLateDriversBellAck()
        {
            LateDriversBellAckStore.SaveForToday(_ldBellAckHash, _ldBellDismissedKeys);
        }

        private static string LateDriversBellItemKey(HiatmeAiClient.WellRydeBellWillCall wc)
        {
            if (wc == null)
                return "";
            if (wc.MessageId > 0)
                return "m:" + wc.MessageId.ToString(CultureInfo.InvariantCulture);
            string trip = (wc.TripNo ?? "").Trim();
            if (string.IsNullOrEmpty(trip))
                return "";
            return "t:" + trip;
        }

        private bool LateDriversBellItemIsDismissed(HiatmeAiClient.WellRydeBellWillCall wc)
        {
            string key = LateDriversBellItemKey(wc);
            return !string.IsNullOrEmpty(key) && _ldBellDismissedKeys.Contains(key);
        }

        private int LateDriversPendingBellWillCallCount()
        {
            if (_ldWillCalls == null || _ldWillCalls.Count == 0)
                return 0;
            int n = 0;
            foreach (var wc in _ldWillCalls)
            {
                if (wc == null || string.IsNullOrWhiteSpace(wc.TripNo))
                    continue;
                if (LateDriversBellItemIsDismissed(wc))
                    continue;
                n++;
            }
            return n;
        }

        private void EnsureLateDriversBellAlertHost()
        {
            if (ldBellAlertHost != null && !ldBellAlertHost.IsDisposed)
                return;
            if (ldMainCard == null || ldMainCard.IsDisposed)
                return;

            ldBellAlertHost = new Panel
            {
                Name = "ldBellAlertHost",
                Dock = DockStyle.Top,
                Height = 0,
                Visible = false,
                Padding = new Padding(10, 0, 10, 4),
                BackColor = Color.Transparent,
            };

            EnsureLateDriversBellAlertBar();
        }

        private void EnsureLateDriversBellAlertBar()
        {
            if (_ldBellAlertBar != null && !_ldBellAlertBar.IsDisposed)
                return;
            EnsureLateDriversBellAlertHost();
            if (ldBellAlertHost == null)
                return;

            _ldBellAlertBar = new Panel
            {
                Name = "ldBellAlertBar",
                Dock = DockStyle.Fill,
                Visible = true,
                BackColor = SupeyTheme.SurfaceElevated,
                Padding = Padding.Empty,
            };

            _ldBellAlertBarAccent = new Panel
            {
                Dock = DockStyle.Left,
                Width = 3,
                BackColor = LateDriversBellAlertAccent,
            };

            _ldBellAlertBtnFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                Width = LateDriversBellAlertButtonStripWidth,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = false,
                Padding = new Padding(0, 10, 4, 0),
                BackColor = SupeyTheme.SurfaceElevated,
            };

            _ldBellAlertShowBtn = MakeLateDriversBellAlertButton("Locate", LateDriversBellAlertShowInList_Click);
            _ldBellAlertPrevBtn = MakeLateDriversBellAlertButton("◀", LateDriversBellAlertPrev_Click);
            _ldBellAlertNextBtn = MakeLateDriversBellAlertButton("▶", LateDriversBellAlertNext_Click);
            _ldBellAlertDismissBtn = MakeLateDriversBellAlertButton("Dismiss", LateDriversBellAlertDismiss_Click);

            _ldBellAlertShowBtn.Size = new Size(54, 30);
            _ldBellAlertPrevBtn.Size = new Size(30, 30);
            _ldBellAlertNextBtn.Size = new Size(30, 30);
            _ldBellAlertDismissBtn.Size = new Size(62, 30);

            _ldBellAlertBtnFlow.Controls.Add(_ldBellAlertShowBtn);
            _ldBellAlertBtnFlow.Controls.Add(_ldBellAlertPrevBtn);
            _ldBellAlertBtnFlow.Controls.Add(_ldBellAlertNextBtn);
            _ldBellAlertBtnFlow.Controls.Add(_ldBellAlertDismissBtn);

            var textHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.SurfaceElevated,
                Padding = new Padding(10, 6, 4, 6),
            };

            _ldBellAlertLine1 = new Label
            {
                Dock = DockStyle.Top,
                Height = 20,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Semibold", 9.75f),
                ForeColor = LateDriversBellAlertAccent,
                BackColor = SupeyTheme.SurfaceElevated,
            };

            _ldBellAlertLine2 = new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9f),
                ForeColor = SupeyTheme.TextSecondary,
                BackColor = SupeyTheme.SurfaceElevated,
            };

            textHost.Controls.Add(_ldBellAlertLine2);
            textHost.Controls.Add(_ldBellAlertLine1);

            var bottomRule = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 1,
                BackColor = SupeyTheme.Divider,
            };

            _ldBellAlertBar.Controls.Add(textHost);
            _ldBellAlertBar.Controls.Add(_ldBellAlertBtnFlow);
            _ldBellAlertBar.Controls.Add(_ldBellAlertBarAccent);
            _ldBellAlertBar.Controls.Add(bottomRule);

            ldBellAlertHost.Controls.Add(_ldBellAlertBar);
            SupeyDarkScrollBars.Apply(_ldBellAlertBar);
        }

        private static SupeyButton MakeLateDriversBellAlertButton(string text, EventHandler click)
        {
            var btn = new SupeyButton
            {
                Text = text,
                AutoSize = false,
                Size = new Size(54, 30),
                Margin = new Padding(0, 1, 3, 0),
                Kind = SupeyButton.Variant.Secondary,
            };
            btn.Click += click;
            return btn;
        }

        private async Task RefreshLateDriversBellAsync(HiatmeAiSettings settings, bool autoShowIfNew)
        {
            if (settings == null || string.IsNullOrWhiteSpace(settings.BaseUrl))
                return;
            try
            {
                var bell = await HiatmeAiClient.GetWellRydeBellStatusAsync(settings)
                    .ConfigureAwait(true);
                ApplyLateDriversBellPayload(bell, autoShowIfNew);
            }
            catch
            {
                // Live refresh must not die on bell failures.
            }
        }

        private void ApplyLateDriversBellPayload(
            HiatmeAiClient.WellRydeBellStatus bell,
            bool autoShowIfNew)
        {
            EnsureLateDriversBellAckLoaded();

            string prevHash = _ldLiveBellHash;
            _ldWillCallTripNos.Clear();
            _ldWillCalls = bell?.Willcalls ?? new List<HiatmeAiClient.WellRydeBellWillCall>();
            _ldLastBellStatus = bell;
            if (!string.IsNullOrWhiteSpace(bell?.ContentHash))
                _ldLiveBellHash = bell.ContentHash.Trim();

            foreach (var wc in _ldWillCalls)
            {
                if (wc == null || string.IsNullOrWhiteSpace(wc.TripNo))
                    continue;
                _ldWillCallTripNos.Add(wc.TripNo.Trim());
            }

            // Drop dismissals that no longer exist in the current payload.
            if (_ldBellDismissedKeys.Count > 0)
            {
                var liveKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var wc in _ldWillCalls)
                {
                    string k = LateDriversBellItemKey(wc);
                    if (!string.IsNullOrEmpty(k))
                        liveKeys.Add(k);
                }
                _ldBellDismissedKeys.RemoveWhere(k => !liveKeys.Contains(k));
            }

            // Will-calls no longer feed Reserved — still refresh if that tile is open.
            ClearLateDriversOffScheduleCache();
            if (LateDriversReservedSelected || LateDriversSearchQuery.Length > 0)
                BindLateDriversTripPane();
            else
                RefreshLateDriversOffScheduleCache();

            LateDriversUpdateLiveBellIndicator();

            // "New" = unacked content with at least one undismissed will-call.
            // Do not use sticky API has_new (stays true until the next WR poll).
            bool hashChanged = !string.IsNullOrEmpty(_ldLiveBellHash)
                && !string.Equals(prevHash, _ldLiveBellHash, StringComparison.Ordinal);
            int pending = LateDriversPendingBellWillCallCount();

            // Hash moved (e.g. WR dropped a trip) but everything left was already dismissed —
            // advance ack quietly so we don't re-prompt; new undismissed messages still alert.
            if (pending == 0
                && !string.IsNullOrEmpty(_ldLiveBellHash)
                && !LateDriversIsBellAcked())
            {
                _ldBellAckHash = _ldLiveBellHash;
                PersistLateDriversBellAck();
                LateDriversUpdateLiveBellIndicator();
            }

            bool unacked = pending > 0 && !LateDriversIsBellAcked();
            // Open the strip when content first appears / changes — not every 60s poll.
            bool isFreshContent = hashChanged
                || (unacked && string.IsNullOrEmpty(prevHash));

            if (LateDriversLiveEnabled && unacked)
            {
                // Sound while any will-call is still viewable (undismissed). Live polls
                // re-chirp as a reminder; dismissing clears pending and stops the sound.
                if (autoShowIfNew || isFreshContent)
                    TryPlayLateDriversBellSoundOnce();

                if (autoShowIfNew && isFreshContent)
                    LateDriversOpenWillCallAlertStrip(focusTrip: true);
            }
            else if (ldBellAlertHost != null && ldBellAlertHost.Visible)
            {
                LateDriversRebuildBellAlertQueue();
                if (_ldBellAlertQueue.Count == 0)
                    HideLateDriversBellAlertBar();
                else
                    LateDriversRefreshBellAlertBar(focusTrip: false);
            }

            ApplyLateDriversTripAlertBlinkPhase();
        }

        private bool LateDriversIsBellAcked()
        {
            return !string.IsNullOrEmpty(_ldBellAckHash)
                && !string.IsNullOrEmpty(_ldLiveBellHash)
                && string.Equals(_ldBellAckHash, _ldLiveBellHash, StringComparison.Ordinal);
        }

        private void AckLateDriversBell()
        {
            if (!string.IsNullOrEmpty(_ldLiveBellHash))
                _ldBellAckHash = _ldLiveBellHash;
            // Mark every current will-call dismissed so rebuilds stay quiet for this hash.
            if (_ldWillCalls != null)
            {
                foreach (var wc in _ldWillCalls)
                {
                    string k = LateDriversBellItemKey(wc);
                    if (!string.IsNullOrEmpty(k))
                        _ldBellDismissedKeys.Add(k);
                }
            }
            PersistLateDriversBellAck();
            LateDriversUpdateLiveBellIndicator();
        }

        private void LateDriversUpdateLiveBellIndicator()
        {
            if (_ldLiveBell == null || _ldLiveBell.IsDisposed)
                return;
            if (!LateDriversLiveEnabled)
            {
                _ldLiveBell.SetNotificationState(0, false);
                return;
            }

            int pending = LateDriversPendingBellWillCallCount();
            int cached = _ldWillCalls?.Count ?? 0;
            int badge = _ldLastBellStatus?.WillcallCount ?? 0;
            if (cached > badge)
                badge = cached;
            else if (badge <= 0)
                badge = cached;
            // Prefer undismissed count for the badge when we have message details.
            if (cached > 0)
                badge = pending;

            // Shake only for unacked / undismissed messages — never override with sticky HasNew.
            bool shouldShake = pending > 0 && !LateDriversIsBellAcked();
            _ldLiveBell.SetNotificationState(badge, shouldShake);
        }

        private void LateDriversLiveBell_Click()
        {
            LateDriversOpenWillCallAlertStrip(focusTrip: true);
        }

        private void LateDriversOpenWillCallAlertStrip(bool focusTrip)
        {
            LateDriversRebuildBellAlertQueue();
            _ldBellAlertIndex = 0;
            if (_ldBellAlertQueue.Count == 0)
            {
                HideLateDriversBellAlertBar();
                AckLateDriversBell();
                SetLateDriversStatus("Status: No will-calls ready right now.");
                return;
            }

            LateDriversRefreshBellAlertBar(focusTrip);
        }

        private void LateDriversRebuildBellAlertQueue()
        {
            _ldBellAlertQueue.Clear();
            if (_ldWillCalls == null)
                return;

            foreach (var wc in _ldWillCalls)
            {
                if (wc == null || string.IsNullOrWhiteSpace(wc.TripNo))
                    continue;
                if (LateDriversBellItemIsDismissed(wc))
                    continue;
                _ldBellAlertQueue.Add(new LateDriversBellAlertItem
                {
                    TripNo = wc.TripNo.Trim(),
                    WillCall = wc,
                    Key = LateDriversBellItemKey(wc),
                });
            }
        }

        private void LateDriversRefreshBellAlertBar(bool focusTrip)
        {
            EnsureLateDriversBellAlertBar();
            if (_ldBellAlertBar == null || ldBellAlertHost == null)
                return;

            if (_ldBellAlertQueue.Count == 0)
            {
                HideLateDriversBellAlertBar();
                return;
            }

            if (_ldBellAlertIndex < 0)
                _ldBellAlertIndex = 0;
            if (_ldBellAlertIndex >= _ldBellAlertQueue.Count)
                _ldBellAlertIndex = _ldBellAlertQueue.Count - 1;

            var item = _ldBellAlertQueue[_ldBellAlertIndex];
            int total = _ldBellAlertQueue.Count;
            string pageSuffix = total > 1
                ? "  (" + (_ldBellAlertIndex + 1) + "/" + total + ")"
                : "";

            if (_ldBellAlertBarAccent != null)
                _ldBellAlertBarAccent.BackColor = LateDriversBellAlertAccent;
            if (_ldBellAlertLine1 != null)
            {
                _ldBellAlertLine1.ForeColor = LateDriversBellAlertAccent;
                _ldBellAlertLine1.Text = "Will-call ready — #" + (item.TripNo ?? "") + pageSuffix;
            }

            if (_ldBellAlertLine2 != null)
            {
                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(item.WillCall?.Rider))
                    parts.Add(item.WillCall.Rider.Trim());
                if (!string.IsNullOrWhiteSpace(item.WillCall?.PuAddr))
                    parts.Add(item.WillCall.PuAddr.Trim());
                string when = FormatLateDriversBellWillCallWhen(item.WillCall);
                if (!string.IsNullOrWhiteSpace(when))
                    parts.Add(when);
                _ldBellAlertLine2.Text = string.Join("  ·  ", parts);
            }

            bool multi = total > 1;
            if (_ldBellAlertPrevBtn != null)
            {
                _ldBellAlertPrevBtn.Visible = multi;
                _ldBellAlertPrevBtn.Enabled = multi && _ldBellAlertIndex > 0;
            }
            if (_ldBellAlertNextBtn != null)
            {
                _ldBellAlertNextBtn.Visible = multi;
                _ldBellAlertNextBtn.Enabled = multi && _ldBellAlertIndex < total - 1;
            }

            if (_ldBellAlertBtnFlow != null && !_ldBellAlertBtnFlow.IsDisposed)
            {
                int hostW = Math.Max(120, ldBellAlertHost.ClientSize.Width);
                int stripW = Math.Min(
                    LateDriversBellAlertButtonStripWidth,
                    Math.Max(160, hostW - LateDriversBellAlertTextMinWidth - 3));
                _ldBellAlertBtnFlow.Width = stripW;
            }

            ldBellAlertHost.Visible = true;
            ldBellAlertHost.Height = LateDriversBellAlertBarHeight + ldBellAlertHost.Padding.Vertical;
            // Height/visibility only — BringToFront here used to reshuffle Dock z-order
            // (toolbar/search jump) every time the bell opened.
            LayoutLateDriversStageList();

            if (focusTrip)
                LateDriversFocusBellCarouselTrip(item);
        }

        private void HideLateDriversBellAlertBar()
        {
            if (ldBellAlertHost == null || ldBellAlertHost.IsDisposed)
                return;

            ldBellAlertHost.Visible = false;
            ldBellAlertHost.Height = 0;
            if (_ldBellAlertLine1 != null)
                _ldBellAlertLine1.Text = string.Empty;
            if (_ldBellAlertLine2 != null)
                _ldBellAlertLine2.Text = string.Empty;
            LayoutLateDriversStageList();
        }

        private void LateDriversFocusBellCarouselTrip(LateDriversBellAlertItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.TripNo))
                return;

            // Clear search so Locate lands on the owning driver / Reserved tile.
            if (ldSearchBox != null && !string.IsNullOrEmpty(ldSearchBox.Text))
            {
                _ldSuppressSearch = true;
                try { ldSearchBox.Text = ""; }
                finally { _ldSuppressSearch = false; }
            }

            GoToLateDriversTripSearch(item.TripNo);
        }

        private LateDriversBellAlertItem LateDriversPeekBellAlertItem()
        {
            if (_ldBellAlertQueue.Count == 0)
                return null;
            int idx = Math.Max(0, Math.Min(_ldBellAlertIndex, _ldBellAlertQueue.Count - 1));
            return _ldBellAlertQueue[idx];
        }

        private void LateDriversBellAlertShowInList_Click(object sender, EventArgs e)
        {
            LateDriversFocusBellCarouselTrip(LateDriversPeekBellAlertItem());
        }

        private void LateDriversBellAlertPrev_Click(object sender, EventArgs e)
        {
            if (_ldBellAlertIndex > 0)
            {
                _ldBellAlertIndex--;
                LateDriversRefreshBellAlertBar(focusTrip: true);
            }
        }

        private void LateDriversBellAlertNext_Click(object sender, EventArgs e)
        {
            if (_ldBellAlertIndex < _ldBellAlertQueue.Count - 1)
            {
                _ldBellAlertIndex++;
                LateDriversRefreshBellAlertBar(focusTrip: true);
            }
        }

        private void LateDriversBellAlertDismiss_Click(object sender, EventArgs e)
        {
            if (_ldBellAlertQueue.Count == 0)
            {
                HideLateDriversBellAlertBar();
                AckLateDriversBell();
                return;
            }

            int idx = Math.Max(0, Math.Min(_ldBellAlertIndex, _ldBellAlertQueue.Count - 1));
            var item = _ldBellAlertQueue[idx];
            string key = item?.Key;
            if (string.IsNullOrEmpty(key) && item?.WillCall != null)
                key = LateDriversBellItemKey(item.WillCall);
            if (!string.IsNullOrEmpty(key))
                _ldBellDismissedKeys.Add(key);

            _ldBellAlertQueue.RemoveAt(idx);
            if (_ldBellAlertQueue.Count == 0)
            {
                _ldBellAlertIndex = 0;
                HideLateDriversBellAlertBar();
                AckLateDriversBell();
                return;
            }

            PersistLateDriversBellAck();
            LateDriversUpdateLiveBellIndicator();
            if (_ldBellAlertIndex >= _ldBellAlertQueue.Count)
                _ldBellAlertIndex = _ldBellAlertQueue.Count - 1;
            LateDriversRefreshBellAlertBar(focusTrip: true);
        }

        private bool LateDriversIsWillCallTrip(string tripNo)
        {
            if (string.IsNullOrWhiteSpace(tripNo) || _ldWillCalls == null || _ldWillCalls.Count == 0)
                return false;

            foreach (var wc in _ldWillCalls)
            {
                if (wc == null || string.IsNullOrWhiteSpace(wc.TripNo))
                    continue;
                if (LateDriversTripQueryMatches(wc.TripNo, tripNo))
                    return true;
                if (TripScoutTripNosMatch(wc.TripNo, tripNo))
                    return true;
            }
            return false;
        }

        private string LateDriversPeekBellStatusNote()
        {
            if (_ldLastBellStatus == null || !_ldLastBellStatus.Ok || !_ldLastBellStatus.Available)
            {
                if (_ldLastBellStatus != null && !_ldLastBellStatus.Ok)
                    return "Bell check failed — " + (_ldLastBellStatus.Error ?? "unknown") + ".";
                return "";
            }

            int pending = LateDriversPendingBellWillCallCount();
            if (pending <= 0 || LateDriversIsBellAcked())
                return "";

            var parts = _ldWillCalls
                .Where(w => w != null && !string.IsNullOrWhiteSpace(w.TripNo)
                    && !LateDriversBellItemIsDismissed(w))
                .Take(3)
                .Select(w =>
                {
                    string rider = string.IsNullOrWhiteSpace(w.Rider) ? "" : " " + w.Rider.Trim();
                    return w.TripNo.Trim() + rider;
                })
                .ToList();
            string summary = string.Join("; ", parts);
            if (pending > 3)
                summary += " +" + (pending - 3) + " more";

            return "Bell: " + pending + " will-call ready — " + summary
                + " (click bell)";
        }

        private static string FormatLateDriversBellWillCallWhen(HiatmeAiClient.WellRydeBellWillCall wc)
        {
            if (wc == null)
                return "";

            string iso = wc.CreatedAt;
            if (string.IsNullOrWhiteSpace(iso))
                iso = wc.PuSchedule;
            if (string.IsNullOrWhiteSpace(iso))
                return "";

            if (DateTime.TryParse(
                    iso,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var dt))
            {
                return dt.ToLocalTime().ToString("h:mm tt", CultureInfo.CurrentCulture);
            }

            return iso.Trim();
        }

        private void StyleLateDriversBellAlertTheme()
        {
            if (_ldBellAlertBar != null && !_ldBellAlertBar.IsDisposed)
                _ldBellAlertBar.BackColor = SupeyTheme.SurfaceElevated;
            if (_ldBellAlertBtnFlow != null && !_ldBellAlertBtnFlow.IsDisposed)
                _ldBellAlertBtnFlow.BackColor = SupeyTheme.SurfaceElevated;
            if (_ldBellAlertLine1 != null && !_ldBellAlertLine1.IsDisposed)
            {
                _ldBellAlertLine1.ForeColor = LateDriversBellAlertAccent;
                _ldBellAlertLine1.BackColor = SupeyTheme.SurfaceElevated;
            }
            if (_ldBellAlertLine2 != null && !_ldBellAlertLine2.IsDisposed)
            {
                _ldBellAlertLine2.ForeColor = SupeyTheme.TextSecondary;
                _ldBellAlertLine2.BackColor = SupeyTheme.SurfaceElevated;
            }
            if (_ldBellAlertBarAccent != null && !_ldBellAlertBarAccent.IsDisposed)
                _ldBellAlertBarAccent.BackColor = LateDriversBellAlertAccent;
            LateDriversUpdateLiveBellIndicator();
        }
    }
}
