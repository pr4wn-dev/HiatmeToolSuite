namespace Update
{
    partial class Form1
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this._startupHintLabel = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // _startupHintLabel
            // 
            this._startupHintLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this._startupHintLabel.Font = new System.Drawing.Font("Segoe UI", 11F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this._startupHintLabel.ForeColor = System.Drawing.Color.Gainsboro;
            this._startupHintLabel.Location = new System.Drawing.Point(0, 0);
            this._startupHintLabel.Name = "_startupHintLabel";
            this._startupHintLabel.Padding = new System.Windows.Forms.Padding(28);
            this._startupHintLabel.Size = new System.Drawing.Size(800, 450);
            this._startupHintLabel.TabIndex = 0;
            this._startupHintLabel.Text = "Hiatme Tool Suite — Updater (Update.exe)\r\n\r\n" +
    "This program is normally launched automatically by the main app when a new version is downloaded.\r\n\r\n" +
    "If you ran it by hand, open \"Hiatme Tool Suite v3\" and use the\r\n" +
    "\"Check for updates\" link in the bottom-right of the main window instead.";
            this._startupHintLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // Form1
            // 
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(800, 450);
            this.Controls.Add(this._startupHintLabel);
            this.Name = "Form1";
            this.Text = "Hiatme — Updater (not the main app)";
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.Label _startupHintLabel;
    }
}

