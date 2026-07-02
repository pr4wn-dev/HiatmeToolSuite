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

        private static Color BlendColors(Color from, Color to, double amountTo)
        {
            amountTo = Math.Max(0d, Math.Min(1d, amountTo));
            double amountFrom = 1d - amountTo;
            return Color.FromArgb(
                (int)((from.R * amountFrom) + (to.R * amountTo)),
                (int)((from.G * amountFrom) + (to.G * amountTo)),
                (int)((from.B * amountFrom) + (to.B * amountTo)));
        }

        /// <summary>Grid line color for owner-drawn Supey ListViews (theme-aware hairline).</summary>
        public static Color ListGridLineColor => SupeyTheme.ListGridLine;

        /// <summary>
        /// Merged/group-bar rows paint in <c>DrawItem</c> across all columns — skip per-cell grids
        /// so vertical dividers do not cut through the bar.
        /// </summary>
        public static bool ShouldSkipCellGrid(ListViewItem item)
        {
            if (item?.Tag == null)
                return false;

            switch (item.Tag)
            {
                case SuggestPreviewRowTag tag when tag.IsGroupBar || tag.IsGap:
                    return true;
                case FsPreviewGapTag gap when ScheduleBuilderGapNotes.GapTagHasNoteBar(gap):
                    return true;
                case FsPreviewNoteTag note when note.Group != null:
                    return true;
                case FsPreviewSectionHeaderTag _:
                    return true;
                default:
                    return item.Tag.GetType().Name == "SupeyPreviewGroupHeaderTag";
            }
        }

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
            if (listView.OwnerDraw && !(listView is SupeyListView supeyList && supeyList.SuppressHoverRepaintFix))
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

        /// <summary>Display text for an owner-drawn ListView cell (raw SubItem text unchanged).</summary>
        public static string GetCellDisplayText(ListView listView, int columnIndex, string raw)
            => WellRydeDisplayText.FormatListCell(listView, columnIndex, raw);

        /// <summary>Header bottom rule + column resize splitter (theme-aware, all palettes).</summary>
        public static void PaintColumnHeaderChrome(Graphics g, Rectangle bounds, bool drawRightSplitter = true)
        {
            if (g == null) return;
            using (var brush = new SolidBrush(SupeyTheme.ListHeader))
                g.FillRectangle(brush, bounds);
            using (var bottomPen = new Pen(ListGridLineColor, 1f))
                g.DrawLine(bottomPen, bounds.Left, bounds.Bottom - 1, bounds.Right - 1, bounds.Bottom - 1);
            if (drawRightSplitter)
                PaintColumnHeaderSplitter(g, bounds);
        }

        /// <summary>Visible full-height column boundary for resize affordance.</summary>
        public static void PaintColumnHeaderSplitter(Graphics g, Rectangle bounds)
        {
            if (g == null) return;
            int x = bounds.Right - 1;
            int y1 = bounds.Top + 2;
            int y2 = bounds.Bottom - 2;
            if (y2 <= y1) return;
            Color bright = SupeyTheme.ListHeaderSplitter;
            Color shadow = BlendColors(SupeyTheme.ListHeader, bright, 0.22);
            using (var shadowPen = new Pen(shadow, 1f))
                g.DrawLine(shadowPen, x - 1, y1, x - 1, y2);
            using (var gripPen = new Pen(bright, 1f))
                g.DrawLine(gripPen, x, y1, x, y2);
        }

        /// <summary>Shared dark column header chrome for Supey owner-draw listviews.</summary>
        public static void DrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
        {
            if (e == null) return;
            e.DrawDefault = false;
            PaintColumnHeaderChrome(e.Graphics, e.Bounds);
            var rect = new Rectangle(e.Bounds.Left + 6, e.Bounds.Top, e.Bounds.Width - 6, e.Bounds.Height);
            TextFormatFlags align = TextFormatFlags.Left;
            if (e.Header != null)
            {
                switch (e.Header.TextAlign)
                {
                    case HorizontalAlignment.Right:
                        align = TextFormatFlags.Right;
                        break;
                    case HorizontalAlignment.Center:
                        align = TextFormatFlags.HorizontalCenter;
                        break;
                }
            }
            TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? "", ListViewOwnerDrawFonts.Header, rect,
                SupeyTheme.ListHeaderText,
                align | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter);
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

        /// <summary>Sum of every column width — full logical width of a Details row.</summary>
        public static int GetDetailsContentWidth(ListView listView)
        {
            if (listView?.Columns == null || listView.Columns.Count == 0)
                return 0;

            int w = 0;
            foreach (ColumnHeader col in listView.Columns)
                w += col.Width;
            return w;
        }

        /// <summary>One merged bar across all columns (workbook-style group / section header).</summary>
        public static void PaintMergedDetailsRow(
            Graphics g,
            Rectangle mergedBounds,
            Color background,
            string text,
            Color textColor,
            Font font,
            bool boldText = false)
        {
            if (g == null)
                return;

            var state = g.Save();
            try
            {
                g.SetClip(mergedBounds);

                using (var brush = new SolidBrush(background))
                    g.FillRectangle(brush, mergedBounds);

                text = text ?? "";
                if (text.Length > 0)
                {
                    var textBounds = new Rectangle(
                        mergedBounds.Left + 6,
                        mergedBounds.Top,
                        Math.Max(0, mergedBounds.Width - 12),
                        mergedBounds.Height);
                    Font drawFont = boldText ? new Font(font, FontStyle.Bold) : font;
                    try
                    {
                        TextRenderer.DrawText(
                            g,
                            text,
                            drawFont,
                            textBounds,
                            textColor,
                            TextFormatFlags.Left
                                | TextFormatFlags.SingleLine
                                | TextFormatFlags.VerticalCenter
                                | TextFormatFlags.EndEllipsis
                                | TextFormatFlags.NoPrefix);
                    }
                    finally
                    {
                        if (boldText && drawFont != null)
                            drawFont.Dispose();
                    }
                }

                using (var pen = new Pen(ListGridLineColor, 1f))
                    g.DrawLine(pen, mergedBounds.Left, mergedBounds.Bottom - 1, mergedBounds.Right - 1, mergedBounds.Bottom - 1);
            }
            finally
            {
                g.Restore(state);
            }
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

        /// <summary>True when <paramref name="listView"/> should paint themed grid lines.</summary>
        public static bool ShowsGridLines(ListView listView)
            => listView is SupeyListView slv ? slv.GridLines : listView?.GridLines == true;

        /// <summary>
        /// Paints a 1px right + bottom hairline on a sub-item cell to emulate
        /// <c>GridLines = true</c> in owner-draw mode. Single source of truth so
        /// every Supey-styled ListView uses the same grid color / weight.
        /// </summary>
        /// <remarks>
        /// On <see cref="SupeyListView"/> with <c>GridLines = true</c>, this is a no-op — the control
        /// paints grids once after all <see cref="ListView.DrawSubItem"/> handlers. Call this only from
        /// custom handlers when <c>GridLines = false</c> (Schedule Builder trips, driver picker, etc.).
        /// </remarks>
        public static void DrawCellGridLines(Graphics g, Rectangle bounds, ListView listView = null)
        {
            if (listView is SupeyListView slv)
            {
                if (slv.GridLines)
                    return;
            }
            PaintCellGridLines(g, bounds);
        }

        /// <summary>Used by <see cref="SupeyListView"/> after owner-draw handlers — always paints when enabled.</summary>
        internal static void DrawCellGridLinesAuto(Graphics g, Rectangle bounds)
            => PaintCellGridLines(g, bounds);

        private static void PaintCellGridLines(Graphics g, Rectangle bounds)
        {
            if (g == null || bounds.Width <= 0 || bounds.Height <= 0)
                return;

            var saved = g.Save();
            try
            {
                g.PixelOffsetMode = PixelOffsetMode.Half;
                int right = bounds.Right - 1;
                int bottom = bounds.Bottom - 1;
                using (var pen = new Pen(ListGridLineColor, 1f))
                {
                    g.DrawLine(pen, right, bounds.Top, right, bottom);
                    g.DrawLine(pen, bounds.Left, bottom, right, bottom);
                }
            }
            finally
            {
                g.Restore(saved);
            }
        }

        /// <summary>Client Y where trip rows begin (below the native column-header child window).</summary>
        public static int GetDetailsHeaderHeight(ListView listView)
        {
            if (listView == null || listView.IsDisposed || listView.View != View.Details)
                return 0;

            if (!listView.IsHandleCreated)
                return FallbackDetailsHeaderHeight();

            try
            {
                if (listView.Items.Count > 0)
                {
                    ListViewItem first = listView.Items[0];
                    if (first != null)
                    {
                        int top = first.Bounds.Top;
                        if (top > 0)
                            return top;
                    }
                }
            }
            catch (NullReferenceException)
            {
                // Bounds are not always materialized during BeginUpdate or the first WM_PAINT.
            }

            return FallbackDetailsHeaderHeight();
        }

        private static int FallbackDetailsHeaderHeight() =>
            Math.Max(24, TextRenderer.MeasureText("Status", ListViewOwnerDrawFonts.Header).Height + 8);

        private static bool TryGetItemBounds(ListView listView, int index, out Rectangle bounds)
        {
            bounds = Rectangle.Empty;
            if (listView == null || listView.IsDisposed || !listView.IsHandleCreated)
                return false;
            if (index < 0 || index >= listView.Items.Count)
                return false;

            try
            {
                ListViewItem item = listView.Items[index];
                if (item == null)
                    return false;
                bounds = item.Bounds;
                return true;
            }
            catch (NullReferenceException)
            {
                return false;
            }
        }

        /// <summary>
        /// Extends column + row grid lines through the empty client area below the last item,
        /// matching the billing list workbook look.
        /// </summary>
        public static void PaintEmptyDetailsGrid(ListView listView, Graphics g)
        {
            if (listView is SupeyListView slv && !slv.GridLines)
                return;
            if (g == null || listView == null || listView.IsDisposed || listView.View != View.Details
                || listView.Columns.Count == 0 || !listView.IsHandleCreated)
                return;

            int headerH = GetDetailsHeaderHeight(listView);

            int rowH = Math.Max(18, TextRenderer.MeasureText("Ag", listView.Font ?? ListViewOwnerDrawFonts.Cell).Height + 5);
            if (TryGetItemBounds(listView, 0, out Rectangle firstBounds) && firstBounds.Height > 0)
                rowH = Math.Max(16, firstBounds.Height);

            int contentW = 0;
            foreach (ColumnHeader col in listView.Columns)
                contentW += col.Width;
            contentW = Math.Max(contentW, listView.ClientSize.Width);

            int startY = headerH;
            if (listView.Items.Count > 0
                && TryGetItemBounds(listView, listView.Items.Count - 1, out Rectangle lastBounds))
            {
                startY = Math.Max(headerH, lastBounds.Bottom);
            }

            using (var pen = new Pen(ListGridLineColor, 1f))
            {
                int x = 0;
                foreach (ColumnHeader col in listView.Columns)
                {
                    x += col.Width;
                    g.DrawLine(pen, x - 1, startY, x - 1, listView.ClientSize.Height - 1);
                }

                for (int y = startY + rowH - 1; y < listView.ClientSize.Height; y += rowH)
                    g.DrawLine(pen, 0, y, contentW - 1, y);
            }
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

        /// <summary>
        /// Re-assert list ink and repaint every ListView under <paramref name="root"/> after a theme switch.
        /// Owner-draw handlers read live <see cref="SupeyTheme"/> at paint time; this sets ForeColor and
        /// invalidates so non-owner-draw paths and headers refresh too.
        /// </summary>
        public static void RefreshThemeColors(Control root)
        {
            if (root == null || root.IsDisposed) return;

            if (root is ListView lv)
            {
                lv.ForeColor = SupeyTheme.ListText;
                lv.Invalidate(true);
            }

            foreach (Control child in root.Controls)
                RefreshThemeColors(child);
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    }
}
