using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Theme-driven left navigation drawer that replaces MaterialSkin's <c>MaterialDrawer</c>.
    /// Hosted on a child control on <see cref="SupeyForm"/> (MaterialDrawer model).
    /// </summary>
    public sealed class SupeyDrawer : Control
    {
        public const int CollapsedWidth = 64;
        public const int ExpandedWidth = 256;

        private const int ItemHeight = 40;
        private const int ItemPitch = 48;
        private const int TopPad = 8;
        /// <summary>Icon column matches collapsed rail width so icons stay centered when the drawer opens.</summary>
        private const int IconColumnWidth = CollapsedWidth;
        private const int ItemSideInset = 8;
        private const int LabelGap = 12;

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
                   | ControlStyles.Opaque, true);
            BackColor = SupeyTheme.SurfaceHeader;
            Cursor = Cursors.Hand;

            _anim = new Timer { Interval = 16 };
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

        /// <summary>Fired when <see cref="VisibleWidth"/> changes (each animation step).</summary>
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

            NotifyVisibleWidthChanged();
            Invalidate();

            if (Math.Abs(_t - target) < 0.001f)
            {
                _t = target;
                _anim.Stop();
                NotifyVisibleWidthChanged();
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
            int w = Math.Min(VisibleWidth, Width);
            int inset = w > CollapsedWidth ? ItemSideInset : Math.Max(4, (CollapsedWidth - 48) / 2);
            return new Rectangle(inset, TopPad + i * ItemPitch, w - inset * 2, ItemHeight);
        }

        private static int IconLeft(Size iconSize) =>
            Math.Max(0, (IconColumnWidth - iconSize.Width) / 2);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            int vis = Math.Min(VisibleWidth, Width);

            using (var bg = new SolidBrush(SupeyTheme.SurfaceHeader))
                g.FillRectangle(bg, 0, 0, vis, Height);

            if (_tabs == null) return;
            if (_iconsDirty) BuildIconCache();

            bool fast = _anim.Enabled;
            int labelAlpha = fast ? 0 : (int)(255 * Math.Max(0f, (_t - 0.25f) / 0.75f));
            Size isz = _tabImages != null ? _tabImages.ImageSize : Size.Empty;

            if (!fast)
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

            for (int i = 0; i < _tabs.TabPages.Count; i++)
            {
                TabPage page = _tabs.TabPages[i];
                Rectangle rect = ItemRect(i);
                bool selected = i == _tabs.SelectedIndex;
                bool hot = i == _hoverIndex;

                if (!fast && (selected || hot))
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

                Bitmap icon = (selected ? _iconActive : hot ? _iconHot : _iconMuted).TryGetValue(page, out var bmp) ? bmp : null;
                if (icon != null)
                {
                    int ix = IconLeft(isz);
                    int iy = rect.Top + (rect.Height - isz.Height) / 2;
                    g.DrawImageUnscaled(icon, ix, iy);
                }

                if (labelAlpha > 4)
                {
                    Color baseColor = selected ? SupeyTheme.AccentPrimary
                                    : hot ? SupeyTheme.TextPrimary
                                    : SupeyTheme.TextSecondary;
                    var textRect = new RectangleF(IconColumnWidth + LabelGap, rect.Top,
                        rect.Right - IconColumnWidth - LabelGap, rect.Height);
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

    /// <summary>
    /// Opaque navigation rail hosted as a child control on <see cref="SupeyForm"/> — same model as
    /// MaterialSkin's <see cref="MaterialDrawer"/> (not a separate overlay window).
    /// </summary>
    public sealed class SupeyDrawerHost
    {
        private readonly SupeyDrawer _drawer;
        private SupeyForm _owner;
        private SupeyForm.ResizeDirection _drawerResize = SupeyForm.ResizeDirection.None;

        public SupeyDrawerHost(TabControl tabs, ImageList tabImages)
        {
            _drawer = new SupeyDrawer(tabs, tabImages);
            _drawer.VisibleWidthChanged += (s, e) => SyncBounds();
            _drawer.MouseMove += Drawer_MouseMove;
            _drawer.MouseDown += Drawer_MouseDown;
        }

        public SupeyDrawer Drawer => _drawer;

        private void Drawer_MouseMove(object sender, MouseEventArgs e)
        {
            if (_owner == null || _owner.WindowState == FormWindowState.Maximized || !_owner.Sizable)
            {
                _drawerResize = SupeyForm.ResizeDirection.None;
                return;
            }

            int b = SupeyForm.BorderWidth;
            SupeyForm.ResizeDirection dir = SupeyForm.ResizeDirection.None;
            Cursor cur = Cursors.Default;

            if (e.X <= b && e.Y >= _drawer.ClientSize.Height - b)
            {
                dir = SupeyForm.ResizeDirection.BottomLeft;
                cur = Cursors.SizeNESW;
            }
            else if (e.X <= b)
            {
                dir = SupeyForm.ResizeDirection.Left;
                cur = Cursors.SizeWE;
            }
            else if (e.Y >= _drawer.ClientSize.Height - b)
            {
                dir = SupeyForm.ResizeDirection.Bottom;
                cur = Cursors.SizeNS;
            }

            _drawerResize = dir;
            _owner.SetDrawerResizeCursor(dir, cur);
            _drawer.Cursor = dir != SupeyForm.ResizeDirection.None ? cur : Cursors.Hand;
        }

        private void Drawer_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || _owner == null)
                return;
            if (_drawerResize != SupeyForm.ResizeDirection.None)
                _owner.BeginResizeFromDrawer(_drawerResize);
        }

        public bool IsOpen => _drawer.IsOpen;
        public void Toggle() => _drawer.Toggle();
        public void CloseDrawer() => _drawer.Close();

        public void AttachTo(SupeyForm owner)
        {
            _owner = owner;

            if (!owner.Controls.Contains(_drawer))
                owner.Controls.Add(_drawer);

            owner.Resize += OwnerLayout;
            owner.VisibleChanged += OwnerLayout;
            owner.Shown += OwnerLayout;

            SyncBounds();
            _drawer.BringToFront();
        }

        private void OwnerLayout(object sender, EventArgs e) => SyncBounds();

        public void Reposition() => SyncBounds();

        private void SyncBounds()
        {
            if (_owner == null || _owner.IsDisposed) return;

            if (_owner.WindowState == FormWindowState.Minimized || !_owner.Visible)
            {
                _drawer.Visible = false;
                return;
            }

            _drawer.Visible = true;
            int top = SupeyForm.TitleBarHeight;
            int height = Math.Max(0, _owner.ClientSize.Height - top - 3);
            int width = Math.Max(SupeyDrawer.CollapsedWidth, _drawer.VisibleWidth);
            _drawer.SetBounds(0, top, width, height);
            _drawer.BringToFront();
        }
    }
}
