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
    internal enum ScheduleToastKind
    {
        Edit,
        Saved,
        Behind,
        Rejected,
        Presence,
        Muted,
        /// <summary>A teammate (or the AI) said something while the dock was hidden.</summary>
        Chat,
        /// <summary>The panel is asking the desk to confirm something for the playbook.</summary>
        Question,
    }

    /// <summary>One inline piece of a toast sentence.</summary>
    internal struct ScheduleToastRun
    {
        public enum Style { Body, Strong, Mono, Chip, ChipAccent, ChipWarn, Arrow }

        public string Text;
        public Style Kind;

        public ScheduleToastRun(string text, Style kind)
        {
            Text = text ?? "";
            Kind = kind;
        }

        public static ScheduleToastRun Body(string t) => new ScheduleToastRun(t, Style.Body);
        public static ScheduleToastRun Strong(string t) => new ScheduleToastRun(t, Style.Strong);
        public static ScheduleToastRun Mono(string t) => new ScheduleToastRun(t, Style.Mono);
        public static ScheduleToastRun Chip(string t) => new ScheduleToastRun(t, Style.Chip);
        public static ScheduleToastRun ChipAccent(string t) => new ScheduleToastRun(t, Style.ChipAccent);
        public static ScheduleToastRun ChipWarn(string t) => new ScheduleToastRun(t, Style.ChipWarn);
        public static ScheduleToastRun Arrow() => new ScheduleToastRun("\u2192", Style.Arrow);
    }

    /// <summary>
    /// HUD-tag toast: top-left and bottom-right corners sheared 10px (the
    /// <c>SupeyCard.KeyedRect</c> geometry), a 2px kind-colored tick riding the sheared
    /// top-left edge, and a 1px fuse along the bottom that drains as the toast ages.
    /// No side stripe, no radius, no shadow. Paints entirely from <see cref="SupeyTheme"/>.
    /// </summary>
    internal sealed class ScheduleActivityToast : Control
    {
        public const int DefaultWidth = 340;
        public const int Cut = 10;
        private const int PadL = 12;
        private const int PadR = 10;
        private const int PadT = 7;
        private const int PadB = 9;
        private const int RowGap = 2;
        private const int ChipPadX = 6;
        private const int ChipH = 15;
        private const int MaxLines = 3;

        private static readonly Font NameFont = new Font("Segoe UI Semibold", 9.75f);
        private static readonly Font BodyFont = new Font("Segoe UI", 9.5f);
        private static readonly Font ChipFont = new Font("Segoe UI", 8.25f);
        private static readonly Font MonoFont = new Font("Consolas", 9.5f);
        private static readonly Font AgeFont = new Font("Consolas", 8.25f);
        private static readonly Font BadgeFont = new Font("Consolas", 8f);
        private static readonly Font ActionFont = new Font("Segoe UI Semibold", 8.25f);

        private readonly List<ScheduleToastRun> _runs = new List<ScheduleToastRun>();
        private DateTime _bornUtc = DateTime.UtcNow;
        private TimeSpan _paused = TimeSpan.Zero;
        private DateTime? _pauseStartUtc;
        private bool _hover;

        public ScheduleToastKind Kind { get; set; } = ScheduleToastKind.Edit;
        public string Who { get; set; } = "";
        public string Verb { get; set; } = "";
        public string ServiceDate { get; set; } = "";
        /// <summary>
        /// This toast is about a schedule other than the one on screen. Off-day edits never reach
        /// a toast at all; the low-volume events that do (a save, someone opening a day) are worth
        /// knowing about but should not read as loud as work on your own schedule.
        /// </summary>
        public bool OffDay { get; set; }
        public string SourceClientId { get; set; } = "";
        public string ToTab { get; set; } = "";
        public double EventTs { get; set; }
        public int Count { get; set; } = 1;
        public bool? Unsaved { get; set; }
        public string ActionText { get; set; }

        /// <summary>
        /// A second choice, drawn to the left of <see cref="ActionText"/>.
        ///
        /// Every other toast offers one way out, so a click anywhere could mean it. A question
        /// has two answers that are not interchangeable, so the words themselves have to be the
        /// targets — see <see cref="RequireActionClick"/>.
        /// </summary>
        public string SecondaryActionText { get; set; }

        /// <summary>
        /// Only a click on an action word counts; clicks elsewhere do nothing.
        ///
        /// Without this, brushing the toast while reaching for the trip list would answer a
        /// question on the dispatcher's behalf and write it to the playbook as a considered
        /// reply. A stray click has to be able to mean nothing.
        /// </summary>
        public bool RequireActionClick { get; set; }

        public object Payload { get; set; }
        /// <summary>Lifetime in ms; 0 = sticky (no fuse, never auto-dismisses).</summary>
        public int LifetimeMs { get; set; } = 8000;
        /// <summary>0..1 dim applied to text/border for toasts further up the stack.</summary>
        public float Dim { get; set; } = 1f;

        public bool Sticky => LifetimeMs <= 0;
        public DateTime BornUtc => _bornUtc;

        public event EventHandler Activated;

        /// <summary>The second action was clicked (Skip, on a question).</summary>
        public event EventHandler SecondaryActivated;

        public ScheduleActivityToast()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.UserPaint,
                true);
            DoubleBuffered = true;
            Width = DefaultWidth;
            Height = 56;
            Cursor = Cursors.Hand;
            BackColor = SupeyTheme.SurfaceElevated;
            TabStop = false;
        }

        public void SetRuns(IEnumerable<ScheduleToastRun> runs)
        {
            using (UiStallWatch.Measure(UiScope.ToastLayoutText))
            {
                _runs.Clear();
                if (runs != null) _runs.AddRange(runs);
                _layoutWidth = Width;
                RecomputeHeight();
            }
            Invalidate();
        }

        public IReadOnlyList<ScheduleToastRun> Runs => _runs;

        /// <summary>Restart the fuse (used when a follow-up event coalesces into this toast).</summary>
        public void Refresh_Lifetime()
        {
            _bornUtc = DateTime.UtcNow;
            _paused = TimeSpan.Zero;
            _pauseStartUtc = _hover ? DateTime.UtcNow : (DateTime?)null;
            Invalidate();
        }

        /// <summary>Elapsed lifetime excluding time spent hovered.</summary>
        public TimeSpan Age
        {
            get
            {
                var paused = _paused;
                if (_pauseStartUtc.HasValue)
                    paused += DateTime.UtcNow - _pauseStartUtc.Value;
                var a = DateTime.UtcNow - _bornUtc - paused;
                return a < TimeSpan.Zero ? TimeSpan.Zero : a;
            }
        }

        public bool Expired => !Sticky && Age.TotalMilliseconds >= LifetimeMs;

        public float FuseRemaining
        {
            get
            {
                if (Sticky) return 1f;
                double f = 1.0 - Age.TotalMilliseconds / Math.Max(1, LifetimeMs);
                return (float)Math.Max(0, Math.Min(1, f));
            }
        }

        public string AgeLabel
        {
            get
            {
                if (EventTs <= 0) return "";
                double s = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0) - EventTs;
                if (s < 3) return "now";
                if (s < 60) return ((int)s).ToString(CultureInfo.InvariantCulture) + "s";
                if (s < 3600) return ((int)(s / 60)).ToString(CultureInfo.InvariantCulture) + "m";
                return ((int)(s / 3600)).ToString(CultureInfo.InvariantCulture) + "h";
            }
        }

        public Color KindColor
        {
            get
            {
                switch (Kind)
                {
                    case ScheduleToastKind.Edit: return SupeyTheme.AccentPrimary;
                    case ScheduleToastKind.Saved: return SupeyTheme.SuccessText;
                    case ScheduleToastKind.Behind: return SupeyTheme.WarnText;
                    case ScheduleToastKind.Rejected: return SupeyTheme.ErrorText;
                    case ScheduleToastKind.Chat: return SupeyTheme.TextLink;
                    case ScheduleToastKind.Question: return SupeyTheme.AccentStripe;
                    default: return SupeyTheme.Divider;
                }
            }
        }

        private bool IsMuted => Kind == ScheduleToastKind.Muted || Kind == ScheduleToastKind.Presence;

        /// <summary>
        /// Dim the wording, but not the kind tick. An off-day save keeps its green tick so it still
        /// reads as a save at a glance; only the sentence recedes.
        /// </summary>
        private bool IsSubdued => IsMuted || OffDay;

        // ------------------------------------------------------------- geometry

        /// <summary>Rect with the top-left and bottom-right corners sheared off flat.</summary>
        internal static GraphicsPath KeyedRect(Rectangle r, int cut)
        {
            var path = new GraphicsPath();
            path.AddLine(r.Left + cut, r.Top, r.Right, r.Top);
            path.AddLine(r.Right, r.Top, r.Right, r.Bottom - cut);
            path.AddLine(r.Right, r.Bottom - cut, r.Right - cut, r.Bottom);
            path.AddLine(r.Right - cut, r.Bottom, r.Left, r.Bottom);
            path.AddLine(r.Left, r.Bottom, r.Left, r.Top + cut);
            path.AddLine(r.Left, r.Top + cut, r.Left + cut, r.Top);
            path.CloseFigure();
            return path;
        }

        private int _layoutWidth = -1;

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            ApplyRegion();
            if (Width != _layoutWidth && IsHandleCreated && _runs.Count > 0)
            {
                _layoutWidth = Width;
                RecomputeHeight();
            }
        }

        private void ApplyRegion()
        {
            if (Width <= 0 || Height <= 0) return;
            try
            {
                using (var p = KeyedRect(new Rectangle(0, 0, Width, Height), Cut))
                    Region = new Region(p);
            }
            catch { }
        }

        // Layout is measured once per SetRuns/resize and cached; OnPaint only draws.
        // Measuring every run on every repaint was enough GDI+ work at 60fps to lag
        // the trip list while dragging or cutting.
        private readonly List<Placed> _layout = new List<Placed>();
        private int _layoutLines = 1;
        private int _lineH = 17;
        private int _nameH = 17;
        private float _whoW;
        private int _actionH;

        private void RecomputeHeight()
        {
            using (var g = CreateGraphics())
            {
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                _lineH = LineHeight(g);
                _nameH = NameRowHeight(g);
                _layout.Clear();
                var lines = LayoutRuns(g, _layout);
                _layoutLines = Math.Max(1, lines.Count);
                _whoW = g.MeasureString(Who ?? "", NameFont, PointF.Empty, StringFormat.GenericTypographic).Width;
                bool anyAction = !string.IsNullOrEmpty(ActionText) || !string.IsNullOrEmpty(SecondaryActionText);
                _actionH = anyAction
                    ? 5 + (int)Math.Ceiling(g.MeasureString("X", ActionFont).Height)
                    : 0;
                LayoutActions(g, PadT + _nameH + RowGap + _layoutLines * _lineH + 5);
                int h = PadT + _nameH + RowGap + _layoutLines * _lineH + PadB + _actionH;
                Height = Math.Max(48, h);
            }
        }

        private Rectangle _actionRect = Rectangle.Empty;
        private Rectangle _secondaryRect = Rectangle.Empty;
        private bool _hoverAction;
        private bool _hoverSecondary;

        /// <summary>
        /// Place the action words right-to-left and remember where they landed.
        ///
        /// Measured here rather than in OnPaint because the click targets have to exist whether
        /// or not a paint has happened, and because partial repaints (the fuse strip, the age
        /// label) never run the full paint path that would otherwise compute them.
        /// </summary>
        private void LayoutActions(Graphics g, int y)
        {
            _actionRect = Rectangle.Empty;
            _secondaryRect = Rectangle.Empty;
            var fmt = StringFormat.GenericTypographic;
            int right = Width - PadR;
            // Slop around the glyphs: these words are 8pt, and a target you have to aim at is a
            // target that gets misclicked into the wrong answer.
            const int PadClickX = 6;
            const int PadClickY = 4;

            if (!string.IsNullOrEmpty(ActionText))
            {
                var sz = g.MeasureString(ActionText.ToUpperInvariant(), ActionFont, PointF.Empty, fmt);
                int w = (int)Math.Ceiling(sz.Width);
                _actionRect = new Rectangle(
                    right - w - PadClickX, y - PadClickY,
                    w + PadClickX * 2, (int)Math.Ceiling(sz.Height) + PadClickY * 2);
                right -= w + 14;
            }
            if (!string.IsNullOrEmpty(SecondaryActionText))
            {
                var sz = g.MeasureString(SecondaryActionText.ToUpperInvariant(), ActionFont, PointF.Empty, fmt);
                int w = (int)Math.Ceiling(sz.Width);
                _secondaryRect = new Rectangle(
                    right - w - PadClickX, y - PadClickY,
                    w + PadClickX * 2, (int)Math.Ceiling(sz.Height) + PadClickY * 2);
            }
        }

        private static int LineHeight(Graphics g) => (int)Math.Ceiling(g.MeasureString("Xg", BodyFont).Height);
        private static int NameRowHeight(Graphics g) => (int)Math.Ceiling(g.MeasureString("Xg", NameFont).Height);

        /// <summary>Only the fuse strip along the bottom edge (repainted ~10x/s while alive).</summary>
        public Rectangle FuseRect => new Rectangle(0, Math.Max(0, Height - 3), Width, 3);

        /// <summary>Only the age label at the top right (repainted once a second).</summary>
        public Rectangle AgeRect => new Rectangle(Math.Max(0, Width - PadR - 40), 0, PadR + 40, PadT + _nameH + 2);

        private sealed class Placed
        {
            public ScheduleToastRun Run;
            public RectangleF Box;
            public int Line;
        }

        /// <summary>Greedy flow layout of runs into lines; returns per-line lists.</summary>
        private List<List<Placed>> LayoutRuns(Graphics g, List<Placed> flat)
        {
            var lines = new List<List<Placed>> { new List<Placed>() };
            float maxW = Width - PadL - PadR;
            float x = 0;
            int lineH = LineHeight(g);
            var fmt = StringFormat.GenericTypographic;
            // GenericTypographic measures a lone space as 0 wide; measure it as the
            // difference between "a a" and "aa" so words actually get a gap.
            float spaceW = Math.Max(3f,
                g.MeasureString("a a", BodyFont, PointF.Empty, fmt).Width
                - g.MeasureString("aa", BodyFont, PointF.Empty, fmt).Width);
            foreach (var run in _runs)
            {
                if (string.IsNullOrEmpty(run.Text)) continue;
                SizeF sz = MeasureRun(g, run, fmt);
                // Punctuation runs (". You're on", ",") hug the previous word.
                bool glue = ".,;:".IndexOf(run.Text[0]) >= 0;
                float gap = x > 0 && !glue ? spaceW : 0;
                float need = gap + sz.Width;
                if (x > 0 && x + need > maxW)
                {
                    if (lines.Count >= MaxLines) break;
                    lines.Add(new List<Placed>());
                    x = 0;
                    gap = 0;
                    need = sz.Width;
                }
                float startX = x + gap;
                var placed = new Placed
                {
                    Run = run,
                    Box = new RectangleF(startX, (lines.Count - 1) * lineH, sz.Width, lineH),
                    Line = lines.Count - 1,
                };
                lines[lines.Count - 1].Add(placed);
                flat?.Add(placed);
                x = startX + sz.Width;
            }
            return lines;
        }

        private static SizeF MeasureRun(Graphics g, ScheduleToastRun run, StringFormat fmt)
        {
            switch (run.Kind)
            {
                case ScheduleToastRun.Style.Mono:
                    return g.MeasureString(run.Text, MonoFont, PointF.Empty, fmt);
                case ScheduleToastRun.Style.Chip:
                case ScheduleToastRun.Style.ChipAccent:
                case ScheduleToastRun.Style.ChipWarn:
                    var s = g.MeasureString(run.Text, ChipFont, PointF.Empty, fmt);
                    return new SizeF(s.Width + ChipPadX * 2 + 1, ChipH);
                default:
                    return g.MeasureString(run.Text, BodyFont, PointF.Empty, fmt);
            }
        }

        // ---------------------------------------------------------------- paint

        private Color Dimmed(Color c)
        {
            if (Dim >= 0.999f) return c;
            Color bg = SupeyTheme.SurfaceElevated;
            float t = Math.Max(0.35f, Dim);
            return Color.FromArgb(
                c.A,
                (int)(bg.R + (c.R - bg.R) * t),
                (int)(bg.G + (c.G - bg.G) * t),
                (int)(bg.B + (c.B - bg.B) * t));
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Region clips the sheared corners; fill the whole thing with the surface.
            using (var b = new SolidBrush(SupeyTheme.SurfaceElevated))
                e.Graphics.FillRectangle(b, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (UiStallWatch.Measure(UiScope.ToastPaint))
                PaintCore(e);
        }

        private void PaintCore(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            var fmt = StringFormat.GenericTypographic;

            Color kind = KindColor;
            Color tick = IsMuted ? SupeyTheme.Divider : kind;
            Color textPrimary = Dimmed(IsSubdued ? SupeyTheme.TextSecondary : SupeyTheme.TextPrimary);
            Color textBody = Dimmed(IsSubdued ? SupeyTheme.TextMuted : SupeyTheme.TextSecondary);
            Color textMuted = Dimmed(SupeyTheme.TextMuted);

            var outer = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = KeyedRect(outer, Cut))
            using (var pen = new Pen(Dimmed(SupeyTheme.BorderSubtle), 1f))
                g.DrawPath(pen, path);

            // Corner tick along the sheared top-left edge.
            using (var pen = new Pen(Dimmed(tick), 2f))
                g.DrawLine(pen, 0.5f, Cut + 0.5f, Cut + 0.5f, 0.5f);

            // Name row.
            float y = PadT;
            float x = PadL;
            using (var b = new SolidBrush(textPrimary))
                g.DrawString(Who, NameFont, b, x, y, fmt);
            x += _whoW;
            int nameH = _nameH;

            if (Unsaved.HasValue)
            {
                x += 6;
                var dot = new RectangleF(x, y + (nameH - 7) / 2f, 7, 7);
                if (Unsaved.Value)
                {
                    using (var pen = new Pen(Dimmed(SupeyTheme.WarnText), 1.5f))
                        g.DrawEllipse(pen, dot);
                }
                else
                {
                    using (var b = new SolidBrush(Dimmed(SupeyTheme.SuccessText)))
                        g.FillEllipse(b, dot);
                }
                x += 7;
            }

            if (Count > 1)
            {
                x += 6;
                string badge = "\u00d7" + Count.ToString(CultureInfo.InvariantCulture);
                var bs = g.MeasureString(badge, BadgeFont, PointF.Empty, fmt);
                var br = new RectangleF(x, y + (nameH - 14) / 2f, bs.Width + 8, 14);
                using (var b = new SolidBrush(Dimmed(SupeyTheme.AccentPrimary)))
                    FillRounded(g, b, br, 2);
                using (var b = new SolidBrush(SupeyTheme.OnAccentText))
                    g.DrawString(badge, BadgeFont, b, br.X + 4, br.Y + (14 - bs.Height) / 2f, fmt);
                x += br.Width;
            }

            string age = AgeLabel;
            if (!string.IsNullOrEmpty(age))
            {
                var asz = g.MeasureString(age, AgeFont, PointF.Empty, fmt);
                using (var b = new SolidBrush(textMuted))
                    g.DrawString(age, AgeFont, b, Width - PadR - asz.Width, y + (nameH - asz.Height) / 2f, fmt);
            }

            // Sentence (pre-measured in RecomputeHeight).
            y += nameH + RowGap;
            foreach (var p in _layout)
            {
                var box = new RectangleF(PadL + p.Box.X, y + p.Box.Y, p.Box.Width, p.Box.Height);
                DrawRun(g, p.Run, box, fmt, textPrimary, textBody, textMuted, kind);
            }
            y += _layoutLines * _lineH;

            if (!string.IsNullOrEmpty(ActionText) || !string.IsNullOrEmpty(SecondaryActionText))
            {
                y += 5;
                Color ac = Kind == ScheduleToastKind.Rejected ? SupeyTheme.ErrorText : SupeyTheme.AccentPrimary;
                if (Kind == ScheduleToastKind.Behind) ac = SupeyTheme.WarnText;
                if (Kind == ScheduleToastKind.Chat) ac = SupeyTheme.TextLink;
                if (Kind == ScheduleToastKind.Question) ac = SupeyTheme.SuccessText;

                // With two choices, lighting up both on hover would say the toast is one button.
                // Only the word under the pointer brightens, so it is obvious which answer a
                // click is about to give.
                bool single = string.IsNullOrEmpty(SecondaryActionText);

                if (!string.IsNullOrEmpty(ActionText))
                {
                    bool lit = single ? _hover : _hoverAction;
                    string a = ActionText.ToUpperInvariant();
                    var asz = g.MeasureString(a, ActionFont, PointF.Empty, fmt);
                    using (var b = new SolidBrush(Dimmed(lit ? SupeyTheme.TextPrimary : ac)))
                        g.DrawString(a, ActionFont, b, Width - PadR - asz.Width, y, fmt);
                }
                if (!string.IsNullOrEmpty(SecondaryActionText))
                {
                    // Declining is always available but never the thing being urged, so it stays
                    // muted until pointed at.
                    string s = SecondaryActionText.ToUpperInvariant();
                    using (var b = new SolidBrush(Dimmed(_hoverSecondary ? SupeyTheme.TextPrimary : SupeyTheme.TextMuted)))
                        g.DrawString(s, ActionFont, b, _secondaryRect.X + 6, y, fmt);
                }
            }

            // Fuse along the bottom edge.
            if (!Sticky)
            {
                float usable = Width - Cut - 1;
                float w = usable * FuseRemaining;
                if (w > 0.5f)
                    using (var pen = new Pen(Dimmed(tick), 1f))
                        g.DrawLine(pen, 0.5f, Height - 1.5f, 0.5f + w, Height - 1.5f);
            }
        }

        private void DrawRun(
            Graphics g, ScheduleToastRun run, RectangleF box, StringFormat fmt,
            Color primary, Color body, Color muted, Color kind)
        {
            switch (run.Kind)
            {
                case ScheduleToastRun.Style.Strong:
                    using (var b = new SolidBrush(primary))
                        g.DrawString(run.Text, BodyFont, b, box.X, box.Y, fmt);
                    break;
                case ScheduleToastRun.Style.Mono:
                    using (var b = new SolidBrush(primary))
                        g.DrawString(run.Text, MonoFont, b, box.X, box.Y + 0.5f, fmt);
                    break;
                case ScheduleToastRun.Style.Arrow:
                    using (var b = new SolidBrush(muted))
                        g.DrawString(run.Text, BodyFont, b, box.X, box.Y, fmt);
                    break;
                case ScheduleToastRun.Style.Chip:
                case ScheduleToastRun.Style.ChipAccent:
                case ScheduleToastRun.Style.ChipWarn:
                    {
                        Color edge = run.Kind == ScheduleToastRun.Style.ChipAccent
                            ? (IsSubdued ? SupeyTheme.TextMuted : kind)
                            : run.Kind == ScheduleToastRun.Style.ChipWarn
                                ? SupeyTheme.WarnText
                                : SupeyTheme.Divider;
                        Color fg = run.Kind == ScheduleToastRun.Style.Chip ? body : Dimmed(edge);
                        var r = new RectangleF(box.X, box.Y + (box.Height - ChipH) / 2f, box.Width - 1, ChipH);
                        using (var b = new SolidBrush(SupeyTheme.Surface))
                            FillRounded(g, b, r, 3);
                        using (var pen = new Pen(Dimmed(edge), 1f))
                            DrawRounded(g, pen, r, 3);
                        var ts = g.MeasureString(run.Text, ChipFont, PointF.Empty, fmt);
                        using (var b = new SolidBrush(fg))
                            g.DrawString(run.Text, ChipFont, b, r.X + ChipPadX, r.Y + (ChipH - ts.Height) / 2f, fmt);
                        break;
                    }
                default:
                    using (var b = new SolidBrush(body))
                        g.DrawString(run.Text, BodyFont, b, box.X, box.Y, fmt);
                    break;
            }
        }

        private static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = radius * 2;
            if (d <= 0 || r.Width < d || r.Height < d)
            {
                p.AddRectangle(r);
                return p;
            }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        private static void FillRounded(Graphics g, Brush b, RectangleF r, float radius)
        {
            using (var p = Rounded(r, radius)) g.FillPath(b, p);
        }

        private static void DrawRounded(Graphics g, Pen pen, RectangleF r, float radius)
        {
            using (var p = Rounded(r, radius)) g.DrawPath(pen, p);
        }

        // ---------------------------------------------------------------- input

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            if (!_pauseStartUtc.HasValue) _pauseStartUtc = DateTime.UtcNow;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool a = _actionRect != Rectangle.Empty && _actionRect.Contains(e.Location);
            bool s = _secondaryRect != Rectangle.Empty && _secondaryRect.Contains(e.Location);
            if (a == _hoverAction && s == _hoverSecondary) return;
            _hoverAction = a;
            _hoverSecondary = s;
            // A pointer that is not over an answer should not look like it can click one.
            Cursor = (RequireActionClick && !a && !s) ? Cursors.Default : Cursors.Hand;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            _hoverAction = false;
            _hoverSecondary = false;
            if (_pauseStartUtc.HasValue)
            {
                _paused += DateTime.UtcNow - _pauseStartUtc.Value;
                _pauseStartUtc = null;
            }
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;

            if (_secondaryRect != Rectangle.Empty && _secondaryRect.Contains(e.Location))
            {
                SecondaryActivated?.Invoke(this, EventArgs.Empty);
                return;
            }
            // On a question, only the words answer. Anywhere else is a miss, and a miss must not
            // be recorded as the dispatcher's opinion.
            if (RequireActionClick
                && !(_actionRect != Rectangle.Empty && _actionRect.Contains(e.Location)))
                return;

            Activated?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Owns the bottom-right stack: enter/exit motion, coalescing, expiry, dot flips,
    /// and the 13px / 41+8px corner rhythm shared with the update link.
    /// </summary>
    internal sealed class ScheduleActivityToastStack : IDisposable
    {
        public const int InsetRight = 13;
        public const int InsetBottom = 41 + 8;
        public const int Gap = 6;
        public const int MaxVisible = 3;
        private const int MotionMs = 160;
        private const int SlideDx = 16;
        private const int CoalesceWindowMs = 4000;

        private sealed class Slot
        {
            public ScheduleActivityToast Toast;
            public Point From;
            public Point To;
            public DateTime MotionStartUtc;
            public bool Entering;
            public bool Exiting;
            public bool Landed;
        }

        private readonly Control _host;
        private readonly Func<int> _bottomInset;
        private readonly List<Slot> _slots = new List<Slot>();
        private readonly Timer _tick = new Timer { Interval = 16 };
        private bool _disposed;

        public event Action<ScheduleActivityToast> ToastClicked;

        /// <summary>The toast's second action was clicked (Skip, on a question).</summary>
        public event Action<ScheduleActivityToast> ToastSecondaryClicked;

        /// <summary>A toast ran out its fuse without anyone acting on it.</summary>
        public event Action<ScheduleActivityToast> ToastExpired;

        /// <summary>Toasts currently on screen and not already sliding out.</summary>
        public int VisibleCount => _slots.Count(s => !s.Exiting);

        public ScheduleActivityToastStack(Control host, Func<int> bottomInset = null)
        {
            _host = host;
            _bottomInset = bottomInset;
            _tick.Tick += (_, __) => Step();
            _host.Resize += (_, __) => Relayout(false);
        }

        public IEnumerable<ScheduleActivityToast> Live => _slots.Where(s => !s.Exiting).Select(s => s.Toast);

        /// <summary>Add a toast; if the newest live toast matches, merge into it instead.</summary>
        public ScheduleActivityToast Push(ScheduleActivityToast toast, Func<ScheduleActivityToast, bool> coalesceWith = null)
        {
            using (UiStallWatch.Measure(UiScope.ToastPush))
                return PushCore(toast, coalesceWith);
        }

        private ScheduleActivityToast PushCore(ScheduleActivityToast toast, Func<ScheduleActivityToast, bool> coalesceWith)
        {
            if (_disposed || toast == null) return null;
            var newest = _slots.LastOrDefault(s => !s.Exiting)?.Toast;
            if (newest != null && coalesceWith != null
                && (DateTime.UtcNow - newest.BornUtc).TotalMilliseconds <= CoalesceWindowMs
                && coalesceWith(newest))
            {
                toast.Dispose();
                newest.Refresh_Lifetime();
                Relayout(true);
                return newest;
            }

            while (_slots.Count(s => !s.Exiting) >= MaxVisible)
            {
                var oldest = _slots.First(s => !s.Exiting);
                if (oldest.Toast.Sticky && _slots.Count(s => !s.Exiting && !s.Toast.Sticky) > 0)
                    oldest = _slots.First(s => !s.Exiting && !s.Toast.Sticky);
                BeginExit(oldest);
            }

            toast.Width = ScheduleActivityToast.DefaultWidth;
            toast.Activated += (s, e) => ToastClicked?.Invoke((ScheduleActivityToast)s);
            toast.SecondaryActivated += (s, e) => ToastSecondaryClicked?.Invoke((ScheduleActivityToast)s);
            _host.Controls.Add(toast);
            toast.BringToFront();
            var slot = new Slot { Toast = toast, Entering = true, MotionStartUtc = DateTime.UtcNow };
            _slots.Add(slot);
            Relayout(true);
            slot.From = new Point(slot.To.X + SlideDx, slot.To.Y);
            toast.Location = slot.From;
            _tick.Start();
            return toast;
        }

        public void Dismiss(ScheduleActivityToast toast)
        {
            var slot = _slots.FirstOrDefault(s => s.Toast == toast && !s.Exiting);
            if (slot != null) BeginExit(slot);
        }

        /// <summary>Flip the state dot on every toast from that desk/day at or before <paramref name="ts"/>.</summary>
        public void MarkSaved(string clientId, string serviceDate, double ts)
        {
            foreach (var s in _slots)
            {
                var t = s.Toast;
                if (t.Unsaved == true
                    && string.Equals(t.SourceClientId, clientId, StringComparison.Ordinal)
                    && string.Equals(t.ServiceDate, serviceDate, StringComparison.Ordinal)
                    && t.EventTs <= ts + 0.5)
                {
                    t.Unsaved = false;
                    t.Invalidate();
                }
            }
        }

        public void DismissWhere(Func<ScheduleActivityToast, bool> pred)
        {
            foreach (var s in _slots.Where(s => !s.Exiting && pred(s.Toast)).ToList())
                BeginExit(s);
        }

        private void BeginExit(Slot slot)
        {
            if (slot.Exiting) return;
            slot.Exiting = true;
            slot.Entering = false;
            slot.MotionStartUtc = DateTime.UtcNow;
            slot.From = slot.Toast.Location;
            slot.To = new Point(slot.From.X + SlideDx * 2, slot.From.Y);
            _tick.Start();
            Relayout(true);
        }

        private int BottomInset()
        {
            try { return _bottomInset?.Invoke() ?? InsetBottom; }
            catch { return InsetBottom; }
        }

        /// <summary>Compute targets: newest at the bottom, stacking upward.</summary>
        private void Relayout(bool animate)
        {
            using (UiStallWatch.Measure(UiScope.ToastRelayout))
                RelayoutCore(animate);
        }

        private void RelayoutCore(bool animate)
        {
            if (_host == null || _host.IsDisposed) return;
            int right = _host.ClientSize.Width - InsetRight;
            int y = _host.ClientSize.Height - BottomInset();
            var live = _slots.Where(s => !s.Exiting).ToList();
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var s = live[i];
                y -= s.Toast.Height;
                var target = new Point(right - s.Toast.Width, y);
                int depth = live.Count - 1 - i;
                s.Toast.Dim = depth == 0 ? 1f : depth == 1 ? 0.85f : 0.6f;
                if (s.To != target)
                {
                    if (animate && s.Landed)
                    {
                        s.From = s.Toast.Location;
                        s.MotionStartUtc = DateTime.UtcNow;
                        s.Landed = false;
                        _tick.Start();
                    }
                    s.To = target;
                    if (!animate) { s.Toast.Location = target; s.Landed = true; }
                }
                y -= Gap;
                s.Toast.BringToFront();
            }
        }

        private static double Ease(double t) => t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;

        private void Step()
        {
            using (UiStallWatch.Measure(UiScope.ToastStep))
                StepCore();
        }

        private void StepCore()
        {
            if (_disposed) { _tick.Stop(); return; }
            bool anyMotion = false;
            var now = DateTime.UtcNow;
            foreach (var s in _slots.ToList())
            {
                var t = s.Toast;
                if (t.IsDisposed) { _slots.Remove(s); continue; }

                double p = Math.Min(1.0, (now - s.MotionStartUtc).TotalMilliseconds / MotionMs);
                if (s.Exiting)
                {
                    anyMotion = true;
                    t.Location = Lerp(s.From, s.To, Ease(p));
                    if (p >= 1.0)
                    {
                        _slots.Remove(s);
                        try { _host.Controls.Remove(t); } catch { }
                        t.Dispose();
                        Relayout(true);
                    }
                    continue;
                }

                if (!s.Landed)
                {
                    anyMotion = true;
                    t.Location = Lerp(s.From, s.To, Ease(p));
                    if (p >= 1.0) { t.Location = s.To; s.Landed = true; s.Entering = false; }
                }

                if (t.Expired)
                {
                    BeginExit(s);
                    anyMotion = true;
                    // Distinct from a dismissal: nobody acted. A question that runs out has not
                    // been answered, and the caller needs to be able to tell those apart.
                    try { ToastExpired?.Invoke(t); } catch { }
                    continue;
                }
                // Fuse: repaint only the 3px strip along the bottom, not the whole card.
                if (!t.Sticky && !anyMotion)
                    t.Invalidate(t.FuseRect);
            }

            // Age label ("3s" -> "4s") once a second, and only that corner.
            if ((now - _lastAgeRepaintUtc).TotalMilliseconds >= 1000)
            {
                _lastAgeRepaintUtc = now;
                foreach (var s in _slots)
                    if (!s.Exiting && s.Landed) s.Toast.Invalidate(s.Toast.AgeRect);
            }

            // 60fps only while something is sliding; 10fps for the fuse; idle for sticky-only.
            int want = anyMotion ? 16 : _slots.Any(s => !s.Toast.Sticky) ? 100 : 1000;
            if (_tick.Interval != want) _tick.Interval = want;
            if (_slots.Count == 0) _tick.Stop();
        }

        private DateTime _lastAgeRepaintUtc = DateTime.MinValue;

        private static Point Lerp(Point a, Point b, double t) =>
            new Point((int)Math.Round(a.X + (b.X - a.X) * t), (int)Math.Round(a.Y + (b.Y - a.Y) * t));

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _tick.Stop();
            _tick.Dispose();
            foreach (var s in _slots.ToList())
            {
                try { _host.Controls.Remove(s.Toast); } catch { }
                s.Toast.Dispose();
            }
            _slots.Clear();
        }
    }
}
