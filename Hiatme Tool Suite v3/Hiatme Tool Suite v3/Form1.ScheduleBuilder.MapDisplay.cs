using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    public partial class Form1
    {
        private Panel _fsMapModeToolbar;
        private FsMapModeIconButton _fsMapModeAllBtn;
        private FsMapModeIconButton _fsMapModeGroupBtn;
        private FsMapModeIconButton _fsMapModeTripsBtn;
        private Label _fsMapModeHintLbl;
        private ToolTip _fsMapModeTip;
        private FsMapDisplayMode _fsMapDisplayMode = FsMapDisplayMode.AllDriverTrips;

        private const int FsMapModeToolbarHeight = 36;

        private static string FsMapModeCaption(FsMapDisplayMode mode)
        {
            switch (mode)
            {
                case FsMapDisplayMode.SelectedGroup:
                    return "Selected group only";
                case FsMapDisplayMode.SelectedTrips:
                    return "Selected trip(s) only";
                default:
                    return "All trips on this driver";
            }
        }

        private void BuildFsMapModeToolbar(Panel host)
        {
            _fsMapModeToolbar = new Panel
            {
                Dock = DockStyle.Top,
                Height = FsMapModeToolbarHeight,
                BackColor = SupeyTheme.Surface,
                Padding = new Padding(4, 3, 4, 3),
            };

            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = SupeyTheme.Surface,
                Margin = new Padding(0),
                Padding = new Padding(0),
            };

            _fsMapModeAllBtn = CreateFsMapModeButton(FsMapDisplayMode.AllDriverTrips, ScheduleBuilderMapDisplayIcons.AllDriverTrips(false));
            _fsMapModeGroupBtn = CreateFsMapModeButton(FsMapDisplayMode.SelectedGroup, ScheduleBuilderMapDisplayIcons.SelectedGroup(false));
            _fsMapModeTripsBtn = CreateFsMapModeButton(FsMapDisplayMode.SelectedTrips, ScheduleBuilderMapDisplayIcons.SelectedTrips(false));

            flow.Controls.Add(_fsMapModeTripsBtn);
            flow.Controls.Add(_fsMapModeGroupBtn);
            flow.Controls.Add(_fsMapModeAllBtn);

            _fsMapModeHintLbl = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = SupeyTheme.TextMuted,
                Font = new Font("Segoe UI", 8.75f),
                Padding = new Padding(4, 0, 0, 0),
                AutoEllipsis = true,
            };

            _fsMapModeToolbar.Controls.Add(flow);
            _fsMapModeToolbar.Controls.Add(_fsMapModeHintLbl);
            host.Controls.Add(_fsMapModeToolbar);

            _fsMapModeTip = SupeyToolTip.Create(initialDelay: 200);
            _fsMapModeTip.SetToolTip(_fsMapModeAllBtn, FsMapModeCaption(FsMapDisplayMode.AllDriverTrips));
            _fsMapModeTip.SetToolTip(_fsMapModeGroupBtn, FsMapModeCaption(FsMapDisplayMode.SelectedGroup));
            _fsMapModeTip.SetToolTip(_fsMapModeTripsBtn, FsMapModeCaption(FsMapDisplayMode.SelectedTrips));

            SetFsMapDisplayMode(_fsMapDisplayMode, applyFilter: false);
        }

        private FsMapModeIconButton CreateFsMapModeButton(FsMapDisplayMode mode, Bitmap icon)
        {
            var btn = new FsMapModeIconButton
            {
                Icon = icon,
                Tag = mode,
                Margin = new Padding(1, 0, 0, 0),
            };
            btn.Click += (s, e) => SetFsMapDisplayMode(mode, applyFilter: true);
            btn.MouseEnter += (s, e) => UpdateFsMapModeHint(mode, hovered: true);
            btn.MouseLeave += (s, e) => UpdateFsMapModeHint(_fsMapDisplayMode, hovered: false);
            return btn;
        }

        private void UpdateFsMapModeHint(FsMapDisplayMode mode, bool hovered)
        {
            if (_fsMapModeHintLbl == null)
                return;

            string caption = FsMapModeCaption(mode);
            _fsMapModeHintLbl.ForeColor = hovered ? SupeyTheme.TextPrimary : SupeyTheme.TextMuted;
            _fsMapModeHintLbl.Text = hovered ? caption : "Map · " + caption;
        }

        private void SetFsMapDisplayMode(FsMapDisplayMode mode, bool applyFilter)
        {
            _fsMapDisplayMode = mode;
            if (_fsMapModeAllBtn != null)
            {
                _fsMapModeAllBtn.Selected = mode == FsMapDisplayMode.AllDriverTrips;
                _fsMapModeGroupBtn.Selected = mode == FsMapDisplayMode.SelectedGroup;
                _fsMapModeTripsBtn.Selected = mode == FsMapDisplayMode.SelectedTrips;
            }

            UpdateFsMapModeHint(mode, hovered: false);

            if (!applyFilter)
            {
                _fsMap?.SetMapDisplayFilterMode(mode);
                return;
            }

            ApplyFsMapDisplayFilter();
        }

        private void ApplyFsMapDisplayFilter(bool autoFit = true)
        {
            if (_fsMap == null || !_fsMap.Visible || !ScheduleOsrmGate.PreviewRoutingOk)
                return;

            ResolveFsMapFilterSelection(
                out int? groupNumber,
                out List<string> tripNumbers);

            _fsMap.ApplyMapDisplayFilter(_fsMapDisplayMode, groupNumber, tripNumbers, autoFit);
            ApplyFsMapTripSelectionHighlight();
        }

        private void ApplyFsMapTripSelectionHighlight()
        {
            if (_fsMap == null || !_fsMap.Visible || !ScheduleOsrmGate.PreviewRoutingOk)
                return;

            var tripNumbers = new List<string>();
            if (_fsTripsLv != null && _fsTripsLv.SelectedItems.Count > 0)
            {
                foreach (ListViewItem item in _fsTripsLv.SelectedItems)
                {
                    if (item?.Tag is FsPreviewGapTag || item?.Tag is FsPreviewSectionHeaderTag)
                        continue;
                    if (TryResolveFsListItemSelection(item, out _, out var trip) && trip != null)
                    {
                        string tn = (trip.TripNumber ?? "").Trim();
                        if (tn.Length > 0
                            && !tripNumbers.Any(x => string.Equals(x, tn, StringComparison.OrdinalIgnoreCase)))
                            tripNumbers.Add(tn);
                    }
                }
            }

            _fsMap.SetSelectedTripHighlight(tripNumbers);
        }

        private void ResolveFsMapFilterSelection(out int? groupNumber, out List<string> tripNumbers)
        {
            groupNumber = null;
            tripNumbers = new List<string>();

            if (_fsTripsLv == null || _fsTripsLv.SelectedItems.Count == 0)
                return;

            foreach (ListViewItem item in _fsTripsLv.SelectedItems)
            {
                if (TryResolveFsListItemSelection(item, out var group, out var trip))
                {
                    if (group != null && !groupNumber.HasValue)
                        groupNumber = group.GroupNumber;
                    if (trip != null)
                    {
                        string tn = (trip.TripNumber ?? "").Trim();
                        if (tn.Length > 0 && !tripNumbers.Any(x => string.Equals(x, tn, StringComparison.OrdinalIgnoreCase)))
                            tripNumbers.Add(tn);
                    }
                }
            }

            if (groupNumber == null && tripNumbers.Count > 0
                && _fsGroupsByTab.TryGetValue(_fsActiveDriverTab ?? "", out var groups))
            {
                foreach (string tn in tripNumbers)
                {
                    foreach (var g in groups)
                    {
                        if (g?.Trips == null)
                            continue;
                        foreach (var t in g.Trips)
                        {
                            if (string.Equals((t?.TripNumber ?? "").Trim(), tn, StringComparison.OrdinalIgnoreCase))
                            {
                                groupNumber = g.GroupNumber;
                                break;
                            }
                        }
                        if (groupNumber.HasValue)
                            break;
                    }
                    if (groupNumber.HasValue)
                        break;
                }
            }
        }

        private bool TryResolveFsListItemSelection(
            ListViewItem item,
            out SupeyTripCluster group,
            out MCDownloadedTrip trip)
        {
            group = null;
            trip = null;
            if (item?.Tag is FsPreviewGapTag || item?.Tag is FsPreviewSectionHeaderTag)
                return false;

            if (item?.Tag is FsPreviewNoteTag noteTag)
            {
                group = noteTag.Group;
                return group != null;
            }

            if (item?.Tag is FsPreviewTripTag rowTag)
            {
                trip = rowTag.Trip;
                group = rowTag.Group;
                return trip != null || group != null;
            }

            trip = item?.Tag as MCDownloadedTrip;
            if (trip != null
                && _fsGroupsByTab.TryGetValue(_fsActiveDriverTab ?? "", out var groups))
            {
                group = FindFsGroupForTrip(groups, trip);
            }

            return trip != null || group != null;
        }
    }
}
