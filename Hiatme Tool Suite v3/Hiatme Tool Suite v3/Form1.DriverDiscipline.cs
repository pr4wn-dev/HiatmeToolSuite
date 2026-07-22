using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Driver Discipline — fill a single corrective-action form and export a printable Word .docx.
    /// Can be prefilled from the Dashcam Videos selection.
    /// </summary>
    partial class Form1
    {
        private const int DdToolbarH = 48;

        private SupeyCard ddMainCard;
        private SupeyCard ddStatusCard;
        private SupeyLabel ddStatusLbl;
        private Panel ddToolbar;
        private SupeyCard ddToolbarCard;
        private Panel ddToolbarInner;
        private SupeyMaterialButton ddClearBtn;
        private SupeyMaterialButton ddFromDashcamBtn;
        private SupeyMaterialButton ddOpenDashcamBtn;
        private SupeyMaterialButton ddAddClipsBtn;
        private SupeyMaterialButton ddGenerateBtn;

        private Panel ddBodyHost;
        private Panel ddScrollBody;
        private TableLayoutPanel ddFormGrid;

        private SupeyTextBox ddCaseTb;
        private SupeyTextBox ddPreparedTb;
        private SupeyTextBox ddDeptTb;
        private SupeyTextBox ddDriverTb;
        private SupeyTextBox ddEmployeeIdTb;
        private SupeyTextBox ddVehicleTb;
        private SupeyTextBox ddSupervisorTb;
        private RJDatePicker ddNoticeDate;
        private RJDatePicker ddIncidentDate;
        private SupeyTextBox ddIncidentTimeTb;
        private SupeyTextBox ddTripRefTb;
        private SupeyTextBox ddLocationTb;
        private SupeyComboBox ddActionCombo;
        private Panel ddViolationHost;
        private readonly List<SupeyCheckbox> _ddViolationChecks = new List<SupeyCheckbox>();
        private TextBox ddFootageSummaryTb;
        private TextBox ddNarrativeTb;
        private SupeyTextBox ddPolicyTb;
        private TextBox ddPriorTb;
        private TextBox ddCorrectiveTb;
        private SupeyTextBox ddFollowUpTb;
        private TextBox ddDriverStatementTb;
        private SupeyTextBox ddFolderTb;
        private SupeyListView ddClipsLv;

        private bool _ddBuilt;
        private readonly List<string> _ddClipPaths = new List<string>();

        private void InitializeDriverDisciplineTab()
        {
            if (_ddBuilt || hiatmeTabControl == null || tabPageDriverDiscipline == null)
                return;

            try
            {
                tabPageDriverDiscipline.SuspendLayout();
                tabPageDriverDiscipline.Controls.Clear();

                if (tabImageList != null && tabImageList.Images.ContainsKey("driver-discipline.png"))
                    tabPageDriverDiscipline.ImageKey = "driver-discipline.png";
                else if (tabImageList != null && tabImageList.Images.ContainsKey("cctv-custom.png"))
                    tabPageDriverDiscipline.ImageKey = "cctv-custom.png";

                tabPageDriverDiscipline.Text = "Driver Discipline";
                tabPageDriverDiscipline.BackColor = SupeyTheme.SurfaceBase;
                tabPageDriverDiscipline.ForeColor = SupeyTheme.TextPrimary;
                tabPageDriverDiscipline.Padding = new Padding(ToolTabInset);

                // Keep immediately after Dashcam Videos.
                int dashAt = hiatmeTabControl.TabPages.IndexOf(tabPageDashcamVideos);
                int mineAt = hiatmeTabControl.TabPages.IndexOf(tabPageDriverDiscipline);
                if (dashAt >= 0)
                {
                    int want = dashAt + 1;
                    if (mineAt != want)
                    {
                        hiatmeTabControl.TabPages.Remove(tabPageDriverDiscipline);
                        if (want > hiatmeTabControl.TabPages.Count)
                            want = hiatmeTabControl.TabPages.Count;
                        hiatmeTabControl.TabPages.Insert(want, tabPageDriverDiscipline);
                    }
                }

                ddStatusCard = new SupeyCard
                {
                    Name = "ddStatusCard",
                    Dock = DockStyle.Bottom,
                    Height = ToolTabStatusH,
                    SurfaceLevel = SupeyCard.Surface.StatusBar,
                    ShowBorder = true,
                    CornerRadius = 6,
                };
                ddStatusLbl = new SupeyLabel
                {
                    Name = "ddStatusLbl",
                    Text = "Status: ready — fill the form, then Generate Word document.",
                    AutoSize = false,
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(10, 0, 10, 0),
                    ForeColor = SupeyTheme.TextSecondary,
                    Font = SupeyTheme.BodyFont,
                    BackColor = SupeyTheme.SurfaceStatusBar,
                };
                ddStatusCard.Controls.Add(ddStatusLbl);

                ddMainCard = new SupeyCard
                {
                    Name = "ddMainCard",
                    Dock = DockStyle.Fill,
                    SurfaceLevel = SupeyCard.Surface.Standard,
                    ShowBorder = true,
                    CornerRadius = 8,
                    Margin = new Padding(0, 0, 0, ToolTabGap),
                };

                BuildDriverDisciplineToolbar();
                BuildDriverDisciplineBody();

                ddMainCard.Controls.Add(ddBodyHost);
                ddMainCard.Controls.Add(ddToolbar);

                tabPageDriverDiscipline.Controls.Add(ddMainCard);
                tabPageDriverDiscipline.Controls.Add(ddStatusCard);

                tabPageDriverDiscipline.VisibleChanged -= DriverDisciplineTab_VisibleChanged;
                tabPageDriverDiscipline.VisibleChanged += DriverDisciplineTab_VisibleChanged;

                ResetDriverDisciplineForm(seedCase: true);
                ApplyDriverDisciplineVisualTheme(layout: true);
                SupeyDarkScrollBars.Apply(tabPageDriverDiscipline);

                _ddBuilt = true;
                tabPageDriverDiscipline.ResumeLayout(true);
            }
            catch (Exception ex)
            {
                _ddBuilt = false;
                try { tabPageDriverDiscipline.ResumeLayout(true); } catch { }
                try
                {
                    tabPageDriverDiscipline.Controls.Clear();
                    tabPageDriverDiscipline.Controls.Add(new Label
                    {
                        Dock = DockStyle.Fill,
                        TextAlign = ContentAlignment.MiddleCenter,
                        BackColor = Color.FromArgb(40, 20, 20),
                        ForeColor = Color.OrangeRed,
                        Font = new Font("Segoe UI", 11f),
                        Text = "Driver Discipline failed to build:\r\n\r\n" + ex.Message,
                    });
                }
                catch { }
            }
        }

        private void DriverDisciplineTab_VisibleChanged(object sender, EventArgs e)
        {
            if (tabPageDriverDiscipline == null || !tabPageDriverDiscipline.Visible) return;
            if (!_ddBuilt)
                InitializeDriverDisciplineTab();
        }

        private void BuildDriverDisciplineToolbar()
        {
            ddToolbar = new Panel
            {
                Name = "ddToolbar",
                Dock = DockStyle.Top,
                Height = DdToolbarH + 12,
                Padding = new Padding(10, 6, 10, 4),
                BackColor = SupeyTheme.Surface,
            };
            ddToolbarCard = new SupeyCard
            {
                Name = "ddToolbarCard",
                Dock = DockStyle.Fill,
                SurfaceLevel = SupeyCard.Surface.Elevated,
                ShowBorder = true,
                CornerRadius = 8,
                Padding = Padding.Empty,
            };
            ddToolbarInner = new Panel
            {
                Name = "ddToolbarInner",
                Dock = DockStyle.Fill,
                Padding = new Padding(12, 8, 12, 8),
                BackColor = SupeyTheme.SurfaceElevated,
            };

            ddGenerateBtn = new SupeyMaterialButton
            {
                Name = "ddGenerateBtn",
                Text = "Generate Word",
                Type = SupeyMaterialButton.MaterialButtonType.Contained,
                UseAccentColor = true,
                Size = new Size(130, 32),
                Location = new Point(0, 2),
            };
            ddGenerateBtn.Click += (_, __) => GenerateDriverDisciplineWord();

            ddFromDashcamBtn = new SupeyMaterialButton
            {
                Name = "ddFromDashcamBtn",
                Text = "From Dashcam",
                Type = SupeyMaterialButton.MaterialButtonType.Outlined,
                Size = new Size(120, 32),
                Location = new Point(140, 2),
            };
            ddFromDashcamBtn.Click += (_, __) => PrefillDriverDisciplineFromDashcam();

            ddOpenDashcamBtn = new SupeyMaterialButton
            {
                Name = "ddOpenDashcamBtn",
                Text = "Open Dashcam",
                Type = SupeyMaterialButton.MaterialButtonType.Outlined,
                Size = new Size(120, 32),
                Location = new Point(270, 2),
            };
            ddOpenDashcamBtn.Click += (_, __) =>
            {
                if (tabPageDashcamVideos != null)
                    hiatmeTabControl.SelectedTab = tabPageDashcamVideos;
            };

            ddAddClipsBtn = new SupeyMaterialButton
            {
                Name = "ddAddClipsBtn",
                Text = "Add clip files…",
                Type = SupeyMaterialButton.MaterialButtonType.Outlined,
                Size = new Size(120, 32),
                Location = new Point(400, 2),
            };
            ddAddClipsBtn.Click += (_, __) => AddDriverDisciplineClipsManual();

            ddClearBtn = new SupeyMaterialButton
            {
                Name = "ddClearBtn",
                Text = "Clear form",
                Type = SupeyMaterialButton.MaterialButtonType.Text,
                Size = new Size(100, 32),
                Location = new Point(530, 2),
            };
            ddClearBtn.Click += (_, __) =>
            {
                if (MessageBox.Show(
                        this,
                        "Clear all fields on this write-up?",
                        "Driver Discipline",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question,
                        MessageBoxDefaultButton.Button2) == DialogResult.Yes)
                {
                    ResetDriverDisciplineForm(seedCase: true);
                    SetDriverDisciplineStatus("Form cleared.");
                }
            };

            ddToolbarInner.Controls.Add(ddGenerateBtn);
            ddToolbarInner.Controls.Add(ddFromDashcamBtn);
            ddToolbarInner.Controls.Add(ddOpenDashcamBtn);
            ddToolbarInner.Controls.Add(ddAddClipsBtn);
            ddToolbarInner.Controls.Add(ddClearBtn);
            ddToolbarCard.Controls.Add(ddToolbarInner);
            ddToolbar.Controls.Add(ddToolbarCard);
        }

        private void BuildDriverDisciplineBody()
        {
            ddBodyHost = new Panel
            {
                Name = "ddBodyHost",
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.Surface,
                Padding = new Padding(4),
            };

            ddScrollBody = new Panel
            {
                Name = "ddScrollBody",
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = SupeyTheme.Surface,
                Padding = new Padding(16, 12, 16, 20),
            };

            ddFormGrid = new TableLayoutPanel
            {
                Name = "ddFormGrid",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                ColumnCount = 1,
                BackColor = SupeyTheme.Surface,
                Padding = new Padding(0),
                Margin = new Padding(0),
            };
            ddFormGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            int row = 0;
            AddDdSection(ref row, "Case & notice");
            AddDdGrid(ref row, BuildDdMetaGrid());
            AddDdSection(ref row, "Employee");
            AddDdGrid(ref row, BuildDdEmployeeGrid());
            AddDdSection(ref row, "Incident");
            AddDdGrid(ref row, BuildDdIncidentGrid());
            AddDdSection(ref row, "Violation type(s) — check all that apply");
            AddDdControl(ref row, BuildDdViolationPanel(), 220);
            AddDdSection(ref row, "Action level");
            ddActionCombo = new SupeyComboBox
            {
                Name = "ddActionCombo",
                Dock = DockStyle.Top,
                Width = 420,
                UseTallSize = true,
                Hint = "Action level",
            };
            foreach (string level in DriverDisciplineOptions.ActionLevels)
                ddActionCombo.Items.Add(level);
            ddActionCombo.SelectedItem = "Written warning";
            AddDdControl(ref row, WrapDdField(ddActionCombo, 440, 64), 68);

            AddDdSection(ref row, "What the footage shows (short summary)");
            ddFootageSummaryTb = MakeDdMemo("ddFootageSummaryTb", 72);
            AddDdControl(ref row, ddFootageSummaryTb, 80);

            AddDdSection(ref row, "Full narrative / investigation notes");
            ddNarrativeTb = MakeDdMemo("ddNarrativeTb", 120);
            AddDdControl(ref row, ddNarrativeTb, 128);

            AddDdSection(ref row, "Dashcam evidence");
            AddDdGrid(ref row, BuildDdEvidenceGrid());

            AddDdSection(ref row, "Policy / rule cited");
            ddPolicyTb = new SupeyTextBox
            {
                Name = "ddPolicyTb",
                Dock = DockStyle.Top,
                UseTallSize = true,
                Hint = "Policy / handbook section",
            };
            AddDdControl(ref row, WrapDdField(ddPolicyTb, 0, 64), 68);

            AddDdSection(ref row, "Prior related history");
            ddPriorTb = MakeDdMemo("ddPriorTb", 72);
            AddDdControl(ref row, ddPriorTb, 80);

            AddDdSection(ref row, "Corrective action required");
            ddCorrectiveTb = MakeDdMemo("ddCorrectiveTb", 90);
            AddDdControl(ref row, ddCorrectiveTb, 98);
            ddFollowUpTb = new SupeyTextBox
            {
                Name = "ddFollowUpTb",
                Dock = DockStyle.Top,
                UseTallSize = true,
                Hint = "Follow-up / review date",
            };
            AddDdControl(ref row, WrapDdField(ddFollowUpTb, 320, 64), 68);

            AddDdSection(ref row, "Driver statement (optional — leave blank for paper completion)");
            ddDriverStatementTb = MakeDdMemo("ddDriverStatementTb", 72);
            AddDdControl(ref row, ddDriverStatementTb, 80);

            ddScrollBody.Controls.Add(ddFormGrid);
            ddBodyHost.Controls.Add(ddScrollBody);
            ddScrollBody.Resize += (_, __) =>
            {
                if (ddFormGrid != null)
                    ddFormGrid.Width = Math.Max(640, ddScrollBody.ClientSize.Width - 36);
            };
        }

        private void AddDdSection(ref int row, string title)
        {
            var lbl = new SupeyLabel
            {
                Text = title,
                AutoSize = false,
                Height = 28,
                Dock = DockStyle.Top,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = SupeyTheme.SubHeaderFont,
                ForeColor = SupeyTheme.TextPrimary,
                BackColor = SupeyTheme.Surface,
                Padding = new Padding(0, 8, 0, 0),
                Margin = new Padding(0, row == 0 ? 0 : 8, 0, 2),
            };
            ddFormGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            ddFormGrid.Controls.Add(lbl, 0, row++);
        }

        private void AddDdGrid(ref int row, Control grid)
        {
            grid.Dock = DockStyle.Top;
            grid.Margin = new Padding(0, 0, 0, 6);
            ddFormGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            ddFormGrid.Controls.Add(grid, 0, row++);
        }

        private void AddDdControl(ref int row, Control c, int height)
        {
            c.Height = height;
            c.Dock = DockStyle.Top;
            c.Margin = new Padding(0, 0, 0, 6);
            ddFormGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, height + 6));
            ddFormGrid.Controls.Add(c, 0, row++);
        }

        private static Panel WrapDdField(Control field, int width, int height)
        {
            var host = new Panel
            {
                Height = height,
                BackColor = SupeyTheme.Surface,
                Padding = new Padding(0),
            };
            if (width > 0) field.Width = width;
            else field.Dock = DockStyle.Fill;
            if (width > 0)
            {
                field.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                field.Width = Math.Max(200, width);
            }
            host.Controls.Add(field);
            if (width <= 0)
                field.Dock = DockStyle.Fill;
            else
            {
                field.Location = new Point(0, 0);
                host.Resize += (_, __) => field.Width = Math.Max(200, host.ClientSize.Width);
            }
            return host;
        }

        private TableLayoutPanel BuildDdMetaGrid()
        {
            var grid = MakeDdTwoColGrid();
            ddCaseTb = MakeDdText("ddCaseTb", "Case / reference #");
            ddPreparedTb = MakeDdText("ddPreparedTb", "Prepared by");
            ddDeptTb = MakeDdText("ddDeptTb", "Department");
            ddNoticeDate = MakeDdDate("ddNoticeDate");

            grid.Controls.Add(LabeledDd("Case / reference #", ddCaseTb), 0, 0);
            grid.Controls.Add(LabeledDd("Notice date", ddNoticeDate), 1, 0);
            grid.Controls.Add(LabeledDd("Prepared by", ddPreparedTb), 0, 1);
            grid.Controls.Add(LabeledDd("Department", ddDeptTb), 1, 1);
            return grid;
        }

        private TableLayoutPanel BuildDdEmployeeGrid()
        {
            var grid = MakeDdTwoColGrid();
            ddDriverTb = MakeDdText("ddDriverTb", "Driver name");
            ddEmployeeIdTb = MakeDdText("ddEmployeeIdTb", "Employee ID");
            ddVehicleTb = MakeDdText("ddVehicleTb", "Vehicle");
            ddSupervisorTb = MakeDdText("ddSupervisorTb", "Supervisor");

            grid.Controls.Add(LabeledDd("Driver name", ddDriverTb), 0, 0);
            grid.Controls.Add(LabeledDd("Employee ID", ddEmployeeIdTb), 1, 0);
            grid.Controls.Add(LabeledDd("Vehicle", ddVehicleTb), 0, 1);
            grid.Controls.Add(LabeledDd("Supervisor", ddSupervisorTb), 1, 1);
            return grid;
        }

        private TableLayoutPanel BuildDdIncidentGrid()
        {
            var grid = MakeDdTwoColGrid();
            grid.ColumnCount = 3;
            grid.ColumnStyles.Clear();
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f));
            grid.RowCount = 2;

            ddIncidentDate = MakeDdDate("ddIncidentDate");
            ddIncidentTimeTb = MakeDdText("ddIncidentTimeTb", "e.g. 14:35");
            ddTripRefTb = MakeDdText("ddTripRefTb", "Trip / client ref");
            ddLocationTb = MakeDdText("ddLocationTb", "Location / area");

            grid.Controls.Add(LabeledDd("Incident date", ddIncidentDate), 0, 0);
            grid.Controls.Add(LabeledDd("Approximate time", ddIncidentTimeTb), 1, 0);
            grid.Controls.Add(LabeledDd("Trip / client ref", ddTripRefTb), 2, 0);

            var locHost = LabeledDd("Location / area", ddLocationTb);
            grid.SetColumnSpan(locHost, 3);
            grid.Controls.Add(locHost, 0, 1);
            return grid;
        }

        private Panel BuildDdViolationPanel()
        {
            ddViolationHost = new Panel
            {
                Name = "ddViolationHost",
                Height = 220,
                BackColor = SupeyTheme.SurfaceElevated,
                Padding = new Padding(12, 10, 12, 10),
            };
            // Paint a light bordered card look via SupeyCard-like panel
            var card = new SupeyCard
            {
                Dock = DockStyle.Fill,
                SurfaceLevel = SupeyCard.Surface.Elevated,
                ShowBorder = true,
                CornerRadius = 8,
                Padding = new Padding(12, 8, 12, 8),
            };
            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = true,
                AutoScroll = false,
                BackColor = SupeyTheme.SurfaceElevated,
                Padding = new Padding(0),
            };

            _ddViolationChecks.Clear();
            foreach (string label in DriverDisciplineOptions.Violations)
            {
                var chk = new SupeyCheckbox
                {
                    Text = label,
                    Checked = false,
                    Size = new Size(420, 24),
                    Margin = new Padding(0, 2, 16, 2),
                    BackColor = SupeyTheme.SurfaceElevated,
                };
                _ddViolationChecks.Add(chk);
                flow.Controls.Add(chk);
            }

            card.Controls.Add(flow);
            ddViolationHost.Controls.Add(card);
            return ddViolationHost;
        }

        private TableLayoutPanel BuildDdEvidenceGrid()
        {
            var grid = new TableLayoutPanel
            {
                ColumnCount = 1,
                RowCount = 2,
                AutoSize = true,
                BackColor = SupeyTheme.Surface,
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 70f));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 140f));

            ddFolderTb = MakeDdText("ddFolderTb", "Footage folder path");
            grid.Controls.Add(LabeledDd("Footage folder", ddFolderTb), 0, 0);

            ddClipsLv = new SupeyListView
            {
                Name = "ddClipsLv",
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                BorderStyle = BorderStyle.None,
                MultiSelect = false,
                Height = 130,
                BackColor = SupeyTheme.ListBody,
                ForeColor = SupeyTheme.ListText,
            };
            try { ddClipsLv.Font = ListViewOwnerDrawFonts.Cell; } catch { }
            ddClipsLv.Columns.Add("Clip file", 280);
            ddClipsLv.Columns.Add("Full path", 520);
            ddClipsLv.DrawColumnHeader += listView_DrawColumnHeader;
            ddClipsLv.DrawItem += listView_DrawItem;
            ddClipsLv.DrawSubItem += listView_DrawSubItem;
            ListViewHeaderEmptyAreaPainter.Attach(ddClipsLv);
            SupeyListViewHelpers.EnableDoubleBufferRecursively(ddClipsLv);

            var clipHost = new SupeyCard
            {
                Dock = DockStyle.Fill,
                SurfaceLevel = SupeyCard.Surface.Elevated,
                ShowBorder = true,
                CornerRadius = 8,
                Padding = new Padding(6),
            };
            clipHost.Controls.Add(ddClipsLv);
            grid.Controls.Add(clipHost, 0, 1);
            return grid;
        }

        private static TableLayoutPanel MakeDdTwoColGrid()
        {
            var grid = new TableLayoutPanel
            {
                ColumnCount = 2,
                RowCount = 2,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = SupeyTheme.Surface,
                Margin = new Padding(0),
                Padding = new Padding(0),
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 78f));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 78f));
            return grid;
        }

        private static Panel LabeledDd(string label, Control field)
        {
            var host = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(0, 0, 12, 8),
                BackColor = SupeyTheme.Surface,
                Margin = new Padding(0),
            };
            var lbl = new SupeyLabel
            {
                Text = label,
                Dock = DockStyle.Top,
                Height = 18,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = SupeyTheme.TextMuted,
                Font = SupeyTheme.CaptionFont,
                BackColor = SupeyTheme.Surface,
                Margin = new Padding(0),
            };
            field.Dock = DockStyle.Top;
            if (field is SupeyTextBox tb)
            {
                tb.UseTallSize = true;
                tb.Height = 58;
            }
            else if (field is RJDatePicker)
            {
                field.Height = 36;
                field.Margin = new Padding(0, 10, 0, 0);
            }
            host.Controls.Add(field);
            host.Controls.Add(lbl);
            return host;
        }

        private static SupeyTextBox MakeDdText(string name, string hint)
        {
            return new SupeyTextBox
            {
                Name = name,
                Hint = hint,
                UseTallSize = true,
                Height = 58,
            };
        }

        private static RJDatePicker MakeDdDate(string name)
        {
            return new RJDatePicker
            {
                Name = name,
                Format = DateTimePickerFormat.Short,
                Value = DateTime.Today,
                Width = 200,
                Height = 36,
            };
        }

        private static TextBox MakeDdMemo(string name, int height)
        {
            return new TextBox
            {
                Name = name,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                AcceptsReturn = true,
                BorderStyle = BorderStyle.FixedSingle,
                Height = height,
                Font = SupeyTheme.BodyFont,
                BackColor = SupeyTheme.SurfaceElevated,
                ForeColor = SupeyTheme.TextPrimary,
                Margin = new Padding(0, 0, 0, 0),
            };
        }

        private void ResetDriverDisciplineForm(bool seedCase)
        {
            if (!_ddBuilt && ddCaseTb == null) return;

            if (ddCaseTb != null)
                ddCaseTb.Text = seedCase
                    ? "CA-" + DateTime.Now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture)
                    : "";
            if (ddPreparedTb != null) ddPreparedTb.Text = Environment.UserName ?? "";
            if (ddDeptTb != null) ddDeptTb.Text = "Operations";
            if (ddDriverTb != null) ddDriverTb.Text = "";
            if (ddEmployeeIdTb != null) ddEmployeeIdTb.Text = "";
            if (ddVehicleTb != null) ddVehicleTb.Text = "";
            if (ddSupervisorTb != null) ddSupervisorTb.Text = "";
            if (ddNoticeDate != null) ddNoticeDate.Value = DateTime.Today;
            if (ddIncidentDate != null) ddIncidentDate.Value = DateTime.Today;
            if (ddIncidentTimeTb != null) ddIncidentTimeTb.Text = "";
            if (ddTripRefTb != null) ddTripRefTb.Text = "";
            if (ddLocationTb != null) ddLocationTb.Text = "";
            if (ddActionCombo != null && ddActionCombo.Items.Count > 0)
                ddActionCombo.SelectedItem = "Written warning";
            foreach (var chk in _ddViolationChecks) chk.Checked = false;
            if (ddFootageSummaryTb != null) ddFootageSummaryTb.Text = "";
            if (ddNarrativeTb != null) ddNarrativeTb.Text = "";
            if (ddPolicyTb != null) ddPolicyTb.Text = "";
            if (ddPriorTb != null) ddPriorTb.Text = "";
            if (ddCorrectiveTb != null) ddCorrectiveTb.Text = "";
            if (ddFollowUpTb != null) ddFollowUpTb.Text = "";
            if (ddDriverStatementTb != null) ddDriverStatementTb.Text = "";
            if (ddFolderTb != null) ddFolderTb.Text = "";
            _ddClipPaths.Clear();
            RefreshDriverDisciplineClipList();
        }

        private DriverDisciplineRecord CollectDriverDisciplineRecord()
        {
            var r = new DriverDisciplineRecord
            {
                CaseNumber = T(ddCaseTb),
                NoticeDate = ddNoticeDate?.Value.Date ?? DateTime.Today,
                PreparedBy = T(ddPreparedTb),
                Department = T(ddDeptTb),
                DriverName = T(ddDriverTb),
                EmployeeId = T(ddEmployeeIdTb),
                Vehicle = T(ddVehicleTb),
                SupervisorName = T(ddSupervisorTb),
                IncidentDate = ddIncidentDate?.Value.Date ?? DateTime.Today,
                IncidentTime = T(ddIncidentTimeTb),
                TripOrClientRef = T(ddTripRefTb),
                Location = T(ddLocationTb),
                ActionLevel = ddActionCombo?.SelectedItem as string ?? "Written warning",
                FootageSummary = ddFootageSummaryTb?.Text?.Trim() ?? "",
                Narrative = ddNarrativeTb?.Text?.Trim() ?? "",
                PolicyCited = T(ddPolicyTb),
                PriorHistory = ddPriorTb?.Text?.Trim() ?? "",
                CorrectiveAction = ddCorrectiveTb?.Text?.Trim() ?? "",
                FollowUpDate = T(ddFollowUpTb),
                DriverStatement = ddDriverStatementTb?.Text?.Trim() ?? "",
                FootageFolder = T(ddFolderTb),
            };

            foreach (var chk in _ddViolationChecks)
            {
                if (chk.Checked)
                    r.Violations.Add(chk.Text);
            }
            r.ClipPaths.AddRange(_ddClipPaths);
            return r;
        }

        private static string T(SupeyTextBox tb) => (tb?.Text ?? "").Trim();

        private void GenerateDriverDisciplineWord()
        {
            if (!_ddBuilt)
                InitializeDriverDisciplineTab();

            var record = CollectDriverDisciplineRecord();
            if (string.IsNullOrWhiteSpace(record.DriverName))
            {
                SupeyMessageForm.Show(this, "Driver Discipline",
                    "Enter a driver name before generating the Word document.",
                    SupeyMessageKind.Warning, "Missing driver");
                ddDriverTb?.Focus();
                return;
            }
            if (record.Violations.Count == 0)
            {
                SupeyMessageForm.Show(this, "Driver Discipline",
                    "Select at least one violation type.",
                    SupeyMessageKind.Warning, "Missing violation");
                return;
            }

            string safeDriver = string.Join("_", record.DriverName.Split(Path.GetInvalidFileNameChars()));
            string suggested = "CorrectiveAction_" + safeDriver + "_" +
                               record.IncidentDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".docx";

            using (var dlg = new SaveFileDialog
            {
                Title = "Save corrective action Word document",
                Filter = "Word document (*.docx)|*.docx",
                FileName = suggested,
                OverwritePrompt = true,
                AddExtension = true,
                DefaultExt = "docx",
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    DriverDisciplineDocument.Save(dlg.FileName, record);
                    SetDriverDisciplineStatus("Saved: " + dlg.FileName);
                    if (MessageBox.Show(
                            this,
                            "Word document saved.\r\n\r\nOpen it now for printing?",
                            "Driver Discipline",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Information) == DialogResult.Yes)
                    {
                        Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
                    }
                }
                catch (Exception ex)
                {
                    SetDriverDisciplineStatus("Save failed.");
                    SupeyMessageForm.Show(this, "Driver Discipline",
                        "Could not create Word document:\r\n\r\n" + ex.Message,
                        SupeyMessageKind.Warning, "Save failed");
                }
            }
        }

        /// <summary>Prefill from the currently selected Dashcam Videos driver (and optional issue row).</summary>
        internal void PrefillDriverDisciplineFromDashcam()
        {
            if (!_ddBuilt)
                InitializeDriverDisciplineTab();

            if (_dcSelected == null)
            {
                // Try ensure dashcam is built / has selection
                if (!_dcBuilt)
                    InitializeDashcamVideosTab();
            }

            var driver = _dcSelected;
            if (driver == null)
            {
                SupeyMessageForm.Show(this, "Driver Discipline",
                    "Select a driver on the Dashcam Videos tab first, then use From Dashcam.",
                    SupeyMessageKind.Information, "No Dashcam selection");
                if (tabPageDashcamVideos != null)
                    hiatmeTabControl.SelectedTab = tabPageDashcamVideos;
                return;
            }

            if (ddDriverTb != null) ddDriverTb.Text = driver.Driver ?? "";
            if (ddFolderTb != null) ddFolderTb.Text = driver.FolderPath ?? "";

            // Best-effort vehicle from Supey roster
            try
            {
                string warn;
                var roster = SupeyDriverRosterStore.Load(out warn);
                var match = roster?.FirstOrDefault(p =>
                    string.Equals((p.Name ?? "").Trim(), (driver.Driver ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
                if (match != null && ddVehicleTb != null && !string.IsNullOrWhiteSpace(match.VehicleLabel))
                    ddVehicleTb.Text = match.VehicleLabel.Trim();
            }
            catch { /* roster is optional */ }

            DateTime around = driver.LastClip != default(DateTime) ? driver.LastClip : DateTime.Today;
            string issueNote = "";

            if (dcIssuesLv != null && dcIssuesLv.SelectedItems.Count > 0)
            {
                var tag = dcIssuesLv.SelectedItems[0].Tag;
                if (tag is DashcamVideoLibrary.SeqGap gap)
                {
                    around = gap.TimeBefore;
                    if (ddIncidentDate != null) ddIncidentDate.Value = gap.TimeBefore.Date;
                    if (ddIncidentTimeTb != null)
                        ddIncidentTimeTb.Text = gap.TimeBefore.ToString("HH:mm", CultureInfo.InvariantCulture);
                    issueNote = string.Format(CultureInfo.InvariantCulture,
                        "Dashcam gap noted: missing ~{0} clip(s) between {1:MM/dd/yyyy HH:mm} and {2:MM/dd/yyyy HH:mm} (seq {3}→{4}).",
                        gap.MissingCount, gap.TimeBefore, gap.TimeAfter, gap.LastSeqBefore, gap.NextSeqAfter);
                }
                else if (tag is DateTime day)
                {
                    around = day.Date.AddHours(12);
                    if (ddIncidentDate != null) ddIncidentDate.Value = day.Date;
                    issueNote = "Dashcam lost-day flag for " + day.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture) + ".";
                }
            }
            else
            {
                if (ddIncidentDate != null && driver.LastClip != default(DateTime))
                    ddIncidentDate.Value = driver.LastClip.Date;
                if (ddIncidentTimeTb != null && driver.LastClip != default(DateTime))
                    ddIncidentTimeTb.Text = driver.LastClip.ToString("HH:mm", CultureInfo.InvariantCulture);
            }

            var clips = DashcamVideoLibrary.FindClipPathsNear(driver.FolderPath, around, TimeSpan.FromMinutes(20), 12);
            _ddClipPaths.Clear();
            _ddClipPaths.AddRange(clips);
            RefreshDriverDisciplineClipList();

            if (ddFootageSummaryTb != null && string.IsNullOrWhiteSpace(ddFootageSummaryTb.Text))
            {
                ddFootageSummaryTb.Text = string.IsNullOrEmpty(issueNote)
                    ? "Review dashcam footage for " + driver.Driver + " near " +
                      around.ToString("MM/dd/yyyy HH:mm", CultureInfo.InvariantCulture) + "."
                    : issueNote;
            }

            if (tabPageDriverDiscipline != null)
                hiatmeTabControl.SelectedTab = tabPageDriverDiscipline;

            SetDriverDisciplineStatus(
                "Prefilled from Dashcam: " + driver.Driver +
                (clips.Count > 0 ? " · " + clips.Count + " nearby clip(s)" : " · no nearby clips found"));
        }

        private void AddDriverDisciplineClipsManual()
        {
            using (var dlg = new OpenFileDialog
            {
                Title = "Add dashcam clip files",
                Filter = "Video files (*.mp4;*.avi;*.mov)|*.mp4;*.avi;*.mov|All files (*.*)|*.*",
                Multiselect = true,
                CheckFileExists = true,
            })
            {
                if (!string.IsNullOrWhiteSpace(ddFolderTb?.Text) && Directory.Exists(ddFolderTb.Text.Trim()))
                    dlg.InitialDirectory = ddFolderTb.Text.Trim();

                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                foreach (string path in dlg.FileNames)
                {
                    bool exists = _ddClipPaths.Any(p =>
                        string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
                    if (!exists)
                        _ddClipPaths.Add(path);
                }
                RefreshDriverDisciplineClipList();
                SetDriverDisciplineStatus(_ddClipPaths.Count + " clip(s) attached.");
            }
        }

        private void RefreshDriverDisciplineClipList()
        {
            if (ddClipsLv == null) return;
            ddClipsLv.BeginUpdate();
            try
            {
                ddClipsLv.Items.Clear();
                foreach (string path in _ddClipPaths)
                {
                    var it = new ListViewItem(Path.GetFileName(path));
                    it.SubItems.Add(path);
                    ddClipsLv.Items.Add(it);
                }
            }
            finally { ddClipsLv.EndUpdate(); }
        }

        private void SetDriverDisciplineStatus(string text)
        {
            if (ddStatusLbl != null)
                ddStatusLbl.Text = "Status: " + (text ?? "");
        }

        private void ApplyDriverDisciplineVisualTheme(bool layout)
        {
            if (!_ddBuilt && ddMainCard == null) return;
            try
            {
                if (tabPageDriverDiscipline != null)
                {
                    tabPageDriverDiscipline.BackColor = SupeyTheme.SurfaceBase;
                    tabPageDriverDiscipline.ForeColor = SupeyTheme.TextPrimary;
                }
                if (ddMainCard != null) StyleToolTabCard(ddMainCard, SupeyCard.Surface.Standard);
                if (ddStatusCard != null) StyleToolTabStatusBar(ddStatusCard);
                if (ddStatusLbl != null)
                {
                    ddStatusLbl.ForeColor = SupeyTheme.TextSecondary;
                    ddStatusLbl.Font = SupeyTheme.BodyFont;
                    ddStatusLbl.BackColor = SupeyTheme.SurfaceStatusBar;
                }
                if (ddToolbar != null) ddToolbar.BackColor = SupeyTheme.Surface;
                if (ddToolbarCard != null)
                {
                    ddToolbarCard.SurfaceLevel = SupeyCard.Surface.Elevated;
                    ddToolbarCard.BackColor = SupeyTheme.SurfaceElevated;
                }
                if (ddToolbarInner != null) ddToolbarInner.BackColor = SupeyTheme.SurfaceElevated;
                if (ddBodyHost != null) ddBodyHost.BackColor = SupeyTheme.Surface;
                if (ddScrollBody != null) ddScrollBody.BackColor = SupeyTheme.Surface;
                if (ddFormGrid != null) ddFormGrid.BackColor = SupeyTheme.Surface;

                foreach (var memo in new[] { ddFootageSummaryTb, ddNarrativeTb, ddPriorTb, ddCorrectiveTb, ddDriverStatementTb })
                {
                    if (memo == null) continue;
                    memo.BackColor = SupeyTheme.SurfaceElevated;
                    memo.ForeColor = SupeyTheme.TextPrimary;
                    memo.Font = SupeyTheme.BodyFont;
                }
                if (ddViolationHost != null) ddViolationHost.BackColor = SupeyTheme.Surface;
                foreach (var chk in _ddViolationChecks)
                    chk.BackColor = SupeyTheme.SurfaceElevated;

                if (layout && ddFormGrid != null && ddScrollBody != null)
                    ddFormGrid.Width = Math.Max(640, ddScrollBody.ClientSize.Width - 36);
            }
            catch { }
        }
    }
}
