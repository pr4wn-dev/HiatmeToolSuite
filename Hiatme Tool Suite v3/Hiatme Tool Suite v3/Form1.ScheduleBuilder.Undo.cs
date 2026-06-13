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

        private void FsClearUndoHistory()
        {
            _fsUndoStack.Clear();
        }

        private void FsPushUndoSnapshot(string label)
        {
            if (!_fsHasPreview || string.IsNullOrWhiteSpace(label))
                return;

            _fsUndoStack.Push(new ScheduleBuilderUndoEntry
            {
                Label = label,
                LinesByTab = ScheduleBuilderPreviewUndo.CloneLinesByTab(_fsLinesByTab),
                CutTrip = _fsCutTrip,
                CutTripReserveBand = _fsCutTripReserveBand,
            });
        }

        private void FsUndoScheduleEdit()
        {
            if (!_fsHasPreview || !_fsUndoStack.CanUndo)
                return;

            var entry = _fsUndoStack.Pop();
            if (entry?.LinesByTab == null)
                return;

            _fsLinesByTab.Clear();
            foreach (var kv in entry.LinesByTab)
                _fsLinesByTab[kv.Key] = kv.Value;

            FsApplyAllPreviewLinesFromDictionary(entry.LinesByTab);

            _fsCutTrip = entry.CutTrip;
            _fsCutTripReserveBand = entry.CutTripReserveBand;

            string tab = string.IsNullOrWhiteSpace(_fsActiveDriverTab)
                ? _fsDriverTabOrder?.FirstOrDefault(t => !t.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
                : _fsActiveDriverTab;

            if (!string.IsNullOrWhiteSpace(tab))
                ShowFsTripsForTab(tab);
            else if (_fsDriverTabOrder != null && _fsDriverTabOrder.Count > 0)
                ShowFsTripsForTab(_fsDriverTabOrder[0]);

            SyncFsPreviewCsvsForExport();
            _ = FsUndoRefreshAsync(entry.Label);
        }

        private async Task FsUndoRefreshAsync(string label)
        {
            await RefreshFsMapForCurrentTabAsync().ConfigureAwait(true);
            FsTripsLv_SelectionChangedUpdateMap();
            string text = string.IsNullOrWhiteSpace(label) ? "Undo." : "Undid " + label + ".";
            SetScheduleBuilderStatus(text);
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

        private void FsTripsLv_KeyDown_Undo(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.Z)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                FsUndoScheduleEdit();
            }
        }

        private string FsUndoMenuText()
        {
            if (!_fsUndoStack.CanUndo)
                return "Undo";
            string label = (_fsUndoStack.NextUndoLabel ?? "").Trim();
            return string.IsNullOrEmpty(label) ? "Undo" : "Undo " + label;
        }
    }
}
