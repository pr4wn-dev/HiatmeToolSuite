using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;

namespace Hiatme_Tool_Suite_v3
{
    public partial class Form1
    {
        private const int GlobalAiDockExpandedWidth = 392;
        private const int GlobalAiDockMinWidth = 300;
        private const int GlobalAiDockRightPad = 6;

        private Panel _globalAiDock;
        private Panel _globalAiHeader;
        private Label _globalAiTitleLbl;
        private SupeyButton _globalAiCollapseBtn;
        private TextBox _globalAiTranscript;
        private Label _globalAiEmptyHint;
        private TextBox _globalAiPrompt;
        private Label _globalAiPromptPlaceholder;
        private Label _globalAiComposerHint;
        private SupeyButton _globalAiSendBtn;
        private Panel _globalAiDraftCard;
        private Label _globalAiDraftTitle;
        private Label _globalAiDraftBody;
        private SupeyButton _globalAiApplyDraftBtn;
        private SupeyButton _globalAiDiscardDraftBtn;
        private Panel _globalAiActionCard;
        private Label _globalAiActionTitle;
        private Label _globalAiActionBody;
        private SupeyButton _globalAiActionDoBtn;
        private SupeyButton _globalAiActionSkipBtn;
        private List<HiatmeAssistantAction> _globalAiPendingActions;
        private SupeyStatusPill _globalAiStatusPill;
        private HiatmeAiSettings _globalAiSettings;
        private CancellationTokenSource _globalAiCts;
        private string _globalAiLastTraceId;
        private JObject _globalAiPendingDraft;
        private bool _globalAiExpanded;

        private void InitializeGlobalAiDock()
        {
            if (_globalAiDock != null && !_globalAiDock.IsDisposed)
                return;

            _globalAiSettings = HiatmeAiSettings.Load();
            _globalAiExpanded = false;

            _globalAiDock = new Panel
            {
                Name = "globalAiDock",
                Visible = false,
                Dock = DockStyle.None,
                Width = GlobalAiDockExpandedWidth,
                BackColor = SupeyTheme.Surface,
                Padding = Padding.Empty,
            };

            _globalAiHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 40,
                BackColor = SupeyTheme.SurfaceHeader,
                Padding = new Padding(12, 0, 8, 0),
            };
            var headerAccent = new Panel
            {
                Dock = DockStyle.Left,
                Width = 3,
                BackColor = SupeyTheme.AccentStripe,
            };
            _globalAiTitleLbl = new Label
            {
                Dock = DockStyle.Fill,
                Text = "AI Copilot",
                ForeColor = SupeyTheme.TextPrimary,
                BackColor = SupeyTheme.SurfaceHeader,
                Font = SupeyTheme.HeaderFont,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 0, 0, 0),
            };
            _globalAiCollapseBtn = new SupeyButton
            {
                Text = "Hide",
                Kind = SupeyButton.Variant.Ghost,
                Dock = DockStyle.Right,
                Size = new Size(56, 28),
                Margin = new Padding(0),
            };
            _globalAiCollapseBtn.Click += (_, __) => SetGlobalAiDockExpanded(false);
            _globalAiHeader.Controls.Add(_globalAiTitleLbl);
            _globalAiHeader.Controls.Add(_globalAiCollapseBtn);
            _globalAiHeader.Controls.Add(headerAccent);

            var headerDivider = new Panel
            {
                Dock = DockStyle.Top,
                Height = 1,
                BackColor = SupeyTheme.Divider,
            };

            var body = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.Surface,
                Padding = new Padding(14, 12, 14, 14),
            };

            var statusRow = new Panel
            {
                Dock = DockStyle.Top,
                Height = 32,
                BackColor = SupeyTheme.Surface,
                Padding = new Padding(0, 2, 0, 8),
            };
            _globalAiStatusPill = new SupeyStatusPill
            {
                Dock = DockStyle.None,
                Location = new Point(0, 2),
                Label = "Ready",
                DotColor = SupeyTheme.SuccessText,
            };
            statusRow.Controls.Add(_globalAiStatusPill);

            var composer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 104,
                BackColor = SupeyTheme.Surface,
            };
            var composerCard = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.Divider,
                Padding = new Padding(1),
            };
            var composerInner = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.SurfaceElevated,
                Padding = new Padding(10, 8, 8, 8),
            };
            var sendCol = new Panel
            {
                Dock = DockStyle.Right,
                Width = 84,
                BackColor = SupeyTheme.SurfaceElevated,
                Padding = new Padding(8, 0, 0, 0),
            };
            _globalAiSendBtn = new SupeyButton
            {
                Text = "Send",
                Kind = SupeyButton.Variant.Primary,
                Dock = DockStyle.Bottom,
                Height = 36,
                CornerRadius = 6,
            };
            _globalAiSendBtn.Click += async (_, __) => await OnGlobalAiSendClickedAsync().ConfigureAwait(true);
            sendCol.Controls.Add(_globalAiSendBtn);

            _globalAiComposerHint = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 16,
                Text = "Enter to send  ·  Shift+Enter for a new line",
                ForeColor = SupeyTheme.TextMuted,
                BackColor = SupeyTheme.SurfaceElevated,
                Font = SupeyTheme.CaptionFont,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(0, 2, 0, 0),
            };

            var promptHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.SurfaceElevated,
                Padding = new Padding(2, 2, 4, 4),
            };
            _globalAiPrompt = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                AcceptsReturn = true,
                AutoSize = false,
                BorderStyle = BorderStyle.None,
                ScrollBars = ScrollBars.None,
                WordWrap = true,
                BackColor = SupeyTheme.SurfaceElevated,
                ForeColor = SupeyTheme.TextPrimary,
                Font = SupeyTheme.BodyFont,
            };
            _globalAiPromptPlaceholder = new Label
            {
                AutoSize = false,
                Text = "Ask about this screen…",
                ForeColor = SupeyTheme.TextMuted,
                BackColor = SupeyTheme.SurfaceElevated,
                Font = SupeyTheme.BodyFont,
                TextAlign = ContentAlignment.TopLeft,
                Location = new Point(2, 2),
                Size = new Size(200, 36),
            };
            _globalAiPromptPlaceholder.Click += (_, __) => _globalAiPrompt.Focus();
            _globalAiPrompt.GotFocus += (_, __) => _globalAiPromptPlaceholder.Visible = false;
            _globalAiPrompt.LostFocus += (_, __) =>
                _globalAiPromptPlaceholder.Visible = string.IsNullOrWhiteSpace(_globalAiPrompt.Text);
            _globalAiPrompt.TextChanged += (_, __) =>
                _globalAiPromptPlaceholder.Visible = !_globalAiPrompt.Focused
                    && string.IsNullOrWhiteSpace(_globalAiPrompt.Text);
            _globalAiPrompt.KeyDown += async (_, e) =>
            {
                if (e.KeyCode == Keys.Enter && !e.Shift)
                {
                    e.SuppressKeyPress = true;
                    await OnGlobalAiSendClickedAsync().ConfigureAwait(true);
                }
            };
            promptHost.Resize += (_, __) =>
            {
                if (_globalAiPromptPlaceholder == null || _globalAiPromptPlaceholder.IsDisposed)
                    return;
                _globalAiPromptPlaceholder.SetBounds(
                    2, 2,
                    Math.Max(40, promptHost.ClientSize.Width - 6),
                    Math.Max(20, promptHost.ClientSize.Height - 4));
            };
            promptHost.Controls.Add(_globalAiPrompt);
            promptHost.Controls.Add(_globalAiPromptPlaceholder);
            _globalAiPromptPlaceholder.BringToFront();

            composerInner.Controls.Add(promptHost);
            composerInner.Controls.Add(_globalAiComposerHint);
            composerInner.Controls.Add(sendCol);
            composerCard.Controls.Add(composerInner);
            composer.Controls.Add(composerCard);

            _globalAiDraftCard = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 168,
                Visible = false,
                BackColor = SupeyTheme.SurfaceElevated,
                Padding = new Padding(12, 10, 12, 10),
            };
            _globalAiDraftTitle = new Label
            {
                Dock = DockStyle.Top,
                Height = 22,
                Text = "Draft preview",
                ForeColor = SupeyTheme.TextPrimary,
                BackColor = SupeyTheme.SurfaceElevated,
                Font = SupeyTheme.HeaderFont,
                TextAlign = ContentAlignment.MiddleLeft,
            };
            var draftActions = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 32,
                BackColor = SupeyTheme.SurfaceElevated,
            };
            _globalAiApplyDraftBtn = new SupeyButton
            {
                Text = "Apply draft",
                Kind = SupeyButton.Variant.Primary,
                Dock = DockStyle.Right,
                Size = new Size(110, 30),
            };
            _globalAiApplyDraftBtn.Click += (_, __) => ApplyGlobalAiPendingDraft();
            _globalAiDiscardDraftBtn = new SupeyButton
            {
                Text = "Discard",
                Kind = SupeyButton.Variant.Ghost,
                Dock = DockStyle.Left,
                Size = new Size(80, 30),
            };
            _globalAiDiscardDraftBtn.Click += (_, __) =>
            {
                ReportGlobalAiOutcome("draft_discarded", "Draft discarded");
                DiscardGlobalAiPendingDraft();
                SetGlobalAiStatus("Draft discarded.", SupeyTheme.TextMuted);
            };
            draftActions.Controls.Add(_globalAiApplyDraftBtn);
            draftActions.Controls.Add(_globalAiDiscardDraftBtn);
            _globalAiDraftBody = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = SupeyTheme.TextSecondary,
                BackColor = SupeyTheme.SurfaceElevated,
                Font = SupeyTheme.CaptionFont,
                TextAlign = ContentAlignment.TopLeft,
            };
            _globalAiDraftCard.Controls.Add(_globalAiDraftBody);
            _globalAiDraftCard.Controls.Add(draftActions);
            _globalAiDraftCard.Controls.Add(_globalAiDraftTitle);

            _globalAiActionCard = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 86,
                Visible = false,
                BackColor = SupeyTheme.SurfaceElevated,
                Padding = new Padding(12, 10, 12, 10),
            };
            _globalAiActionTitle = new Label
            {
                Dock = DockStyle.Top,
                Height = 20,
                Text = "Suggested action",
                ForeColor = SupeyTheme.TextPrimary,
                BackColor = SupeyTheme.SurfaceElevated,
                Font = SupeyTheme.HeaderFont,
                TextAlign = ContentAlignment.MiddleLeft,
            };
            var actionBtns = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 32,
                BackColor = SupeyTheme.SurfaceElevated,
            };
            _globalAiActionDoBtn = new SupeyButton
            {
                Text = "Do it",
                Kind = SupeyButton.Variant.Primary,
                Dock = DockStyle.Right,
                Size = new Size(88, 30),
            };
            _globalAiActionDoBtn.Click += (_, __) => RunGlobalAiPendingActions();
            _globalAiActionSkipBtn = new SupeyButton
            {
                Text = "Skip",
                Kind = SupeyButton.Variant.Ghost,
                Dock = DockStyle.Left,
                Size = new Size(72, 30),
            };
            _globalAiActionSkipBtn.Click += (_, __) =>
            {
                ReportGlobalAiOutcome("action_skipped", "Action skipped");
                ClearGlobalAiPendingActions();
                SetGlobalAiStatus("Action skipped.", SupeyTheme.TextMuted);
            };
            actionBtns.Controls.Add(_globalAiActionDoBtn);
            actionBtns.Controls.Add(_globalAiActionSkipBtn);
            _globalAiActionBody = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = SupeyTheme.TextSecondary,
                BackColor = SupeyTheme.SurfaceElevated,
                Font = SupeyTheme.CaptionFont,
                TextAlign = ContentAlignment.TopLeft,
            };
            _globalAiActionCard.Controls.Add(_globalAiActionBody);
            _globalAiActionCard.Controls.Add(actionBtns);
            _globalAiActionCard.Controls.Add(_globalAiActionTitle);

            _globalAiTranscript = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                AutoSize = false,
                BorderStyle = BorderStyle.None,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = true,
                BackColor = SupeyTheme.SurfaceBase,
                ForeColor = SupeyTheme.TextPrimary,
                Font = SupeyTheme.BodyFont,
            };
            _globalAiEmptyHint = new Label
            {
                Dock = DockStyle.Fill,
                Text = "Ask about this screen — trips, drivers, times, whatever you're looking at.",
                ForeColor = SupeyTheme.TextMuted,
                BackColor = SupeyTheme.SurfaceBase,
                Font = SupeyTheme.BodyFont,
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = new Padding(24, 16, 24, 16),
            };
            _globalAiEmptyHint.Click += (_, __) => _globalAiPrompt?.Focus();
            var transcriptPad = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.SurfaceBase,
                Padding = new Padding(14, 12, 12, 12),
            };
            transcriptPad.Controls.Add(_globalAiTranscript);
            transcriptPad.Controls.Add(_globalAiEmptyHint);
            _globalAiEmptyHint.BringToFront();
            var transcriptCard = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.Divider,
                Padding = new Padding(1),
            };
            transcriptCard.Controls.Add(transcriptPad);

            Panel StackGap() => new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 8,
                BackColor = SupeyTheme.Surface,
            };

            body.Controls.Add(transcriptCard);
            body.Controls.Add(_globalAiDraftCard);
            body.Controls.Add(StackGap());
            body.Controls.Add(_globalAiActionCard);
            body.Controls.Add(StackGap());
            body.Controls.Add(composer);
            body.Controls.Add(statusRow);

            var leftEdge = new Panel
            {
                Dock = DockStyle.Left,
                Width = 1,
                BackColor = SupeyTheme.Divider,
            };

            _globalAiDock.Controls.Add(body);
            _globalAiDock.Controls.Add(headerDivider);
            _globalAiDock.Controls.Add(_globalAiHeader);
            _globalAiDock.Controls.Add(leftEdge);

            Controls.Add(_globalAiDock);
            _globalAiDock.BringToFront();

            ShowTitleBarAiButton = true;
            TitleBarAiOpen = false;
            TitleBarAiClick -= OnTitleBarAiClicked;
            TitleBarAiClick += OnTitleBarAiClicked;

            ApplyGlobalAiDockTheme();
            LayoutGlobalAiDock();
        }

        private void OnTitleBarAiClicked(object sender, EventArgs e)
        {
            SetGlobalAiDockExpanded(!_globalAiExpanded);
        }

        private void SetGlobalAiDockExpanded(bool expanded)
        {
            _globalAiExpanded = expanded;
            TitleBarAiOpen = expanded;
            if (_globalAiDock != null && !_globalAiDock.IsDisposed)
                _globalAiDock.Visible = expanded;
            LayoutGlobalAiDock();
            if (expanded)
            {
                _globalAiDock?.BringToFront();
                try { _globalAiPrompt?.Focus(); } catch { }
                _ = ProbeGlobalAiPanelAsync();
            }
            RefreshTitleBarChrome();
        }

        protected override void OnLayoutTitleBarControls()
        {
            LayoutGlobalAiDock();
        }

        private void LayoutGlobalAiDock()
        {
            int rightGutter = 0;
            if (_globalAiExpanded && _globalAiDock != null && !_globalAiDock.IsDisposed)
            {
                int w = Math.Max(GlobalAiDockMinWidth, Math.Min(GlobalAiDockExpandedWidth, ClientSize.Width / 3));
                int top = TitleBarHeight;
                int h = Math.Max(0, ClientSize.Height - top - GlobalAiDockRightPad);
                int x = Math.Max(0, ClientSize.Width - w - GlobalAiDockRightPad);
                _globalAiDock.SetBounds(x, top, w, h);
                _globalAiDock.Visible = true;
                _globalAiDock.BringToFront();
                rightGutter = w + GlobalAiDockRightPad;
            }
            else if (_globalAiDock != null && !_globalAiDock.IsDisposed)
            {
                _globalAiDock.Visible = false;
            }

            SetMaterialContentGutters(SupeyDrawer.CollapsedWidth, rightGutter, GlobalAiDockRightPad);
        }

        private void ApplyGlobalAiDockTheme()
        {
            if (_globalAiDock == null || _globalAiDock.IsDisposed)
                return;
            try
            {
                _globalAiDock.BackColor = SupeyTheme.Surface;
                if (_globalAiHeader != null)
                    _globalAiHeader.BackColor = SupeyTheme.SurfaceHeader;
                if (_globalAiTitleLbl != null)
                {
                    _globalAiTitleLbl.BackColor = SupeyTheme.SurfaceHeader;
                    _globalAiTitleLbl.ForeColor = SupeyTheme.TextPrimary;
                }
                if (_globalAiTranscript != null)
                {
                    _globalAiTranscript.BackColor = SupeyTheme.SurfaceBase;
                    _globalAiTranscript.ForeColor = SupeyTheme.TextPrimary;
                    _globalAiTranscript.Font = SupeyTheme.BodyFont;
                }
                if (_globalAiEmptyHint != null)
                {
                    _globalAiEmptyHint.BackColor = SupeyTheme.SurfaceBase;
                    _globalAiEmptyHint.ForeColor = SupeyTheme.TextMuted;
                }
                if (_globalAiPrompt != null)
                {
                    _globalAiPrompt.BackColor = SupeyTheme.SurfaceElevated;
                    _globalAiPrompt.ForeColor = SupeyTheme.TextPrimary;
                }
                if (_globalAiPromptPlaceholder != null)
                {
                    _globalAiPromptPlaceholder.BackColor = SupeyTheme.SurfaceElevated;
                    _globalAiPromptPlaceholder.ForeColor = SupeyTheme.TextMuted;
                }
                if (_globalAiComposerHint != null)
                {
                    _globalAiComposerHint.BackColor = SupeyTheme.SurfaceElevated;
                    _globalAiComposerHint.ForeColor = SupeyTheme.TextMuted;
                }
                if (_globalAiDraftCard != null)
                    _globalAiDraftCard.BackColor = SupeyTheme.SurfaceElevated;
                if (_globalAiDraftTitle != null)
                {
                    _globalAiDraftTitle.BackColor = SupeyTheme.SurfaceElevated;
                    _globalAiDraftTitle.ForeColor = SupeyTheme.TextPrimary;
                }
                if (_globalAiDraftBody != null)
                {
                    _globalAiDraftBody.BackColor = SupeyTheme.SurfaceElevated;
                    _globalAiDraftBody.ForeColor = SupeyTheme.TextSecondary;
                }
                if (_globalAiActionCard != null)
                    _globalAiActionCard.BackColor = SupeyTheme.SurfaceElevated;
                if (_globalAiActionTitle != null)
                {
                    _globalAiActionTitle.BackColor = SupeyTheme.SurfaceElevated;
                    _globalAiActionTitle.ForeColor = SupeyTheme.TextPrimary;
                }
                if (_globalAiActionBody != null)
                {
                    _globalAiActionBody.BackColor = SupeyTheme.SurfaceElevated;
                    _globalAiActionBody.ForeColor = SupeyTheme.TextSecondary;
                }
                SupeyThemeApplier.Recolor(_globalAiDock);
                SupeyDarkScrollBars.Apply(_globalAiDock);
                _globalAiDock.Invalidate(true);
            }
            catch { }
        }

        private async Task OnGlobalAiSendClickedAsync()
        {
            if (_globalAiCts != null)
                return;
            string msg = (_globalAiPrompt?.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(msg))
                return;

            if (!_globalAiExpanded)
                SetGlobalAiDockExpanded(true);

            _globalAiCts = new CancellationTokenSource();
            _globalAiSendBtn.Enabled = false;
            AppendGlobalAiTranscript("You", msg);
            SetGlobalAiStatus("Thinking...", SupeyTheme.TextMuted);

            try
            {
                _globalAiSettings = _globalAiSettings ?? HiatmeAiSettings.Load();
                var ctx = BuildGlobalAiContext();
                var resp = await HiatmeAiClient.SendAssistantAsync(
                    _globalAiSettings,
                    ctx,
                    msg,
                    _globalAiCts.Token).ConfigureAwait(true);
                if (resp == null)
                    throw new InvalidOperationException("Empty assistant response.");

                _globalAiLastTraceId = resp.TraceId ?? "";
                if (!string.IsNullOrWhiteSpace(resp.Thinking))
                    AppendGlobalAiTranscript("AI · thinking", resp.Thinking);
                AppendGlobalAiTranscript("AI", resp.Message ?? "");

                _globalAiPendingDraft = resp.Draft;
                ShowGlobalAiDraftPreview(resp);
                ShowGlobalAiPendingActions(resp.Actions);
                if (_globalAiPendingDraft != null)
                    SetGlobalAiStatus("Draft ready. Review the preview, then Apply.", SupeyTheme.AccentStripe);
                else if (_globalAiPendingActions != null && _globalAiPendingActions.Count > 0)
                    SetGlobalAiStatus("Suggested action ready. Confirm to run it.", SupeyTheme.AccentStripe);
                else
                    SetGlobalAiStatus("Reply ready.", SupeyTheme.SuccessText);
                _globalAiPrompt.Clear();
            }
            catch (OperationCanceledException)
            {
                AppendGlobalAiTranscript("System", "Canceled.");
                SetGlobalAiStatus("Canceled.", SupeyTheme.WarnText);
            }
            catch (Exception ex)
            {
                string friendly = DescribeGlobalAiFailure(ex);
                AppendGlobalAiTranscript("Error", friendly);
                SetGlobalAiStatus(ShortGlobalAiFailureStatus(friendly), SupeyTheme.ErrorText);
            }
            finally
            {
                _globalAiCts?.Dispose();
                _globalAiCts = null;
                if (_globalAiSendBtn != null && !_globalAiSendBtn.IsDisposed)
                    _globalAiSendBtn.Enabled = true;
            }
        }

        private JObject BuildGlobalAiContext()
        {
            var ctx = new JObject
            {
                ["active_tab_name"] = (hiatmeTabControl?.SelectedTab?.Name ?? "").Trim(),
                ["active_tab_title"] = (hiatmeTabControl?.SelectedTab?.Text ?? "").Trim(),
                ["trace_id"] = _globalAiLastTraceId ?? "",
                ["timestamp_local"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            };
            if (tabPageDriverDiscipline != null
                && hiatmeTabControl?.SelectedTab == tabPageDriverDiscipline
                && _ddBuilt)
            {
                try
                {
                    var rec = CollectDriverDisciplineRecord();
                    ctx["active_tool"] = "driver_discipline";
                    ctx["driver_discipline"] = JObject.FromObject(rec);
                    ctx["allowed_violations"] = new JArray(DriverDisciplineOptions.Violations);
                    ctx["allowed_actions"] = new JArray(DriverDisciplineOptions.ActionLevels);
                }
                catch
                {
                    ctx["active_tool"] = "driver_discipline";
                }
            }
            else
            {
                ctx["active_tool"] = ResolveGlobalAiToolId();
            }
            try
            {
                string sd = GlobalAiServiceDateIso();
                if (!string.IsNullOrWhiteSpace(sd))
                    ctx["service_date"] = sd;
            }
            catch { }
            try
            {
                if (!string.IsNullOrWhiteSpace(_ldSelectedDriver)
                    && !LateDriversIsReservedSelected(_ldSelectedDriver))
                    ctx["selected_driver"] = _ldSelectedDriver.Trim();
            }
            catch { }
            var src = (_ldStripDrivers != null && _ldStripDrivers.Count > 0)
                ? _ldStripDrivers
                : _ldDriverRows;
            if (src != null && src.Count > 0)
            {
                var names = new JArray();
                foreach (var row in src)
                {
                    if (row == null || string.IsNullOrWhiteSpace(row.Driver))
                        continue;
                    names.Add(row.Driver.Trim());
                    if (names.Count >= 24)
                        break;
                }
                if (names.Count > 0)
                    ctx["known_drivers"] = names;
            }
            return ctx;
        }

        private void ApplyGlobalAiPendingDraft()
        {
            if (_globalAiPendingDraft == null)
                return;
            try
            {
                if (tabPageDriverDiscipline != null)
                    hiatmeTabControl.SelectedTab = tabPageDriverDiscipline;
                if (!_ddBuilt)
                    InitializeDriverDisciplineTab();
                ApplyDriverDisciplineDraft(_globalAiPendingDraft);
                ReportGlobalAiOutcome("draft_applied", "Draft applied to Driver Discipline");
                DiscardGlobalAiPendingDraft();
                SetGlobalAiStatus("Draft applied to Driver Discipline form.", SupeyTheme.SuccessText);
                SetDriverDisciplineStatus("AI draft applied. Review fields before Save.");
            }
            catch (Exception ex)
            {
                AppendGlobalAiTranscript("Error", "Could not apply draft: " + ex.Message);
                SetGlobalAiStatus("Apply failed.", SupeyTheme.ErrorText);
            }
        }

        private void DiscardGlobalAiPendingDraft()
        {
            _globalAiPendingDraft = null;
            if (_globalAiDraftCard != null && !_globalAiDraftCard.IsDisposed)
                _globalAiDraftCard.Visible = false;
            if (_globalAiDraftBody != null)
                _globalAiDraftBody.Text = "";
        }

        private void ClearGlobalAiPendingActions()
        {
            _globalAiPendingActions = null;
            if (_globalAiActionCard != null && !_globalAiActionCard.IsDisposed)
                _globalAiActionCard.Visible = false;
            if (_globalAiActionBody != null)
                _globalAiActionBody.Text = "";
        }

        private void ShowGlobalAiPendingActions(List<HiatmeAssistantAction> actions)
        {
            if (_globalAiActionCard == null || _globalAiActionCard.IsDisposed)
                return;
            var safe = new List<HiatmeAssistantAction>();
            if (actions != null)
            {
                foreach (var a in actions)
                {
                    if (a == null) continue;
                    string id = (a.Id ?? "").Trim().ToLowerInvariant();
                    if (id != "open_tab" && id != "focus_driver")
                        continue;
                    if (string.IsNullOrWhiteSpace(a.Label))
                        a.Label = id == "focus_driver" ? "Show driver" : "Open tab";
                    safe.Add(a);
                    if (safe.Count >= 2)
                        break;
                }
            }
            if (safe.Count == 0)
            {
                ClearGlobalAiPendingActions();
                return;
            }
            _globalAiPendingActions = safe;
            _globalAiActionTitle.Text = safe.Count == 1 ? "Suggested action" : "Suggested actions";
            var lines = new List<string>();
            foreach (var a in safe)
                lines.Add("• " + (a.Label ?? a.Id));
            _globalAiActionBody.Text = string.Join("\r\n", lines);
            _globalAiActionCard.Visible = true;
        }

        private void RunGlobalAiPendingActions()
        {
            var pending = _globalAiPendingActions;
            ClearGlobalAiPendingActions();
            if (pending == null || pending.Count == 0)
                return;
            try
            {
                foreach (var action in pending)
                    ExecuteGlobalAiAction(action);
                SetGlobalAiStatus("Action done.", SupeyTheme.SuccessText);
                ReportGlobalAiOutcome("action_done", pending[0].Label ?? pending[0].Id);
            }
            catch (Exception ex)
            {
                AppendGlobalAiTranscript("Error", "Could not run action: " + ex.Message);
                SetGlobalAiStatus("Action failed.", SupeyTheme.ErrorText);
            }
        }

        private void ExecuteGlobalAiAction(HiatmeAssistantAction action)
        {
            if (action == null)
                return;
            string id = (action.Id ?? "").Trim().ToLowerInvariant();
            string tab = (action.Args?.Tab ?? "").Trim().ToLowerInvariant();
            string driver = (action.Args?.Driver ?? "").Trim();
            string trip = (action.Args?.Trip ?? "").Trim();

            if (id == "open_tab")
            {
                OpenGlobalAiTab(tab);
                return;
            }
            if (id == "focus_driver")
            {
                OpenGlobalAiTab(string.IsNullOrEmpty(tab) ? "driver_habits" : tab);
                if (!_ldBuilt)
                    InitializeLateDriversTab();
                if (!string.IsNullOrWhiteSpace(driver))
                    SelectLateDriversDriver(driver, trip);
            }
        }

        private string ResolveGlobalAiToolId()
        {
            var page = hiatmeTabControl?.SelectedTab;
            if (page == null) return "general_toolsuite";
            if (page == tabPageLateDrivers) return "driver_habits";
            if (page == tabPageDriverDiscipline) return "driver_discipline";
            if (page == tabPage6) return "schedule_builder";
            if (page == tabPage9) return "trip_scout";
            if (page == tabPageDashcamVideos) return "dashcam";
            if (page == tabPage2) return "billing";
            if (page == tabPage4) return "time_correction";
            if (page == tabPage5) return "templates";
            if (page == tabPageSupey) return "supey";
            if (page == tabPage7) return "auto_assign";
            if (page == tabPageMarketPerformance) return "market";
            return "general_toolsuite";
        }

        private string GlobalAiServiceDateIso()
        {
            var page = hiatmeTabControl?.SelectedTab;
            if (page == tabPageLateDrivers)
                return LateDriversSelectedServiceDateIso();
            if (page == tabPage6 && fsbdatepicker != null)
                return fsbdatepicker.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (page == tabPage9 && tsdatepicker != null)
                return tsdatepicker.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        private void OpenGlobalAiTab(string tabId)
        {
            TabPage page = null;
            switch ((tabId ?? "").Trim().ToLowerInvariant())
            {
                case "driver_habits": page = tabPageLateDrivers; break;
                case "driver_discipline": page = tabPageDriverDiscipline; break;
                case "schedule_builder": page = tabPage6; break;
                case "trip_scout": page = tabPage9; break;
                case "dashcam": page = tabPageDashcamVideos; break;
                case "billing": page = tabPage2; break;
                case "time_correction": page = tabPage4; break;
                case "templates": page = tabPage5; break;
                case "supey": page = tabPageSupey; break;
                case "auto_assign": page = tabPage7; break;
                case "market": page = tabPageMarketPerformance; break;
            }
            if (page != null && hiatmeTabControl != null)
                hiatmeTabControl.SelectedTab = page;
        }

        private void ShowGlobalAiDraftPreview(HiatmeAssistantResponse resp)
        {
            if (_globalAiDraftCard == null || _globalAiDraftCard.IsDisposed)
                return;
            if (resp == null || resp.Draft == null || resp.Draft.Count == 0)
            {
                DiscardGlobalAiPendingDraft();
                return;
            }

            var preview = resp.Preview;
            var lines = new List<string>();
            string conf = (preview?.Confidence ?? resp.Confidence ?? "medium").Trim();
            if (string.IsNullOrEmpty(conf)) conf = "medium";
            lines.Add("Confidence: " + conf);

            var missing = preview?.MissingLabels;
            if (missing == null || missing.Count == 0)
                missing = FriendlyMissingLabels(resp.MissingFields);
            if (missing != null && missing.Count > 0)
                lines.Add("Still needed: " + string.Join(", ", missing));
            else
                lines.Add("Required fields are filled. Review, then Apply.");

            string driver = preview?.DriverName ?? DraftString(resp.Draft, "driver_name", "driverName");
            string action = preview?.ActionLevel ?? DraftString(resp.Draft, "action_level", "actionLevel");
            string when = preview?.IncidentDate ?? DraftString(resp.Draft, "incident_date", "incidentDate");
            if (!string.IsNullOrWhiteSpace(driver))
                lines.Add("Driver: " + driver);
            if (!string.IsNullOrWhiteSpace(action))
                lines.Add("Action: " + action);
            if (!string.IsNullOrWhiteSpace(when))
                lines.Add("Incident: " + when);

            var viols = preview?.Violations;
            if (viols == null || viols.Count == 0)
                viols = DraftStringList(resp.Draft, "violations");
            if (viols != null && viols.Count > 0)
                lines.Add("Violations: " + string.Join("; ", viols));

            string narrative = preview?.Narrative ?? DraftString(resp.Draft, "narrative");
            if (!string.IsNullOrWhiteSpace(narrative))
                lines.Add(narrative.Trim());

            _globalAiDraftTitle.Text = (preview != null && preview.Ready)
                ? "Draft preview — ready to apply"
                : "Draft preview — review missing fields";
            _globalAiDraftBody.Text = string.Join("\r\n", lines);
            _globalAiDraftCard.Visible = true;
            _globalAiPendingDraft = resp.Draft;
        }

        private static List<string> FriendlyMissingLabels(IList<string> keys)
        {
            var outList = new List<string>();
            if (keys == null) return outList;
            foreach (string k in keys)
            {
                string key = (k ?? "").Trim().ToLowerInvariant();
                if (key == "driver_name") outList.Add("Driver");
                else if (key == "incident_date") outList.Add("Incident date");
                else if (key == "violations") outList.Add("Violation");
                else if (key == "narrative") outList.Add("Narrative");
                else if (key == "action_level") outList.Add("Action level");
                else if (!string.IsNullOrEmpty(k))
                    outList.Add(k.Replace('_', ' '));
            }
            return outList;
        }

        private static string DraftString(JObject draft, params string[] keys)
        {
            if (draft == null) return "";
            foreach (string k in keys)
            {
                var t = draft.SelectToken(k);
                string s = t?.ToString();
                if (!string.IsNullOrWhiteSpace(s))
                    return s.Trim();
            }
            return "";
        }

        private static List<string> DraftStringList(JObject draft, string key)
        {
            var list = new List<string>();
            if (draft == null) return list;
            if (draft[key] is JArray arr)
            {
                foreach (var t in arr)
                {
                    string s = (t?.ToString() ?? "").Trim();
                    if (!string.IsNullOrEmpty(s))
                        list.Add(s);
                }
            }
            return list;
        }

        private void ApplyDriverDisciplineDraft(JObject draft)
        {
            if (draft == null)
                return;

            string Get(params string[] keys)
            {
                foreach (string k in keys)
                {
                    var t = draft.SelectToken(k);
                    string s = t?.ToString();
                    if (!string.IsNullOrWhiteSpace(s))
                        return s.Trim();
                }
                return "";
            }

            void SetTb(SupeyTextBox tb, string value)
            {
                if (tb == null || string.IsNullOrWhiteSpace(value))
                    return;
                tb.Text = value.Trim();
            }

            SetTb(ddDriverTb, Get("driver_name", "driverName"));
            SetTb(ddEmployeeIdTb, Get("employee_id", "employeeId"));
            SetTb(ddVehicleTb, Get("vehicle", "vehicle_id"));
            SetTb(ddSupervisorTb, Get("supervisor_name", "supervisorName"));
            SetTb(ddTripRefTb, Get("trip_or_client_ref", "tripOrClientRef"));
            SetTb(ddLocationTb, Get("location"));
            SetTb(ddIncidentTimeTb, Get("incident_time", "incidentTime"));
            SetTb(ddPolicyTb, Get("policy_cited", "policyCited"));
            SetTb(ddFollowUpTb, Get("follow_up_date", "followUpDate"));
            SetTb(ddFolderTb, Get("footage_folder", "footageFolder"));

            if (ddFootageSummaryTb != null)
            {
                string v = Get("footage_summary", "footageSummary");
                if (!string.IsNullOrWhiteSpace(v)) ddFootageSummaryTb.Text = v;
            }
            if (ddNarrativeTb != null)
            {
                string v = Get("narrative");
                if (!string.IsNullOrWhiteSpace(v)) ddNarrativeTb.Text = v;
            }
            if (ddPriorTb != null)
            {
                string v = Get("prior_history", "priorHistory");
                if (!string.IsNullOrWhiteSpace(v)) ddPriorTb.Text = v;
            }
            if (ddCorrectiveTb != null)
            {
                string v = Get("corrective_action", "correctiveAction");
                if (!string.IsNullOrWhiteSpace(v)) ddCorrectiveTb.Text = v;
            }
            if (ddDriverStatementTb != null)
            {
                string v = Get("driver_statement", "driverStatement");
                if (!string.IsNullOrWhiteSpace(v)) ddDriverStatementTb.Text = v;
            }

            string action = Get("action_level", "actionLevel");
            if (ddActionCombo != null && !string.IsNullOrWhiteSpace(action))
            {
                foreach (var item in ddActionCombo.Items)
                {
                    if (string.Equals(item?.ToString(), action, StringComparison.OrdinalIgnoreCase))
                    {
                        ddActionCombo.SelectedItem = item;
                        break;
                    }
                }
            }

            try
            {
                string incidentDate = Get("incident_date", "incidentDate");
                if (!string.IsNullOrWhiteSpace(incidentDate)
                    && DateTime.TryParse(incidentDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var id))
                    ddIncidentDate.Value = id.Date;
            }
            catch { }

            try
            {
                string noticeDate = Get("notice_date", "noticeDate");
                if (!string.IsNullOrWhiteSpace(noticeDate)
                    && DateTime.TryParse(noticeDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var nd))
                    ddNoticeDate.Value = nd.Date;
            }
            catch { }

            if (draft["violations"] is JArray viols && _ddViolationChecks != null && _ddViolationChecks.Count > 0)
            {
                foreach (var chk in _ddViolationChecks)
                    chk.Checked = false;
                foreach (var token in viols)
                {
                    string v = (token?.ToString() ?? "").Trim();
                    if (string.IsNullOrEmpty(v))
                        continue;
                    foreach (var chk in _ddViolationChecks)
                    {
                        if (string.Equals(chk.Text ?? "", v, StringComparison.OrdinalIgnoreCase))
                        {
                            chk.Checked = true;
                            break;
                        }
                    }
                }
            }
        }

        private void AppendGlobalAiTranscript(string role, string text)
        {
            if (_globalAiTranscript == null || string.IsNullOrWhiteSpace(text))
                return;
            if (_globalAiEmptyHint != null && !_globalAiEmptyHint.IsDisposed)
                _globalAiEmptyHint.Visible = false;
            _globalAiTranscript.AppendText(
                "[" + DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture) + "] "
                + role + ":\r\n" + text.Trim() + "\r\n\r\n");
            _globalAiTranscript.SelectionStart = _globalAiTranscript.TextLength;
            _globalAiTranscript.ScrollToCaret();
        }

        private void SetGlobalAiStatus(string label, Color dot)
        {
            if (_globalAiStatusPill == null || _globalAiStatusPill.IsDisposed)
                return;
            _globalAiStatusPill.Label = string.IsNullOrWhiteSpace(label) ? "Ready" : label.Trim();
            _globalAiStatusPill.DotColor = dot;
            _globalAiStatusPill.ForeColor = SupeyTheme.TextPrimary;
        }

        private static void ReportGlobalAiOutcome(string outcome, string message)
        {
            try
            {
                HiatmeEventReporter.Report(
                    "ai_outcome",
                    "ai_copilot",
                    string.IsNullOrWhiteSpace(message) ? outcome : message.Trim(),
                    extra: new JObject { ["outcome"] = outcome ?? "" });
            }
            catch { }
        }

        private async Task ProbeGlobalAiPanelAsync()
        {
            SetGlobalAiStatus("Checking panel...", SupeyTheme.TextMuted);
            try
            {
                bool ok = await HiatmeAiSettings.RefreshPanelConnectionAsync().ConfigureAwait(true);
                _globalAiSettings = HiatmeAiSettings.Load();
                HiatmeAiSettings.LogProbe("dock probe ok=" + ok
                    + " base=" + (_globalAiSettings?.BaseUrl ?? "")
                    + " detail=" + HiatmeAiSettings.LastConnectionDetail);
                if (ok)
                {
                    SetGlobalAiStatus("Ready.", SupeyTheme.SuccessText);
                    return;
                }
                string detail = (HiatmeAiSettings.LastConnectionDetail ?? "").Trim();
                if (detail.IndexOf("token", StringComparison.OrdinalIgnoreCase) >= 0)
                    SetGlobalAiStatus("Panel token mismatch — chat unavailable.", SupeyTheme.ErrorText);
                else
                    SetGlobalAiStatus("Panel offline — other tools still work.", SupeyTheme.WarnText);
            }
            catch (Exception ex)
            {
                HiatmeAiSettings.LogProbe("dock probe threw " + ex.GetType().Name + ": " + ex.Message);
                SetGlobalAiStatus("Panel offline — other tools still work.", SupeyTheme.WarnText);
            }
        }

        private string DescribeGlobalAiFailure(Exception ex)
        {
            string where = (_globalAiSettings?.BaseUrl ?? "").Trim();
            if (ex is OperationCanceledException || ex is TaskCanceledException)
                return HiatmeAiClient.DescribeRequestError(ex, where);
            string msg = ex?.Message ?? "";
            if (msg.IndexOf("token", StringComparison.OrdinalIgnoreCase) >= 0
                || msg.IndexOf("401", StringComparison.OrdinalIgnoreCase) >= 0
                || msg.IndexOf("403", StringComparison.OrdinalIgnoreCase) >= 0)
                return "AI panel rejected the API token. Other Tool Suite tabs still work.";
            return HiatmeAiClient.DescribeRequestError(ex, where);
        }

        private static string ShortGlobalAiFailureStatus(string friendly)
        {
            if (string.IsNullOrWhiteSpace(friendly))
                return "Assistant unavailable.";
            if (friendly.IndexOf("token", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Token mismatch.";
            if (friendly.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Panel timed out.";
            if (friendly.IndexOf("unreachable", StringComparison.OrdinalIgnoreCase) >= 0
                || friendly.IndexOf("offline", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Panel offline.";
            return "Assistant unavailable.";
        }
    }
}
