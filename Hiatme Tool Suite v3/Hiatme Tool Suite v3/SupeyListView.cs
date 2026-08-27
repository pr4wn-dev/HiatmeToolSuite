using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Owner-drawn Supey ListView with double-buffering and reduced erase flicker.
    /// Use instead of <see cref="ListView"/> on the schedule tab and related dialogs.
    /// </summary>
    public sealed class SupeyListView : ListView
    {
        private const int WM_ERASEBKGND = 0x0014;
        private const int WM_PAINT = 0x000F;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_MOUSELEAVE = 0x02A3;
        private const int LVM_FIRST = 0x1000;
        private const int LVM_SETEXTENDEDLISTVIEWSTYLE = LVM_FIRST + 54;
        private const int LVM_GETEXTENDEDLISTVIEWSTYLE = LVM_FIRST + 55;
        private const int LVM_SETHOTITEM = LVM_FIRST + 60;
        private const int LVS_EX_GRIDLINES = 0x00000001;
        private const int LVS_EX_TRACKSELECT = 0x00000008;
        private const int LVS_EX_ONECLICKACTIVATE = 0x00000040;
        private const int LVS_EX_TWOCLICKACTIVATE = 0x00000080;

        /// <summary>Invoked at the end of WM_PAINT so owner-draw adornments can paint above items.</summary>
        public Action<Graphics> PostPaintItems { get; set; }

        /// <summary>Disable native ListView hot-track painting for fully owner-drawn views.</summary>
        public bool SuppressHotTracking
        {
            get => _suppressHotTracking;
            set
            {
                if (_suppressHotTracking == value)
                    return;

                _suppressHotTracking = value;
                if (value && IsHandleCreated)
                    ApplyHotTrackingSuppression();
            }
        }

        private bool _suppressHotTracking;

        private bool _suppressHoverRepaintFix;

        /// <summary>Skip the row-hover repaint workaround for views that paint each cell themselves.</summary>
        public bool SuppressHoverRepaintFix
        {
            get => _suppressHoverRepaintFix;
            set
            {
                if (_suppressHoverRepaintFix == value)
                    return;

                _suppressHoverRepaintFix = value;
                if (value)
                    ListViewHoverRepaintFix.Detach(this);
                else if (OwnerDraw)
                    ListViewHoverRepaintFix.Attach(this);
            }
        }

        private int _wndProcDepth;
        private bool _gridLines = true;

        /// <summary>
        /// Themed owner-draw grid lines (per-cell + empty area below rows). Default on.
        /// Native <see cref="ListView.GridLines"/> is never used — we paint SupeyTheme grids instead.
        /// </summary>
        public new bool GridLines
        {
            get => _gridLines;
            set
            {
                if (_gridLines == value)
                    return;
                _gridLines = value;
                base.GridLines = false;
                if (IsHandleCreated)
                {
                    SuppressNativeGridLines();
                    Invalidate(true);
                }
            }
        }

        public SupeyListView()
        {
            DoubleBuffered = true;
            base.GridLines = false;
            OwnerDraw = true;
            BorderStyle = System.Windows.Forms.BorderStyle.None;
            FullRowSelect = true;
            HideSelection = false;
            HoverSelection = false;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            SupeyListViewHelpers.ApplyNativeFlickerFixes(this);
            // Re-force after handle creation because designer-generated code can set these after ctor.
            base.GridLines = false;
            OwnerDraw = true;
            BorderStyle = System.Windows.Forms.BorderStyle.None;
            FullRowSelect = true;
            HideSelection = false;
            HoverSelection = false;
            SuppressNativeGridLines();
            if (SuppressHotTracking)
                ApplyHotTrackingSuppression();
            EnsureAutoGridHooked();
            try
            {
                if (IsHandleCreated)
                    BeginInvoke(new Action(EnsureAutoGridHooked));
            }
            catch (InvalidOperationException)
            {
            }
        }

        /// <summary>
        /// Re-subscribes so grid lines paint after every other <see cref="DrawSubItem"/> handler.
        /// </summary>
        private void EnsureAutoGridHooked()
        {
            DrawSubItem -= OnAutoDrawCellGrid;
            DrawSubItem += OnAutoDrawCellGrid;
        }

        private void OnAutoDrawCellGrid(object sender, DrawListViewSubItemEventArgs e)
        {
            // Continuous overlay in WM_PAINT seals empty-area joins; per-cell strokes stay in the
            // double-buffer so scroll/select repaints keep grids without waiting on CreateGraphics.
            if (!GridLines || View != View.Details || e?.Graphics == null || e.Item == null)
                return;
            if (SupeyListViewHelpers.ShouldSkipCellGrid(e.Item))
                return;
            e.DrawDefault = false;
            SupeyListViewHelpers.DrawCellGridLinesAuto(e.Graphics, e.Bounds);
        }

        /// <summary>Win32 grid lines ignore owner-draw and paint system chrome on top of our hairlines.</summary>
        private void SuppressNativeGridLines()
        {
            if (!IsHandleCreated)
                return;
            try
            {
                base.GridLines = false;
                IntPtr stylePtr = SendMessage(Handle, LVM_GETEXTENDEDLISTVIEWSTYLE, IntPtr.Zero, IntPtr.Zero);
                int style = stylePtr.ToInt32() & ~LVS_EX_GRIDLINES;
                SendMessage(Handle, LVM_SETEXTENDEDLISTVIEWSTYLE, new IntPtr(LVS_EX_GRIDLINES), new IntPtr(style));
            }
            catch
            {
                // Extended styles are best-effort.
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (IsDisposed)
                return;

            // Suppress background erase before owner-draw repaints — a common scroll/selection flicker source.
            if (m.Msg == WM_ERASEBKGND)
            {
                if (IsHandleCreated)
                    m.Result = (IntPtr)1;
                return;
            }

            if (!IsHandleCreated)
            {
                try { base.WndProc(ref m); }
                catch (ObjectDisposedException) { }
                return;
            }

            _wndProcDepth++;
            try
            {
                base.WndProc(ref m);
            }
            catch (NullReferenceException)
            {
                // ListView can NRE during handle teardown or nested WM_PAINT — never crash the desk.
                if (!IsDisposed && IsHandleCreated)
                    throw;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            finally
            {
                _wndProcDepth--;
            }

            if (SuppressHotTracking && (m.Msg == WM_MOUSEMOVE || m.Msg == WM_MOUSELEAVE
                || m.Msg == WM_LBUTTONDOWN || m.Msg == WM_LBUTTONUP))
                ApplyHotTrackingSuppression();

            // Post-paint only on the outermost WM_PAINT — CreateGraphics here re-enters WndProc.
            if (m.Msg != WM_PAINT || _wndProcDepth > 0)
                return;

            var postPaint = PostPaintItems;
            if (!Visible)
                return;

            // Re-check: CreateGraphics forces handle creation, which can throw
            // Win32Exception ("Error creating window handle") under GDI/USER object
            // pressure. That exception previously bubbled out of the native WndProc
            // callback and killed the whole Suite (KERNELBASE c000041d). This overlay
            // is purely cosmetic, so swallow any paint failure and let the next tick retry.
            if (IsDisposed || !IsHandleCreated || !Visible)
                return;
            try
            {
                using (var g = CreateGraphics())
                {
                    g.SetClip(ClientRectangle);
                    if (GridLines && View == View.Details)
                        SupeyListViewHelpers.PaintEmptyDetailsGrid(this, g);
                    postPaint?.Invoke(g);
                }
            }
            catch (ObjectDisposedException)
            {
            }
            catch (ArgumentException)
            {
                // Handle destroyed between WM_PAINT and CreateGraphics.
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Out of window handles / GDI objects — skip this overlay, never crash the desk.
            }
            catch (InvalidOperationException)
            {
                // Cross-thread / handle-recreation race during teardown.
            }
            catch (NullReferenceException)
            {
                // ListView internals can be half-disposed while WM_PAINT still fires on exit.
            }
        }

        private void ClearNativeHotItem()
        {
            try
            {
                IntPtr previous = SendMessage(Handle, LVM_SETHOTITEM, new IntPtr(-1), IntPtr.Zero);
                int index = previous.ToInt32();
                if (index >= 0 && index < Items.Count)
                    Invalidate(Items[index].Bounds);
            }
            catch
            {
                // Hot-track clearing is cosmetic; never let it destabilize painting.
            }
        }

        /// <summary>Call after bulk item loads to clear any hot/track state that accumulated while the list was empty.</summary>
        public void ResetHotState()
        {
            if (IsHandleCreated && SuppressHotTracking)
                ApplyHotTrackingSuppression();
        }

        private void ApplyHotTrackingSuppression()
        {
            try
            {
                int mask = LVS_EX_TRACKSELECT | LVS_EX_ONECLICKACTIVATE | LVS_EX_TWOCLICKACTIVATE;
                IntPtr stylePtr = SendMessage(Handle, LVM_GETEXTENDEDLISTVIEWSTYLE, IntPtr.Zero, IntPtr.Zero);
                int style = stylePtr.ToInt32() & ~mask;
                SendMessage(Handle, LVM_SETEXTENDEDLISTVIEWSTYLE, new IntPtr(mask), new IntPtr(style));
            }
            catch
            {
                // Extended styles are best-effort; clearing the current hot item still helps.
            }

            ClearNativeHotItem();
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    }
}
