using System;
using System.Drawing;
using System.Windows.Forms;
using Hiatme_Tool_Suite_v3.Properties;

namespace Hiatme_Tool_Suite_v3
{
    public partial class Form1
    {
        private const int FsSettingsPad = 10;

        private CheckBox _fsSettingsShowGaps;
        private CheckBox _fsSettingsShowGroupColors;

        private bool _fsShowGaps;
        private bool _fsShowGroupColors;

        private void LoadFsScheduleBuilderSettings()
        {
            _fsShowGaps = Settings.Default.FsShowGaps;
            _fsShowGroupColors = Settings.Default.FsShowGroupColors;
        }

        private void BuildFsSettingsPanel(Panel host)
        {
            host.BackColor = SupeyTheme.Surface;
            host.Padding = new Padding(FsSettingsPad, FsSettingsPad, FsSettingsPad, FsSettingsPad);

            var layout = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = false,
                BackColor = SupeyTheme.Surface,
            };

            layout.Controls.Add(MakeFsSettingsHintLabel(
                "Trip list and map display options. Saved for your next session."));

            layout.Controls.Add(MakeFsSettingsOption(
                "Show gap rows",
                "Blank template rows between groups for drag-and-drop splits.",
                _fsShowGaps,
                out _fsSettingsShowGaps,
                OnFsSettingsShowGapsChanged));

            layout.Controls.Add(MakeFsSettingsOption(
                "Show group colors",
                "Color-coded group headers, trip stripes, and map routes.",
                _fsShowGroupColors,
                out _fsSettingsShowGroupColors,
                OnFsSettingsShowGroupColorsChanged));

            host.Controls.Add(layout);
        }

        private static Label MakeFsSettingsHintLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                MaximumSize = new Size(240, 0),
                ForeColor = SupeyTheme.TextMuted,
                BackColor = SupeyTheme.Surface,
                Font = new Font("Segoe UI", 8.75f),
                Margin = new Padding(0, 0, 0, 12),
            };
        }

        private static Panel MakeFsSettingsOption(
            string title,
            string hint,
            bool isChecked,
            out CheckBox checkBox,
            EventHandler onChanged)
        {
            var block = new Panel
            {
                AutoSize = true,
                BackColor = SupeyTheme.Surface,
                Margin = new Padding(0, 0, 0, 14),
                Width = 240,
            };

            checkBox = new CheckBox
            {
                Text = title,
                Checked = isChecked,
                AutoSize = true,
                ForeColor = SupeyTheme.TextPrimary,
                BackColor = SupeyTheme.Surface,
                Font = new Font("Segoe UI Semibold", 9.25f),
                Location = new Point(0, 0),
            };
            checkBox.CheckedChanged += onChanged;

            var hintLbl = new Label
            {
                Text = hint,
                AutoSize = true,
                MaximumSize = new Size(220, 0),
                ForeColor = SupeyTheme.TextMuted,
                BackColor = SupeyTheme.Surface,
                Font = new Font("Segoe UI", 8.5f),
                Location = new Point(22, checkBox.Height + 2),
            };

            block.Controls.Add(hintLbl);
            block.Controls.Add(checkBox);
            block.Height = hintLbl.Bottom + 2;
            return block;
        }

        private void OnFsSettingsShowGapsChanged(object sender, EventArgs e)
        {
            if (_fsSettingsShowGaps == null) return;
            _fsShowGaps = _fsSettingsShowGaps.Checked;
            Settings.Default.FsShowGaps = _fsShowGaps;
            Settings.Default.Save();
            ApplyFsDisplaySettings();
        }

        private void OnFsSettingsShowGroupColorsChanged(object sender, EventArgs e)
        {
            if (_fsSettingsShowGroupColors == null) return;
            _fsShowGroupColors = _fsSettingsShowGroupColors.Checked;
            Settings.Default.FsShowGroupColors = _fsShowGroupColors;
            Settings.Default.Save();
            ApplyFsDisplaySettings();
        }

        private void ApplyFsDisplaySettings()
        {
            if (_fsHasPreview && !string.IsNullOrWhiteSpace(_fsActiveDriverTab))
                ShowFsTripsForTab(_fsActiveDriverTab);
            else if (_fsTripsLv != null)
                _fsTripsLv.Invalidate();

            if (_fsMap != null)
            {
                _fsMap.UseGroupRouteColors = _fsShowGroupColors;
                if (_fsHasPreview && !string.IsNullOrWhiteSpace(_fsActiveDriverTab)
                    && !_fsActiveDriverTab.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
                    _ = RefreshFsMapForCurrentTabAsync();
            }
        }

        internal bool FsShowGapsEnabled => _fsShowGaps;

        internal bool FsShowGroupColorsEnabled => _fsShowGroupColors;
    }
}
