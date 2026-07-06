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
        private const int TripScoutChangeAlertBarHeight = 54;
        private static readonly Color TripScoutChangeAlertAccent = Color.FromArgb(200, 156, 72);

        private Panel _tsChangeAlertBar;
        private Panel _tsChangeAlertBarAccent;
        private Label _tsChangeAlertLine1;
        private Label _tsChangeAlertLine2;
        private Label _tsChangeAlertCounter;
        private FlowLayoutPanel _tsChangeAlertBtnFlow;
        private SupeyButton _tsChangeAlertShowBtn;
        private SupeyButton _tsChangeAlertDetailsBtn;
        private SupeyButton _tsChangeAlertPrevBtn;
        private SupeyButton _tsChangeAlertNextBtn;
        private SupeyButton _tsChangeAlertDismissBtn;

        private readonly List<HiatmeAiClient.TripScoutChangeRow> _tripScoutChangeAlertQueue =
            new List<HiatmeAiClient.TripScoutChangeRow>();
        private int _tripScoutChangeAlertIndex;
        private double _tripScoutAlertCursorTs;
        private bool _tripScoutChangeAlertNeedsBaseline;

        internal void EnsureTripScoutChangeAlertBar()
        {
            if (_tsChangeAlertBar != null && !_tsChangeAlertBar.IsDisposed)
                return;
            if (tsmaterialCard == null || tsmaterialCard.IsDisposed)
                return;

            _tsChangeAlertBar = new Panel
            {
                Name = "tsChangeAlertBar",
                Visible = false,
                Height = 0,
                BackColor = SupeyTheme.SurfaceElevated,
                Padding = new Padding(0),
            };

            _tsChangeAlertBarAccent = new Panel
            {
                Dock = DockStyle.Left,
                Width = 3,
                BackColor = TripScoutChangeAlertAccent,
            };

            _tsChangeAlertBtnFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                Width = 300,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = false,
                Padding = new Padding(0, 10, 8, 0),
                BackColor = SupeyTheme.SurfaceElevated,
            };

            _tsChangeAlertCounter = new Label
            {
                AutoSize = true,
                ForeColor = SupeyTheme.TextSecondary,
                BackColor = SupeyTheme.SurfaceElevated,
                Font = SupeyTheme.CaptionFont,
                Margin = new Padding(0, 8, 6, 0),
                TextAlign = ContentAlignment.MiddleLeft,
            };

            _tsChangeAlertShowBtn = MakeTripScoutChangeAlertButton("Show in list", TripScoutChangeAlertShowInList_Click);
            _tsChangeAlertDetailsBtn = MakeTripScoutChangeAlertButton("Details", TripScoutChangeAlertDetails_Click);
            _tsChangeAlertPrevBtn = MakeTripScoutChangeAlertButton("◀", TripScoutChangeAlertPrev_Click);
            _tsChangeAlertNextBtn = MakeTripScoutChangeAlertButton("▶", TripScoutChangeAlertNext_Click);
            _tsChangeAlertDismissBtn = MakeTripScoutChangeAlertButton("Dismiss", TripScoutChangeAlertDismiss_Click);

            _tsChangeAlertPrevBtn.Size = new Size(34, 30);
            _tsChangeAlertNextBtn.Size = new Size(34, 30);
            _tsChangeAlertDismissBtn.Size = new Size(72, 30);

            _tsChangeAlertBtnFlow.Controls.Add(_tsChangeAlertCounter);
            _tsChangeAlertBtnFlow.Controls.Add(_tsChangeAlertShowBtn);
            _tsChangeAlertBtnFlow.Controls.Add(_tsChangeAlertDetailsBtn);
            _tsChangeAlertBtnFlow.Controls.Add(_tsChangeAlertPrevBtn);
            _tsChangeAlertBtnFlow.Controls.Add(_tsChangeAlertNextBtn);
            _tsChangeAlertBtnFlow.Controls.Add(_tsChangeAlertDismissBtn);

            var textHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.SurfaceElevated,
                Padding = new Padding(10, 6, 4, 6),
            };

            _tsChangeAlertLine1 = new Label
            {
                Dock = DockStyle.Top,
                Height = 20,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Semibold", 9.75f),
                ForeColor = TripScoutChangeAlertAccent,
                BackColor = SupeyTheme.SurfaceElevated,
            };

            _tsChangeAlertLine2 = new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9f),
                ForeColor = SupeyTheme.TextSecondary,
                BackColor = SupeyTheme.SurfaceElevated,
            };

            textHost.Controls.Add(_tsChangeAlertLine2);
            textHost.Controls.Add(_tsChangeAlertLine1);

            var bottomRule = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 1,
                BackColor = SupeyTheme.Divider,
            };

            _tsChangeAlertBar.Controls.Add(textHost);
            _tsChangeAlertBar.Controls.Add(_tsChangeAlertBtnFlow);
            _tsChangeAlertBar.Controls.Add(_tsChangeAlertBarAccent);
            _tsChangeAlertBar.Controls.Add(bottomRule);

            tsmaterialCard.Controls.Add(_tsChangeAlertBar);
            _tsChangeAlertBar.BringToFront();
            if (_tripScoutToolbarPanel != null)
                _tripScoutToolbarPanel.BringToFront();
            SupeyDarkScrollBars.Apply(_tsChangeAlertBar);
        }

        private static SupeyButton MakeTripScoutChangeAlertButton(string text, EventHandler click)
        {
            var btn = new SupeyButton
            {
                Text = text,
                AutoSize = false,
                Size = new Size(88, 30),
                Margin = new Padding(0, 1, 4, 0),
                Kind = SupeyButton.Variant.Secondary,
            };
            btn.Click += click;
            return btn;
        }

        internal void TripScoutResetChangeAlert()
        {
            _tripScoutChangeAlertQueue.Clear();
            _tripScoutChangeAlertIndex = 0;
            _tripScoutAlertCursorTs = 0;
            _tripScoutChangeAlertNeedsBaseline = true;
            TripScoutHideChangeAlertBar();
        }

        internal void TripScoutClearChangeAlertQueue()
        {
            _tripScoutChangeAlertQueue.Clear();
            _tripScoutChangeAlertIndex = 0;
            _tripScoutAlertCursorTs = TripScoutMaxChangeTs(_tripScoutDayChanges);
            TripScoutHideChangeAlertBar();
        }

        internal void TripScoutProcessNewChangeAlerts()
        {
            if (_tripScoutDayChanges == null)
                return;

            if (_tripScoutChangeAlertNeedsBaseline)
            {
                _tripScoutChangeAlertNeedsBaseline = false;
                _tripScoutAlertCursorTs = TripScoutMaxChangeTs(_tripScoutDayChanges);
                return;
            }

            var incoming = _tripScoutDayChanges
                .Where(r => r != null && (r.Ts ?? 0) > _tripScoutAlertCursorTs)
                .OrderByDescending(r => r.Ts ?? 0)
                .ToList();

            if (incoming.Count == 0)
                return;

            foreach (var row in incoming)
            {
                if (TripScoutChangeAlertQueueContains(row))
                    continue;
                _tripScoutChangeAlertQueue.Insert(0, row);
            }

            double maxTs = incoming.Max(r => r.Ts ?? 0);
            if (maxTs > _tripScoutAlertCursorTs)
                _tripScoutAlertCursorTs = maxTs;

            if (_tripScoutChangeAlertIndex < 0 || _tripScoutChangeAlertIndex >= _tripScoutChangeAlertQueue.Count)
                _tripScoutChangeAlertIndex = 0;

            TripScoutRefreshChangeAlertBar();
            TripScoutProcessNewCancelledBellAlerts();
        }

        private void LayoutTripScoutChangeAlertBar()
        {
            if (_tsChangeAlertBar == null || _tsChangeAlertBar.IsDisposed
                || tsmaterialCard == null || _tripScoutToolbarPanel == null)
                return;

            int y = _tripScoutToolbarPanel.Bottom + BillingCardPad;
            int w = Math.Max(120, tsmaterialCard.ClientSize.Width - (BillingCardPad * 2));
            int h = _tsChangeAlertBar.Visible ? TripScoutChangeAlertBarHeight : 0;
            _tsChangeAlertBar.SetBounds(BillingCardPad, y, w, h);
        }

        private void TripScoutRefreshChangeAlertBar()
        {
            EnsureTripScoutChangeAlertBar();
            if (_tsChangeAlertBar == null)
                return;

            if (_tripScoutChangeAlertQueue.Count == 0)
            {
                TripScoutHideChangeAlertBar();
                return;
            }

            if (_tripScoutChangeAlertIndex < 0)
                _tripScoutChangeAlertIndex = 0;
            if (_tripScoutChangeAlertIndex >= _tripScoutChangeAlertQueue.Count)
                _tripScoutChangeAlertIndex = _tripScoutChangeAlertQueue.Count - 1;

            var row = _tripScoutChangeAlertQueue[_tripScoutChangeAlertIndex];
            var trip = TripScoutFindTripForChange(row?.TripNo);

            _tsChangeAlertLine1.Text = TripScoutChangeFormat.FormatDiff(row);
            _tsChangeAlertLine1.ForeColor = TripScoutChangeAlertAccent;

            var contextParts = new List<string>();
            string context = TripScoutChangeFormat.FormatTripContext(row, trip);
            if (!string.IsNullOrWhiteSpace(context))
                contextParts.Add(context);
            string detected = TripScoutChangeFormat.FormatDetectedAt(row?.Ts);
            if (!string.IsNullOrWhiteSpace(detected))
                contextParts.Add(detected);
            _tsChangeAlertLine2.Text = string.Join("  ·  ", contextParts);

            int total = _tripScoutChangeAlertQueue.Count;
            _tsChangeAlertCounter.Text = total > 1
                ? (_tripScoutChangeAlertIndex + 1) + " / " + total
                : "";
            _tsChangeAlertCounter.Visible = total > 1;

            bool multi = total > 1;
            _tsChangeAlertPrevBtn.Visible = multi;
            _tsChangeAlertNextBtn.Visible = multi;
            _tsChangeAlertPrevBtn.Enabled = multi && _tripScoutChangeAlertIndex > 0;
            _tsChangeAlertNextBtn.Enabled = multi && _tripScoutChangeAlertIndex < total - 1;

            _tsChangeAlertBar.Visible = true;
            _tsChangeAlertBar.Height = TripScoutChangeAlertBarHeight;
            LayoutTripScoutChangeAlertBar();
            LayoutTripScoutListBounds();
            _tsChangeAlertBar.BringToFront();
            if (_tripScoutToolbarPanel != null)
                _tripScoutToolbarPanel.BringToFront();
        }

        private void TripScoutHideChangeAlertBar()
        {
            if (_tsChangeAlertBar == null || _tsChangeAlertBar.IsDisposed)
                return;

            _tsChangeAlertBar.Visible = false;
            _tsChangeAlertBar.Height = 0;
            if (_tsChangeAlertLine1 != null)
                _tsChangeAlertLine1.Text = string.Empty;
            if (_tsChangeAlertLine2 != null)
                _tsChangeAlertLine2.Text = string.Empty;
            LayoutTripScoutChangeAlertBar();
            LayoutTripScoutListBounds();
        }

        private void TripScoutChangeAlertShowInList_Click(object sender, EventArgs e)
        {
            var row = TripScoutPeekChangeAlertRow();
            if (row == null || string.IsNullOrWhiteSpace(row.TripNo))
                return;

            string tripNo = row.TripNo.Trim();
            TripScoutSelectTripByNumber(tripNo);
            if (!_tripScoutExpandedTripNos.Contains(tripNo))
                _tripScoutExpandedTripNos.Add(tripNo);
            TripScoutRebindVisibleListPreserveScroll();
        }

        private void TripScoutChangeAlertDetails_Click(object sender, EventArgs e)
        {
            var row = TripScoutPeekChangeAlertRow();
            if (row == null || string.IsNullOrWhiteSpace(row.TripNo))
                return;

            string tripNo = row.TripNo.Trim();
            if (!_tripScoutExpandedTripNos.Contains(tripNo))
                _tripScoutExpandedTripNos.Add(tripNo);
            TripScoutSelectTripByNumber(tripNo);
            TripScoutRebindVisibleListPreserveScroll();
        }

        private void TripScoutChangeAlertPrev_Click(object sender, EventArgs e)
        {
            if (_tripScoutChangeAlertIndex > 0)
            {
                _tripScoutChangeAlertIndex--;
                TripScoutRefreshChangeAlertBar();
            }
        }

        private void TripScoutChangeAlertNext_Click(object sender, EventArgs e)
        {
            if (_tripScoutChangeAlertIndex < _tripScoutChangeAlertQueue.Count - 1)
            {
                _tripScoutChangeAlertIndex++;
                TripScoutRefreshChangeAlertBar();
            }
        }

        private void TripScoutChangeAlertDismiss_Click(object sender, EventArgs e)
        {
            if (_tripScoutChangeAlertQueue.Count == 0)
            {
                TripScoutHideChangeAlertBar();
                return;
            }

            int idx = Math.Max(0, Math.Min(_tripScoutChangeAlertIndex, _tripScoutChangeAlertQueue.Count - 1));
            _tripScoutChangeAlertQueue.RemoveAt(idx);
            if (_tripScoutChangeAlertQueue.Count == 0)
            {
                _tripScoutChangeAlertIndex = 0;
                TripScoutHideChangeAlertBar();
                return;
            }

            if (_tripScoutChangeAlertIndex >= _tripScoutChangeAlertQueue.Count)
                _tripScoutChangeAlertIndex = _tripScoutChangeAlertQueue.Count - 1;
            TripScoutRefreshChangeAlertBar();
        }

        private HiatmeAiClient.TripScoutChangeRow TripScoutPeekChangeAlertRow()
        {
            if (_tripScoutChangeAlertQueue.Count == 0)
                return null;
            int idx = Math.Max(0, Math.Min(_tripScoutChangeAlertIndex, _tripScoutChangeAlertQueue.Count - 1));
            return _tripScoutChangeAlertQueue[idx];
        }

        private WRDownloadedTrip TripScoutFindTripForChange(string tripNo)
        {
            return TripScoutFindLoadedTrip(tripNo);
        }

        private static double TripScoutMaxChangeTs(IList<HiatmeAiClient.TripScoutChangeRow> rows)
        {
            if (rows == null || rows.Count == 0)
                return 0;
            double max = 0;
            foreach (var row in rows)
            {
                if (row?.Ts == null)
                    continue;
                if (row.Ts.Value > max)
                    max = row.Ts.Value;
            }
            return max;
        }

        private bool TripScoutChangeAlertQueueContains(HiatmeAiClient.TripScoutChangeRow row)
        {
            if (row == null)
                return true;
            string sig = TripScoutChangeAlertSignature(row);
            foreach (var existing in _tripScoutChangeAlertQueue)
            {
                if (existing != null
                    && string.Equals(TripScoutChangeAlertSignature(existing), sig, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static string TripScoutChangeAlertSignature(HiatmeAiClient.TripScoutChangeRow row)
        {
            return (row.TripNo ?? "") + "|" + (row.Ts?.ToString(CultureInfo.InvariantCulture) ?? "") + "|"
                + TripScoutChangeFormat.FormatDiff(row);
        }
    }
}
