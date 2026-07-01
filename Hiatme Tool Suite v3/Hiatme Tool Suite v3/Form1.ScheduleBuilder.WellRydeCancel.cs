using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    public partial class Form1
    {
        /// <summary>
        /// Pull WellRyde trip list for the service date and highlight cancelled trips on every tab.
        /// Fails soft when WellRyde is unavailable — schedule load/build continues normally.
        /// </summary>
        private async Task<string> FsSyncCancelledTripsFromWellRydeAsync(
            DateTime serviceDate,
            bool refreshListView = true)
        {
            if (fsbuilder == null || _fsLinesByTab == null || _fsLinesByTab.Count == 0)
                return "";

            void ProbeStatus(string text)
            {
                SetScheduleBuilderStatus(text);
                UpdateTabLoadingOverlayMessage(tabPage6, text);
            }

            ProbeStatus("Checking cancelled trips on WellRyde…");

            List<WRDownloadedTrip> trips = null;
            try
            {
                var load = await LoadWellRydeTripsForDateWithAuthRetryAsync(
                    serviceDate,
                    backgroundSilent: true).ConfigureAwait(true);
                if (!load.result.IsSuccess || load.trips == null)
                {
                    _fsWellRydeCancelledKeys = null;
                    ScheduleBuilderWellRydeCancelled.ApplyCancelledFlagsToPreview(_fsLinesByTab, null);
                    if (refreshListView)
                        FsSyncReroutedHighlightsFromPreviewLines();
                    return " Cancel check skipped — WellRyde unavailable.";
                }

                trips = load.trips;
            }
            catch
            {
                _fsWellRydeCancelledKeys = null;
                ScheduleBuilderWellRydeCancelled.ApplyCancelledFlagsToPreview(_fsLinesByTab, null);
                if (refreshListView)
                    FsSyncReroutedHighlightsFromPreviewLines();
                return " Cancel check skipped — WellRyde unavailable.";
            }

            var cancelledKeys = ScheduleBuilderWellRydeCancelled.CollectCancelledTripKeys(trips);
            _fsWellRydeCancelledKeys = cancelledKeys;
            ScheduleBuilderWellRydeCancelled.ApplyCancelledFlagsToPreview(_fsLinesByTab, cancelledKeys);

            if (refreshListView)
                FsSyncReroutedHighlightsFromPreviewLines();

            int highlighted = ScheduleBuilderWellRydeCancelled.CountMarkedOnPreview(_fsLinesByTab);
            if (highlighted == 0)
                return "";

            return " Cancel check — " + highlighted + " trip" + (highlighted == 1 ? "" : "s")
                + " cancelled on WellRyde (highlighted).";
        }

        /// <summary>Re-apply cached WellRyde cancel flags after preview lines are rebuilt.</summary>
        private void FsReapplyWellRydeCancelledHighlights()
        {
            if (_fsLinesByTab == null || _fsLinesByTab.Count == 0)
                return;
            if (_fsWellRydeCancelledKeys == null || _fsWellRydeCancelledKeys.Count == 0)
                return;

            ScheduleBuilderWellRydeCancelled.ApplyCancelledFlagsToPreview(
                _fsLinesByTab, _fsWellRydeCancelledKeys);
        }
    }
}
