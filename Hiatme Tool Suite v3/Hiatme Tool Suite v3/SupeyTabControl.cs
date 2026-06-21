using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Header-less <see cref="TabControl"/> — same core technique as MaterialSkin's
    /// <c>MaterialTabControl</c> (<c>TCM_ADJUSTRECT</c> swallowed, zero-height tabs). Tab-button
    /// chrome is owner-drawn as a flat theme fill so Win32 never flashes white/system tabs on
    /// maximize or restore. Drawer icons use a separate <see cref="ImageList"/> reference.
    /// </summary>
    public class SupeyTabControl : TabControl
    {
        private const int TCM_ADJUSTRECT = 0x1328;
        private const int WM_ERASEBKGND = 0x0014;

        private FormWindowState _ownerLastState = FormWindowState.Normal;

        public SupeyTabControl()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            EnableDoubleBuffering(this);

            Multiline = false;
            SizeMode = TabSizeMode.Fixed;
            ItemSize = new Size(0, 1);
            Appearance = TabAppearance.FlatButtons;
            DrawMode = TabDrawMode.OwnerDrawFixed;
            BackColor = SupeyTheme.SurfaceBase;

            DrawItem += DrawTabHeader;

            SupeyThemeManager.ThemeChanged += OnThemeChanged;
        }

        /// <summary>
        /// Call once after <see cref="SupeyDrawerHost"/> copies the tab icons.
        /// </summary>
        public void DetachImageListForDrawer()
        {
            if (!DesignMode)
                ImageList = null;
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            if (IsDisposed) return;
            BackColor = SupeyTheme.SurfaceBase;
            Invalidate(true);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DrawItem -= DrawTabHeader;
                SupeyThemeManager.ThemeChanged -= OnThemeChanged;
                UnhookOwnerForm();
            }
            base.Dispose(disposing);
        }

        protected override void OnCreateControl()
        {
            if (!DesignMode)
            {
                Multiline = false;
                SizeMode = TabSizeMode.Fixed;
                ItemSize = new Size(0, 1);
                DrawMode = TabDrawMode.OwnerDrawFixed;
            }
            base.OnCreateControl();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            HookOwnerForm();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            UnhookOwnerForm();
            base.OnHandleDestroyed(e);
        }

        protected override void OnControlAdded(ControlEventArgs e)
        {
            base.OnControlAdded(e);
            if (DesignMode || !(e.Control is TabPage page)) return;
            page.BackColor = SupeyTheme.SurfaceBase;
        }

        private Form _ownerForm;

        private void HookOwnerForm()
        {
            if (DesignMode) return;
            UnhookOwnerForm();
            _ownerForm = FindForm();
            if (_ownerForm == null) return;
            _ownerLastState = _ownerForm.WindowState;
            _ownerForm.Resize += OwnerFormResize;
        }

        private void UnhookOwnerForm()
        {
            if (_ownerForm != null)
            {
                _ownerForm.Resize -= OwnerFormResize;
                _ownerForm = null;
            }
        }

        /// <summary>Maximize/restore relayout can flash native tab chrome — one repaint after state change.</summary>
        private void OwnerFormResize(object sender, EventArgs e)
        {
            if (_ownerForm == null || IsDisposed) return;
            var state = _ownerForm.WindowState;
            if (state == _ownerLastState) return;
            _ownerLastState = state;
            try { BeginInvoke(new Action(() => { try { Invalidate(true); Update(); } catch { } })); } catch { }
        }

        private void DrawTabHeader(object sender, DrawItemEventArgs e)
        {
            if (DesignMode || e.Index < 0) return;
            using (var fill = new SolidBrush(SupeyTheme.SurfaceBase))
                e.Graphics.FillRectangle(fill, e.Bounds);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == TCM_ADJUSTRECT && !DesignMode)
            {
                m.Result = (IntPtr)1;
                return;
            }

            if (m.Msg == WM_ERASEBKGND && !DesignMode)
            {
                using (var g = Graphics.FromHdc(m.WParam))
                using (var fill = new SolidBrush(SupeyTheme.SurfaceBase))
                    g.FillRectangle(fill, ClientRectangle);
                m.Result = (IntPtr)1;
                return;
            }

            base.WndProc(ref m);
        }

        private static void EnableDoubleBuffering(Control c)
        {
            try
            {
                typeof(Control).InvokeMember(
                    "DoubleBuffered",
                    BindingFlags.SetProperty | BindingFlags.Instance | BindingFlags.NonPublic,
                    null, c, new object[] { true });
            }
            catch { }
        }
    }
}
