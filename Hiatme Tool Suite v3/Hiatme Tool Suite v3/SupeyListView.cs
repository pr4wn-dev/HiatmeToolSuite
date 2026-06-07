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
    internal class SupeyListView : ListView
    {
        private const int WM_ERASEBKGND = 0x0014;
        private const int WM_PAINT = 0x000F;

        /// <summary>Invoked at the end of WM_PAINT so owner-draw adornments can paint above items.</summary>
        public Action<Graphics> PostPaintItems { get; set; }

        public SupeyListView()
        {
            DoubleBuffered = true;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            SupeyListViewHelpers.ApplyNativeFlickerFixes(this);
        }

        protected override void WndProc(ref Message m)
        {
            // Suppress background erase before owner-draw repaints — a common scroll/selection flicker source.
            if (m.Msg == WM_ERASEBKGND)
            {
                m.Result = (IntPtr)1;
                return;
            }
            base.WndProc(ref m);
            if (m.Msg == WM_PAINT && PostPaintItems != null && IsHandleCreated && Visible)
            {
                try
                {
                    using (var g = CreateGraphics())
                    {
                        g.SetClip(ClientRectangle);
                        PostPaintItems(g);
                    }
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }
    }
}
