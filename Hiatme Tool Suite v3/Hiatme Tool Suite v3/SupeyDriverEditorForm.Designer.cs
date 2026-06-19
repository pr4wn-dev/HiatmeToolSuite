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
            const int padX = 24;
            const int contentW = 432;
            const int labelH = 16;
            const int labelToField = 6;
            const int fieldH = 48;
            const int rowGap = 14;
            int rowStep = labelH + labelToField + fieldH + rowGap;

            this._headerLabel = new System.Windows.Forms.Label();
            this._helpLabel = new System.Windows.Forms.Label();
            this._portalSectionLabel = new System.Windows.Forms.Label();
            this._localSectionLabel = new System.Windows.Forms.Label();
            this._nameLabel = new System.Windows.Forms.Label();
            this._streetLabel = new System.Windows.Forms.Label();
            this._cityLabel = new System.Windows.Forms.Label();
            this._stateLabel = new System.Windows.Forms.Label();
            this._zipLabel = new System.Windows.Forms.Label();
            this._capacityLabel = new System.Windows.Forms.Label();
            this._vehicleLabel = new System.Windows.Forms.Label();
            this._shiftStartLabel = new System.Windows.Forms.Label();
            this._shiftEndLabel = new System.Windows.Forms.Label();
            this._emailLabel = new System.Windows.Forms.Label();
            this._nameTb = new MaterialSkin.Controls.MaterialTextBox2();
            this._emailTb = new MaterialSkin.Controls.MaterialTextBox2();
            this._streetTb = new MaterialSkin.Controls.MaterialTextBox2();
            this._cityTb = new MaterialSkin.Controls.MaterialTextBox2();
            this._stateTb = new MaterialSkin.Controls.MaterialTextBox2();
            this._zipTb = new MaterialSkin.Controls.MaterialTextBox2();
            this._capacityTb = new MaterialSkin.Controls.MaterialTextBox2();
            this._vehicleTb = new MaterialSkin.Controls.MaterialTextBox2();
            this._shiftStartTb = new MaterialSkin.Controls.MaterialTextBox2();
            this._shiftEndTb = new MaterialSkin.Controls.MaterialTextBox2();
            this._cancelButton = new MaterialSkin.Controls.MaterialButton();
            this._okButton = new Hiatme_Tool_Suite_v3.DarkOnAccentMaterialButton();
            this.SuspendLayout();

            int y = 76;
            ConfigHeader(this._headerLabel, "Driver profile", padX, y, contentW, 26);
            y += 30;

            ConfigHelp(this._helpLabel, padX, y, contentW, 34);
            y += 40;

            ConfigSection(this._portalSectionLabel, "From WellRyde", padX, y, contentW);
            y += 22;

            y = LayoutFieldRow(this._nameLabel, "Driver name", this._nameTb, "Driver name",
                padX, y, contentW, labelH, labelToField, fieldH);
            y += rowGap;

            y = LayoutFieldRow(this._emailLabel, "Email", this._emailTb, "driver@example.com",
                padX, y, contentW, labelH, labelToField, fieldH);
            y += rowGap;

            y = LayoutFieldRow(this._streetLabel, "Home street", this._streetTb, "123 Main St",
                padX, y, contentW, labelH, labelToField, fieldH);
            y += rowGap;

            y = LayoutCityStateZipRow(padX, y, labelH, labelToField, fieldH);
            y += rowGap + 6;

            ConfigSection(this._localSectionLabel, "Supey schedule (saved on this PC)", padX, y, contentW);
            y += 22;

            y = LayoutSplitRow(
                this._capacityLabel, "Capacity (passengers)", this._capacityTb, "4",
                this._vehicleLabel, "Vehicle label", this._vehicleTb, "Sedan / Van #5",
                padX, y, 200, 212, labelH, labelToField, fieldH, rowGap);
            y += rowGap;

            y = LayoutSplitRow(
                this._shiftStartLabel, "Shift start (HH:mm)", this._shiftStartTb, "06:00",
                this._shiftEndLabel, "Shift end (HH:mm)", this._shiftEndTb, "18:00",
                padX, y, 200, 212, labelH, labelToField, fieldH, rowGap);
            y += 28;

            this._cancelButton.AutoSize = false;
            this._cancelButton.Density = MaterialSkin.Controls.MaterialButton.MaterialButtonDensity.Default;
            this._cancelButton.Location = new System.Drawing.Point(padX + contentW - 200, y);
            this._cancelButton.Size = new System.Drawing.Size(96, 36);
            this._cancelButton.Text = "CANCEL";
            this._cancelButton.Type = MaterialSkin.Controls.MaterialButton.MaterialButtonType.Text;
            this._cancelButton.UseAccentColor = false;
            this._cancelButton.NoAccentTextColor = Hiatme_Tool_Suite_v3.SupeyTheme.TextPrimary;
            this._cancelButton.Click += new System.EventHandler(this.OnCancelClicked);

            this._okButton.AutoSize = false;
            this._okButton.Density = MaterialSkin.Controls.MaterialButton.MaterialButtonDensity.Default;
            this._okButton.Location = new System.Drawing.Point(padX + contentW - 100, y);
            this._okButton.Size = new System.Drawing.Size(100, 36);
            this._okButton.Text = "SAVE";
            this._okButton.Type = MaterialSkin.Controls.MaterialButton.MaterialButtonType.Contained;
            this._okButton.UseAccentColor = true;
            this._okButton.Click += new System.EventHandler(this.OnOkClicked);

            int formH = y + 52;
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            this.ClientSize = new System.Drawing.Size(480, formH);
            this.Controls.Add(this._headerLabel);
            this.Controls.Add(this._helpLabel);
            this.Controls.Add(this._portalSectionLabel);
            this.Controls.Add(this._nameLabel);
            this.Controls.Add(this._nameTb);
            this.Controls.Add(this._emailLabel);
            this.Controls.Add(this._emailTb);
            this.Controls.Add(this._streetLabel);
            this.Controls.Add(this._streetTb);
            this.Controls.Add(this._cityLabel);
            this.Controls.Add(this._cityTb);
            this.Controls.Add(this._stateLabel);
            this.Controls.Add(this._stateTb);
            this.Controls.Add(this._zipLabel);
            this.Controls.Add(this._zipTb);
            this.Controls.Add(this._localSectionLabel);
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
            this.Padding = new System.Windows.Forms.Padding(0, 0, 0, 8);
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Name = "SupeyDriverEditorForm";
            this.Text = "Driver";
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private static void ConfigHeader(System.Windows.Forms.Label l, string text, int x, int y, int w, int h)
        {
            l.AutoSize = false;
            l.ForeColor = Hiatme_Tool_Suite_v3.SupeyTheme.TextPrimary;
            l.Font = new System.Drawing.Font("Segoe UI Semibold", 12F);
            l.Location = new System.Drawing.Point(x, y);
            l.Size = new System.Drawing.Size(w, h);
            l.Text = text;
        }

        private static void ConfigHelp(System.Windows.Forms.Label l, int x, int y, int w, int h)
        {
            l.AutoSize = false;
            l.ForeColor = System.Drawing.Color.DimGray;
            l.Font = new System.Drawing.Font("Segoe UI", 8.75F);
            l.Location = new System.Drawing.Point(x, y);
            l.Size = new System.Drawing.Size(w, h);
            l.Text = "Times use 24-hour format (e.g. 06:00 / 18:00). Capacity is total seated riders.";
        }

        private static void ConfigSection(System.Windows.Forms.Label l, string text, int x, int y, int w)
        {
            l.AutoSize = false;
            l.ForeColor = System.Drawing.Color.FromArgb(140, 200, 120);
            l.Font = new System.Drawing.Font("Segoe UI Semibold", 9F);
            l.Location = new System.Drawing.Point(x, y);
            l.Size = new System.Drawing.Size(w, 18);
            l.Text = text;
        }

        private static int LayoutFieldRow(
            System.Windows.Forms.Label label, string labelText,
            MaterialSkin.Controls.MaterialTextBox2 field, string hint,
            int x, int y, int width, int labelH, int labelToField, int fieldH)
        {
            ConfigLabel(label, labelText, x, y, width, labelH);
            int fieldY = y + labelH + labelToField;
            ConfigTextBox(field, hint, x, fieldY, width, fieldH);
            return fieldY + fieldH;
        }

        private int LayoutCityStateZipRow(int x, int y, int labelH, int labelToField, int fieldH)
        {
            const int cityW = 188;
            const int stateW = 72;
            const int zipW = 148;
            const int gap = 12;
            int stateX = x + cityW + gap;
            int zipX = stateX + stateW + gap;

            ConfigLabel(this._cityLabel, "City", x, y, cityW, labelH);
            ConfigLabel(this._stateLabel, "State", stateX, y, stateW, labelH);
            ConfigLabel(this._zipLabel, "ZIP", zipX, y, zipW, labelH);

            int fieldY = y + labelH + labelToField;
            ConfigTextBox(this._cityTb, "City", x, fieldY, cityW, fieldH);
            ConfigTextBox(this._stateTb, "State", stateX, fieldY, stateW, fieldH);
            ConfigTextBox(this._zipTb, "ZIP", zipX, fieldY, zipW, fieldH);
            return fieldY + fieldH;
        }

        private static int LayoutSplitRow(
            System.Windows.Forms.Label leftLabel, string leftLabelText,
            MaterialSkin.Controls.MaterialTextBox2 leftField, string leftHint,
            System.Windows.Forms.Label rightLabel, string rightLabelText,
            MaterialSkin.Controls.MaterialTextBox2 rightField, string rightHint,
            int x, int y, int leftW, int rightW, int labelH, int labelToField, int fieldH, int gap)
        {
            int rightX = x + leftW + gap;
            ConfigLabel(leftLabel, leftLabelText, x, y, leftW, labelH);
            ConfigLabel(rightLabel, rightLabelText, rightX, y, rightW, labelH);
            int fieldY = y + labelH + labelToField;
            ConfigTextBox(leftField, leftHint, x, fieldY, leftW, fieldH);
            ConfigTextBox(rightField, rightHint, rightX, fieldY, rightW, fieldH);
            return fieldY + fieldH;
        }

        private static void ConfigLabel(System.Windows.Forms.Label l, string text, int x, int y, int w, int h)
        {
            l.AutoSize = false;
            l.ForeColor = Hiatme_Tool_Suite_v3.SupeyTheme.TextSecondary;
            l.Font = new System.Drawing.Font("Segoe UI", 9F);
            l.Location = new System.Drawing.Point(x, y);
            l.Size = new System.Drawing.Size(w, h);
            l.Text = text;
        }

        private static void ConfigTextBox(MaterialSkin.Controls.MaterialTextBox2 tb,
            string hint, int x, int y, int width, int height)
        {
            tb.AnimateReadOnly = false;
            tb.BackColor = System.Drawing.Color.White;
            tb.Depth = 0;
            tb.Font = new System.Drawing.Font("Roboto", 16F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Pixel);
            tb.Hint = hint;
            tb.Location = new System.Drawing.Point(x, y);
            tb.MaxLength = 100;
            tb.MouseState = MaterialSkin.MouseState.OUT;
            tb.Size = new System.Drawing.Size(width, height);
            tb.UseAccent = false;
            tb.UseTallSize = false;
        }

        private System.Windows.Forms.Label _headerLabel;
        private System.Windows.Forms.Label _helpLabel;
        private System.Windows.Forms.Label _portalSectionLabel;
        private System.Windows.Forms.Label _localSectionLabel;
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
        private System.Windows.Forms.Label _emailLabel;
        private MaterialSkin.Controls.MaterialTextBox2 _emailTb;
        private MaterialSkin.Controls.MaterialButton _cancelButton;
        private Hiatme_Tool_Suite_v3.DarkOnAccentMaterialButton _okButton;
    }
}
