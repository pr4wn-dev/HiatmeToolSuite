using System;
using System.Collections.Generic;
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
        private Label _supeyAiPromptPlaceholder;
        private SupeyButton _supeyAiSendBtn;
        private SupeyStatusPill _supeyAiStatusPill;
        private SupeyStatusPill _supeyAiLessonsPill;
        private Label _supeyAiUrlLbl;
        private MaterialLabel _supeyAiStatusLbl;       // legacy hidden alias for back-compat
        private Label _supeyAiLastAppliedLbl;
        private SupeyButton _supeyAiGoodBtn;
        private SupeyButton _supeyAiBadBtn;
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
            host.BackColor = SupeyTheme.Surface;
            host.Padding = new Padding(10, 10, 10, 10);

            // ── Header row 1: connection pill + lessons badge + ⚙ ─────────────
            // The previous header was a flat label saying "Online — http://… · 2"
            // which read like a console log. Now we have proper UI components: a
            // colored-dot status pill ("● Connected"), an info-only lessons badge
            // ("2 lessons") to the right of it, and a clickable ⚙ button anchored
            // far right. The URL goes on its own muted caption row below.
            var headerRow1 = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 26,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = SupeyTheme.Surface,
                Padding = new Padding(0),
            };
            _supeyAiStatusPill = new SupeyStatusPill
            {
                Label = "Connecting…",
                DotColor = SupeyTheme.TextMuted,
                Margin = new Padding(0, 1, 6, 0),
            };
            _supeyAiLessonsPill = new SupeyStatusPill
            {
                ShowDot = false,
                Label = "",
                PillBackColor = SupeyTheme.SurfaceElevated,
                PillBorderColor = SupeyTheme.BorderSubtle,
                ForeColor = SupeyTheme.TextSecondary,
                Visible = false,
                Margin = new Padding(0, 1, 0, 0),
            };
            // ⚙ rendered as a 24×24 hover button so it actually feels clickable.
            var settingsBtn = new Label
            {
                Width = 26,
                Height = 24,
                Text = "⚙",
                Font = new Font("Segoe UI", 12f),
                ForeColor = SupeyTheme.TextSecondary,
                BackColor = SupeyTheme.Surface,
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand,
                Margin = new Padding(8, 0, 0, 0),
            };
            settingsBtn.MouseEnter += (s, e) => settingsBtn.ForeColor = SupeyTheme.TextPrimary;
            settingsBtn.MouseLeave += (s, e) => settingsBtn.ForeColor = SupeyTheme.TextSecondary;
            settingsBtn.Click += (s, e) => OnSupeyAiSettingsClicked();
            // Note: settingsBtn lives in headerRow1 too so the user can click it inline.
            // We add it after the pills so RTL would put it last; with LTR it floats to
            // the right via a stretch spacer.
            var headerSpacer = new Panel
            {
                BackColor = SupeyTheme.Surface,
                Height = 1,
                Width = 1,
                AutoSize = false,
            };
            headerRow1.Controls.Add(_supeyAiStatusPill);
            headerRow1.Controls.Add(_supeyAiLessonsPill);
            headerRow1.Controls.Add(headerSpacer);
            headerRow1.Controls.Add(settingsBtn);
            // Push the gear to the right edge by sizing the spacer on resize.
            headerRow1.Resize += (s, e) =>
            {
                int taken = _supeyAiStatusPill.Width + _supeyAiStatusPill.Margin.Right
                            + (_supeyAiLessonsPill.Visible ? _supeyAiLessonsPill.Width + _supeyAiLessonsPill.Margin.Right : 0)
                            + settingsBtn.Width + settingsBtn.Margin.Left;
                int avail = headerRow1.ClientSize.Width - taken;
                headerSpacer.Width = Math.Max(1, avail);
            };

            // ── Header row 2: URL caption ─────────────────────────────────────
            _supeyAiUrlLbl = new Label
            {
                Dock = DockStyle.Top,
                Height = 18,
                AutoEllipsis = true,
                ForeColor = SupeyTheme.TextMuted,
                BackColor = SupeyTheme.Surface,
                Font = SupeyTheme.CaptionFont,
                Text = _supeyAiSettings.BaseUrl ?? "",
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(0, 4, 0, 0),
            };

            // Legacy alias kept (other code paths still write to _supeyAiStatusLbl).
            // Hidden 1×1 control off-screen so any stale assignments are no-ops.
            _supeyAiStatusLbl = new MaterialLabel
            {
                Visible = false,
                Width = 1,
                Height = 1,
                Location = new Point(-100, -100),
            };

            _supeyAiLastAppliedLbl = new Label
            {
                Dock = DockStyle.Top,
                Height = 22,
                ForeColor = SupeyTheme.TextMuted,
                BackColor = SupeyTheme.Surface,
                Text = "No updates from AI yet",
                AutoSize = false,
                Font = SupeyTheme.CaptionFont,
                Padding = new Padding(0, 4, 0, 0),
                TextAlign = ContentAlignment.MiddleLeft,
            };

            var headerSep = new Panel
            {
                Dock = DockStyle.Top,
                Height = 1,
                Margin = new Padding(0, 6, 0, 0),
                BackColor = SupeyTheme.Divider,
            };

            // ── Composer (bottom): prompt-card + action row ──────────────────
            // The composer is what the user sees when they want to talk to the AI. It
            // contains a clearly-bordered prompt area (so people know exactly where to
            // click) and an action row underneath. The previous version had a borderless
            // textbox blending into the surrounding panel, which made the input area
            // invisible.
            var composer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 124,
                BackColor = SupeyTheme.Surface,
                Padding = new Padding(0, 8, 0, 0),
                Margin = new Padding(0, 8, 0, 0),
            };

            var actionRow = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 36,
                BackColor = SupeyTheme.Surface,
                Padding = new Padding(0, 6, 0, 0),
            };

            // SEND is the primary call-to-action — accent fill so it visually outranks
            // the outlined GOOD/BAD buttons. When the user looks at this row their eye
            // should land on SEND first.
            _supeyAiSendBtn = new SupeyButton
            {
                Text = "SEND",
                Kind = SupeyButton.Variant.Primary,
                Size = new Size(96, 30),
                Dock = DockStyle.Right,
                Margin = new Padding(0),
            };
            _supeyAiSendBtn.Click += async (s, e) => await OnSupeyAiSendClickedAsync();

            var feedbackFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Left,
                Width = 160,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = SupeyTheme.Surface,
            };
            _supeyAiGoodBtn = new SupeyButton
            {
                Text = "GOOD",
                Kind = SupeyButton.Variant.Outlined,
                ForeColor = SupeyTheme.SuccessText,
                Size = new Size(72, 30),
                Margin = new Padding(0, 0, 6, 0),
            };
            _supeyAiGoodBtn.Click += async (s, e) => await OnSupeyAiFeedbackAsync(1);
            _supeyAiBadBtn = new SupeyButton
            {
                Text = "BAD",
                Kind = SupeyButton.Variant.Outlined,
                ForeColor = SupeyTheme.ErrorText,
                Size = new Size(72, 30),
                Margin = new Padding(0),
            };
            _supeyAiBadBtn.Click += async (s, e) => await OnSupeyAiFeedbackAsync(-1);
            feedbackFlow.Controls.Add(_supeyAiGoodBtn);
            feedbackFlow.Controls.Add(_supeyAiBadBtn);

            actionRow.Controls.Add(_supeyAiSendBtn);
            actionRow.Controls.Add(feedbackFlow);

            // Prompt card = elevated surface with a 1px border. Inside it: borderless
            // textbox + a placeholder label that hides on focus. Looks like a real
            // input field, not a flat text run.
            var promptCard = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.SurfaceElevated,
                Padding = new Padding(1),
                Margin = new Padding(0),
            };
            // 1px border by stacking a colored panel under a slightly inset child.
            var promptInner = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.SurfaceElevated,
                Padding = new Padding(8, 6, 8, 6),
            };
            // The bordered look comes from giving the OUTER panel the divider color and
            // letting the 1px Padding expose it as a hairline frame.
            promptCard.BackColor = SupeyTheme.BorderSubtle;
            promptCard.Controls.Add(promptInner);

            _supeyAiPrompt = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                BackColor = SupeyTheme.SurfaceElevated,
                ForeColor = SupeyTheme.TextPrimary,
                BorderStyle = BorderStyle.None,
                Font = SupeyTheme.BodyFont,
                ScrollBars = ScrollBars.Vertical,
            };
            _supeyAiPromptPlaceholder = new Label
            {
                Text = "Ask the AI to adjust this schedule…  (Ctrl+Enter to send)",
                Dock = DockStyle.Fill,
                ForeColor = SupeyTheme.TextMuted,
                BackColor = Color.Transparent,
                Font = SupeyTheme.BodyFont,
                TextAlign = ContentAlignment.TopLeft,
                Padding = new Padding(0, 0, 0, 0),
            };
            _supeyAiPromptPlaceholder.Click += (s, e) => _supeyAiPrompt.Focus();
            _supeyAiPrompt.GotFocus += (s, e) => _supeyAiPromptPlaceholder.Visible = false;
            _supeyAiPrompt.LostFocus += (s, e) =>
                _supeyAiPromptPlaceholder.Visible = string.IsNullOrEmpty(_supeyAiPrompt.Text);
            _supeyAiPrompt.TextChanged += (s, e) =>
                _supeyAiPromptPlaceholder.Visible = !_supeyAiPrompt.Focused
                    && string.IsNullOrEmpty(_supeyAiPrompt.Text);
            _supeyAiPrompt.KeyDown += async (s, e) =>
            {
                if (e.KeyCode == Keys.Enter && e.Control)
                {
                    e.SuppressKeyPress = true;
                    await OnSupeyAiSendClickedAsync();
                }
            };

            promptInner.Controls.Add(_supeyAiPromptPlaceholder);
            promptInner.Controls.Add(_supeyAiPrompt);
            _supeyAiPromptPlaceholder.BringToFront();

            composer.Controls.Add(promptCard);
            composer.Controls.Add(actionRow);

            // ── Center: transcript ───────────────────────────────────────────
            _supeyAiTranscript = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                BackColor = SupeyTheme.SurfaceBase,
                ForeColor = SupeyTheme.TextPrimary,
                BorderStyle = BorderStyle.None,
                Font = SupeyTheme.MonoFont,
                ScrollBars = ScrollBars.Vertical,
            };
            // Wrap the transcript in a 1px-bordered card so it visually anchors as the
            // main reading surface of the panel.
            var transcriptCard = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.Divider,
                Padding = new Padding(1),
            };
            transcriptCard.Controls.Add(_supeyAiTranscript);

            // Dock order: Fill is added FIRST so all top/bottom siblings get their slot
            // before the transcript fills the remainder. Tops stack outermost-last:
            // headerRow1 ends up at the very top, then URL caption below it, then the
            // last-applied caption, then the divider; transcript fills; composer docks
            // bottom.
            host.Controls.Add(transcriptCard);
            host.Controls.Add(composer);
            host.Controls.Add(headerSep);
            host.Controls.Add(_supeyAiLastAppliedLbl);
            host.Controls.Add(_supeyAiUrlLbl);
            host.Controls.Add(headerRow1);

            _ = RefreshSupeyAiConnectionStatusAsync();
        }

        private async Task RefreshSupeyAiConnectionStatusAsync()
        {
            if (_supeyAiSettings == null) return;
            // Set both pills to a "checking" state so the user gets immediate feedback
            // that we're working on it, instead of a stale "Online" left over from the
            // previous refresh.
            if (_supeyAiStatusPill != null)
            {
                _supeyAiStatusPill.Label = "Connecting…";
                _supeyAiStatusPill.DotColor = SupeyTheme.TextMuted;
            }
            if (_supeyAiUrlLbl != null)
                _supeyAiUrlLbl.Text = _supeyAiSettings.BaseUrl ?? "";

            bool ok = false;
            string pingDetail = "";
            try
            {
                ok = await HiatmeAiClient.PingAsync(_supeyAiSettings).ConfigureAwait(true);
                if (!ok)
                    pingDetail = "Panel not reachable at " + (_supeyAiSettings.BaseUrl ?? "");
            }
            catch (Exception ex)
            {
                ok = false;
                pingDetail = ex.Message;
            }

            if (_supeyAiStatusPill != null)
            {
                _supeyAiStatusPill.Label = ok ? "Connected" : "Offline";
                _supeyAiStatusPill.DotColor = ok ? SupeyTheme.SuccessText : SupeyTheme.ErrorText;
                _supeyAiStatusPill.ForeColor = SupeyTheme.TextPrimary;
                _supeyAiStatusPill.Tag = pingDetail;
            }
            if (_supeyAiLessonsPill != null && !ok)
                _supeyAiLessonsPill.Visible = false;

            if (ok)
            {
                _ = RefreshSupeyRulesPanelAsync();
                _ = RefreshSupeyAiLessonsCountAsync();
            }
            // Force the spacer to recompute now that pill widths may have changed.
            _supeyAiStatusPill?.Parent?.PerformLayout();
        }

        private async Task RefreshSupeyAiLessonsCountAsync()
        {
            if (_supeyAiSettings == null || _supeyAiLessonsPill == null) return;
            try
            {
                int notes = await HiatmeAiClient.GetMemoryCountAsync(_supeyAiSettings).ConfigureAwait(true);
                if (notes > 0)
                {
                    _supeyAiLessonsPill.Label = notes + " lesson" + (notes == 1 ? "" : "s");
                    _supeyAiLessonsPill.Visible = true;
                }
                else
                    _supeyAiLessonsPill.Visible = false;
                _supeyAiLessonsPill.Parent?.PerformLayout();
            }
            catch
            {
                /* optional badge */
            }
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
                var dispatchPayload = new JObject
                {
                    ["note"] = note,
                    ["trace_id"] = _supeyAiLastTraceId ?? "",
                    ["rating"] = rating,
                };
                if (_supeyResult != null)
                {
                    dispatchPayload["service_date"] = _supeyDatePicker.Value.ToString("yyyy-MM-dd");
                    dispatchPayload["summary"] = HiatmeScheduleSummary.ForMemory(_supeyResult);
                }
                await HiatmeAiClient.SendDispatchFeedbackKindAsync(
                    _supeyAiSettings,
                    rating > 0 ? "good" : "bad",
                    dispatchPayload).ConfigureAwait(true);
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

        /// <summary>
        /// AI panel → schedule JSON → trip list + map. Tool Suite does not assign trips here;
        /// it only maps <c>trip_numbers</c> to loaded Modivcare rows and refreshes UI.
        /// </summary>
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
            // JSON is applied to the trip list only — do not dump it in the transcript.
            AppendSupeyAiTranscript(transcriptRole, TranscriptForScheduleApplied(message, thinking));
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

        /// <summary>Short dispatcher text only — never raw schedule JSON in the chat pane.</summary>
        private static string TranscriptForScheduleApplied(string message, string thinking)
        {
            var m = (message ?? "").Trim();
            if (!string.IsNullOrEmpty(m) && !LooksLikeScheduleJson(m) && m.Length <= 600)
                return m;
            if (!string.IsNullOrEmpty(m) && !LooksLikeScheduleJson(m))
                return m.Substring(0, 600) + "…";
            return "Schedule loaded on the trip list — edit or Save when ready.";
        }

        private static bool LooksLikeScheduleJson(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            var s = text.Trim();
            if (s.IndexOf('{') < 0) return false;
            return s.IndexOf("\"drivers\"", StringComparison.OrdinalIgnoreCase) >= 0
                || s.IndexOf("\"trip_numbers\"", StringComparison.OrdinalIgnoreCase) >= 0
                || s.IndexOf("\"schedule\"", StringComparison.OrdinalIgnoreCase) >= 0
                || s.IndexOf("\"reserves\"", StringComparison.OrdinalIgnoreCase) >= 0;
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

        /// <summary>
        /// Post the locally-built schedule to the company AI for review/commentary only.
        /// Server response is shown as a chat reply; it does NOT replace the schedule on screen.
        /// </summary>
        private async Task RequestAiBuildReviewAsync(
            DateTime serviceDate,
            IList<SupeyDriverProfile> selected,
            SupeyScheduleResult result,
            HiatmeAiSettings settings)
        {
            if (result == null || settings == null) return;
            try
            {
                AppendSupeyAiTranscriptIfPresent("AI · review", "Looking over the route…");
                var ctx = HiatmeScheduleContextBuilder.Build(
                    serviceDate, _supeyRoster, _supeyLoadedTrips, result, true, selected);
                ApplyWellRydeDispatcherToAiContext(ctx);
                int onScreen = HiatmeAiScheduleMapper.CountAssignedTrips(result);
                long geoHits = AddressGeocoder.CacheHits;
                long geoMisses = AddressGeocoder.CacheMisses;
                string ask =
                    "I just built today's schedule with the local route builder. " +
                    onScreen + " trip(s) on " + result.DriverPlans.Count + " driver(s), " +
                    result.Reserves.Count + " in reserves. " +
                    "Geocode this run: " + geoHits + " from cache, " + geoMisses +
                    " new from Nominatim (saved to the shared cache for next time). " +
                    "Review the assignments and tell me in 1-3 short sentences: anything to swap, " +
                    "client→driver bindings I should remember, reserves that look reassignable, " +
                    "addresses in warnings_text I should hand-fix the pin for, or — if " +
                    "_recent_driver_moves is set — trips assigned to a moved driver that no " +
                    "longer fit their new home and should be reassigned.";
                var resp = await HiatmeAiClient.SendMessageAsync(settings, ctx, ask).ConfigureAwait(true);
                if (resp != null)
                {
                    AppendSupeyAiReply("AI · review", resp.Thinking, resp.Message ?? "");
                    AppendSupeyAiServerWarnings(resp);
                    _supeyAiLastTraceId = resp.TraceId;
                }
            }
            catch (Exception ex)
            {
                AppendSupeyAiTranscript("AI · review", "(skipped: " + ex.Message + ")");
            }
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
                _ = RefreshSupeyRulesPanelAsync();

                if (resp.ProposedAddressChange != null &&
                    string.Equals(resp.ProposedAddressChange.Kind, "queued", StringComparison.OrdinalIgnoreCase))
                {
                    HandleProposedAddressChange(resp.ProposedAddressChange);
                }
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

        /// <summary>
        /// Path B: AI extracted "X moved to Y" from the dispatcher's chat. Show a
        /// confirmation dialog; on Apply write the new home into the roster + persist
        /// it + tell the server. We never silently mutate driver fields.
        /// </summary>
        private void HandleProposedAddressChange(HiatmeProposedAddressChange change)
        {
            if (change == null || string.IsNullOrWhiteSpace(change.Id) ||
                string.IsNullOrWhiteSpace(change.DriverName) ||
                change.ProposedHome == null)
                return;

            var driver = ResolveSupeyDriverByName(change.DriverName);
            if (driver == null)
            {
                AppendSupeyAiTranscript(
                    "AI",
                    "Caught a possible move for '" + change.DriverName +
                    "' but couldn't match that to a driver in the roster — ignored.");
                _ = HiatmeAiClient.RejectAddressChangeAsync(
                    _supeyAiSettings, change.Id, "auto:no_match");
                return;
            }

            string body =
                "The AI thinks " + change.DriverName + " just moved.\r\n\r\n" +
                "Current home: " + (change.CurrentHomePretty ?? "(unknown)") + "\r\n" +
                "Proposed:     " + (change.ProposedHomePretty ?? "(unknown)") + "\r\n\r\n" +
                "Apply this update to the driver roster?\r\n" +
                "(Source: \"" + Trunc(change.SourceMessage, 200) + "\")";

            var dlg = MessageBox.Show(
                this, body, "Driver address change",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);

            if (dlg != DialogResult.Yes)
            {
                _ = HiatmeAiClient.RejectAddressChangeAsync(
                    _supeyAiSettings, change.Id, GetWellRydeDispatcherName());
                AppendSupeyAiTranscript("AI", "Address update for " + change.DriverName + " — ignored.");
                return;
            }

            driver.HomeStreet = (change.ProposedHome.Street ?? "").Trim();
            driver.HomeCity = (change.ProposedHome.City ?? "").Trim();
            driver.HomeState = string.IsNullOrWhiteSpace(change.ProposedHome.State)
                ? "ME" : change.ProposedHome.State.Trim();
            driver.HomeZip = (change.ProposedHome.Zip ?? "").Trim();

            SaveSupeyRosterToDisk(false);
            try { RebuildSupeyDriversList(); } catch { }

            _ = HiatmeAiClient.ApproveAddressChangeAsync(
                _supeyAiSettings, change.Id, GetWellRydeDispatcherName());

            AppendSupeyAiTranscript(
                "AI",
                "Updated " + change.DriverName + "'s home to " +
                (change.ProposedHomePretty ?? "(new address)") + ". Next BUILD will route from there.");
        }

        private SupeyDriverProfile ResolveSupeyDriverByName(string name)
        {
            string target = (name ?? "").Trim();
            if (target.Length == 0 || _supeyRoster == null) return null;
            foreach (var d in _supeyRoster)
            {
                if (d == null) continue;
                if (string.Equals(d.Name, target, StringComparison.OrdinalIgnoreCase))
                    return d;
            }
            string targetCollapsed = string.Join(" ", target.Split(
                new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
            foreach (var d in _supeyRoster)
            {
                if (d == null || string.IsNullOrWhiteSpace(d.Name)) continue;
                string n = string.Join(" ", d.Name.Split(
                    new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
                if (n == targetCollapsed) return d;
            }
            return null;
        }

        private string GetWellRydeDispatcherName()
        {
            try { return (Properties.Settings.Default.wrUserName ?? "").Trim(); }
            catch { return ""; }
        }

        private static string Trunc(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }

        private async Task ShowSupeyPreReviewWarningsAsync()
        {
            if (_supeyAiSettings == null)
                _supeyAiSettings = HiatmeAiSettings.Load();
            try
            {
                var pre = await HiatmeAiClient.PreReviewAsync(_supeyAiSettings).ConfigureAwait(true);
                if (pre?.Warnings == null || pre.Warnings.Count == 0) return;
                foreach (var w in pre.Warnings)
                    AppendSupeyAiTranscriptIfPresent("AI · rules", w);
                SetSupeyStatus(pre.Warnings.Count + " standing rule reminder(s) — see Rules panel.");
            }
            catch
            {
                /* panel optional */
            }
            await RefreshSupeyRulesPanelAsync().ConfigureAwait(true);
        }

    }
}
