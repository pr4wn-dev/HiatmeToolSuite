using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MaterialSkin;
using MaterialSkin.Controls;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Modal "pull driver roster from WellRyde" picker. Caller hands the dialog a live signed-in
    /// <see cref="WellRydePortalSession"/> plus the existing roster (so already-imported drivers
    /// can be dimmed / pre-checked); the dialog runs the import job, shows incremental progress,
    /// and returns the user-selected <see cref="WellRydeUserDetail"/> rows via
    /// <see cref="SelectedDetails"/> when the user clicks ADD SELECTED.
    /// </summary>
    /// <remarks>
    /// Theme matches the rest of the app's modal dialogs (DriverPickerForm, SupeyDriverEditorForm):
    /// Grey900 form chrome, RGB(70,70,70) ListView body, RoyalBlue selection, Gainsboro labels.
    /// All controls are composed programmatically; the .Designer.cs sibling is empty save for the
    /// dispose pattern.
    /// </remarks>
    internal partial class SupeyImportFromWellRydeForm : MaterialForm
    {
        private static readonly Color FormBg = Color.FromArgb(33, 33, 33);
        private static readonly Color ListBg = Color.FromArgb(70, 70, 70);
        private static readonly Color ListSelected = Color.RoyalBlue;
        private static readonly Color ListAlreadyAdded = Color.FromArgb(58, 70, 58); // muted green tint
        private static readonly Color ListText = Color.White;
        private static readonly Color ListTextDim = Color.LightGray;
        // Owner-draw mode shortcuts the framework's GridLines rendering, so we paint cell
        // borders ourselves to match the legacy listview look the user expects.
        private static readonly Color ListGrid = Color.FromArgb(56, 56, 56);

        private readonly WellRydePortalSession _session;
        private readonly HashSet<string> _alreadyImportedSecIds;

        private MaterialButton _pullBtn;
        private MaterialButton _addSelectedBtn;
        // Tooltip explains the *why* of the disabled state — without it, the user just sees a
        // greyed button with no feedback when nothing is checked.
        private readonly ToolTip _addSelectedTip = new ToolTip { AutoPopDelay = 6000, InitialDelay = 250, ReshowDelay = 100 };
        private MaterialButton _cancelBtn;
        private MaterialButton _checkAllBtn;
        private MaterialButton _checkNoneBtn;
        private MaterialProgressBar _progressBar;
        private Label _headerLbl;
        private Label _statusLbl;
        private Label _summaryLbl;
        private ListView _driverList;
        private ColumnHeader _colCheck;
        private ColumnHeader _colName;
        private ColumnHeader _colUsername;
        private ColumnHeader _colAddress;
        private ColumnHeader _colCityState;
        private ColumnHeader _colRoles;
        private Label _emptyHint;

        private CancellationTokenSource _cts;
        private bool _pulling;

        /// <summary>
        /// Detail records the user explicitly checked when they hit ADD SELECTED. Empty (but not
        /// null) when the user cancels or hasn't clicked yet. Re-imports of already-known drivers
        /// are still included if the user re-checks them so the caller can decide whether to merge
        /// or skip.
        /// </summary>
        public IList<WellRydeUserDetail> SelectedDetails { get; private set; } = new List<WellRydeUserDetail>();

        public SupeyImportFromWellRydeForm(WellRydePortalSession session,
            IEnumerable<string> alreadyImportedSecIds)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _alreadyImportedSecIds = new HashSet<string>(alreadyImportedSecIds ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            // MaterialForm theme — matches every other dark dialog in the app. Try/catch so
            // theming failures never block the dialog (rare, but seen on first-launch corruption).
            try
            {
                var mgr = MaterialSkinManager.Instance;
                mgr.AddFormToManage(this);
                mgr.Theme = MaterialSkinManager.Themes.DARK;
                mgr.ColorScheme = new ColorScheme(Primary.Grey900, Primary.Grey800, Primary.BlueGrey500,
                    Accent.Lime700, TextShade.WHITE);
            }
            catch { }

            BuildUi();
            UpdateButtonStates();
        }

        private void BuildUi()
        {
            Text = "Pull drivers from WellRyde";
            ClientSize = new Size(960, 600);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            MinimizeBox = false;
            MinimumSize = new Size(720, 480);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = FormBg;

            _headerLbl = new Label
            {
                Text = "Pull active drivers from the WellRyde portal",
                Location = new Point(20, 78),
                AutoSize = false,
                Size = new Size(900, 24),
                ForeColor = Color.Gainsboro,
                Font = new Font("Segoe UI Semibold", 11f),
                BackColor = Color.Transparent,
            };

            _statusLbl = new Label
            {
                Text = "Click PULL FROM WELLRYDE to fetch the active user roster.",
                Location = new Point(20, 104),
                AutoSize = false,
                Size = new Size(900, 22),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                ForeColor = Color.Silver,
                Font = new Font("Segoe UI", 9f),
                BackColor = Color.Transparent,
            };

            _progressBar = new MaterialProgressBar
            {
                Location = new Point(20, 132),
                Size = new Size(900, 8),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Style = ProgressBarStyle.Continuous,
                Visible = false,
            };

            _pullBtn = MakeButton("PULL FROM WELLRYDE", new Point(20, 150), new Size(196, 36), useAccent: true);
            _pullBtn.Click += async (s, e) => await OnPullClickedAsync();

            _checkAllBtn = MakeButton("CHECK ALL", new Point(228, 150), new Size(110, 36), useAccent: false);
            _checkAllBtn.Type = MaterialButton.MaterialButtonType.Outlined;
            _checkAllBtn.NoAccentTextColor = Color.Gainsboro;
            _checkAllBtn.Click += (s, e) => SetAllChecked(true);

            _checkNoneBtn = MakeButton("CLEAR", new Point(346, 150), new Size(96, 36), useAccent: false);
            _checkNoneBtn.Type = MaterialButton.MaterialButtonType.Outlined;
            _checkNoneBtn.NoAccentTextColor = Color.Gainsboro;
            _checkNoneBtn.Click += (s, e) => SetAllChecked(false);

            _summaryLbl = new Label
            {
                Location = new Point(456, 158),
                AutoSize = false,
                Size = new Size(490, 22),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                ForeColor = Color.Silver,
                Font = new Font("Segoe UI", 9f),
                BackColor = Color.Transparent,
                Text = "",
                TextAlign = ContentAlignment.MiddleRight,
            };

            // ---- ListView ----
            _driverList = new ListView
            {
                Location = new Point(20, 200),
                Size = new Size(920, 340),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                BackColor = ListBg,
                ForeColor = Color.Gainsboro,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Archivo Medium", 9.75f),
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                MultiSelect = false,
                CheckBoxes = true,
                View = View.Details,
                OwnerDraw = true,
                UseCompatibleStateImageBehavior = false,
            };
            _colCheck = new ColumnHeader { Text = "Use", Width = 50 };
            _colName = new ColumnHeader { Text = "Driver", Width = 200 };
            _colUsername = new ColumnHeader { Text = "Username", Width = 120 };
            _colAddress = new ColumnHeader { Text = "Address", Width = 220 };
            _colCityState = new ColumnHeader { Text = "City, ST", Width = 140 };
            _colRoles = new ColumnHeader { Text = "Roles", Width = 170 };
            _driverList.Columns.AddRange(new[] { _colCheck, _colName, _colUsername, _colAddress, _colCityState, _colRoles });
            _driverList.DrawColumnHeader += OnDrawColumnHeader;
            _driverList.DrawItem += OnDrawItem;
            _driverList.DrawSubItem += OnDrawSubItem;
            _driverList.ItemChecked += (s, e) => UpdateButtonStates();

            // Empty-state label that overlays the ListView until a pull has run.
            _emptyHint = new Label
            {
                Location = _driverList.Location,
                Size = _driverList.Size,
                Anchor = _driverList.Anchor,
                BackColor = ListBg,
                ForeColor = Color.Silver,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 10.5f),
                Text = "No drivers loaded yet.\nClick PULL FROM WELLRYDE above to fetch the active driver list.",
            };

            // ---- Bottom buttons ----
            _cancelBtn = MakeButton("CANCEL", new Point(720, 552), new Size(96, 36), useAccent: false);
            _cancelBtn.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _cancelBtn.Type = MaterialButton.MaterialButtonType.Text;
            _cancelBtn.NoAccentTextColor = Color.Gainsboro;
            _cancelBtn.Click += (s, e) => OnCancelClicked();

            _addSelectedBtn = new DarkOnAccentMaterialButton
            {
                Text = "ADD SELECTED",
                Location = new Point(820, 552),
                AutoSize = false,
                Size = new Size(120, 36),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                Type = MaterialButton.MaterialButtonType.Contained,
                Density = MaterialButton.MaterialButtonDensity.Default,
                UseAccentColor = true,
            };
            _addSelectedBtn.Click += (s, e) => OnAddSelectedClicked();

            Controls.Add(_headerLbl);
            Controls.Add(_statusLbl);
            Controls.Add(_progressBar);
            Controls.Add(_pullBtn);
            Controls.Add(_checkAllBtn);
            Controls.Add(_checkNoneBtn);
            Controls.Add(_summaryLbl);
            Controls.Add(_emptyHint);
            Controls.Add(_driverList);
            Controls.Add(_cancelBtn);
            Controls.Add(_addSelectedBtn);
            _emptyHint.BringToFront();

            // Same custom-listview treatment as the rest of the app: click-to-sort, dynamic
            // column min widths, dark empty-area header painter.
            try
            {
                ListViewSorter.Attach(_driverList);
                ListViewMinWidthEnforcer.Attach(_driverList);
                ListViewHeaderEmptyAreaPainter.Attach(_driverList);
            }
            catch { }

            FormClosing += (s, e) =>
            {
                // Cancel any in-flight import so the dialog closes promptly.
                try { _cts?.Cancel(); } catch { }
            };
        }

        private static MaterialButton MakeButton(string text, Point location, Size size, bool useAccent)
        {
            // Accent (green) buttons go through DarkOnAccentMaterialButton so their label is
            // painted dark + bold for contrast against MaterialSkin's bright Lime700 fill, the
            // same treatment ADD SELECTED uses. Non-accent buttons get the stock material look.
            if (useAccent)
            {
                return new DarkOnAccentMaterialButton
                {
                    Text = text,
                    Location = location,
                    AutoSize = false,
                    Size = size,
                    Type = MaterialButton.MaterialButtonType.Contained,
                    Density = MaterialButton.MaterialButtonDensity.Default,
                    UseAccentColor = true,
                    HighEmphasis = true,
                };
            }
            return new MaterialButton
            {
                Text = text,
                Location = location,
                AutoSize = false,
                Size = size,
                Type = MaterialButton.MaterialButtonType.Contained,
                Density = MaterialButton.MaterialButtonDensity.Default,
                UseAccentColor = false,
                HighEmphasis = true,
            };
        }

        // ----------------------------------------------------------------------
        // Pull
        // ----------------------------------------------------------------------

        private async Task OnPullClickedAsync()
        {
            if (_pulling) return;
            _pulling = true;
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            UpdateButtonStates();

            _progressBar.Visible = true;
            _progressBar.Style = ProgressBarStyle.Marquee;
            _statusLbl.Text = "Connecting to WellRyde...";
            _emptyHint.Visible = false;
            _driverList.Items.Clear();

            var importer = new WellRydeRosterImporter(_session);
            var progress = new Progress<WellRydeRosterImportProgress>(p => OnProgressUpdate(p));

            WellRydeRosterImportResult result;
            try
            {
                result = await importer.ImportEligibleDriversAsync(progress, _cts.Token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                _statusLbl.Text = "Import cancelled.";
                _progressBar.Visible = false;
                _pulling = false;
                _emptyHint.Visible = _driverList.Items.Count == 0;
                UpdateButtonStates();
                return;
            }
            catch (Exception ex)
            {
                _statusLbl.Text = "Import failed: " + (ex.Message ?? "unknown error");
                _progressBar.Visible = false;
                _pulling = false;
                _emptyHint.Visible = _driverList.Items.Count == 0;
                UpdateButtonStates();
                return;
            }

            _progressBar.Visible = false;

            if (result.ListLoadFailed)
            {
                _statusLbl.Text = "Could not load user list: " + (result.ListErrorMessage ?? "unknown error") +
                    " Try signing in again.";
                _emptyHint.Visible = true;
                _pulling = false;
                UpdateButtonStates();
                return;
            }

            PopulateDriverList(result);
            _pulling = false;
            UpdateButtonStates();
        }

        private void OnProgressUpdate(WellRydeRosterImportProgress p)
        {
            if (p == null) return;
            _statusLbl.Text = p.Message ?? "";
            if (p.Total > 0)
            {
                _progressBar.Style = ProgressBarStyle.Continuous;
                _progressBar.Maximum = p.Total;
                _progressBar.Value = Math.Min(p.Completed, p.Total);
            }
            else
            {
                _progressBar.Style = ProgressBarStyle.Marquee;
            }
        }

        private void PopulateDriverList(WellRydeRosterImportResult result)
        {
            _driverList.BeginUpdate();
            try
            {
                _driverList.Items.Clear();
                if (result.Details.Count == 0)
                {
                    _statusLbl.Text = "No active drivers found in WellRyde (scanned " + result.TotalUsersScanned + " users).";
                    _emptyHint.Text = "WellRyde returned no enabled, unlocked users.\nNothing to import.";
                    _emptyHint.Visible = true;
                    _summaryLbl.Text = "";
                    return;
                }

                int alreadyCount = 0;
                foreach (var d in result.Details.OrderBy(x => x.FullName, StringComparer.OrdinalIgnoreCase))
                {
                    bool already = !string.IsNullOrEmpty(d.SecId) && _alreadyImportedSecIds.Contains(d.SecId);
                    if (already) alreadyCount++;

                    string cityState = (d.City ?? "").Trim();
                    string state = (d.State ?? "").Trim();
                    if (cityState.Length > 0 && state.Length > 0) cityState += ", " + state;
                    else if (state.Length > 0) cityState = state;
                    string zip = (d.Zip ?? "").Trim();
                    if (zip.Length > 0) cityState += " " + zip;

                    string street = (d.FullStreet ?? "").Trim();
                    if (street.Length == 0) street = "(no address)";

                    string roles = d.Roles != null && d.Roles.Count > 0
                        ? string.Join(", ", d.Roles.Take(4)) + (d.Roles.Count > 4 ? "..." : "")
                        : "";

                    var item = new ListViewItem(new[]
                    {
                        "",
                        d.FullName ?? "",
                        d.Username ?? "",
                        street,
                        cityState,
                        roles,
                    });
                    item.Tag = d;
                    // Pre-check users not already in roster + with a usable street address; users
                    // already imported get unchecked so re-pulls don't accidentally re-add them.
                    item.Checked = !already && !string.IsNullOrWhiteSpace(d.FullStreet);
                    _driverList.Items.Add(item);
                }

                _summaryLbl.Text = result.Details.Count + " active driver" +
                    (result.Details.Count == 1 ? "" : "s") + " found" +
                    (alreadyCount > 0 ? " (" + alreadyCount + " already in roster)" : "") + ".";
                _statusLbl.Text = "Check the drivers you want to add, then click ADD SELECTED.";
                _emptyHint.Visible = false;
            }
            finally
            {
                _driverList.EndUpdate();
            }
            // Auto-fit columns to the widest cell + header so long names / addresses aren't clipped.
            try { ListViewMinWidthEnforcer.Recompute(_driverList); } catch { }
        }

        private void SetAllChecked(bool value)
        {
            _driverList.BeginUpdate();
            try
            {
                foreach (ListViewItem item in _driverList.Items)
                {
                    if (item != null) item.Checked = value;
                }
            }
            finally
            {
                _driverList.EndUpdate();
            }
        }

        private void UpdateButtonStates()
        {
            int checkedCount = 0;
            if (_driverList != null)
            {
                foreach (ListViewItem item in _driverList.Items)
                {
                    if (item != null && item.Checked) checkedCount++;
                }
            }
            if (_addSelectedBtn != null)
            {
                _addSelectedBtn.Enabled = checkedCount > 0 && !_pulling;
                string tip;
                if (_pulling)
                    tip = "Wait for the pull to finish before adding drivers.";
                else if (checkedCount == 0)
                    tip = "Check at least one driver above to enable this button.";
                else
                    tip = "Add the " + checkedCount + " checked driver" + (checkedCount == 1 ? "" : "s") + " to your roster.";
                _addSelectedTip.SetToolTip(_addSelectedBtn, tip);
            }
            if (_pullBtn != null) _pullBtn.Enabled = !_pulling;
            if (_checkAllBtn != null) _checkAllBtn.Enabled = _driverList != null && _driverList.Items.Count > 0 && !_pulling;
            if (_checkNoneBtn != null) _checkNoneBtn.Enabled = _checkAllBtn != null && _checkAllBtn.Enabled;
        }

        private void OnAddSelectedClicked()
        {
            var picks = new List<WellRydeUserDetail>();
            foreach (ListViewItem item in _driverList.Items)
            {
                if (item == null || !item.Checked) continue;
                var d = item.Tag as WellRydeUserDetail;
                if (d != null) picks.Add(d);
            }
            SelectedDetails = picks;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void OnCancelClicked()
        {
            try { _cts?.Cancel(); } catch { }
            DialogResult = DialogResult.Cancel;
            Close();
        }

        // ----------------------------------------------------------------------
        // Owner-draw — same color rules as DriverPickerForm so the dialogs feel like one app.
        // ----------------------------------------------------------------------

        private void OnDrawColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            // Match the trips preview / roster ListView header for visual consistency:
            // 51/51/51 background, Gainsboro Archivo Medium 11pt label.
            using (var bg = new SolidBrush(Color.FromArgb(51, 51, 51)))
            {
                e.Graphics.FillRectangle(bg, e.Bounds);
            }
            using (var fnt = new Font("Archivo Medium", 11f))
            {
                var bounds = new Rectangle(e.Bounds.Left + 6, e.Bounds.Top, e.Bounds.Width - 6, e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, e.Header.Text ?? "", fnt, bounds, Color.Gainsboro,
                    TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter);
            }
        }

        private void OnDrawItem(object sender, DrawListViewItemEventArgs e)
        {
            // The owner-draw item handler covers the whole row background; per-subitem fills are
            // handled in OnDrawSubItem so columns can override colors (e.g. the address column
            // turns dim red when blank).
            bool selected = e.Item != null && e.Item.Selected;
            bool already = ItemIsAlreadyImported(e.Item);

            Color bg = selected ? ListSelected : (already ? ListAlreadyAdded : ListBg);
            using (var brush = new SolidBrush(bg))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
            }
        }

        private void OnDrawSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            bool selected = e.Item != null && e.Item.Selected;
            bool already = ItemIsAlreadyImported(e.Item);
            bool noAddress = (e.SubItem == e.Item.SubItems[3]) && IsNoAddress(e.SubItem.Text);

            Color bg = selected ? ListSelected : (already ? ListAlreadyAdded : ListBg);
            using (var brush = new SolidBrush(bg))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
            }

            // Column 0 (Use) carries the standard checkbox glyph drawn by the framework when the
            // owner-draw item handler returns without painting it. We let the default checkbox
            // renderer handle that — but ListView's owner-draw mode requires us to manually call
            // it via DrawDefault for the first sub-item. To keep things simple we skip drawing
            // text for the first sub-item and let the system render the checkbox when DrawDefault
            // is set.
            if (e.ColumnIndex == 0)
            {
                e.DrawDefault = true;
                DrawCellGridLines(e.Graphics, e.Bounds);
                return;
            }

            Color fg;
            if (selected) fg = Color.White;
            else if (already) fg = Color.LightGreen;
            else if (noAddress) fg = Color.IndianRed;
            else fg = ListText;

            var bounds = new Rectangle(e.Bounds.Left + 8, e.Bounds.Top, e.Bounds.Width - 12, e.Bounds.Height);
            const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.SingleLine
                | TextFormatFlags.VerticalCenter | TextFormatFlags.WordEllipsis | TextFormatFlags.GlyphOverhangPadding;
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text ?? "", _driverList.Font, bounds, fg, flags);

            DrawCellGridLines(e.Graphics, e.Bounds);
        }

        /// <summary>
        /// Owner-draw bypasses <see cref="ListView.GridLines"/>; we restore the grid by hand so the
        /// dialog matches the rest of the suite's listviews.
        /// </summary>
        private static void DrawCellGridLines(System.Drawing.Graphics g, Rectangle bounds)
        {
            using (var pen = new Pen(ListGrid, 1f))
            {
                g.DrawLine(pen, bounds.Right - 1, bounds.Top, bounds.Right - 1, bounds.Bottom - 1);
                g.DrawLine(pen, bounds.Left, bounds.Bottom - 1, bounds.Right - 1, bounds.Bottom - 1);
            }
        }

        private bool ItemIsAlreadyImported(ListViewItem item)
        {
            if (item == null) return false;
            var d = item.Tag as WellRydeUserDetail;
            if (d == null) return false;
            return !string.IsNullOrEmpty(d.SecId) && _alreadyImportedSecIds.Contains(d.SecId);
        }

        private static bool IsNoAddress(string text)
        {
            return string.Equals((text ?? "").Trim(), "(no address)", StringComparison.OrdinalIgnoreCase);
        }
    }
}
