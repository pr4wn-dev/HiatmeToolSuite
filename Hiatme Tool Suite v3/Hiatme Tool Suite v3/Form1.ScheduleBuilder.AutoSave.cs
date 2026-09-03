using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Hiatme_Tool_Suite_v3.Properties;

namespace Hiatme_Tool_Suite_v3
{
    public partial class Form1
    {
        private const int FsAutoSaveDebounceMs = 45_000;
        private const int FsAutoSaveMaxIntervalMs = 300_000;

        private bool _fsAutoSaveDirty;
        private bool _fsAutoSaveInProgress;
        private bool _fsScheduleBuilderExportBusy;
        private DateTime? _fsLastAutoSaveLocal;

        private System.Windows.Forms.Timer _fsAutoSaveDebounceTimer;
        private System.Windows.Forms.Timer _fsAutoSaveMaxTimer;

        private Label _fsAutoSaveHintLbl;
        private bool _fsAppShuttingDown;

        private bool FsAutoSaveIsOn => Settings.Default.FsAutoSave;

        private void InitializeFsAutoSave()
        {
            _fsAutoSaveDebounceTimer = new System.Windows.Forms.Timer { Interval = FsAutoSaveDebounceMs };
            _fsAutoSaveDebounceTimer.Tick += (_, __) =>
            {
                _fsAutoSaveDebounceTimer.Stop();
                _ = FsTryAutoSaveAsync();
            };

            _fsAutoSaveMaxTimer = new System.Windows.Forms.Timer { Interval = FsAutoSaveMaxIntervalMs };
            _fsAutoSaveMaxTimer.Tick += (_, __) => _ = FsTryAutoSaveAsync();

            tabPage6.VisibleChanged += (_, __) =>
            {
                if (_fsAppShuttingDown || tabPage6 == null || !tabPage6.Visible)
                    return;
                _ = FsTryAutoSaveOnTabLeaveAsync();
            };

            FsUpdateAutoSaveHint();
        }

        private void FsStopAutoSaveTimers()
        {
            try
            {
                _fsAutoSaveDebounceTimer?.Stop();
                _fsAutoSaveMaxTimer?.Stop();
            }
            catch
            {
                // ignore
            }
        }

        internal void FsAutoSaveBeforeShutdown()
        {
            if (!FsAutoSaveIsOn || !FsScheduleBuilderHasUnsavedChanges())
                return;

            _fsAppShuttingDown = true;
            FsStopAutoSaveTimers();

            try
            {
                WaitForScheduleExportIdle(maxMs: 15000);
                FsExportScheduleWorkbookShutdownSync();
            }
            catch
            {
                // best effort on exit — never block or crash shutdown
            }
        }

        private void WaitForScheduleExportIdle(int maxMs)
        {
            var deadline = Environment.TickCount + Math.Max(0, maxMs);
            while (_fsScheduleBuilderExportBusy && Environment.TickCount < deadline)
                Thread.Sleep(50);
        }

        /// <summary>
        /// Synchronous export for app exit only. Async save uses ConfigureAwait(true) and deadlocks
        /// if GetResult is called on the UI thread during FormClosing.
        /// </summary>
        private void FsExportScheduleWorkbookShutdownSync()
        {
            if (fsbuilder == null || !_fsHasPreview || _fsScheduleBuilderExportBusy)
                return;

            _fsScheduleBuilderExportBusy = true;
            try
            {
                if (fsbdatepicker != null && !fsbdatepicker.IsDisposed)
                    fsbuilder.ApplyServiceDate(fsbdatepicker.Value);

                string path = FsResolveScheduleBuilderSavePath(allowDefault: true);
                if (string.IsNullOrWhiteSpace(path))
                    return;

                fsbuilder.PreferredExportPath = path;
                SyncFsPreviewCsvsForExport();

                fsbuilder.CreateWorkbookAsync(promptForLocation: false, openAfterSave: false)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();

                if (string.IsNullOrEmpty(fsbuilder.LastExportPath))
                    return;

                _fsPreferredSavePath = fsbuilder.LastExportPath;
                _fsAutoSaveDirty = false;
                FsUploadSavedWorkbookToServer(fsbuilder.LastExportPath);
            }
            finally
            {
                _fsScheduleBuilderExportBusy = false;
            }
        }

        private void FsUploadSavedWorkbookToServer(string workbookPath)
        {
            if (string.IsNullOrWhiteSpace(workbookPath) || !File.Exists(workbookPath))
                return;

            var settings = HiatmeAiSettings.Load();
            if (settings == null || string.IsNullOrWhiteSpace(settings.BaseUrl))
                return;

            DateTime serviceDate = fsbdatepicker?.Value.Date ?? DateTime.Today;
            if (fsbuilder != null)
            {
                try { serviceDate = fsbuilder.ServiceDate; }
                catch { /* keep picker date */ }
            }

            string iso = serviceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            HiatmeAiClient.UploadScheduleWorkbookFireAndForget(
                settings, iso, workbookPath, "schedule_builder_save");
        }

        private void FsMarkScheduleBuilderDirty()
        {
            if (!_fsHasPreview)
                return;

            _fsAutoSaveDirty = true;
            FsUpdateAutoSaveHint();

            if (!FsAutoSaveIsOn)
                return;

            _fsAutoSaveDebounceTimer?.Stop();
            _fsAutoSaveDebounceTimer?.Start();
            if (_fsAutoSaveMaxTimer != null && !_fsAutoSaveMaxTimer.Enabled)
                _fsAutoSaveMaxTimer.Start();
        }

        private void FsClearScheduleBuilderDirtyAfterSave()
        {
            _fsAutoSaveDirty = false;
            _fsAutoSaveDebounceTimer?.Stop();
            _fsAutoSaveMaxTimer?.Stop();
            FsUpdateAutoSaveHint();
        }

        private bool FsScheduleBuilderHasUnsavedChanges() =>
            _fsHasPreview && _fsAutoSaveDirty;

        private string FsResolveScheduleBuilderSavePath(bool allowDefault = false)
        {
            if (!string.IsNullOrWhiteSpace(_fsPreferredSavePath))
                return _fsPreferredSavePath;

            if (fsbuilder != null && !string.IsNullOrWhiteSpace(fsbuilder.LastExportPath))
                return fsbuilder.LastExportPath;

            if (!allowDefault)
                return null;

            DateTime serviceDate = fsbdatepicker?.Value.Date ?? DateTime.Today;
            if (fsbuilder != null)
            {
                try { serviceDate = fsbuilder.ServiceDate; }
                catch { /* keep picker date */ }
            }

            string month = serviceDate.ToString("MMMM");
            ScheduleExportPaths.GetDefaultWorkbookSaveLocation(
                month,
                serviceDate.Day.ToString(),
                serviceDate.Year.ToString(),
                out string yearFolder,
                out _,
                out string fullPath);

            try
            {
                Directory.CreateDirectory(yearFolder);
            }
            catch
            {
                // ignore — write will fail with a clearer error
            }

            return fullPath;
        }

        private void FsUpdateAutoSaveHint()
        {
            if (_fsAppShuttingDown)
                return;

            if (_fsAutoSaveHintLbl == null || _fsAutoSaveHintLbl.IsDisposed)
                return;

            if (!FsAutoSaveIsOn)
            {
                _fsAutoSaveHintLbl.Text = "";
                return;
            }

            if (_fsAutoSaveInProgress)
            {
                _fsAutoSaveHintLbl.Text = "AutoSave · Saving…";
                return;
            }

            if (FsScheduleBuilderHasUnsavedChanges())
            {
                _fsAutoSaveHintLbl.Text = "AutoSave · Unsaved changes";
                return;
            }

            if (_fsLastAutoSaveLocal.HasValue)
            {
                _fsAutoSaveHintLbl.Text = "AutoSave · Saved " + _fsLastAutoSaveLocal.Value.ToString("h:mm tt");
                return;
            }

            _fsAutoSaveHintLbl.Text = "AutoSave on";
        }

        private async Task FsTryAutoSaveOnTabLeaveAsync()
        {
            if (!FsAutoSaveIsOn || !FsScheduleBuilderHasUnsavedChanges())
                return;

            await FsTryAutoSaveAsync(force: true).ConfigureAwait(true);
        }

        private async Task FsTryAutoSaveAsync(bool force = false)
        {
            if (_fsAppShuttingDown)
                return;

            if (!FsAutoSaveIsOn)
                return;

            if (!force && !FsScheduleBuilderHasUnsavedChanges())
                return;

            if (_fsAutoSaveInProgress || _fsScheduleBuilderExportBusy || _fsAssignRunning)
                return;

            if (fsbuilder == null || !_fsHasPreview)
                return;

            string path = FsResolveScheduleBuilderSavePath(allowDefault: true);
            if (string.IsNullOrWhiteSpace(path))
                return;

            _fsAutoSaveInProgress = true;
            FsUpdateAutoSaveHint();
            try
            {
                bool ok = await FsExportScheduleWorkbookCoreAsync(
                    promptForLocation: false,
                    openAfterSave: false,
                    reportStatus: null).ConfigureAwait(true);

                if (ok)
                {
                    _fsLastAutoSaveLocal = DateTime.Now;
                    FsClearScheduleBuilderDirtyAfterSave();
                }
            }
            catch (ScheduleBuilderException ex) when (FsIsWorkbookFileLockError(ex))
            {
                if (_fsAutoSaveHintLbl != null && !_fsAutoSaveHintLbl.IsDisposed)
                    _fsAutoSaveHintLbl.Text = "AutoSave · File in use";
            }
            catch
            {
                if (_fsAutoSaveHintLbl != null && !_fsAutoSaveHintLbl.IsDisposed)
                    _fsAutoSaveHintLbl.Text = "AutoSave · Save failed";
            }
            finally
            {
                _fsAutoSaveInProgress = false;
                FsUpdateAutoSaveHint();
            }
        }

        private static bool FsIsWorkbookFileLockError(ScheduleBuilderException ex)
        {
            if (ex?.InnerException == null)
                return false;

            return ScheduleBuilderXlsxWriter.IsFileLockError(ex.InnerException);
        }

        /// <summary>Shared workbook export for manual SAVE, BUILD, and auto-save.</summary>
        private async Task<bool> FsExportScheduleWorkbookCoreAsync(
            bool promptForLocation,
            bool openAfterSave,
            Action<string> reportStatus)
        {
            if (fsbuilder == null || !_fsHasPreview)
                return false;

            if (_fsScheduleBuilderExportBusy)
                return false;

            _fsScheduleBuilderExportBusy = true;
            try
            {
                reportStatus?.Invoke("Preparing export…");

                if (fsbdatepicker != null)
                    fsbuilder.ApplyServiceDate(fsbdatepicker.Value);

                if (!promptForLocation)
                {
                    string path = FsResolveScheduleBuilderSavePath(allowDefault: true);
                    if (string.IsNullOrWhiteSpace(path))
                        return false;

                    fsbuilder.PreferredExportPath = path;
                }

                SyncFsPreviewCsvsForExport();
                reportStatus?.Invoke("Saving schedule…");

                await fsbuilder.CreateWorkbookAsync(promptForLocation, openAfterSave).ConfigureAwait(false);

                if (string.IsNullOrEmpty(fsbuilder.LastExportPath))
                    return false;

                _fsPreferredSavePath = fsbuilder.LastExportPath;
                FsUploadSavedWorkbookToServer(fsbuilder.LastExportPath);
                return true;
            }
            finally
            {
                _fsScheduleBuilderExportBusy = false;
            }
        }
    }
}
