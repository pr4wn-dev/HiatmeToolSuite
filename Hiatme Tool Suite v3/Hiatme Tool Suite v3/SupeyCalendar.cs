using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Fully owner-drawn month calendar in the Supey theme — the themed replacement for the native
    /// Win32 <c>MonthCalendar</c> popup (which can't be dark-themed past a point). A header with
    /// prev/next month chevrons, a muted weekday row, and a 6x7 day grid: today gets an accent ring,
    /// the selected day an accent fill, hover a soft surface circle. Raises <see cref="DateSelected"/>.
    /// </summary>
    public sealed class SupeyCalendar : Control
    {
        private const int Pad = 8;
        private const int HeaderH = 40;
        private const int WeekRowH = 24;
        private const int Cell = 32;

        private static readonly string[] WeekDays = { "Su", "Mo", "Tu", "We", "Th", "Fr", "Sa" };

        private DateTime _display;     // first day of the month being shown
        private DateTime _selected;
        private int _hoverCell = -1;
        private Rectangle _prevRect, _nextRect;

        public event Action<DateTime> DateSelected;

        public SupeyCalendar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.UserPaint
                   | ControlStyles.ResizeRedraw, true);
            _selected = DateTime.Today;
            _display = new DateTime(_selected.Year, _selected.Month, 1);
            Width = Pad * 2 + 7 * Cell;
            Height = HeaderH + WeekRowH + 6 * Cell + Pad;
            BackColor = SupeyTheme.Surface;
            Font = SupeyTheme.BodyFont;

            SupeyThemeManager.ThemeChanged += (s, e) =>
            {
                if (IsDisposed) return;
                try { BackColor = SupeyTheme.Surface; Invalidate(); } catch { }
            };
        }

        public void SetValue(DateTime value)
        {
            _selected = value.Date;
            _display = new DateTime(_selected.Year, _selected.Month, 1);
            Invalidate();
        }

        private DateTime GridStart()
        {
            int offset = (int)_display.DayOfWeek; // Sunday = 0
            return _display.AddDays(-offset);
        }

        private Rectangle CellRect(int index)
        {
            int row = index / 7, col = index % 7;
            return new Rectangle(Pad + col * Cell, HeaderH + WeekRowH + row * Cell, Cell, Cell);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int hov = -1;
            for (int i = 0; i < 42; i++)
                if (CellRect(i).Contains(e.Location)) { hov = i; break; }
            bool overNav = _prevRect.Contains(e.Location) || _nextRect.Contains(e.Location);
            Cursor = (hov >= 0 || overNav) ? Cursors.Hand : Cursors.Default;
            if (hov != _hoverCell) { _hoverCell = hov; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverCell != -1) { _hoverCell = -1; Invalidate(); }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (_prevRect.Contains(e.Location)) { _display = _display.AddMonths(-1); Invalidate(); return; }
            if (_nextRect.Contains(e.Location)) { _display = _display.AddMonths(1); Invalidate(); return; }

            DateTime start = GridStart();
            for (int i = 0; i < 42; i++)
            {
                if (CellRect(i).Contains(e.Location))
                {
                    DateTime d = start.AddDays(i);
                    _selected = d;
                    _display = new DateTime(d.Year, d.Month, 1);
                    Invalidate();
                    DateSelected?.Invoke(d);
                    return;
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var bg = new SolidBrush(SupeyTheme.Surface))
                g.FillRectangle(bg, ClientRectangle);

            DrawHeader(g);
            DrawWeekRow(g);
            DrawDays(g);
        }

        private void DrawHeader(Graphics g)
        {
            int chev = 24;
            _prevRect = new Rectangle(Pad, (HeaderH - chev) / 2, chev, chev);
            _nextRect = new Rectangle(Width - Pad - chev, (HeaderH - chev) / 2, chev, chev);

            DrawChevron(g, _prevRect, true);
            DrawChevron(g, _nextRect, false);

            var title = _display.ToString("MMMM yyyy");
            var rect = new Rectangle(_prevRect.Right, 0, _nextRect.Left - _prevRect.Right, HeaderH);
            TextRenderer.DrawText(g, title, SupeyTheme.SubHeaderFont, rect, SupeyTheme.TextPrimary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        private void DrawChevron(Graphics g, Rectangle r, bool left)
        {
            bool hot = r.Contains(PointToClient(Cursor.Position));
            Color c = hot ? SupeyTheme.AccentPrimary : SupeyTheme.TextSecondary;
            int cx = r.Left + r.Width / 2, cy = r.Top + r.Height / 2;
            using (var pen = new Pen(c, 2f))
            {
                if (left)
                {
                    g.DrawLine(pen, cx + 3, cy - 5, cx - 3, cy);
                    g.DrawLine(pen, cx - 3, cy, cx + 3, cy + 5);
                }
                else
                {
                    g.DrawLine(pen, cx - 3, cy - 5, cx + 3, cy);
                    g.DrawLine(pen, cx + 3, cy, cx - 3, cy + 5);
                }
            }
        }

        private void DrawWeekRow(Graphics g)
        {
            for (int col = 0; col < 7; col++)
            {
                var rect = new Rectangle(Pad + col * Cell, HeaderH, Cell, WeekRowH);
                TextRenderer.DrawText(g, WeekDays[col], SupeyTheme.CaptionFont, rect, SupeyTheme.TextMuted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
        }

        private void DrawDays(Graphics g)
        {
            DateTime start = GridStart();
            for (int i = 0; i < 42; i++)
            {
                DateTime d = start.AddDays(i);
                Rectangle rect = CellRect(i);
                bool inMonth = d.Month == _display.Month;
                bool isSelected = d.Date == _selected.Date;
                bool isToday = d.Date == DateTime.Today;
                bool hot = i == _hoverCell;

                int dia = Cell - 6;
                var circle = new Rectangle(rect.Left + (Cell - dia) / 2, rect.Top + (Cell - dia) / 2, dia, dia);

                if (isSelected)
                {
                    using (var b = new SolidBrush(SupeyTheme.AccentPrimary))
                        g.FillEllipse(b, circle);
                }
                else if (hot)
                {
                    using (var b = new SolidBrush(SupeyTheme.SurfaceElevated))
                        g.FillEllipse(b, circle);
                }
                if (isToday && !isSelected)
                {
                    using (var pen = new Pen(SupeyTheme.AccentPrimary, 1.5f))
                        g.DrawEllipse(pen, circle);
                }

                Color fg = isSelected ? SupeyTheme.OnAccentText
                         : inMonth ? SupeyTheme.TextPrimary
                         : SupeyTheme.TextMuted;
                TextRenderer.DrawText(g, d.Day.ToString(), SupeyTheme.BodyFont, rect, fg,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
        }
    }

    /// <summary>
    /// Borderless dropdown that hosts a <see cref="SupeyCalendar"/> below a date picker. Uses
    /// <see cref="ToolStripDropDown"/> for free top-level positioning + click-outside auto-close.
    /// </summary>
    public sealed class SupeyCalendarPopup : ToolStripDropDown
    {
        private readonly SupeyCalendar _cal;
        public event Action<DateTime> DateSelected;

        public SupeyCalendarPopup()
        {
            _cal = new SupeyCalendar();
            _cal.DateSelected += d => { DateSelected?.Invoke(d); Close(ToolStripDropDownCloseReason.ItemClicked); };

            var host = new ToolStripControlHost(_cal)
            {
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                AutoSize = false,
                Size = _cal.Size,
            };

            AutoSize = false;
            Padding = new Padding(1);
            Margin = Padding.Empty;
            DropShadowEnabled = true;
            BackColor = SupeyTheme.BorderSubtle; // 1px frame around the calendar
            Items.Add(host);
            Size = new Size(_cal.Width + 2, _cal.Height + 2);
        }

        public void ShowBelow(Control anchor, DateTime value)
        {
            _cal.SetValue(value);
            BackColor = SupeyTheme.BorderSubtle;
            Show(anchor, new Point(0, anchor.Height));
        }
    }
}
