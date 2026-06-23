using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Works around a Win32 ListView bug: in Details + OwnerDraw, moving the mouse over a row
    /// raises <see cref="ListView.DrawItem"/> without <see cref="ListView.DrawSubItem"/>, which
    /// leaves subitem text unpainted if backgrounds are drawn only in DrawItem.
    /// See <see href="https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.listview.ownerdraw"/>.
    /// </summary>
    internal sealed class ListViewHoverRepaintFix
    {
        private static readonly Dictionary<ListView, ListViewHoverRepaintFix> _attached
            = new Dictionary<ListView, ListViewHoverRepaintFix>();

        private readonly ListView _lv;
        private readonly HashSet<int> _hoverRepaintedRows = new HashSet<int>();

        private ListViewHoverRepaintFix(ListView lv)
        {
            _lv = lv;
            lv.MouseMove += OnMouseMove;
            lv.Invalidated += OnInvalidated;
            lv.ColumnWidthChanged += OnColumnWidthChanged;
            lv.Disposed += OnDisposed;
        }

        public static void Attach(ListView lv)
        {
            if (lv == null) throw new ArgumentNullException(nameof(lv));
            if (_attached.ContainsKey(lv)) return;
            _attached[lv] = new ListViewHoverRepaintFix(lv);
        }

        public static void Detach(ListView lv)
        {
            if (lv == null) return;
            if (!_attached.TryGetValue(lv, out var fix)) return;
            fix.Detach();
            _attached.Remove(lv);
        }

        private void Detach()
        {
            _lv.MouseMove -= OnMouseMove;
            _lv.Invalidated -= OnInvalidated;
            _lv.ColumnWidthChanged -= OnColumnWidthChanged;
            _lv.Disposed -= OnDisposed;
            _hoverRepaintedRows.Clear();
        }

        private void OnDisposed(object sender, EventArgs e)
        {
            Detach();
            _attached.Remove(_lv);
        }

        private void OnInvalidated(object sender, InvalidateEventArgs e)
        {
            _hoverRepaintedRows.Clear();
        }

        private void OnColumnWidthChanged(object sender, ColumnWidthChangedEventArgs e)
        {
            _hoverRepaintedRows.Clear();
            // Auto-fit sets every column width in a loop; invalidating here made columns
            // appear to load one at a time. One repaint runs when the batch finishes.
            if (ListViewMinWidthEnforcer.IsApplyingColumnWidths(_lv)) return;
            _lv.Invalidate(true);
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (_lv.View != View.Details) return;
            ListViewItem item = _lv.GetItemAt(e.X, e.Y);
            if (item == null) return;
            if (!_hoverRepaintedRows.Add(item.Index)) return;

            try
            {
                Rectangle row = _lv.GetItemRect(item.Index, ItemBoundsPortion.Entire);
                _lv.Invalidate(row);
            }
            catch (ArgumentException)
            {
                _lv.Invalidate(item.Bounds);
            }
        }
    }
}
