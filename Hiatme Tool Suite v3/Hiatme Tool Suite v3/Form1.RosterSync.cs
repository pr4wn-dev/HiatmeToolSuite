using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Keeps this PC's van roster in step with the one the panel holds for everybody.
    /// </summary>
    /// <remarks>
    /// Capacity and shift used to be marked "local Supey only" and lived in SupeyDrivers.json
    /// next to the .exe. A dispatcher who set a van to five seats was the only person in the
    /// building who knew: every other copy still said four, because four is the default nobody
    /// ever changed. The solver on one desk would refuse a group the solver on the next desk
    /// would happily take, and neither had any way to notice they disagreed.
    ///
    /// The panel holds those numbers now. This pulls them in when the roster loads and pushes
    /// them back when somebody saves. The local file stays exactly where it was and is still
    /// written on every save — it is the cache that keeps the app working when the panel is
    /// unreachable, which is the one case where being alone with your own numbers is correct.
    /// </remarks>
    public partial class Form1
    {
        private bool _rosterSyncInFlight;

        /// <summary>
        /// Pull the shared roster and fold it into the local one. Quiet on failure by design.
        /// </summary>
        private async Task PullSharedRosterAsync(Action rebuildLists = null)
        {
            if (_rosterSyncInFlight) return;
            if (_supeyRoster == null) return;

            var settings = _globalAiSettings;
            if (settings == null || string.IsNullOrWhiteSpace(settings.BaseUrl))
                return;

            _rosterSyncInFlight = true;
            try
            {
                List<SupeyDriverProfile> shared;
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
                    shared = await HiatmeAiClient.GetRosterAsync(settings, cts.Token).ConfigureAwait(true);

                // Null is "could not ask", which is not the same as "the roster is empty".
                // Blanking capacities somebody set because the panel was down for a minute
                // would be far worse than running on yesterday's numbers.
                if (shared == null || shared.Count == 0)
                    return;

                var merged = SupeyDriverRosterStore.MergeShared(_supeyRoster, shared);
                _supeyRoster.Clear();
                _supeyRoster.AddRange(merged);
                SupeyDriverRosterStore.Save(_supeyRoster);

                if (rebuildLists != null)
                    BeginInvoke(rebuildLists);
            }
            catch
            {
                // A roster that could not be refreshed is not worth interrupting anybody over.
            }
            finally
            {
                _rosterSyncInFlight = false;
            }
        }

        /// <summary>
        /// Send this desk's roster up so the next desk to open reads the same seats.
        /// </summary>
        private void PushSharedRoster()
        {
            var settings = _globalAiSettings;
            if (settings == null || string.IsNullOrWhiteSpace(settings.BaseUrl)) return;
            if (_supeyRoster == null || _supeyRoster.Count == 0) return;

            var snapshot = new List<SupeyDriverProfile>(_supeyRoster);
            string by = ScheduleActivityIdentity.DispatcherName();

            // Saving the roster is a foreground action a person is waiting on; the trip to the
            // panel is not, and a slow network should not make the Save button feel broken.
            _ = Task.Run(async () =>
            {
                try
                {
                    using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
                        await HiatmeAiClient.PushRosterAsync(settings, snapshot, by, cts.Token)
                            .ConfigureAwait(false);
                }
                catch
                {
                    // The local save already succeeded; the next save or pull will reconcile.
                }
            });
        }
    }
}
