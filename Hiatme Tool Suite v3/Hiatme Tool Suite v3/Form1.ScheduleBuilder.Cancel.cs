using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    public partial class Form1
    {
        private void FsAddTripToCancelsSectionFromContext()
        {
            if (!_fsHasPreview || string.IsNullOrWhiteSpace(_fsActiveDriverTab))
                return;
            if (fsbuilder == null)
                return;

            string tab = _fsActiveDriverTab;
            var trips = FsCollectSelectedTrips();
            if (trips.Count == 0)
                return;

            var toMove = new List<MCDownloadedTrip>();
            foreach (var trip in trips)
            {
                if (FsNeedsMoveToReservesCancels(trip, tab, fsbuilder))
                    toMove.Add(trip);
            }

            if (toMove.Count == 0)
            {
                SetScheduleBuilderStatus(trips.Count == 1
                    ? "Trip is already in Reserves → Cancels."
                    : "Selected trips are already in Reserves → Cancels.");
                return;
            }

            FsPushUndoSnapshot(toMove.Count == 1
                ? "add trip to cancels"
                : "add " + toMove.Count + " trips to cancels");

            if (!_fsLinesByTab.TryGetValue("Reserves", out var reserveLines) || reserveLines == null)
            {
                reserveLines = new List<ScheduleBuilderPreviewLine>();
                SetFsLinesByTabEntry("Reserves", reserveLines);
            }

            var touchedTabs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var trip in toMove)
            {
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
                        touchedTabs.Add(key);
                }

                ScheduleBuilderReserveBuckets.MoveTripIntoCancelsSectionInPlace(reserveLines, trip);
                fsbuilder.MoveTripToPreviewReservesCancel(trip);
            }

            foreach (string key in touchedTabs)
            {
                if (_fsLinesByTab.TryGetValue(key, out var lines) && lines != null)
                    SetFsLinesByTabEntry(key, ScheduleBuilderGroupHeaderReconcile.Reconcile(lines));
            }

            // Keep the builder model (buckets + driver lists) in step for export/save without
            // rebuilding the UI from it.
            fsbuilder.SyncPreviewDriverLinesFromUi(_fsLinesByTab);
            FsCommitReservePreviewLinesDirect(reserveLines);

            FsReapplyReroutedHighlights();
            FsReapplyWellRydeCancelledHighlights();

            SelectFsDriverTab("Reserves");
            ShowFsTripsForTab("Reserves");
            _fsTripsLv?.Invalidate(true);
            SelectFsTripInListView(toMove[toMove.Count - 1]);
            SyncFsPreviewCsvsForExport();
            RequestFsMapRefresh();

            if (toMove.Count == 1)
            {
                string num = (toMove[0].TripNumber ?? "").Trim();
                SetScheduleBuilderStatus(string.IsNullOrEmpty(num)
                    ? "Trip added to Reserves → Cancels."
                    : "Trip " + num + " added to Reserves → Cancels.");
            }
            else
            {
                SetScheduleBuilderStatus(toMove.Count + " trips added to Reserves → Cancels.");
            }
        }

        /// <summary>
        /// Selected trip rows in the preview list, in row order, plus the right-clicked trip if it
        /// wasn't part of the selection. Every action on trip rows scopes itself with this, so
        /// cut, delete, reroute and cancel all agree on what "the selection" means.
        /// </summary>
        private List<MCDownloadedTrip> FsCollectSelectedTrips()
        {
            var trips = new List<MCDownloadedTrip>();
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenRefs = new HashSet<MCDownloadedTrip>();

            void TryAdd(MCDownloadedTrip trip)
            {
                if (trip == null)
                    return;

                string key = ScheduleBuilderReroutedTrips.TripNumberKey(trip.TripNumber);
                if (key.Length > 0)
                {
                    if (!seenKeys.Add(key))
                        return;
                }
                else if (!seenRefs.Add(trip))
                {
                    return;
                }

                trips.Add(trip);
            }

            if (_fsTripsLv != null)
            {
                foreach (ListViewItem item in _fsTripsLv.SelectedItems)
                {
                    if (item?.Tag is FsPreviewTripTag tag && tag.Trip != null)
                        TryAdd(tag.Trip);
                }
            }

            // Right-click on an unselected row should still include that trip.
            TryAdd(_fsTripsCtxTrip);
            return trips;
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
            ScheduleBuilderReserveBuckets.ReassignBandsAndRefreshSectionCounts(lines);
            FsTrackReroutedKeysFromLines(lines);
            SetFsLinesByTabEntry("Reserves", lines);

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
