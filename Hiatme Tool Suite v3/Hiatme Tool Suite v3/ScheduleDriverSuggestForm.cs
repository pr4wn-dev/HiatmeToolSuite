using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
namespace Hiatme_Tool_Suite_v3
{
    internal sealed class ScheduleDriverSuggestForm : SupeyForm
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
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(760, 720);
            MinimumSize = new Size(720, 620);
            BackColor = SupeyTheme.Surface;

            try
            {
            }
            catch { }

            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 56,
                BackColor = SupeyTheme.SurfaceHeader,
            };

            const int btnW = 96;
            const int btnH = 36;
            const int gap = 8;
            const int pad = 16;
            int btnY = (footer.Height - btnH) / 2;

            _confirmBtn = new DarkOnAccentMaterialButton
            {
                Text = "MOVE",
                AutoSize = false,
                Type = SupeyMaterialButton.MaterialButtonType.Contained,
                UseAccentColor = true,
                Size = new Size(btnW, btnH),
                DialogResult = DialogResult.OK,
            };

            var nextBtn = new SupeyMaterialButton
            {
                Text = "NEXT",
                AutoSize = false,
                Type = SupeyMaterialButton.MaterialButtonType.Outlined,
                UseAccentColor = true,
                Size = new Size(btnW, btnH),
            };
            nextBtn.Click += (s, e) => ShowSuggestion(_index + 1);

            var cancelBtn = new SupeyMaterialButton
            {
                Text = "CANCEL",
                AutoSize = false,
                Type = SupeyMaterialButton.MaterialButtonType.Text,
                UseAccentColor = false,
                NoAccentTextColor = SupeyTheme.TextSecondary,
                Size = new Size(btnW, btnH),
                DialogResult = DialogResult.Cancel,
            };

            void LayoutFooterButtons()
            {
                int moveX = footer.ClientSize.Width - pad - btnW;
                int nextX = moveX - gap - btnW;
                int cancelX = nextX - gap - btnW;
                _confirmBtn.Location = new Point(moveX, btnY);
                nextBtn.Location = new Point(nextX, btnY);
                cancelBtn.Location = new Point(cancelX, btnY);
            }

            footer.Controls.Add(cancelBtn);
            footer.Controls.Add(nextBtn);
            footer.Controls.Add(_confirmBtn);
            footer.Resize += (s, e) => LayoutFooterButtons();
            LayoutFooterButtons();

            var body = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.Surface,
                Padding = new Padding(16, 12, 16, 8),
            };

            _counterLbl = new Label
            {
                Dock = DockStyle.Top,
                Height = 20,
                ForeColor = SupeyTheme.TextSecondary,
                Font = new Font("Segoe UI", 9f),
                TextAlign = ContentAlignment.MiddleLeft,
            };

            _headlineLbl = new Label
            {
                Dock = DockStyle.Top,
                Height = 44,
                ForeColor = SupeyTheme.TextPrimary,
                Font = new Font("Segoe UI Semibold", 11.5f),
                TextAlign = ContentAlignment.TopLeft,
            };

            _summaryLbl = new Label
            {
                Dock = DockStyle.Top,
                Height = 36,
                ForeColor = SupeyTheme.TextSecondary,
                Font = new Font("Segoe UI", 9.5f),
                TextAlign = ContentAlignment.TopLeft,
            };

            _previewPanel = new ScheduleDriverSuggestPreviewPanel
            {
                Dock = DockStyle.Top,
                Height = 240,
                Margin = new Padding(0, 4, 0, 6),
            };

            var whyLbl = new Label
            {
                Dock = DockStyle.Top,
                Height = 28,
                Text = "Why this slot",
                ForeColor = SupeyTheme.TextSecondary,
                Font = new Font("Segoe UI Semibold", 9f),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(0, 6, 0, 0),
            };

            var reasonsHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.SurfaceElevated,
                Padding = new Padding(10, 8, 6, 8),
            };

            _reasonsBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                WordWrap = true,
                BorderStyle = BorderStyle.None,
                BackColor = SupeyTheme.SurfaceElevated,
                ForeColor = SupeyTheme.TextPrimary,
                Font = new Font("Segoe UI", 10f),
                ScrollBars = ScrollBars.Vertical,
                TabStop = false,
            };
            reasonsHost.Controls.Add(_reasonsBox);

            var stack = new Panel { Dock = DockStyle.Fill, BackColor = SupeyTheme.Surface };
            stack.Controls.Add(reasonsHost);
            stack.Controls.Add(whyLbl);
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
            SupeyDarkScrollBars.Apply(_reasonsBox);

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
            _headlineLbl.ForeColor = s.Feasible ? SupeyTheme.SuccessText : SupeyTheme.WarnText;

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
            _reasonsBox.Text = string.Join(Environment.NewLine + Environment.NewLine, lines);

            _confirmBtn.Text = "MOVE";
        }
    }
}
