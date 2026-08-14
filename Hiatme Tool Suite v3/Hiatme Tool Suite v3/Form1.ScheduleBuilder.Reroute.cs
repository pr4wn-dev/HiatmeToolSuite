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
                SelectFsDriverTab("Reserves", refreshMap: false);
                FsSyncReroutedHighlightsFromPreviewLines();
                StartFsMapPreloadAfterScheduleBind(
                    ScheduleBuilderTabOrder.NormalizeFullTabOrder(
                        fsbuilder?.TabOrder?.Count > 0 ? fsbuilder.TabOrder : null,
                        _fsLinesByTab.Keys));
                return;
            }

            var driverNames = ScheduleBuilderTabOrder.OrderDriverNames(
                _fsLinesByTab.Keys,
                fsbuilder?.TabOrder);
            string firstDriver = driverNames.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(firstDriver))
                SelectFsDriverTab(firstDriver, refreshMap: false);
            else
            {
                var tabNames = ScheduleBuilderTabOrder.NormalizeFullTabOrder(
                    fsbuilder?.TabOrder?.Count > 0 ? fsbuilder.TabOrder : null,
                    _fsLinesByTab.Keys);
                if (tabNames.Count > 0)
                    SelectFsDriverTab(tabNames[0], refreshMap: false);
            }

            FsSyncReroutedHighlightsFromPreviewLines();

            var preloadTabs = ScheduleBuilderTabOrder.NormalizeFullTabOrder(
                fsbuilder?.TabOrder?.Count > 0 ? fsbuilder.TabOrder : null,
                _fsLinesByTab.Keys);
            if (preloadTabs.Count > 0 && !string.IsNullOrWhiteSpace(_fsActiveDriverTab))
                StartFsMapPreloadAfterScheduleBind(preloadTabs);
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
                skipAlreadyMarked: true,
                bucketFallback: rerouteBucket);

            if (toCheck.Count == 0)
                return "";

            void ProbeStatus(string text)
            {
                SetScheduleBuilderStatus(text);
                UpdateTabLoadingOverlayMessage(tabPage6, text);
            }

            ProbeStatus("Connecting to Modivcare…");
            if (!await EnsureModivcareSessionAsync(ProbeStatus).ConfigureAwait(true))
                return " Reroute verify skipped — Modivcare not available.";

            int marked = 0;
            int unmarked = 0;
            int checkedCount = 0;
            var probeSession = new MCTripRerouter.RerouteProbeSession();

            // Trips confirmed rerouted on Modivcare this load — persist to the reroute registry so
            // future loads pre-mark them and skip re-checking.
            var verifiedReroutes = new List<MCDownloadedTrip>();

            async Task PersistVerifiedReroutesAsync()
            {
                if (verifiedReroutes.Count == 0)
                    return;
                try
                {
                    var aiSettings = HiatmeAiSettings.Load();
                    await ScheduleBuilderReroutedTripsRegistry.RecordVerifiedReroutesAsync(
                            aiSettings,
                            serviceDate,
                            verifiedReroutes,
                            aiSettings.ResolvedClientId())
                        .ConfigureAwait(true);
                }
                catch { /* local cache is best-effort */ }
                verifiedReroutes.Clear();
            }

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
                    probe = await FsProbeTripOnModivcareAsync(trip, serviceDate, token, probeSession)
                        .ConfigureAwait(true);
                }
                catch (ModivcareSessionExpiredException)
                {
                    await PersistVerifiedReroutesAsync().ConfigureAwait(true);
                    return " Reroute verify stopped — Modivcare session expired.";
                }
                catch (OperationCanceledException)
                {
                    await PersistVerifiedReroutesAsync().ConfigureAwait(true);
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
                    FsTrackReroutedTripKey(trip.TripNumber);
                    verifiedReroutes.Add(trip);
                    if (refreshListView)
                        FsRefreshReroutedHighlightForTrip(trip);
                    if (!wasMarked)
                        marked++;
                }
                else if (probe.Outcome == MCTripRerouter.RerouteProbeOutcome.StillOnCompany)
                {
                    if (ScheduleBuilderReroutedTrips.ClearReroutedAnyTab(_fsLinesByTab, trip))
                        unmarked++;
                    FsUntrackReroutedTripKey(trip.TripNumber);
                    if (refreshListView)
                        FsRefreshReroutedHighlightForTrip(trip);
                }

                if (i + 1 < toCheck.Count && MCTripRerouter.ProbeDelayMilliseconds > 0)
                {
                    try
                    {
                        await Task.Delay(MCTripRerouter.ProbeDelayMilliseconds, token).ConfigureAwait(true);
                    }
                    catch (OperationCanceledException)
                    {
                        await PersistVerifiedReroutesAsync().ConfigureAwait(true);
                        return "";
                    }
                }
            }

            await PersistVerifiedReroutesAsync().ConfigureAwait(true);

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
            CancellationToken token,
            MCTripRerouter.RerouteProbeSession session)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    return await MCTripRerouter.ProbeRerouteStatusAsync(
                            mcLoginHandler,
                            trip,
                            serviceDate,
                            token,
                            session)
                        .ConfigureAwait(true);
                }
                catch (ModivcareSessionExpiredException)
                {
                    if (attempt > 0)
                        throw;

                    session?.ResetAfterReconnect();
                    await HandleModivcareSessionExpiredAsync().ConfigureAwait(true);
                    if (!await EnsureModivcareSessionAsync(msg => SetScheduleBuilderStatus(msg)).ConfigureAwait(true))
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
                {
                    tag.CancelledOnWellRyde = false;
                    ApplyFsReroutedTripRowStyle(lvi);
                }
                else
                {
                    tag.ReroutedOnModivcare = false;
                    bool cancelled = line?.CancelledOnWellRyde == true;
                    tag.CancelledOnWellRyde = cancelled;
                    if (cancelled)
                        ApplyFsCancelledTripRowStyle(lvi);
                    else
                        ClearFsReroutedTripRowStyle(lvi, line, isReservesTab, tag, FsShowGroupColorsEnabled);
                }
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
                    tag.CancelledOnWellRyde = false;
                    ApplyFsReroutedTripRowStyle(lvi);
                    continue;
                }

                tag.ReroutedOnModivcare = false;
                if (line.CancelledOnWellRyde)
                {
                    tag.CancelledOnWellRyde = true;
                    ApplyFsCancelledTripRowStyle(lvi);
                    continue;
                }

                tag.CancelledOnWellRyde = false;
                ClearFsReroutedTripRowStyle(lvi, line, isReservesTab, tag, FsShowGroupColorsEnabled);
            }

            FsAutoSizeAlertsColumnToWidest();
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

            if (!await EnsureModivcareSessionAsync(msg => SetScheduleBuilderStatus(msg)))
            {
                SetScheduleBuilderStatus("Modivcare sign-in required.");
                return;
            }

            SetScheduleBuilderStatus(string.IsNullOrEmpty(num)
                ? "Loading reroute reasons from Modivcare…"
                : "Loading reroute reasons for " + num + "…");

            MCTripRerouter.PrepareResult prepared;
            try
            {
                prepared = await MCTripRerouter.PrepareRerouteFormAsync(
                        mcLoginHandler,
                        trip,
                        fsbdatepicker?.Value.Date)
                    .ConfigureAwait(true);
            }
            catch (ModivcareSessionExpiredException)
            {
                SetScheduleBuilderStatus("Modivcare session expired — sign in again.");
                return;
            }

            if (!prepared.Success)
            {
                SupeyMessageForm.Show(this,
                    "Reroute on Modivcare",
                    prepared.Message ?? "Modivcare did not open the reroute form.",
                    SupeyMessageKind.Warning,
                    "Could not load reroute form");
                SetScheduleBuilderStatus("Reroute cancelled — could not load reasons.");
                return;
            }

            string selectedReasonCode;
            string selectedReasonLabel;
            using (var picker = new ScheduleRerouteReasonForm(num, prepared.Reasons))
            {
                if (picker.ShowDialog(this) != DialogResult.OK)
                {
                    SetScheduleBuilderStatus("Reroute cancelled.");
                    return;
                }

                selectedReasonCode = picker.SelectedReasonCode;
                selectedReasonLabel = picker.SelectedReasonLabel;
            }

            if (string.IsNullOrWhiteSpace(selectedReasonCode))
            {
                SetScheduleBuilderStatus("Reroute cancelled — no reason selected.");
                return;
            }

            SetScheduleBuilderStatus(string.IsNullOrEmpty(num)
                ? "Submitting reroute on Modivcare…"
                : "Submitting reroute for " + num + "…");

            try
            {
                MCTripRerouter.Result result = await MCTripRerouter.SubmitPreparedRerouteAsync(
                        mcLoginHandler,
                        trip,
                        selectedReasonCode)
                    .ConfigureAwait(true);

                if (!result.Success)
                {
                    SupeyMessageForm.Show(this,
                        "Reroute on Modivcare",
                        result.Message ?? "Modivcare did not accept the reroute.",
                        SupeyMessageKind.Warning,
                        "Reroute failed");
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
                RequestFsMapRefresh();

                string successMsg = result.Message ?? ("Trip " + num + " rerouted on Modivcare.");
                if (!string.IsNullOrWhiteSpace(selectedReasonLabel))
                    successMsg += "\n\nReason: " + selectedReasonLabel.Trim();
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

                SupeyMessageForm.Show(this,
                    "Reroute on Modivcare",
                    successMsg,
                    SupeyMessageKind.Information,
                    string.IsNullOrEmpty(num) ? "Trip rerouted" : "Trip " + num + " rerouted");

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
                SupeyMessageForm.Show(this,
                    "Reroute on Modivcare",
                    msg,
                    SupeyMessageKind.Warning,
                    "Reroute failed");
                SetScheduleBuilderStatus(ModivcareRequestErrors.IsUnreachable(ex)
                    ? "Modivcare unreachable."
                    : "Reroute failed.");
            }
        }

        private void FsAddTripToReroutesSectionFromContext()
        {
            if (!_fsHasPreview || string.IsNullOrWhiteSpace(_fsActiveDriverTab))
                return;
            if (fsbuilder == null)
                return;

            string tab = _fsActiveDriverTab;
            var trips = FsCollectSelectedTrips();
            if (trips.Count == 0)
                return;

            _fsLinesByTab.TryGetValue("Reserves", out var reserveLinesForMove);

            var toMove = new List<MCDownloadedTrip>();
            foreach (var trip in trips)
            {
                if (FsNeedsMoveToReservesReroutes(trip, tab, fsbuilder, reserveLinesForMove))
                    toMove.Add(trip);
            }

            if (toMove.Count == 0)
            {
                SetScheduleBuilderStatus(trips.Count == 1
                    ? "Trip is already in Reserves → Reroutes."
                    : "Selected trips are already in Reserves → Reroutes.");
                return;
            }

            FsPushUndoSnapshot(toMove.Count == 1
                ? "add trip to reroutes"
                : "add " + toMove.Count + " trips to reroutes");

            foreach (var trip in toMove)
                FsMoveSingleTripToReservesReroutesSection(trip, markNewlyReroutedOnModivcare: false);

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
                    ? "Trip added to Reserves → Reroutes."
                    : "Trip " + num + " added to Reserves → Reroutes.");
            }
            else
            {
                SetScheduleBuilderStatus(toMove.Count + " trips added to Reserves → Reroutes.");
            }
        }

        /// <summary>
        /// Move exactly one trip into Reserves → Reroutes without rebuilding the tab.
        /// Other reserve rows (including other rerouted trips still under Reservers) stay put.
        /// </summary>
        private void FsMoveSingleTripToReservesReroutesSection(
            MCDownloadedTrip trip,
            bool markNewlyReroutedOnModivcare)
        {
            if (trip == null || fsbuilder == null)
                return;

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
                    SetFsLinesByTabEntry(key, ScheduleBuilderGroupHeaderReconcile.Reconcile(lines));
            }

            if (!_fsLinesByTab.TryGetValue("Reserves", out var reserveLines) || reserveLines == null)
            {
                reserveLines = new List<ScheduleBuilderPreviewLine>();
                SetFsLinesByTabEntry("Reserves", reserveLines);
            }

            ScheduleBuilderReserveBuckets.MoveTripIntoReroutesSectionInPlace(reserveLines, trip);

            if (markNewlyReroutedOnModivcare)
            {
                foreach (var line in reserveLines)
                {
                    if (line?.Kind != ScheduleBuilderPreviewLine.LineKind.Trip
                        || !ScheduleBuilderPreviewDrag.TripEquals(line.Trip, trip))
                    {
                        continue;
                    }

                    line.ReroutedOnModivcare = true;
                    line.CancelledOnWellRyde = false;
                    break;
                }

                FsTrackReroutedTripKey(trip.TripNumber);
            }

            fsbuilder.SyncPreviewDriverLinesFromUi(_fsLinesByTab);
            fsbuilder.MoveTripToPreviewReservesReroute(trip);
            FsCommitReservePreviewLinesDirect(reserveLines);
            FsReapplyReroutedHighlights();
            FsReapplyWellRydeCancelledHighlights();
        }

        private static bool FsNeedsMoveToReservesReroutes(
            MCDownloadedTrip trip,
            string sourceTab,
            FullScheduleBuilder builder,
            IList<ScheduleBuilderPreviewLine> reserveLines = null)
        {
            if (trip == null || builder == null)
                return false;

            if (!sourceTab.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
                return true;

            if (reserveLines != null
                && ScheduleBuilderReroutedTrips.IsInReservesRerouteSection(reserveLines, trip))
                return false;

            // Bucket can list a trip while preview lines still show it under Reservers
            // (e.g. after Modivcare new-trip sync). Offer section move until lines match.
            if (reserveLines != null)
                return true;

            return !ScheduleBuilderReroutedTrips.IsInReservesRerouteBucket(
                builder.PreviewReservesReroute, trip);
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

            if (FsNeedsMoveToReservesReroutes(trip, sourceTab, fsbuilder, priorReserves))
            {
                FsMoveSingleTripToReservesReroutesSection(trip, markNewlyReroutedOnModivcare: true);
                marked = ScheduleBuilderReroutedTrips.IsMarkedAnyTab(_fsLinesByTab, trip);
                movedToReserves = true;
                SelectFsDriverTab("Reserves");
                return;
            }

            if (_fsLinesByTab.TryGetValue(sourceTab, out var lines) && lines != null)
            {
                marked = ScheduleBuilderReroutedTrips.MarkRerouted(lines, trip);
                if (!marked)
                    marked = ScheduleBuilderReroutedTrips.MarkReroutedAnyTab(_fsLinesByTab, trip);
                if (marked)
                    FsTrackReroutedTripKey(trip.TripNumber);
                FsCommitPreviewLinesForTab(sourceTab, lines);
            }
        }

        private void FsSetReroutedTripKeyCache(IEnumerable<string> tripNumbers)
        {
            _fsReroutedTripKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (tripNumbers == null)
                return;
            foreach (string raw in tripNumbers)
                ScheduleBuilderReroutedTrips.AddTripNumberKey(_fsReroutedTripKeys, raw);
        }

        private void FsTrackReroutedTripKey(string tripNumber)
        {
            if (_fsReroutedTripKeys == null)
                _fsReroutedTripKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ScheduleBuilderReroutedTrips.AddTripNumberKey(_fsReroutedTripKeys, tripNumber);
        }

        private void FsUntrackReroutedTripKey(string tripNumber)
        {
            if (_fsReroutedTripKeys == null || string.IsNullOrWhiteSpace(tripNumber))
                return;
            string key = ScheduleBuilderReroutedTrips.TripNumberKey(tripNumber);
            if (key.Length > 0)
                _fsReroutedTripKeys.Remove(key);
        }

        private void FsTrackReroutedKeysFromLines(IList<ScheduleBuilderPreviewLine> lines)
        {
            if (lines == null)
                return;
            foreach (var line in lines)
            {
                if (line?.Kind != ScheduleBuilderPreviewLine.LineKind.Trip
                    || !line.ReroutedOnModivcare
                    || line.Trip == null)
                    continue;
                FsTrackReroutedTripKey(line.Trip.TripNumber);
            }
        }

        /// <summary>Re-apply cached Modivcare reroute red after preview lines are rebuilt.</summary>
        private void FsReapplyReroutedHighlights()
        {
            if (_fsLinesByTab == null || _fsLinesByTab.Count == 0)
                return;
            if (_fsReroutedTripKeys == null || _fsReroutedTripKeys.Count == 0)
                return;

            ScheduleBuilderReroutedTripsRegistry.MarkReroutedOnPreview(
                _fsLinesByTab, _fsReroutedTripKeys);
        }
    }
}
