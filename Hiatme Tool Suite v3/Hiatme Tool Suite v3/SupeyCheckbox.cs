using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Theme-driven check box — the Supey replacement for MaterialCheckbox. Derives from
    /// <see cref="CheckBox"/> (native toggle/keyboard behavior) and owner-paints a flat themed box +
    /// lime check + label from the <see cref="SupeyTheme"/> palette. MaterialCheckbox-only members are
    /// accepted as no-op shims for Designer compatibility.
    /// </summary>
    internal class SupeyCheckbox : CheckBox
    {
        private const int BoxSize = 18;

        public SupeyCheckbox()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            ForeColor = SupeyTheme.TextPrimary;
            Font = SupeyTheme.BodyFont;
            Cursor = Cursors.Hand;
            AutoSize = false;
            Size = new Size(160, 24);
            SupeyThemeManager.ThemeChanged += OnThemeChanged;
        }

        // ── Designer-compat no-ops ────────────────────────────────────────────────
        public int Depth { get; set; }
        public SupeyMouseState MouseState { get; set; } = SupeyMouseState.OUT;
        public Point MouseLocation { get; set; } = new Point(-1, -1);
        public bool Ripple { get; set; }
        /// <summary>Accepted for Designer compatibility (MaterialCheckbox.ReadOnly); unused.</summary>
        public bool ReadOnly { get; set; }

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

            int boxY = (Height - BoxSize) / 2;
            var box = new Rectangle(1, boxY, BoxSize, BoxSize);

            using (var fill = new SolidBrush(Checked ? SupeyTheme.AccentPrimary : SupeyTheme.SurfaceElevated))
                g.FillRectangle(fill, box);
            using (var pen = new Pen(Checked ? SupeyTheme.AccentPrimary : SupeyTheme.BorderSubtle))
                g.DrawRectangle(pen, box);

            if (Checked)
            {
                using (var pen = new Pen(SupeyTheme.OnAccentText, 2f))
                {
                    g.DrawLines(pen, new[]
                    {
                        new Point(box.Left + 4, box.Top + 9),
                        new Point(box.Left + 7, box.Top + 13),
                        new Point(box.Left + 14, box.Top + 5),
                    });
                }
            }

            if (!string.IsNullOrEmpty(Text))
            {
                var textRect = new Rectangle(BoxSize + 8, 0, Width - BoxSize - 8, Height);
                TextRenderer.DrawText(g, Text, Font, textRect, ForeColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }
    }
}
