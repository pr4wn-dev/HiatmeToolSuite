using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    public partial class Form1
    {
        private MCDownloadedTrip _fsCutTrip;
        private Color? _fsCutTripReserveBand;
        private ListViewItem _fsTripsCtxHitItem;

        private bool FsHasCutTrip => _fsCutTrip != null;

        private void FsClearCutTrip()
        {
            _fsCutTrip = null;
            _fsCutTripReserveBand = null;
        }

        private void FsCutSelectedTrip()
        {
            if (_fsTripsCtxTrip == null || string.IsNullOrWhiteSpace(_fsActiveDriverTab) || !_fsHasPreview)
                return;

            string tab = _fsActiveDriverTab;
            if (!_fsLinesByTab.TryGetValue(tab, out var lines) || lines == null)
                return;

            _fsCutTripReserveBand = FsFindTripReserveBand(lines, _fsTripsCtxTrip);
            FsPushUndoSnapshot("cut trip");
            if (!ScheduleBuilderPreviewDrag.TryRemoveTrip(lines, _fsTripsCtxTrip))
                return;

            _fsCutTrip = _fsTripsCtxTrip;
            FsCommitPreviewLinesForTab(tab, lines);
            ShowFsTripsForTab(tab);
            _ = RefreshFsMapForCurrentTabAsync();

            string num = (_fsCutTrip.TripNumber ?? "").Trim();
            SetScheduleBuilderStatus(string.IsNullOrEmpty(num)
                ? "Trip cut — right-click a row · Insert above or Insert below."
                : "Trip " + num + " cut — right-click a row · Insert above or Insert below.");
        }

        private void FsInsertFromContextMenu(bool below)
        {
            if (FsHasCutTrip)
                FsInsertCutTrip(below);
            else
                FsInsertBlankRow(below);
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
            Color? cutReserveBand = _fsCutTripReserveBand;
            FsClearCutTrip();

            _fsPreserveRouteChangeBaseline = true;
            FsSnapshotPreMoveGroupMeters(tab, trip, merge: false, mergeTargetTrip: null);

            if (!_fsLinesByTab.TryGetValue(tab, out var lines) || lines == null)
                return;

            Color? reserveBand = null;
            if (tab.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
            {
                reserveBand = cutReserveBand
                    ?? ScheduleBuilderPreviewDrag.ResolveReserveBandForInsert(lines, insertBeforeLine);
            }

            FsPushUndoSnapshot("insert trip");
            ScheduleBuilderPreviewDrag.InsertTripLine(lines, trip, insertBeforeLine, reserveBand);

            FsCommitPreviewLinesForTab(tab, lines);
            ShowFsTripsForTab(tab);
            SelectFsTripInListView(trip);
            _ = FsRefreshAfterTripMoveAsync();

            string num = (trip.TripNumber ?? "").Trim();
            SetScheduleBuilderStatus(string.IsNullOrEmpty(num)
                ? "Trip inserted — map updating…"
                : "Trip " + num + " inserted — map updating…");
        }

        private void FsInsertBlankRow(bool below)
        {
            if (FsHasCutTrip || string.IsNullOrWhiteSpace(_fsActiveDriverTab) || !_fsHasPreview)
                return;

            if (_fsActiveDriverTab.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
                return;

            if (!TryResolveFsInsertBeforeLine(_fsTripsCtxHitItem, below, out int insertBeforeLine))
            {
                SetScheduleBuilderStatus("Could not insert here — right-click a trip, gap, or group row.");
                return;
            }

            string tab = _fsActiveDriverTab;
            if (!_fsLinesByTab.TryGetValue(tab, out var lines) || lines == null)
                return;

            FsRevealGapsForManualInsert();
            FsPushUndoSnapshot("insert blank row");
            ScheduleBuilderPreviewDrag.InsertGapLine(lines, insertBeforeLine);
            // Keep consecutive spacers — collapse would undo insert-from-gap immediately.
            FsCommitPreviewLinesForTab(tab, lines);
            ShowFsTripsForTab(tab);
            SyncFsPreviewCsvsForExport();
            _ = RefreshFsMapForCurrentTabAsync();

            SetScheduleBuilderStatus("Blank row inserted.");
        }

        private async Task FsRefreshAfterTripMoveAsync()
        {
            await RefreshFsMapForCurrentTabAsync().ConfigureAwait(true);
            FsTripsLv_SelectionChangedUpdateMap();
        }

        /// <summary>Line index in preview lines where insert should occur.</summary>
        private bool TryResolveFsInsertBeforeLine(ListViewItem item, bool below, out int insertBeforeLine)
        {
            insertBeforeLine = 0;
            if (_fsTripsLv == null || string.IsNullOrWhiteSpace(_fsActiveDriverTab))
                return false;

            if (!_fsLinesByTab.TryGetValue(_fsActiveDriverTab, out var lines) || lines == null)
                return false;

            return ScheduleBuilderPreviewDrag.TryResolveInsertLineIndex(lines, item, below, out insertBeforeLine);
        }

        private bool FsCanInsertAtContext()
        {
            if (!_fsHasPreview || string.IsNullOrWhiteSpace(_fsActiveDriverTab))
                return false;
            if (_fsTripsCtxHitItem == null)
                return true;
            if (_fsTripsCtxHitItem.Tag is FsPreviewSectionHeaderTag sectionTag)
                return sectionTag.PreviewLineIndex >= 0;
            return ScheduleBuilderPreviewDrag.TryResolveInsertLineIndex(
                _fsLinesByTab.TryGetValue(_fsActiveDriverTab, out var lines) ? lines : null,
                _fsTripsCtxHitItem,
                below: false,
                out _);
        }

        private static Color? FsFindTripReserveBand(IList<ScheduleBuilderPreviewLine> lines, MCDownloadedTrip trip)
        {
            if (lines == null || trip == null) return null;
            foreach (var line in lines)
            {
                if (line?.Kind != ScheduleBuilderPreviewLine.LineKind.Trip || line.Trip == null)
                    continue;
                if (ReferenceEquals(line.Trip, trip)
                    || string.Equals(line.Trip.TripNumber, trip.TripNumber, StringComparison.OrdinalIgnoreCase))
                    return line.ReserveBandColor;
            }
            return null;
        }
    }
}
