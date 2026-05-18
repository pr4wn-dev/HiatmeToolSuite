using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Per-column minimum-width clamp for a details-mode <see cref="ListView"/>. The floor for each
    /// column is <c>max(header text width, widest cell text width, optional floor override) + padding</c>,
    /// so the user can always make a column wider but can't drag it narrower than its widest visible content.
    /// </summary>
    /// <remarks>
    /// Attached lists auto-fit when data changes (debounced content watch), on resize, font change,
    /// layout, and after column sort. Call <see cref="Recompute(ListView)"/> for an immediate fit after
    /// bulk binding if you need widths before the next UI idle tick.
    /// </remarks>
    internal sealed class ListViewMinWidthEnforcer
    {
        /// <summary>Matches <c>listView_DrawSubItem</c> left inset (10) plus sort-arrow/header slack.</summary>
        private const int Padding = 34;
        private const int DebounceMs = 100;
        private const int IdleCheckIntervalMs = 150;
        private static readonly Dictionary<ListView, ListViewMinWidthEnforcer> _attached
            = new Dictionary<ListView, ListViewMinWidthEnforcer>();

        private static readonly Dictionary<ListView, Dictionary<int, int>> _columnFloors
            = new Dictionary<ListView, Dictionary<int, int>>();

        private static readonly Dictionary<ListView, Dictionary<int, int>> _columnCeilings
            = new Dictionary<ListView, Dictionary<int, int>>();

        private static EventHandler _idleHandler;
        private static bool _idleRegistered;
        private static DateTime _lastIdlePassUtc = DateTime.MinValue;

        private readonly ListView _lv;
        private int[] _minWidths;
        private bool _isRecomputing;
        private bool _contentAutoFit = true;
        private int _contentSignature;
        private Timer _debounceTimer;

        private ListViewMinWidthEnforcer(ListView lv)
        {
            _lv = lv;
            Recompute();
            _lv.ColumnWidthChanging += OnChanging;
            _lv.SizeChanged += OnLayoutRelatedChange;
            _lv.FontChanged += OnLayoutRelatedChange;
            _lv.VisibleChanged += OnVisibleChanged;
            _lv.Layout += OnLayoutRelatedChange;
            _lv.HandleCreated += OnHandleCreated;
            _lv.Disposed += OnDisposed;

            _debounceTimer = new Timer { Interval = DebounceMs };
            _debounceTimer.Tick += (s, e) =>
            {
                _debounceTimer.Stop();
                Recompute();
            };

            EnsureIdleMonitor();
        }

        private void OnVisibleChanged(object sender, EventArgs e)
        {
            if (_lv.Visible)
                ScheduleRecompute();
        }

        private void OnLayoutRelatedChange(object sender, EventArgs e)
        {
            ScheduleRecompute();
        }

        private void OnDisposed(object sender, EventArgs e)
        {
            _attached.Remove(_lv);
            _columnFloors.Remove(_lv);
            _debounceTimer?.Stop();
            _debounceTimer?.Dispose();
            _debounceTimer = null;
            _lv.ColumnWidthChanging -= OnChanging;
            _lv.SizeChanged -= OnLayoutRelatedChange;
            _lv.FontChanged -= OnLayoutRelatedChange;
            _lv.VisibleChanged -= OnVisibleChanged;
            _lv.Layout -= OnLayoutRelatedChange;
            _lv.HandleCreated -= OnHandleCreated;
            _lv.Disposed -= OnDisposed;
        }

        private void OnHandleCreated(object sender, EventArgs e)
        {
            ScheduleRecompute();
        }

        private void OnChanging(object sender, ColumnWidthChangingEventArgs e)
        {
            if (e.ColumnIndex >= 0 && e.ColumnIndex < _lv.Columns.Count && IsHiddenColumn(_lv.Columns[e.ColumnIndex]))
            {
                e.NewWidth = 0;
                return;
            }
            if (_isRecomputing || _minWidths == null) return;
            if (e.ColumnIndex < 0 || e.ColumnIndex >= _minWidths.Length) return;
            int floor = _minWidths[e.ColumnIndex];
            if (e.NewWidth < floor)
                e.NewWidth = floor;
        }

        private static bool IsHiddenColumn(ColumnHeader col)
        {
            return col != null && string.Equals(col.Tag as string, "hidden", StringComparison.OrdinalIgnoreCase);
        }

        private void CheckContentSignature()
        {
            if (!_contentAutoFit || !_lv.IsHandleCreated || !_lv.Visible)
                return;

            int sig = ComputeContentSignature();
            if (sig == _contentSignature)
                return;

            _contentSignature = sig;
            ScheduleRecompute();
        }

        private int ComputeContentSignature()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + _lv.Items.Count;
                hash = hash * 31 + _lv.Columns.Count;
                for (int i = 0; i < _lv.Columns.Count; i++)
                    hash = hash * 31 + (_lv.Columns[i].Text ?? "").GetHashCode();

                foreach (ListViewItem it in _lv.Items)
                {
                    if (it == null) continue;
                    hash = hash * 31 + (it.Text ?? "").GetHashCode();
                    for (int i = 0; i < it.SubItems.Count; i++)
                        hash = hash * 31 + (it.SubItems[i].Text ?? "").GetHashCode();
                }

                return hash;
            }
        }

        private static void EnsureIdleMonitor()
        {
            if (_idleRegistered) return;
            _idleRegistered = true;
            _idleHandler = OnApplicationIdle;
            Application.Idle += _idleHandler;
        }

        private static void OnApplicationIdle(object sender, EventArgs e)
        {
            if ((DateTime.UtcNow - _lastIdlePassUtc).TotalMilliseconds < IdleCheckIntervalMs)
                return;
            _lastIdlePassUtc = DateTime.UtcNow;

            foreach (var enforcer in _attached.Values)
                enforcer.CheckContentSignature();
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
            if (_lv.IsDisposed)
                return;
            if (_lv.InvokeRequired)
            {
                try { _lv.BeginInvoke((MethodInvoker)Recompute); }
                catch (InvalidOperationException) { }
                return;
            }

            try
            {
                if (!_lv.IsHandleCreated)
                    return;

                int colCount = _lv.Columns.Count;
                if (colCount == 0)
                {
                    _minWidths = Array.Empty<int>();
                    _contentSignature = ComputeContentSignature();
                    return;
                }

                var widths = new int[colCount];
                Dictionary<int, int> floors = null;
                Dictionary<int, int> ceilings = null;
                _columnFloors.TryGetValue(_lv, out floors);
                _columnCeilings.TryGetValue(_lv, out ceilings);

                for (int i = 0; i < colCount; i++)
                {
                    if (IsHiddenColumn(_lv.Columns[i]))
                    {
                        widths[i] = 0;
                        continue;
                    }

                    string headerText = _lv.Columns[i].Text ?? "";
                    int max = TextRenderer.MeasureText(headerText, ListViewOwnerDrawFonts.Header).Width;

                    foreach (ListViewItem it in _lv.Items)
                    {
                        if (it == null) continue;
                        string cellText = GetCellText(it, i);
                        if (cellText.Length == 0) continue;
                        int w = TextRenderer.MeasureText(cellText, ListViewOwnerDrawFonts.Cell).Width;
                        if (w > max) max = w;
                    }

                    int floor = max + Padding;
                    if (floors != null && floors.TryGetValue(i, out int minOverride))
                        floor = Math.Max(floor, minOverride);
                    if (ceilings != null && ceilings.TryGetValue(i, out int maxOverride))
                        floor = Math.Min(floor, maxOverride);

                    widths[i] = floor;
                }

                _minWidths = widths;
                _contentSignature = ComputeContentSignature();

                _isRecomputing = true;
                _lv.BeginUpdate();
                try
                {
                    for (int i = 0; i < colCount && i < _lv.Columns.Count; i++)
                        _lv.Columns[i].Width = widths[i];
                }
                finally
                {
                    _lv.EndUpdate();
                    _isRecomputing = false;
                }
            }
            catch
            {
                _isRecomputing = false;
            }
        }

        private static string GetCellText(ListViewItem it, int columnIndex)
        {
            if (columnIndex == 0)
                return it.Text ?? "";
            if (columnIndex < it.SubItems.Count)
                return it.SubItems[columnIndex].Text ?? "";
            return "";
        }

        private void ScheduleRecompute()
        {
            if (_lv.IsDisposed)
                return;

            _debounceTimer.Stop();
            _debounceTimer.Start();
        }

        /// <summary>Wires up <paramref name="lv"/> for min-width clamping and auto-fit. Idempotent.</summary>
        public static ListViewMinWidthEnforcer Attach(ListView lv)
        {
            if (lv == null) throw new ArgumentNullException(nameof(lv));
            if (_attached.TryGetValue(lv, out var existing)) return existing;
            var enforcer = new ListViewMinWidthEnforcer(lv);
            _attached[lv] = enforcer;
            return enforcer;
        }

        /// <summary>
        /// Ensures a column never auto-fits narrower than <paramref name="minimumPixels"/> (e.g. billing notes).
        /// </summary>
        public static void SetColumnFloor(ListView lv, int columnIndex, int minimumPixels)
        {
            if (lv == null || columnIndex < 0) return;
            if (!_columnFloors.TryGetValue(lv, out var floors))
            {
                floors = new Dictionary<int, int>();
                _columnFloors[lv] = floors;
            }
            floors[columnIndex] = minimumPixels;
            if (_attached.ContainsKey(lv))
                ScheduleRecompute(lv);
        }

        /// <summary>Caps auto-fit width (e.g. Trip Scout address columns). Text ellipsizes when narrower than content.</summary>
        public static void SetColumnCeiling(ListView lv, int columnIndex, int maximumPixels)
        {
            if (lv == null || columnIndex < 0 || maximumPixels <= 0) return;
            if (!_columnCeilings.TryGetValue(lv, out var ceilings))
            {
                ceilings = new Dictionary<int, int>();
                _columnCeilings[lv] = ceilings;
            }
            ceilings[columnIndex] = maximumPixels;
            if (_attached.ContainsKey(lv))
                ScheduleRecompute(lv);
        }

        /// <summary>
        /// When false, idle content watching won't resize columns (use for in-memory search filters).
        /// Explicit <see cref="Recompute"/> / <see cref="ScheduleRecompute"/> still run.
        /// </summary>
        public static void SetContentAutoFit(ListView lv, bool enabled)
        {
            if (lv == null) return;
            if (_attached.TryGetValue(lv, out var enforcer))
                enforcer._contentAutoFit = enabled;
        }

        /// <summary>Debounced auto-fit; coalesces rapid updates (search filters, execute loop).</summary>
        public static void ScheduleRecompute(ListView lv)
        {
            if (lv == null) return;
            if (_attached.TryGetValue(lv, out var enforcer))
                enforcer.ScheduleRecompute();
        }

        /// <summary>
        /// Recomputes the per-column floors for an attached <paramref name="lv"/>. Safe no-op if the
        /// list isn't attached. Call this after any bulk binding so the new content sets the floor immediately.
        /// </summary>
        public static void Recompute(ListView lv)
        {
            if (lv == null) return;
            if (_attached.TryGetValue(lv, out var enforcer))
                enforcer.Recompute();
        }
    }
}
