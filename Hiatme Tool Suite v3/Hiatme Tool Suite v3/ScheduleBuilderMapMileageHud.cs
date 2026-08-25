using System;
using System.Drawing;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Compact overlay for group miles, route efficiency, and selected-trip miles.
    /// One short bar that wraps when the map is narrow instead of three tall cards.
    /// </summary>
    internal sealed class ScheduleBuilderMapMileageHud : Panel
    {
        private readonly FlowLayoutPanel _row;
        private readonly MileageChip _groupChip;
        private readonly MileageChip _effChip;
        private readonly MileageChip _tripChip;
        private readonly ToolTip _tip;

        public ScheduleBuilderMapMileageHud()
        {
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            BackColor = Color.Transparent;
            Visible = false;

            _row = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Padding = new Padding(6, 3, 6, 3),
                Margin = new Padding(0),
                BackColor = Color.FromArgb(228, 28, 32, 28),
            };
            _row.Paint += OnRowPaint;

            _groupChip = new MileageChip();
            _effChip = new MileageChip();
            _tripChip = new MileageChip();

            _row.Controls.Add(_groupChip);
            _row.Controls.Add(_effChip);
            _row.Controls.Add(_tripChip);

            Controls.Add(_row);

            _tip = SupeyToolTip.Create(initialDelay: 280);
            SupeyThemeManager.ThemeChanged += OnThemeChanged;
        }

        public void SetValues(
            SupeyTripCluster group,
            MCDownloadedTrip trip,
            double groupMeters,
            double? tripMeters,
            bool tripApprox,
            double? efficiencyScorePercent,
            bool efficiencyApprox,
            double? routeChangeMeters)
        {
            if (group == null)
            {
                HideHud();
                return;
            }

            string groupTip = "Group " + group.GroupNumber
                + (group.IsStraightLineFallback ? " · estimated straight-line" : " · road route");
            string groupValue = SupeyTripTimes.FormatMiles(groupMeters);
            if (routeChangeMeters.HasValue && Math.Abs(routeChangeMeters.Value) >= 80)
            {
                string delta = SupeyTripTimes.FormatMiles(Math.Abs(routeChangeMeters.Value));
                groupValue += routeChangeMeters.Value > 0 ? "  +" + delta : "  −" + delta;
            }

            _groupChip.Set(
                "G" + group.GroupNumber,
                groupValue,
                SupeyTheme.AccentPrimary,
                groupTip);
            _groupChip.Visible = true;

            if (efficiencyScorePercent.HasValue)
            {
                double score = efficiencyScorePercent.Value;
                Color color = score >= 85
                    ? SupeyTheme.AccentPrimary
                    : score >= 70
                        ? Color.FromArgb(220, 180, 70)
                        : Color.FromArgb(220, 120, 90);
                _effChip.Set(
                    "Eff",
                    score.ToString("0") + "%" + (efficiencyApprox ? "~" : ""),
                    color,
                    efficiencyApprox
                        ? "Route efficiency (approximate for this group size)"
                        : "Route efficiency vs best trip order");
                _effChip.Visible = true;
            }
            else if (group.RiderCount > 0)
            {
                _effChip.Set("Eff", "—", SupeyTheme.TextMuted, "Route efficiency unavailable");
                _effChip.Visible = true;
            }
            else
            {
                _effChip.Visible = false;
            }

            if (trip != null)
            {
                string tn = (trip.TripNumber ?? "").Trim();
                string label = tn.Length > 0 ? "#" + tn : "Trip";
                string value = tripMeters.HasValue
                    ? SupeyTripTimes.FormatMiles(tripMeters.Value)
                    : "—";
                string tip = (tn.Length > 0 ? tn + " · " : "")
                    + "Pickup → dropoff"
                    + (tripApprox ? " · estimated" : "");
                _tripChip.Set(
                    label,
                    value,
                    tripMeters.HasValue ? SupeyTheme.AccentPrimary : SupeyTheme.TextMuted,
                    tip);
                _tripChip.Visible = true;
            }
            else
            {
                _tripChip.Visible = false;
            }

            ApplyTips();
            ShowHud();
        }

        public void SetBusy(SupeyTripCluster group, MCDownloadedTrip trip)
        {
            if (group == null)
            {
                HideHud();
                return;
            }

            _groupChip.Set("G" + group.GroupNumber, "…", SupeyTheme.TextMuted, "Calculating group mileage…");
            _groupChip.Visible = true;
            _effChip.Set("Eff", "…", SupeyTheme.TextMuted, "Comparing trip orders…");
            _effChip.Visible = true;

            if (trip != null)
            {
                string tn = (trip.TripNumber ?? "").Trim();
                _tripChip.Set(tn.Length > 0 ? "#" + tn : "Trip", "…", SupeyTheme.TextMuted, "Calculating trip mileage…");
                _tripChip.Visible = true;
            }
            else
            {
                _tripChip.Visible = false;
            }

            ApplyTips();
            ShowHud();
        }

        public void HideHud()
        {
            Visible = false;
        }

        public void FitToMap(Size mapClient)
        {
            int maxW = Math.Max(132, mapClient.Width - 20);
            _row.MaximumSize = new Size(maxW, 0);
            _row.PerformLayout();
            Size = _row.PreferredSize;
            Location = new Point(10, 10);
            if (Visible)
                BringToFront();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                SupeyThemeManager.ThemeChanged -= OnThemeChanged;
                _tip?.Dispose();
            }
            base.Dispose(disposing);
        }

        private void ShowHud()
        {
            Visible = true;
            _row.PerformLayout();
            Size = _row.PreferredSize;
            BringToFront();
        }

        private void ApplyTips()
        {
            _tip.SetToolTip(_groupChip, _groupChip.TipText);
            _tip.SetToolTip(_effChip, _effChip.Visible ? _effChip.TipText : "");
            _tip.SetToolTip(_tripChip, _tripChip.Visible ? _tripChip.TipText : "");
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            if (IsDisposed)
                return;
            _row.BackColor = Color.FromArgb(228, 28, 32, 28);
            _row.Invalidate();
        }

        private static void OnRowPaint(object sender, PaintEventArgs e)
        {
            var panel = sender as Control;
            if (panel == null)
                return;
            using (var pen = new Pen(SupeyTheme.BorderSubtle))
                e.Graphics.DrawRectangle(pen, 0, 0, panel.Width - 1, panel.Height - 1);
        }

        private sealed class MileageChip : Panel
        {
            private readonly Label _key;
            private readonly Label _value;

            public string TipText { get; private set; } = "";

            public MileageChip()
            {
                AutoSize = true;
                AutoSizeMode = AutoSizeMode.GrowAndShrink;
                BackColor = Color.Transparent;
                Margin = new Padding(2, 0, 10, 0);
                Height = 22;

                _key = new Label
                {
                    AutoSize = true,
                    ForeColor = SupeyTheme.TextMuted,
                    BackColor = Color.Transparent,
                    Font = new Font("Segoe UI Semibold", 8f),
                    Margin = new Padding(0, 3, 5, 0),
                };
                _value = new Label
                {
                    AutoSize = true,
                    ForeColor = SupeyTheme.AccentPrimary,
                    BackColor = Color.Transparent,
                    Font = new Font("Segoe UI Semibold", 9.25f),
                    Margin = new Padding(0, 2, 0, 0),
                };

                var flow = new FlowLayoutPanel
                {
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    FlowDirection = FlowDirection.LeftToRight,
                    WrapContents = false,
                    BackColor = Color.Transparent,
                    Margin = new Padding(0),
                    Padding = new Padding(0),
                };
                flow.Controls.Add(_key);
                flow.Controls.Add(_value);
                Controls.Add(flow);
            }

            public void Set(string key, string value, Color valueColor, string tip)
            {
                _key.Text = key ?? "";
                _value.Text = value ?? "";
                _value.ForeColor = valueColor;
                TipText = tip ?? "";
            }
        }
    }
}
