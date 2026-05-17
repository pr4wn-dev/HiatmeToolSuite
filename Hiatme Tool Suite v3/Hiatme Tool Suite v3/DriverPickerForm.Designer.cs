namespace Hiatme_Tool_Suite_v3
{
    partial class DriverPickerForm
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
            this._tripsLabel = new System.Windows.Forms.Label();
            this._searchBox = new MaterialSkin.Controls.MaterialTextBox2();
            this._driverList = new System.Windows.Forms.ListView();
            this._driverColName = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this._countLabel = new System.Windows.Forms.Label();
            this._okButton = new Hiatme_Tool_Suite_v3.DarkOnAccentMaterialButton();
            this._cancelButton = new MaterialSkin.Controls.MaterialButton();
            this.SuspendLayout();
            // 
            // _headerLabel
            // 
            this._headerLabel.AutoSize = false;
            this._headerLabel.ForeColor = System.Drawing.Color.Gainsboro;
            this._headerLabel.Font = new System.Drawing.Font("Segoe UI Semibold", 11F);
            this._headerLabel.Location = new System.Drawing.Point(20, 78);
            this._headerLabel.Size = new System.Drawing.Size(440, 24);
            this._headerLabel.Text = "Pick a driver to assign to";
            // 
            // _tripsLabel
            // 
            this._tripsLabel.AutoSize = false;
            this._tripsLabel.ForeColor = System.Drawing.Color.Silver;
            this._tripsLabel.Font = new System.Drawing.Font("Segoe UI", 9F);
            this._tripsLabel.Location = new System.Drawing.Point(20, 104);
            this._tripsLabel.Size = new System.Drawing.Size(440, 18);
            this._tripsLabel.Text = "";
            // 
            // _searchBox
            // 
            this._searchBox.AnimateReadOnly = false;
            this._searchBox.BackColor = System.Drawing.Color.White;
            this._searchBox.Depth = 0;
            this._searchBox.Font = new System.Drawing.Font("Roboto", 16F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Pixel);
            this._searchBox.Hint = "Filter drivers by name...";
            this._searchBox.Location = new System.Drawing.Point(20, 130);
            this._searchBox.MaxLength = 100;
            this._searchBox.MouseState = MaterialSkin.MouseState.OUT;
            this._searchBox.Name = "_searchBox";
            this._searchBox.Size = new System.Drawing.Size(440, 50);
            this._searchBox.TabIndex = 0;
            this._searchBox.UseAccent = false;
            this._searchBox.UseTallSize = false;
            this._searchBox.TextChanged += new System.EventHandler(this.OnSearchChanged);
            this._searchBox.KeyDown += new System.Windows.Forms.KeyEventHandler(this.OnSearchKeyDown);
            // 
            // _driverList
            // 
            this._driverList.BackColor = System.Drawing.Color.FromArgb(70, 70, 70);
            this._driverList.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this._driverList.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] { this._driverColName });
            this._driverList.Font = new System.Drawing.Font("Archivo Medium", 10.25F, System.Drawing.FontStyle.Regular);
            this._driverList.ForeColor = System.Drawing.Color.Gainsboro;
            this._driverList.FullRowSelect = true;
            this._driverList.GridLines = false;
            this._driverList.HeaderStyle = System.Windows.Forms.ColumnHeaderStyle.None;
            this._driverList.HideSelection = false;
            this._driverList.Location = new System.Drawing.Point(20, 188);
            this._driverList.MultiSelect = false;
            this._driverList.Name = "_driverList";
            this._driverList.OwnerDraw = true;
            this._driverList.Size = new System.Drawing.Size(440, 320);
            this._driverList.TabIndex = 1;
            this._driverList.UseCompatibleStateImageBehavior = false;
            this._driverList.View = System.Windows.Forms.View.Details;
            this._driverList.DrawColumnHeader += new System.Windows.Forms.DrawListViewColumnHeaderEventHandler(this.OnDriverListDrawColumnHeader);
            this._driverList.DrawItem += new System.Windows.Forms.DrawListViewItemEventHandler(this.OnDriverListDrawItem);
            this._driverList.DrawSubItem += new System.Windows.Forms.DrawListViewSubItemEventHandler(this.OnDriverListDrawSubItem);
            this._driverList.DoubleClick += new System.EventHandler(this.OnDriverListDoubleClick);
            this._driverList.KeyDown += new System.Windows.Forms.KeyEventHandler(this.OnDriverListKeyDown);
            // 
            // _driverColName
            // 
            this._driverColName.Text = "Driver";
            this._driverColName.Width = 420;
            // 
            // _countLabel
            // 
            this._countLabel.AutoSize = false;
            this._countLabel.ForeColor = System.Drawing.Color.Silver;
            this._countLabel.Font = new System.Drawing.Font("Segoe UI", 8.5F);
            this._countLabel.Location = new System.Drawing.Point(20, 514);
            this._countLabel.Size = new System.Drawing.Size(280, 18);
            this._countLabel.Text = "";
            // 
            // _okButton
            // 
            this._okButton.AutoSize = false;
            this._okButton.Density = MaterialSkin.Controls.MaterialButton.MaterialButtonDensity.Default;
            this._okButton.Location = new System.Drawing.Point(360, 540);
            this._okButton.Size = new System.Drawing.Size(100, 36);
            this._okButton.Text = "ASSIGN";
            this._okButton.Type = MaterialSkin.Controls.MaterialButton.MaterialButtonType.Contained;
            this._okButton.UseAccentColor = true;
            this._okButton.Click += new System.EventHandler(this.OnOkClicked);
            // 
            // _cancelButton
            // 
            this._cancelButton.AutoSize = false;
            this._cancelButton.Density = MaterialSkin.Controls.MaterialButton.MaterialButtonDensity.Default;
            this._cancelButton.Location = new System.Drawing.Point(260, 540);
            this._cancelButton.Size = new System.Drawing.Size(96, 36);
            this._cancelButton.Text = "CANCEL";
            this._cancelButton.Type = MaterialSkin.Controls.MaterialButton.MaterialButtonType.Text;
            // Text-type MaterialButton defaults its label to the scheme's Primary (Grey900),
            // which is invisible on the dark form. Force a light color so it reads cleanly.
            this._cancelButton.UseAccentColor = false;
            this._cancelButton.NoAccentTextColor = System.Drawing.Color.Gainsboro;
            this._cancelButton.Click += new System.EventHandler(this.OnCancelClicked);
            // 
            // DriverPickerForm
            // 
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            this.ClientSize = new System.Drawing.Size(480, 595);
            this.Controls.Add(this._headerLabel);
            this.Controls.Add(this._tripsLabel);
            this.Controls.Add(this._searchBox);
            this.Controls.Add(this._driverList);
            this.Controls.Add(this._countLabel);
            this.Controls.Add(this._okButton);
            this.Controls.Add(this._cancelButton);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Name = "DriverPickerForm";
            this.Text = "Pick driver";
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private System.Windows.Forms.Label _headerLabel;
        private System.Windows.Forms.Label _tripsLabel;
        private MaterialSkin.Controls.MaterialTextBox2 _searchBox;
        private System.Windows.Forms.ListView _driverList;
        private System.Windows.Forms.ColumnHeader _driverColName;
        private System.Windows.Forms.Label _countLabel;
        private Hiatme_Tool_Suite_v3.DarkOnAccentMaterialButton _okButton;
        private MaterialSkin.Controls.MaterialButton _cancelButton;
    }
}
