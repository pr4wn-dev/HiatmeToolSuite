using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Glossy red/green login status LED — drawn in code so no PNG fringe on dark surfaces.</summary>
    internal sealed class SupeyStatusLight : Control
    {
        public enum LightState
        {
            Off = 0,
            On = 1,
        }

        private LightState _state = LightState.Off;

        public SupeyStatusLight()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.UserPaint,
                true);
            DoubleBuffered = true;
            Size = new Size(20, 20);
            TabStop = false;
            SupeyThemeManager.ThemeChanged += OnThemeChanged;
        }

        public LightState State
        {
            get => _state;
            set
            {
                if (_state == value) return;
                _state = value;
                Invalidate();
            }
        }

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            SyncBackground();
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            Color bg = Parent?.BackColor ?? SupeyTheme.SurfaceElevated;
            using (var brush = new SolidBrush(bg))
                pevent.Graphics.FillRectangle(brush, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            var outer = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Color bezel = Blend(SupeyTheme.BorderSubtle, SupeyTheme.SurfaceElevated, 0.35f);
            using (var bezelBrush = new SolidBrush(bezel))
                g.FillEllipse(bezelBrush, outer);

            var inner = Inset(outer, 2.5f);
            Color lit = _state == LightState.On
                ? Color.FromArgb(52, 196, 86)
                : Color.FromArgb(232, 58, 52);
            Color dark = _state == LightState.On
                ? Color.FromArgb(24, 128, 48)
                : Color.FromArgb(140, 28, 24);

            using (var path = new GraphicsPath())
            {
                path.AddEllipse(inner);
                using (var glow = new PathGradientBrush(path))
                {
                    glow.CenterColor = Blend(lit, Color.White, 0.28f);
                    glow.SurroundColors = new[] { dark };
                    glow.CenterPoint = new PointF(
                        inner.X + inner.Width * 0.38f,
                        inner.Y + inner.Height * 0.32f);
                    g.FillPath(glow, path);
                }
            }

            var highlight = Inset(inner, inner.Width * 0.18f);
            highlight.Height = highlight.Height * 0.42f;
            using (var hi = new LinearGradientBrush(
                       highlight,
                       Color.FromArgb(170, 255, 255, 255),
                       Color.FromArgb(0, 255, 255, 255),
                       LinearGradientMode.ForwardDiagonal))
                g.FillEllipse(hi, highlight);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                SupeyThemeManager.ThemeChanged -= OnThemeChanged;
            base.Dispose(disposing);
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            if (IsDisposed) return;
            SyncBackground();
            Invalidate();
        }

        private void SyncBackground()
        {
            BackColor = Parent?.BackColor ?? SupeyTheme.SurfaceElevated;
        }

        private static RectangleF Inset(RectangleF r, float pad)
            => new RectangleF(r.X + pad, r.Y + pad, Math.Max(0f, r.Width - pad * 2f), Math.Max(0f, r.Height - pad * 2f));

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
