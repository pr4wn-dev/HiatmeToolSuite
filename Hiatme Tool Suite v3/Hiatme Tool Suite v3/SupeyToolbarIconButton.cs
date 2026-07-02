using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Square Schedule Builder toolbar icon — same chrome as <see cref="SupeyButton"/> Secondary
    /// (matches EMAIL SCHEDULES), without map-toggle selection borders.
    /// </summary>
    internal sealed class SupeyToolbarIconButton : Control
    {
        private Func<bool, Bitmap> _iconFactory;
        private Bitmap _icon;
        private bool _hover;
        private bool _pressed;

        public SupeyToolbarIconButton()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.UserPaint
                | ControlStyles.SupportsTransparentBackColor
                | ControlStyles.StandardClick,
                true);
            BackColor = Color.Transparent;
            Size = new Size(26, 26);
            Cursor = Cursors.Hand;
            DoubleBuffered = true;
            SupeyThemeManager.ThemeChanged += OnThemeChanged;
        }

        public void SetIconFactory(Func<bool, Bitmap> factory)
        {
            _iconFactory = factory;
            RefreshIcon();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                SupeyThemeManager.ThemeChanged -= OnThemeChanged;
                _icon?.Dispose();
                _icon = null;
            }
            base.Dispose(disposing);
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            if (IsDisposed)
                return;
            RefreshIcon();
        }

        private void RefreshIcon()
        {
            _icon?.Dispose();
            _icon = _iconFactory?.Invoke(Enabled);
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            RefreshIcon();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            _pressed = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _pressed = true;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _pressed = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            ResolveColors(out Color fill, out Color border);
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = RoundedRect(rect, 4))
            {
                if (fill.A > 0)
                {
                    using (var brush = new SolidBrush(fill))
                        g.FillPath(brush, path);
                }
                if (border.A > 0)
                {
                    using (var pen = new Pen(border, 1f))
                        g.DrawPath(pen, path);
                }
            }

            if (_icon == null)
                return;

            int x = (Width - _icon.Width) / 2;
            int y = (Height - _icon.Height) / 2;
            g.DrawImage(_icon, x, y, _icon.Width, _icon.Height);
        }

        private void ResolveColors(out Color fill, out Color border)
        {
            if (!Enabled)
            {
                fill = SupeyTheme.SurfaceElevated;
                border = SupeyTheme.BorderSubtle;
                return;
            }

            fill = _pressed ? Darken(SupeyTheme.SurfaceElevated, 0.15f)
                : _hover ? Lighten(SupeyTheme.SurfaceElevated, 0.12f)
                : SupeyTheme.SurfaceElevated;
            border = SupeyTheme.BorderSubtle;
        }

        private static Color Lighten(Color c, float amount)
        {
            amount = Math.Max(0f, Math.Min(1f, amount));
            return Color.FromArgb(
                c.A,
                c.R + (int)((255 - c.R) * amount),
                c.G + (int)((255 - c.G) * amount),
                c.B + (int)((255 - c.B) * amount));
        }

        private static Color Darken(Color c, float amount)
        {
            amount = Math.Max(0f, Math.Min(1f, amount));
            return Color.FromArgb(
                c.A,
                (int)(c.R * (1f - amount)),
                (int)(c.G * (1f - amount)),
                (int)(c.B * (1f - amount)));
        }

        private static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            int d = Math.Max(0, radius * 2);
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
