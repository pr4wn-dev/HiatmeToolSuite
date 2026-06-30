using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Confirm a Modivcare reroute and pick the portal-provided reason.</summary>
    internal sealed class ScheduleRerouteReasonForm : SupeyForm
    {
        private const int DialogWidth = 500;
        private const int ContentWidth = 420;

        private readonly SupeyComboBox _reasonCombo;

        public string SelectedReasonCode { get; private set; }

        public string SelectedReasonLabel { get; private set; }

        public ScheduleRerouteReasonForm(
            string tripNumber,
            IReadOnlyList<MCTripRerouter.RerouteReasonOption> reasons)
        {
            Text = "Reroute on Modivcare";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(DialogWidth, 340);
            MinimumSize = new Size(DialogWidth, 340);
            MaximumSize = new Size(DialogWidth, 340);
            BackColor = SupeyTheme.Surface;

            var reasonsList = (reasons ?? Array.Empty<MCTripRerouter.RerouteReasonOption>())
                .Where(r => r != null && r.Code.Length > 0 && r.Label.Length > 0)
                .ToList();
            if (reasonsList.Count == 0)
            {
                reasonsList.Add(new MCTripRerouter.RerouteReasonOption(
                    MCTripRerouter.DefaultRerouteReasonCode,
                    "Not in Service Area"));
            }

            string num = (tripNumber ?? "").Trim();
            string tripLine = num.Length > 0 ? "Trip " + num : "This trip";

            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 56,
                BackColor = SupeyTheme.Surface,
            };

            var footerButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(0, 8, 20, 12),
                BackColor = SupeyTheme.Surface,
            };

            var rerouteBtn = new DarkOnAccentMaterialButton
            {
                Text = "REROUTE",
                AutoSize = false,
                Type = SupeyMaterialButton.MaterialButtonType.Contained,
                UseAccentColor = true,
                Size = new Size(112, 36),
                DialogResult = DialogResult.OK,
            };
            rerouteBtn.Click += (s, e) => CommitSelection(reasonsList);

            var cancelBtn = new SupeyMaterialButton
            {
                Text = "CANCEL",
                AutoSize = false,
                Type = SupeyMaterialButton.MaterialButtonType.Text,
                UseAccentColor = false,
                NoAccentTextColor = SupeyTheme.TextSecondary,
                Size = new Size(96, 36),
                Margin = new Padding(0, 0, 8, 0),
                DialogResult = DialogResult.Cancel,
            };

            footerButtons.Controls.Add(rerouteBtn);
            footerButtons.Controls.Add(cancelBtn);
            footer.Controls.Add(footerButtons);

            var body = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.Surface,
                Padding = new Padding(24, 76, 24, 12),
            };

            var stack = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                BackColor = SupeyTheme.Surface,
            };

            stack.Controls.Add(MakeLabel(
                "Submit reroute to Modivcare?",
                new Font("Segoe UI Semibold", 11f),
                SupeyTheme.TextPrimary,
                0), 0, 0);
            stack.Controls.Add(MakeLabel(
                tripLine + " will move to Reserves → Reroutes and be marked red on success.",
                new Font("Segoe UI", 9f),
                SupeyTheme.TextSecondary,
                8), 0, 1);
            stack.Controls.Add(MakeLabel(
                "Reason (from Modivcare):",
                new Font("Segoe UI", 9.75f),
                SupeyTheme.TextPrimary,
                18), 0, 2);

            _reasonCombo = new SupeyComboBox
            {
                Dock = DockStyle.Fill,
                Hint = "Reroute reason",
                UseTallSize = true,
            };
            foreach (MCTripRerouter.RerouteReasonOption reason in reasonsList)
                _reasonCombo.Items.Add(reason);

            SelectDefaultReason(reasonsList);
            _reasonCombo.SelectedIndexChanged += (s, e) => CommitSelection(reasonsList);

            var comboHost = new Panel
            {
                Dock = DockStyle.Top,
                Height = 58,
                BackColor = SupeyTheme.Surface,
                Margin = new Padding(0, 0, 0, 0),
            };
            comboHost.Controls.Add(_reasonCombo);

            stack.Controls.Add(comboHost, 0, 3);

            body.Controls.Add(stack);

            AcceptButton = rerouteBtn;
            CancelButton = cancelBtn;

            Controls.Add(body);
            Controls.Add(footer);

            SupeyDarkScrollBars.Apply(this);

            CommitSelection(reasonsList);
        }

        private void SelectDefaultReason(IReadOnlyList<MCTripRerouter.RerouteReasonOption> reasons)
        {
            int pick = 0;
            for (int i = 0; i < reasons.Count; i++)
            {
                if (string.Equals(reasons[i].Code, MCTripRerouter.DefaultRerouteReasonCode, StringComparison.Ordinal))
                {
                    pick = i;
                    break;
                }

                if (reasons[i].Label.IndexOf("Not in Service Area", StringComparison.OrdinalIgnoreCase) >= 0)
                    pick = i;
            }

            if (pick >= 0 && pick < _reasonCombo.Items.Count)
                _reasonCombo.SelectedIndex = pick;
            else if (_reasonCombo.Items.Count > 0)
                _reasonCombo.SelectedIndex = 0;
        }

        private void CommitSelection(IReadOnlyList<MCTripRerouter.RerouteReasonOption> reasons)
        {
            if (_reasonCombo.SelectedItem is MCTripRerouter.RerouteReasonOption selected)
            {
                SelectedReasonCode = selected.Code;
                SelectedReasonLabel = selected.Label;
                return;
            }

            if (reasons != null && reasons.Count > 0)
            {
                SelectedReasonCode = reasons[0].Code;
                SelectedReasonLabel = reasons[0].Label;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (DialogResult == DialogResult.OK && _reasonCombo.Items.Count > 0)
            {
                var reasons = _reasonCombo.Items.Cast<MCTripRerouter.RerouteReasonOption>().ToList();
                CommitSelection(reasons);
                if (string.IsNullOrWhiteSpace(SelectedReasonCode))
                {
                    e.Cancel = true;
                    SupeyMessageForm.Show(this,
                        "Reroute on Modivcare",
                        "Choose a reroute reason from the Modivcare list.",
                        SupeyMessageKind.Information,
                        "Reason required");
                }
            }

            base.OnFormClosing(e);
        }

        private static Label MakeLabel(string text, Font font, Color color, int topMargin)
        {
            return new Label
            {
                Text = text,
                Font = font,
                ForeColor = color,
                AutoSize = true,
                MaximumSize = new Size(ContentWidth, 0),
                Margin = new Padding(0, topMargin, 0, 0),
                BackColor = SupeyTheme.Surface,
            };
        }
    }
}
