using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// A flat, dark-themed <see cref="ComboBox"/> that paints its own border and dropdown
    /// arrow. The stock <see cref="FlatStyle.Flat"/> combo still lets Windows render a light
    /// gray 3D border and a gray arrow button, which clashes with the SupeyTheme surfaces.
    /// This subclass overpaints that chrome after the base control draws: a thin themed
    /// border plus a lime-green chevron that matches the accent used elsewhere on the tab.
    /// Item / selected-text rendering is left to the owner-draw handler the caller wires up.
    /// </summary>
    internal sealed class SupeyComboBox : ComboBox
    {
        private const int WM_PAINT = 0x000F;
        private const int ArrowZoneWidth = 22;

        public SupeyComboBox()
        {
            DropDownStyle = ComboBoxStyle.DropDownList;
            FlatStyle = FlatStyle.Flat;
            DrawMode = DrawMode.OwnerDrawFixed;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        /// <summary>Themed border color drawn around the closed control.</summary>
        public Color BorderColor { get; set; } = SupeyTheme.BorderSubtle;

        /// <summary>Color of the dropdown chevron.</summary>
        public Color ArrowColor { get; set; } = SupeyTheme.AccentPrimary;

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_PAINT && !IsDisposed && IsHandleCreated)
                PaintChrome();
        }

        private void PaintChrome()
        {
            using (var g = Graphics.FromHwnd(Handle))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;

                // Cover the system gray arrow button with our dark surface.
                var arrowZone = new Rectangle(Width - ArrowZoneWidth - 1, 1, ArrowZoneWidth, Height - 2);
                using (var fill = new SolidBrush(BackColor))
                    g.FillRectangle(fill, arrowZone);

                // Lime chevron, centered in the arrow zone.
                int cx = arrowZone.Left + (arrowZone.Width / 2);
                int cy = arrowZone.Top + (arrowZone.Height / 2);
                Point[] chevron =
                {
                    new Point(cx - 5, cy - 2),
                    new Point(cx + 5, cy - 2),
                    new Point(cx, cy + 4),
                };
                using (var arrow = new SolidBrush(Enabled ? ArrowColor : SupeyTheme.TextMuted))
                    g.FillPolygon(arrow, chevron);

                // Thin themed border (anti-alias off for a crisp 1px rectangle).
                g.SmoothingMode = SmoothingMode.None;
                using (var pen = new Pen(BorderColor))
                    g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
        }
    }
}
