using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
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
    /// <see cref="ListView.DoubleBuffered"/> is a protected property; we set it via
    /// reflection so the framework allocates an off-screen buffer for each row, which
    /// commits in one go and eliminates the half-painted flash.
    /// </para>
    /// </remarks>
    internal static class SupeyListViewHelpers
    {
        private static readonly PropertyInfo _doubleBufferedPi =
            typeof(ListView).GetProperty(
                "DoubleBuffered",
                BindingFlags.Instance | BindingFlags.NonPublic);

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
            if (root is ListView lv) EnableDoubleBuffer(lv);
            foreach (Control child in root.Controls)
            {
                EnableDoubleBufferRecursively(child);
            }
            // Late-added ListViews (rare, but it happens for tabs that lazy-init) get the
            // same treatment without us having to remember to call this again.
            root.ControlAdded -= OnControlAdded_PropagateBuffer;
            root.ControlAdded += OnControlAdded_PropagateBuffer;
        }

        private static void OnControlAdded_PropagateBuffer(object sender, ControlEventArgs e)
        {
            if (e?.Control == null) return;
            EnableDoubleBufferRecursively(e.Control);
        }

        /// <summary>
        /// Paints a 1px right + bottom hairline on a sub-item cell to emulate
        /// <c>GridLines = true</c> in owner-draw mode. Single source of truth so
        /// every Supey-styled ListView uses the same grid color / weight.
        /// </summary>
        public static void DrawCellGridLines(Graphics g, Rectangle bounds)
        {
            using (var pen = new Pen(SupeyTheme.ListGrid, 1f))
            {
                // Right border (cell separator); Bottom-1 keeps the line inside
                // the row instead of bleeding into the next.
                g.DrawLine(pen, bounds.Right - 1, bounds.Top, bounds.Right - 1, bounds.Bottom - 1);
                g.DrawLine(pen, bounds.Left, bounds.Bottom - 1, bounds.Right - 1, bounds.Bottom - 1);
            }
        }

        /// <summary>
        /// Modern flat checkbox for a ListView's column-0 cell. Replaces the
        /// chunky beveled Win32 default with a square, anti-aliased two-state
        /// glyph that fits the SupeyTheme palette:
        /// <list type="bullet">
        /// <item><b>Checked</b>   — filled with <see cref="SupeyTheme.AccentPrimary"/>,
        /// dark check mark drawn on top.</item>
        /// <item><b>Unchecked</b> — outlined square in <see cref="SupeyTheme.BorderSubtle"/>,
        /// fill matches the elevated card surface so it reads as "clickable".</item>
        /// </list>
        /// Painted at the same 4–6 px-from-left offset the Win32 default uses, so
        /// the framework's built-in click-to-toggle hit region (driven by
        /// <c>CheckBoxes = true</c> + state image bounds) lines up exactly with
        /// the visual.
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
    }
}
