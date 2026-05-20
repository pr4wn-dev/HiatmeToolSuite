using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// A small rounded-rectangle status pill: colored dot + text label on a subtle
    /// elevated background. Used in the toolbar (OSRM health), in the AI Assistant
    /// header (Online/Offline), and anywhere else we want a "this is the current
    /// state" indicator that reads as a single chip rather than a bare label.
    /// </summary>
    /// <remarks>
    /// Custom-painted so we get true rounded corners under WinForms — Label / Panel
    /// can't render those without owner-draw. The dot color carries the semantic
    /// (green = healthy, orange = degraded, red = error, gray = unknown), and the
    /// pill chrome stays neutral so two pills sitting next to each other look
    /// consistent regardless of state.
    /// </remarks>
    internal sealed class SupeyStatusPill : Control
    {
        private string _text = "";
        private Color _dotColor = SupeyTheme.SuccessText;
        private Color _pillBackColor = SupeyTheme.SurfaceElevated;
        private Color _pillBorderColor = SupeyTheme.BorderSubtle;
        private bool _showDot = true;

        public SupeyStatusPill()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.UserPaint
                | ControlStyles.SupportsTransparentBackColor,
                true);
            BackColor = Color.Transparent;
            ForeColor = SupeyTheme.TextPrimary;
            Font = SupeyTheme.CaptionFont;
            Height = 22;
            DoubleBuffered = true;
        }

        public string Label
        {
            get => _text;
            set
            {
                _text = value ?? "";
                AutoFit();
                Invalidate();
            }
        }

        public Color DotColor
        {
            get => _dotColor;
            set { _dotColor = value; Invalidate(); }
        }

        public Color PillBackColor
        {
            get => _pillBackColor;
            set { _pillBackColor = value; Invalidate(); }
        }

        public Color PillBorderColor
        {
            get => _pillBorderColor;
            set { _pillBorderColor = value; Invalidate(); }
        }

        /// <summary>
        /// Hide the leading dot when the pill itself is purely informational (e.g. a
        /// "2 lessons" count badge that doesn't need a state indicator).
        /// </summary>
        public bool ShowDot
        {
            get => _showDot;
            set { _showDot = value; AutoFit(); Invalidate(); }
        }

        private void AutoFit()
        {
            // TextRenderer.MeasureText with no Graphics arg uses the screen DC for the
            // active font — that's accurate enough for the pill width and avoids
            // forcing handle creation on the control before the parent has laid us out.
            var sz = TextRenderer.MeasureText(_text, Font, Size.Empty,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            int dotW = _showDot ? 14 : 0;
            int padH = 10;
            int padV = 4;
            Width = dotW + sz.Width + padH * 2;
            Height = Math.Max(22, sz.Height + padV * 2);
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            AutoFit();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            int radius = Math.Min(Height, 999) / 2;
            var pillRect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = RoundedRect(pillRect, radius))
            {
                using (var bg = new SolidBrush(_pillBackColor)) g.FillPath(bg, path);
                using (var pen = new Pen(_pillBorderColor, 1)) g.DrawPath(pen, path);
            }

            int textLeft = 10;
            if (_showDot)
            {
                int dotSize = 8;
                int dotX = 8;
                int dotY = (Height - dotSize) / 2;
                using (var dot = new SolidBrush(_dotColor))
                    g.FillEllipse(dot, dotX, dotY, dotSize, dotSize);
                textLeft = dotX + dotSize + 6;
            }

            var textRect = new Rectangle(textLeft, 0, Width - textLeft - 10, Height);
            TextRenderer.DrawText(g, _text, Font, textRect, ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }

        private static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            if (d <= 0)
            {
                path.AddRectangle(r);
                return path;
            }
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
