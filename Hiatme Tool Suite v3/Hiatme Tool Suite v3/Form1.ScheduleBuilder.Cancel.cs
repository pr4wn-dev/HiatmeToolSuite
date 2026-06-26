using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    public partial class Form1
    {
        private void FsAddTripToCancelsSectionFromContext()
        {
            if (_fsTripsCtxTrip == null || !_fsHasPreview || string.IsNullOrWhiteSpace(_fsActiveDriverTab))
                return;
            if (fsbuilder == null)
                return;

            MCDownloadedTrip trip = _fsTripsCtxTrip;
            string tab = _fsActiveDriverTab;
            string num = (trip.TripNumber ?? "").Trim();

            if (!FsNeedsMoveToReservesCancels(trip, tab, fsbuilder))
            {
                SetScheduleBuilderStatus(string.IsNullOrEmpty(num)
                    ? "Trip is already in Reserves → Cancels."
                    : "Trip " + num + " is already in Reserves → Cancels.");
                return;
            }

            FsPushUndoSnapshot("add trip to cancels");

            // Surgical move: pull ONLY this trip's row from wherever it currently lives and
            // drop it into Reserves → Cancels. No rebuild and no partner-leg handling, so the
            // B leg (and every other trip) keeps its exact position.
            foreach (string key in new List<string>(_fsLinesByTab.Keys))
            {
                var lines = _fsLinesByTab[key];
                if (lines == null)
                    continue;

                bool removed = false;
                for (int i = lines.Count - 1; i >= 0; i--)
                {
                    var ln = lines[i];
                    if (ln?.Kind == ScheduleBuilderPreviewLine.LineKind.Trip
                        && ScheduleBuilderPreviewDrag.TripEquals(ln.Trip, trip))
                    {
                        lines.RemoveAt(i);
                        removed = true;
                    }
                }

                if (removed && !key.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
                    _fsLinesByTab[key] = ScheduleBuilderGroupHeaderReconcile.Reconcile(lines);
            }

            if (!_fsLinesByTab.TryGetValue("Reserves", out var reserveLines) || reserveLines == null)
            {
                reserveLines = new List<ScheduleBuilderPreviewLine>();
                _fsLinesByTab["Reserves"] = reserveLines;
            }

            ScheduleBuilderReserveBuckets.MoveTripIntoCancelsSectionInPlace(reserveLines, trip);

            // Keep the builder model (buckets + driver lists) in step for export/save without
            // rebuilding the UI from it.
            fsbuilder.SyncPreviewDriverLinesFromUi(_fsLinesByTab);
            fsbuilder.MoveTripToPreviewReservesCancel(trip);
            FsCommitReservePreviewLinesDirect(reserveLines);

            FsReapplyReroutedHighlights();
            FsReapplyWellRydeCancelledHighlights();

            SelectFsDriverTab("Reserves");
            ShowFsTripsForTab("Reserves");
            _fsTripsLv?.Invalidate(true);
            SelectFsTripInListView(trip);
            SyncFsPreviewCsvsForExport();
            _ = RefreshFsMapForCurrentTabAsync();

            SetScheduleBuilderStatus(string.IsNullOrEmpty(num)
                ? "Trip added to Reserves → Cancels."
                : "Trip " + num + " added to Reserves → Cancels.");
        }

        private static bool FsNeedsMoveToReservesCancels(
            MCDownloadedTrip trip,
            string sourceTab,
            FullScheduleBuilder builder)
        {
            if (trip == null || builder == null)
                return false;

            if (!sourceTab.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
                return true;

            if (!ScheduleBuilderReserveBuckets.IsInCancelBucket(builder.PreviewReservesCancel, trip))
                return true;

            return false;
        }

        /// <summary>
        /// Reserve lines after a bucket move — buckets are already correct; do not re-parse
        /// (ApplyPreviewReserveLines rebuckets banned/no-go and can touch partner legs).
        /// </summary>
        private void FsCommitReservePreviewLinesDirect(List<ScheduleBuilderPreviewLine> lines)
        {
            FsTrackReroutedKeysFromLines(lines);
            _fsLinesByTab["Reserves"] = lines;

            if (fsbuilder?.driverTripList != null)
            {
                var trips = new List<MCDownloadedTrip>();
                if (lines != null)
                {
                    foreach (var line in lines)
                    {
                        if (line?.Kind == ScheduleBuilderPreviewLine.LineKind.Trip && line.Trip != null)
                            trips.Add(line.Trip);
                    }
                }
                fsbuilder.driverTripList["Reserves"] = trips;
            }
        }
    }
}
