using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    public partial class Form1
    {
        private Panel _supeyOutOfAreaHost;
        private ListBox _supeyOutOfAreaList;
        private TextBox _supeyOutOfAreaAddTxt;
        private Label _supeyOutOfAreaStatusLbl;
        private Label _supeyRulesTabOutOfArea;

        private void BuildSupeyOutOfAreaTab(Panel host)
        {
            _supeyOutOfAreaHost = host;
            host.Controls.Clear();
            host.Padding = new Padding(4);

            _supeyOutOfAreaStatusLbl = new Label
            {
                Dock = DockStyle.Top,
                Height = 52,
                ForeColor = SupeyTheme.TextSecondary,
                BackColor = SupeyTheme.SurfaceBase,
                Font = SupeyTheme.CaptionFont,
                Text = "Towns we do not service. Saved on the office server — all desks use this list on BUILD. Match: PU or DO city contains the name.",
            };

            _supeyOutOfAreaList = new ListBox
            {
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.SurfaceElevated,
                ForeColor = SupeyTheme.TextPrimary,
                BorderStyle = BorderStyle.None,
                IntegralHeight = false,
            };

            var addRow = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 36,
                BackColor = SupeyTheme.SurfaceBase,
                Padding = new Padding(0, 4, 0, 0),
            };
            _supeyOutOfAreaAddTxt = new TextBox
            {
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.SurfaceElevated,
                ForeColor = SupeyTheme.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle,
            };
            var addBtn = new Button
            {
                Text = "Add",
                Dock = DockStyle.Right,
                Width = 56,
                FlatStyle = FlatStyle.Flat,
                BackColor = SupeyTheme.SurfaceElevated,
                ForeColor = SupeyTheme.TextPrimary,
            };
            addBtn.Click += async (s, e) => await SupeyOutOfAreaAddAsync().ConfigureAwait(true);
            _supeyOutOfAreaAddTxt.KeyDown += async (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    await SupeyOutOfAreaAddAsync().ConfigureAwait(true);
                }
            };
            addRow.Controls.Add(addBtn);
            addRow.Controls.Add(_supeyOutOfAreaAddTxt);

            var btnRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 36,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = SupeyTheme.SurfaceBase,
                Padding = new Padding(0, 4, 0, 0),
            };
            var removeBtn = MakeRulesSmallButton("Remove");
            removeBtn.Click += async (s, e) => await SupeyOutOfAreaRemoveSelectedAsync().ConfigureAwait(true);
            var saveBtn = MakeRulesSmallButton("Save to server");
            saveBtn.Click += async (s, e) => await SupeyOutOfAreaSaveAsync().ConfigureAwait(true);
            var refreshBtn = MakeRulesSmallButton("Refresh");
            refreshBtn.Click += async (s, e) => await RefreshSupeyOutOfAreaListAsync().ConfigureAwait(true);
            btnRow.Controls.Add(removeBtn);
            btnRow.Controls.Add(saveBtn);
            btnRow.Controls.Add(refreshBtn);

            host.Controls.Add(_supeyOutOfAreaList);
            host.Controls.Add(btnRow);
            host.Controls.Add(addRow);
            host.Controls.Add(_supeyOutOfAreaStatusLbl);
        }

        private async Task RefreshSupeyOutOfAreaListAsync()
        {
            if (_supeyOutOfAreaList == null) return;
            if (_supeyAiSettings == null)
                _supeyAiSettings = HiatmeAiSettings.Load();

            IList<string> areas;
            string status;
            if (HiatmeGeoSettings.UseServer)
            {
                bool pushed = await SupeyOutOfArea.TrySyncLocalFileToServerAsync(_supeyAiSettings)
                    .ConfigureAwait(true);
                areas = await HiatmeAiClient.GetOutOfAreaAsync(_supeyAiSettings).ConfigureAwait(true);
                SupeyOutOfArea.TrySaveLocalFallback(areas);
                status = areas.Count + " area(s) from office server.";
                if (pushed)
                    status += " Offline edits synced.";
            }
            else
            {
                areas = SupeyOutOfArea.LoadLocalFallback();
                SupeyOutOfArea.SetCachedAreas(areas);
                status = areas.Count + " area(s) from local file (panel offline).";
            }

            _supeyOutOfAreaList.Items.Clear();
            foreach (var a in areas)
                _supeyOutOfAreaList.Items.Add(a);
            if (_supeyOutOfAreaStatusLbl != null)
                _supeyOutOfAreaStatusLbl.Text =
                    "Towns we do not service (shared on BUILD). " + status;
        }

        private List<string> SupeyOutOfAreaListSnapshot()
        {
            var list = new List<string>();
            if (_supeyOutOfAreaList == null) return list;
            foreach (var item in _supeyOutOfAreaList.Items)
            {
                var s = (item?.ToString() ?? "").Trim();
                if (s.Length > 0) list.Add(s);
            }
            return SupeyOutOfArea.NormalizeAreas(list);
        }

        private async Task SupeyOutOfAreaAddAsync()
        {
            if (_supeyOutOfAreaAddTxt == null || _supeyOutOfAreaList == null) return;
            var name = (_supeyOutOfAreaAddTxt.Text ?? "").Trim();
            if (name.Length == 0) return;
            foreach (var item in _supeyOutOfAreaList.Items)
            {
                if (string.Equals(item?.ToString(), name, StringComparison.OrdinalIgnoreCase))
                {
                    _supeyOutOfAreaAddTxt.Clear();
                    return;
                }
            }
            _supeyOutOfAreaList.Items.Add(name);
            _supeyOutOfAreaAddTxt.Clear();
            await SupeyOutOfAreaSaveAsync().ConfigureAwait(true);
        }

        private async Task SupeyOutOfAreaRemoveSelectedAsync()
        {
            if (_supeyOutOfAreaList == null || _supeyOutOfAreaList.SelectedIndices.Count == 0) return;
            var indices = _supeyOutOfAreaList.SelectedIndices.Cast<int>().OrderByDescending(i => i).ToList();
            foreach (var i in indices)
                _supeyOutOfAreaList.Items.RemoveAt(i);
            await SupeyOutOfAreaSaveAsync().ConfigureAwait(true);
        }

        private async Task SupeyOutOfAreaSaveAsync()
        {
            if (_supeyAiSettings == null)
                _supeyAiSettings = HiatmeAiSettings.Load();
            var areas = SupeyOutOfAreaListSnapshot();
            if (areas.Count == 0)
            {
                MessageBox.Show(this, "Add at least one town name before saving.", "No-go areas",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!HiatmeGeoSettings.UseServer)
            {
                if (SupeyOutOfArea.TrySaveLocalFallback(areas))
                    SetSupeyStatus("No-go areas saved locally — will sync to the server when the panel is online.");
                else
                    MessageBox.Show(this, "Could not write local no-go file.", "No-go areas",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SupeyOutOfArea.SetCachedAreas(areas);
                return;
            }
            bool ok = await HiatmeAiClient.SetOutOfAreaAsync(_supeyAiSettings, areas).ConfigureAwait(true);
            if (ok)
            {
                SupeyOutOfArea.TrySaveLocalFallback(areas);
                SetSupeyStatus("No-go areas saved — all desks will use this list on BUILD.");
            }
            else
                MessageBox.Show(this,
                    "Could not save to the office server. Check panel URL and try Refresh.",
                    "No-go areas", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
