using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Theme-driven left navigation drawer that replaces MaterialSkin's <c>MaterialDrawer</c>.
    /// Lives on the main form (opaque child control) — no separate transparent overlay window,
    /// so restore-from-minimize cannot flash the desktop through a color-key hole.
    /// </summary>
    public sealed class SupeyDrawer : Control
    {
        public const int CollapsedWidth = 64;
        public const int ExpandedWidth = 256;

        private const int ItemHeight = 40;
        private const int ItemPitch = 48;
        private const int TopPad = 8;
        private const int IconBox = 40;   // left square the icon centers within
        private const int SidePad = 6;

        private readonly TabControl _tabs;
        private readonly ImageList _tabImages;
        private readonly Timer _anim;
        private bool _open;
        private float _t;                 // 0 = collapsed, 1 = fully open
        private int _hoverIndex = -1;

        // Pre-tinted icon cache (MaterialSkin's preProcessIcons trick): tint each icon ONCE per theme
        // into muted / active / hover variants so the per-frame paint is just a cheap DrawImage —
        // never a ColorMatrix recolor while animating.
        private readonly System.Collections.Generic.Dictionary<TabPage, Bitmap> _iconMuted
            = new System.Collections.Generic.Dictionary<TabPage, Bitmap>();
        private readonly System.Collections.Generic.Dictionary<TabPage, Bitmap> _iconActive
            = new System.Collections.Generic.Dictionary<TabPage, Bitmap>();
        private readonly System.Collections.Generic.Dictionary<TabPage, Bitmap> _iconHot
            = new System.Collections.Generic.Dictionary<TabPage, Bitmap>();
        private bool _iconsDirty = true;

        public SupeyDrawer(TabControl tabs, ImageList tabImages)
        {
            _tabs = tabs;
            _tabImages = tabImages;
            SetStyle(ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.UserPaint
                   | ControlStyles.Opaque
                   | ControlStyles.ResizeRedraw, true);
            BackColor = SupeyTheme.SurfaceHeader;
            Cursor = Cursors.Hand;

            _anim = new Timer { Interval = 15 };
            _anim.Tick += Animate;

            if (_tabs != null)
            {
                _tabs.SelectedIndexChanged += (s, e) => Invalidate();
                _tabs.ControlAdded += (s, e) => { _iconsDirty = true; Invalidate(); };
                _tabs.ControlRemoved += (s, e) => { _iconsDirty = true; Invalidate(); };
            }

            SupeyThemeManager.ThemeChanged += (s, e) =>
            {
                if (IsDisposed) return;
                try { _iconsDirty = true; Invalidate(); } catch { }
            };
        }

        public bool IsOpen => _open;

        /// <summary>Width of the currently-painted (opaque) portion of the rail.</summary>
        public int VisibleWidth => CollapsedWidth + (int)((ExpandedWidth - CollapsedWidth) * _t);

        /// <summary>Fired when <see cref="VisibleWidth"/> changes (drawer animation / layout).</summary>
        public event EventHandler VisibleWidthChanged;

        private int _lastVisibleWidth = CollapsedWidth;

        public void Toggle()
        {
            _open = !_open;
            if (!_anim.Enabled) _anim.Start();
        }

        public void Close()
        {
            if (!_open) return;
            _open = false;
            if (!_anim.Enabled) _anim.Start();
        }

        private void Animate(object sender, EventArgs e)
        {
            float target = _open ? 1f : 0f;
            const float step = 0.16f;
            if (_t < target) _t = Math.Min(target, _t + step);
            else if (_t > target) _t = Math.Max(target, _t - step);

            Invalidate();
            NotifyVisibleWidthChanged();

            if (Math.Abs(_t - target) < 0.001f)
            {
                _t = target;
                _anim.Stop();
            }
        }

        private void NotifyVisibleWidthChanged()
        {
            int w = VisibleWidth;
            if (w == _lastVisibleWidth) return;
            _lastVisibleWidth = w;
            try { VisibleWidthChanged?.Invoke(this, EventArgs.Empty); } catch { }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int idx = HitItem(e.Location);
            if (idx != _hoverIndex) { _hoverIndex = idx; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverIndex != -1) { _hoverIndex = -1; Invalidate(); }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int idx = HitItem(e.Location);
            if (idx < 0 || _tabs == null || idx >= _tabs.TabPages.Count) return;
            try { _tabs.SelectedTab = _tabs.TabPages[idx]; } catch { }
            Close();
        }

        private int HitItem(Point p)
        {
            if (_tabs == null || p.X > VisibleWidth) return -1;
            for (int i = 0; i < _tabs.TabPages.Count; i++)
                if (ItemRect(i).Contains(p)) return i;
            return -1;
        }

        private Rectangle ItemRect(int i)
        {
            return new Rectangle(SidePad, TopPad + i * ItemPitch, VisibleWidth - SidePad * 2, ItemHeight);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            int vis = VisibleWidth;

            // Opaque rail only — host width matches VisibleWidth so nothing past the rail exists.
            using (var bg = new SolidBrush(SupeyTheme.SurfaceHeader))
                g.FillRectangle(bg, 0, 0, vis, Height);

            if (_tabs == null) return;
            if (_iconsDirty) BuildIconCache();

            // Anti-aliased text blends with the fade alpha (ClearType needs an opaque background).
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

            int labelAlpha = (int)(255 * Math.Max(0f, (_t - 0.25f) / 0.75f));
            Size isz = _tabImages != null ? _tabImages.ImageSize : Size.Empty;

            for (int i = 0; i < _tabs.TabPages.Count; i++)
            {
                TabPage page = _tabs.TabPages[i];
                Rectangle rect = ItemRect(i);
                bool selected = i == _tabs.SelectedIndex;
                bool hot = i == _hoverIndex;

                // Highlight pill (only the active / hovered row needs antialiasing).
                if (selected || hot)
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (var path = RoundRect(rect, 6))
                    using (var fill = new SolidBrush(selected
                        ? Color.FromArgb(40, SupeyTheme.AccentPrimary)
                        : SupeyTheme.SurfaceElevated))
                        g.FillPath(fill, path);
                    g.SmoothingMode = SmoothingMode.None;
                    if (selected)
                        using (var bar = new SolidBrush(SupeyTheme.AccentPrimary))
                            g.FillRectangle(bar, rect.Left, rect.Top + 6, 3, rect.Height - 12);
                }

                // Cached, pre-tinted icon — plain DrawImage, no per-frame recolor.
                Bitmap icon = (selected ? _iconActive : hot ? _iconHot : _iconMuted).TryGetValue(page, out var bmp) ? bmp : null;
                if (icon != null)
                {
                    int ix = rect.Left + (IconBox - isz.Width) / 2;
                    int iy = rect.Top + (rect.Height - isz.Height) / 2;
                    g.DrawImageUnscaled(icon, ix, iy);
                }

                if (labelAlpha > 4)
                {
                    Color baseColor = selected ? SupeyTheme.AccentPrimary
                                    : hot ? SupeyTheme.TextPrimary
                                    : SupeyTheme.TextSecondary;
                    // Graphics.DrawString honors the alpha channel (GDI TextRenderer does not), so the
                    // label can truly fade in as the drawer opens.
                    var textRect = new RectangleF(rect.Left + IconBox + 4, rect.Top,
                        rect.Right - (rect.Left + IconBox + 4) - 8, rect.Height);
                    using (var br = new SolidBrush(Color.FromArgb(labelAlpha, baseColor)))
                    using (var fmt = new StringFormat(StringFormatFlags.NoWrap)
                    { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter })
                        g.DrawString(page.Text ?? "", SupeyTheme.BodyFont, br, textRect, fmt);
                }
            }

            using (var pen = new Pen(SupeyTheme.Divider))
                g.DrawLine(pen, vis - 1, 0, vis - 1, Height);
        }

        private Image ResolveImage(TabPage page)
        {
            var il = _tabImages;
            if (il == null) return null;
            if (page.ImageIndex >= 0 && page.ImageIndex < il.Images.Count)
                return il.Images[page.ImageIndex];
            if (!string.IsNullOrEmpty(page.ImageKey) && il.Images.ContainsKey(page.ImageKey))
                return il.Images[page.ImageKey];
            return null;
        }

        private void BuildIconCache()
        {
            DisposeIconCache();
            _iconsDirty = false;
            if (_tabImages == null) return;
            Size sz = _tabImages.ImageSize;

            foreach (TabPage page in _tabs.TabPages)
            {
                Image src = ResolveImage(page);
                if (src == null) continue;
                _iconMuted[page] = Tint(src, sz, SupeyTheme.TextSecondary);
                _iconActive[page] = Tint(src, sz, SupeyTheme.AccentPrimary);
                _iconHot[page] = Tint(src, sz, SupeyTheme.TextPrimary);
            }
        }

        private static Bitmap Tint(Image src, Size sz, Color tint)
        {
            var bmp = new Bitmap(sz.Width, sz.Height);
            var cm = new ColorMatrix(new[]
            {
                new float[] { 0, 0, 0, 0, 0 },
                new float[] { 0, 0, 0, 0, 0 },
                new float[] { 0, 0, 0, 0, 0 },
                new float[] { 0, 0, 0, 1, 0 },
                new float[] { tint.R / 255f, tint.G / 255f, tint.B / 255f, 0, 1 }
            });
            using (var g = Graphics.FromImage(bmp))
            using (var ia = new ImageAttributes())
            {
                ia.SetColorMatrix(cm, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                g.DrawImage(src, new Rectangle(0, 0, sz.Width, sz.Height),
                    0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
            }
            return bmp;
        }

        private void DisposeIconCache()
        {
            foreach (var d in new[] { _iconMuted, _iconActive, _iconHot })
            {
                foreach (var b in d.Values) { try { b.Dispose(); } catch { } }
                d.Clear();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _anim?.Stop();
                _anim?.Dispose();
                DisposeIconCache();
            }
            base.Dispose(disposing);
        }

        private static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    /// <summary>Three-line hamburger toggle for the title bar; raises <see cref="Control.Click"/>.</summary>
    public sealed class SupeyHamburger : Control
    {
        private bool _hot;

        public SupeyHamburger()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.UserPaint
                   | ControlStyles.Opaque, true);
            Size = new Size(38, 30);
            Cursor = Cursors.Hand;
            BackColor = SupeyTheme.SurfaceHeader;
            SupeyThemeManager.ThemeChanged += (s, e) =>
            {
                if (IsDisposed) return;
                try { BackColor = SupeyTheme.SurfaceHeader; Invalidate(); } catch { }
            };
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hot = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hot = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var bg = new SolidBrush(_hot ? SupeyTheme.SurfaceElevated : SupeyTheme.SurfaceHeader))
                g.FillRectangle(bg, ClientRectangle);

            Color line = _hot ? SupeyTheme.TextPrimary : SupeyTheme.TextSecondary;
            int cx = Width / 2;
            int cy = Height / 2;
            using (var pen = new Pen(line, 2f))
            {
                g.DrawLine(pen, cx - 8, cy - 6, cx + 8, cy - 6);
                g.DrawLine(pen, cx - 8, cy, cx + 8, cy);
                g.DrawLine(pen, cx - 8, cy + 6, cx + 8, cy + 6);
            }
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) Invalidate(true);
        }
    }
}
