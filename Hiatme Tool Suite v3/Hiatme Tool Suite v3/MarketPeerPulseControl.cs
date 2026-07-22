using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Compact You / ME / US comparison bar — shows how a meter sits vs peers in real time.
    /// </summary>
    internal sealed class MarketPeerPulseControl : Panel
    {
        public string Title { get; set; } = "";
        public double? You { get; set; }
        public double? Regional { get; set; }
        public double? National { get; set; }
        public bool InvertGood { get; set; }
        public string Unit { get; set; } = "%";

        private double _displayYou;
        private double _targetYou;
        private readonly Timer _anim;

        public MarketPeerPulseControl()
        {
            DoubleBuffered = true;
            Height = 54;
            BackColor = Color.Transparent;
            _anim = new Timer { Interval = 16 };
            _anim.Tick += (_, __) =>
            {
                double d = _targetYou - _displayYou;
                if (Math.Abs(d) < 0.02)
                {
                    _displayYou = _targetYou;
                    _anim.Stop();
                }
                else
                    _displayYou += d * 0.16;
                Invalidate();
            };
            Disposed += (_, __) => { try { _anim.Stop(); _anim.Dispose(); } catch { } };
        }

        public void SetValues(double? you, double? regional, double? national, bool animate = true)
        {
            You = you;
            Regional = regional;
            National = national;
            _targetYou = you ?? 0;
            if (!animate || !you.HasValue)
                _displayYou = _targetYou;
            else if (!_anim.Enabled)
                _anim.Start();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(SupeyTheme.Surface);

            using (var bg = new SolidBrush(SupeyTheme.SurfaceElevated))
            using (var border = new Pen(SupeyTheme.BorderSubtle, 1f))
            using (var path = Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 8))
            {
                g.FillPath(bg, path);
                g.DrawPath(border, path);
            }

            using (var titleFont = new Font("Segoe UI Semibold", 8.5f))
            using (var titleBrush = new SolidBrush(SupeyTheme.TextPrimary))
                g.DrawString(Title ?? "", titleFont, titleBrush, 10, 6);

            string youTxt = You.HasValue
                ? You.Value.ToString("0.0", CultureInfo.InvariantCulture) + Unit
                : "—";
            using (var valFont = new Font("Segoe UI", 9f, FontStyle.Bold))
            using (var valBrush = new SolidBrush(BarColor(_displayYou)))
            {
                var sz = g.MeasureString(youTxt, valFont);
                g.DrawString(youTxt, valFont, valBrush, Width - sz.Width - 10, 5);
            }

            int barTop = 28;
            int barH = 8;
            int left = 10;
            int barW = Width - 20;
            DrawBar(g, left, barTop, barW, barH, National, SupeyTheme.TextMuted, "US");
            DrawBar(g, left, barTop + 10, barW, barH, Regional, Color.FromArgb(120, SupeyTheme.AccentPrimary), "ME");
            DrawBar(g, left, barTop + 20, barW, barH + 2, _displayYou, BarColor(_displayYou), "YOU", filled: You.HasValue);
        }

        private void DrawBar(Graphics g, int x, int y, int w, int h, double? value, Color color, string tag, bool filled = true)
        {
            using (var track = new SolidBrush(Color.FromArgb(40, SupeyTheme.TextMuted)))
                g.FillRectangle(track, x, y, w, h);

            if (!filled || !value.HasValue) return;
            double pct = InvertGood
                ? Math.Min(100, Math.Max(0, 100 - value.Value)) // for invert metrics, lower value = longer good bar visually via score
                : Math.Min(100, Math.Max(0, value.Value));
            // For invert metrics show raw magnitude on a capped scale so tiny % still visible.
            if (InvertGood)
                pct = Math.Min(100, Math.Max(4, value.Value * 5)); // 0–20% maps across bar

            int fill = (int)Math.Round(w * (pct / 100.0));
            if (fill > 0)
            {
                using (var brush = new SolidBrush(color))
                    g.FillRectangle(brush, x, y, fill, h);
            }
        }

        private Color BarColor(double pct)
        {
            double score = InvertGood ? 100 - Math.Min(100, pct * 5) : pct;
            if (score >= 90) return Color.FromArgb(63, 185, 80);
            if (score >= 70) return Color.FromArgb(210, 153, 34);
            return Color.FromArgb(248, 81, 73);
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            var path = new GraphicsPath();
            if (d <= 0) { path.AddRectangle(r); return path; }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
