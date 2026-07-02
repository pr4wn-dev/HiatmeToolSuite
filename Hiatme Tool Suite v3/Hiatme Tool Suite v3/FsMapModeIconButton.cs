using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Small icon toggle for Schedule Builder map display modes.</summary>
    internal sealed class FsMapModeIconButton : Control
    {
        private bool _selected;
        private bool _hover;
        private Bitmap _icon;

        public FsMapModeIconButton()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.UserPaint
                | ControlStyles.Opaque,
                true);
            BackColor = SupeyTheme.SurfaceHeader;
            Size = new Size(30, 30);
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        public bool Selected
        {
            get => _selected;
            set
            {
                if (_selected == value) return;
                _selected = value;
                Invalidate();
            }
        }

        public Bitmap Icon
        {
            get => _icon;
            set
            {
                _icon = value;
                Invalidate();
            }
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
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var back = _selected
                ? Color.FromArgb(48, SupeyTheme.AccentPrimary)
                : _hover
                    ? SupeyTheme.SurfaceElevated
                    : BackColor;
            if (back.A > 0)
            {
                using (var brush = new SolidBrush(back))
                using (var path = RoundedRect(ClientRectangle, 4))
                    g.FillPath(brush, path);
            }

            if (_selected)
            {
                using (var border = new Pen(Color.FromArgb(120, SupeyTheme.AccentPrimary), 1f))
                using (var path = RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 4))
                    g.DrawPath(border, path);
            }

            if (_icon != null)
            {
                int x = (Width - _icon.Width) / 2;
                int y = (Height - _icon.Height) / 2;
                g.DrawImage(_icon, x, y, _icon.Width, _icon.Height);
            }
        }

        private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
