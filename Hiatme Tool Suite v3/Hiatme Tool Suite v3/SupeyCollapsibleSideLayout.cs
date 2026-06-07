using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Stacks collapsed left/right <see cref="SupeyCollapsiblePanel"/> headers at the top edge
    /// instead of letting WinForms stretch them to full workspace height.
    /// </summary>
    internal static class SupeyCollapsibleSideLayout
    {
        private static readonly HashSet<Control> WiredHosts = new HashSet<Control>();
        private static bool _inLayout;

        public static void EnsureWired(Control host)
        {
            if (host == null || !WiredHosts.Add(host))
                return;
            host.Layout += OnHostLayout;
        }

        private static void OnHostLayout(object sender, LayoutEventArgs e)
        {
            if (_inLayout) return;
            var host = sender as Control;
            if (host == null) return;
            RelayoutCollapsed(host);
        }

        /// <summary>Position collapsed side chips immediately (e.g. right after collapse).</summary>
        public static void RelayoutCollapsed(Control host)
        {
            if (host == null || _inLayout) return;

            _inLayout = true;
            try
            {
                LayoutCollapsedStack(host, DockStyle.Right);
                LayoutCollapsedStack(host, DockStyle.Left);
            }
            finally
            {
                _inLayout = false;
            }
        }

        private static void LayoutCollapsedStack(Control host, DockStyle edge)
        {
            var collapsed = host.Controls
                .OfType<SupeyCollapsiblePanel>()
                .Where(p => p.IsInCollapsedSideStack(edge))
                .OrderByDescending(p => host.Controls.GetChildIndex(p))
                .ToList();

            int y = 0;
            foreach (SupeyCollapsiblePanel panel in collapsed)
            {
                int w = panel.CollapsedRailWidth > 0 ? panel.CollapsedRailWidth : panel.Width;
                int h = panel.CollapsedSideStackHeightPx;
                int x = edge == DockStyle.Right
                    ? Math.Max(0, host.ClientSize.Width - w)
                    : 0;
                var target = new Rectangle(x, y, w, h);
                if (panel.Bounds != target)
                    panel.SetBounds(target.X, target.Y, target.Width, target.Height, BoundsSpecified.All);
                panel.BringToFront();
                panel.EnsureCollapsedHeaderVisible();
                panel.Invalidate(true);
                y += h;
            }
        }
    }
}
