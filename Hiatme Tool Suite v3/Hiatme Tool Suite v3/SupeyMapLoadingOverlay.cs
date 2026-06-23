using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Single-surface loading veil — themed backdrop, dot spinner, status text.
    /// No child controls (avoids WinForms transparency / z-order artifacts over maps and lists).
    /// </summary>
    internal sealed class SupeyMapLoadingOverlay : Control
    {
        private const int DotCount = 8;
        private const int SpinnerDiameter = 52;

        private readonly Timer _timer;
        private int _tick;
        private string _message = "Loading…";

        public SupeyMapLoadingOverlay()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.UserPaint
                    | ControlStyles.ResizeRedraw,
                true);

            ApplyTheme();
            Visible = false;
            TabStop = false;

            _timer = new Timer { Interval = 130 };
            _timer.Tick += (s, e) =>
            {
                _tick = (_tick + 1) % DotCount;
                Invalidate();
            };

            SupeyThemeManager.ThemeChanged += OnThemeChanged;
        }

        public string Message
        {
            get => _message;
            set
            {
                string text = string.IsNullOrWhiteSpace(value) ? _message : value.Trim();
                if (string.Equals(_message, text, StringComparison.Ordinal))
                    return;
                _message = text;
                Invalidate();
            }
        }

        public bool IsAnimating
        {
            get => _timer.Enabled;
            set
            {
                if (value)
                {
                    _tick = 0;
                    if (!IsDisposed)
                        _timer.Start();
                }
                else
                {
                    _timer.Stop();
                }

                Invalidate();
            }
        }

        /// <summary>Sync backdrop to the active Supey theme and repaint.</summary>
        public void ApplyTheme()
        {
            BackColor = ResolveBackdrop();
            Invalidate();
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            if (IsDisposed)
                return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(ApplyTheme)); } catch { }
                return;
            }
            ApplyTheme();
        }

        private static Color ResolveBackdrop()
            => Blend(SupeyTheme.SurfaceBase, SupeyTheme.SurfaceHeader, 0.72f);

        private static Color ResolveDimDot()
            => Blend(SupeyTheme.TextMuted, SupeyTheme.SurfaceBase, 0.42f);

        private static Color ResolveLitDot()
            => SupeyTheme.TextSecondary;

        private static Font ResolveMessageFont()
            => SupeyTheme.CaptionFont ?? SupeyTheme.BodyFont;

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            using (var brush = new SolidBrush(ResolveBackdrop()))
                pevent.Graphics.FillRectangle(brush, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            var backdrop = ResolveBackdrop();
            using (var backdropBrush = new SolidBrush(backdrop))
                g.FillRectangle(backdropBrush, ClientRectangle);

            int cx = Width / 2;
            int cy = Height / 2;
            var messageFont = ResolveMessageFont();
            int textHeight = (int)Math.Ceiling(g.MeasureString(_message, messageFont, Width).Height);
            int blockHeight = SpinnerDiameter + 10 + textHeight;
            int spinnerCy = cy - blockHeight / 2 + SpinnerDiameter / 2;

            DrawSpinner(g, cx, spinnerCy);

            var textRect = new RectangleF(0, spinnerCy + SpinnerDiameter / 2 + 10, Width, textHeight + 4);
            using (var brush = new SolidBrush(SupeyTheme.TextSecondary))
            {
                var format = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Near,
                    Trimming = StringTrimming.EllipsisCharacter,
                };
                g.DrawString(_message, messageFont, brush, textRect, format);
            }
        }

        private void DrawSpinner(Graphics g, int cx, int cy)
        {
            float orbit = SpinnerDiameter / 2f - 8f;
            int active = _tick % DotCount;
            var dimDot = ResolveDimDot();
            var litDot = ResolveLitDot();

            for (int i = 0; i < DotCount; i++)
            {
                bool lit = i == active;
                float dotSize = lit ? 7f : 5.5f;
                Color color = lit ? litDot : dimDot;

                double angle = (i * (Math.PI * 2 / DotCount)) - (Math.PI / 2);
                float x = cx + (float)(Math.Cos(angle) * orbit);
                float y = cy + (float)(Math.Sin(angle) * orbit);

                using (var brush = new SolidBrush(color))
                    g.FillEllipse(brush, x - dotSize / 2f, y - dotSize / 2f, dotSize, dotSize);
            }
        }

        private static Color Blend(Color a, Color b, float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                SupeyThemeManager.ThemeChanged -= OnThemeChanged;
                _timer?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
