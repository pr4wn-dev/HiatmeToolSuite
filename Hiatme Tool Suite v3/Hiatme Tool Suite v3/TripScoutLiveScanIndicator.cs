using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Dot-circle spinner for Trip Scout live panel polling (no text).</summary>
    internal sealed class TripScoutLiveScanIndicator : Control
    {
        private const int DotCount = 8;
        private const int TickMs = 180;

        private readonly Timer _timer;
        private int _tick;
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

            Size = new Size(22, 22);
            BackColor = Color.Transparent;
            TabStop = false;
            Visible = false;
            AccessibleName = "Live panel scanning";

            _timer = new Timer { Interval = TickMs };
            _timer.Tick += (_, __) =>
            {
                _tick = (_tick + 1) % DotCount;
                Invalidate();
            };
        }

        public bool Scanning
        {
            get => _scanning;
            set
            {
                _scanning = value;
                if (value)
                {
                    _tick = 0;
                    Visible = true;
                    if (!IsDisposed && !_timer.Enabled)
                        _timer.Start();
                }
                else
                {
                    _timer.Stop();
                    Visible = false;
                }
                Invalidate();
            }
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            if (Parent != null)
            {
                using (var brush = new SolidBrush(Parent.BackColor))
                    pevent.Graphics.FillRectangle(brush, ClientRectangle);
            }
            else
            {
                base.OnPaintBackground(pevent);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (!_scanning)
                return;

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            float cx = Width / 2f;
            float cy = Height / 2f;
            float orbit = Math.Min(Width, Height) / 2f - 4f;
            int active = _tick % DotCount;
            var dimDot = Blend(SupeyTheme.TextMuted, SupeyTheme.SurfaceHeader, 0.35f);
            var litDot = SupeyTheme.AccentPrimary;

            for (int i = 0; i < DotCount; i++)
            {
                bool lit = i == active;
                float dotSize = lit ? 4.2f : 3f;
                double angle = (i * (Math.PI * 2 / DotCount)) - (Math.PI / 2);
                float x = cx + (float)(Math.Cos(angle) * orbit);
                float y = cy + (float)(Math.Sin(angle) * orbit);

                using (var brush = new SolidBrush(lit ? litDot : dimDot))
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
                _timer?.Dispose();
            base.Dispose(disposing);
        }
    }
}
