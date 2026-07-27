using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Theme-aware WellRyde notification bell for Trip Scout live panel.</summary>
    internal sealed class TripScoutLiveBellControl : Control
    {
        private static readonly Font BadgeFont = new Font("Segoe UI", 7f, FontStyle.Bold);

        private readonly Timer _shakeTimer;
        private int _shakeFrame;
        private bool _shaking;
        private bool _hover;
        private int _badgeCount;
        private bool _hostOwnsBackColor;

        public event EventHandler BellClicked;

        public TripScoutLiveBellControl()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.UserPaint
                | ControlStyles.StandardClick,
                true);

            MinimumSize = new Size(28, 28);
            Size = new Size(32, 32);
            BackColor = SupeyTheme.Surface;
            Cursor = Cursors.Hand;
            TabStop = false;
            AccessibleName = "WellRyde notifications";
            AccessibleRole = AccessibleRole.PushButton;

            _shakeTimer = new Timer { Interval = 60 };
            _shakeTimer.Tick += (_, __) =>
            {
                _shakeFrame = (_shakeFrame + 1) % 12;
                Invalidate();
            };

            Click += (_, __) => BellClicked?.Invoke(this, EventArgs.Empty);
            SupeyThemeManager.ThemeChanged += OnThemeChanged;
        }

        /// <summary>
        /// Host chips (e.g. Driver Habits elevated live strip) set an explicit fill so the bell
        /// does not paint the darker <see cref="SupeyTheme.Surface"/> square.
        /// </summary>
        public void SetHostBackColor(Color color)
        {
            _hostOwnsBackColor = true;
            BackColor = color;
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                SupeyThemeManager.ThemeChanged -= OnThemeChanged;
                _shakeTimer?.Dispose();
            }
            base.Dispose(disposing);
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            if (IsDisposed)
                return;

            if (!_hostOwnsBackColor)
                BackColor = SupeyTheme.Surface;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            Invalidate();
        }

        public void SetNotificationState(int badgeCount, bool shouldShake)
        {
            _badgeCount = Math.Max(0, badgeCount);
            _shaking = shouldShake && _badgeCount > 0;
            if (_shaking)
            {
                if (!_shakeTimer.Enabled)
                {
                    _shakeFrame = 0;
                    _shakeTimer.Start();
                }
            }
            else
            {
                _shakeTimer.Stop();
                _shakeFrame = 0;
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            Color fillBg = BackColor.IsEmpty ? SupeyTheme.Surface : BackColor;
            using (var bg = new SolidBrush(fillBg))
                g.FillRectangle(bg, ClientRectangle);

            float angle = 0f;
            if (_shaking)
            {
                float[] swing = { -12f, 10f, -8f, 7f, -5f, 4f, -2f, 1f, 0f, 0f, 0f, 0f };
                angle = swing[_shakeFrame % swing.Length];
            }

            var center = new PointF(Width / 2f, Height / 2f);
            g.TranslateTransform(center.X, center.Y);
            g.RotateTransform(angle);
            g.TranslateTransform(-center.X, -center.Y);

            Color bellFill = ResolveBellFill();
            Color bellStroke = ResolveBellStroke(bellFill);
            DrawBell(g, center, bellFill, bellStroke);

            g.ResetTransform();

            if (_badgeCount > 0)
                DrawBadge(g, _badgeCount);
        }

        private Color ResolveBellFill()
        {
            if (_badgeCount > 0)
                return SupeyTheme.AccentPrimary;
            if (_hover)
                return Blend(SupeyTheme.TextSecondary, SupeyTheme.AccentPrimary, 0.22f);
            return SupeyTheme.TextSecondary;
        }

        private static Color ResolveBellStroke(Color fill)
        {
            return Blend(fill, SupeyTheme.TextPrimary, 0.35f);
        }

        private static void DrawBell(Graphics g, PointF center, Color fill, Color stroke)
        {
            float scale = Math.Min(g.VisibleClipBounds.Width, g.VisibleClipBounds.Height) / 32f;
            scale = Math.Max(0.75f, Math.Min(1.15f, scale));
            float w = 14f * scale;
            float h = 15f * scale;
            float cx = center.X;
            float top = center.Y - h * 0.55f;

            using (var path = new GraphicsPath())
            {
                path.AddBezier(
                    cx - w * 0.42f, top + h * 0.12f,
                    cx - w * 0.58f, top + h * 0.82f,
                    cx - w * 0.34f, top + h,
                    cx, top + h);
                path.AddBezier(
                    cx, top + h,
                    cx + w * 0.34f, top + h,
                    cx + w * 0.58f, top + h * 0.82f,
                    cx + w * 0.42f, top + h * 0.12f);
                path.CloseFigure();

                using (var brush = new SolidBrush(fill))
                    g.FillPath(brush, path);
                using (var pen = new Pen(stroke, 1.1f))
                    g.DrawPath(pen, path);
            }

            Color clapper = Blend(fill, SupeyTheme.TextPrimary, 0.25f);
            using (var brush = new SolidBrush(clapper))
                g.FillEllipse(brush, cx - 2.2f * scale, top + h + 0.5f, 4.4f * scale, 4.4f * scale);

            using (var brush = new SolidBrush(fill))
                g.FillRectangle(brush, cx - 1.8f * scale, top - 2.5f * scale, 3.6f * scale, 3.6f * scale);
        }

        private void DrawBadge(Graphics g, int count)
        {
            string text = count > 9 ? "9+" : count.ToString();
            int badgeW = 15;
            int badgeH = 14;
            var rect = new Rectangle(Width - badgeW - 1, 1, badgeW, badgeH);
            using (var brush = new SolidBrush(SupeyTheme.ErrorText))
                g.FillEllipse(brush, rect);

            TextRenderer.DrawText(
                g,
                text,
                BadgeFont,
                rect,
                Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private static Color Blend(Color a, Color b, float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }
    }
}
