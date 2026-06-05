using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    public partial class Form1
    {
        private MCDownloadedTrip _fsCutTrip;
        private ListViewItem _fsTripsCtxHitItem;

        private bool FsHasCutTrip => _fsCutTrip != null;

        private void FsClearCutTrip()
        {
            _fsCutTrip = null;
        }

        private void FsCutSelectedTrip()
        {
            if (_fsTripsCtxTrip == null || string.IsNullOrWhiteSpace(_fsActiveDriverTab) || !_fsHasPreview)
                return;

            string tab = _fsActiveDriverTab;
            if (!_fsLinesByTab.TryGetValue(tab, out var lines) || lines == null)
                return;

            if (!ScheduleBuilderPreviewDrag.TryRemoveTrip(lines, _fsTripsCtxTrip))
                return;

            _fsCutTrip = _fsTripsCtxTrip;
            lines = ScheduleBuilderTemplateSlots.CollapseConsecutivePreviewGaps(lines);
            FsCommitPreviewLinesForTab(tab, lines);
            ShowFsTripsForTab(tab);
            _ = RefreshFsMapForCurrentTabAsync();

            string num = (_fsCutTrip.TripNumber ?? "").Trim();
            SetScheduleBuilderStatus(string.IsNullOrEmpty(num)
                ? "Trip cut — right-click a row · Insert above or Insert below."
                : "Trip " + num + " cut — right-click a row · Insert above or Insert below.");
        }

        private void FsInsertCutTrip(bool below)
        {
            if (_fsCutTrip == null || string.IsNullOrWhiteSpace(_fsActiveDriverTab) || !_fsHasPreview)
                return;

            if (!TryResolveFsInsertBeforeLine(_fsTripsCtxHitItem, below, out int insertBeforeLine))
            {
                SetScheduleBuilderStatus("Could not insert here — right-click a trip, gap, or group row.");
                return;
            }

            string tab = _fsActiveDriverTab;
            MCDownloadedTrip trip = _fsCutTrip;
            FsClearCutTrip();

            _fsPreserveRouteChangeBaseline = true;
            FsSnapshotPreMoveGroupMeters(tab, trip, merge: false, mergeTargetTrip: null);

            if (!_fsLinesByTab.TryGetValue(tab, out var lines) || lines == null)
                return;

            ScheduleBuilderPreviewDrag.InsertTripLine(lines, trip, insertBeforeLine);

            lines = ScheduleBuilderTemplateSlots.CollapseConsecutivePreviewGaps(lines);
            FsCommitPreviewLinesForTab(tab, lines);
            ShowFsTripsForTab(tab);
            SelectFsTripInListView(trip);
            _ = FsRefreshAfterTripMoveAsync();

            string num = (trip.TripNumber ?? "").Trim();
            SetScheduleBuilderStatus(string.IsNullOrEmpty(num)
                ? "Trip inserted — map updating…"
                : "Trip " + num + " inserted — map updating…");
        }

        private async Task FsRefreshAfterTripMoveAsync()
        {
            await RefreshFsMapForCurrentTabAsync().ConfigureAwait(true);
            FsTripsLv_SelectionChangedUpdateMap();
        }

        /// <summary>Line index among trip+gap rows where a cut trip should be inserted (Excel-style).</summary>
        private bool TryResolveFsInsertBeforeLine(ListViewItem item, bool below, out int insertBeforeLine)
        {
            insertBeforeLine = 0;
            if (_fsTripsLv == null)
                return false;

            if (item == null)
            {
                insertBeforeLine = ScheduleBuilderPreviewDrag.CountTripAndGapLines(_fsTripsLv);
                return true;
            }

            if (item.Tag is FsPreviewSectionHeaderTag)
                return false;

            if (item.Tag is FsPreviewNoteTag)
            {
                int firstInGroup = ScheduleBuilderPreviewDrag.ListViewIndexToLineIndex(
                    _fsTripsLv, item.Index + 1, out _, out _);
                if (firstInGroup < 0)
                    firstInGroup = ScheduleBuilderPreviewDrag.CountTripAndGapLines(_fsTripsLv);
                insertBeforeLine = below ? firstInGroup + 1 : firstInGroup;
                return true;
            }

            int line = ScheduleBuilderPreviewDrag.ListViewIndexToLineIndex(
                _fsTripsLv, item.Index, out _, out _);
            if (line < 0)
                return false;

            insertBeforeLine = below ? line + 1 : line;
            return true;
        }

        private bool FsCanInsertCutTripAtContext()
        {
            if (!FsHasCutTrip || !_fsHasPreview || string.IsNullOrWhiteSpace(_fsActiveDriverTab))
                return false;
            if (_fsTripsCtxHitItem == null)
                return ScheduleBuilderPreviewDrag.CountTripAndGapLines(_fsTripsLv) >= 0;
            if (_fsTripsCtxHitItem.Tag is FsPreviewSectionHeaderTag)
                return false;
            return true;
        }
    }
}
