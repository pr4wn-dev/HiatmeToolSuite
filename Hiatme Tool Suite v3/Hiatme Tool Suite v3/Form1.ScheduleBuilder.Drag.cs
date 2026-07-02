using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    public partial class Form1
    {
        /// <summary>Trip list drag-and-drop reorder/merge — off until re-enabled.</summary>
        private const bool FsTripDragDropEnabled = false;

        private static void EnableFsControlDoubleBuffer(Control control)
        {
            if (control == null)
                return;

            typeof(Control).InvokeMember(
                "DoubleBuffered",
                System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.SetProperty,
                null,
                control,
                new object[] { true });
        }

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
            return FsTripDragDropEnabled
                && _fsHasPreview
                && _fsTripsLv != null
                && !string.IsNullOrWhiteSpace(_fsActiveDriverTab)
                && !_fsActiveDriverTab.Equals("Reserves", StringComparison.OrdinalIgnoreCase)
                && _fsLinesByTab.TryGetValue(_fsActiveDriverTab, out var lines)
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
                if (item.Tag is FsPreviewNoteTag)
                    continue;

                if (item.Tag is FsPreviewGapTag
                    || item.Tag is FsPreviewTripTag
                    || item.Tag is FsPreviewSectionHeaderTag)
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
            MCDownloadedTrip movedTrip = _fsTripDragTag.Trip;

            _fsPreserveRouteChangeBaseline = true;
            FsSnapshotPreMoveGroupMeters(tab, movedTrip, _fsTripDragMerge, _fsTripDragDropTarget?.Trip);

            var lines = ScheduleBuilderPreviewDrag.ParseLinesFromListView(_fsTripsLv);
            FsPushUndoSnapshot(_fsTripDragMerge ? "merge trip" : "move trip");
            ScheduleBuilderPreviewDrag.ApplyTripMove(
                lines,
                _fsTripDragTag.Trip,
                _fsTripDragDropTarget?.Trip,
                _fsTripDragInsertLine,
                _fsTripDragMerge);

            FsCommitPreviewLinesForTab(tab, lines);

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
            SelectFsTripInListView(movedTrip);
            _ = RefreshFsMapAndMileageHudAsync();
            SetScheduleBuilderStatus(_fsTripDragMerge
                ? "Merged trip into group — map updating…"
                : "Moved trip — map updating…");
        }

        private async Task RefreshFsMapAndMileageHudAsync()
        {
            await RefreshFsMapForCurrentTabAsync().ConfigureAwait(true);
            FsTripsLv_SelectionChangedUpdateMap();
        }

        internal void FsSelectGroupInListView(int groupNumber)
        {
            if (_fsTripsLv == null || groupNumber <= 0) return;

            foreach (ListViewItem item in _fsTripsLv.Items)
            {
                if (item.Tag is FsPreviewTripTag row
                    && row.Trip != null
                    && row.Group != null
                    && row.Group.GroupNumber == groupNumber)
                {
                    _fsTripsLv.SelectedItems.Clear();
                    item.Selected = true;
                    item.Focused = true;
                    item.EnsureVisible();
                    return;
                }
            }

            foreach (ListViewItem item in _fsTripsLv.Items)
            {
                if (item.Tag is FsPreviewNoteTag note
                    && note.Group != null
                    && note.Group.GroupNumber == groupNumber)
                {
                    _fsTripsLv.SelectedItems.Clear();
                    item.Selected = true;
                    item.Focused = true;
                    item.EnsureVisible();
                    return;
                }
            }
        }

        private void SelectFsTripInListView(MCDownloadedTrip trip)
        {
            if (_fsTripsLv == null || trip == null) return;

            _fsTripsLv.SelectedItems.Clear();
            foreach (ListViewItem item in _fsTripsLv.Items)
            {
                if (item.Tag is FsPreviewTripTag tag && tag.Trip != null
                    && (ReferenceEquals(tag.Trip, trip)
                        || (!string.IsNullOrEmpty(trip.TripNumber)
                            && string.Equals(tag.Trip.TripNumber, trip.TripNumber, StringComparison.OrdinalIgnoreCase))))
                {
                    item.Selected = true;
                    item.Focused = true;
                    item.EnsureVisible();
                    return;
                }
            }
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
                fill = grp.DisplayColor;
            else if (useColors && columnIndex > 0 && grp != null && slotHighlight)
                fill = FsRouteHeaderBackColor(grp.DisplayColor);

            using (var br = new SolidBrush(fill))
                g.FillRectangle(br, cellBounds);

            if (columnIndex < 0 || columnIndex >= _fsTripDragSourceItem.SubItems.Count)
                return;

            string text = _fsTripDragSourceItem.SubItems[columnIndex].Text ?? "";
            if (useColors && columnIndex == 0 && grp != null)
                text = grp.GroupNumber.ToString();

            var textRect = new Rectangle(cellBounds.Left + 6, cellBounds.Top, cellBounds.Width - 8, cellBounds.Height);
            TextFormatFlags align = TextFormatFlags.Left;
            if (columnIndex >= 0 && columnIndex < _fsTripsLv.Columns.Count)
            {
                switch (_fsTripsLv.Columns[columnIndex].TextAlign)
                {
                    case HorizontalAlignment.Right:
                        align = TextFormatFlags.Right;
                        break;
                    case HorizontalAlignment.Center:
                        align = TextFormatFlags.HorizontalCenter;
                        break;
                }
            }
            TextRenderer.DrawText(g, text, _fsTripsLv.Font, textRect, SupeyTheme.ListText,
                align | TextFormatFlags.VerticalCenter
                | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);

            SupeyListViewHelpers.DrawCellGridLines(g, cellBounds, _fsTripsLv);
        }

        private void FsDriverTabButton_MouseDown_Reorder(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || !_fsHasPreview || _fsDriverTabFlow == null)
                return;

            if (!(sender is SupeyButton btn) || !(btn.Tag is string name))
                return;

            FsEndDriverTabReorder();

            _fsTabReorderButton = btn;
            _fsTabReorderSourceName = name;
            _fsTabReorderStartScreen = Control.MousePosition;
            _fsTabReorderFromIndex = _fsDriverTabOrder.FindIndex(t =>
                t.Equals(name, StringComparison.OrdinalIgnoreCase));
            _fsTabReorderInsertIndex = -1;
            _fsTabReorderDragging = false;

            btn.Capture = true;
            btn.MouseMove += FsDriverTabButton_MouseMove_Reorder;
            btn.MouseUp += FsDriverTabButton_MouseUp_Reorder;
        }

        private void FsDriverTabButton_MouseMove_Reorder(object sender, MouseEventArgs e)
        {
            if (_fsTabReorderButton == null || _fsTabReorderFromIndex < 0)
                return;

            if ((Control.MouseButtons & MouseButtons.Left) == 0)
            {
                FsEndDriverTabReorder();
                return;
            }

            var screen = Control.MousePosition;
            if (!_fsTabReorderDragging)
            {
                int dx = Math.Abs(screen.X - _fsTabReorderStartScreen.X);
                int dy = Math.Abs(screen.Y - _fsTabReorderStartScreen.Y);
                if (dx < 5 && dy < 5)
                    return;

                _fsTabReorderDragging = true;
                _fsDriverTabSuppressClick = true;
                _fsTabReorderButton.Cursor = Cursors.SizeAll;
                BeginFsTabReorderVisual(_fsTabReorderButton);
            }

            _fsTabReorderInsertIndex = ComputeFsDriverTabInsertIndex(screen);
            UpdateFsTabReorderVisual(screen, _fsTabReorderInsertIndex);
        }

        private void FsDriverTabButton_MouseUp_Reorder(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;

            try
            {
                if (_fsTabReorderDragging && _fsTabReorderFromIndex >= 0 && _fsTabReorderInsertIndex >= 0)
                    CommitFsDriverTabReorder(_fsTabReorderFromIndex, _fsTabReorderInsertIndex);
            }
            finally
            {
                FsEndDriverTabReorder();
            }
        }

        private void FsEndDriverTabReorder()
        {
            bool wasDragging = _fsTabReorderDragging;
            string activeTab = _fsActiveDriverTab;

            EndFsTabReorderVisual();

            if (_fsTabReorderButton != null)
            {
                _fsTabReorderButton.Capture = false;
                _fsTabReorderButton.Cursor = Cursors.Hand;
                _fsTabReorderButton.MouseMove -= FsDriverTabButton_MouseMove_Reorder;
                _fsTabReorderButton.MouseUp -= FsDriverTabButton_MouseUp_Reorder;
            }

            if (_fsDriverTabFlow != null)
                _fsDriverTabFlow.Cursor = Cursors.Default;

            _fsTabReorderButton = null;
            _fsTabReorderSourceName = null;
            _fsTabReorderFromIndex = -1;
            _fsTabReorderInsertIndex = -1;
            _fsTabReorderVisualGapIndex = -1;
            _fsTabReorderDragLayoutReady = false;
            _fsTabReorderGhostRaised = false;
            _fsTabReorderGhostLastLocation = new Point(int.MinValue, int.MinValue);
            _fsTabReorderDragging = false;

            if (wasDragging)
            {
                RebuildFsDriverTabs();
                if (!string.IsNullOrWhiteSpace(activeTab))
                    SelectFsDriverTab(activeTab);
            }
        }

        private void BeginFsTabReorderVisual(SupeyButton source)
        {
            if (source == null || _fsDriverTabFlow == null)
                return;

            EndFsTabReorderVisual();

            var sourceScreen = source.PointToScreen(Point.Empty);
            _fsTabReorderGhostOffset = new Point(
                Control.MousePosition.X - sourceScreen.X,
                0);
            _fsTabReorderGhostAnchorScreenY = sourceScreen.Y;

            if (_fsDriverTabStrip == null)
                return;

            _fsTabReorderGhost = new SupeyButton
            {
                Text = source.Text,
                Tag = source.Tag,
                Kind = source.Kind,
                Size = source.Size,
                BackColor = SupeyTheme.SurfaceHeader,
                Enabled = false,
            };
            _fsDriverTabStrip.Controls.Add(_fsTabReorderGhost);

            _fsTabReorderDropIndicator = new Panel
            {
                Size = source.Size,
                BackColor = Color.Transparent,
                Visible = false,
            };
            EnableFsControlDoubleBuffer(_fsTabReorderDropIndicator);
            _fsTabReorderDropIndicator.Paint += FsTabReorderSpacer_PaintGap;
            _fsDriverTabStrip.Controls.Add(_fsTabReorderDropIndicator);

            _fsTabReorderSpacer = new Panel
            {
                Size = source.Size,
                Margin = source.Margin,
                BackColor = SupeyTheme.SurfaceHeader,
            };

            _fsTabReorderDragLayoutReady = false;
            _fsTabReorderGhostRaised = false;
            _fsTabReorderGhostLastLocation = new Point(int.MinValue, int.MinValue);

            if (source.Parent != null)
                source.Parent.Controls.Remove(source);
            source.Visible = false;
            ApplyFsDriverTabStripDuringDrag(VisualInsertToOrderIndex(_fsTabReorderFromIndex));
            SyncFsTabReorderDropIndicator();
            UpdateFsTabReorderGhostPosition(Control.MousePosition);
            _fsTabReorderGhost?.BringToFront();
        }

        private void SyncFsTabReorderDropIndicator()
        {
            if (_fsTabReorderDropIndicator == null || _fsDriverTabStrip == null
                || _fsTabReorderSpacer?.Parent == null)
            {
                if (_fsTabReorderDropIndicator != null)
                    _fsTabReorderDropIndicator.Visible = false;
                return;
            }

            var screen = _fsTabReorderSpacer.PointToScreen(Point.Empty);
            var stripPt = _fsDriverTabStrip.PointToClient(screen);
            _fsTabReorderDropIndicator.SetBounds(
                stripPt.X,
                stripPt.Y,
                _fsTabReorderSpacer.Width,
                _fsTabReorderSpacer.Height);
            _fsTabReorderDropIndicator.Visible = true;
            _fsTabReorderDropIndicator.Invalidate();
        }

        private static void FsTabReorderSpacer_PaintGap(object sender, PaintEventArgs e)
        {
            var panel = sender as Panel;
            if (panel == null)
                return;

            var rect = new Rectangle(1, 1, panel.Width - 3, panel.Height - 3);
            if (rect.Width <= 0 || rect.Height <= 0)
                return;

            if (panel.BackColor.A == 255)
                e.Graphics.Clear(panel.BackColor);

            using (var fill = new SolidBrush(Color.FromArgb(40, SupeyTheme.AccentPrimary)))
                e.Graphics.FillRectangle(fill, rect);
            using (var pen = new Pen(SupeyTheme.AccentPrimary) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot })
                e.Graphics.DrawRectangle(pen, rect);
        }

        private void UpdateFsTabReorderGhostPosition(Point screenPt)
        {
            if (_fsTabReorderGhost == null || _fsDriverTabStrip == null)
                return;

            var stripPt = _fsDriverTabStrip.PointToClient(screenPt);
            var anchorStripPt = _fsDriverTabStrip.PointToClient(
                new Point(screenPt.X, _fsTabReorderGhostAnchorScreenY));

            var next = new Point(
                stripPt.X - _fsTabReorderGhostOffset.X,
                anchorStripPt.Y);

            if (next == _fsTabReorderGhostLastLocation)
                return;

            _fsTabReorderGhostLastLocation = next;
            _fsTabReorderGhost.Location = next;

            if (!_fsTabReorderGhostRaised)
            {
                _fsTabReorderGhost.BringToFront();
                _fsTabReorderGhostRaised = true;
            }
        }

        private void UpdateFsTabReorderVisual(Point screenPt, int insertIndex)
        {
            UpdateFsTabReorderGhostPosition(screenPt);

            int visualGap = OrderIndexToVisualInsert(insertIndex);
            if (visualGap == _fsTabReorderVisualGapIndex)
                return;

            _fsTabReorderVisualGapIndex = visualGap;
            ApplyFsDriverTabStripDuringDrag(insertIndex);
            SyncFsTabReorderDropIndicator();
            _fsTabReorderGhost?.BringToFront();
        }

        private void ApplyFsDriverTabStripDuringDrag(int insertIndex)
        {
            if (_fsDriverTabFlow == null || _fsTabReorderSpacer == null
                || string.IsNullOrWhiteSpace(_fsTabReorderSourceName))
                return;

            int gapIndex = OrderIndexToVisualInsert(insertIndex);
            int visibleCount = _fsDriverTabOrder.Count - 1;
            gapIndex = Math.Max(0, Math.Min(gapIndex, visibleCount));

            if (!_fsTabReorderDragLayoutReady)
            {
                BuildFsDriverTabStripDuringDrag(gapIndex);
                _fsTabReorderDragLayoutReady = true;
                return;
            }

            MoveFsDriverTabSpacerTo(gapIndex);
        }

        private void BuildFsDriverTabStripDuringDrag(int gapIndex)
        {
            var visible = GetVisibleTabNamesDuringDrag();
            gapIndex = Math.Max(0, Math.Min(gapIndex, visible.Count));

            _fsDriverTabFlow.SuspendLayout();
            try
            {
                _fsDriverTabFlow.Controls.Clear();

                for (int i = 0; i < visible.Count; i++)
                {
                    if (i == gapIndex)
                        _fsDriverTabFlow.Controls.Add(_fsTabReorderSpacer);

                    if (_fsDriverTabButtons.TryGetValue(visible[i], out SupeyButton btn))
                        _fsDriverTabFlow.Controls.Add(btn);
                }

                if (gapIndex >= visible.Count)
                    _fsDriverTabFlow.Controls.Add(_fsTabReorderSpacer);
            }
            finally
            {
                _fsDriverTabFlow.ResumeLayout(true);
            }
        }

        private void MoveFsDriverTabSpacerTo(int gapIndex)
        {
            if (_fsTabReorderSpacer.Parent != _fsDriverTabFlow)
                return;

            int targetIndex = Math.Max(0, Math.Min(gapIndex, _fsDriverTabFlow.Controls.Count - 1));
            int currentIndex = _fsDriverTabFlow.Controls.GetChildIndex(_fsTabReorderSpacer);
            if (currentIndex == targetIndex)
                return;

            _fsDriverTabFlow.SuspendLayout();
            try
            {
                _fsDriverTabFlow.Controls.SetChildIndex(_fsTabReorderSpacer, targetIndex);
            }
            finally
            {
                _fsDriverTabFlow.ResumeLayout(true);
            }
        }

        private List<string> GetVisibleTabNamesDuringDrag()
        {
            var visible = new List<string>();
            foreach (string name in _fsDriverTabOrder)
            {
                if (name.Equals(_fsTabReorderSourceName, StringComparison.OrdinalIgnoreCase))
                    continue;
                visible.Add(name);
            }

            return visible;
        }

        private int OrderIndexToVisualInsert(int orderInsertIndex)
        {
            if (_fsDriverTabOrder == null || string.IsNullOrWhiteSpace(_fsTabReorderSourceName))
                return 0;

            int visual = 0;
            for (int i = 0; i < _fsDriverTabOrder.Count && i < orderInsertIndex; i++)
            {
                if (!_fsDriverTabOrder[i].Equals(_fsTabReorderSourceName, StringComparison.OrdinalIgnoreCase))
                    visual++;
            }

            return visual;
        }

        private int VisualInsertToOrderIndex(int visualInsert)
        {
            if (_fsDriverTabOrder == null || string.IsNullOrWhiteSpace(_fsTabReorderSourceName))
                return 0;

            int seen = 0;
            for (int i = 0; i < _fsDriverTabOrder.Count; i++)
            {
                if (_fsDriverTabOrder[i].Equals(_fsTabReorderSourceName, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (seen == visualInsert)
                    return i;
                seen++;
            }

            return _fsDriverTabOrder.Count;
        }

        private void EndFsTabReorderVisual()
        {
            if (_fsTabReorderGhost != null)
            {
                if (_fsTabReorderGhost.Parent != null)
                    _fsTabReorderGhost.Parent.Controls.Remove(_fsTabReorderGhost);
                _fsTabReorderGhost.Dispose();
                _fsTabReorderGhost = null;
            }

            if (_fsTabReorderSpacer != null)
            {
                if (_fsTabReorderSpacer.Parent != null)
                    _fsTabReorderSpacer.Parent.Controls.Remove(_fsTabReorderSpacer);
                _fsTabReorderSpacer.Dispose();
                _fsTabReorderSpacer = null;
            }

            if (_fsTabReorderDropIndicator != null)
            {
                _fsTabReorderDropIndicator.Paint -= FsTabReorderSpacer_PaintGap;
                if (_fsTabReorderDropIndicator.Parent != null)
                    _fsTabReorderDropIndicator.Parent.Controls.Remove(_fsTabReorderDropIndicator);
                _fsTabReorderDropIndicator.Dispose();
                _fsTabReorderDropIndicator = null;
            }

            _fsTabReorderVisualGapIndex = -1;
            _fsTabReorderDragLayoutReady = false;
            _fsTabReorderGhostRaised = false;
            _fsTabReorderGhostLastLocation = new Point(int.MinValue, int.MinValue);
        }

        private int ComputeFsDriverTabInsertIndex(Point screenPt)
        {
            if (_fsDriverTabFlow == null || _fsDriverTabOrder == null || _fsDriverTabOrder.Count == 0)
                return 0;

            var client = _fsDriverTabFlow.PointToClient(screenPt);
            var layoutPt = new Point(
                client.X - _fsDriverTabFlow.AutoScrollPosition.X,
                client.Y - _fsDriverTabFlow.AutoScrollPosition.Y);

            var buttons = GetVisibleTabButtonsInFlowOrder();
            if (buttons.Count == 0)
                return 0;

            if (layoutPt.X < buttons[0].Left)
                return VisualInsertToOrderIndex(0);

            for (int i = 0; i < buttons.Count; i++)
            {
                var btn = buttons[i];

                if (i > 0)
                {
                    int gapLeft = buttons[i - 1].Right + buttons[i - 1].Margin.Right;
                    int gapRight = btn.Left;
                    if (layoutPt.X >= gapLeft && layoutPt.X < gapRight)
                        return VisualInsertToOrderIndex(i);
                }

                if (layoutPt.X >= btn.Left && layoutPt.X < btn.Right)
                {
                    int mid = btn.Left + btn.Width / 2;
                    return VisualInsertToOrderIndex(layoutPt.X < mid ? i : i + 1);
                }
            }

            return _fsDriverTabOrder.Count;
        }

        private List<SupeyButton> GetVisibleTabButtonsInFlowOrder()
        {
            var list = new List<SupeyButton>();
            if (_fsDriverTabFlow == null)
                return list;

            foreach (Control c in _fsDriverTabFlow.Controls)
            {
                if (c is SupeyButton btn && btn.Tag is string name
                    && !name.Equals(_fsTabReorderSourceName, StringComparison.OrdinalIgnoreCase))
                {
                    list.Add(btn);
                }
            }

            return list;
        }

        private bool CommitFsDriverTabReorder(int fromIndex, int insertIndex)
        {
            if (fromIndex < 0 || fromIndex >= _fsDriverTabOrder.Count)
                return false;

            insertIndex = Math.Max(0, Math.Min(insertIndex, _fsDriverTabOrder.Count));
            if (insertIndex == fromIndex || insertIndex == fromIndex + 1)
                return false;

            string item = _fsDriverTabOrder[fromIndex];
            _fsDriverTabOrder.RemoveAt(fromIndex);
            if (fromIndex < insertIndex)
                insertIndex--;

            insertIndex = Math.Max(0, Math.Min(insertIndex, _fsDriverTabOrder.Count));
            _fsDriverTabOrder.Insert(insertIndex, item);

            fsbuilder?.SetTabOrder(_fsDriverTabOrder);
            SetScheduleBuilderStatus("Tab order updated — click SAVE SCHEDULE to write the new order to the workbook.");
            return true;
        }
    }
}
