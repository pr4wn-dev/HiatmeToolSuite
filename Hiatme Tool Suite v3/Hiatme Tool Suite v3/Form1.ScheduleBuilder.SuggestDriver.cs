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
        internal async Task FsSuggestDriverForTripAsync()
        {
            if (_fsTripsCtxTrip == null || !_fsHasPreview || string.IsNullOrWhiteSpace(_fsActiveDriverTab))
                return;

            if (!ScheduleOsrmGate.PreviewRoutingOk)
            {
                MessageBox.Show(this,
                    "Driver suggestions need road routing (OSRM).\r\n\r\n"
                    + "Start the office AI server and Maine OSRM, then try again.",
                    "Schedule Builder",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            MCDownloadedTrip trip = _fsTripsCtxTrip;
            string sourceTab = _fsActiveDriverTab;
            string tripLabel = (trip.TripNumber ?? "").Trim();
            if (tripLabel.Length == 0)
                tripLabel = (trip.ClientFullName ?? "trip").Trim();

            UseWaitCursor = true;
            SetScheduleBuilderStatus("Finding driver suggestions for trip " + tripLabel + "…");
            try
            {
                EnsureFsDriverRosterLoaded();
                var progress = new Progress<ScheduleBuilderDriverSuggestProgress>(p =>
                {
                    if (p == null)
                        return;
                    switch (p.Phase)
                    {
                        case "geocode":
                            SetScheduleBuilderStatus("Suggest driver · geocoding trips for " + tripLabel + "…");
                            break;
                        case "driver":
                            SetScheduleBuilderStatus(
                                "Suggest driver · checking " + p.DriverIndex + "/" + p.DriverTotal
                                + " · " + p.DriverDisplayName + "…");
                            break;
                        case "rank":
                            SetScheduleBuilderStatus("Suggest driver · ranking placements for " + tripLabel + "…");
                            break;
                    }
                });

                var suggestions = await ScheduleBuilderDriverSuggest.SuggestAsync(
                    trip,
                    sourceTab,
                    _fsLinesByTab,
                    _supeyRoster,
                    fsbdatepicker?.Value,
                    progress,
                    CancellationToken.None).ConfigureAwait(true);

                if (suggestions.Count == 0)
                {
                    MessageBox.Show(this,
                        "No driver suggestions found for this trip.\r\n\r\n"
                        + "No feasible merge into an existing clinic wave, and no open batch slot "
                        + "where the driver is free before this pickup window closes.",
                        "Schedule Builder",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    SetScheduleBuilderStatus("No driver suggestions for trip " + tripLabel + ".");
                    return;
                }

                using (var dlg = new ScheduleDriverSuggestForm(
                    suggestions,
                    trip,
                    sourceTab,
                    _fsLinesByTab,
                    FsShowGroupColorsEnabled,
                    tripLabel))
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK)
                    {
                        SetScheduleBuilderStatus("Driver suggestion cancelled.");
                        return;
                    }

                    await ApplyFsDriverSuggestionAsync(trip, sourceTab, dlg.CurrentSuggestion).ConfigureAwait(true);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "Could not compute driver suggestions.\r\n\r\n" + ex.Message,
                    "Schedule Builder",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                SetScheduleBuilderStatus("Driver suggestion failed.");
            }
            finally
            {
                UseWaitCursor = false;
            }
        }

        private async Task ApplyFsDriverSuggestionAsync(
            MCDownloadedTrip trip,
            string sourceTab,
            ScheduleBuilderDriverSuggestion suggestion)
        {
            if (trip == null || suggestion == null || string.IsNullOrWhiteSpace(suggestion.DriverTab))
                return;

            string targetTab = suggestion.DriverTab;
            if (!_fsLinesByTab.TryGetValue(sourceTab, out var sourceLines) || sourceLines == null)
                return;

            bool rerouted = ScheduleBuilderReroutedTrips.IsMarked(sourceLines, trip);
            bool fromReserves = sourceTab.Equals("Reserves", StringComparison.OrdinalIgnoreCase);
            Color? reserveBand = fromReserves ? null : FsFindTripReserveBand(sourceLines, trip);

            FsPushUndoSnapshot("suggest driver → " + targetTab);

            if (!ScheduleBuilderPreviewDrag.TryRemoveTrip(sourceLines, trip))
            {
                SetScheduleBuilderStatus("Could not remove trip from current tab.");
                return;
            }

            FsCommitPreviewLinesForTab(sourceTab, sourceLines);

            if (!_fsLinesByTab.TryGetValue(targetTab, out var targetLines) || targetLines == null)
                targetLines = new List<ScheduleBuilderPreviewLine>();

            ScheduleBuilderDriverSuggest.ApplyPlacementToLines(
                targetLines, trip, suggestion, reserveBand, rerouted);

            FsCommitPreviewLinesForTab(targetTab, targetLines);

            SelectFsDriverTab(targetTab);
            ShowFsTripsForTab(targetTab);
            SelectFsTripInListView(trip);
            SyncFsPreviewCsvsForExport();
            await FsRefreshAfterTripMoveAsync().ConfigureAwait(true);

            string num = (trip.TripNumber ?? "").Trim();
            SetScheduleBuilderStatus(string.IsNullOrEmpty(num)
                ? "Trip moved to " + suggestion.DriverDisplayName + " — map updating…"
                : "Trip " + num + " moved to " + suggestion.DriverDisplayName + " — map updating…");
        }
    }
}
