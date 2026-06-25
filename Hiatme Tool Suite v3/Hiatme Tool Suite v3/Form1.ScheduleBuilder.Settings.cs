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
        private CheckBox _fsSettingsMultiRowGaps;
        private CheckBox _fsSettingsShowGroupColors;
        private CheckBox _fsSettingsSafeBuildMode;
        private CheckBox _fsSettingsAdvancedSuggestHistory;

        private Label _fsSettingsAdvancedToggle;
        private readonly System.Collections.Generic.List<Control> _fsAdvancedSettingControls =
            new System.Collections.Generic.List<Control>();
        private bool _fsAdvancedSettingsExpanded;

        private bool _fsShowGaps;
        private bool _fsMultiRowGaps;
        private bool _fsShowGroupColors;
        private bool _fsSafeBuildMode;
        private bool _fsAdvancedSuggestHistory;

        private void LoadFsScheduleBuilderSettings()
        {
            _fsShowGaps = Settings.Default.FsShowGaps;
            _fsMultiRowGaps = Settings.Default.FsMultiRowGaps;
            _fsShowGroupColors = Settings.Default.FsShowGroupColors;
            _fsSafeBuildMode = Settings.Default.FsSafeBuildMode;
            _fsAdvancedSuggestHistory = Settings.Default.FsEnableAdvancedSuggestHistory;
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
                AutoScroll = true,
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
                "Multi-row gaps",
                "Keep every blank template row when building. Off = each run of blank rows collapses to one. Needs Show gap rows on to be visible.",
                _fsMultiRowGaps,
                out _fsSettingsMultiRowGaps,
                OnFsSettingsMultiRowGapsChanged));

            layout.Controls.Add(MakeFsSettingsOption(
                "Show group colors",
                "Group headers, stripes, and group tour routes. Each trip still gets a PU→DO line on the map. Off = PU→DO only (no group tour).",
                _fsShowGroupColors,
                out _fsSettingsShowGroupColors,
                OnFsSettingsShowGroupColorsChanged));

            _fsAdvancedSettingControls.Clear();

            _fsSettingsAdvancedToggle = MakeFsSettingsAdvancedToggle();
            layout.Controls.Add(_fsSettingsAdvancedToggle);

            var advancedHeader = MakeFsSettingsSectionHeader("Advanced options");
            layout.Controls.Add(advancedHeader);
            _fsAdvancedSettingControls.Add(advancedHeader);

            var safeBuildBlock = MakeFsSettingsOption(
                "Safe Build Mode",
                "Build schedule exactly from the normal template-first workflow. Keep this ON for default safe behavior.",
                _fsSafeBuildMode,
                out _fsSettingsSafeBuildMode,
                OnFsSettingsSafeBuildModeChanged);
            layout.Controls.Add(safeBuildBlock);
            _fsAdvancedSettingControls.Add(safeBuildBlock);

            var advancedSuggestBlock = MakeFsSettingsOption(
                "Advanced suggest assist (history)",
                "Optional post-build enhancement: Suggest Driver can use historical archive patterns to improve ranking. Feasibility rules still win.",
                _fsAdvancedSuggestHistory,
                out _fsSettingsAdvancedSuggestHistory,
                OnFsSettingsAdvancedSuggestHistoryChanged);
            layout.Controls.Add(advancedSuggestBlock);
            _fsAdvancedSettingControls.Add(advancedSuggestBlock);

            ApplyFsAdvancedSettingsVisibility();

            host.Controls.Add(layout);

            // Dark themed scrollbar to match the rest of the app when options overflow vertically.
            SupeyDarkScrollBars.Apply(host);
        }

        private static Label MakeFsSettingsHintLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                MaximumSize = new Size(218, 0),
                ForeColor = SupeyTheme.TextMuted,
                BackColor = SupeyTheme.Surface,
                Font = new Font("Segoe UI", 8.75f),
                Margin = new Padding(0, 0, 0, 12),
            };
        }

        private static Label MakeFsSettingsSectionHeader(string text)
        {
            return new Label
            {
                Text = (text ?? "").ToUpperInvariant(),
                AutoSize = true,
                MaximumSize = new Size(218, 0),
                ForeColor = SupeyTheme.TextSecondary,
                BackColor = SupeyTheme.Surface,
                Font = new Font("Segoe UI Semibold", 8.25f),
                Margin = new Padding(0, 6, 0, 10),
            };
        }

        private Label MakeFsSettingsAdvancedToggle()
        {
            var lbl = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(218, 0),
                ForeColor = SupeyTheme.AccentPrimary,
                BackColor = SupeyTheme.Surface,
                Font = new Font("Segoe UI Semibold", 9f),
                Margin = new Padding(0, 6, 0, 12),
                Cursor = Cursors.Hand,
            };
            lbl.Click += OnFsSettingsAdvancedToggleClicked;
            return lbl;
        }

        private void OnFsSettingsAdvancedToggleClicked(object sender, EventArgs e)
        {
            _fsAdvancedSettingsExpanded = !_fsAdvancedSettingsExpanded;
            ApplyFsAdvancedSettingsVisibility();
        }

        private void ApplyFsAdvancedSettingsVisibility()
        {
            if (_fsSettingsAdvancedToggle != null)
                _fsSettingsAdvancedToggle.Text = _fsAdvancedSettingsExpanded
                    ? "▾ Hide advanced options"
                    : "▸ Show advanced options";

            foreach (var c in _fsAdvancedSettingControls)
            {
                if (c != null)
                    c.Visible = _fsAdvancedSettingsExpanded;
            }
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
                Width = 218,
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
                MaximumSize = new Size(196, 0),
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

        private void OnFsSettingsMultiRowGapsChanged(object sender, EventArgs e)
        {
            if (_fsSettingsMultiRowGaps == null) return;
            _fsMultiRowGaps = _fsSettingsMultiRowGaps.Checked;
            Settings.Default.FsMultiRowGaps = _fsMultiRowGaps;
            Settings.Default.Save();
        }

        private void OnFsSettingsShowGroupColorsChanged(object sender, EventArgs e)
        {
            if (_fsSettingsShowGroupColors == null) return;
            _fsShowGroupColors = _fsSettingsShowGroupColors.Checked;
            Settings.Default.FsShowGroupColors = _fsShowGroupColors;
            Settings.Default.Save();
            ApplyFsDisplaySettings();
        }

        private void OnFsSettingsSafeBuildModeChanged(object sender, EventArgs e)
        {
            if (_fsSettingsSafeBuildMode == null) return;
            _fsSafeBuildMode = _fsSettingsSafeBuildMode.Checked;
            Settings.Default.FsSafeBuildMode = _fsSafeBuildMode;
            Settings.Default.Save();
        }

        private void OnFsSettingsAdvancedSuggestHistoryChanged(object sender, EventArgs e)
        {
            if (_fsSettingsAdvancedSuggestHistory == null) return;
            _fsAdvancedSuggestHistory = _fsSettingsAdvancedSuggestHistory.Checked;
            Settings.Default.FsEnableAdvancedSuggestHistory = _fsAdvancedSuggestHistory;
            Settings.Default.Save();
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
                _fsMap.TripFlatMapMode = !_fsShowGroupColors;
                if (_fsHasPreview && !string.IsNullOrWhiteSpace(_fsActiveDriverTab)
                    && !_fsActiveDriverTab.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
                    _ = RefreshFsMapForCurrentTabAsync();
            }
        }

        internal bool FsShowGapsEnabled => _fsShowGaps;

        internal bool FsMultiRowGapsEnabled => _fsMultiRowGaps;

        internal bool FsShowGroupColorsEnabled => _fsShowGroupColors;

        internal bool FsSafeBuildModeEnabled => _fsSafeBuildMode;

        internal bool FsAdvancedSuggestHistoryEnabled => _fsAdvancedSuggestHistory;

        /// <summary>Show gap rows in the list after a manual insert so the new row is visible.</summary>
        internal void FsRevealGapsForManualInsert()
        {
            if (_fsShowGaps)
            {
                if (_fsHasPreview && !string.IsNullOrWhiteSpace(_fsActiveDriverTab))
                    ShowFsTripsForTab(_fsActiveDriverTab);
                return;
            }

            _fsShowGaps = true;
            Settings.Default.FsShowGaps = true;
            Settings.Default.Save();
            if (_fsSettingsShowGaps != null)
                _fsSettingsShowGaps.Checked = true;
            ApplyFsDisplaySettings();
        }

        internal ScheduleBuilderPreviewCsvExport.Options MakeFsPreviewCsvExportOptions()
        {
            return new ScheduleBuilderPreviewCsvExport.Options
            {
                // Always persist route-break spacer rows in saved workbooks.
                IncludeGaps = true,
                // Colored spacer row at top of each group — no group number text on the saved sheet.
                IncludeGroupHeaders = FsShowGroupColorsEnabled,
                IncludeReserveSections = true,
            };
        }
    }
}
