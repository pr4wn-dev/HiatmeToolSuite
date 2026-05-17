namespace Hiatme_Tool_Suite_v3
{
    partial class SupeyDriverEditorForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this._headerLabel = new System.Windows.Forms.Label();
            this._nameLabel = new System.Windows.Forms.Label();
            this._streetLabel = new System.Windows.Forms.Label();
            this._cityLabel = new System.Windows.Forms.Label();
            this._stateLabel = new System.Windows.Forms.Label();
            this._zipLabel = new System.Windows.Forms.Label();
            this._capacityLabel = new System.Windows.Forms.Label();
            this._vehicleLabel = new System.Windows.Forms.Label();
            this._shiftStartLabel = new System.Windows.Forms.Label();
            this._shiftEndLabel = new System.Windows.Forms.Label();
            this._nameTb = new MaterialSkin.Controls.MaterialTextBox2();
            this._streetTb = new MaterialSkin.Controls.MaterialTextBox2();
            this._cityTb = new MaterialSkin.Controls.MaterialTextBox2();
            this._stateTb = new MaterialSkin.Controls.MaterialTextBox2();
            this._zipTb = new MaterialSkin.Controls.MaterialTextBox2();
            this._capacityTb = new MaterialSkin.Controls.MaterialTextBox2();
            this._vehicleTb = new MaterialSkin.Controls.MaterialTextBox2();
            this._shiftStartTb = new MaterialSkin.Controls.MaterialTextBox2();
            this._shiftEndTb = new MaterialSkin.Controls.MaterialTextBox2();
            this._helpLabel = new System.Windows.Forms.Label();
            this._cancelButton = new MaterialSkin.Controls.MaterialButton();
            this._okButton = new Hiatme_Tool_Suite_v3.DarkOnAccentMaterialButton();
            this.SuspendLayout();
            // 
            // _headerLabel
            // 
            this._headerLabel.AutoSize = false;
            this._headerLabel.ForeColor = System.Drawing.Color.Gainsboro;
            this._headerLabel.Font = new System.Drawing.Font("Segoe UI Semibold", 12F);
            this._headerLabel.Location = new System.Drawing.Point(20, 78);
            this._headerLabel.Size = new System.Drawing.Size(440, 24);
            this._headerLabel.Text = "Driver profile";
            // 
            // _helpLabel
            // 
            this._helpLabel.AutoSize = false;
            this._helpLabel.ForeColor = System.Drawing.Color.Silver;
            this._helpLabel.Font = new System.Drawing.Font("Segoe UI", 8.5F);
            this._helpLabel.Location = new System.Drawing.Point(20, 104);
            this._helpLabel.Size = new System.Drawing.Size(440, 18);
            this._helpLabel.Text = "Times use 24-hour format (e.g. 06:00 / 18:00). Capacity is total seated riders.";
            //
            // labels - same style for all
            //
            ConfigLabel(this._nameLabel, "Driver name", 130);
            ConfigLabel(this._streetLabel, "Home street", 178);
            ConfigLabel(this._cityLabel, "City", 226);
            ConfigLabel(this._stateLabel, "State", 226);
            ConfigLabel(this._zipLabel, "ZIP", 226);
            ConfigLabel(this._capacityLabel, "Capacity (passengers)", 274);
            ConfigLabel(this._vehicleLabel, "Vehicle label (cosmetic)", 274);
            ConfigLabel(this._shiftStartLabel, "Shift start (HH:mm)", 322);
            ConfigLabel(this._shiftEndLabel, "Shift end (HH:mm)", 322);
            this._cityLabel.Location = new System.Drawing.Point(20, 226);
            this._cityLabel.Size = new System.Drawing.Size(180, 18);
            this._stateLabel.Location = new System.Drawing.Point(220, 226);
            this._stateLabel.Size = new System.Drawing.Size(80, 18);
            this._zipLabel.Location = new System.Drawing.Point(320, 226);
            this._zipLabel.Size = new System.Drawing.Size(140, 18);
            this._vehicleLabel.Location = new System.Drawing.Point(240, 274);
            this._vehicleLabel.Size = new System.Drawing.Size(220, 18);
            this._capacityLabel.Size = new System.Drawing.Size(220, 18);
            this._shiftEndLabel.Location = new System.Drawing.Point(240, 322);
            this._shiftEndLabel.Size = new System.Drawing.Size(220, 18);
            this._shiftStartLabel.Size = new System.Drawing.Size(220, 18);
            //
            // textboxes
            //
            ConfigTextBox(this._nameTb, "Driver name", 20, 148, 440);
            ConfigTextBox(this._streetTb, "123 Main St", 20, 196, 440);
            ConfigTextBox(this._cityTb, "City", 20, 244, 180);
            ConfigTextBox(this._stateTb, "OH", 220, 244, 80);
            ConfigTextBox(this._zipTb, "ZIP", 320, 244, 140);
            ConfigTextBox(this._capacityTb, "4", 20, 292, 220);
            ConfigTextBox(this._vehicleTb, "Sedan / Van #5", 240, 292, 220);
            ConfigTextBox(this._shiftStartTb, "06:00", 20, 340, 220);
            ConfigTextBox(this._shiftEndTb, "18:00", 240, 340, 220);
            // 
            // _cancelButton
            // 
            this._cancelButton.AutoSize = false;
            this._cancelButton.Density = MaterialSkin.Controls.MaterialButton.MaterialButtonDensity.Default;
            this._cancelButton.Location = new System.Drawing.Point(260, 396);
            this._cancelButton.Size = new System.Drawing.Size(96, 36);
            this._cancelButton.Text = "CANCEL";
            this._cancelButton.Type = MaterialSkin.Controls.MaterialButton.MaterialButtonType.Text;
            this._cancelButton.UseAccentColor = false;
            this._cancelButton.NoAccentTextColor = System.Drawing.Color.Gainsboro;
            this._cancelButton.Click += new System.EventHandler(this.OnCancelClicked);
            // 
            // _okButton
            // 
            this._okButton.AutoSize = false;
            this._okButton.Density = MaterialSkin.Controls.MaterialButton.MaterialButtonDensity.Default;
            this._okButton.Location = new System.Drawing.Point(360, 396);
            this._okButton.Size = new System.Drawing.Size(100, 36);
            this._okButton.Text = "SAVE";
            this._okButton.Type = MaterialSkin.Controls.MaterialButton.MaterialButtonType.Contained;
            this._okButton.UseAccentColor = true;
            this._okButton.Click += new System.EventHandler(this.OnOkClicked);
            // 
            // SupeyDriverEditorForm
            // 
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            this.ClientSize = new System.Drawing.Size(480, 450);
            this.Controls.Add(this._headerLabel);
            this.Controls.Add(this._helpLabel);
            this.Controls.Add(this._nameLabel);
            this.Controls.Add(this._nameTb);
            this.Controls.Add(this._streetLabel);
            this.Controls.Add(this._streetTb);
            this.Controls.Add(this._cityLabel);
            this.Controls.Add(this._cityTb);
            this.Controls.Add(this._stateLabel);
            this.Controls.Add(this._stateTb);
            this.Controls.Add(this._zipLabel);
            this.Controls.Add(this._zipTb);
            this.Controls.Add(this._capacityLabel);
            this.Controls.Add(this._capacityTb);
            this.Controls.Add(this._vehicleLabel);
            this.Controls.Add(this._vehicleTb);
            this.Controls.Add(this._shiftStartLabel);
            this.Controls.Add(this._shiftStartTb);
            this.Controls.Add(this._shiftEndLabel);
            this.Controls.Add(this._shiftEndTb);
            this.Controls.Add(this._cancelButton);
            this.Controls.Add(this._okButton);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Name = "SupeyDriverEditorForm";
            this.Text = "Driver";
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private static void ConfigLabel(System.Windows.Forms.Label l, string text, int y)
        {
            l.AutoSize = false;
            l.ForeColor = System.Drawing.Color.Silver;
            l.Font = new System.Drawing.Font("Segoe UI", 9F);
            l.Location = new System.Drawing.Point(20, y);
            l.Size = new System.Drawing.Size(440, 18);
            l.Text = text;
        }

        private static void ConfigTextBox(MaterialSkin.Controls.MaterialTextBox2 tb,
            string hint, int x, int y, int width)
        {
            tb.AnimateReadOnly = false;
            tb.BackColor = System.Drawing.Color.White;
            tb.Depth = 0;
            tb.Font = new System.Drawing.Font("Roboto", 16F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Pixel);
            tb.Hint = hint;
            tb.Location = new System.Drawing.Point(x, y);
            tb.MaxLength = 100;
            tb.MouseState = MaterialSkin.MouseState.OUT;
            tb.Size = new System.Drawing.Size(width, 50);
            tb.UseAccent = false;
            tb.UseTallSize = false;
        }

        private System.Windows.Forms.Label _headerLabel;
        private System.Windows.Forms.Label _helpLabel;
        private System.Windows.Forms.Label _nameLabel;
        private System.Windows.Forms.Label _streetLabel;
        private System.Windows.Forms.Label _cityLabel;
        private System.Windows.Forms.Label _stateLabel;
        private System.Windows.Forms.Label _zipLabel;
        private System.Windows.Forms.Label _capacityLabel;
        private System.Windows.Forms.Label _vehicleLabel;
        private System.Windows.Forms.Label _shiftStartLabel;
        private System.Windows.Forms.Label _shiftEndLabel;
        private MaterialSkin.Controls.MaterialTextBox2 _nameTb;
        private MaterialSkin.Controls.MaterialTextBox2 _streetTb;
        private MaterialSkin.Controls.MaterialTextBox2 _cityTb;
        private MaterialSkin.Controls.MaterialTextBox2 _stateTb;
        private MaterialSkin.Controls.MaterialTextBox2 _zipTb;
        private MaterialSkin.Controls.MaterialTextBox2 _capacityTb;
        private MaterialSkin.Controls.MaterialTextBox2 _vehicleTb;
        private MaterialSkin.Controls.MaterialTextBox2 _shiftStartTb;
        private MaterialSkin.Controls.MaterialTextBox2 _shiftEndTb;
        private MaterialSkin.Controls.MaterialButton _cancelButton;
        private Hiatme_Tool_Suite_v3.DarkOnAccentMaterialButton _okButton;
    }
}
