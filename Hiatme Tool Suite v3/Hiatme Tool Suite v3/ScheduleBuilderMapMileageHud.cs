using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Full-width map strip: group mileage, route efficiency, and trip mileage.
    /// Optional Pin / Dock / Hide icons sit on the right when the map is floating.
    /// </summary>
    internal sealed class ScheduleBuilderMapMileageHud : Panel
    {
        public const int StripHeight = 48;
        private const int ActionSize = 26;
        private const int ActionGap = 4;
        private const int ActionPad = 10;

        private static readonly TextFormatFlags TextFlags =
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine
            | TextFormatFlags.VerticalCenter | TextFormatFlags.Left;

        private readonly Font _labelFont = new Font("Segoe UI Semibold", 8.25f);
        private readonly Font _valueFont = new Font("Segoe UI Semibold", 11f);
        private readonly ToolTip _tip;

        private Column _group;
        private Column _efficiency;
        private Column _trip;
        private int _hoverColumn = -1;
        private int _hoverAction = -1;
        private bool _windowActionsVisible;
        private bool _pinned;

        public event Action PinClicked;
        public event Action DockClicked;
        public event Action HideClicked;

        public ScheduleBuilderMapMileageHud()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw,
                true);
            UpdateStyles();
            Dock = DockStyle.Top;
            Height = StripHeight;
            MinimumSize = new Size(0, StripHeight);
            BackColor = SupeyTheme.SurfaceHeader;
            Visible = false;
            TabStop = false;
            Cursor = Cursors.Default;
            _tip = SupeyToolTip.Create(initialDelay: 250);
            SupeyThemeManager.ThemeChanged += OnThemeChanged;
        }

        public void SetWindowActions(bool visible, bool pinned)
        {
            _windowActionsVisible = visible;
            _pinned = pinned;
            if (visible && !Visible)
                ShowHud();
            else if (!visible && _group == null && _efficiency == null && _trip == null)
                Visible = false;
            Invalidate();
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

            string groupValue = SupeyTripTimes.FormatMiles(groupMeters);
            if (routeChangeMeters.HasValue && Math.Abs(routeChangeMeters.Value) >= 80)
            {
                string delta = SupeyTripTimes.FormatMiles(Math.Abs(routeChangeMeters.Value));
                groupValue += routeChangeMeters.Value > 0 ? "  +" + delta : "  -" + delta;
            }

            string groupKind = group.IsStraightLineFallback ? "estimated straight-line" : "road route";
            _group = new Column(
                "Group mileage  ·  Group " + group.GroupNumber,
                groupValue,
                SupeyTheme.AccentPrimary,
                "Group " + group.GroupNumber + "  ·  " + groupKind);

            if (efficiencyScorePercent.HasValue)
            {
                double score = efficiencyScorePercent.Value;
                Color color = score >= 85
                    ? SupeyTheme.AccentPrimary
                    : score >= 70
                        ? Color.FromArgb(220, 180, 70)
                        : Color.FromArgb(220, 120, 90);
                string detail = efficiencyApprox
                    ? "Approximate for this group size"
                    : "Compared to the best trip order";
                _efficiency = new Column(
                    efficiencyApprox ? "Route efficiency  ·  approximate" : "Route efficiency",
                    score.ToString("0") + "%",
                    color,
                    detail);
            }
            else if (group.RiderCount > 0)
            {
                _efficiency = new Column(
                    "Route efficiency",
                    "Not available",
                    SupeyTheme.TextMuted,
                    "Route efficiency is not available for this group");
            }
            else
            {
                _efficiency = new Column(
                    "Route efficiency",
                    "No trips in group",
                    SupeyTheme.TextMuted,
                    "");
            }

            if (trip != null)
            {
                string tn = (trip.TripNumber ?? "").Trim();
                string miles = tripMeters.HasValue
                    ? SupeyTripTimes.FormatMiles(tripMeters.Value)
                    : "Not available";
                string detail = (tn.Length > 0 ? "Trip " + tn + "  ·  " : "")
                    + "pickup to dropoff"
                    + (tripApprox ? "  ·  estimated" : "");
                _trip = new Column(
                    "Trip mileage" + (tn.Length > 0 ? "  ·  Trip " + tn : "")
                        + (tripApprox ? "  ·  estimated" : ""),
                    miles,
                    tripMeters.HasValue ? SupeyTheme.AccentPrimary : SupeyTheme.TextMuted,
                    detail);
            }
            else
            {
                _trip = new Column(
                    "Trip mileage",
                    "Select a trip",
                    SupeyTheme.TextMuted,
                    "Select a trip to show pickup-to-dropoff mileage");
            }

            ShowHud();
        }

        public void SetBusy(SupeyTripCluster group, MCDownloadedTrip trip)
        {
            if (group == null)
            {
                HideHud();
                return;
            }

            _group = new Column(
                "Group mileage  ·  Group " + group.GroupNumber,
                "Calculating…",
                SupeyTheme.TextMuted,
                "Calculating group mileage");
            _efficiency = new Column(
                "Route efficiency",
                "Calculating…",
                SupeyTheme.TextMuted,
                "Comparing trip orders");
            if (trip != null)
            {
                string tn = (trip.TripNumber ?? "").Trim();
                _trip = new Column(
                    "Trip mileage" + (tn.Length > 0 ? "  ·  Trip " + tn : ""),
                    "Calculating…",
                    SupeyTheme.TextMuted,
                    "Calculating trip mileage");
            }
            else
            {
                _trip = new Column(
                    "Trip mileage",
                    "Select a trip",
                    SupeyTheme.TextMuted,
                    "Select a trip to show pickup-to-dropoff mileage");
            }

            ShowHud();
        }

        public void HideHud()
        {
            _group = _efficiency = _trip = null;
            _tip.SetToolTip(this, "");
            if (_windowActionsVisible)
            {
                ShowHud();
                return;
            }

            Visible = false;
        }

        public void FitToMap(Size mapClient)
        {
            Height = StripHeight;
            if (Visible)
                BringToFront();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(SupeyTheme.SurfaceHeader);

            using (var edge = new Pen(SupeyTheme.Divider))
                g.DrawLine(edge, 0, Height - 1, Width, Height - 1);

            Rectangle[] cols = ColumnBounds();
            for (int i = 0; i < cols.Length; i++)
            {
                if (i > 0 && cols[i].Width > 8)
                {
                    using (var div = new Pen(SupeyTheme.Divider))
                        g.DrawLine(div, cols[i].X, 8, cols[i].X, Height - 8);
                }

                Column col = ColumnAt(i);
                if (col == null)
                    continue;

                var labelRect = new Rectangle(cols[i].X + 14, 5, Math.Max(8, cols[i].Width - 28), 16);
                var valueRect = new Rectangle(cols[i].X + 14, 22, Math.Max(8, cols[i].Width - 28), 20);

                TextRenderer.DrawText(g, col.Label, _labelFont, labelRect, SupeyTheme.TextMuted, TextFlags);
                TextRenderer.DrawText(g, col.Value, _valueFont, valueRect, col.ValueColor, TextFlags);
            }

            if (!_windowActionsVisible)
                return;

            for (int i = 0; i < 3; i++)
                PaintAction(g, i, ActionRect(i));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int action = HitTestAction(e.Location);
            int col = action >= 0 ? -1 : HitTestColumn(e.Location);
            Cursor = action >= 0 ? Cursors.Hand : Cursors.Default;

            if (action == _hoverAction && col == _hoverColumn)
                return;

            _hoverAction = action;
            _hoverColumn = col;
            if (action >= 0)
                _tip.SetToolTip(this, ActionTip(action));
            else
            {
                Column hit = ColumnAt(col);
                _tip.SetToolTip(this, hit != null ? hit.Tip : "");
            }
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hoverColumn = -1;
            _hoverAction = -1;
            Cursor = Cursors.Default;
            _tip.SetToolTip(this, "");
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left)
                return;

            switch (HitTestAction(e.Location))
            {
                case 0:
                    PinClicked?.Invoke();
                    break;
                case 1:
                    DockClicked?.Invoke();
                    break;
                case 2:
                    HideClicked?.Invoke();
                    break;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                SupeyThemeManager.ThemeChanged -= OnThemeChanged;
                _tip?.Dispose();
                _labelFont.Dispose();
                _valueFont.Dispose();
            }
            base.Dispose(disposing);
        }

        private void ShowHud()
        {
            Height = StripHeight;
            Visible = true;
            BringToFront();
            Invalidate();
        }

        private int ActionClusterWidth()
        {
            if (!_windowActionsVisible)
                return 0;
            return ActionPad + (ActionSize * 3) + (ActionGap * 2) + ActionPad;
        }

        private Rectangle[] ColumnBounds()
        {
            int w = Math.Max(0, Width - ActionClusterWidth());
            int third = w / 3;
            return new[]
            {
                new Rectangle(0, 0, third, Height),
                new Rectangle(third, 0, third, Height),
                new Rectangle(third * 2, 0, Math.Max(0, w - third * 2), Height),
            };
        }

        private Rectangle ActionRect(int index)
        {
            int cluster = ActionClusterWidth();
            int x = Width - cluster + ActionPad + index * (ActionSize + ActionGap);
            int y = Math.Max(0, (Height - ActionSize) / 2);
            return new Rectangle(x, y, ActionSize, ActionSize);
        }

        private Column ColumnAt(int index)
        {
            switch (index)
            {
                case 0: return _group;
                case 1: return _efficiency;
                case 2: return _trip;
                default: return null;
            }
        }

        private int HitTestColumn(Point p)
        {
            var cols = ColumnBounds();
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i].Contains(p))
                    return i;
            }
            return -1;
        }

        private int HitTestAction(Point p)
        {
            if (!_windowActionsVisible)
                return -1;
            for (int i = 0; i < 3; i++)
            {
                if (ActionRect(i).Contains(p))
                    return i;
            }
            return -1;
        }

        private string ActionTip(int index)
        {
            switch (index)
            {
                case 0:
                    return _pinned
                        ? "Unpin this window"
                        : "Keep this window above other windows";
                case 1:
                    return "Put the map back into Schedule Builder";
                case 2:
                    return "Hide the map. Show it again from Schedule Builder.";
                default:
                    return "";
            }
        }

        private void PaintAction(Graphics g, int index, Rectangle rect)
        {
            bool hover = _hoverAction == index;
            bool active = index == 0 && _pinned;
            Color fill = active
                ? Color.FromArgb(48, SupeyTheme.AccentPrimary)
                : hover
                    ? SupeyTheme.SurfaceElevated
                    : Color.Transparent;
            if (fill.A > 0)
            {
                using (var brush = new SolidBrush(fill))
                using (var path = RoundedRect(rect, 4))
                    g.FillPath(brush, path);
            }

            if (active || hover)
            {
                using (var pen = new Pen(active ? SupeyTheme.AccentPrimary : SupeyTheme.BorderSubtle))
                using (var path = RoundedRect(new Rectangle(rect.X, rect.Y, rect.Width - 1, rect.Height - 1), 4))
                    g.DrawPath(pen, path);
            }

            Color ink = active || hover ? SupeyTheme.TextPrimary : SupeyTheme.TextSecondary;
            var icon = new Rectangle(rect.X + 5, rect.Y + 5, rect.Width - 10, rect.Height - 10);
            switch (index)
            {
                case 0:
                    DrawPinIcon(g, icon, ink, active);
                    break;
                case 1:
                    DrawDockIcon(g, icon, ink);
                    break;
                default:
                    DrawHideIcon(g, icon, ink);
                    break;
            }
        }

        private static void DrawPinIcon(Graphics g, Rectangle r, Color color, bool pinned)
        {
            using (var pen = new Pen(color, 1.6f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (var brush = new SolidBrush(pinned ? color : Color.FromArgb(0, color)))
            {
                var head = new RectangleF(r.X + 3, r.Y, r.Width - 6, r.Height * 0.55f);
                g.DrawEllipse(pen, head);
                if (pinned)
                    g.FillEllipse(brush, head);
                g.DrawLine(pen, r.X + r.Width / 2f, head.Bottom - 1, r.X + r.Width / 2f, r.Bottom);
            }
        }

        private static void DrawDockIcon(Graphics g, Rectangle r, Color color)
        {
            using (var pen = new Pen(color, 1.6f) { LineJoin = LineJoin.Round })
            {
                g.DrawRectangle(pen, r.X + 1, r.Y + 1, r.Width - 6, r.Height - 6);
                g.DrawLine(pen, r.X + 4, r.Bottom - 2, r.Right - 1, r.Bottom - 2);
                g.DrawLine(pen, r.Right - 2, r.Y + 4, r.Right - 2, r.Bottom - 1);
            }
        }

        private static void DrawHideIcon(Graphics g, Rectangle r, Color color)
        {
            using (var pen = new Pen(color, 1.6f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawLine(pen, r.X + 2, r.Y + r.Height / 2f, r.Right - 2, r.Y + r.Height / 2f);
                g.DrawLine(pen, r.X + r.Width / 2f - 3, r.Y + 3, r.X + 2, r.Y + r.Height / 2f);
                g.DrawLine(pen, r.X + r.Width / 2f + 3, r.Y + 3, r.Right - 2, r.Y + r.Height / 2f);
            }
        }

        private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            if (IsDisposed)
                return;
            BackColor = SupeyTheme.SurfaceHeader;
            Invalidate();
        }

        private sealed class Column
        {
            public readonly string Label;
            public readonly string Value;
            public readonly Color ValueColor;
            public readonly string Tip;

            public Column(string label, string value, Color valueColor, string tip)
            {
                Label = label ?? "";
                Value = value ?? "";
                ValueColor = valueColor;
                Tip = tip ?? "";
            }
        }
    }
}
