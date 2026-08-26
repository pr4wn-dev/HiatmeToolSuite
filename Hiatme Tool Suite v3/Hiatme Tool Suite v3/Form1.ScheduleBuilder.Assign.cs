using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    public partial class Form1
    {
        private bool _fsAssignRunning;

        private void SetFsAssignBusy(bool busy, string statusMessage)
        {
            _fsAssignRunning = busy;
            EnableScheduleBuilderInputs(!busy);
            SetFsPreviewExportButtonsEnabled(_fsHasPreview && !busy);

            if (!string.IsNullOrWhiteSpace(statusMessage))
                SetScheduleBuilderStatus(statusMessage);

            if (_fsAssignBtn != null && !_fsAssignBtn.IsDisposed)
                _fsAssignBtn.Text = busy ? "ASSIGNING…" : "ASSIGN";

            UseWaitCursor = busy;
        }

        private async Task FsAssignBtn_ClickAsync()
        {
            if (_fsAssignRunning)
                return;

            if (fsbuilder == null || !_fsHasPreview)
            {
                SetScheduleBuilderStatus("Build or load a schedule first, then click ASSIGN.");
                return;
            }

            DateTime serviceDate = fsbdatepicker != null ? fsbdatepicker.Value.Date : DateTime.Today;

            SetFsAssignBusy(true, "Preparing WellRyde assign…");
            SupeyWellRydeAssignProgressForm assignProgress = null;
            Analyzer.UpdateLoadingScreenHandler onAssignStatus = text =>
            {
                if (string.IsNullOrWhiteSpace(text))
                    return;
                SetScheduleBuilderStatus(text);
                try
                {
                    assignProgress?.SetStatus(text);
                }
                catch
                {
                    // progress form may already be closed
                }
            };
            analyzer.UpdateLoadingScreen += onAssignStatus;

            try
            {
                fsbuilder.SyncPreviewDriverLinesFromUi(_fsLinesByTab);

                SetScheduleBuilderStatus("Connecting to WellRyde…");
                if (await EnsureWellRydePortalSessionForBillingAsync().ConfigureAwait(true))
                    analyzer.SetWellRydePortalSession(_wellRydeSession);
                else
                {
                    analyzer.SetWellRydePortalSession(null);
                    SupeyMessageDialog.ShowWarning(
                        this,
                        "Assign",
                        "WellRyde is not connected",
                        "Sign in to WellRyde, then try ASSIGN again.");
                    SetScheduleBuilderStatus("Assign cancelled — WellRyde not connected.");
                    return;
                }

                SetScheduleBuilderStatus("Connecting to Modivcare for assign checks…");
                await EnsureModivcareSessionAsync(msg =>
                    SetScheduleBuilderStatus(string.IsNullOrWhiteSpace(msg) ? "Connecting to Modivcare…" : msg))
                    .ConfigureAwait(true);

                analyzer.IntializeAnalyzer(mcLoginHandler);
                SetScheduleBuilderStatus("Checking trips (same rules as Analyze)…");
                await analyzer.ApplyAlertsToScheduleBuilderAsync(fsbuilder, serviceDate).ConfigureAwait(true);

                FsAutoSizeAlertsColumnToWidest();
                if (_fsTripsLv != null && !_fsTripsLv.IsDisposed)
                    _fsTripsLv.Invalidate();

                var plan = analyzer.SnapshotWellRydeAssignPlan(serviceDate);
                int assignSlots = plan.SentSlots;
                string dateLabel = serviceDate.ToString("M/d/yyyy");
                string body =
                    "This unassigns ALL Assigned trips on WellRyde for "
                    + dateLabel
                    + ", then assigns "
                    + assignSlots.ToString()
                    + " trip(s) from the driver tabs.\r\n\r\n"
                    + "Same skip rules as Analyze Trips: cancels, dupes, time/address mismatches, "
                    + "and will-calls on a driver page.\r\n\r\n"
                    + "Reserves are not assigned.";

                UseWaitCursor = false;
                DialogResult confirm = SupeyMessageDialog.Confirm(
                    this,
                    SupeyMessageDialog.Kind.Warning,
                    "Assign",
                    "Assign trips for " + dateLabel + " to WellRyde?",
                    body,
                    "Assign",
                    "Cancel");
                if (confirm != DialogResult.Yes)
                {
                    SetScheduleBuilderStatus("Assign cancelled.");
                    return;
                }

                UseWaitCursor = true;
                assignProgress = new SupeyWellRydeAssignProgressForm(serviceDate, assignSlots);
                assignProgress.SetStatus("Assigning trips on WellRyde…");
                SupeyForm.CenterOnWorkingArea(assignProgress, this);
                assignProgress.Show(this);

                try
                {
                    await analyzer.StartTripAssigning(
                        serviceDate.ToLongDateString(),
                        serviceDate.Day,
                        serviceDate.Year,
                        serviceDate).ConfigureAwait(true);
                }
                finally
                {
                    assignProgress.ForceClose();
                    assignProgress = null;
                }

                plan.AssignedOnWellRyde = analyzer.GetAssignedTripCount();
                plan.ReservedOnWellRyde = analyzer.GetReservedTripCount();
                plan.PortalWritesEnabled = Analyzer.WellRydePortalAssignAndUnassignCallsServer;

                SetScheduleBuilderAssignCompleteStatus(plan);

                UseWaitCursor = false;
                SupeyWellRydeAssignResultForm.Show(this, plan);

                // Modal dialog can leave the bottom bar stale — re-apply counts when it closes.
                SetScheduleBuilderAssignCompleteStatus(plan);
            }
            catch (ScheduleAnalysisException ex)
            {
                SetScheduleBuilderStatus("Assign failed — " + ex.Message);
                SupeyMessageDialog.ShowWarning(this, "Assign", "Assign failed", ex.Message);
            }
            catch (Exception ex)
            {
                SetScheduleBuilderStatus("Assign failed.");
                SupeyMessageDialog.ShowWarning(this, "Assign", "Assign failed", ex.Message);
            }
            finally
            {
                analyzer.UpdateLoadingScreen -= onAssignStatus;
                try { assignProgress?.ForceClose(); } catch { }
                SetFsAssignBusy(false, null);
            }
        }
    }
}
