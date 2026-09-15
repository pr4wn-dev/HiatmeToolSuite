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
    /// Owner-drawn transcript for the dock: teammates, me, the AI and local notes each
    /// get their own look, consecutive lines from one sender group under a single name,
    /// and every wrap is measured once per width. Thin theme-colored scrollbar, wheel
    /// works wherever the pointer is. Paints entirely from <see cref="SupeyTheme"/>.
    /// </summary>
    internal sealed class ChatTranscriptView : Control
    {
        private const int PadX = 10;
        private const int GroupGap = 12;
        private const int LineGap = 3;
        private const int HeaderH = 18;
        private const int TickW = 2;
        private const int BarW = 6;
        private const int BarPad = 3;
        private const int DayBreakH = 22;
        private static readonly TimeSpan GroupWindow = TimeSpan.FromMinutes(3);

        private static readonly Font TimeFont = new Font("Consolas", 8.25f);
        private static readonly Font ChipFont = new Font("Consolas", 8f);

        private sealed class Row
        {
            public ChatMessage Msg;
            public bool ShowHeader;
            public bool DayBreak;
            public int Top;
            public int Height;
            public int BodyH;
            public Rectangle BodyRect; // relative to row top
        }

        private readonly List<Row> _rows = new List<Row>();
        private readonly HashSet<long> _seen = new HashSet<long>();
        private int _layoutWidth = -1;
        private int _contentH;
        private int _scroll;
        private bool _stickBottom = true;
        private bool _barHot;
        private bool _barDrag;
        private int _barDragOffset;
        private Row _hitRow;
        private ContextMenuStrip _menu;

        public string EmptyHint { get; set; } = "";
        public int Count => _rows.Count;

        public ChatTranscriptView()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.UserPaint
                | ControlStyles.Selectable,
                true);
            DoubleBuffered = true;
            BackColor = SupeyTheme.SurfaceBase;
            TabStop = false;
            WheelRouter.Register(this);

            _menu = new ContextMenuStrip();
            var copy = new ToolStripMenuItem("Copy message");
            copy.Click += (_, __) =>
            {
                var m = _hitRow?.Msg;
                if (m == null || string.IsNullOrEmpty(m.Text)) return;
                try { Clipboard.SetText(m.Text); } catch { }
            };
            _menu.Items.Add(copy);
        }

        // ---------------------------------------------------------------- data

        public bool Contains(long seq) => seq > 0 && _seen.Contains(seq);

        public void Add(ChatMessage m)
        {
            using (UiStallWatch.Measure(UiScope.ChatViewAdd))
                AddCore(m);
        }

        private void AddCore(ChatMessage m)
        {
            if (m == null) return;
            if (m.Seq > 0 && !_seen.Add(m.Seq)) return;
            var prev = _rows.Count > 0 ? _rows[_rows.Count - 1] : null;
            var row = new Row { Msg = m };
            row.DayBreak = prev == null || prev.Msg.Time.Date != m.Time.Date;
            row.ShowHeader = row.DayBreak || !Groups(prev?.Msg, m);
            _rows.Add(row);
            if (_layoutWidth > 0)
            {
                using (var g = CreateGraphics())
                    Measure(g, row, _layoutWidth);
                row.Top = _contentH;
                _contentH += row.Height;
            }
            AfterContentChange(m.Kind == ChatMessageKind.Me);
        }

        public void AddRange(IEnumerable<ChatMessage> msgs)
        {
            if (msgs == null) return;
            foreach (var m in msgs.OrderBy(x => x.Seq == 0 ? long.MaxValue : x.Seq).ThenBy(x => x.Time))
            {
                if (m == null) continue;
                if (m.Seq > 0 && !_seen.Add(m.Seq)) continue;
                var prev = _rows.Count > 0 ? _rows[_rows.Count - 1] : null;
                var row = new Row { Msg = m };
                row.DayBreak = prev == null || prev.Msg.Time.Date != m.Time.Date;
                row.ShowHeader = row.DayBreak || !Groups(prev?.Msg, m);
                _rows.Add(row);
            }
            _layoutWidth = -1;
            AfterContentChange(false);
        }

        /// <summary>A pending local echo got its server seq back (or failed).</summary>
        public void Resolve(ChatMessage local, ChatMessage server)
        {
            if (local == null) return;
            local.Pending = false;
            if (server == null) { local.Failed = true; }
            else
            {
                local.Seq = server.Seq;
                local.Time = server.Time;
                if (local.Seq > 0) _seen.Add(local.Seq);
            }
            Invalidate();
        }

        public void Clear()
        {
            _rows.Clear();
            _seen.Clear();
            _contentH = 0;
            _scroll = 0;
            _stickBottom = true;
            Invalidate();
        }

        public void ScrollToBottom()
        {
            _stickBottom = true;
            _scroll = Math.Max(0, _contentH - ClientSize.Height);
            Invalidate();
        }

        public void ScrollToSeq(long seq)
        {
            EnsureLayout();
            var row = _rows.FirstOrDefault(r => r.Msg.Seq == seq);
            if (row == null) { ScrollToBottom(); return; }
            _stickBottom = false;
            _scroll = Math.Max(0, Math.Min(row.Top - 6, Math.Max(0, _contentH - ClientSize.Height)));
            Invalidate();
        }

        private static bool Groups(ChatMessage prev, ChatMessage cur)
        {
            if (prev == null || cur == null) return false;
            if (cur.Kind == ChatMessageKind.AiThinking || cur.Kind == ChatMessageKind.System || cur.Kind == ChatMessageKind.Error)
                return true; // notes tuck under whatever came before
            if (prev.Kind == ChatMessageKind.AiThinking) return cur.Kind == ChatMessageKind.Ai;
            if (prev.Kind != cur.Kind) return false;
            if (!string.Equals(prev.Sender, cur.Sender, StringComparison.Ordinal)) return false;
            if (cur.Kind == ChatMessageKind.Team && !string.Equals(prev.ClientId, cur.ClientId, StringComparison.Ordinal)) return false;
            return cur.Time - prev.Time <= GroupWindow;
        }

        private void AfterContentChange(bool force)
        {
            if (force || _stickBottom)
                _scroll = Math.Max(0, _contentH - ClientSize.Height);
            if (force) _stickBottom = true;
            Invalidate();
        }

        // -------------------------------------------------------------- layout

        private int TextWidth => Math.Max(40, ClientSize.Width - PadX * 2 - BarW - BarPad * 2 - TickW - 6);

        private void EnsureLayout()
        {
            int w = TextWidth;
            if (_layoutWidth == w) return;
            _layoutWidth = w;
            using (var g = CreateGraphics())
            {
                int y = 0;
                foreach (var r in _rows)
                {
                    Measure(g, r, w);
                    r.Top = y;
                    y += r.Height;
                }
                _contentH = y;
            }
            if (_stickBottom) _scroll = Math.Max(0, _contentH - ClientSize.Height);
            else _scroll = Math.Max(0, Math.Min(_scroll, _contentH - ClientSize.Height));
        }

        private static Font BodyFontFor(ChatMessage m)
        {
            switch (m.Kind)
            {
                case ChatMessageKind.AiThinking:
                case ChatMessageKind.System:
                case ChatMessageKind.Error:
                    return SupeyTheme.CaptionFont;
                default:
                    return SupeyTheme.BodyFont;
            }
        }

        private static void Measure(Graphics g, Row r, int w)
        {
            var m = r.Msg;
            int y = 0;
            if (r.DayBreak) y += DayBreakH;
            if (r.ShowHeader) y += GroupGap + HeaderH;
            else y += LineGap;
            string text = string.IsNullOrEmpty(m.Text) ? " " : m.Text;
            var sz = TextRenderer.MeasureText(g, text, BodyFontFor(m), new Size(w, int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl);
            r.BodyH = Math.Max(14, sz.Height);
            r.BodyRect = new Rectangle(0, y, w, r.BodyH);
            r.Height = y + r.BodyH;
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            _layoutWidth = -1;
            Invalidate();
        }

        // --------------------------------------------------------------- paint

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (var b = new SolidBrush(SupeyTheme.SurfaceBase))
                e.Graphics.FillRectangle(b, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (UiStallWatch.Measure(UiScope.ChatViewPaint))
                PaintCore(e);
        }

        private void PaintCore(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            EnsureLayout();

            if (_rows.Count == 0)
            {
                if (!string.IsNullOrEmpty(EmptyHint))
                {
                    var r = new Rectangle(24, 16, Math.Max(20, ClientSize.Width - 48), Math.Max(20, ClientSize.Height - 32));
                    TextRenderer.DrawText(g, EmptyHint, SupeyTheme.BodyFont, r, SupeyTheme.TextMuted,
                        TextFormatFlags.WordBreak | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                }
                return;
            }

            int x0 = PadX + TickW + 6;
            int viewTop = _scroll;
            int viewBottom = _scroll + ClientSize.Height;
            const TextFormatFlags bodyFlags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl;

            foreach (var r in _rows)
            {
                if (r.Top + r.Height < viewTop) continue;
                if (r.Top > viewBottom) break;
                var m = r.Msg;
                int y = r.Top - _scroll;
                int cy = y;

                if (r.DayBreak)
                {
                    string day = m.Time.Date == DateTime.Today ? "Today"
                        : m.Time.Date == DateTime.Today.AddDays(-1) ? "Yesterday"
                        : m.Time.ToString("ddd MMM d", CultureInfo.InvariantCulture);
                    var dsz = TextRenderer.MeasureText(g, day, TimeFont);
                    int mid = cy + DayBreakH / 2 + 2;
                    int dx = (ClientSize.Width - BarW - BarPad * 2 - dsz.Width) / 2;
                    using (var pen = new Pen(SupeyTheme.Divider))
                    {
                        g.DrawLine(pen, PadX, mid, dx - 8, mid);
                        g.DrawLine(pen, dx + dsz.Width + 8, mid, ClientSize.Width - BarW - BarPad * 2 - PadX, mid);
                    }
                    TextRenderer.DrawText(g, day, TimeFont, new Point(dx, mid - dsz.Height / 2), SupeyTheme.TextMuted, TextFormatFlags.NoPrefix);
                    cy += DayBreakH;
                }

                Color nameColor, bodyColor, tickColor;
                Palette(m, out nameColor, out bodyColor, out tickColor);

                if (r.ShowHeader)
                {
                    cy += GroupGap;
                    string name = m.Kind == ChatMessageKind.Me ? "You" : (string.IsNullOrEmpty(m.Sender) ? "Someone" : m.Sender);
                    TextRenderer.DrawText(g, name, SupeyTheme.SubHeaderFont, new Point(x0 - 1, cy), nameColor, TextFormatFlags.NoPrefix);
                    int nx = x0 + TextRenderer.MeasureText(g, name, SupeyTheme.SubHeaderFont).Width;

                    if (m.MentionsAi && m.Kind != ChatMessageKind.Ai)
                    {
                        string chip = "\u2192 AI";
                        var csz = TextRenderer.MeasureText(g, chip, ChipFont);
                        TextRenderer.DrawText(g, chip, ChipFont, new Point(nx + 4, cy + 2), SupeyTheme.TextMuted, TextFormatFlags.NoPrefix);
                        nx += csz.Width + 4;
                    }

                    string time = m.Time.ToString("HH:mm", CultureInfo.InvariantCulture);
                    var tsz = TextRenderer.MeasureText(g, time, TimeFont);
                    int tx = ClientSize.Width - BarW - BarPad * 2 - PadX - tsz.Width;
                    TextRenderer.DrawText(g, time, TimeFont, new Point(tx, cy + 2), SupeyTheme.TextMuted, TextFormatFlags.NoPrefix);
                    cy += HeaderH;
                }
                else
                {
                    cy += LineGap;
                }

                // Left tick: AI gets the accent, errors red, everything else nothing.
                if (tickColor != Color.Empty)
                {
                    using (var b = new SolidBrush(tickColor))
                        g.FillRectangle(b, PadX, cy + 2, TickW, Math.Max(6, r.BodyH - 4));
                }

                var body = new Rectangle(x0, cy, r.BodyRect.Width, r.BodyH);
                string text = m.Kind == ChatMessageKind.AiThinking ? "thinking \u00b7 " + m.Text : m.Text;
                TextRenderer.DrawText(g, text, BodyFontFor(m), body, bodyColor, bodyFlags);

                if (m.Pending || m.Failed)
                {
                    string tag = m.Failed ? "not sent" : "\u2026";
                    var ssz = TextRenderer.MeasureText(g, tag, ChipFont);
                    int sx = ClientSize.Width - BarW - BarPad * 2 - PadX - ssz.Width;
                    TextRenderer.DrawText(g, tag, ChipFont, new Point(sx, cy + r.BodyH - ssz.Height),
                        m.Failed ? SupeyTheme.ErrorText : SupeyTheme.TextMuted, TextFormatFlags.NoPrefix);
                }
            }

            DrawBar(g);
        }

        private static void Palette(ChatMessage m, out Color name, out Color body, out Color tick)
        {
            tick = Color.Empty;
            switch (m.Kind)
            {
                case ChatMessageKind.Me:
                    name = SupeyTheme.TextMuted;
                    body = SupeyTheme.TextSecondary;
                    break;
                case ChatMessageKind.Ai:
                    name = SupeyTheme.AccentPrimary;
                    body = SupeyTheme.TextPrimary;
                    tick = SupeyTheme.AccentPrimary;
                    break;
                case ChatMessageKind.AiThinking:
                    name = SupeyTheme.TextMuted;
                    body = SupeyTheme.TextMuted;
                    tick = SupeyTheme.Divider;
                    break;
                case ChatMessageKind.System:
                    name = SupeyTheme.TextMuted;
                    body = SupeyTheme.TextMuted;
                    break;
                case ChatMessageKind.Error:
                    name = SupeyTheme.ErrorText;
                    body = SupeyTheme.ErrorText;
                    tick = SupeyTheme.ErrorText;
                    break;
                default: // Team
                    name = SupeyTheme.TextPrimary;
                    body = SupeyTheme.TextPrimary;
                    break;
            }
        }

        // ----------------------------------------------------------- scrollbar

        private int MaxScroll => Math.Max(0, _contentH - ClientSize.Height);

        private Rectangle BarTrack => new Rectangle(ClientSize.Width - BarW - BarPad, BarPad, BarW, Math.Max(0, ClientSize.Height - BarPad * 2));

        private Rectangle BarThumb
        {
            get
            {
                if (_contentH <= ClientSize.Height || ClientSize.Height <= 0) return Rectangle.Empty;
                var t = BarTrack;
                int th = Math.Max(24, (int)((long)t.Height * ClientSize.Height / Math.Max(1, _contentH)));
                int range = t.Height - th;
                int ty = t.Y + (MaxScroll == 0 ? 0 : (int)((long)range * _scroll / MaxScroll));
                return new Rectangle(t.X, ty, t.Width, th);
            }
        }

        private void DrawBar(Graphics g)
        {
            var thumb = BarThumb;
            if (thumb.IsEmpty) return;
            var state = g.Save();
            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Color c = _barHot || _barDrag ? SupeyTheme.AccentPrimary : SupeyTheme.BorderSubtle;
                using (var b = new SolidBrush(c))
                using (var path = Rounded(thumb, 3))
                    g.FillPath(b, path);
            }
            finally { g.Restore(state); }
        }

        private static GraphicsPath Rounded(Rectangle r, int rad)
        {
            var p = new GraphicsPath();
            int d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        private void SetScroll(int v)
        {
            int nv = Math.Max(0, Math.Min(MaxScroll, v));
            if (nv == _scroll) return;
            _scroll = nv;
            _stickBottom = nv >= MaxScroll;
            Invalidate();
        }

        public void WheelBy(int delta)
        {
            EnsureLayout();
            SetScroll(_scroll - delta / 120 * 48);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            WheelBy(e.Delta);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            var thumb = BarThumb;
            if (e.Button == MouseButtons.Left && !thumb.IsEmpty && BarTrack.Inflate2(6).Contains(e.Location))
            {
                if (!thumb.Contains(e.Location))
                {
                    // Jump so the thumb centers on the click, then drag from there.
                    var t = BarTrack;
                    int range = Math.Max(1, t.Height - thumb.Height);
                    SetScroll((int)((long)(e.Y - t.Y - thumb.Height / 2) * MaxScroll / range));
                    thumb = BarThumb;
                }
                _barDrag = true;
                _barDragOffset = e.Y - thumb.Y;
                Capture = true;
                Invalidate();
                return;
            }
            if (e.Button == MouseButtons.Right)
            {
                _hitRow = RowAt(e.Y);
                if (_hitRow != null) _menu.Show(this, e.Location);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_barDrag)
            {
                var t = BarTrack;
                var thumb = BarThumb;
                int range = Math.Max(1, t.Height - thumb.Height);
                SetScroll((int)((long)(e.Y - _barDragOffset - t.Y) * MaxScroll / range));
                return;
            }
            bool hot = !BarThumb.IsEmpty && BarTrack.Inflate2(6).Contains(e.Location);
            if (hot != _barHot)
            {
                _barHot = hot;
                Invalidate(BarTrack.Inflate2(2));
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_barDrag)
            {
                _barDrag = false;
                Capture = false;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_barHot && !_barDrag)
            {
                _barHot = false;
                Invalidate(BarTrack.Inflate2(2));
            }
        }

        private Row RowAt(int clientY)
        {
            int y = clientY + _scroll;
            foreach (var r in _rows)
                if (y >= r.Top && y < r.Top + r.Height) return r;
            return null;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                WheelRouter.Unregister(this);
                _menu?.Dispose();
            }
            base.Dispose(disposing);
        }

        // -------------------------------------------------------- wheel router

        /// <summary>Send WM_MOUSEWHEEL to the transcript under the pointer, not the focused prompt box.</summary>
        private sealed class WheelRouter : IMessageFilter
        {
            private const int WM_MOUSEWHEEL = 0x020A;
            private static WheelRouter _instance;
            private static readonly List<ChatTranscriptView> Views = new List<ChatTranscriptView>();

            public static void Register(ChatTranscriptView v)
            {
                if (_instance == null)
                {
                    _instance = new WheelRouter();
                    Application.AddMessageFilter(_instance);
                }
                if (!Views.Contains(v)) Views.Add(v);
            }

            public static void Unregister(ChatTranscriptView v) => Views.Remove(v);

            public bool PreFilterMessage(ref Message m)
            {
                if (m.Msg != WM_MOUSEWHEEL || Views.Count == 0) return false;
                var pos = Control.MousePosition;
                foreach (var v in Views)
                {
                    if (v == null || v.IsDisposed || !v.Visible || !v.IsHandleCreated) continue;
                    if (!v.RectangleToScreen(v.ClientRectangle).Contains(pos)) continue;
                    if (v.Focused) return false; // normal path
                    int delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
                    v.WheelBy(delta);
                    return true;
                }
                return false;
            }
        }
    }

    internal static class ChatRectExt
    {
        public static Rectangle Inflate2(this Rectangle r, int by)
        {
            r.Inflate(by, by);
            return r;
        }
    }
}
