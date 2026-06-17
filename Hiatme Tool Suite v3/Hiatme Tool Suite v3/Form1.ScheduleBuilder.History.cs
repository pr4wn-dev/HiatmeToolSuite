using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    public partial class Form1
    {
        private HiatmeArchiveStatusResponse _fsArchiveStatus;

        private async Task FsSyncHistoryNowAsync()
        {
            if (_fsSyncHistoryBtn != null)
                _fsSyncHistoryBtn.Enabled = false;

            SetScheduleBuilderStatus("Syncing historical schedules to AI archive…");
            try
            {
                var settings = HiatmeAiSettings.Load();
                var sync = await HiatmeAiClient.SyncArchiveAsync(settings, force: false).ConfigureAwait(true);
                if (sync == null)
                {
                    SetScheduleBuilderStatus("History sync unavailable — AI panel offline.");
                    return;
                }

                await FsRefreshArchiveStatusAsync(reportOffline: false).ConfigureAwait(true);
                string days = _fsArchiveStatus != null
                    ? _fsArchiveStatus.IngestedDays.ToString()
                    : (sync.IngestedDaysTotal > 0 ? sync.IngestedDaysTotal.ToString() : "?");
                string upd = sync.UpdatedServiceDates != null && sync.UpdatedServiceDates.Count > 0
                    ? " · updated " + string.Join(", ", sync.UpdatedServiceDates.Take(4))
                        + (sync.UpdatedServiceDates.Count > 4 ? "…" : "")
                    : "";
                SetScheduleBuilderStatus(
                    "History sync complete — "
                    + sync.NewOrChangedFiles + " file(s), "
                    + days + " archived day(s)." + upd);
            }
            catch (Exception ex)
            {
                SetScheduleBuilderStatus("History sync failed.");
                MessageBox.Show(this,
                    "Could not sync schedule history.\r\n\r\n" + ex.Message,
                    "Schedule Builder",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            finally
            {
                if (_fsSyncHistoryBtn != null)
                    _fsSyncHistoryBtn.Enabled = true;
            }
        }

        private async Task<bool> FsRefreshArchiveStatusAsync(bool reportOffline)
        {
            try
            {
                var settings = HiatmeAiSettings.Load();
                var status = await HiatmeAiClient.GetArchiveStatusAsync(settings).ConfigureAwait(true);
                if (status == null)
                {
                    if (reportOffline)
                        SetScheduleBuilderStatus("History sync unavailable — AI panel offline.");
                    return false;
                }

                _fsArchiveStatus = status;
                if (_fsSyncHistoryBtn != null)
                {
                    _fsSyncHistoryBtn.Text = "SYNC HISTORY (" + Math.Max(0, status.IngestedDays) + ")";
                }
                return true;
            }
            catch
            {
                if (reportOffline)
                    SetScheduleBuilderStatus("History status unavailable.");
                return false;
            }
        }
    }
}
