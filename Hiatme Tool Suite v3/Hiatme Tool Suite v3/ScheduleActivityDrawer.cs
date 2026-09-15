using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Slide-in activity log for one schedule day. Each row is a ledger line — Consolas
    /// clock, dispatcher, verb tinted by kind, detail — so it reads like <c>tail -f</c>
    /// on the day. Filter pills: All / Edits / Saves / Mine.
    /// </summary>
    internal sealed class ScheduleActivityDrawer : Control
    {
        public const int DrawerWidth = 420;
        private const int HeaderH = 36;
        private const int RowH = 22;
        private const int PadX = 12;
        private const int MotionMs = 160;

        private static readonly Font TitleFont = new Font("Segoe UI Semibold", 9.5f);
        private static readonly Font NameFont = new Font("Segoe UI Semibold", 9f);
        private static readonly Font BodyFont = new Font("Segoe UI", 9f);
        private static readonly Font MonoFont = new Font("Consolas", 8.75f);
        private static readonly Font PillFont = new Font("Segoe UI", 8.25f);

        private readonly VScrollBar _scroll = new VScrollBar();
        private readonly Timer _anim = new Timer { Interval = 16 };
        private readonly List<ScheduleActivityEvent> _all = new List<ScheduleActivityEvent>();
        private readonly string[] _filters = { "All", "Edits", "Saves", "Mine" };
        private int _filter;
        private bool _open;
        private double _t; // 0 closed .. 1 open
        private DateTime _animStart;
        private int _hoverRow = -1;
        private string _myClientId = "";
        private long _highlightSeq = -1;

        public string ServiceDate { get; private set; } = "";
        public bool IsOpen => _open;
        public event EventHandler Closed;

        public ScheduleActivityDrawer()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.UserPaint,
                true);
            DoubleBuffered = true;
            Width = DrawerWidth;
            BackColor = SupeyTheme.SurfaceStatusBar;
            Visible = false;
            TabStop = false;

            _scroll.Dock = DockStyle.Right;
            _scroll.Width = 10;
            _scroll.SmallChange = RowH;
            _scroll.ValueChanged += (_, __) => Invalidate();
            Controls.Add(_scroll);

            _anim.Tick += (_, __) => StepAnim();
            MouseWheel += (_, e) =>
            {
                int v = _scroll.Value - Math.Sign(e.Delta) * RowH * 3;
                _scroll.Value = Math.Max(_scroll.Minimum, Math.Min(Math.Max(_scroll.Minimum, _scroll.Maximum - _scroll.LargeChange + 1), v));
            };
        }

        public void SetIdentity(string myClientId) => _myClientId = myClientId ?? "";

        public void SetEvents(string serviceDate, IEnumerable<ScheduleActivityEvent> events, long highlightSeq = -1)
        {
            ServiceDate = serviceDate ?? "";
            _all.Clear();
            if (events != null)
                _all.AddRange(events.OrderByDescending(e => e.Seq));
            _highlightSeq = highlightSeq;
            UpdateScroll();
            if (highlightSeq >= 0)
            {
                var vis = Visible_();
                int idx = vis.FindIndex(e => e.Seq == highlightSeq);
                if (idx >= 0)
                    _scroll.Value = Math.Max(0, Math.Min(_scroll.Maximum, idx * RowH - RowH * 2));
            }
            Invalidate();
        }

        /// <summary>Prepend live events while open.</summary>
        public void Prepend(IEnumerable<ScheduleActivityEvent> events)
        {
            if (events == null) return;
            bool any = false;
            foreach (var ev in events)
            {
                if (!string.Equals(ev.ServiceDate, ServiceDate, StringComparison.Ordinal)) continue;
                if (string.Equals(ev.Audience, "self", StringComparison.Ordinal)) continue;
                if (_all.Any(e => e.Seq == ev.Seq)) continue;
                _all.Insert(0, ev);
                any = true;
            }
            if (any)
            {
                _all.Sort((a, b) => b.Seq.CompareTo(a.Seq));
                UpdateScroll();
                Invalidate();
            }
        }

        public void Open()
        {
            if (_open) return;
            _open = true;
            Visible = true;
            BringToFront();
            _animStart = DateTime.UtcNow;
            _anim.Start();
        }

        public void Close()
        {
            if (!_open) return;
            _open = false;
            _animStart = DateTime.UtcNow;
            _anim.Start();
        }

        public void Toggle()
        {
            if (_open) Close(); else Open();
        }

        /// <summary>Host calls this on resize: the drawer hugs the right edge under the title bar.</summary>
        public void Reposition(Control host, int top, int bottomInset)
        {
            if (host == null) return;
            int h = Math.Max(120, host.ClientSize.Height - top - bottomInset);
            int x = host.ClientSize.Width - (int)Math.Round(DrawerWidth * _t);
            SetBounds(x, top, DrawerWidth, h);
        }

        private void StepAnim()
        {
            double p = Math.Min(1.0, (DateTime.UtcNow - _animStart).TotalMilliseconds / MotionMs);
            double e = p < 0.5 ? 2 * p * p : 1 - Math.Pow(-2 * p + 2, 2) / 2;
            _t = _open ? e : 1 - e;
            if (Parent != null)
                Left = Parent.ClientSize.Width - (int)Math.Round(DrawerWidth * _t);
            if (p >= 1.0)
            {
                _anim.Stop();
                if (!_open)
                {
                    Visible = false;
                    Closed?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        private List<ScheduleActivityEvent> Visible_()
        {
            switch (_filter)
            {
                case 1: return _all.Where(e => e.IsEdit).ToList();
                case 2: return _all.Where(e => e.Verb == "saved").ToList();
                case 3: return _all.Where(e => string.Equals(e.ClientId, _myClientId, StringComparison.Ordinal)).ToList();
                default: return _all;
            }
        }

        private void UpdateScroll()
        {
            int rows = Visible_().Count;
            int content = rows * RowH + 8;
            int view = Math.Max(1, Height - HeaderH);
            _scroll.Minimum = 0;
            _scroll.Maximum = Math.Max(0, content);
            _scroll.LargeChange = view;
            _scroll.Visible = content > view;
            if (_scroll.Value > Math.Max(0, content - view))
                _scroll.Value = Math.Max(0, content - view);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            UpdateScroll();
        }

        private Color VerbColor(string verb)
        {
            switch (verb)
            {
                case "saved": return SupeyTheme.SuccessText;
                case "opened":
                case "closed": return SupeyTheme.TextMuted;
                case "reroute":
                case "cancel": return SupeyTheme.WarnText;
                case "rejected": return SupeyTheme.ErrorText;
                default: return SupeyTheme.AccentPrimary;
            }
        }

        private static string VerbWord(string verb)
        {
            switch (verb)
            {
                case "paste": return "pasted";
                case "blank_row": return "spacing";
                case "note": return "note";
                case "suggest": return "suggest";
                default: return verb ?? "";
            }
        }

        private static string DetailText(ScheduleActivityEvent ev)
        {
            var d = ev.Detail;
            if (d == null) return ev.Text ?? "";
            string tripHead = "";
            if (d.Trips != null && d.Trips.Count > 0)
            {
                var t0 = d.Trips[0];
                tripHead = string.Join(" ", new[] { t0.Trip, t0.Client }.Where(s => !string.IsNullOrWhiteSpace(s)));
                if (d.EffectiveCount > 1) tripHead += " +" + (d.EffectiveCount - 1);
            }
            else if (d.EffectiveCount > 1)
                tripHead = d.EffectiveCount + " trips";

            string tabs = "";
            if (!string.IsNullOrEmpty(d.FromTab) && !string.IsNullOrEmpty(d.ToTab) && d.FromTab != d.ToTab)
                tabs = d.FromTab + " \u2192 " + d.ToTab;
            else if (!string.IsNullOrEmpty(d.ToTab)) tabs = "\u2192 " + d.ToTab;
            else if (!string.IsNullOrEmpty(d.Tab)) tabs = d.Tab;
            else if (!string.IsNullOrEmpty(d.FromTab)) tabs = "from " + d.FromTab;

            switch (ev.Verb)
            {
                case "saved": return "rev " + (d.Revision ?? 0);
                case "opened":
                case "closed": return ScheduleActivityFormat.ShortDate(ev.ServiceDate);
                case "undo":
                case "redo": return d.Label ?? "";
                case "reroute": return string.IsNullOrEmpty(tripHead) ? "\u2192 Reroutes" : tripHead + " \u2192 Reroutes";
                case "cancel": return string.IsNullOrEmpty(tripHead) ? "\u2192 Cancels" : tripHead + " \u2192 Cancels";
            }
            var parts = new[] { tripHead, tabs }.Where(s => !string.IsNullOrEmpty(s));
            string s2 = string.Join(" \u00b7 ", parts);
            return string.IsNullOrEmpty(s2) ? (ev.Text ?? "") : s2;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            var fmt = StringFormat.GenericTypographic;

            // Left accent edge + header.
            using (var pen = new Pen(SupeyTheme.AccentPrimary, 2f))
                g.DrawLine(pen, 1f, 0, 1f, Height);
            using (var b = new SolidBrush(SupeyTheme.SurfaceHeader))
                g.FillRectangle(b, 2, 0, Width - 2, HeaderH);
            using (var pen = new Pen(SupeyTheme.Divider))
                g.DrawLine(pen, 2, HeaderH - 0.5f, Width, HeaderH - 0.5f);

            string title = "Activity \u00b7 " + ScheduleActivityFormat.ShortDate(ServiceDate);
            using (var b = new SolidBrush(SupeyTheme.TextPrimary))
                g.DrawString(title, TitleFont, b, PadX, (HeaderH - TitleFont.Height) / 2f, fmt);

            // Filter pills, right-aligned; the close glyph after them.
            float x = Width - PadX - 14;
            using (var b = new SolidBrush(SupeyTheme.TextMuted))
                g.DrawString("\u2715", PillFont, b, x + 2, (HeaderH - PillFont.Height) / 2f, fmt);
            _closeRect = new RectangleF(x - 2, 0, 20, HeaderH);
            x -= 10;
            _pillRects.Clear();
            for (int i = _filters.Length - 1; i >= 0; i--)
            {
                var sz = g.MeasureString(_filters[i], PillFont, PointF.Empty, fmt);
                float w = sz.Width + 16;
                x -= w;
                var r = new RectangleF(x, (HeaderH - 18) / 2f, w, 18);
                _pillRects.Insert(0, r);
                Color edge = i == _filter ? SupeyTheme.AccentPrimary : SupeyTheme.Divider;
                Color fg = i == _filter ? SupeyTheme.AccentPrimary : SupeyTheme.TextSecondary;
                using (var path = Rounded(r, 9))
                using (var pen = new Pen(edge))
                    g.DrawPath(pen, path);
                using (var b = new SolidBrush(fg))
                    g.DrawString(_filters[i], PillFont, b, r.X + 8, r.Y + (18 - sz.Height) / 2f, fmt);
                x -= 6;
            }

            // Rows.
            var rows = Visible_();
            int y0 = HeaderH + 4 - _scroll.Value;
            int listRight = Width - (_scroll.Visible ? _scroll.Width : 0) - 6;
            float colTime = PadX + 4;
            float colWho = colTime + 60;
            float colVerb = colWho + 62;
            float colDetail = colVerb + 66;
            var clip = new Rectangle(0, HeaderH, Width, Height - HeaderH);
            g.SetClip(clip);
            if (rows.Count == 0)
            {
                using (var b = new SolidBrush(SupeyTheme.TextMuted))
                    g.DrawString("Nothing yet today.", BodyFont, b, colTime, y0 + 6, fmt);
            }
            for (int i = 0; i < rows.Count; i++)
            {
                int y = y0 + i * RowH;
                if (y + RowH < HeaderH || y > Height) continue;
                var ev = rows[i];
                if (i == _hoverRow || ev.Seq == _highlightSeq)
                    using (var b = new SolidBrush(ev.Seq == _highlightSeq ? SupeyTheme.SurfaceElevated : SupeyTheme.Surface))
                        g.FillRectangle(b, 2, y, listRight - 2, RowH);

                string clock = ev.LocalTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
                using (var b = new SolidBrush(SupeyTheme.TextMuted))
                    g.DrawString(clock, MonoFont, b, colTime, y + 4, fmt);

                string who = ev.Verb == "rejected" ? "you" : (ev.Dispatcher ?? "");
                using (var b = new SolidBrush(SupeyTheme.TextPrimary))
                    g.DrawString(Fit(g, who, NameFont, colVerb - colWho - 6, fmt), NameFont, b, colWho, y + 3, fmt);

                using (var b = new SolidBrush(VerbColor(ev.Verb)))
                    g.DrawString(VerbWord(ev.Verb), MonoFont, b, colVerb, y + 4, fmt);

                string detail = DetailText(ev);
                using (var b = new SolidBrush(SupeyTheme.TextSecondary))
                    g.DrawString(Fit(g, detail, BodyFont, listRight - colDetail - 4, fmt), BodyFont, b, colDetail, y + 3, fmt);

                if (ev.IsEdit && ev.Saved.HasValue)
                {
                    var dot = new RectangleF(listRight - 12, y + (RowH - 6) / 2f, 6, 6);
                    if (ev.Saved.Value)
                        using (var b = new SolidBrush(SupeyTheme.SuccessText)) g.FillEllipse(b, dot);
                    else
                        using (var pen = new Pen(SupeyTheme.WarnText, 1.2f)) g.DrawEllipse(pen, dot);
                }
            }
            g.ResetClip();
        }

        private RectangleF _closeRect;
        private readonly List<RectangleF> _pillRects = new List<RectangleF>();

        private static string Fit(Graphics g, string s, Font f, float maxW, StringFormat fmt)
        {
            if (string.IsNullOrEmpty(s) || maxW <= 8) return "";
            if (g.MeasureString(s, f, PointF.Empty, fmt).Width <= maxW) return s;
            for (int n = s.Length - 1; n > 0; n--)
            {
                string t = s.Substring(0, n) + "\u2026";
                if (g.MeasureString(t, f, PointF.Empty, fmt).Width <= maxW) return t;
            }
            return "\u2026";
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

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int row = e.Y < HeaderH ? -1 : (e.Y - (HeaderH + 4 - _scroll.Value)) / RowH;
            if (row != _hoverRow)
            {
                _hoverRow = row;
                Invalidate();
            }
            Cursor = e.Y < HeaderH ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hoverRow = -1;
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            if (_closeRect.Contains(e.Location)) { Close(); return; }
            for (int i = 0; i < _pillRects.Count; i++)
            {
                if (_pillRects[i].Contains(e.Location))
                {
                    _filter = i;
                    UpdateScroll();
                    Invalidate();
                    return;
                }
            }
        }
    }

    internal static class ScheduleActivityFormat
    {
        /// <summary>"2026-09-17" → "Sep 17".</summary>
        public static string ShortDate(string iso)
        {
            if (DateTime.TryParseExact((iso ?? "").Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var d))
                return d.ToString("MMM d", CultureInfo.InvariantCulture);
            return iso ?? "";
        }
    }
}
