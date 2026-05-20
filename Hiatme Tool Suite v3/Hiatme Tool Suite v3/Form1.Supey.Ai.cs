using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MaterialSkin.Controls;
using Newtonsoft.Json.Linq;

namespace Hiatme_Tool_Suite_v3
{
    public partial class Form1
    {
        private SupeyCollapsiblePanel _supeyAiCollapsible;
        private TextBox _supeyAiTranscript;
        private TextBox _supeyAiPrompt;
        private MaterialButton _supeyAiSendBtn;
        private MaterialLabel _supeyAiStatusLbl;
        private MaterialLabel _supeyAiLastAppliedLbl;
        private MaterialButton _supeyAiGoodBtn;
        private MaterialButton _supeyAiBadBtn;
        private HiatmeAiSettings _supeyAiSettings;
        private string _supeyAiLastTraceId;
        private CancellationTokenSource _supeyAiCts;

        private void BuildSupeyAiPanel()
        {
            _supeyAiSettings = HiatmeAiSettings.Load();
            HiatmeAiClient.SyncTemplatesFireAndForget(_supeyAiSettings);

            _supeyAiCollapsible = new SupeyCollapsiblePanel
            {
                Title = "AI Assistant",
                Dock = DockStyle.Right,
                ExpandedWidth = 340,
            };

            var host = _supeyAiCollapsible.ContentPanel;
            host.BackColor = Color.FromArgb(30, 30, 30);
            host.Padding = new Padding(6);

            _supeyAiStatusLbl = new MaterialLabel
            {
                Dock = DockStyle.Top,
                Height = 36,
                ForeColor = Color.Silver,
                Text = "Server: " + (_supeyAiSettings.BaseUrl ?? ""),
                AutoSize = false,
            };

            _supeyAiLastAppliedLbl = new MaterialLabel
            {
                Dock = DockStyle.Top,
                Height = 28,
                ForeColor = Color.Gray,
                Text = "List not updated by AI yet.",
                AutoSize = false,
                Font = new Font("Segoe UI", 8.5f),
            };

            var settingsBtn = MakeFlatButton("SETTINGS", 0, 0, 90);
            settingsBtn.Dock = DockStyle.Top;
            settingsBtn.Height = 32;
            settingsBtn.Click += (s, e) => OnSupeyAiSettingsClicked();

            var hintLbl = new Label
            {
                Dock = DockStyle.Top,
                Height = 44,
                ForeColor = Color.Gray,
                Text = "BUILD first, then talk here. The AI answers in this box; the trip list updates on its own.",
                Font = new Font("Segoe UI", 8f),
            };

            var feedbackRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 36,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
            };
            _supeyAiGoodBtn = MakeFlatButton("GOOD", 0, 0, 64);
            _supeyAiGoodBtn.Click += async (s, e) => await OnSupeyAiFeedbackAsync(1);
            _supeyAiBadBtn = MakeFlatButton("BAD", 0, 0, 64);
            _supeyAiBadBtn.Type = MaterialButton.MaterialButtonType.Outlined;
            _supeyAiBadBtn.UseAccentColor = false;
            _supeyAiBadBtn.NoAccentTextColor = Color.Gainsboro;
            _supeyAiBadBtn.Click += async (s, e) => await OnSupeyAiFeedbackAsync(-1);
            feedbackRow.Controls.Add(_supeyAiGoodBtn);
            feedbackRow.Controls.Add(_supeyAiBadBtn);

            var btnRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 40,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
            };

            _supeyAiSendBtn = MakeFlatButton("SEND", 0, 0, 72);
            _supeyAiSendBtn.Click += async (s, e) => await OnSupeyAiSendClickedAsync();

            btnRow.Controls.Add(_supeyAiSendBtn);

            _supeyAiPrompt = new TextBox
            {
                Dock = DockStyle.Bottom,
                Height = 56,
                Multiline = true,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9f),
                ScrollBars = ScrollBars.Vertical,
            };
            _supeyAiPrompt.KeyDown += async (s, e) =>
            {
                if (e.KeyCode == Keys.Enter && e.Control)
                {
                    e.SuppressKeyPress = true;
                    await OnSupeyAiSendClickedAsync();
                }
            };

            _supeyAiTranscript = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                BackColor = Color.FromArgb(28, 28, 28),
                ForeColor = Color.Gainsboro,
                BorderStyle = BorderStyle.None,
                Font = new Font("Consolas", 8.5f),
                ScrollBars = ScrollBars.Vertical,
            };

            host.Controls.Add(_supeyAiTranscript);
            host.Controls.Add(_supeyAiPrompt);
            host.Controls.Add(btnRow);
            host.Controls.Add(feedbackRow);
            host.Controls.Add(hintLbl);
            host.Controls.Add(settingsBtn);
            host.Controls.Add(_supeyAiLastAppliedLbl);
            host.Controls.Add(_supeyAiStatusLbl);

            _ = RefreshSupeyAiConnectionStatusAsync();
        }

        private async Task RefreshSupeyAiConnectionStatusAsync()
        {
            if (_supeyAiStatusLbl == null || _supeyAiSettings == null) return;
            _supeyAiStatusLbl.Text = "Checking " + (_supeyAiSettings.BaseUrl ?? "") + "…";
            bool ok = false;
            try
            {
                ok = await HiatmeAiClient.PingAsync(_supeyAiSettings).ConfigureAwait(true);
            }
            catch
            {
                ok = false;
            }

            int notes = 0;
            if (ok)
                notes = await HiatmeAiClient.GetMemoryCountAsync(_supeyAiSettings).ConfigureAwait(true);

            _supeyAiStatusLbl.ForeColor = ok ? Color.LightGreen : Color.IndianRed;
            _supeyAiStatusLbl.Text = (ok ? "Online — " : "Offline — ") + (_supeyAiSettings.BaseUrl ?? "")
                + (ok ? " · " + notes + " lessons" : "");
        }

        private async Task OnSupeyAiFeedbackAsync(int rating)
        {
            if (_supeyAiSettings == null) _supeyAiSettings = HiatmeAiSettings.Load();
            var note = (_supeyAiPrompt?.Text ?? "").Trim();
            if (string.IsNullOrEmpty(note) && _supeyResult != null)
                note = HiatmeScheduleSummary.ForMemory(_supeyResult);
            if (string.IsNullOrEmpty(note))
            {
                SetSupeyStatus("Type a note (or build first) before Good/Bad.");
                return;
            }
            try
            {
                await HiatmeAiClient.SendFeedbackAsync(
                    _supeyAiSettings, rating, note, _supeyAiLastTraceId).ConfigureAwait(true);
                AppendSupeyAiTranscript("System",
                    (rating > 0 ? "Marked good: " : "Marked bad: ") + note);
                SetSupeyStatus("Feedback saved.");
                _ = RefreshSupeyAiConnectionStatusAsync();
            }
            catch (Exception ex)
            {
                AppendSupeyAiTranscript("Error", ex.Message);
            }
        }

        private void OnSupeyAiSettingsClicked()
        {
            using (var dlg = new Form())
            {
                dlg.Text = "AIagent connection";
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.ClientSize = new Size(420, 200);
                dlg.BackColor = Color.FromArgb(40, 40, 40);
                dlg.ForeColor = Color.White;

                var urlLbl = new Label { Text = "Server URL", Location = new Point(12, 16), AutoSize = true };
                var urlTb = new TextBox
                {
                    Location = new Point(12, 36),
                    Width = 390,
                    Text = _supeyAiSettings?.BaseUrl ?? "",
                    BackColor = Color.FromArgb(55, 55, 55),
                    ForeColor = Color.White,
                };
                var tokLbl = new Label { Text = "API token (optional)", Location = new Point(12, 68), AutoSize = true };
                var tokTb = new TextBox
                {
                    Location = new Point(12, 88),
                    Width = 390,
                    Text = _supeyAiSettings?.ApiToken ?? "",
                    BackColor = Color.FromArgb(55, 55, 55),
                    ForeColor = Color.White,
                };
                var saveMemChk = new CheckBox
                {
                    Text = "Remember schedule when I SAVE workbook",
                    Location = new Point(12, 118),
                    Width = 380,
                    Checked = _supeyAiSettings?.RememberOnSave ?? true,
                    ForeColor = Color.White,
                };
                var ok = MakeFlatButton("SAVE", 12, 148, 100);
                ok.Click += (s, e) =>
                {
                    _supeyAiSettings.BaseUrl = urlTb.Text.Trim();
                    _supeyAiSettings.ApiToken = tokTb.Text.Trim();
                    _supeyAiSettings.RememberOnSave = saveMemChk.Checked;
                    _supeyAiSettings.Save();
                    dlg.DialogResult = DialogResult.OK;
                    _ = RefreshSupeyAiConnectionStatusAsync();
                };
                dlg.Controls.Add(urlLbl);
                dlg.Controls.Add(urlTb);
                dlg.Controls.Add(tokLbl);
                dlg.Controls.Add(tokTb);
                dlg.Controls.Add(saveMemChk);
                dlg.Controls.Add(ok);
                dlg.ShowDialog(this);
            }
        }

        internal void AppendSupeyAiTranscriptIfPresent(string role, string text)
        {
            if (_supeyAiTranscript == null || string.IsNullOrWhiteSpace(text)) return;
            AppendSupeyAiTranscript(role, text);
        }

        private void AppendSupeyAiTranscript(string role, string text)
        {
            if (_supeyAiTranscript == null) return;
            _supeyAiTranscript.AppendText(
                "[" + DateTime.Now.ToString("HH:mm") + "] " + role + ":\r\n" + text + "\r\n\r\n");
            _supeyAiTranscript.SelectionStart = _supeyAiTranscript.TextLength;
            _supeyAiTranscript.ScrollToCaret();
        }

        private void AppendSupeyAiReply(string role, string thinking, string message)
        {
            var t = (thinking ?? "").Trim();
            if (!string.IsNullOrEmpty(t))
                AppendSupeyAiTranscript(role + " · thinking", t);
            if (!string.IsNullOrWhiteSpace(message))
                AppendSupeyAiTranscript(role, message);
        }

        private void AppendSupeyAiWorking(string role)
        {
            AppendSupeyAiTranscript(role + " · thinking", "…");
        }

        private void ApplySupeyAiSchedule(HiatmeAiBuildResponse aiResp, string transcriptRole)
        {
            ApplySupeyAiSchedule(aiResp?.Schedule, aiResp?.Message, aiResp?.Thinking, transcriptRole);
        }

        private void ApplySupeyAiSchedule(HiatmeAiScheduleBody schedule, string message, string transcriptRole)
        {
            ApplySupeyAiSchedule(schedule, message, null, transcriptRole);
        }

        private void ApplySupeyAiSchedule(
            HiatmeAiScheduleBody schedule,
            string message,
            string thinking,
            string transcriptRole)
        {
            var selected = GetCheckedSupeyDrivers();
            var date = _supeyDatePicker.Value;
            _supeyTripsPanelView = SupeyTripsPanelView.AiSchedule;
            _supeyResult = HiatmeAiScheduleMapper.ToSupeyScheduleResult(
                schedule, date, selected, _supeyLoadedTrips, message);
            int scheduled = HiatmeAiScheduleMapper.CountAssignedTrips(_supeyResult);
            BindSupeyPreview();
            _ = HydrateSupeyGeocodeForMapAsync();
            var hints = new SupeyTemplateHints(date.DayOfWeek.ToString());
            _supeyLastTemplateCompare = SupeyTemplateCompare.Run(_supeyResult, hints);
            if (_supeyTemplateCompareLbl != null)
                _supeyTemplateCompareLbl.Text = _supeyLastTemplateCompare.SummaryText;
            AppendSupeyAiReply(transcriptRole, thinking, message ?? "Done — check the trip list.");
            var syncSource = (transcriptRole ?? "").IndexOf("build", StringComparison.OrdinalIgnoreCase) >= 0
                ? "build"
                : "update";
            SyncSupeyScheduleToServer(syncSource);
            MarkSupeyScheduleUpdated(transcriptRole);
            SetSupeyAiLastAppliedLabel(transcriptRole);
            SetSupeyStatus(scheduled > 0
                ? "Schedule updated · " + scheduled + " trip(s) on screen. Save when ready."
                : "Schedule updated but no trips matched — check warnings, then BUILD again.");
        }

        private static bool IsAiScheduleUpdate(string mode)
        {
            return string.Equals(mode, "update", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mode, "revise", StringComparison.OrdinalIgnoreCase);
        }

        private void AppendSupeyAiServerWarnings(HiatmeAiMessageResponse resp)
        {
            if (resp?.Warnings == null || resp.Warnings.Count == 0) return;
            AppendSupeyAiTranscript("AI · note", string.Join(Environment.NewLine, resp.Warnings));
        }

        private void SetSupeyAiLastAppliedLabel(string source)
        {
            if (_supeyAiLastAppliedLbl == null) return;
            string when = DateTime.Now.ToString("h:mm tt");
            string text = "List updated " + when;
            if (!string.IsNullOrWhiteSpace(source))
                text += " · " + source.Trim();
            _supeyAiLastAppliedLbl.Text = text;
            _supeyAiLastAppliedLbl.ForeColor = Color.FromArgb(144, 238, 144);
        }

        private JObject BuildSupeyAiContext()
        {
            bool hasBuild = _supeyResult != null;
            var ctx = HiatmeScheduleContextBuilder.Build(
                _supeyDatePicker.Value,
                _supeyRoster,
                _supeyLoadedTrips,
                _supeyResult,
                hasBuild,
                GetCheckedSupeyDrivers());
            ApplyWellRydeDispatcherToAiContext(ctx);
            return ctx;
        }

        /// <summary>WellRyde login name so the server AI knows who is at the desk.</summary>
        private async Task HydrateSupeyGeocodeForMapAsync()
        {
            if (_supeyResult == null) return;
            try
            {
                SetSupeyStatus("Geocoding trips for map…");
                await SupeyScheduleGeocoder.HydrateResultAsync(
                    _supeyResult, _supeyAiCts?.Token ?? default).ConfigureAwait(true);
                BindSupeyPreview();
                SetSupeyStatus("Map pins ready · click a trip to focus PU/DO.");
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
            catch (Exception ex)
            {
                SetSupeyStatus("Geocode for map failed: " + ex.Message);
            }
        }

        private void ApplyWellRydeDispatcherToAiContext(JObject ctx)
        {
            if (ctx == null) return;
            string user = null;
            string company = null;
            if (loginCB != null && loginCB.SelectedIndex != 2)
            {
                company = (loginCodeTB?.Text ?? "").Trim();
                user = (loginUserTB?.Text ?? "").Trim();
            }
            if (string.IsNullOrWhiteSpace(user))
                user = (Properties.Settings.Default.wrUserName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(company))
                company = (Properties.Settings.Default.wrCompanyCode ?? "").Trim();
            if (string.IsNullOrWhiteSpace(user))
                WellRydeDispatcherIdentity.ApplyFromSettings(ctx);
            else
                WellRydeDispatcherIdentity.ApplyToContext(ctx, user, company);
        }

        private void SyncSupeyScheduleToServer(string source)
        {
            if (_supeyResult == null) return;
            if (_supeyAiSettings == null)
                _supeyAiSettings = HiatmeAiSettings.Load();
            var ctx = BuildSupeyAiContext();
            HiatmeAiClient.SyncScheduleFireAndForget(_supeyAiSettings, ctx, source);
        }

        private async Task OnSupeyAiSendClickedAsync()
        {
            if (_supeyAiCts != null) return;
            var message = (_supeyAiPrompt?.Text ?? "").Trim();
            if (string.IsNullOrEmpty(message))
            {
                SetSupeyStatus("Enter a message.");
                return;
            }

            _supeyAiCts = new CancellationTokenSource();
            AppendSupeyAiTranscript("You", message);
            _supeyAiSendBtn.Enabled = false;

            try
            {
                if (_supeyAiSettings == null)
                    _supeyAiSettings = HiatmeAiSettings.Load();

                var ctx = BuildSupeyAiContext();
                SetSupeyStatus("AI thinking...");
                AppendSupeyAiWorking("AI");

                var resp = await HiatmeAiClient.SendMessageAsync(
                    _supeyAiSettings, ctx, message, _supeyAiCts.Token).ConfigureAwait(true);

                if (resp == null)
                    throw new InvalidOperationException("Empty response from server.");

                _supeyAiLastTraceId = resp.TraceId;
                string mode = resp.Mode ?? "chat";

                if (IsAiScheduleUpdate(mode) && resp.Schedule != null)
                {
                    ApplySupeyAiSchedule(resp.Schedule, resp.Message, resp.Thinking, "AI");
                    AppendSupeyAiServerWarnings(resp);
                    SetSupeyStatus("Trip list updated · " + DateTime.Now.ToString("h:mm tt"));
                }
                else
                {
                    AppendSupeyAiReply("AI", resp.Thinking, resp.Message ?? "");
                    SetSupeyStatus(string.Equals(mode, "chat", StringComparison.OrdinalIgnoreCase)
                        ? "AI replied (list unchanged)."
                        : "AI replied.");
                }
                _supeyAiPrompt.Clear();
                _ = RefreshSupeyAiConnectionStatusAsync();
            }
            catch (OperationCanceledException)
            {
                AppendSupeyAiTranscript("System", "Canceled.");
            }
            catch (Exception ex)
            {
                AppendSupeyAiTranscript("Error", ex.Message);
                SetSupeyStatus("AI failed.");
            }
            finally
            {
                _supeyAiCts?.Dispose();
                _supeyAiCts = null;
                _supeyAiSendBtn.Enabled = true;
            }
        }

    }
}
