using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Per-column minimum-width clamp for a details-mode <see cref="ListView"/>. The floor for each
    /// column is <c>max(header text width, widest cell text width) + padding</c>, so the user can
    /// always make a column wider but can't drag it narrower than its widest visible content.
    /// </summary>
    /// <remarks>
    /// Cache is computed lazily and on demand. Call <see cref="Recompute(ListView)"/> after binding
    /// new data so the floor reflects the new contents; otherwise the floor stays at whatever it was
    /// the last time it was computed (still works, just may not match new wide cells).
    /// </remarks>
    internal sealed class ListViewMinWidthEnforcer
    {
        private const int Padding = 24;
        private const string HeaderFontFamily = "Archivo Medium";
        private const float HeaderFontSize = 11f;

        private static readonly Dictionary<ListView, ListViewMinWidthEnforcer> _attached
            = new Dictionary<ListView, ListViewMinWidthEnforcer>();

        private readonly ListView _lv;
        private int[] _minWidths;

        private ListViewMinWidthEnforcer(ListView lv)
        {
            _lv = lv;
            Recompute();
            _lv.ColumnWidthChanging += OnChanging;
        }

        private void OnChanging(object sender, ColumnWidthChangingEventArgs e)
        {
            if (_minWidths == null) return;
            if (e.ColumnIndex < 0 || e.ColumnIndex >= _minWidths.Length) return;
            int floor = _minWidths[e.ColumnIndex];
            if (e.NewWidth < floor)
            {
                e.NewWidth = floor;
            }
        }

        /// <summary>
        /// Walk every cell in every column to find the widest text, then store
        /// <c>widest + padding</c> as the floor. Header text is measured with the same font used by
        /// <c>listView_DrawColumnHeader</c> so the floor matches what's actually painted.
        /// Any column currently narrower than its computed floor is widened to the floor so the
        /// content becomes visible right after a load (the floor only acts as a clamp, never shrinks).
        /// </summary>
        public void Recompute()
        {
            try
            {
                int colCount = _lv.Columns.Count;
                if (colCount == 0) { _minWidths = Array.Empty<int>(); return; }

                var widths = new int[colCount];
                using (var headerFont = new Font(HeaderFontFamily, HeaderFontSize, FontStyle.Regular))
                {
                    for (int i = 0; i < colCount; i++)
                    {
                        string headerText = _lv.Columns[i].Text ?? "";
                        int max = TextRenderer.MeasureText(headerText, headerFont).Width;

                        foreach (ListViewItem it in _lv.Items)
                        {
                            if (it == null) continue;
                            if (i < it.SubItems.Count)
                            {
                                string s = it.SubItems[i].Text ?? "";
                                if (s.Length == 0) continue;
                                int w = TextRenderer.MeasureText(s, _lv.Font).Width;
                                if (w > max) max = w;
                            }
                        }

                        widths[i] = max + Padding;
                    }
                }
                _minWidths = widths;

                // Auto-fit: set every column to exactly its floor so the list shrink/grow-fits to
                // the natural content width on load. The drag clamp in OnChanging still prevents
                // the user dragging below this floor afterwards.
                _lv.BeginUpdate();
                try
                {
                    for (int i = 0; i < colCount && i < _lv.Columns.Count; i++)
                    {
                        _lv.Columns[i].Width = widths[i];
                    }
                }
                finally
                {
                    _lv.EndUpdate();
                }
            }
            catch
            {
                // Sizing must never break the list; on failure we just keep the previous floors.
            }
        }

        /// <summary>Wires up <paramref name="lv"/> for min-width clamping. Idempotent.</summary>
        public static ListViewMinWidthEnforcer Attach(ListView lv)
        {
            if (lv == null) throw new ArgumentNullException(nameof(lv));
            if (_attached.TryGetValue(lv, out var existing)) return existing;
            var enforcer = new ListViewMinWidthEnforcer(lv);
            _attached[lv] = enforcer;
            return enforcer;
        }

        /// <summary>
        /// Recomputes the per-column floors for an attached <paramref name="lv"/>. Safe no-op if the
        /// list isn't attached. Call this after any bulk binding so the new content sets the floor.
        /// </summary>
        public static void Recompute(ListView lv)
        {
            if (lv == null) return;
            if (_attached.TryGetValue(lv, out var enforcer))
            {
                enforcer.Recompute();
            }
        }
    }
}
