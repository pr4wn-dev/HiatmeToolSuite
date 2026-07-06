using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    partial class Form1
    {
        private const int TripScoutBellAlertBarHeight = 54;
        private const int TripScoutBellAlertButtonStripWidth = 188;
        private const int TripScoutBellAlertTextMinWidth = 100;
        private static readonly Color TripScoutBellAlertAccent = Color.FromArgb(108, 148, 220);
        private static readonly Color TripScoutBellCancelAccent = Color.FromArgb(168, 108, 188);

        private Panel _tsBellAlertBar;
        private Panel _tsBellAlertBarAccent;
        private Label _tsBellAlertLine1;
        private Label _tsBellAlertLine2;
        private Label _tsBellAlertCounter;
        private FlowLayoutPanel _tsBellAlertBtnFlow;
        private SupeyButton _tsBellAlertShowBtn;
        private SupeyButton _tsBellAlertPrevBtn;
        private SupeyButton _tsBellAlertNextBtn;
        private SupeyButton _tsBellAlertDismissBtn;

        private readonly List<TripScoutBellAlertItem> _tripScoutBellAlertQueue =
            new List<TripScoutBellAlertItem>();
        private int _tripScoutBellAlertIndex;
        private double _tripScoutBellCancelCursorTs;

        private sealed class TripScoutBellAlertItem
        {
            public enum ItemKind
            {
                WillCall,
                Cancelled,
            }

            public ItemKind Kind;
            public string TripNo;
            public HiatmeAiClient.WellRydeBellWillCall WillCall;
            public HiatmeAiClient.TripScoutChangeRow Change;
        }

        internal void EnsureTripScoutBellAlertBar()
        {
            if (_tsBellAlertBar != null && !_tsBellAlertBar.IsDisposed)
                return;
            if (tsmaterialCard == null || tsmaterialCard.IsDisposed)
                return;

            _tsBellAlertBar = new Panel
            {
                Name = "tsBellAlertBar",
                Visible = false,
                Height = 0,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = SupeyTheme.SurfaceElevated,
                Padding = new Padding(0),
            };

            _tsBellAlertBarAccent = new Panel
            {
                Dock = DockStyle.Left,
                Width = 3,
                BackColor = TripScoutBellAlertAccent,
            };

            _tsBellAlertBtnFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                Width = TripScoutBellAlertButtonStripWidth,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = false,
                Padding = new Padding(0, 10, 4, 0),
                BackColor = SupeyTheme.SurfaceElevated,
            };

            _tsBellAlertCounter = new Label
            {
                AutoSize = false,
                Visible = false,
                Width = 0,
                Height = 0,
                ForeColor = SupeyTheme.TextSecondary,
                BackColor = SupeyTheme.SurfaceElevated,
                Font = SupeyTheme.CaptionFont,
            };

            _tsBellAlertShowBtn = MakeTripScoutBellAlertButton("Locate", TripScoutBellAlertShowInList_Click);
            _tsBellAlertPrevBtn = MakeTripScoutBellAlertButton("◀", TripScoutBellAlertPrev_Click);
            _tsBellAlertNextBtn = MakeTripScoutBellAlertButton("▶", TripScoutBellAlertNext_Click);
            _tsBellAlertDismissBtn = MakeTripScoutBellAlertButton("Dismiss", TripScoutBellAlertDismiss_Click);

            _tsBellAlertShowBtn.Size = new Size(54, 30);
            _tsBellAlertPrevBtn.Size = new Size(30, 30);
            _tsBellAlertNextBtn.Size = new Size(30, 30);
            _tsBellAlertDismissBtn.Size = new Size(62, 30);

            _tsBellAlertBtnFlow.Controls.Add(_tsBellAlertShowBtn);
            _tsBellAlertBtnFlow.Controls.Add(_tsBellAlertPrevBtn);
            _tsBellAlertBtnFlow.Controls.Add(_tsBellAlertNextBtn);
            _tsBellAlertBtnFlow.Controls.Add(_tsBellAlertDismissBtn);

            var textHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.SurfaceElevated,
                Padding = new Padding(10, 6, 4, 6),
            };

            _tsBellAlertLine1 = new Label
            {
                Dock = DockStyle.Top,
                Height = 20,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Semibold", 9.75f),
                ForeColor = TripScoutBellAlertAccent,
                BackColor = SupeyTheme.SurfaceElevated,
            };

            _tsBellAlertLine2 = new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9f),
                ForeColor = SupeyTheme.TextSecondary,
                BackColor = SupeyTheme.SurfaceElevated,
            };

            textHost.Controls.Add(_tsBellAlertLine2);
            textHost.Controls.Add(_tsBellAlertLine1);

            var bottomRule = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 1,
                BackColor = SupeyTheme.Divider,
            };

            _tsBellAlertBar.Controls.Add(textHost);
            _tsBellAlertBar.Controls.Add(_tsBellAlertBtnFlow);
            _tsBellAlertBar.Controls.Add(_tsBellAlertBarAccent);
            _tsBellAlertBar.Controls.Add(bottomRule);

            tsmaterialCard.Controls.Add(_tsBellAlertBar);
            _tsBellAlertBar.BringToFront();
            if (_tripScoutToolbarPanel != null)
                _tripScoutToolbarPanel.BringToFront();
            SupeyDarkScrollBars.Apply(_tsBellAlertBar);
        }

        private static SupeyButton MakeTripScoutBellAlertButton(string text, EventHandler click)
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

        internal void TripScoutResetBellAlert()
        {
            _tripScoutBellAlertQueue.Clear();
            _tripScoutBellAlertIndex = 0;
            _tripScoutBellCancelCursorTs = 0;
            TripScoutHideBellAlertBar();
        }

        private void TripScoutClearBellAlertQueue()
        {
            _tripScoutBellAlertQueue.Clear();
            _tripScoutBellAlertIndex = 0;
            TripScoutHideBellAlertBar();
        }

        private void TripScoutRebuildBellAlertQueueForDisplay(bool includeAllPending)
        {
            _tripScoutBellAlertQueue.Clear();

            if (_tripScoutWillCalls != null)
            {
                foreach (var wc in _tripScoutWillCalls)
                {
                    if (wc == null || string.IsNullOrWhiteSpace(wc.TripNo))
                        continue;
                    _tripScoutBellAlertQueue.Add(new TripScoutBellAlertItem
                    {
                        Kind = TripScoutBellAlertItem.ItemKind.WillCall,
                        TripNo = wc.TripNo.Trim(),
                        WillCall = wc,
                    });
                }
            }

            if (_tripScoutDayChanges != null)
            {
                foreach (var row in _tripScoutDayChanges
                             .Where(TripScoutChangeIsCancelled)
                             .OrderByDescending(r => r.Ts ?? 0))
                {
                    if (row == null || string.IsNullOrWhiteSpace(row.TripNo))
                        continue;
                    if (!includeAllPending && (row.Ts ?? 0) <= _tripScoutBellCancelCursorTs)
                        continue;
                    string tripNo = row.TripNo.Trim();
                    if (_tripScoutBellAlertQueue.Any(i =>
                            string.Equals(i.TripNo, tripNo, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    _tripScoutBellAlertQueue.Add(new TripScoutBellAlertItem
                    {
                        Kind = TripScoutBellAlertItem.ItemKind.Cancelled,
                        TripNo = tripNo,
                        Change = row,
                    });
                }
            }
        }

        internal void TripScoutProcessNewCancelledBellAlerts()
        {
            if (_tripScoutDayChanges == null || _tripScoutDayChanges.Count == 0)
                return;

            var incoming = _tripScoutDayChanges
                .Where(r => r != null && TripScoutChangeIsCancelled(r) && (r.Ts ?? 0) > _tripScoutBellCancelCursorTs)
                .OrderByDescending(r => r.Ts ?? 0)
                .ToList();

            if (incoming.Count == 0)
                return;

            foreach (var row in incoming)
            {
                if (TripScoutBellAlertQueueContains(row))
                    continue;

                _tripScoutBellAlertQueue.Insert(0, new TripScoutBellAlertItem
                {
                    Kind = TripScoutBellAlertItem.ItemKind.Cancelled,
                    TripNo = row.TripNo.Trim(),
                    Change = row,
                });
            }

            double maxTs = incoming.Max(r => r.Ts ?? 0);
            if (maxTs > _tripScoutBellCancelCursorTs)
                _tripScoutBellCancelCursorTs = maxTs;

            if (_tripScoutBellAlertIndex < 0 || _tripScoutBellAlertIndex >= _tripScoutBellAlertQueue.Count)
                _tripScoutBellAlertIndex = 0;

            if (TripScoutLivePanelEnabled)
                TripScoutRefreshBellAlertBar();
        }

        private void LayoutTripScoutBellAlertBar()
        {
            if (_tsBellAlertBar == null || _tsBellAlertBar.IsDisposed
                || tsmaterialCard == null || _tripScoutToolbarPanel == null)
                return;

            int y = _tripScoutToolbarPanel.Bottom + BillingCardPad;
            if (_tsChangeAlertBar != null && !_tsChangeAlertBar.IsDisposed && _tsChangeAlertBar.Visible)
                y += TripScoutChangeAlertBarHeight + BillingCardPad;

            int w = Math.Max(120, tsmaterialCard.ClientSize.Width - (BillingCardPad * 2));
            int h = _tsBellAlertBar.Visible ? TripScoutBellAlertBarHeight : 0;
            _tsBellAlertBar.SetBounds(BillingCardPad, y, w, h);

            if (_tsBellAlertBtnFlow != null && !_tsBellAlertBtnFlow.IsDisposed && h > 0)
            {
                int stripW = Math.Min(
                    TripScoutBellAlertButtonStripWidth,
                    Math.Max(160, w - TripScoutBellAlertTextMinWidth - 3));
                _tsBellAlertBtnFlow.Width = stripW;
            }
        }

        private void TripScoutRefreshBellAlertBar()
        {
            EnsureTripScoutBellAlertBar();
            if (_tsBellAlertBar == null)
                return;

            if (_tripScoutBellAlertQueue.Count == 0)
            {
                TripScoutHideBellAlertBar();
                return;
            }

            if (_tripScoutBellAlertIndex < 0)
                _tripScoutBellAlertIndex = 0;
            if (_tripScoutBellAlertIndex >= _tripScoutBellAlertQueue.Count)
                _tripScoutBellAlertIndex = _tripScoutBellAlertQueue.Count - 1;

            var item = _tripScoutBellAlertQueue[_tripScoutBellAlertIndex];
            var trip = TripScoutFindTripForChange(item?.TripNo);

            int total = _tripScoutBellAlertQueue.Count;
            string pageSuffix = total > 1
                ? "  (" + (_tripScoutBellAlertIndex + 1) + "/" + total + ")"
                : "";

            if (item.Kind == TripScoutBellAlertItem.ItemKind.Cancelled)
            {
                _tsBellAlertBarAccent.BackColor = TripScoutBellCancelAccent;
                _tsBellAlertLine1.ForeColor = TripScoutBellCancelAccent;
                _tsBellAlertLine1.Text = "Cancelled — " + TripScoutChangeFormat.FormatDiff(item.Change) + pageSuffix;

                var contextParts = new List<string>();
                string context = TripScoutChangeFormat.FormatTripContext(item.Change, trip);
                if (!string.IsNullOrWhiteSpace(context))
                    contextParts.Add(context);
                string detected = TripScoutChangeFormat.FormatDetectedAt(item.Change?.Ts);
                if (!string.IsNullOrWhiteSpace(detected))
                    contextParts.Add(detected);
                _tsBellAlertLine2.Text = string.Join("  ·  ", contextParts);
            }
            else
            {
                _tsBellAlertBarAccent.BackColor = TripScoutBellAlertAccent;
                _tsBellAlertLine1.ForeColor = TripScoutBellAlertAccent;
                string tripNo = (item.TripNo ?? "").Trim();
                _tsBellAlertLine1.Text = "Will-call ready — #" + tripNo + pageSuffix;

                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(item.WillCall?.Rider))
                    parts.Add(item.WillCall.Rider.Trim());
                if (!string.IsNullOrWhiteSpace(item.WillCall?.PuAddr))
                    parts.Add(item.WillCall.PuAddr.Trim());
                string when = FormatBellWillCallWhen(item.WillCall);
                if (!string.IsNullOrWhiteSpace(when))
                    parts.Add(when);
                _tsBellAlertLine2.Text = string.Join("  ·  ", parts);
            }

            bool multi = total > 1;
            _tsBellAlertPrevBtn.Visible = multi;
            _tsBellAlertNextBtn.Visible = multi;
            _tsBellAlertPrevBtn.Enabled = multi && _tripScoutBellAlertIndex > 0;
            _tsBellAlertNextBtn.Enabled = multi && _tripScoutBellAlertIndex < total - 1;

            _tsBellAlertBar.Visible = true;
            _tsBellAlertBar.Height = TripScoutBellAlertBarHeight;
            LayoutTripScoutBellAlertBar();
            LayoutTripScoutListBounds();
            _tsBellAlertBar.BringToFront();
            if (_tripScoutToolbarPanel != null)
                _tripScoutToolbarPanel.BringToFront();
            if (_tsChangeAlertBar != null && _tsChangeAlertBar.Visible)
                _tsChangeAlertBar.BringToFront();

            TripScoutFocusBellCarouselTrip(item);
        }

        private void TripScoutFocusBellCarouselTrip(TripScoutBellAlertItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.TripNo))
                return;

            TripScoutSelectTripByNumber(item.TripNo, expandDetails: true);
        }

        private void TripScoutHideBellAlertBar()
        {
            if (_tsBellAlertBar == null || _tsBellAlertBar.IsDisposed)
                return;

            _tsBellAlertBar.Visible = false;
            _tsBellAlertBar.Height = 0;
            if (_tsBellAlertLine1 != null)
                _tsBellAlertLine1.Text = string.Empty;
            if (_tsBellAlertLine2 != null)
                _tsBellAlertLine2.Text = string.Empty;
            LayoutTripScoutBellAlertBar();
            LayoutTripScoutListBounds();
        }

        private void TripScoutBellAlertShowInList_Click(object sender, EventArgs e)
        {
            TripScoutFocusBellCarouselTrip(TripScoutPeekBellAlertItem());
        }

        private void TripScoutBellAlertPrev_Click(object sender, EventArgs e)
        {
            if (_tripScoutBellAlertIndex > 0)
            {
                _tripScoutBellAlertIndex--;
                TripScoutRefreshBellAlertBar();
            }
        }

        private void TripScoutBellAlertNext_Click(object sender, EventArgs e)
        {
            if (_tripScoutBellAlertIndex < _tripScoutBellAlertQueue.Count - 1)
            {
                _tripScoutBellAlertIndex++;
                TripScoutRefreshBellAlertBar();
            }
        }

        private void TripScoutBellAlertDismiss_Click(object sender, EventArgs e)
        {
            if (_tripScoutBellAlertQueue.Count == 0)
            {
                TripScoutHideBellAlertBar();
                AckTripScoutBell();
                return;
            }

            int idx = Math.Max(0, Math.Min(_tripScoutBellAlertIndex, _tripScoutBellAlertQueue.Count - 1));
            _tripScoutBellAlertQueue.RemoveAt(idx);
            if (_tripScoutBellAlertQueue.Count == 0)
            {
                _tripScoutBellAlertIndex = 0;
                TripScoutHideBellAlertBar();
                AckTripScoutBell();
                return;
            }

            if (_tripScoutBellAlertIndex >= _tripScoutBellAlertQueue.Count)
                _tripScoutBellAlertIndex = _tripScoutBellAlertQueue.Count - 1;
            TripScoutRefreshBellAlertBar();
        }

        private TripScoutBellAlertItem TripScoutPeekBellAlertItem()
        {
            if (_tripScoutBellAlertQueue.Count == 0)
                return null;
            int idx = Math.Max(0, Math.Min(_tripScoutBellAlertIndex, _tripScoutBellAlertQueue.Count - 1));
            return _tripScoutBellAlertQueue[idx];
        }

        private bool TripScoutBellAlertQueueContains(HiatmeAiClient.TripScoutChangeRow row)
        {
            if (row == null || string.IsNullOrWhiteSpace(row.TripNo))
                return true;

            string sig = TripScoutBellCancelSignature(row);
            foreach (var existing in _tripScoutBellAlertQueue)
            {
                if (existing?.Kind != TripScoutBellAlertItem.ItemKind.Cancelled || existing.Change == null)
                    continue;
                if (string.Equals(TripScoutBellCancelSignature(existing.Change), sig, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static string TripScoutBellCancelSignature(HiatmeAiClient.TripScoutChangeRow row)
        {
            return (row.TripNo ?? "") + "|" + (row.Ts?.ToString(CultureInfo.InvariantCulture) ?? "") + "|cancel";
        }

        private static bool TripScoutChangeIsCancelled(HiatmeAiClient.TripScoutChangeRow row)
        {
            if (row == null)
                return false;

            if (row.Tags != null)
            {
                foreach (var tag in row.Tags)
                {
                    if (string.Equals(tag, "cancelled", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            if (row.Fields != null)
            {
                foreach (var field in row.Fields)
                {
                    if (field == null || !string.Equals(field.Field, "status", StringComparison.OrdinalIgnoreCase))
                        continue;
                    string after = Convert.ToString(field.After, CultureInfo.InvariantCulture) ?? "";
                    if (after.IndexOf("cancel", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }

            string summary = (row.Summary ?? "").Trim();
            if (summary.IndexOf("cancel", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return false;
        }

        private static string FormatBellWillCallWhen(HiatmeAiClient.WellRydeBellWillCall wc)
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
    }
}
