using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Paints the strip of header bar to the right of the last column with the same dark color
    /// the owner-drawn columns use (<c>#333333</c>). Without this, Windows fills the unused
    /// header area with the default white theme background, which clashes with our dark UI.
    /// </summary>
    /// <remarks>
    /// Works by subclassing the native HeaderControl child window of a <see cref="ListView"/>
    /// (obtained via <c>LVM_GETHEADER</c>) and post-painting the empty area after the default
    /// WM_PAINT runs. Painting after default avoids fighting the framework's column rendering.
    /// </remarks>
    internal sealed class ListViewHeaderEmptyAreaPainter : NativeWindow
    {
        private const int WM_PAINT = 0x000F;
        private const int LVM_FIRST = 0x1000;
        private const int LVM_GETHEADER = LVM_FIRST + 31;

        private static readonly Color HeaderBackground = ColorTranslator.FromHtml("#333333");

        private static readonly Dictionary<ListView, ListViewHeaderEmptyAreaPainter> _attached
            = new Dictionary<ListView, ListViewHeaderEmptyAreaPainter>();

        private readonly ListView _lv;

        private ListViewHeaderEmptyAreaPainter(ListView lv)
        {
            _lv = lv;
            if (lv.IsHandleCreated)
            {
                HookHeader();
            }
            else
            {
                lv.HandleCreated += (s, e) => HookHeader();
            }
            lv.HandleDestroyed += (s, e) => ReleaseHandle();
        }

        private void HookHeader()
        {
            try
            {
                IntPtr header = SendMessage(_lv.Handle, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
                if (header != IntPtr.Zero)
                {
                    AssignHandle(header);
                }
            }
            catch
            {
                // Native subclass is best-effort; leave the white strip rather than crashing.
            }
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_PAINT)
                PaintEmptyAreaBuffered();
        }

        private void PaintEmptyAreaBuffered()
        {
            try
            {
                RECT clientRect;
                if (!GetClientRect(Handle, out clientRect)) return;

                int totalColumns = 0;
                foreach (ColumnHeader col in _lv.Columns)
                    totalColumns += col.Width;

                int clientWidth = clientRect.right - clientRect.left;
                if (totalColumns >= clientWidth) return;

                int emptyW = clientWidth - totalColumns;
                int emptyH = clientRect.bottom - clientRect.top;
                if (emptyW <= 0 || emptyH <= 0) return;

                using (var bmp = new Bitmap(emptyW, emptyH))
                using (var mem = Graphics.FromImage(bmp))
                using (var brush = new SolidBrush(HeaderBackground))
                using (var screen = Graphics.FromHwnd(Handle))
                {
                    mem.FillRectangle(brush, 0, 0, emptyW, emptyH);
                    screen.DrawImage(bmp, totalColumns, clientRect.top);
                }
            }
            catch
            {
                // Painting must never propagate an exception out of WndProc.
            }
        }

        /// <summary>Wires up <paramref name="lv"/>; idempotent.</summary>
        public static ListViewHeaderEmptyAreaPainter Attach(ListView lv)
        {
            if (lv == null) throw new ArgumentNullException(nameof(lv));
            if (_attached.TryGetValue(lv, out var existing)) return existing;
            var painter = new ListViewHeaderEmptyAreaPainter(lv);
            _attached[lv] = painter;
            return painter;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT rect);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }
    }
}
