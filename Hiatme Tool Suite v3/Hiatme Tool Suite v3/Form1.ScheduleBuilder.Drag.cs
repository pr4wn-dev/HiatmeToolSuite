using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    public partial class Form1
    {
        private bool _fsTripDragActive;
        private bool _fsTripDragPending;
        private Point _fsTripDragStartPt;
        private FsPreviewTripTag _fsTripDragTag;
        private ListViewItem _fsTripDragSourceItem;
        private int _fsTripDragSourceItemIndex = -1;
        private int _fsTripDragInsertLine = -1;
        private bool _fsTripDragMerge;
        private FsPreviewTripTag _fsTripDragDropTarget;
        private int _fsTripDragInsertItemIndex = -1;
        private int _fsTripDragRowHeight = 24;
        private int _fsTripDragLastPreviewKey = int.MinValue;
        private Point _fsTripDragLastClientPt;
        private bool _fsTripDragDecorPainted;

        private void WireFsTripsListDragDrop()
        {
            if (_fsTripsLv == null) return;
            _fsTripsLv.MouseDown += FsTripsLv_MouseDown_TripDrag;
            _fsTripsLv.MouseMove += FsTripsLv_MouseMove_TripDrag;
            _fsTripsLv.MouseUp += FsTripsLv_MouseUp_TripDrag;
        }

        private bool FsTripsAllowsTripDrag()
        {
            if (!_fsHasPreview || _fsTripsLv == null) return false;
            if (string.IsNullOrWhiteSpace(_fsActiveDriverTab)) return false;
            if (_fsActiveDriverTab.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
                return false;
            return _fsLinesByTab.TryGetValue(_fsActiveDriverTab, out var lines)
                && lines != null
                && lines.Exists(l => l?.Kind == ScheduleBuilderPreviewLine.LineKind.Trip);
        }

        private void FsTripsLv_MouseDown_TripDrag(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || !FsTripsAllowsTripDrag()) return;
            var hit = _fsTripsLv.HitTest(e.Location);
            if (hit.Item?.Tag is FsPreviewTripTag tag && tag.Trip != null)
            {
                _fsTripDragPending = true;
                _fsTripDragActive = false;
                _fsTripDragStartPt = e.Location;
                _fsTripDragTag = tag;
                _fsTripDragSourceItem = hit.Item;
                _fsTripDragSourceItemIndex = hit.Item.Index;
            }
        }

        private void FsTripsLv_MouseMove_TripDrag(object sender, MouseEventArgs e)
        {
            if (!_fsTripDragPending && !_fsTripDragActive) return;
            if ((Control.MouseButtons & MouseButtons.Left) == 0)
            {
                CancelFsTripDrag();
                return;
            }

            _fsTripDragLastClientPt = e.Location;
            _fsTripDragDecorPainted = false;

            if (!_fsTripDragActive)
            {
                int dx = Math.Abs(e.X - _fsTripDragStartPt.X);
                int dy = Math.Abs(e.Y - _fsTripDragStartPt.Y);
                if (dx < 5 && dy < 5) return;
                BeginFsTripDrag(e.Location);
            }

            UpdateFsTripDragTarget(e.Location);
            if (_fsTripDragActive)
                _fsTripsLv.Invalidate(_fsTripsLv.ClientRectangle);
        }

        private void FsTripsLv_MouseUp_TripDrag(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (_fsTripDragActive)
            {
                CommitFsTripDrag();
                CancelFsTripDrag();
                return;
            }
            CancelFsTripDrag();
        }

        private void BeginFsTripDrag(Point clientPt)
        {
            _fsTripDragActive = true;
            _fsTripDragPending = false;
            _fsTripDragRowHeight = GetFsTripDragRowHeight();
            _fsTripDragLastPreviewKey = int.MinValue;
            _fsTripDragLastClientPt = clientPt;
            _fsTripDragDecorPainted = false;
            _fsTripsLv.Capture = true;
            _fsTripsLv.Cursor = Cursors.SizeAll;

            UpdateFsTripDragTarget(clientPt);
            _fsTripsLv.Invalidate();
        }

        private void UpdateFsTripDragTarget(Point clientPt)
        {
            if (!_fsTripDragActive || _fsTripsLv == null) return;

            if (!TryResolveFsTripDropTarget(clientPt, out int insertLine, out bool merge, out FsPreviewTripTag target))
            {
                if (_fsTripDragInsertLine >= 0 || _fsTripDragMerge)
                {
                    _fsTripDragInsertLine = -1;
                    _fsTripDragInsertItemIndex = -1;
                    _fsTripDragMerge = false;
                    _fsTripDragDropTarget = null;
                    _fsTripDragLastPreviewKey = int.MinValue;
                    _fsTripDragDecorPainted = false;
                }
                return;
            }

            _fsTripDragInsertLine = insertLine;
            _fsTripDragMerge = merge;
            _fsTripDragDropTarget = target;
            _fsTripDragInsertItemIndex = ResolveFsInsertListViewIndex(insertLine, merge, target);

            if (!merge
                && TryResolveFsTripDropTarget(clientPt, out insertLine, out merge, out target))
            {
                _fsTripDragInsertLine = insertLine;
                _fsTripDragMerge = merge;
                _fsTripDragDropTarget = target;
                _fsTripDragInsertItemIndex = ResolveFsInsertListViewIndex(insertLine, merge, target);
            }

            int previewKey = (_fsTripDragInsertItemIndex + 1) * 2 + (_fsTripDragMerge ? 1 : 0);
            if (previewKey != _fsTripDragLastPreviewKey)
            {
                _fsTripDragLastPreviewKey = previewKey;
                _fsTripDragDecorPainted = false;
            }
        }

        private bool TryResolveFsTripDropTarget(
            Point clientPt,
            out int insertLine,
            out bool merge,
            out FsPreviewTripTag target)
        {
            return ScheduleBuilderPreviewDrag.TryGetDropTarget(
                _fsTripsLv,
                clientPt,
                _fsTripDragTag,
                dragActive: true,
                dragSourceItemIndex: _fsTripDragSourceItemIndex,
                dragRowHeight: _fsTripDragRowHeight,
                dragInsertItemIndex: _fsTripDragInsertItemIndex,
                dragMerge: _fsTripDragMerge,
                out insertLine,
                out merge,
                out target);
        }

        private int ResolveFsInsertListViewIndex(int insertLine, bool merge, FsPreviewTripTag targetTrip)
        {
            if (_fsTripsLv == null) return -1;

            if (merge && targetTrip != null)
            {
                for (int i = 0; i < _fsTripsLv.Items.Count; i++)
                {
                    if (_fsTripsLv.Items[i].Tag is FsPreviewTripTag t
                        && ReferenceEquals(t.Trip, targetTrip.Trip))
                        return i;
                }
            }

            int line = 0;
            for (int i = 0; i < _fsTripsLv.Items.Count; i++)
            {
                var item = _fsTripsLv.Items[i];
                if (item.Tag is FsPreviewNoteTag || item.Tag is FsPreviewSectionHeaderTag)
                    continue;

                if (item.Tag is FsPreviewGapTag || item.Tag is FsPreviewTripTag)
                {
                    if (line == insertLine)
                        return i;
                    line++;
                }
            }

            return _fsTripsLv.Items.Count;
        }

        private Rectangle? GetFsTripDragInsertSlotRect()
        {
            if (_fsTripsLv == null || _fsTripDragInsertLine < 0) return null;

            if (_fsTripDragMerge && _fsTripDragInsertItemIndex >= 0
                && _fsTripDragInsertItemIndex < _fsTripsLv.Items.Count)
            {
                var bounds = _fsTripsLv.Items[_fsTripDragInsertItemIndex].GetBounds(ItemBoundsPortion.Entire);
                return new Rectangle(0, bounds.Top, _fsTripsLv.ClientSize.Width, bounds.Height);
            }

            if (_fsTripDragInsertItemIndex >= 0 && _fsTripDragInsertItemIndex < _fsTripsLv.Items.Count)
            {
                var bounds = _fsTripsLv.Items[_fsTripDragInsertItemIndex].GetBounds(ItemBoundsPortion.Entire);
                return new Rectangle(0, bounds.Top, _fsTripsLv.ClientSize.Width, bounds.Height);
            }

            int y = 0;
            if (_fsTripsLv.Items.Count > 0)
            {
                var last = _fsTripsLv.Items[_fsTripsLv.Items.Count - 1];
                y = last.GetBounds(ItemBoundsPortion.Entire).Bottom;
            }
            return new Rectangle(0, y, _fsTripsLv.ClientSize.Width, _fsTripDragRowHeight);
        }

        private int GetFsTripDragRowHeight()
        {
            if (_fsTripsLv?.Items.Count > 0)
                return Math.Max(_fsTripsLv.Items[0].GetBounds(ItemBoundsPortion.Entire).Height, 22);
            return 24;
        }

        internal int FsTripsGetDragBumpPixels(int itemIndex)
        {
            if (!_fsTripDragActive || _fsTripDragMerge || _fsTripDragInsertItemIndex < 0)
                return 0;
            return ScheduleBuilderPreviewDrag.GetVisualBumpPixels(
                _fsTripsLv,
                itemIndex,
                _fsTripDragSourceItemIndex,
                _fsTripDragInsertItemIndex,
                _fsTripDragRowHeight);
        }

        private void CancelFsTripDrag()
        {
            _fsTripDragPending = false;
            _fsTripDragActive = false;
            _fsTripDragTag = null;
            _fsTripDragSourceItem = null;
            _fsTripDragDropTarget = null;
            _fsTripDragSourceItemIndex = -1;
            _fsTripDragInsertLine = -1;
            _fsTripDragInsertItemIndex = -1;
            _fsTripDragLastPreviewKey = int.MinValue;
            _fsTripDragDecorPainted = false;
            if (_fsTripsLv != null)
            {
                _fsTripsLv.Capture = false;
                _fsTripsLv.Cursor = Cursors.Default;
                _fsTripsLv.Invalidate();
            }
        }

        private void CommitFsTripDrag()
        {
            if (_fsTripDragTag?.Trip == null || _fsTripDragInsertLine < 0) return;
            if (string.IsNullOrWhiteSpace(_fsActiveDriverTab)) return;

            string tab = _fsActiveDriverTab;
            var lines = ScheduleBuilderPreviewDrag.ParseLinesFromListView(_fsTripsLv);
            ScheduleBuilderPreviewDrag.ApplyTripMove(
                lines,
                _fsTripDragTag.Trip,
                _fsTripDragDropTarget?.Trip,
                _fsTripDragInsertLine,
                _fsTripDragMerge);

            lines = ScheduleBuilderTemplateSlots.CollapseConsecutivePreviewGaps(lines);
            _fsLinesByTab[tab] = lines;

            if (fsbuilder?.PreviewDriverLines != null)
            {
                var dict = fsbuilder.PreviewDriverLines as Dictionary<string, List<ScheduleBuilderPreviewLine>>;
                if (dict != null)
                    dict[tab] = lines;
            }

            if (fsbuilder?.driverTripList != null)
            {
                var trips = new List<MCDownloadedTrip>();
                foreach (var line in lines)
                {
                    if (line?.Kind == ScheduleBuilderPreviewLine.LineKind.Trip && line.Trip != null)
                        trips.Add(line.Trip);
                }
                fsbuilder.driverTripList[tab] = trips;
            }

            ShowFsTripsForTab(tab);
            _ = RefreshFsMapForCurrentTabAsync();
            SetScheduleBuilderStatus(_fsTripDragMerge
                ? "Merged trip into group — map updating…"
                : "Moved trip — map updating…");
        }

        internal bool FsTripsIsDragSourceRow(ListViewItem item)
        {
            return _fsTripDragActive
                && item?.Tag is FsPreviewTripTag t
                && _fsTripDragTag != null
                && ReferenceEquals(t.Trip, _fsTripDragTag.Trip);
        }

        internal bool FsTripsIsDragMergeTargetRow(ListViewItem item)
        {
            return _fsTripDragActive
                && _fsTripDragMerge
                && item != null
                && item.Index == _fsTripDragInsertItemIndex;
        }

        /// <summary>Owner-draw hook: paint insert gap, append slot, and cursor ghost once per frame.</summary>
        internal void FsTripsPaintDragDecorations(Graphics g, ListViewItem item, int columnIndex, Rectangle cellBounds, int bump)
        {
            if (!_fsTripDragActive || _fsTripDragSourceItem == null || columnIndex != 0)
                return;

            if (!_fsTripDragDecorPainted)
            {
                _fsTripDragDecorPainted = true;
                FsTripsPaintAppendInsertSlot(g);
            }

            if (!_fsTripDragMerge
                && _fsTripDragInsertItemIndex >= 0
                && _fsTripDragInsertItemIndex < _fsTripsLv.Items.Count
                && item.Index == _fsTripDragInsertItemIndex
                && bump > 0)
            {
                var gap = new Rectangle(0, cellBounds.Y - bump, _fsTripsLv.ClientSize.Width, bump);
                PaintFsTripDragRow(g, gap, slotHighlight: true);
            }
        }

        internal void FsTripsPaintDragMergeCell(DrawListViewSubItemEventArgs e)
        {
            if (_fsTripDragSourceItem == null || _fsTripsLv == null) return;

            SupeyTripCluster grp = _fsTripDragDropTarget?.Group ?? _fsTripDragTag?.Group;
            PaintFsTripDragCell(
                e.Graphics,
                e.Bounds,
                e.ColumnIndex,
                grp,
                slotHighlight: true);
        }

        private void FsTripsPaintAppendInsertSlot(Graphics g)
        {
            if (_fsTripDragMerge || _fsTripDragInsertLine < 0) return;
            if (_fsTripDragInsertItemIndex < _fsTripsLv.Items.Count) return;

            var slot = GetFsTripDragInsertSlotRect();
            if (slot.HasValue)
                PaintFsTripDragRow(g, slot.Value, slotHighlight: true);
        }

        internal void FsTripsPostPaintDragCursor(Graphics g)
        {
            if (!_fsTripDragActive || _fsTripDragSourceItem == null || _fsTripsLv == null)
                return;

            int rowH = _fsTripDragRowHeight;
            int y = _fsTripDragLastClientPt.Y - rowH / 2;
            y = Math.Max(0, Math.Min(y, _fsTripsLv.ClientSize.Height - rowH));
            var row = new Rectangle(0, y, _fsTripsLv.ClientSize.Width, rowH);
            PaintFsTripDragRow(g, row, slotHighlight: false);
        }

        private SupeyTripCluster FsTripDragPreviewGroup()
        {
            if (_fsTripDragMerge && _fsTripDragDropTarget?.Group != null)
                return _fsTripDragDropTarget.Group;
            return _fsTripDragTag?.Group;
        }

        private void PaintFsTripDragRow(Graphics g, Rectangle rowBounds, bool slotHighlight)
        {
            if (_fsTripDragSourceItem == null || _fsTripsLv == null) return;

            SupeyTripCluster grp = FsTripDragPreviewGroup();
            int x = rowBounds.X;
            for (int c = 0; c < _fsTripsLv.Columns.Count && c < _fsTripDragSourceItem.SubItems.Count; c++)
            {
                int colW = _fsTripsLv.Columns[c].Width;
                var cell = new Rectangle(x, rowBounds.Y, colW, rowBounds.Height);
                PaintFsTripDragCell(g, cell, c, grp, slotHighlight);
                x += colW;
            }

            if (slotHighlight)
            {
                using (var pen = new Pen(
                    _fsTripDragMerge ? SupeyTheme.AccentStripe : SupeyTheme.AccentPrimary, 2f))
                    g.DrawRectangle(pen, rowBounds.X + 1, rowBounds.Y + 1, rowBounds.Width - 3, rowBounds.Height - 3);
            }
        }

        private void PaintFsTripDragCell(
            Graphics g,
            Rectangle cellBounds,
            int columnIndex,
            SupeyTripCluster grp,
            bool slotHighlight)
        {
            bool useColors = FsShowGroupColorsEnabled;
            Color fill = SupeyTheme.ListBody;
            if (useColors && columnIndex == 0 && grp != null)
                fill = grp.GroupColor;
            else if (useColors && columnIndex > 0 && grp != null && slotHighlight)
                fill = FsRouteHeaderBackColor(grp.GroupColor);

            using (var br = new SolidBrush(fill))
                g.FillRectangle(br, cellBounds);

            string text = _fsTripDragSourceItem.SubItems[columnIndex].Text ?? "";
            if (useColors && columnIndex == 0 && grp != null)
                text = grp.GroupNumber.ToString();

            var textRect = new Rectangle(cellBounds.Left + 6, cellBounds.Top, cellBounds.Width - 8, cellBounds.Height);
            TextRenderer.DrawText(g, text, _fsTripsLv.Font, textRect, SupeyTheme.ListText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);

            SupeyListViewHelpers.DrawCellGridLines(g, cellBounds);
        }
    }
}
