using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Small helpers for the owner-drawn ListViews on the Supey tab.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this exists:</b> the trips preview and drivers ListViews are owner-drawn so
    /// they can render the dark theme + group color swatches we want. With OwnerDraw=true and
    /// View.Details, Windows paints each cell into the on-screen DC directly — no double
    /// buffering — so on the *first* selection paint after items are populated you can see
    /// the bg fill before the text DrawSubItem run completes, and the row appears as flat
    /// gray with the text "missing" until the next mouse move forces another paint cycle.
    /// </para>
    /// <para>
    /// <see cref="ListView.DoubleBuffered"/>, <c>LVS_EX_DOUBLEBUFFER</c>, and painting the
    /// row background once in <c>DrawItem</c> (not again in every <c>DrawSubItem</c>) commit
    /// each row in one pass and eliminate the half-painted flash.
    /// </para>
    /// </remarks>
    internal static class SupeyListViewHelpers
    {
        private const int LvmFirst = 0x1000;
        private const int LvmSetExtendedListViewStyle = LvmFirst + 54;
        private const int LvmGetExtendedListViewStyle = LvmFirst + 55;
        private const int LvsExDoubleBuffer = 0x00010000;
        private const int WmSetRedraw = 0x000B;

        private static readonly PropertyInfo _doubleBufferedPi =
            typeof(ListView).GetProperty(
                "DoubleBuffered",
                BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly Pen _gridPen = new Pen(SupeyTheme.ListGrid, 1f);

        /// <summary>
        /// Turn on double-buffered painting for an owner-drawn ListView. Safe to call before
        /// or after handle creation; no-op if reflection unexpectedly fails on a future
        /// .NET release.
        /// </summary>
        public static void EnableDoubleBuffer(ListView listView)
        {
            if (listView == null) return;
            try
            {
                _doubleBufferedPi?.SetValue(listView, true, null);
            }
            catch
            {
                // Should never happen on .NET Framework 4.x / .NET 6+, but if Microsoft
                // ever renames the property we silently keep the old single-buffered
                // behavior rather than crashing the form load.
            }
        }

        /// <summary>Double-buffer property + native <c>LVS_EX_DOUBLEBUFFER</c> extended style.</summary>
        public static void ApplyNativeFlickerFixes(ListView listView)
        {
            if (listView == null) return;
            EnableDoubleBuffer(listView);
            if (listView.OwnerDraw)
                ListViewHoverRepaintFix.Attach(listView);
            if (listView.IsHandleCreated)
                ApplyNativeExtendedStyles(listView);
            else
                listView.HandleCreated += OnHandleCreated_ApplyNativeStyles;
        }

        private static void OnHandleCreated_ApplyNativeStyles(object sender, EventArgs e)
        {
            if (sender is ListView lv)
            {
                lv.HandleCreated -= OnHandleCreated_ApplyNativeStyles;
                ApplyNativeExtendedStyles(lv);
            }
        }

        private static void ApplyNativeExtendedStyles(ListView listView)
        {
            if (!listView.IsHandleCreated) return;
            try
            {
                IntPtr style = SendMessage(listView.Handle, LvmGetExtendedListViewStyle, IntPtr.Zero, IntPtr.Zero);
                style = new IntPtr(style.ToInt64() | LvsExDoubleBuffer);
                SendMessage(listView.Handle, LvmSetExtendedListViewStyle, new IntPtr(LvsExDoubleBuffer), style);
            }
            catch
            {
                // Best-effort; DoubleBuffered alone still helps.
            }
        }

        /// <summary>Suppresses painting during bulk column-width or item updates.</summary>
        public static void SetRedraw(ListView listView, bool enable, bool invalidate = false)
        {
            if (listView == null) return;
            SetControlRedraw(listView, enable, invalidate);
        }

        /// <summary>WM_SETREDRAW for any control — used to batch layout during splitter drags.</summary>
        public static void SetControlRedraw(Control control, bool enable, bool invalidate = false)
        {
            if (control == null || !control.IsHandleCreated) return;
            SendMessage(control.Handle, WmSetRedraw, enable ? (IntPtr)1 : IntPtr.Zero, IntPtr.Zero);
            if (enable && invalidate)
                control.Invalidate(true);
        }

        /// <summary>True while the user is dragging a wired splitter (ListView repaints deferred).</summary>
        internal static bool SplitterDragActive => _splitterDragDepth > 0;

        private static int _splitterDragDepth;
        private static Control _splitterDragScope;
        private static HashSet<ListView> _splitterDragListViews;

        private static void BeginSplitterDrag(Control scope)
        {
            if (Interlocked.Increment(ref _splitterDragDepth) != 1)
                return;

            _splitterDragScope = scope;
            _splitterDragListViews = new HashSet<ListView>();
            if (scope != null)
                CollectListViews(scope, _splitterDragListViews);

            foreach (var lv in _splitterDragListViews)
                SetControlRedraw(lv, false);
        }

        private static void EndSplitterDrag()
        {
            if (Interlocked.Decrement(ref _splitterDragDepth) != 0)
                return;

            if (_splitterDragListViews != null)
            {
                foreach (var lv in _splitterDragListViews)
                {
                    SetControlRedraw(lv, true);
                    if (lv.IsHandleCreated)
                        lv.Invalidate(true);
                }
            }

            _splitterDragScope?.Invalidate(true);
            _splitterDragScope = null;
            _splitterDragListViews = null;
        }

        /// <summary>Defers heavy repaints until splitter drag ends. Layout stays live.</summary>
        public static void WireSplitContainerSmoothResize(SplitContainer split)
        {
            if (split == null) return;

            split.ControlAdded += (s, e) =>
            {
                if (_splitterDragScope == split)
                    _splitterDragListViews = null;
            };
            split.ControlRemoved += (s, e) =>
            {
                if (_splitterDragScope == split)
                    _splitterDragListViews = null;
            };

            split.SplitterMoving += (s, e) =>
            {
                if (_splitterDragDepth == 0)
                    BeginSplitterDrag(split);
            };
            split.SplitterMoved += (s, e) => EndSplitterDrag();
        }

        /// <summary>Same as <see cref="WireSplitContainerSmoothResize"/> for legacy <see cref="Splitter"/> bars.</summary>
        public static void WireSplitterSmoothResize(Splitter splitter, Control layoutRoot = null)
        {
            if (splitter == null) return;
            Control root = layoutRoot ?? splitter.Parent;

            splitter.SplitterMoving += (s, e) =>
            {
                if (_splitterDragDepth == 0)
                    BeginSplitterDrag(root ?? splitter.Parent);
            };
            splitter.SplitterMoved += (s, e) => EndSplitterDrag();
        }

        private static void CollectListViews(Control root, HashSet<ListView> into)
        {
            if (root == null || into == null) return;
            if (root is ListView lv)
                into.Add(lv);
            foreach (Control child in root.Controls)
                CollectListViews(child, into);
        }

        /// <summary>
        /// Walks <paramref name="root"/>'s control tree and double-buffers every
        /// <see cref="ListView"/> found, including ones nested in panels, splitters, tab
        /// pages, etc. We only flip the buffering bit — colors, owner-draw handlers, and
        /// every other property are untouched, so existing themes are preserved exactly.
        /// </summary>
        /// <remarks>
        /// Idempotent: calling twice on the same control is harmless. Also wires the
        /// <see cref="Control.ControlAdded"/> event so ListViews inserted later (e.g. by
        /// async UI code) inherit the same fix without each call site needing to opt in.
        /// </remarks>
        public static void EnableDoubleBufferRecursively(Control root)
        {
            if (root == null) return;
            if (root is ListView lv) ApplyNativeFlickerFixes(lv);
            foreach (Control child in root.Controls)
                EnableDoubleBufferRecursively(child);

            root.ControlAdded -= OnControlAdded_PropagateBuffer;
            root.ControlAdded += OnControlAdded_PropagateBuffer;
        }

        private static void OnControlAdded_PropagateBuffer(object sender, ControlEventArgs e)
        {
            if (e?.Control == null) return;
            EnableDoubleBufferRecursively(e.Control);
        }

        /// <summary>Shared dark column header chrome for Supey owner-draw listviews.</summary>
        public static void DrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
        {
            if (e == null) return;
            e.DrawDefault = false;
            using (var brush = new SolidBrush(SupeyTheme.ListHeader))
                e.Graphics.FillRectangle(brush, e.Bounds);
            using (var pen = new Pen(SupeyTheme.Divider, 1f))
                e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            var rect = new Rectangle(e.Bounds.Left + 6, e.Bounds.Top, e.Bounds.Width - 6, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? "", ListViewOwnerDrawFonts.Header, rect,
                SupeyTheme.ListHeaderText,
                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter);
        }

        /// <summary>
        /// Details view: do not paint in <c>DrawItem</c> — Win32 repaints column 0 on hover without
        /// <c>DrawSubItem</c>, which wipes the row if the background is drawn here. Paint cells in
        /// <see cref="DrawSubItemCellBackground"/> instead.
        /// </summary>
        public static void SuppressDefaultDrawItem(DrawListViewItemEventArgs e)
        {
            if (e != null) e.DrawDefault = false;
        }

        /// <summary>Fill one subitem cell (required for every column in Details owner-draw).</summary>
        public static void DrawSubItemCellBackground(DrawListViewSubItemEventArgs e, Color background)
        {
            if (e == null) return;
            DrawSubItemCellBackground(e, background, e.Bounds);
        }

        public static void DrawSubItemCellBackground(DrawListViewSubItemEventArgs e, Color background, Rectangle bounds)
        {
            if (e == null) return;
            e.DrawDefault = false;
            using (var brush = new SolidBrush(background))
                e.Graphics.FillRectangle(brush, bounds);
        }

        /// <summary>
        /// Paints a 1px right + bottom hairline on a sub-item cell to emulate
        /// <c>GridLines = true</c> in owner-draw mode. Single source of truth so
        /// every Supey-styled ListView uses the same grid color / weight.
        /// </summary>
        public static void DrawCellGridLines(Graphics g, Rectangle bounds)
        {
            g.DrawLine(_gridPen, bounds.Right - 1, bounds.Top, bounds.Right - 1, bounds.Bottom - 1);
            g.DrawLine(_gridPen, bounds.Left, bounds.Bottom - 1, bounds.Right - 1, bounds.Bottom - 1);
        }

        /// <summary>
        /// Modern flat checkbox for a ListView's column-0 cell. Replaces the
        /// chunky beveled Win32 default with a square, anti-aliased two-state
        /// glyph that fits the SupeyTheme palette.
        /// </summary>
        public static void DrawModernCheckbox(Graphics g, Rectangle cellBounds, bool isChecked, bool selectedRow)
        {
            const int boxSize = 14;
            int x = cellBounds.Left + 6;
            int y = cellBounds.Top + (cellBounds.Height - boxSize) / 2;
            var box = new Rectangle(x, y, boxSize, boxSize);

            var oldSmooth = g.SmoothingMode;
            var oldOffset = g.PixelOffsetMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            try
            {
                if (isChecked)
                {
                    using (var fill = new SolidBrush(SupeyTheme.AccentPrimary))
                        g.FillRectangle(fill, box);
                    using (var pen = new Pen(SupeyTheme.AccentStripe, 1f))
                        g.DrawRectangle(pen, box);
                    using (var pen = new Pen(Color.FromArgb(20, 28, 12), 1.8f)
                    {
                        StartCap = LineCap.Round,
                        EndCap = LineCap.Round,
                        LineJoin = LineJoin.Round,
                    })
                    {
                        g.DrawLines(pen, new[]
                        {
                            new PointF(x + 3f, y + 7.5f),
                            new PointF(x + 6f, y + 10.5f),
                            new PointF(x + 11f, y + 4f),
                        });
                    }
                }
                else
                {
                    Color innerFill = selectedRow
                        ? Color.FromArgb(80, 130, 180)
                        : SupeyTheme.SurfaceElevated;
                    using (var fill = new SolidBrush(innerFill))
                        g.FillRectangle(fill, box);
                    using (var pen = new Pen(selectedRow ? Color.FromArgb(180, 210, 240) : SupeyTheme.BorderSubtle, 1.2f))
                        g.DrawRectangle(pen, box);
                }
            }
            finally
            {
                g.SmoothingMode = oldSmooth;
                g.PixelOffsetMode = oldOffset;
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    }
}
