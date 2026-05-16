namespace Hiatme_Tool_Suite_v3
{
    partial class UpdatePrompt
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
            this._versionLabel = new System.Windows.Forms.Label();
            this._sizeLabel = new System.Windows.Forms.Label();
            this._notesLabel = new System.Windows.Forms.Label();
            this._notesBox = new System.Windows.Forms.TextBox();
            this._progress = new System.Windows.Forms.ProgressBar();
            this._progressLabel = new System.Windows.Forms.Label();
            this._installButton = new MaterialSkin.Controls.MaterialButton();
            this._laterButton = new MaterialSkin.Controls.MaterialButton();
            this.SuspendLayout();
            //
            // _versionLabel
            //
            this._versionLabel.AutoSize = false;
            this._versionLabel.ForeColor = System.Drawing.Color.Gainsboro;
            this._versionLabel.Font = new System.Drawing.Font("Segoe UI Semibold", 11F);
            this._versionLabel.Location = new System.Drawing.Point(20, 78);
            this._versionLabel.Size = new System.Drawing.Size(540, 26);
            this._versionLabel.Text = "Current → New";
            //
            // _sizeLabel
            //
            this._sizeLabel.AutoSize = false;
            this._sizeLabel.ForeColor = System.Drawing.Color.Silver;
            this._sizeLabel.Font = new System.Drawing.Font("Segoe UI", 9F);
            this._sizeLabel.Location = new System.Drawing.Point(20, 106);
            this._sizeLabel.Size = new System.Drawing.Size(540, 18);
            //
            // _notesLabel
            //
            this._notesLabel.AutoSize = false;
            this._notesLabel.ForeColor = System.Drawing.Color.Gainsboro;
            this._notesLabel.Font = new System.Drawing.Font("Segoe UI Semibold", 10F);
            this._notesLabel.Location = new System.Drawing.Point(20, 132);
            this._notesLabel.Size = new System.Drawing.Size(540, 20);
            this._notesLabel.Text = "What's new";
            //
            // _notesBox
            //
            this._notesBox.Multiline = true;
            this._notesBox.ReadOnly = true;
            this._notesBox.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this._notesBox.BackColor = System.Drawing.Color.FromArgb(33, 33, 33);
            this._notesBox.ForeColor = System.Drawing.Color.Gainsboro;
            this._notesBox.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this._notesBox.Font = new System.Drawing.Font("Segoe UI", 9F);
            this._notesBox.Location = new System.Drawing.Point(20, 155);
            this._notesBox.Size = new System.Drawing.Size(540, 130);
            //
            // _progress
            //
            this._progress.Location = new System.Drawing.Point(20, 296);
            this._progress.Size = new System.Drawing.Size(540, 14);
            //
            // _progressLabel
            //
            this._progressLabel.AutoSize = false;
            this._progressLabel.ForeColor = System.Drawing.Color.Silver;
            this._progressLabel.Font = new System.Drawing.Font("Segoe UI", 8.5F);
            this._progressLabel.Location = new System.Drawing.Point(20, 314);
            this._progressLabel.Size = new System.Drawing.Size(540, 18);
            //
            // _installButton
            //
            this._installButton.AutoSize = false;
            this._installButton.Density = MaterialSkin.Controls.MaterialButton.MaterialButtonDensity.Default;
            this._installButton.Location = new System.Drawing.Point(380, 340);
            this._installButton.Size = new System.Drawing.Size(180, 36);
            this._installButton.Text = "INSTALL NOW";
            this._installButton.Type = MaterialSkin.Controls.MaterialButton.MaterialButtonType.Contained;
            this._installButton.UseAccentColor = true;
            this._installButton.Click += new System.EventHandler(this.OnInstallClicked);
            //
            // _laterButton
            //
            this._laterButton.AutoSize = false;
            this._laterButton.Density = MaterialSkin.Controls.MaterialButton.MaterialButtonDensity.Default;
            this._laterButton.Location = new System.Drawing.Point(260, 340);
            this._laterButton.Size = new System.Drawing.Size(110, 36);
            this._laterButton.Text = "LATER";
            this._laterButton.Type = MaterialSkin.Controls.MaterialButton.MaterialButtonType.Text;
            this._laterButton.Click += new System.EventHandler(this.OnLaterClicked);
            //
            // UpdatePrompt
            //
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            this.ClientSize = new System.Drawing.Size(580, 392);
            this.Controls.Add(this._versionLabel);
            this.Controls.Add(this._sizeLabel);
            this.Controls.Add(this._notesLabel);
            this.Controls.Add(this._notesBox);
            this.Controls.Add(this._progress);
            this.Controls.Add(this._progressLabel);
            this.Controls.Add(this._installButton);
            this.Controls.Add(this._laterButton);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Name = "UpdatePrompt";
            this.Text = "Update available";
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private System.Windows.Forms.Label _versionLabel;
        private System.Windows.Forms.Label _sizeLabel;
        private System.Windows.Forms.Label _notesLabel;
        private System.Windows.Forms.TextBox _notesBox;
        private System.Windows.Forms.ProgressBar _progress;
        private System.Windows.Forms.Label _progressLabel;
        private MaterialSkin.Controls.MaterialButton _installButton;
        private MaterialSkin.Controls.MaterialButton _laterButton;
    }
}
