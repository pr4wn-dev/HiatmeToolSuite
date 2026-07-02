using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Theme-aware spinner for Trip Scout live panel polling.</summary>
    internal sealed class TripScoutLiveScanIndicator : Control
    {
        private const int TickMs = 80;
        private const float SpinStep = 28f;

        private readonly Timer _timer;
        private float _angle;
        private bool _scanning;

        public TripScoutLiveScanIndicator()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.UserPaint
                    | ControlStyles.ResizeRedraw
                    | ControlStyles.SupportsTransparentBackColor,
                true);

            MinimumSize = new Size(24, 24);
            Size = new Size(28, 28);
            BackColor = SupeyTheme.Surface;
            TabStop = false;
            AccessibleName = "Live panel scanning";

            _timer = new Timer { Interval = TickMs };
            _timer.Tick += (_, __) =>
            {
                _angle = (_angle + SpinStep) % 360f;
                Invalidate();
            };

            SupeyThemeManager.ThemeChanged += OnThemeChanged;
        }

        public bool Scanning
        {
            get => _scanning;
            set
            {
                if (_scanning == value)
                    return;

                _scanning = value;
                if (value)
                {
                    _angle = 0f;
                    if (!IsDisposed && !_timer.Enabled)
                        _timer.Start();
                }
                else
                {
                    _timer.Stop();
                    _angle = 0f;
                }

                Invalidate();
            }
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            if (IsDisposed)
                return;

            BackColor = SupeyTheme.Surface;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            using (var bg = new SolidBrush(SupeyTheme.Surface))
                g.FillRectangle(bg, ClientRectangle);

            var rect = new Rectangle(4, 4, Width - 9, Height - 9);
            if (rect.Width <= 0 || rect.Height <= 0)
                return;

            Color track = Blend(SupeyTheme.TextMuted, SupeyTheme.Surface, 0.45f);
            using (var trackPen = new Pen(track, 2f))
                g.DrawEllipse(trackPen, rect);

            if (!_scanning)
                return;

            using (var pen = new Pen(SupeyTheme.AccentPrimary, 2.5f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            })
                g.DrawArc(pen, rect, _angle, 270f);
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
