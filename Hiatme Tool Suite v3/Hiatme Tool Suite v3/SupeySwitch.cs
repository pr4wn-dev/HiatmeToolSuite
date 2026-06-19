using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Theme-driven pill toggle — the Supey replacement for MaterialSwitch. Derives from
    /// <see cref="CheckBox"/> so <c>Checked</c>, <c>CheckedChanged</c>, <c>Text</c> and click/keyboard
    /// toggling all work natively; we just owner-paint a rounded track + knob and the label from the
    /// <see cref="SupeyTheme"/> palette. MaterialSwitch-only members are accepted as no-op shims.
    /// </summary>
    internal class SupeySwitch : CheckBox
    {
        private const int TrackW = 40;
        private const int TrackH = 18;
        private const int Knob = 14;

        public SupeySwitch()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            ForeColor = SupeyTheme.TextPrimary;
            Font = SupeyTheme.BodyFont;
            Cursor = Cursors.Hand;
            AutoSize = false;
            Size = new Size(200, 24);
            SupeyThemeManager.ThemeChanged += OnThemeChanged;
        }

        // ── Designer-compat no-ops ────────────────────────────────────────────────
        public int Depth { get; set; }
        public SupeyMouseState MouseState { get; set; } = SupeyMouseState.OUT;
        public Point MouseLocation { get; set; } = new Point(-1, -1);
        public bool Ripple { get; set; }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            if (IsDisposed) return;
            ForeColor = SupeyTheme.TextPrimary;
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                SupeyThemeManager.ThemeChanged -= OnThemeChanged;
            base.Dispose(disposing);
        }

        protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int trackY = (Height - TrackH) / 2;
            var track = new Rectangle(0, trackY, TrackW, TrackH);

            Color trackColor = Checked ? SupeyTheme.AccentPrimary : SupeyTheme.SurfaceElevated;
            using (var path = Rounded(track, TrackH / 2))
            using (var b = new SolidBrush(trackColor))
                g.FillPath(b, path);

            if (!Checked)
            {
                using (var path = Rounded(track, TrackH / 2))
                using (var pen = new Pen(SupeyTheme.BorderSubtle))
                    g.DrawPath(pen, path);
            }

            int knobX = Checked ? TrackW - Knob - 2 : 2;
            int knobY = trackY + (TrackH - Knob) / 2;
            Color knobColor = Checked ? SupeyTheme.OnAccentText : SupeyTheme.TextSecondary;
            using (var b = new SolidBrush(knobColor))
                g.FillEllipse(b, knobX, knobY, Knob, Knob);

            if (!string.IsNullOrEmpty(Text))
            {
                var textRect = new Rectangle(TrackW + 10, 0, Width - TrackW - 10, Height);
                TextRenderer.DrawText(g, Text, Font, textRect, ForeColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            int d = radius * 2;
            var p = new GraphicsPath();
            p.AddArc(r.Left, r.Top, d, d, 90, 180);
            p.AddArc(r.Right - d, r.Top, d, d, 270, 180);
            p.CloseFigure();
            return p;
        }
    }
}
