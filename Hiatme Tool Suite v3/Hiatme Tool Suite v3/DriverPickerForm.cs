using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using MaterialSkin;
using MaterialSkin.Controls;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Modal driver picker. Caller passes the full <see cref="WRDrivers"/> roster (typically from
    /// <c>WellRydePortalSession.GetAllDriversForTripAssignmentAsync</c>) plus a short context line
    /// describing what the user is about to do (e.g. "Assigning 3 trips"); the dialog filters the
    /// roster live as the user types and returns the selected driver via <see cref="SelectedDriver"/>.
    /// Returns <see cref="DialogResult.OK"/> when a driver is chosen, <see cref="DialogResult.Cancel"/> otherwise.
    /// </summary>
    internal partial class DriverPickerForm : MaterialForm
    {
        private readonly List<WRDrivers> _allDrivers;

        /// <summary>The driver selected when the dialog returns <see cref="DialogResult.OK"/>; otherwise null.</summary>
        public WRDrivers SelectedDriver { get; private set; }

        public DriverPickerForm(IEnumerable<WRDrivers> drivers, string contextLine)
        {
            InitializeComponent();

            try
            {
                var mgr = MaterialSkinManager.Instance;
                mgr.AddFormToManage(this);
                mgr.Theme = MaterialSkinManager.Themes.DARK;
                mgr.ColorScheme = new ColorScheme(Primary.Grey900, Primary.Grey800, Primary.BlueGrey500, Accent.Lime700, TextShade.WHITE);
            }
            catch
            {
                // Theming is cosmetic; never block the dialog if MaterialSkin trips up.
            }

            _allDrivers = new List<WRDrivers>();
            if (drivers != null)
            {
                foreach (var d in drivers)
                {
                    if (d == null) continue;
                    if (string.IsNullOrWhiteSpace(d.text)) continue;
                    if (string.IsNullOrWhiteSpace(d.value)) continue;
                    _allDrivers.Add(d);
                }
            }

            _tripsLabel.Text = contextLine ?? "";
            ListViewMinWidthEnforcer.Attach(_driverList);
            PopulateList(_allDrivers);
            UpdateOkEnabled();
        }

        private void OnSearchChanged(object sender, EventArgs e)
        {
            string q = (_searchBox.Text ?? "").Trim();
            if (q.Length == 0)
            {
                PopulateList(_allDrivers);
                return;
            }

            var matched = new List<WRDrivers>(_allDrivers.Count);
            foreach (var d in _allDrivers)
            {
                if (d.text != null && d.text.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    matched.Add(d);
            }
            PopulateList(matched);
        }

        /// <summary>Down-arrow in the search box jumps focus into the list so the user can pick without releasing the keyboard.</summary>
        private void OnSearchKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Down && _driverList.Items.Count > 0)
            {
                _driverList.Focus();
                _driverList.Items[0].Selected = true;
                _driverList.Items[0].Focused = true;
                _driverList.EnsureVisible(0);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Enter)
            {
                if (_driverList.Items.Count == 1)
                {
                    _driverList.Items[0].Selected = true;
                    AcceptSelection();
                }
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        }

        private void OnDriverListKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                AcceptSelection();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        }

        private void OnDriverListDoubleClick(object sender, EventArgs e)
        {
            AcceptSelection();
        }

        private void OnOkClicked(object sender, EventArgs e)
        {
            AcceptSelection();
        }

        private void OnCancelClicked(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private void AcceptSelection()
        {
            if (_driverList.SelectedItems.Count == 0) return;
            var picked = _driverList.SelectedItems[0].Tag as WRDrivers;
            if (picked == null) return;
            SelectedDriver = picked;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void PopulateList(List<WRDrivers> drivers)
        {
            _driverList.BeginUpdate();
            try
            {
                _driverList.Items.Clear();
                foreach (var d in drivers)
                {
                    var item = new ListViewItem(d.text ?? "")
                    {
                        Tag = d,
                    };
                    _driverList.Items.Add(item);
                }
                if (_driverList.Items.Count > 0)
                {
                    _driverList.Items[0].Selected = true;
                }
            }
            finally
            {
                _driverList.EndUpdate();
            }

            _countLabel.Text = drivers.Count == _allDrivers.Count
                ? drivers.Count + " drivers"
                : drivers.Count + " of " + _allDrivers.Count + " drivers";
            ListViewMinWidthEnforcer.ScheduleRecompute(_driverList);
            UpdateOkEnabled();
        }

        private void UpdateOkEnabled()
        {
            _okButton.Enabled = _driverList.Items.Count > 0;
        }

        // Owner-draw plumbing — matches the main trip listviews: RGB(70,70,70) body, white text,
        // RoyalBlue selection highlight (instead of the default white "selected" stripe).

        private static readonly Color ListBackground = Color.FromArgb(70, 70, 70);
        private static readonly Color ListSelected = Color.RoyalBlue;
        private static readonly Color ListText = Color.White;

        private void OnDriverListDrawColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            // Header is hidden (HeaderStyle.None) but DrawColumnHeader still fires once at startup.
            e.DrawDefault = false;
            using (var brush = new SolidBrush(ListBackground))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
            }
        }

        private void OnDriverListDrawItem(object sender, DrawListViewItemEventArgs e)
        {
            // e.Item.Selected is the per-row truth — e.State's Selected bit can read inconsistently
            // for non-focused items in Details view, which previously made every row paint blue.
            bool selected = e.Item != null && e.Item.Selected;
            using (var bg = new SolidBrush(selected ? ListSelected : ListBackground))
            {
                e.Graphics.FillRectangle(bg, e.Bounds);
            }
            // No focus/selection border — the RoyalBlue fill on the active row is the sole
            // indicator. A 1px border was leaving stale pixels between selection changes.
        }

        private void OnDriverListDrawSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            // Re-fill the subitem too so anything DrawItem missed (or that came in dirty) gets
            // overwritten before we paint text on top.
            bool selected = e.Item != null && e.Item.Selected;
            using (var bg = new SolidBrush(selected ? ListSelected : ListBackground))
            {
                e.Graphics.FillRectangle(bg, e.Bounds);
            }

            var bounds = new Rectangle(e.Bounds.Left + 8, e.Bounds.Top, e.Bounds.Width - 8, e.Bounds.Height);
            const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.SingleLine
                | TextFormatFlags.VerticalCenter | TextFormatFlags.WordEllipsis | TextFormatFlags.GlyphOverhangPadding;
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text ?? "", _driverList.Font, bounds, ListText, flags);
        }
    }
}
