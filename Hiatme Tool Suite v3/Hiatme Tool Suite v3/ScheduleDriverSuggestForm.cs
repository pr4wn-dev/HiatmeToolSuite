using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using MaterialSkin;
using MaterialSkin.Controls;

namespace Hiatme_Tool_Suite_v3
{
    internal sealed class ScheduleDriverSuggestForm : MaterialForm
    {
        private readonly IReadOnlyList<ScheduleBuilderDriverSuggestion> _suggestions;
        private readonly MCDownloadedTrip _trip;
        private readonly IReadOnlyDictionary<string, List<ScheduleBuilderPreviewLine>> _linesByTab;
        private readonly bool _showGroupColors;
        private int _index;
        private Label _headlineLbl;
        private Label _summaryLbl;
        private TextBox _reasonsBox;
        private Label _counterLbl;
        private ScheduleDriverSuggestPreviewPanel _previewPanel;
        private DarkOnAccentMaterialButton _confirmBtn;

        public ScheduleDriverSuggestForm(
            IReadOnlyList<ScheduleBuilderDriverSuggestion> suggestions,
            MCDownloadedTrip trip,
            string sourceTab,
            IReadOnlyDictionary<string, List<ScheduleBuilderPreviewLine>> linesByTab,
            bool showGroupColors,
            string tripLabel)
        {
            _suggestions = suggestions ?? throw new ArgumentNullException(nameof(suggestions));
            _trip = trip;
            _linesByTab = linesByTab;
            _showGroupColors = showGroupColors;
            if (_suggestions.Count == 0)
                throw new ArgumentException("At least one suggestion is required.", nameof(suggestions));

            Text = "Suggest driver — " + (tripLabel ?? "trip");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(700, 560);
            MinimumSize = new Size(700, 560);
            MaximumSize = new Size(700, 560);
            BackColor = DarkContextMenuRenderer.Background;

            try
            {
                var mgr = MaterialSkinManager.Instance;
                mgr.AddFormToManage(this);
                SupeyMaterialSkinBridge.ApplyTo(mgr);
            }
            catch { }

            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 56,
                BackColor = DarkContextMenuRenderer.Background,
            };

            var footerButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(0, 8, 16, 12),
                BackColor = DarkContextMenuRenderer.Background,
            };

            _confirmBtn = new DarkOnAccentMaterialButton
            {
                Text = "MOVE TRIP",
                AutoSize = false,
                Type = MaterialButton.MaterialButtonType.Contained,
                UseAccentColor = true,
                Size = new Size(110, 36),
                DialogResult = DialogResult.OK,
            };

            var nextBtn = new MaterialButton
            {
                Text = "NEXT",
                AutoSize = false,
                Type = MaterialButton.MaterialButtonType.Outlined,
                UseAccentColor = true,
                Size = new Size(88, 36),
                Margin = new Padding(0, 0, 8, 0),
            };
            nextBtn.Click += (s, e) => ShowSuggestion(_index + 1);

            var cancelBtn = new MaterialButton
            {
                Text = "CANCEL",
                AutoSize = false,
                Type = MaterialButton.MaterialButtonType.Text,
                UseAccentColor = false,
                NoAccentTextColor = Color.Gainsboro,
                Size = new Size(88, 36),
                Margin = new Padding(0, 0, 8, 0),
                DialogResult = DialogResult.Cancel,
            };

            footerButtons.Controls.Add(_confirmBtn);
            footerButtons.Controls.Add(nextBtn);
            footerButtons.Controls.Add(cancelBtn);
            footer.Controls.Add(footerButtons);

            var body = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = DarkContextMenuRenderer.Background,
                Padding = new Padding(16, 72, 16, 8),
            };

            _counterLbl = new Label
            {
                Dock = DockStyle.Top,
                Height = 20,
                ForeColor = Color.Silver,
                Font = new Font("Segoe UI", 9f),
                TextAlign = ContentAlignment.MiddleLeft,
            };

            _headlineLbl = new Label
            {
                Dock = DockStyle.Top,
                Height = 44,
                ForeColor = Color.Gainsboro,
                Font = new Font("Segoe UI Semibold", 11.5f),
                TextAlign = ContentAlignment.TopLeft,
            };

            _summaryLbl = new Label
            {
                Dock = DockStyle.Top,
                Height = 36,
                ForeColor = Color.Silver,
                Font = new Font("Segoe UI", 9.5f),
                TextAlign = ContentAlignment.TopLeft,
            };

            _previewPanel = new ScheduleDriverSuggestPreviewPanel
            {
                Dock = DockStyle.Top,
                Height = 280,
                Margin = new Padding(0, 4, 0, 6),
            };

            _reasonsBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(28, 28, 28),
                ForeColor = Color.Gainsboro,
                Font = new Font("Segoe UI", 9.25f),
                ScrollBars = ScrollBars.Vertical,
            };

            var stack = new Panel { Dock = DockStyle.Fill, BackColor = DarkContextMenuRenderer.Background };
            stack.Controls.Add(_reasonsBox);
            stack.Controls.Add(_previewPanel);
            stack.Controls.Add(_summaryLbl);
            stack.Controls.Add(_headlineLbl);
            stack.Controls.Add(_counterLbl);

            body.Controls.Add(stack);

            AcceptButton = _confirmBtn;
            CancelButton = cancelBtn;

            Controls.Add(body);
            Controls.Add(footer);

            SupeyDarkScrollBars.Apply(this);
            SupeyDarkScrollBars.Apply(body);

            ShowSuggestion(0);
        }

        public ScheduleBuilderDriverSuggestion CurrentSuggestion => _suggestions[_index];

        private void ShowSuggestion(int index)
        {
            _index = ((index % _suggestions.Count) + _suggestions.Count) % _suggestions.Count;
            var s = _suggestions[_index];

            _counterLbl.Text = "Suggestion " + (_index + 1) + " of " + _suggestions.Count
                + (s.Feasible ? " · timing OK" : " · timing tight");

            _headlineLbl.Text = s.Headline ?? "";
            _headlineLbl.ForeColor = s.Feasible ? Color.FromArgb(180, 220, 160) : Color.FromArgb(220, 180, 120);

            _summaryLbl.Text = s.Summary ?? "";

            if (_trip != null && _linesByTab != null)
            {
                _previewPanel.SetPreview(_trip, s, _linesByTab, _showGroupColors);
            }
            else
            {
                _previewPanel.SetPreview(null, null, null, _showGroupColors);
            }

            var lines = new List<string>();
            if (s.Reasons != null)
            {
                foreach (string r in s.Reasons)
                {
                    if (!string.IsNullOrWhiteSpace(r))
                        lines.Add("• " + r.Trim());
                }
            }
            _reasonsBox.Text = string.Join(Environment.NewLine, lines);

            _confirmBtn.Text = s.Feasible ? "MOVE TRIP" : "MOVE ANYWAY";
        }
    }
}
