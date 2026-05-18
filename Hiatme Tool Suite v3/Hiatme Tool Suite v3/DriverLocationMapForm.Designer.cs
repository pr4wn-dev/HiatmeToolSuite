namespace Hiatme_Tool_Suite_v3
{
    partial class DriverLocationMapForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_refreshTimer != null) _refreshTimer.Dispose();
                if (components != null) components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this._headerPanel = new System.Windows.Forms.Panel();
            this._statusDot = new System.Windows.Forms.Panel();
            this._driverNameLabel = new System.Windows.Forms.Label();
            this._statusLabel = new System.Windows.Forms.Label();
            this._diagnosticsPanel = new System.Windows.Forms.Panel();
            this._diagnosticsHeadlineLabel = new System.Windows.Forms.Label();
            this._diagnosticsBodyLabel = new System.Windows.Forms.Label();
            this._footerPanel = new System.Windows.Forms.Panel();
            this._lastReportedLabel = new System.Windows.Forms.Label();
            this._autoRefreshCheck = new System.Windows.Forms.CheckBox();
            this._refreshButton = new MaterialSkin.Controls.MaterialButton();
            this._closeButton = new MaterialSkin.Controls.MaterialButton();
            this._gmap = new GMap.NET.WindowsForms.GMapControl();
            this._tripsPanel = new System.Windows.Forms.Panel();
            this._tripsHeaderLabel = new System.Windows.Forms.Label();
            this._tripsCountLabel = new System.Windows.Forms.Label();
            this._tripsHorizonLabel = new System.Windows.Forms.Label();
            this._tripsHorizonCombo = new System.Windows.Forms.ComboBox();
            this._etaButton = new MaterialSkin.Controls.MaterialButton();
            this._etaStatusLabel = new System.Windows.Forms.Label();
            this._tripsFlow = new System.Windows.Forms.FlowLayoutPanel();
            this._tripsEmptyLabel = new System.Windows.Forms.Label();
            this._refreshTimer = new System.Windows.Forms.Timer(this.components);
            this._headerPanel.SuspendLayout();
            this._diagnosticsPanel.SuspendLayout();
            this._footerPanel.SuspendLayout();
            this._tripsPanel.SuspendLayout();
            this.SuspendLayout();
            // 
            // _headerPanel
            // 
            this._headerPanel.BackColor = System.Drawing.Color.FromArgb(50, 50, 50);
            this._headerPanel.Controls.Add(this._statusDot);
            this._headerPanel.Controls.Add(this._driverNameLabel);
            this._headerPanel.Controls.Add(this._statusLabel);
            this._headerPanel.Dock = System.Windows.Forms.DockStyle.Top;
            this._headerPanel.Location = new System.Drawing.Point(3, 64);
            this._headerPanel.Name = "_headerPanel";
            this._headerPanel.Size = new System.Drawing.Size(894, 60);
            this._headerPanel.TabIndex = 0;
            // 
            // _statusDot
            // 
            this._statusDot.BackColor = System.Drawing.Color.FromArgb(50, 50, 50);
            this._statusDot.Location = new System.Drawing.Point(16, 14);
            this._statusDot.Name = "_statusDot";
            this._statusDot.Size = new System.Drawing.Size(14, 14);
            this._statusDot.TabIndex = 0;
            this._statusDot.Paint += new System.Windows.Forms.PaintEventHandler(this.OnStatusDotPaint);
            // 
            // _driverNameLabel
            // 
            this._driverNameLabel.AutoSize = false;
            this._driverNameLabel.Font = new System.Drawing.Font("Segoe UI Semibold", 13F);
            this._driverNameLabel.ForeColor = System.Drawing.Color.Gainsboro;
            this._driverNameLabel.Location = new System.Drawing.Point(38, 8);
            this._driverNameLabel.Size = new System.Drawing.Size(840, 24);
            this._driverNameLabel.Text = "";
            this._driverNameLabel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top
                | System.Windows.Forms.AnchorStyles.Left
                | System.Windows.Forms.AnchorStyles.Right)));
            // 
            // _statusLabel
            // 
            this._statusLabel.AutoSize = false;
            this._statusLabel.Font = new System.Drawing.Font("Segoe UI", 9F);
            this._statusLabel.ForeColor = System.Drawing.Color.Silver;
            this._statusLabel.Location = new System.Drawing.Point(38, 34);
            this._statusLabel.Size = new System.Drawing.Size(840, 20);
            this._statusLabel.Text = "";
            this._statusLabel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top
                | System.Windows.Forms.AnchorStyles.Left
                | System.Windows.Forms.AnchorStyles.Right)));
            // 
            // _diagnosticsPanel
            // 
            this._diagnosticsPanel.BackColor = System.Drawing.Color.FromArgb(45, 45, 45);
            this._diagnosticsPanel.Controls.Add(this._diagnosticsHeadlineLabel);
            this._diagnosticsPanel.Controls.Add(this._diagnosticsBodyLabel);
            this._diagnosticsPanel.Dock = System.Windows.Forms.DockStyle.Top;
            this._diagnosticsPanel.Location = new System.Drawing.Point(3, 124);
            this._diagnosticsPanel.Name = "_diagnosticsPanel";
            this._diagnosticsPanel.Padding = new System.Windows.Forms.Padding(14, 8, 14, 8);
            this._diagnosticsPanel.Size = new System.Drawing.Size(894, 96);
            this._diagnosticsPanel.TabIndex = 4;
            // 
            // _diagnosticsHeadlineLabel
            // 
            this._diagnosticsHeadlineLabel.AutoSize = false;
            this._diagnosticsHeadlineLabel.Font = new System.Drawing.Font("Segoe UI Semibold", 9.5F);
            this._diagnosticsHeadlineLabel.ForeColor = System.Drawing.Color.Gainsboro;
            this._diagnosticsHeadlineLabel.Location = new System.Drawing.Point(14, 8);
            this._diagnosticsHeadlineLabel.Name = "_diagnosticsHeadlineLabel";
            this._diagnosticsHeadlineLabel.Size = new System.Drawing.Size(866, 20);
            this._diagnosticsHeadlineLabel.Text = "Connection status";
            this._diagnosticsHeadlineLabel.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top
                | System.Windows.Forms.AnchorStyles.Left)
                | System.Windows.Forms.AnchorStyles.Right)));
            // 
            // _diagnosticsBodyLabel
            // 
            this._diagnosticsBodyLabel.AutoSize = false;
            this._diagnosticsBodyLabel.Font = new System.Drawing.Font("Segoe UI", 8.75F);
            this._diagnosticsBodyLabel.ForeColor = System.Drawing.Color.Silver;
            this._diagnosticsBodyLabel.Location = new System.Drawing.Point(14, 28);
            this._diagnosticsBodyLabel.Name = "_diagnosticsBodyLabel";
            this._diagnosticsBodyLabel.Size = new System.Drawing.Size(866, 60);
            this._diagnosticsBodyLabel.Text = "";
            this._diagnosticsBodyLabel.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top
                | System.Windows.Forms.AnchorStyles.Bottom)
                | System.Windows.Forms.AnchorStyles.Left)
                | System.Windows.Forms.AnchorStyles.Right)));
            // 
            // _footerPanel
            // 
            this._footerPanel.BackColor = System.Drawing.Color.FromArgb(50, 50, 50);
            this._footerPanel.Controls.Add(this._lastReportedLabel);
            this._footerPanel.Controls.Add(this._autoRefreshCheck);
            this._footerPanel.Controls.Add(this._refreshButton);
            this._footerPanel.Controls.Add(this._closeButton);
            this._footerPanel.Dock = System.Windows.Forms.DockStyle.Bottom;
            this._footerPanel.Location = new System.Drawing.Point(3, 600);
            this._footerPanel.Name = "_footerPanel";
            this._footerPanel.Size = new System.Drawing.Size(894, 50);
            this._footerPanel.TabIndex = 2;
            // 
            // _lastReportedLabel
            // 
            this._lastReportedLabel.AutoSize = false;
            this._lastReportedLabel.Font = new System.Drawing.Font("Segoe UI", 9F);
            this._lastReportedLabel.ForeColor = System.Drawing.Color.Silver;
            this._lastReportedLabel.Location = new System.Drawing.Point(16, 16);
            this._lastReportedLabel.Size = new System.Drawing.Size(420, 20);
            this._lastReportedLabel.Text = "";
            this._lastReportedLabel.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom
                | System.Windows.Forms.AnchorStyles.Left)
                | System.Windows.Forms.AnchorStyles.Right)));
            // 
            // _autoRefreshCheck
            // 
            this._autoRefreshCheck.AutoSize = true;
            this._autoRefreshCheck.BackColor = System.Drawing.Color.FromArgb(50, 50, 50);
            this._autoRefreshCheck.Font = new System.Drawing.Font("Segoe UI", 9F);
            this._autoRefreshCheck.ForeColor = System.Drawing.Color.Gainsboro;
            this._autoRefreshCheck.Location = new System.Drawing.Point(556, 14);
            this._autoRefreshCheck.Name = "_autoRefreshCheck";
            this._autoRefreshCheck.Size = new System.Drawing.Size(140, 22);
            this._autoRefreshCheck.Text = "Auto-refresh (30s)";
            this._autoRefreshCheck.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom
                | System.Windows.Forms.AnchorStyles.Right)));
            this._autoRefreshCheck.UseVisualStyleBackColor = false;
            this._autoRefreshCheck.Checked = true;
            this._autoRefreshCheck.CheckedChanged += new System.EventHandler(this.OnAutoRefreshChanged);
            // 
            // _refreshButton
            // 
            this._refreshButton.AutoSize = false;
            this._refreshButton.Density = MaterialSkin.Controls.MaterialButton.MaterialButtonDensity.Default;
            this._refreshButton.Location = new System.Drawing.Point(700, 8);
            this._refreshButton.Size = new System.Drawing.Size(96, 36);
            this._refreshButton.Text = "REFRESH";
            this._refreshButton.Type = MaterialSkin.Controls.MaterialButton.MaterialButtonType.Text;
            this._refreshButton.UseAccentColor = false;
            this._refreshButton.NoAccentTextColor = System.Drawing.Color.Gainsboro;
            this._refreshButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom
                | System.Windows.Forms.AnchorStyles.Right)));
            this._refreshButton.Click += new System.EventHandler(this.OnRefreshClicked);
            // 
            // _closeButton
            // 
            this._closeButton.AutoSize = false;
            this._closeButton.Density = MaterialSkin.Controls.MaterialButton.MaterialButtonDensity.Default;
            this._closeButton.Location = new System.Drawing.Point(800, 8);
            this._closeButton.Size = new System.Drawing.Size(80, 36);
            this._closeButton.Text = "CLOSE";
            this._closeButton.Type = MaterialSkin.Controls.MaterialButton.MaterialButtonType.Text;
            this._closeButton.UseAccentColor = false;
            this._closeButton.NoAccentTextColor = System.Drawing.Color.Gainsboro;
            this._closeButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom
                | System.Windows.Forms.AnchorStyles.Right)));
            this._closeButton.Click += new System.EventHandler(this.OnCloseClicked);
            // 
            // _gmap
            // 
            this._gmap.Bearing = 0F;
            this._gmap.CanDragMap = true;
            this._gmap.Dock = System.Windows.Forms.DockStyle.Fill;
            this._gmap.EmptyTileColor = System.Drawing.Color.FromArgb(50, 50, 50);
            this._gmap.GrayScaleMode = false;
            this._gmap.HelperLineOption = GMap.NET.WindowsForms.HelperLineOptions.DontShow;
            this._gmap.LevelsKeepInMemory = 5;
            this._gmap.Location = new System.Drawing.Point(3, 124);
            this._gmap.MarkersEnabled = true;
            this._gmap.MaxZoom = 18;
            this._gmap.MinZoom = 2;
            this._gmap.MouseWheelZoomEnabled = true;
            this._gmap.MouseWheelZoomType = GMap.NET.MouseWheelZoomType.MousePositionAndCenter;
            this._gmap.Name = "_gmap";
            this._gmap.NegativeMode = false;
            this._gmap.PolygonsEnabled = true;
            this._gmap.RetryLoadTile = 0;
            this._gmap.RoutesEnabled = true;
            this._gmap.ScaleMode = GMap.NET.WindowsForms.ScaleModes.Integer;
            this._gmap.SelectedAreaFillColor = System.Drawing.Color.FromArgb(((int)(((byte)(33)))), ((int)(((byte)(65)))), ((int)(((byte)(105)))), ((int)(((byte)(225)))));
            this._gmap.ShowTileGridLines = false;
            this._gmap.Size = new System.Drawing.Size(894, 476);
            this._gmap.TabIndex = 1;
            this._gmap.Zoom = 14D;
            // 
            // _tripsPanel
            // 
            this._tripsPanel.BackColor = System.Drawing.Color.FromArgb(40, 40, 40);
            this._tripsPanel.Controls.Add(this._tripsHeaderLabel);
            this._tripsPanel.Controls.Add(this._tripsCountLabel);
            this._tripsPanel.Controls.Add(this._tripsHorizonLabel);
            this._tripsPanel.Controls.Add(this._tripsHorizonCombo);
            this._tripsPanel.Controls.Add(this._etaButton);
            this._tripsPanel.Controls.Add(this._etaStatusLabel);
            this._tripsPanel.Controls.Add(this._tripsFlow);
            this._tripsPanel.Controls.Add(this._tripsEmptyLabel);
            this._tripsPanel.Dock = System.Windows.Forms.DockStyle.Right;
            this._tripsPanel.Name = "_tripsPanel";
            this._tripsPanel.Size = new System.Drawing.Size(320, 476);
            this._tripsPanel.TabIndex = 3;
            // 
            // _tripsHeaderLabel
            // 
            this._tripsHeaderLabel.AutoSize = false;
            this._tripsHeaderLabel.Font = new System.Drawing.Font("Segoe UI Semibold", 10F);
            this._tripsHeaderLabel.ForeColor = System.Drawing.Color.Gainsboro;
            this._tripsHeaderLabel.Location = new System.Drawing.Point(14, 12);
            this._tripsHeaderLabel.Size = new System.Drawing.Size(294, 22);
            this._tripsHeaderLabel.Text = "Trips assigned today";
            this._tripsHeaderLabel.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top
                | System.Windows.Forms.AnchorStyles.Left)
                | System.Windows.Forms.AnchorStyles.Right)));
            // 
            // _tripsCountLabel
            // 
            this._tripsCountLabel.AutoSize = false;
            this._tripsCountLabel.Font = new System.Drawing.Font("Segoe UI", 8.5F);
            this._tripsCountLabel.ForeColor = System.Drawing.Color.Silver;
            this._tripsCountLabel.Location = new System.Drawing.Point(14, 34);
            this._tripsCountLabel.Size = new System.Drawing.Size(294, 18);
            this._tripsCountLabel.Text = "";
            this._tripsCountLabel.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top
                | System.Windows.Forms.AnchorStyles.Left)
                | System.Windows.Forms.AnchorStyles.Right)));
            // 
            // _tripsHorizonLabel
            // 
            this._tripsHorizonLabel.AutoSize = false;
            this._tripsHorizonLabel.Font = new System.Drawing.Font("Segoe UI", 8.5F);
            this._tripsHorizonLabel.ForeColor = System.Drawing.Color.Gainsboro;
            this._tripsHorizonLabel.Location = new System.Drawing.Point(14, 60);
            this._tripsHorizonLabel.Size = new System.Drawing.Size(46, 18);
            this._tripsHorizonLabel.Text = "Show:";
            this._tripsHorizonLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // _tripsHorizonCombo
            // 
            this._tripsHorizonCombo.BackColor = System.Drawing.Color.FromArgb(60, 60, 60);
            this._tripsHorizonCombo.DrawMode = System.Windows.Forms.DrawMode.OwnerDrawFixed;
            this._tripsHorizonCombo.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this._tripsHorizonCombo.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this._tripsHorizonCombo.Font = new System.Drawing.Font("Segoe UI", 8.5F);
            this._tripsHorizonCombo.ForeColor = System.Drawing.Color.Gainsboro;
            this._tripsHorizonCombo.FormattingEnabled = true;
            this._tripsHorizonCombo.ItemHeight = 18;
            this._tripsHorizonCombo.Location = new System.Drawing.Point(60, 58);
            this._tripsHorizonCombo.Name = "_tripsHorizonCombo";
            this._tripsHorizonCombo.Size = new System.Drawing.Size(248, 24);
            this._tripsHorizonCombo.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top
                | System.Windows.Forms.AnchorStyles.Left)
                | System.Windows.Forms.AnchorStyles.Right)));
            this._tripsHorizonCombo.SelectedIndexChanged += new System.EventHandler(this.OnTripFilterChanged);
            this._tripsHorizonCombo.DrawItem += new System.Windows.Forms.DrawItemEventHandler(this.OnTripHorizonComboDrawItem);
            // 
            // _etaButton
            // 
            this._etaButton.AutoSize = false;
            this._etaButton.Density = MaterialSkin.Controls.MaterialButton.MaterialButtonDensity.Default;
            this._etaButton.Location = new System.Drawing.Point(12, 88);
            this._etaButton.Size = new System.Drawing.Size(140, 32);
            this._etaButton.Text = "ESTIMATE ETAs";
            this._etaButton.Type = MaterialSkin.Controls.MaterialButton.MaterialButtonType.Outlined;
            this._etaButton.UseAccentColor = false;
            this._etaButton.NoAccentTextColor = System.Drawing.Color.Gainsboro;
            this._etaButton.Click += new System.EventHandler(this.OnEstimateEtasClicked);
            // 
            // _etaStatusLabel
            // 
            // Lives on its own row beneath the button so a long error like "Routing service
            // returned HTTP 414 — chain too long..." has room to wrap. AutoEllipsis + the
            // ToolTip wired up in the form's constructor cover anything that still doesn't fit.
            this._etaStatusLabel.AutoSize = false;
            this._etaStatusLabel.AutoEllipsis = true;
            this._etaStatusLabel.Font = new System.Drawing.Font("Segoe UI", 8.25F);
            this._etaStatusLabel.ForeColor = System.Drawing.Color.Silver;
            this._etaStatusLabel.Location = new System.Drawing.Point(12, 124);
            this._etaStatusLabel.Size = new System.Drawing.Size(308, 36);
            this._etaStatusLabel.Text = "";
            this._etaStatusLabel.TextAlign = System.Drawing.ContentAlignment.TopLeft;
            this._etaStatusLabel.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top
                | System.Windows.Forms.AnchorStyles.Left)
                | System.Windows.Forms.AnchorStyles.Right)));
            // 
            // _tripsFlow
            // 
            this._tripsFlow.AutoScroll = true;
            this._tripsFlow.BackColor = System.Drawing.Color.FromArgb(40, 40, 40);
            this._tripsFlow.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            this._tripsFlow.Location = new System.Drawing.Point(12, 168);
            this._tripsFlow.Name = "_tripsFlow";
            this._tripsFlow.Padding = new System.Windows.Forms.Padding(0, 4, 0, 4);
            this._tripsFlow.Size = new System.Drawing.Size(308, 300);
            this._tripsFlow.WrapContents = false;
            this._tripsFlow.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top
                | System.Windows.Forms.AnchorStyles.Bottom)
                | System.Windows.Forms.AnchorStyles.Left)
                | System.Windows.Forms.AnchorStyles.Right)));
            // 
            // _tripsEmptyLabel
            // 
            this._tripsEmptyLabel.AutoSize = false;
            this._tripsEmptyLabel.Font = new System.Drawing.Font("Segoe UI", 9F);
            this._tripsEmptyLabel.ForeColor = System.Drawing.Color.Silver;
            this._tripsEmptyLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this._tripsEmptyLabel.Location = new System.Drawing.Point(12, 168);
            this._tripsEmptyLabel.Size = new System.Drawing.Size(308, 300);
            this._tripsEmptyLabel.Text = "No trips assigned today";
            this._tripsEmptyLabel.Visible = false;
            this._tripsEmptyLabel.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top
                | System.Windows.Forms.AnchorStyles.Bottom)
                | System.Windows.Forms.AnchorStyles.Left)
                | System.Windows.Forms.AnchorStyles.Right)));
            // 
            // _refreshTimer
            // 
            this._refreshTimer.Interval = 30000;
            this._refreshTimer.Tick += new System.EventHandler(this.OnAutoRefreshTick);
            // 
            // DriverLocationMapForm
            // 
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            this.ClientSize = new System.Drawing.Size(1200, 680);
            this.Controls.Add(this._gmap);
            this.Controls.Add(this._tripsPanel);
            this.Controls.Add(this._diagnosticsPanel);
            this.Controls.Add(this._headerPanel);
            this.Controls.Add(this._footerPanel);
            this.MinimumSize = new System.Drawing.Size(800, 520);
            this.Name = "DriverLocationMapForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Driver location";
            this._headerPanel.ResumeLayout(false);
            this._diagnosticsPanel.ResumeLayout(false);
            this._footerPanel.ResumeLayout(false);
            this._footerPanel.PerformLayout();
            this._tripsPanel.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        private System.Windows.Forms.Panel _headerPanel;
        private System.Windows.Forms.Panel _statusDot;
        private System.Windows.Forms.Label _driverNameLabel;
        private System.Windows.Forms.Label _statusLabel;
        private System.Windows.Forms.Panel _diagnosticsPanel;
        private System.Windows.Forms.Label _diagnosticsHeadlineLabel;
        private System.Windows.Forms.Label _diagnosticsBodyLabel;
        private System.Windows.Forms.Panel _footerPanel;
        private System.Windows.Forms.Label _lastReportedLabel;
        private System.Windows.Forms.CheckBox _autoRefreshCheck;
        private MaterialSkin.Controls.MaterialButton _refreshButton;
        private MaterialSkin.Controls.MaterialButton _closeButton;
        private GMap.NET.WindowsForms.GMapControl _gmap;
        private System.Windows.Forms.Panel _tripsPanel;
        private System.Windows.Forms.Label _tripsHeaderLabel;
        private System.Windows.Forms.Label _tripsCountLabel;
        private System.Windows.Forms.Label _tripsHorizonLabel;
        private System.Windows.Forms.ComboBox _tripsHorizonCombo;
        private MaterialSkin.Controls.MaterialButton _etaButton;
        private System.Windows.Forms.Label _etaStatusLabel;
        private System.Windows.Forms.FlowLayoutPanel _tripsFlow;
        private System.Windows.Forms.Label _tripsEmptyLabel;
        private System.Windows.Forms.Timer _refreshTimer;
    }
}
