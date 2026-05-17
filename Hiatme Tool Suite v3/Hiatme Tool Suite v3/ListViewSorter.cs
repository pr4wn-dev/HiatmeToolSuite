using System;
using System.Collections;
using System.Globalization;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Drop-in column-click sorter for any details-mode <see cref="ListView"/>. Cycles a clicked
    /// column through Ascending → Descending → None, and parses common cell formats (currency,
    /// numeric, date/time) before falling back to a case-insensitive string compare.
    /// </summary>
    /// <remarks>
    /// Pair with <c>listView_DrawColumnHeader</c> in <c>Form1.cs</c> — it inspects
    /// <see cref="ListView.ListViewItemSorter"/> for an instance of this class to draw the sort arrow.
    /// </remarks>
    internal sealed class ListViewSorter : IComparer
    {
        public int SortColumn { get; private set; } = -1;
        public SortOrder Order { get; private set; } = SortOrder.None;

        public int Compare(object x, object y)
        {
            if (SortColumn < 0 || Order == SortOrder.None) return 0;
            var lx = x as ListViewItem;
            var ly = y as ListViewItem;
            if (lx == null || ly == null) return 0;

            string a = SortColumn < lx.SubItems.Count ? (lx.SubItems[SortColumn].Text ?? "") : "";
            string b = SortColumn < ly.SubItems.Count ? (ly.SubItems[SortColumn].Text ?? "") : "";
            int cmp = SmartCompare(a, b);
            return Order == SortOrder.Descending ? -cmp : cmp;
        }

        /// <summary>
        /// Tries currency ("$1,234.56"), then plain numeric, then date/time, then case-insensitive
        /// string. Empty cells are pushed to the bottom regardless of sort direction so they don't
        /// flip-flop position when toggling Asc/Desc.
        /// </summary>
        private static int SmartCompare(string a, string b)
        {
            bool ea = string.IsNullOrWhiteSpace(a);
            bool eb = string.IsNullOrWhiteSpace(b);
            if (ea && eb) return 0;
            if (ea) return 1;
            if (eb) return -1;

            if (TryParseCurrency(a, out var ca) && TryParseCurrency(b, out var cb))
                return ca.CompareTo(cb);

            if (decimal.TryParse(a, NumberStyles.Any, CultureInfo.InvariantCulture, out var na)
                && decimal.TryParse(b, NumberStyles.Any, CultureInfo.InvariantCulture, out var nb))
                return na.CompareTo(nb);

            if (DateTime.TryParse(a, CultureInfo.CurrentCulture, DateTimeStyles.None, out var da)
                && DateTime.TryParse(b, CultureInfo.CurrentCulture, DateTimeStyles.None, out var db))
                return da.CompareTo(db);

            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryParseCurrency(string s, out decimal value)
        {
            value = 0;
            if (string.IsNullOrEmpty(s)) return false;
            string t = s.Trim();
            if (t.Length > 0 && t[0] == '$') t = t.Substring(1);
            return decimal.TryParse(t, NumberStyles.Number | NumberStyles.AllowCurrencySymbol,
                CultureInfo.InvariantCulture, out value);
        }

        /// <summary>
        /// Wires up <paramref name="lv"/> for click-to-sort. Returns the sorter so callers can read
        /// or override its state if needed; ownership is transferred to the ListView via
        /// <see cref="ListView.ListViewItemSorter"/>.
        /// </summary>
        public static ListViewSorter Attach(ListView lv)
        {
            if (lv == null) throw new ArgumentNullException(nameof(lv));
            // ListView quirk: setting Sorting = None on a Details-view list NULLS OUT ListViewItemSorter.
            // Force Sorting to Ascending FIRST, then assign our sorter. Our sorter starts in a no-op state
            // (SortColumn == -1, Order == None → Compare returns 0) so Items.Add doesn't reshuffle anything
            // until the user actually clicks a header.
            lv.Sorting = SortOrder.Ascending;
            var sorter = new ListViewSorter();
            lv.ListViewItemSorter = sorter;
            lv.ColumnClick += (s, e) =>
            {
                if (sorter.SortColumn == e.Column)
                {
                    sorter.Order = sorter.Order == SortOrder.Ascending
                        ? SortOrder.Descending
                        : sorter.Order == SortOrder.Descending
                            ? SortOrder.None
                            : SortOrder.Ascending;
                }
                else
                {
                    sorter.SortColumn = e.Column;
                    sorter.Order = SortOrder.Ascending;
                }
                lv.Sort();
                // Force header repaint so the indicator arrow follows the new state.
                lv.Invalidate();
            };
            return sorter;
        }
    }
}
