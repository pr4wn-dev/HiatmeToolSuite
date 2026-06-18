using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    public partial class Form1
    {
        private bool _fsCutTripRerouted;

        private async void FsRerouteTripOnModivcareFromContext()
        {
            if (_fsTripsCtxTrip == null || !_fsHasPreview || string.IsNullOrWhiteSpace(_fsActiveDriverTab))
                return;

            MCDownloadedTrip trip = _fsTripsCtxTrip;
            string tab = _fsActiveDriverTab;
            string num = (trip.TripNumber ?? "").Trim();

            if (!_fsLinesByTab.TryGetValue(tab, out var lines) || lines == null)
                return;

            if (ScheduleBuilderReroutedTrips.IsMarked(lines, trip))
            {
                SetScheduleBuilderStatus(string.IsNullOrEmpty(num)
                    ? "Trip is already marked rerouted."
                    : "Trip " + num + " is already marked rerouted.");
                return;
            }

            string prompt = string.IsNullOrEmpty(num)
                ? "Submit this trip for reroute on Modivcare?\n\n"
                    + "On success it moves to Reserves → Reroutes (if not already there) and is marked red."
                : "Submit trip " + num + " for reroute on Modivcare?\n\n"
                    + "On success it moves to Reserves → Reroutes (if not already there) and is marked red.";

            if (MessageBox.Show(this, prompt, "Reroute on Modivcare",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2)
                != DialogResult.Yes)
                return;

            if (!await EnsureModivcareSessionAsync())
            {
                SetScheduleBuilderStatus("Modivcare sign-in required.");
                return;
            }

            SetScheduleBuilderStatus(string.IsNullOrEmpty(num)
                ? "Submitting reroute on Modivcare…"
                : "Submitting reroute for " + num + "…");

            try
            {
                MCTripRerouter.Result result = await MCTripRerouter.SubmitRerouteAsync(
                        mcLoginHandler,
                        trip,
                        MCTripRerouter.DefaultRerouteReasonCode,
                        fsbdatepicker?.Value.Date)
                    .ConfigureAwait(true);

                if (!result.Success)
                {
                    MessageBox.Show(this,
                        result.Message ?? "Modivcare did not accept the reroute.",
                        "Reroute on Modivcare",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    SetScheduleBuilderStatus("Reroute failed — see message.");
                    return;
                }

                FsPushUndoSnapshot("reroute trip");
                FsApplySuccessfulModivcareReroute(trip, tab, out bool movedToReserves, out bool marked);
                string displayTab = movedToReserves ? "Reserves" : tab;

                ShowFsTripsForTab(displayTab);
                _fsTripsLv?.Invalidate(true);
                SelectFsTripInListView(trip);
                SyncFsPreviewCsvsForExport();
                _ = RefreshFsMapForCurrentTabAsync();

                string successMsg = result.Message ?? ("Trip " + num + " rerouted on Modivcare.");
                if (!marked)
                {
                    successMsg += "\n\nModivcare accepted the reroute, but the schedule row could not be marked red — try reloading the preview.";
                }
                else if (movedToReserves)
                {
                    successMsg += "\n\nMoved to Reserves → Reroutes and marked red.";
                }
                else
                {
                    successMsg += "\n\nAlready in Reserves → Reroutes — marked red.";
                }

                MCDownloadedTrip tripForRegistry = trip;
                if (_fsLinesByTab.TryGetValue(displayTab, out var registryLines)
                    && registryLines != null)
                {
                    var line = ScheduleBuilderReroutedTrips.FindLine(registryLines, trip);
                    if (line?.Trip != null)
                        tripForRegistry = line.Trip;
                }
                else if (fsbuilder != null)
                {
                    var previewTrip = fsbuilder.FindTripInPreviewByNumber(trip.TripNumber);
                    if (previewTrip != null)
                        tripForRegistry = previewTrip;
                }

                var aiSettings = HiatmeAiSettings.Load();
                var recordResult = await ScheduleBuilderReroutedTripsRegistry.RecordRerouteAsync(
                    aiSettings,
                    fsbdatepicker?.Value.Date ?? DateTime.Today,
                    tripForRegistry,
                    aiSettings.ResolvedClientId()).ConfigureAwait(true);

                if (recordResult.LocalSaved && !recordResult.ServerSaved && HiatmeGeoSettings.UseServer)
                {
                    successMsg += "\n\nSaved on this PC — office server offline; other desks may not see this reroute yet.";
                }
                else if (recordResult.LocalSaved && recordResult.ServerSaved)
                {
                    successMsg += "\n\nShared with office server — other desks will see this on BUILD.";
                }

                MessageBox.Show(this, successMsg, "Reroute on Modivcare",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);

                SetScheduleBuilderStatus(string.IsNullOrEmpty(num)
                    ? "Trip rerouted on Modivcare."
                    : "Trip " + num + " rerouted on Modivcare.");
            }
            catch (ModivcareSessionExpiredException)
            {
                await HandleModivcareSessionExpiredAsync().ConfigureAwait(true);
                SetScheduleBuilderStatus("Modivcare session expired — sign in and try again.");
            }
            catch (Exception ex)
            {
                string msg = ModivcareRequestErrors.DescribeOrDefault(ex, "Could not submit reroute.");
                MessageBox.Show(this, msg, "Reroute on Modivcare",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SetScheduleBuilderStatus(ModivcareRequestErrors.IsUnreachable(ex)
                    ? "Modivcare unreachable."
                    : "Reroute failed.");
            }
        }

        private void FsAddTripToReroutesSectionFromContext()
        {
            if (_fsTripsCtxTrip == null || !_fsHasPreview || string.IsNullOrWhiteSpace(_fsActiveDriverTab))
                return;
            if (fsbuilder == null)
                return;

            MCDownloadedTrip trip = _fsTripsCtxTrip;
            string tab = _fsActiveDriverTab;
            string num = (trip.TripNumber ?? "").Trim();

            if (!FsNeedsMoveToReservesReroutes(trip, tab, fsbuilder))
            {
                SetScheduleBuilderStatus(string.IsNullOrEmpty(num)
                    ? "Trip is already in Reserves → Reroutes."
                    : "Trip " + num + " is already in Reserves → Reroutes.");
                return;
            }

            FsPushUndoSnapshot("add trip to reroutes");

            // Preserve any rows already marked rerouted (red) — rebuilding reserve
            // lines wipes the ReroutedOnModivcare flags.
            _fsLinesByTab.TryGetValue("Reserves", out var priorReserves);

            // Move the trip into the Reserves → Reroutes bucket WITHOUT submitting to
            // Modivcare and WITHOUT marking the row red (no rerouted highlight).
            fsbuilder.MoveTripToPreviewReservesReroute(trip);

            if (fsbuilder.PreviewDriverLines is Dictionary<string, List<ScheduleBuilderPreviewLine>> dict)
            {
                var driverTabs = new List<string>(dict.Keys);
                foreach (string driverTab in driverTabs)
                {
                    if (driverTab.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (dict.TryGetValue(driverTab, out var driverLines) && driverLines != null)
                        FsCommitPreviewLinesForTab(driverTab, driverLines);
                }
            }

            var reserveLines = ScheduleBuilderReserveBuckets.BuildReservePreviewLines(
                fsbuilder.PreviewReserves,
                fsbuilder.PreviewReservesReroute,
                banned: null,
                fsbuilder.PreviewReservesWillCalls,
                fsbuilder.WillCallsInDownloadCount);
            // Restore prior red marks but do NOT mark the newly added trip.
            ScheduleBuilderReroutedTrips.RestoreAndMarkRerouted(reserveLines, priorReserves, justRerouted: null);
            FsCommitPreviewLinesForTab("Reserves", reserveLines);

            SelectFsDriverTab("Reserves");
            ShowFsTripsForTab("Reserves");
            _fsTripsLv?.Invalidate(true);
            SelectFsTripInListView(trip);
            SyncFsPreviewCsvsForExport();
            _ = RefreshFsMapForCurrentTabAsync();

            SetScheduleBuilderStatus(string.IsNullOrEmpty(num)
                ? "Trip added to Reserves → Reroutes."
                : "Trip " + num + " added to Reserves → Reroutes.");
        }

        private static bool FsNeedsMoveToReservesReroutes(MCDownloadedTrip trip, string sourceTab, FullScheduleBuilder builder)
        {
            if (trip == null || builder == null)
                return false;

            if (!sourceTab.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
                return true;

            if (!ScheduleBuilderReroutedTrips.IsInReservesRerouteBucket(builder.PreviewReservesReroute, trip))
                return true;

            return false;
        }

        private void FsApplySuccessfulModivcareReroute(
            MCDownloadedTrip trip,
            string sourceTab,
            out bool movedToReserves,
            out bool marked)
        {
            movedToReserves = false;
            marked = false;

            if (fsbuilder == null)
                return;

            _fsLinesByTab.TryGetValue("Reserves", out var priorReserves);

            if (FsNeedsMoveToReservesReroutes(trip, sourceTab, fsbuilder))
            {
                fsbuilder.MoveTripToPreviewReservesReroute(trip);

                if (fsbuilder.PreviewDriverLines is Dictionary<string, List<ScheduleBuilderPreviewLine>> dict)
                {
                    var driverTabs = new List<string>(dict.Keys);
                    foreach (string driverTab in driverTabs)
                    {
                        if (driverTab.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (dict.TryGetValue(driverTab, out var driverLines) && driverLines != null)
                            FsCommitPreviewLinesForTab(driverTab, driverLines);
                    }
                }

                var reserveLines = ScheduleBuilderReserveBuckets.BuildReservePreviewLines(
                    fsbuilder.PreviewReserves,
                    fsbuilder.PreviewReservesReroute,
                    banned: null,
                    fsbuilder.PreviewReservesWillCalls,
                    fsbuilder.WillCallsInDownloadCount);
                ScheduleBuilderReroutedTrips.RestoreAndMarkRerouted(reserveLines, priorReserves, trip);
                marked = ScheduleBuilderReroutedTrips.IsMarked(reserveLines, trip);
                FsCommitPreviewLinesForTab("Reserves", reserveLines);
                movedToReserves = true;
                SelectFsDriverTab("Reserves");
                return;
            }

            if (_fsLinesByTab.TryGetValue(sourceTab, out var lines) && lines != null)
            {
                marked = ScheduleBuilderReroutedTrips.MarkRerouted(lines, trip);
                if (!marked)
                    marked = ScheduleBuilderReroutedTrips.MarkReroutedAnyTab(_fsLinesByTab, trip);
                FsCommitPreviewLinesForTab(sourceTab, lines);
            }
        }
    }
}
