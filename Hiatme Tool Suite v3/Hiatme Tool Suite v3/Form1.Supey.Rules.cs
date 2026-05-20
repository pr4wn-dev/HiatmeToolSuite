using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    public partial class Form1
    {
        private SupeyCollapsiblePanel _supeyRulesCollapsible;
        private FlowLayoutPanel _supeyRulesStandingFlow;
        private FlowLayoutPanel _supeyRulesDispatchFlow;
        private Panel _supeyRulesStandingHost;
        private Panel _supeyRulesDispatchHost;
        private Label _supeyRulesTabStanding;
        private Label _supeyRulesTabDispatch;
        private Label _supeyRulesSummaryLbl;
        private int _supeyRulesStandingCount;
        private int _supeyRulesDispatchTotal;
        private int _supeyRulesDispatchOn;

        private void BuildSupeyRulesPanel()
        {
            _supeyRulesCollapsible = new SupeyCollapsiblePanel
            {
                Title = "Rules",
                Dock = DockStyle.Right,
                ExpandedWidth = 320,
                MinExpandedWidth = 260,
                MaxExpandedWidth = 480,
            };

            var host = _supeyRulesCollapsible.ContentPanel;
            host.BackColor = SupeyTheme.Surface;
            host.Padding = new Padding(8, 6, 8, 8);

            _supeyRulesSummaryLbl = new Label
            {
                Dock = DockStyle.Top,
                Height = 36,
                ForeColor = SupeyTheme.TextSecondary,
                BackColor = SupeyTheme.Surface,
                Font = SupeyTheme.CaptionFont,
                Text = "Keep = enforce on BUILD. Off = saved, not enforced. Remove = delete. Standing rules always apply to the AI.",
                Padding = new Padding(0, 0, 0, 4),
            };

            var tabBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 32,
                BackColor = SupeyTheme.SurfaceHeader,
                Padding = new Padding(0, 4, 0, 0),
            };
            var tabInner = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.SurfaceBase,
                Padding = new Padding(4, 2, 4, 2),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = false,
            };
            _supeyRulesTabStanding = MakeRulesTabButton("Standing", true);
            _supeyRulesTabDispatch = MakeRulesTabButton("Dispatch", false);
            _supeyRulesTabStanding.Click += (s, e) => ShowSupeyRulesTab(standing: true);
            _supeyRulesTabDispatch.Click += (s, e) => ShowSupeyRulesTab(standing: false);
            tabInner.Controls.Add(_supeyRulesTabStanding);
            tabInner.Controls.Add(_supeyRulesTabDispatch);
            tabBar.Controls.Add(tabInner);

            var body = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.SurfaceBase,
            };
            _supeyRulesStandingHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.SurfaceBase,
                Padding = new Padding(4),
            };
            _supeyRulesDispatchHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.SurfaceBase,
                Padding = new Padding(4),
                Visible = false,
            };
            _supeyRulesStandingFlow = MakeRulesFlowPanel();
            _supeyRulesDispatchFlow = MakeRulesFlowPanel();
            _supeyRulesStandingHost.Controls.Add(_supeyRulesStandingFlow);
            _supeyRulesDispatchHost.Controls.Add(_supeyRulesDispatchFlow);
            body.Controls.Add(_supeyRulesDispatchHost);
            body.Controls.Add(_supeyRulesStandingHost);

            host.Controls.Add(body);
            host.Controls.Add(tabBar);
            host.Controls.Add(_supeyRulesSummaryLbl);

            _ = RefreshSupeyRulesPanelAsync();
        }

        private static FlowLayoutPanel MakeRulesFlowPanel()
        {
            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                BackColor = SupeyTheme.SurfaceBase,
            };
            flow.Resize += (s, e) => SupeyRulesFlow_Resize(flow);
            return flow;
        }

        private static void SupeyRulesFlow_Resize(FlowLayoutPanel flow)
        {
            int w = Math.Max(180, flow.ClientSize.Width - 8);
            foreach (Control c in flow.Controls)
            {
                if (c is Panel p)
                    p.Width = w;
            }
        }

        private static Label MakeRulesTabButton(string text, bool selected)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                Width = 96,
                Height = 24,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = SupeyTheme.CaptionFont,
                ForeColor = selected ? SupeyTheme.TextPrimary : SupeyTheme.TextSecondary,
                BackColor = selected ? SupeyTheme.SurfaceElevated : SupeyTheme.SurfaceBase,
                BorderStyle = BorderStyle.FixedSingle,
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 6, 0),
            };
        }

        private void ShowSupeyRulesTab(bool standing)
        {
            if (_supeyRulesStandingHost != null)
                _supeyRulesStandingHost.Visible = standing;
            if (_supeyRulesDispatchHost != null)
                _supeyRulesDispatchHost.Visible = !standing;
            if (_supeyRulesTabStanding != null)
            {
                _supeyRulesTabStanding.ForeColor = standing ? SupeyTheme.TextPrimary : SupeyTheme.TextSecondary;
                _supeyRulesTabStanding.BackColor = standing ? SupeyTheme.SurfaceElevated : SupeyTheme.SurfaceBase;
            }
            if (_supeyRulesTabDispatch != null)
            {
                _supeyRulesTabDispatch.ForeColor = standing ? SupeyTheme.TextSecondary : SupeyTheme.TextPrimary;
                _supeyRulesTabDispatch.BackColor = standing ? SupeyTheme.SurfaceBase : SupeyTheme.SurfaceElevated;
            }
        }

        internal async Task RefreshSupeyRulesPanelAsync()
        {
            if (_supeyRulesStandingFlow == null || _supeyRulesDispatchFlow == null) return;
            if (_supeyAiSettings == null)
                _supeyAiSettings = HiatmeAiSettings.Load();

            await RefreshSupeyRulesStandingAsync().ConfigureAwait(true);
            await RefreshSupeyRulesDispatchAsync().ConfigureAwait(true);
            UpdateSupeyRulesSummary();
        }

        private async Task RefreshSupeyRulesStandingAsync()
        {
            _supeyRulesStandingFlow.SuspendLayout();
            _supeyRulesStandingFlow.Controls.Clear();
            try
            {
                var lines = await HiatmeAiClient.GetStandingRulesAsync(_supeyAiSettings).ConfigureAwait(true);
                if (lines == null || lines.Count == 0)
                {
                    _supeyRulesStandingCount = 0;
                    _supeyRulesStandingFlow.Controls.Add(MakeRulesEmptyLabel(
                        "No standing rules on server yet.\n\nThey sync from config/hiatme/dispatch_rules/standing_rules.json when the panel starts."));
                    return;
                }
                _supeyRulesStandingCount = lines.Count;
                foreach (var text in lines)
                    _supeyRulesStandingFlow.Controls.Add(MakeStandingRuleCard(text));
            }
            catch (Exception ex)
            {
                _supeyRulesStandingFlow.Controls.Add(MakeRulesEmptyLabel("Could not load standing rules:\n" + ex.Message));
            }
            finally
            {
                _supeyRulesStandingFlow.ResumeLayout(true);
                SupeyRulesFlow_Resize(_supeyRulesStandingFlow);
            }
        }

        private async Task RefreshSupeyRulesDispatchAsync()
        {
            _supeyRulesDispatchFlow.SuspendLayout();
            _supeyRulesDispatchFlow.Controls.Clear();
            try
            {
                var rules = await HiatmeAiClient.GetRulesAsync(_supeyAiSettings).ConfigureAwait(true);
                _supeyRulesDispatchTotal = 0;
                _supeyRulesDispatchOn = 0;
                if (rules == null || rules.Count == 0)
                {
                    _supeyRulesDispatchFlow.Controls.Add(MakeRulesEmptyLabel(
                        "No dispatch rules yet.\n\nTeach the AI in chat or add rules in the web panel."));
                    return;
                }
                foreach (var rule in rules)
                {
                    if (string.IsNullOrWhiteSpace(rule?.Id)) continue;
                    _supeyRulesDispatchTotal++;
                    if (rule.Enabled) _supeyRulesDispatchOn++;
                    _supeyRulesDispatchFlow.Controls.Add(MakeDispatchRuleCard(rule));
                }
            }
            catch (Exception ex)
            {
                _supeyRulesDispatchFlow.Controls.Add(MakeRulesEmptyLabel("Could not load dispatch rules:\n" + ex.Message));
            }
            finally
            {
                _supeyRulesDispatchFlow.ResumeLayout(true);
                SupeyRulesFlow_Resize(_supeyRulesDispatchFlow);
            }
        }

        private void UpdateSupeyRulesSummary()
        {
            if (_supeyRulesSummaryLbl == null) return;
            _supeyRulesSummaryLbl.Text =
                $"{_supeyRulesStandingCount} standing · {_supeyRulesDispatchOn} on / {_supeyRulesDispatchTotal} dispatch";
        }

        private static Label MakeRulesEmptyLabel(string text)
        {
            return new Label
            {
                Width = 260,
                Height = 72,
                Margin = new Padding(0, 8, 0, 0),
                ForeColor = SupeyTheme.TextMuted,
                BackColor = SupeyTheme.SurfaceBase,
                Font = SupeyTheme.BodyFont,
                Text = text,
            };
        }

        private Panel MakeStandingRuleCard(string text)
        {
            int w = 280;
            var card = new Panel
            {
                Width = w,
                Margin = new Padding(0, 0, 0, 8),
                BackColor = SupeyTheme.SurfaceElevated,
                Padding = new Padding(10, 8, 10, 10),
            };
            var badge = new Label
            {
                Dock = DockStyle.Top,
                Height = 18,
                Text = "Company standing",
                ForeColor = SupeyTheme.AccentStripe,
                Font = SupeyTheme.CaptionFont,
                BackColor = Color.Transparent,
            };
            var body = new TextBox
            {
                Dock = DockStyle.Top,
                Multiline = true,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = SupeyTheme.SurfaceElevated,
                ForeColor = SupeyTheme.TextPrimary,
                Font = SupeyTheme.BodyFont,
                Text = (text ?? "").Trim(),
                ScrollBars = ScrollBars.None,
            };
            int lines = Math.Max(3, Math.Min(12, body.Text.Split('\n').Length
                + (body.Text.Length / 42)));
            body.Height = Math.Max(56, lines * 18 + 8);
            card.Height = badge.Height + body.Height + card.Padding.Vertical + 6;
            card.Controls.Add(body);
            card.Controls.Add(badge);
            return card;
        }

        private Panel MakeDispatchRuleCard(HiatmeAiRuleItem rule)
        {
            int w = 280;
            string title = (rule.Title ?? rule.Kind ?? "Rule").Trim();
            string detail = (rule.Rationale ?? "").Trim();
            if (!string.IsNullOrEmpty(detail) && detail != title)
                detail = title + "\r\n\r\n" + detail;
            else
                detail = title;

            var card = new Panel
            {
                Width = w,
                Margin = new Padding(0, 0, 0, 8),
                BackColor = rule.Enabled ? SupeyTheme.SurfaceElevated : SupeyTheme.Surface,
                Padding = new Padding(10, 8, 10, 10),
                Tag = rule,
            };
            var top = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 28,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
            };
            var ruleId = rule.Id;
            if (rule.Enabled)
            {
                var onBox = new CheckBox
                {
                    Text = "On — BUILD",
                    Checked = true,
                    AutoSize = true,
                    ForeColor = SupeyTheme.SuccessText,
                    Font = SupeyTheme.CaptionFont,
                    Margin = new Padding(0, 2, 8, 0),
                };
                onBox.CheckedChanged += async (s, e) =>
                {
                    if (_supeyAiSettings == null || onBox.Checked) return;
                    if (await HiatmeAiClient.SetRuleEnabledAsync(_supeyAiSettings, ruleId, false)
                            .ConfigureAwait(true))
                        await RefreshSupeyRulesPanelAsync().ConfigureAwait(true);
                };
                top.Controls.Add(onBox);
                var turnOffBtn = new SupeyButton
                {
                    Text = "Turn off",
                    Kind = SupeyButton.Variant.Outlined,
                    Size = new Size(72, 24),
                };
                turnOffBtn.Click += async (s, e) =>
                {
                    if (_supeyAiSettings == null) return;
                    if (await HiatmeAiClient.SetRuleEnabledAsync(_supeyAiSettings, ruleId, false)
                            .ConfigureAwait(true))
                        await RefreshSupeyRulesPanelAsync().ConfigureAwait(true);
                };
                top.Controls.Add(turnOffBtn);
            }
            else
            {
                var keepBtn = new SupeyButton
                {
                    Text = "Keep",
                    Kind = SupeyButton.Variant.Primary,
                    Size = new Size(72, 24),
                };
                keepBtn.Click += async (s, e) =>
                {
                    if (_supeyAiSettings == null) return;
                    if (await HiatmeAiClient.SetRuleEnabledAsync(_supeyAiSettings, ruleId, true)
                            .ConfigureAwait(true))
                        await RefreshSupeyRulesPanelAsync().ConfigureAwait(true);
                };
                top.Controls.Add(keepBtn);
                var offBtn = new SupeyButton
                {
                    Text = "Off",
                    Kind = SupeyButton.Variant.Outlined,
                    Size = new Size(52, 24),
                };
                offBtn.Click += async (s, e) =>
                {
                    if (_supeyAiSettings == null) return;
                    if (await HiatmeAiClient.SetRuleEnabledAsync(_supeyAiSettings, ruleId, false)
                            .ConfigureAwait(true))
                        await RefreshSupeyRulesPanelAsync().ConfigureAwait(true);
                };
                top.Controls.Add(offBtn);
            }
            var removeBtn = new SupeyButton
            {
                Text = "Remove",
                Kind = SupeyButton.Variant.Outlined,
                Size = new Size(76, 24),
            };
            removeBtn.Click += async (s, e) =>
            {
                if (_supeyAiSettings == null) return;
                if (await HiatmeAiClient.DeleteRuleAsync(_supeyAiSettings, ruleId).ConfigureAwait(true))
                    await RefreshSupeyRulesPanelAsync().ConfigureAwait(true);
            };
            top.Controls.Add(removeBtn);

            var body = new TextBox
            {
                Dock = DockStyle.Top,
                Multiline = true,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = card.BackColor,
                ForeColor = SupeyTheme.TextPrimary,
                Font = SupeyTheme.BodyFont,
                Text = detail,
                ScrollBars = ScrollBars.None,
            };
            int lines = Math.Max(2, Math.Min(10, detail.Length / 38 + 1));
            body.Height = Math.Max(44, lines * 18 + 6);
            card.Height = top.Height + body.Height + card.Padding.Vertical + 4;
            card.Controls.Add(body);
            card.Controls.Add(top);
            return card;
        }
    }
}
