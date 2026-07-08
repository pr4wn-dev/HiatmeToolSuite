using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    public partial class Form1
    {
        private readonly ScheduleBuilderPreviewUndoStack _fsUndoStack = new ScheduleBuilderPreviewUndoStack();

        private void FsClearUndoHistory(string reason = null)
        {
            _fsUndoStack.Clear();
            if (!string.IsNullOrWhiteSpace(reason))
                SetScheduleBuilderStatus(reason);
        }

        private ScheduleBuilderUndoEntry FsMakeUndoSnapshot(string label)
        {
            return new ScheduleBuilderUndoEntry
            {
                Label = label,
                LinesByTab = ScheduleBuilderPreviewUndo.CloneLinesByTab(_fsLinesByTab),
                CutTrip = _fsCutTrip,
                CutTripReserveBand = _fsCutTripReserveBand,
            };
        }

        private void FsPushUndoSnapshot(string label)
        {
            if (!_fsHasPreview || string.IsNullOrWhiteSpace(label))
                return;

            _fsUndoStack.PushBeforeEdit(FsMakeUndoSnapshot(label));
        }

        private void FsUndoScheduleEdit()
        {
            if (!_fsHasPreview || !_fsUndoStack.CanUndo)
                return;

            var restore = _fsUndoStack.PopUndo();
            if (restore?.LinesByTab == null)
                return;

            _fsUndoStack.PushRedo(FsMakeUndoSnapshot(restore.Label));
            FsApplyUndoEntry(restore);
            _ = FsHistoryRefreshAsync("Undid " + restore.Label + ".");
        }

        private void FsRedoScheduleEdit()
        {
            if (!_fsHasPreview || !_fsUndoStack.CanRedo)
                return;

            var restore = _fsUndoStack.PopRedo();
            if (restore?.LinesByTab == null)
                return;

            _fsUndoStack.PushUndoCheckpoint(FsMakeUndoSnapshot(restore.Label));
            FsApplyUndoEntry(restore);
            _ = FsHistoryRefreshAsync("Redid " + restore.Label + ".");
        }

        private void FsApplyUndoEntry(ScheduleBuilderUndoEntry entry)
        {
            ReplaceFsLinesByTabFrom(entry.LinesByTab);

            FsApplyAllPreviewLinesFromDictionary(entry.LinesByTab);

            _fsCutTrip = entry.CutTrip;
            _fsCutTripReserveBand = entry.CutTripReserveBand;
            FsUpdateCutTripBar();

            string tab = string.IsNullOrWhiteSpace(_fsActiveDriverTab)
                ? _fsDriverTabOrder?.FirstOrDefault(t => !t.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
                : _fsActiveDriverTab;

            if (!string.IsNullOrWhiteSpace(tab))
                ShowFsTripsForTab(tab);
            else if (_fsDriverTabOrder != null && _fsDriverTabOrder.Count > 0)
                ShowFsTripsForTab(_fsDriverTabOrder[0]);

            SyncFsPreviewCsvsForExport();
        }

        private async Task FsHistoryRefreshAsync(string statusText)
        {
            await RefreshFsMapForCurrentTabAsync().ConfigureAwait(true);
            FsTripsLv_SelectionChangedUpdateMap();
            if (!string.IsNullOrWhiteSpace(statusText))
                SetScheduleBuilderStatus(statusText);
        }

        private void FsApplyAllPreviewLinesFromDictionary(
            Dictionary<string, List<ScheduleBuilderPreviewLine>> linesByTab)
        {
            if (linesByTab == null)
                return;

            foreach (var kv in linesByTab)
            {
                string tab = kv.Key;
                var lines = kv.Value;
                if (lines == null)
                    continue;

                if (tab.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
                {
                    ScheduleBuilderReserveBuckets.ReassignBandsAndRefreshSectionCounts(lines);
                    fsbuilder?.ApplyPreviewReserveLines(lines);
                    continue;
                }

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
            }
        }

        private void FsTripsLv_KeyDown_ScheduleShortcuts(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete && !e.Control)
            {
                if (FsTryPrepareKeyboardDeleteAction())
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    FsDeleteSelection();
                }

                return;
            }

            if (!e.Control)
                return;

            if (e.KeyCode == Keys.Z)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                FsUndoScheduleEdit();
                return;
            }

            if (e.KeyCode == Keys.Y)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                FsRedoScheduleEdit();
                return;
            }

            if (e.KeyCode == Keys.X)
            {
                if (FsTryPrepareKeyboardTripAction(out _))
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    FsCutSelectedTrip();
                }

                return;
            }

            if (e.KeyCode == Keys.V)
            {
                if (FsHasCutTrip && FsTryPrepareKeyboardTripAction(out _))
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    FsInsertFromContextMenu(below: false);
                }
            }
        }

        private bool FsTryPrepareKeyboardDeleteAction()
        {
            if (_fsTripsLv == null || _fsTripsLv.SelectedItems.Count == 0)
                return false;

            var item = _fsTripsLv.SelectedItems[0];
            _fsTripsCtxHitItem = item;
            FsBindContextTagsFromHitItem(item);
            return true;
        }

        private bool FsTryPrepareKeyboardTripAction(out MCDownloadedTrip trip)
        {
            trip = null;
            if (_fsTripsLv == null || _fsTripsLv.SelectedItems.Count == 0)
                return false;

            var item = _fsTripsLv.SelectedItems[0];
            _fsTripsCtxHitItem = item;
            _fsTripsCtxTrip = null;
            _fsTripsCtxGroup = null;

            if (item.Tag is FsPreviewTripTag tripTag && tripTag.Trip != null)
            {
                trip = tripTag.Trip;
                _fsTripsCtxTrip = trip;
                _fsTripsCtxGroup = tripTag.Group;
                return true;
            }

            if (item.Tag is FsPreviewGapTag || item.Tag is FsPreviewNoteTag)
                return true;

            trip = GetFsTripFromListItem(item);
            _fsTripsCtxTrip = trip;
            return trip != null;
        }

        private string FsUndoMenuText()
        {
            if (!_fsUndoStack.CanUndo)
                return "Undo";
            string label = (_fsUndoStack.NextUndoLabel ?? "").Trim();
            return string.IsNullOrEmpty(label) ? "Undo" : "Undo " + label;
        }

        private string FsRedoMenuText()
        {
            if (!_fsUndoStack.CanRedo)
                return "Redo";
            string label = (_fsUndoStack.NextRedoLabel ?? "").Trim();
            return string.IsNullOrEmpty(label) ? "Redo" : "Redo " + label;
        }
    }
}
