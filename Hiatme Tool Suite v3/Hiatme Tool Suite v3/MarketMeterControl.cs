using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Animated circular performance meter — shared by Market Performance and Driver Habits.
    /// Flashes red when the value drops by ≥ 0.1%.
    /// </summary>
    internal sealed class MarketMeterControl : Panel
    {
        private const double DropFlashThreshold = 0.1;

        private readonly Timer _animTimer;
        private double _displayPercent;
        private double _targetPercent;
        private double _lastCommittedPercent = double.NaN;
        private bool _hasValue;
        private float _pulse;
        private bool _pulseUp = true;

        /// <summary>0 = idle, &gt;0 = flash remaining (ticks down each frame).</summary>
        private float _dropFlash;

        public string Caption { get; set; } = "";
        public string ValueText { get; set; } = "—";
        public string DetailText { get; set; } = "—";
        public bool InvertGood { get; set; }
        public bool Compact { get; set; }

        public MarketMeterControl()
        {
            DoubleBuffered = true;
            BackColor = Color.Transparent;
            Resize += (_, __) => Invalidate();

            _animTimer = new Timer { Interval = 16 };
            _animTimer.Tick += AnimTick;
            Disposed += (_, __) =>
            {
                try { _animTimer.Stop(); _animTimer.Dispose(); } catch { }
            };
        }

        public void SetValue(double? percent, string valueText, string detail = null, bool animate = true)
        {
            ValueText = valueText ?? "—";
            if (detail != null)
                DetailText = detail;

            if (!percent.HasValue)
            {
                _hasValue = false;
                _targetPercent = 0;
                if (!animate)
                    _displayPercent = 0;
                Invalidate();
                EnsureAnim();
                return;
            }

            double next = Math.Min(100, Math.Max(0, percent.Value));

            // Flash when the live value drops by ≥ 0.1% from the last committed reading.
            if (_hasValue && !double.IsNaN(_lastCommittedPercent)
                && (_lastCommittedPercent - next) >= DropFlashThreshold)
            {
                _dropFlash = 1f; // ~1s of flash decay + blink
            }

            _hasValue = true;
            _targetPercent = next;
            _lastCommittedPercent = next;
            if (!animate)
                _displayPercent = _targetPercent;
            EnsureAnim();
            Invalidate();
        }

        private void EnsureAnim()
        {
            if (!_animTimer.Enabled)
                _animTimer.Start();
        }

        private void AnimTick(object sender, EventArgs e)
        {
            bool moved = false;
            double delta = _targetPercent - _displayPercent;
            if (Math.Abs(delta) > 0.05)
            {
                _displayPercent += delta * 0.14;
                if (Math.Abs(_targetPercent - _displayPercent) < 0.05)
                    _displayPercent = _targetPercent;
                moved = true;
            }

            if (_hasValue)
            {
                if (_pulseUp) { _pulse += 0.035f; if (_pulse >= 1f) { _pulse = 1f; _pulseUp = false; } }
                else { _pulse -= 0.035f; if (_pulse <= 0f) { _pulse = 0f; _pulseUp = true; } }
                moved = true;
            }

            if (_dropFlash > 0f)
            {
                _dropFlash -= 0.028f;
                if (_dropFlash < 0f) _dropFlash = 0f;
                moved = true;
            }

            if (moved)
                Invalidate();
            else if (Math.Abs(delta) <= 0.05 && !_hasValue && _dropFlash <= 0f)
                _animTimer.Stop();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(SupeyTheme.Surface);

            var card = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            using (var bg = new SolidBrush(SupeyTheme.SurfaceElevated))
            using (var border = new Pen(SupeyTheme.BorderSubtle, 1f))
            using (var path = Rounded(card, 8))
            {
                g.FillPath(bg, path);
                g.DrawPath(border, path);
            }

            // Drop flash: blink red wash + border so a 0.1% dip is obvious.
            if (_dropFlash > 0f)
            {
                float blink = (float)(0.45 + 0.55 * Math.Abs(Math.Sin(_dropFlash * Math.PI * 6)));
                int alpha = (int)(55 + 140 * _dropFlash * blink);
                using (var wash = new SolidBrush(Color.FromArgb(alpha, 248, 81, 73)))
                using (var flashBorder = new Pen(Color.FromArgb(Math.Min(255, alpha + 80), 248, 81, 73), 2.5f))
                using (var path = Rounded(card, 8))
                {
                    g.FillPath(wash, path);
                    g.DrawPath(flashBorder, path);
                }
            }

            int bottomReserve = Compact ? 48 : 70;
            int ringSize = Math.Min(Width - 24, Height - bottomReserve);
            ringSize = Math.Max(Compact ? 56 : 72, ringSize);
            int cx = Width / 2;
            int topPad = Compact ? 10 : 18;
            int cy = topPad + ringSize / 2;
            var ring = new Rectangle(cx - ringSize / 2, cy - ringSize / 2, ringSize, ringSize);

            float penW = Compact ? 7f : 8f;
            using (var track = new Pen(SupeyTheme.BorderSubtle, penW) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawArc(track, ring, -90, 360);

            double pct = _hasValue ? _displayPercent : 0;
            Color accent = _dropFlash > 0.15f
                ? Color.FromArgb(248, 81, 73)
                : PickColor(pct);
            if (_hasValue && pct > 0.05)
            {
                float glow = 0.55f + (_pulse * 0.45f);
                if (_dropFlash > 0f)
                    glow = 0.7f + (_dropFlash * 0.9f);
                var glowColor = Color.FromArgb(
                    (int)Math.Min(220, 90 * glow + (_dropFlash * 100)),
                    accent.R, accent.G, accent.B);
                using (var glowPen = new Pen(glowColor, penW + 4f + (_dropFlash * 3f))
                { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawArc(glowPen, Rectangle.Inflate(ring, 3, 3), -90, (float)(360.0 * pct / 100.0));

                using (var fill = new Pen(accent, penW) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawArc(fill, ring, -90, (float)(360.0 * pct / 100.0));
            }

            float valSize = Compact ? 11f : 12.5f;
            Color valColor = _dropFlash > 0.2f
                ? Color.FromArgb(255, 180, 180)
                : SupeyTheme.TextPrimary;
            using (var valBrush = new SolidBrush(valColor))
            using (var valFont = new Font("Segoe UI", valSize, FontStyle.Bold))
            {
                string text = ValueText ?? "—";
                var sz = g.MeasureString(text, valFont);
                g.DrawString(text, valFont, valBrush, cx - sz.Width / 2f, cy - sz.Height / 2f);
            }

            using (var capBrush = new SolidBrush(SupeyTheme.TextSecondary))
            using (var capFont = new Font("Segoe UI", Compact ? 7.5f : 8.5f, FontStyle.Bold))
            {
                string cap = (Caption ?? "").ToUpperInvariant();
                var sz = g.MeasureString(cap, capFont);
                g.DrawString(cap, capFont, capBrush, cx - sz.Width / 2f, ring.Bottom + (Compact ? 4 : 8));
            }

            using (var detBrush = new SolidBrush(SupeyTheme.TextMuted))
            using (var detFont = new Font("Segoe UI", Compact ? 7.25f : 8f))
            {
                var rect = new RectangleF(8, ring.Bottom + (Compact ? 18 : 26), Width - 16, Compact ? 22 : 28);
                var sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Near,
                    Trimming = StringTrimming.EllipsisCharacter,
                };
                g.DrawString(DetailText ?? "—", detFont, detBrush, rect, sf);
            }
        }

        private Color PickColor(double pct)
        {
            double score = InvertGood ? 100 - pct : pct;
            if (score >= 90) return Color.FromArgb(63, 185, 80);
            if (score >= 70) return Color.FromArgb(210, 153, 34);
            return Color.FromArgb(248, 81, 73);
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            if (r.Width <= 0 || r.Height <= 0)
            {
                path.AddRectangle(r);
                return path;
            }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
