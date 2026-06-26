using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    public partial class Form1
    {
        private bool _fsCutTripRerouted;

        private CancellationTokenSource _fsRerouteProbeCts;

        private void FsCancelRerouteProbe()
        {
            try { _fsRerouteProbeCts?.Cancel(); }
            catch { /* ignore */ }
            _fsRerouteProbeCts = null;
        }

        private void FsShowInitialTabAfterScheduleLoad()
        {
            if (_fsTripsLv == null || _fsLinesByTab == null || _fsLinesByTab.Count == 0)
                return;

            _fsLinesByTab.TryGetValue("Reserves", out var reserveLines);
            IList<MCDownloadedTrip> bucket = fsbuilder?.PreviewReservesReroute;
            bool showReserves = reserveLines != null
                && (ScheduleBuilderReroutedTrips.CountTripsInReroutesSection(reserveLines, bucket) > 0
                    || ScheduleBuilderReroutedTrips.AnyMarked(reserveLines));

            if (showReserves)
            {
                SelectFsDriverTab("Reserves");
                FsSyncReroutedHighlightsFromPreviewLines();
                return;
            }

            var driverNames = ScheduleBuilderTabOrder.OrderDriverNames(
                _fsLinesByTab.Keys,
                fsbuilder?.TabOrder);
            string firstDriver = driverNames.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(firstDriver))
                SelectFsDriverTab(firstDriver);
            else
            {
                var tabNames = ScheduleBuilderTabOrder.NormalizeFullTabOrder(
                    fsbuilder?.TabOrder?.Count > 0 ? fsbuilder.TabOrder : null,
                    _fsLinesByTab.Keys);
                if (tabNames.Count > 0)
                    SelectFsDriverTab(tabNames[0]);
            }

            FsSyncReroutedHighlightsFromPreviewLines();
        }

        /// <summary>
        /// After load, check Reserves → Reroutes trips against Modivcare (lookup only — no reroute submit).
        /// Uses the same sign-in, cookie, and TripReroutes.aspx flow as manual reroute.
        /// </summary>
        /// <param name="refreshListView">False during load — caller shows the list after flags are final.</param>
        /// <returns>Status suffix for the load message, or empty when nothing to verify.</returns>
        private async Task<string> FsProbeReroutesAfterScheduleLoadAsync(
            DateTime serviceDate,
            bool refreshListView = true)
        {
            FsCancelRerouteProbe();
            _fsRerouteProbeCts = new CancellationTokenSource();
            CancellationToken token = _fsRerouteProbeCts.Token;

            if (fsbuilder == null)
                return "";

            if (!_fsLinesByTab.TryGetValue("Reserves", out var reserveLines) || reserveLines == null)
                return "";

            IList<MCDownloadedTrip> rerouteBucket = fsbuilder.PreviewReservesReroute;
            List<MCDownloadedTrip> toCheck = ScheduleBuilderReroutedTrips.EnumerateTripsInReroutesSection(
                reserveLines,
                skipAlreadyMarked: false,
                bucketFallback: rerouteBucket);

            if (toCheck.Count == 0)
                return "";

            void ProbeStatus(string text)
            {
                SetScheduleBuilderStatus(text);
                UpdateTabLoadingOverlayMessage(tabPage6, text);
            }

            ProbeStatus("Connecting to Modivcare…");
            if (!await EnsureModivcareSessionAsync().ConfigureAwait(true))
                return " Reroute verify skipped — Modivcare not available.";

            int marked = 0;
            int unmarked = 0;
            int checkedCount = 0;
            for (int i = 0; i < toCheck.Count; i++)
            {
                if (token.IsCancellationRequested || fsbuilder == null)
                    break;

                MCDownloadedTrip trip = toCheck[i];
                string num = (trip.TripNumber ?? "").Trim();
                ProbeStatus("Checking reroutes on Modivcare (" + (i + 1) + "/" + toCheck.Count
                    + (string.IsNullOrEmpty(num) ? ")…" : ") · " + num + "…"));

                MCTripRerouter.ProbeResult probe;
                try
                {
                    probe = await FsProbeTripOnModivcareAsync(trip, serviceDate, token).ConfigureAwait(true);
                }
                catch (ModivcareSessionExpiredException)
                {
                    return " Reroute verify stopped — Modivcare session expired.";
                }
                catch (OperationCanceledException)
                {
                    return "";
                }
                catch
                {
                    continue;
                }

                checkedCount++;

                if (probe.Outcome == MCTripRerouter.RerouteProbeOutcome.AlreadyRerouted)
                {
                    bool wasMarked = ScheduleBuilderReroutedTrips.IsMarkedAnyTab(_fsLinesByTab, trip);
                    ScheduleBuilderReroutedTrips.MarkReroutedAnyTab(_fsLinesByTab, trip);
                    if (refreshListView)
                        FsRefreshReroutedHighlightForTrip(trip);
                    if (!wasMarked)
                        marked++;
                    continue;
                }

                if (probe.Outcome == MCTripRerouter.RerouteProbeOutcome.StillOnCompany)
                {
                    if (ScheduleBuilderReroutedTrips.ClearReroutedAnyTab(_fsLinesByTab, trip))
                        unmarked++;
                    if (refreshListView)
                        FsRefreshReroutedHighlightForTrip(trip);
                }
            }

            if (token.IsCancellationRequested || fsbuilder == null)
                return "";

            if (refreshListView)
                FsSyncReroutedHighlightsFromPreviewLines();

            if (marked > 0 || unmarked > 0
                || ScheduleBuilderReroutedTrips.AnyMarked(reserveLines))
            {
                SyncFsPreviewCsvsForExport();
                if (refreshListView)
                    FsShowInitialTabAfterScheduleLoad();
            }

            if (checkedCount == 0)
                return " Reroute verify skipped — Modivcare unreachable.";

            if (marked > 0 && unmarked > 0)
            {
                return " Reroute verify — " + marked + " marked red, " + unmarked
                    + " cleared (still on company).";
            }

            if (marked > 0)
            {
                return " Reroute verify — " + marked + " trip" + (marked == 1 ? "" : "s")
                    + " already rerouted on Modivcare (marked red).";
            }

            if (unmarked > 0)
            {
                return " Reroute verify — " + unmarked + " trip" + (unmarked == 1 ? "" : "s")
                    + " cleared red highlight (still on company).";
            }

            return " Reroute verify — " + checkedCount + " trip" + (checkedCount == 1 ? "" : "s")
                + " checked; all still on company.";
        }

        /// <summary>One trip lookup with the same session-expiry reconnect pattern as manual reroute.</summary>
        private async Task<MCTripRerouter.ProbeResult> FsProbeTripOnModivcareAsync(
            MCDownloadedTrip trip,
            DateTime serviceDate,
            CancellationToken token)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    return await MCTripRerouter.ProbeRerouteStatusAsync(
                            mcLoginHandler,
                            trip,
                            serviceDate,
                            token)
                        .ConfigureAwait(true);
                }
                catch (ModivcareSessionExpiredException)
                {
                    if (attempt > 0)
                        throw;

                    await HandleModivcareSessionExpiredAsync().ConfigureAwait(true);
                    if (!await EnsureModivcareSessionAsync().ConfigureAwait(true))
                        throw new ModivcareSessionExpiredException();
                }
            }

            throw new ModivcareSessionExpiredException();
        }

        private void FsRefreshReroutedHighlightForTrip(MCDownloadedTrip trip)
        {
            if (_fsTripsLv == null || trip == null)
                return;

            bool rerouted = ScheduleBuilderReroutedTrips.IsMarkedAnyTab(_fsLinesByTab, trip);
            bool isReservesTab = string.Equals(_fsActiveDriverTab, "Reserves", StringComparison.OrdinalIgnoreCase);
            _fsLinesByTab.TryGetValue(_fsActiveDriverTab ?? "", out var activeLines);

            foreach (ListViewItem lvi in _fsTripsLv.Items)
            {
                if (!(lvi.Tag is FsPreviewTripTag tag) || tag.Trip == null)
                    continue;
                if (!ScheduleBuilderPreviewDrag.TripEquals(tag.Trip, trip))
                    continue;

                tag.ReroutedOnModivcare = rerouted;
                var line = ScheduleBuilderReroutedTrips.FindLine(activeLines, tag.Trip);
                if (rerouted)
                    ApplyFsReroutedTripRowStyle(lvi);
                else
                    ClearFsReroutedTripRowStyle(lvi, line, isReservesTab, tag, FsShowGroupColorsEnabled);
            }
        }

        /// <summary>Align list-view tags and back colors with preview-line rerouted flags.</summary>
        private void FsSyncReroutedHighlightsFromPreviewLines()
        {
            if (_fsTripsLv == null || string.IsNullOrWhiteSpace(_fsActiveDriverTab))
                return;
            if (!_fsLinesByTab.TryGetValue(_fsActiveDriverTab, out var lines) || lines == null)
                return;

            bool isReservesTab = _fsActiveDriverTab.Equals("Reserves", StringComparison.OrdinalIgnoreCase);

            foreach (ListViewItem lvi in _fsTripsLv.Items)
            {
                if (!(lvi.Tag is FsPreviewTripTag tag) || tag.Trip == null)
                    continue;

                var line = ScheduleBuilderReroutedTrips.FindLine(lines, tag.Trip);
                if (line == null)
                    continue;

                if (line.ReroutedOnModivcare)
                {
                    tag.ReroutedOnModivcare = true;
                    ApplyFsReroutedTripRowStyle(lvi);
                    continue;
                }

                tag.ReroutedOnModivcare = false;
                ClearFsReroutedTripRowStyle(lvi, line, isReservesTab, tag, FsShowGroupColorsEnabled);
            }

            _fsTripsLv.Invalidate(true);
        }

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
                fsbuilder.WillCallsInDownloadCount,
                preserveTripOrder: true);
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
                    fsbuilder.WillCallsInDownloadCount,
                    preserveTripOrder: true);
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
