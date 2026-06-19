using System;
using System.Globalization;
using System.Windows.Forms;
using MaterialSkin;
using MaterialSkin.Controls;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Add / edit dialog for one <see cref="SupeyDriverProfile"/>. The caller passes either an
    /// existing profile (Edit) or null (Add); if the dialog returns <see cref="DialogResult.OK"/>
    /// the caller reads <see cref="Result"/> to get the populated profile back.
    /// </summary>
    /// <remarks>
    /// Validation is intentionally lenient — only Name and Capacity are required. Empty home
    /// address fields just mean the algorithm can't score this driver and the build will warn.
    /// Same dark-theme button treatment as <see cref="DriverPickerForm"/>: light cancel text,
    /// dark text on the green Save button via <see cref="DarkOnAccentMaterialButton"/>.
    /// </remarks>
    internal partial class SupeyDriverEditorForm : MaterialForm
    {
        private readonly SupeyDriverProfile _existing;

        public SupeyDriverProfile Result { get; private set; }

        /// <summary>When true and driver has a WellRyde SEC id, save also POSTs to nuUpdateUser.</summary>
        public bool SaveToWellRyde { get; private set; }

        public SupeyDriverEditorForm(SupeyDriverProfile existing)
        {
            _existing = existing;
            InitializeComponent();

            try
            {
                var mgr = MaterialSkinManager.Instance;
                mgr.AddFormToManage(this);
                SupeyMaterialSkinBridge.ApplyTo(mgr);
            }
            catch
            {
                // Skinning is cosmetic.
            }

            if (existing == null)
            {
                Text = "Add driver";
                _headerLabel.Text = "Add a driver to the roster";
                _portalSectionLabel.Text = "Driver info";
                _localSectionLabel.Text = "Supey schedule";
                _helpLabel.Text =
                    "Add manually or use Pull from WellRyde to link a portal account. Times are 24-hour (06:00 / 18:00).";
                _capacityTb.Text = "4";
                _shiftStartTb.Text = "06:00";
                _shiftEndTb.Text = "18:00";
            }
            else
            {
                Text = "Edit driver - " + (existing.Name ?? "");
                _headerLabel.Text = "Edit " + (existing.Name ?? "");
                _nameTb.Text = existing.Name ?? "";
                _streetTb.Text = existing.HomeStreet ?? "";
                _cityTb.Text = existing.HomeCity ?? "";
                _stateTb.Text = existing.HomeState ?? "";
                _zipTb.Text = existing.HomeZip ?? "";
                _capacityTb.Text = existing.CapacityPassengers > 0
                    ? existing.CapacityPassengers.ToString(CultureInfo.InvariantCulture)
                    : "4";
                _vehicleTb.Text = existing.VehicleLabel ?? "";
                _shiftStartTb.Text = existing.ShiftStart ?? "06:00";
                _shiftEndTb.Text = existing.ShiftEnd ?? "18:00";
                _emailTb.Text = existing.Email ?? string.Empty;
                if (string.IsNullOrWhiteSpace(existing.WellRydeSecId))
                {
                    _portalSectionLabel.Text = "Driver info (local)";
                    _helpLabel.Text =
                        "Not linked to WellRyde — use Pull from WellRyde to sync name and home from the portal.";
                }
                else
                {
                    _helpLabel.Text =
                        "Name and home were loaded from WellRyde. Capacity and shift stay on this PC. Save updates the portal.";
                }
            }
        }

        private void OnOkClicked(object sender, EventArgs e)
        {
            string name = (_nameTb.Text ?? "").Trim();
            if (name.Length == 0)
            {
                MessageBox.Show(this, "Driver name is required.", "Validation",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _nameTb.Focus();
                return;
            }

            int capacity;
            string capRaw = (_capacityTb.Text ?? "").Trim();
            if (!int.TryParse(capRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out capacity)
                || capacity < 1 || capacity > 30)
            {
                MessageBox.Show(this, "Capacity must be a positive integer (1-30).", "Validation",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _capacityTb.Focus();
                return;
            }

            Result = new SupeyDriverProfile
            {
                Name = name,
                Email = (_emailTb.Text ?? "").Trim(),
                HomeStreet = (_streetTb.Text ?? "").Trim(),
                HomeCity = (_cityTb.Text ?? "").Trim(),
                HomeState = (_stateTb.Text ?? "").Trim(),
                HomeZip = (_zipTb.Text ?? "").Trim(),
                CapacityPassengers = capacity,
                VehicleLabel = (_vehicleTb.Text ?? "").Trim(),
                ShiftStart = (_shiftStartTb.Text ?? "").Trim(),
                ShiftEnd = (_shiftEndTb.Text ?? "").Trim(),
            };
            if (_existing != null)
            {
                Result.WellRydeSecId = _existing.WellRydeSecId ?? "";
                Result.WellRydeUsername = _existing.WellRydeUsername ?? "";
                Result.WellRydeSyncedAtUtc = _existing.WellRydeSyncedAtUtc;
                Result.ScheduleTabKey = _existing.ScheduleTabKey ?? "";
                // Push on OK for any edit; SEC id is resolved at push time if missing.
                SaveToWellRyde = true;
            }
            DialogResult = DialogResult.OK;
            Close();
        }

        private void OnCancelClicked(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }
}
