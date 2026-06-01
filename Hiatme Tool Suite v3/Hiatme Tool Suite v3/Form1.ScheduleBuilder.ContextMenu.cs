using System;
using System.Drawing;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    public partial class Form1
    {
        private ContextMenuStrip _fsTripsCtxMenu;
        private ToolStripMenuItem _fsTripsCtxBanClient;
        private ToolStripMenuItem _fsTripsCtxUnbanClient;
        private ToolStripMenuItem _fsTripsCtxFocusMap;
        private ToolStripMenuItem _fsTripsCtxCopyForAi;
        private ToolStripMenuItem _fsTripsCtxCopyCurrentTab;
        private ToolStripMenuItem _fsTripsCtxCopySelectedTrip;
        private MCDownloadedTrip _fsTripsCtxTrip;

        private void BuildFsTripsContextMenu()
        {
            _fsTripsCtxMenu = new ContextMenuStrip
            {
                Renderer = new DarkContextMenuRenderer(),
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
                ShowImageMargin = true,
            };

            _fsTripsCtxBanClient = new ToolStripMenuItem("Ban client")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
                Image = MenuIconFactory.GetUnassignIcon(),
            };
            _fsTripsCtxBanClient.Click += (s, e) =>
            {
                if (_fsTripsCtxTrip != null)
                    FsBanClientFromTrip(_fsTripsCtxTrip, quietWhenMissing: true);
            };

            _fsTripsCtxUnbanClient = new ToolStripMenuItem("Remove from banned list")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
                Image = MenuIconFactory.GetAssignIcon(),
            };
            _fsTripsCtxUnbanClient.Click += (s, e) =>
            {
                if (_fsTripsCtxTrip != null)
                    FsUnbanClientFromTrip(_fsTripsCtxTrip);
            };

            _fsTripsCtxFocusMap = new ToolStripMenuItem("Show on map")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
                Image = MenuIconFactory.GetLocateIcon(),
            };
            _fsTripsCtxFocusMap.Click += (s, e) =>
            {
                if (_fsTripsCtxTrip != null && _fsMap != null)
                    _fsMap.FocusTrip(_fsTripsCtxTrip);
            };

            _fsTripsCtxCopyForAi = new ToolStripMenuItem("Copy for AI review (Cursor)")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
            };
            _fsTripsCtxCopyForAi.Click += (s, e) => FsCopyScheduleForAiReviewToClipboard();

            _fsTripsCtxCopyCurrentTab = new ToolStripMenuItem("Copy current tab")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
            };
            _fsTripsCtxCopyCurrentTab.Click += (s, e) => FsCopyCurrentTabToClipboard();

            _fsTripsCtxCopySelectedTrip = new ToolStripMenuItem("Copy selected trip")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
            };
            _fsTripsCtxCopySelectedTrip.Click += (s, e) => FsCopySelectedTripToClipboard();

            _fsTripsCtxMenu.Items.Add(_fsTripsCtxBanClient);
            _fsTripsCtxMenu.Items.Add(_fsTripsCtxUnbanClient);
            _fsTripsCtxMenu.Items.Add(new ToolStripSeparator());
            _fsTripsCtxMenu.Items.Add(_fsTripsCtxFocusMap);
            _fsTripsCtxMenu.Items.Add(new ToolStripSeparator());
            _fsTripsCtxMenu.Items.Add(_fsTripsCtxCopyForAi);
            _fsTripsCtxMenu.Items.Add(_fsTripsCtxCopyCurrentTab);
            _fsTripsCtxMenu.Items.Add(_fsTripsCtxCopySelectedTrip);
        }

        private void FsTripsLv_MouseUp_ShowContextMenu(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right || _fsTripsLv == null) return;

            var hit = _fsTripsLv.HitTest(e.Location);
            _fsTripsCtxTrip = null;
            if (hit.Item != null)
            {
                _fsTripsCtxTrip = GetFsTripFromListItem(hit.Item);
                if (_fsTripsCtxTrip != null)
                {
                    hit.Item.Selected = true;
                    hit.Item.Focused = true;
                }
            }

            bool hasTrip = _fsTripsCtxTrip != null;
            bool isBanned = hasTrip && ScheduleBuilderBannedClients.IsBanned(_fsTripsCtxTrip);

            bool hasBuild = _fsHasPreview && fsbuilder != null;

            _fsTripsCtxBanClient.Enabled = hasTrip;
            _fsTripsCtxUnbanClient.Enabled = hasTrip && isBanned;
            _fsTripsCtxFocusMap.Enabled = hasTrip;
            _fsTripsCtxCopyForAi.Enabled = hasBuild;
            _fsTripsCtxCopyCurrentTab.Enabled = hasBuild;
            _fsTripsCtxCopySelectedTrip.Enabled = hasTrip;

            if (hasTrip)
            {
                string name = (_fsTripsCtxTrip.ClientFullName ?? "").Trim();
                string age = string.IsNullOrWhiteSpace(_fsTripsCtxTrip.Age) ? "" : " · age " + _fsTripsCtxTrip.Age;
                _fsTripsCtxBanClient.Text = isBanned
                    ? "Ban client (already banned)"
                    : "Ban client — " + name + age;
                _fsTripsCtxUnbanClient.Text = "Remove ban — " + name + age;
            }
            else
            {
                _fsTripsCtxBanClient.Text = "Ban client";
                _fsTripsCtxUnbanClient.Text = "Remove from banned list";
            }

            _fsTripsCtxMenu.Show(_fsTripsLv, e.Location);
        }

        private void FsCopyScheduleForAiReviewToClipboard()
        {
            if (!_fsHasPreview || fsbuilder == null)
            {
                MessageBox.Show(this, "Run BUILD first, then copy for AI review.", "Schedule Builder",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                DateTime date = fsbdatepicker?.Value ?? DateTime.Today;
                string folder = date.DayOfWeek.ToString();
                string text = ScheduleBuilderReviewExport.BuildFull(
                    date, folder, fsbuilder, _fsLinesByTab, _fsActiveDriverTab);
                Clipboard.SetText(text);
                SetScheduleBuilderStatus("Copied full schedule for AI review — paste into Cursor chat.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not copy to clipboard:\n\n" + ex.Message, "Schedule Builder",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void FsCopyCurrentTabToClipboard()
        {
            if (!_fsHasPreview || fsbuilder == null || string.IsNullOrWhiteSpace(_fsActiveDriverTab))
            {
                MessageBox.Show(this, "Run BUILD and select a driver or Reserves tab first.", "Schedule Builder",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!_fsLinesByTab.TryGetValue(_fsActiveDriverTab, out var lines))
                lines = new System.Collections.Generic.List<ScheduleBuilderPreviewLine>();
            try
            {
                DateTime date = fsbdatepicker?.Value ?? DateTime.Today;
                string text = ScheduleBuilderReviewExport.BuildTab(date, _fsActiveDriverTab, lines, fsbuilder);
                Clipboard.SetText(text);
                SetScheduleBuilderStatus("Copied \"" + _fsActiveDriverTab + "\" tab to clipboard.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not copy to clipboard:\n\n" + ex.Message, "Schedule Builder",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void FsCopySelectedTripToClipboard()
        {
            if (_fsTripsCtxTrip == null) return;
            try
            {
                string text = ScheduleBuilderReviewExport.BuildSingleTrip(_fsTripsCtxTrip, _fsActiveDriverTab);
                Clipboard.SetText(text);
                SetScheduleBuilderStatus("Copied trip " + (_fsTripsCtxTrip.TripNumber ?? "") + " to clipboard.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not copy to clipboard:\n\n" + ex.Message, "Schedule Builder",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
