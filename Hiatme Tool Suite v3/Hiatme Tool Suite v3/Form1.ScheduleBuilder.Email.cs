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
                + "Uses the office Gmail account automatically — no login needed. "
                + "Optional: Login → Gmail → enter your own Gmail App Password instead.");

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
            address = string.Empty;
            password = string.Empty;

            bool useOffice = Properties.Settings.Default.gmailUseOfficeDefault;
            if (!useOffice)
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

                ScheduleBuilderGmailMailer.NormalizeCredentials(ref address, ref password);
                if (!string.IsNullOrEmpty(address) && !string.IsNullOrEmpty(password))
                    return true;
            }

            if (ScheduleBuilderGmailDefaults.TryGet(out address, out password))
                return true;

            // Last resort: personal creds even if office mode was on but json missing.
            address = (Properties.Settings.Default.gmailUserName ?? "").Trim();
            password = Properties.Settings.Default.gmailUserPass ?? "";
            ScheduleBuilderGmailMailer.NormalizeCredentials(ref address, ref password);
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
                    "Office Gmail is not set up yet.\r\n\r\n"
                    + "An admin needs to fill in hiatme_config\\gmail_default.json once before distributing the app.\r\n\r\n"
                    + "Or use Login → Gmail to enter your own Gmail App Password.",
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

            var recipientEntries = BuildScheduleEmailRecipientEntries(driverTabs);

            if (recipientEntries.Count == 0)
            {
                MessageBox.Show(this,
                    "No drivers to email.\r\n\r\n"
                    + "Add drivers on the Drivers tab or build/load a schedule first.",
                    "Schedule Builder",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (recipientEntries.All(r => !r.CanSend))
            {
                MessageBox.Show(this,
                    "No drivers have an email on the roster.\r\n\r\n"
                    + "Add emails via Pull from WellRyde or Edit driver on the Drivers tab.",
                    "Schedule Builder",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            List<(string TabName, string Email, string DisplayName)> sendPlan;
            using (var picker = new ScheduleEmailRecipientsForm(recipientEntries, serviceDate, gmailUser))
            {
                if (picker.ShowDialog(this) != DialogResult.OK)
                    return;

                sendPlan = (picker.SelectedRecipients ?? Array.Empty<ScheduleEmailRecipientEntry>())
                    .Where(r => r != null && r.CanSend)
                    .Select(r => (r.TabName, r.Email.Trim(), r.DisplayName ?? r.TabName))
                    .ToList();
            }

            if (sendPlan.Count == 0)
                return;

            var recipients = sendPlan
                .Select(x => (x.Email, x.DisplayName))
                .ToList();
            await FsSendWorkbookEmailsAsync(recipients, serviceDate, gmailUser, gmailPass, showResultPopup: true)
                .ConfigureAwait(true);
        }

        /// <summary>Right-click on schedule preview — email the active driver tab's roster entry.</summary>
        internal async Task FsEmailActiveDriverFromContextAsync()
        {
            if (fsbuilder == null || !_fsHasPreview)
            {
                SetScheduleBuilderStatus("Build or load a schedule first.");
                return;
            }

            string tab = (_fsActiveDriverTab ?? "").Trim();
            if (tab.Length == 0 || tab.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
                return;

            if (!TryGetGmailCredentialsForMailer(out string gmailUser, out string gmailPass))
            {
                MessageBox.Show(this,
                    "Office Gmail is not set up yet.\r\n\r\n"
                    + "An admin needs to fill in hiatme_config\\gmail_default.json once before distributing the app.\r\n\r\n"
                    + "Or use Login → Gmail to enter your own Gmail App Password.",
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

            var profile = ScheduleBuilderDriverMapRouting.FindProfileForScheduleTab(_supeyRoster, tab);
            string email = (profile?.Email ?? "").Trim();
            string displayName = (profile?.Name ?? "").Trim();
            if (displayName.Length == 0)
                displayName = tab;

            if (email.Length == 0)
            {
                MessageBox.Show(this,
                    "This driver has no email on the roster.\r\n\r\n"
                    + "Edit the driver on the Drivers tab or Pull from WellRyde.",
                    "Schedule Builder",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            DateTime serviceDate = fsbuilder.ServiceDate;
            using (var confirm = new ScheduleEmailSendConfirmForm(displayName, email, serviceDate, gmailUser))
            {
                if (confirm.ShowDialog(this) != DialogResult.OK)
                    return;
            }

            await FsSendWorkbookEmailsAsync(
                new List<(string Email, string DisplayName)> { (email, displayName) },
                serviceDate,
                gmailUser,
                gmailPass,
                showResultPopup: false).ConfigureAwait(true);
        }

        private async Task FsSendWorkbookEmailsAsync(
            IList<(string Email, string DisplayName)> recipients,
            DateTime serviceDate,
            string gmailUser,
            string gmailPass,
            bool showResultPopup)
        {
            if (recipients == null || recipients.Count == 0)
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

                foreach (var item in recipients)
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
                    if (showResultPopup)
                    {
                        MessageBox.Show(this,
                            "Sent " + sent + " schedule email" + (sent == 1 ? "" : "s") + ".",
                            "Schedule Builder",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                }
                else
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("Sent " + sent + " of " + recipients.Count + ".");
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

        /// <summary>Schedule tabs first, then custom roster drivers not already on a tab.</summary>
        private List<ScheduleEmailRecipientEntry> BuildScheduleEmailRecipientEntries(
            IReadOnlyList<string> scheduleTabs)
        {
            var entries = new List<ScheduleEmailRecipientEntry>();
            var linkedProfiles = new HashSet<SupeyDriverProfile>();

            foreach (string tab in scheduleTabs ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(tab))
                    continue;

                string tabTrim = tab.Trim();
                var profile = ScheduleBuilderDriverMapRouting.FindProfileForScheduleTab(_supeyRoster, tabTrim);
                if (profile != null)
                    linkedProfiles.Add(profile);

                entries.Add(new ScheduleEmailRecipientEntry
                {
                    TabName = tabTrim,
                    DisplayName = profile?.Name ?? tabTrim,
                    Email = (profile?.Email ?? "").Trim(),
                });
            }

            var tabNames = new HashSet<string>(
                entries.Select(e => e.TabName),
                StringComparer.OrdinalIgnoreCase);

            if (_supeyRoster != null)
            {
                foreach (var profile in _supeyRoster)
                {
                    if (profile == null || linkedProfiles.Contains(profile))
                        continue;

                    string name = (profile.Name ?? "").Trim();
                    string tabKey = (profile.ScheduleTabKey ?? "").Trim();
                    string label = tabKey.Length > 0 ? tabKey : name;
                    if (label.Length == 0 || tabNames.Contains(label))
                        continue;

                    linkedProfiles.Add(profile);
                    tabNames.Add(label);
                    entries.Add(new ScheduleEmailRecipientEntry
                    {
                        TabName = label,
                        DisplayName = name.Length > 0 ? name : label,
                        Email = (profile.Email ?? "").Trim(),
                    });
                }
            }

            return entries;
        }
    }
}
