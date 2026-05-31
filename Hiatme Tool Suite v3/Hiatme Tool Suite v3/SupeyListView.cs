using System;
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

        public SupeyListView()
        {
            DoubleBuffered = true;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            SupeyListViewHelpers.ApplyNativeFlickerFixes(this);
            ListViewHoverRepaintFix.Attach(this);
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
        }
    }
}
