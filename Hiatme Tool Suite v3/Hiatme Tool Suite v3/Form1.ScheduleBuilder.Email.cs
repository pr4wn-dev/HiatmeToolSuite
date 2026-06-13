using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    public partial class Form1
    {
        private SupeyButton _fsEmailSchedulesBtn;

        private void SetFsPreviewExportButtonsEnabled(bool enabled)
        {
            if (_fsSaveBtn != null)
                _fsSaveBtn.Enabled = enabled;
            if (_fsEmailSchedulesBtn != null)
                _fsEmailSchedulesBtn.Enabled = enabled;
        }

        private void WireFsEmailSchedulesButton(Panel host)
        {
            _fsEmailSchedulesBtn = new SupeyButton
            {
                Text = "EMAIL SCHEDULES",
                Kind = SupeyButton.Variant.Secondary,
                Size = new System.Drawing.Size(148, 26),
                Margin = new Padding(6, 0, 0, 0),
                Enabled = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
            };
            _fsEmailSchedulesBtn.Click += async (s, e) => await FsEmailSchedulesBtn_ClickAsync();

            var tip = SupeyToolTip.Create(autoPopDelay: 14000, initialDelay: 400);
            tip.SetToolTip(_fsEmailSchedulesBtn,
                "Email each driver the full schedule workbook (.xlsx, all tabs) using Gmail. "
                + "Set Gmail credentials in Login → Gmail (use a Google App Password if 2-step verification is on). "
                + "Driver emails come from the roster (Pull from WellRyde or edit driver).");

            host.Controls.Add(_fsEmailSchedulesBtn);
            host.Resize += (s, e) => PositionFsEmailSchedulesButton();
            PositionFsEmailSchedulesButton();
        }

        private void PositionFsEmailSchedulesButton()
        {
            if (_fsEmailSchedulesBtn == null || _fsDriverTabStrip == null)
                return;

            int pad = 4;
            _fsEmailSchedulesBtn.Location = new System.Drawing.Point(
                Math.Max(pad, _fsDriverTabStrip.ClientSize.Width - _fsEmailSchedulesBtn.Width - pad),
                pad);
            _fsEmailSchedulesBtn.BringToFront();

            if (_fsDriverTabFlow != null)
                _fsDriverTabFlow.Padding = new Padding(0, 0, _fsEmailSchedulesBtn.Width + pad * 2, 0);
        }

        private bool TryGetGmailCredentialsForMailer(out string address, out string password)
        {
            address = (Properties.Settings.Default.gmailUserName ?? "").Trim();
            password = Properties.Settings.Default.gmailUserPass ?? "";

            if (loginCB != null && loginCB.SelectedIndex == 3)
            {
                string onScreenUser = (loginUserTB?.Text ?? "").Trim();
                string onScreenPass = loginPassTB?.Text ?? "";

                if (!string.IsNullOrEmpty(onScreenUser))
                    address = onScreenUser;
                if (!string.IsNullOrEmpty(onScreenPass))
                    password = onScreenPass;
            }

            return !string.IsNullOrEmpty(address) && !string.IsNullOrEmpty(password);
        }

        private async Task FsEmailSchedulesBtn_ClickAsync()
        {
            if (fsbuilder == null || !_fsHasPreview)
            {
                SetScheduleBuilderStatus("Build or load a schedule first, then email drivers.");
                return;
            }

            if (!TryGetGmailCredentialsForMailer(out string gmailUser, out string gmailPass))
            {
                MessageBox.Show(this,
                    "Gmail credentials are not saved.\r\n\r\n"
                    + "Open Login → Gmail, enter your Gmail address and password (or App Password), "
                    + "turn on Remember credentials, and click Test login.",
                    "Schedule Builder",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            EnsureFsDriverRosterLoaded();
            ScheduleBuilderDriverEmailsRegistry.ApplyLocalRegistryToRoster(_supeyRoster);

            if (fsbdatepicker != null)
                fsbuilder.ApplyServiceDate(fsbdatepicker.Value);

            await SyncFsDriverEmailsAsync(reportOffline: true).ConfigureAwait(true);

            DateTime serviceDate = fsbuilder.ServiceDate;
            var driverTabs = _fsDriverTabOrder
                .Where(n => !string.IsNullOrWhiteSpace(n)
                    && !n.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (driverTabs.Count == 0)
            {
                MessageBox.Show(this,
                    "There are no driver tabs to email.",
                    "Schedule Builder",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            var sendPlan = new List<(string TabName, string Email, string DisplayName)>();
            var missingEmail = new List<string>();

            foreach (string tab in driverTabs)
            {
                var profile = ScheduleBuilderDriverMapRouting.FindProfileForScheduleTab(_supeyRoster, tab);
                string email = (profile?.Email ?? "").Trim();
                string display = profile?.Name ?? tab;

                if (string.IsNullOrEmpty(email))
                {
                    missingEmail.Add(tab);
                    continue;
                }

                sendPlan.Add((tab, email, display));
            }

            if (sendPlan.Count == 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine("No drivers are ready to email.");
                sb.AppendLine();
                sb.AppendLine("Add an email for each driver on the roster (Pull from WellRyde or Edit driver).");
                if (missingEmail.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("Missing email for:");
                    foreach (string n in missingEmail)
                        sb.AppendLine("  · " + n);
                }

                MessageBox.Show(this, sb.ToString(), "Schedule Builder",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var confirm = new StringBuilder();
            confirm.AppendLine("Send the full schedule workbook for " + serviceDate.ToString("MMMM d, yyyy") + "?");
            confirm.AppendLine("(Same .xlsx — all driver tabs — attached to each email.)");
            confirm.AppendLine();
            confirm.AppendLine("From: " + gmailUser);
            confirm.AppendLine("Recipients (" + sendPlan.Count + "):");
            foreach (var item in sendPlan)
                confirm.AppendLine("  · " + item.DisplayName + " → " + item.Email);
            if (missingEmail.Count > 0)
            {
                confirm.AppendLine();
                confirm.AppendLine("Skipped (no email on roster): " + string.Join(", ", missingEmail));
            }

            if (MessageBox.Show(this, confirm.ToString(), "Email driver schedules",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            SyncFsPreviewCsvsForExport();
            var exportOptions = MakeFsPreviewCsvExportOptions();

            SetFsPreviewExportButtonsEnabled(false);
            if (_fsBuildBtn != null) _fsBuildBtn.Enabled = false;
            if (_fsLoadBtn != null) _fsLoadBtn.Enabled = false;

            string tempRoot = Path.Combine(
                Path.GetTempPath(),
                "HiatmeScheduleEmail_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            int sent = 0;
            var failures = new List<string>();

            try
            {
                string workbookFileName = ScheduleExportPaths.WorkbookFileName(
                    serviceDate.ToString("MMMM"),
                    serviceDate.Day,
                    serviceDate.Year);
                string attachmentPath = Path.Combine(tempRoot, workbookFileName);

                SetScheduleBuilderStatus("Building schedule workbook…");
                var tabs = ScheduleBuilderPreviewCsvExport.BuildWorkbookTabs(_fsLinesByTab, exportOptions);
                ScheduleBuilderXlsxWriter.WriteWorkbookFromTabs(attachmentPath, tabs);

                foreach (var item in sendPlan)
                {
                    SetScheduleBuilderStatus("Emailing " + item.DisplayName + "…");

                    try
                    {
                        await ScheduleBuilderGmailMailer.SendDriverScheduleAsync(
                            gmailUser,
                            gmailPass,
                            item.Email,
                            item.DisplayName,
                            serviceDate,
                            attachmentPath).ConfigureAwait(true);

                        sent++;
                    }
                    catch (Exception ex)
                    {
                        failures.Add(item.DisplayName + ": " + ex.Message);
                    }
                }

                if (failures.Count == 0)
                {
                    SetScheduleBuilderStatus("Emailed " + sent + " driver schedule" + (sent == 1 ? "" : "s") + ".");
                    MessageBox.Show(this,
                        "Sent " + sent + " schedule email" + (sent == 1 ? "" : "s") + ".",
                        "Schedule Builder",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                else
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("Sent " + sent + " of " + sendPlan.Count + ".");
                    sb.AppendLine();
                    sb.AppendLine("Failed:");
                    foreach (string f in failures)
                        sb.AppendLine("  · " + f);

                    SetScheduleBuilderStatus("Email finished with errors — see message.");
                    MessageBox.Show(this, sb.ToString(), "Schedule Builder",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempRoot))
                        Directory.Delete(tempRoot, recursive: true);
                }
                catch
                {
                    /* ignore temp cleanup */
                }

                EnableScheduleBuilderInputs(true);
                if (_fsHasPreview)
                    SetFsPreviewExportButtonsEnabled(true);
            }
        }
    }
}
