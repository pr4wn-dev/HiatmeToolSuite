using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Driver Discipline — fill a single corrective-action form and export a printable Word .docx.
    /// Can be prefilled from the Dashcam Videos selection.
    /// </summary>
    partial class Form1
    {
        // Shared inset so toolbar card and section cards share the same left/right edges.
        private const int DdHostPadX = 10;
        private const int DdCardPadX = 16; // matches MakeDdSectionCard + toolbar button inset
        private const int DdToolbarInnerH = 72; // host height = this + 12
        private const int DdCardGap = 14;
        private const int DdToolbarBtnH = 32;
        private const int DdToolbarBtnGap = 8;

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
        private SupeyMaterialButton ddExportBtn;
        private SupeyMaterialButton ddRefreshHistoryBtn;
        private Panel ddHistoryLoadingHost;
        private TripScoutLiveScanIndicator ddHistoryLoadingScan;
        private Label ddHistoryLoadingLbl;
        private Label ddPriorsLbl;
        private SupeyListView ddHistoryLv;

        private Panel ddBodyHost;
        private Panel ddScrollBody;
        private FlowLayoutPanel ddStack;
        private readonly List<SupeyCard> _ddSectionCards = new List<SupeyCard>();
        private string _ddPriorsDriverKey = "";
        private bool _ddHistoryLoading;
        private int _ddHistoryLoadGen;
        private List<DriverDisciplineIndexItem> _ddHistoryCache = new List<DriverDisciplineIndexItem>();
        private System.Windows.Forms.Timer _ddDriverFilterTimer;
        private bool _ddDriverFilterSuppress;
        private bool _ddLoadingSelectedReport;
        private string _ddLoadedCaseId = "";

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
                    Text = "Status: ready — fill the form, then Save to the library.",
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
            // Host pad matches body host pad so this card lines up with section cards below.
            ddToolbar = new Panel
            {
                Name = "ddToolbar",
                Dock = DockStyle.Top,
                Height = DdToolbarInnerH + 12,
                Padding = new Padding(DdHostPadX, 6, DdHostPadX, 4),
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
            // Absolute Location ignores Padding — Layout uses DisplayRectangle instead.
            ddToolbarInner = new Panel
            {
                Name = "ddToolbarInner",
                Dock = DockStyle.Fill,
                Padding = new Padding(DdCardPadX, 16, DdCardPadX, 16),
                BackColor = SupeyTheme.SurfaceElevated,
            };

            ddGenerateBtn = MakeDdToolbarButton("ddGenerateBtn", "Save", 90,
                SupeyMaterialButton.MaterialButtonType.Contained, accent: true);
            ddGenerateBtn.Click += async (_, __) => await SaveDriverDisciplineToLibraryAsync();

            ddExportBtn = MakeDdToolbarButton("ddExportBtn", "Export copy…", 120,
                SupeyMaterialButton.MaterialButtonType.Outlined);
            ddExportBtn.Click += (_, __) => ExportDriverDisciplineCopy();

            ddFromDashcamBtn = MakeDdToolbarButton("ddFromDashcamBtn", "From Dashcam", 124,
                SupeyMaterialButton.MaterialButtonType.Outlined);
            ddFromDashcamBtn.Click += (_, __) => PrefillDriverDisciplineFromDashcam();

            ddOpenDashcamBtn = MakeDdToolbarButton("ddOpenDashcamBtn", "Open Dashcam", 124,
                SupeyMaterialButton.MaterialButtonType.Outlined);
            ddOpenDashcamBtn.Click += (_, __) =>
            {
                if (tabPageDashcamVideos != null)
                    hiatmeTabControl.SelectedTab = tabPageDashcamVideos;
            };

            ddAddClipsBtn = MakeDdToolbarButton("ddAddClipsBtn", "Add clips…", 112,
                SupeyMaterialButton.MaterialButtonType.Outlined);
            ddAddClipsBtn.Click += (_, __) => AddDriverDisciplineClipsManual();

            ddClearBtn = MakeDdToolbarButton("ddClearBtn", "Clear form", 104,
                SupeyMaterialButton.MaterialButtonType.Text);
            ddClearBtn.Click += (_, __) =>
            {
                if (SupeyMessageDialog.Confirm(
                        this,
                        SupeyMessageDialog.Kind.Warning,
                        "Driver Discipline",
                        "Clear this write-up?",
                        "All fields on the form will be reset. Saved library write-ups are not deleted.",
                        "Clear form",
                        "Keep editing") == DialogResult.Yes)
                {
                    ResetDriverDisciplineForm(seedCase: true);
                    SetDriverDisciplineStatus("Form cleared.");
                    _ = RefreshDriverDisciplinePriorsAsync();
                    _ = RefreshDriverDisciplineHistoryAsync();
                }
            };

            ddToolbarInner.Controls.Add(ddGenerateBtn);
            ddToolbarInner.Controls.Add(ddExportBtn);
            ddToolbarInner.Controls.Add(ddFromDashcamBtn);
            ddToolbarInner.Controls.Add(ddOpenDashcamBtn);
            ddToolbarInner.Controls.Add(ddAddClipsBtn);
            ddToolbarInner.Controls.Add(ddClearBtn);
            ddToolbarInner.Resize += (_, __) =>
            {
                LayoutDriverDisciplineToolbar();
                LayoutDriverDisciplineStack();
            };

            ddToolbarCard.Controls.Add(ddToolbarInner);
            ddToolbar.Controls.Add(ddToolbarCard);
            LayoutDriverDisciplineToolbar();
        }

        private void LayoutDriverDisciplineToolbar()
        {
            if (ddToolbarInner == null) return;

            // Padding does not apply to absolute Location — use DisplayRectangle.
            Rectangle r = ddToolbarInner.DisplayRectangle;
            int y = r.Top + Math.Max(0, (r.Height - DdToolbarBtnH) / 2);
            int x = r.Left;

            void Place(SupeyMaterialButton btn, int width, int gapAfter)
            {
                if (btn == null) return;
                btn.Size = new Size(width, DdToolbarBtnH);
                btn.Location = new Point(x, y);
                x += width + gapAfter;
            }

            Place(ddGenerateBtn, 90, DdToolbarBtnGap);
            Place(ddExportBtn, 120, DdToolbarBtnGap * 2);
            Place(ddFromDashcamBtn, 124, DdToolbarBtnGap);
            Place(ddOpenDashcamBtn, 124, DdToolbarBtnGap);
            Place(ddAddClipsBtn, 112, DdToolbarBtnGap);

            if (ddClearBtn != null)
            {
                ddClearBtn.Size = new Size(104, DdToolbarBtnH);
                ddClearBtn.Location = new Point(r.Right - ddClearBtn.Width, y);
            }
        }

        private static SupeyMaterialButton MakeDdToolbarButton(
            string name, string text, int width, SupeyMaterialButton.MaterialButtonType type, bool accent = false)
        {
            return new SupeyMaterialButton
            {
                Name = name,
                Text = text,
                Type = type,
                UseAccentColor = accent,
                Size = new Size(width, DdToolbarBtnH),
            };
        }

        private void BuildDriverDisciplineBody()
        {
            _ddSectionCards.Clear();

            // Same horizontal host pad as the toolbar so section cards share its left/right edges.
            ddBodyHost = new Panel
            {
                Name = "ddBodyHost",
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.Surface,
                Padding = new Padding(DdHostPadX, 0, DdHostPadX, 8),
            };

            // FlowLayout owns vertical stacking; parent panel scrolls (no extra X pad — host owns inset).
            ddScrollBody = new Panel
            {
                Name = "ddScrollBody",
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = SupeyTheme.Surface,
                Padding = new Padding(0, 8, 0, 16),
            };

            ddStack = new FlowLayoutPanel
            {
                Name = "ddStack",
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = SupeyTheme.Surface,
                Margin = new Padding(0),
                Padding = new Padding(0),
                Location = new Point(0, 0),
            };

            // Fixed-height section cards (no AutoSize) — prevents SupeyCard/Dock overflow.
            const int fieldRow = 58;
            int violCols = 2;
            int violRows = (DriverDisciplineOptions.Violations.Length + violCols - 1) / violCols;
            int violH = violRows * 28 + 4;
            int actionH = 56;

            ddStack.Controls.Add(MakeDdSectionCard(
                "Corrective action write-up",
                "Work top to bottom: who & when → what happened → evidence → action. Save stores to the AI panel library (local cache is optional).",
                BuildDdAccentStripe(),
                contentHeight: 6));

            ddStack.Controls.Add(MakeDdSectionCard(
                "Library history",
                "Saved write-ups (local cache + AI panel). Double-click to open Word · right-click for more.",
                BuildDdHistoryPanel(),
                contentHeight: 200));

            ddStack.Controls.Add(MakeDdSectionCard(
                "1 · Case & notice",
                "Reference for the write-up and who prepared it.",
                BuildDdMetaGrid(fieldRow),
                contentHeight: fieldRow * 2 + 4));

            ddStack.Controls.Add(MakeDdSectionCard(
                "2 · Employee",
                "Driver and supervisor on the notice.",
                BuildDdEmployeeGrid(fieldRow),
                contentHeight: fieldRow * 2 + 28));

            ddStack.Controls.Add(MakeDdSectionCard(
                "3 · Incident",
                "When and where it happened.",
                BuildDdIncidentGrid(fieldRow),
                contentHeight: fieldRow * 3 + 4));

            ddStack.Controls.Add(MakeDdSectionCard(
                "4 · Violation & action",
                "Check all that apply, then pick the action level.",
                BuildDdJudgmentPanel(violH, actionH),
                contentHeight: violH + actionH + 8));

            ddStack.Controls.Add(MakeDdSectionCard(
                "5 · Dashcam evidence",
                "Folder and clips that support this write-up.",
                BuildDdEvidencePanel(fieldRow),
                contentHeight: fieldRow + 150));

            ddStack.Controls.Add(MakeDdSectionCard(
                "6 · What the footage shows",
                "Short summary — prints near the top of the Word form.",
                BuildDdMemoBlock(out ddFootageSummaryTb, "ddFootageSummaryTb"),
                contentHeight: 86));

            ddStack.Controls.Add(MakeDdSectionCard(
                "7 · Investigation notes",
                "Full narrative for the file.",
                BuildDdMemoBlock(out ddNarrativeTb, "ddNarrativeTb"),
                contentHeight: 140));

            ddStack.Controls.Add(MakeDdSectionCard(
                "8 · Policy & history",
                "Cite the rule and any prior coaching or write-ups.",
                BuildDdPolicyPriorPanel(fieldRow),
                contentHeight: 120));

            ddStack.Controls.Add(MakeDdSectionCard(
                "9 · Corrective action",
                "What the driver must do next, and when you’ll review.",
                BuildDdCorrectivePanel(fieldRow),
                contentHeight: 160));

            ddStack.Controls.Add(MakeDdSectionCard(
                "10 · Driver statement (optional)",
                "Leave blank if they’ll write this on the printed copy.",
                BuildDdMemoBlock(out ddDriverStatementTb, "ddDriverStatementTb"),
                contentHeight: 90));

            ddScrollBody.Controls.Add(ddStack);
            ddBodyHost.Controls.Add(ddScrollBody);

            ddScrollBody.Resize += (_, __) => LayoutDriverDisciplineStack();
            LayoutDriverDisciplineStack();

            _ = RefreshDriverDisciplineHistoryAsync();
            _ = RefreshDriverDisciplinePriorsAsync();
        }

        private Panel BuildDdHistoryPanel()
        {
            var host = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.SurfaceElevated,
                Padding = new Padding(0),
            };

            ddRefreshHistoryBtn = new SupeyMaterialButton
            {
                Name = "ddRefreshHistoryBtn",
                Text = "Refresh",
                Type = SupeyMaterialButton.MaterialButtonType.Text,
                Size = new Size(80, 28),
                Dock = DockStyle.Right,
            };
            ddRefreshHistoryBtn.Click += async (_, __) =>
            {
                await RefreshDriverDisciplineHistoryAsync();
                await RefreshDriverDisciplinePriorsAsync();
            };

            ddHistoryLoadingHost = new Panel
            {
                Name = "ddHistoryLoadingHost",
                Dock = DockStyle.Left,
                Width = 154,
                Visible = false,
                BackColor = SupeyTheme.SurfaceElevated,
            };
            ddHistoryLoadingScan = new TripScoutLiveScanIndicator
            {
                Name = "ddHistoryLoadingScan",
                Size = new Size(20, 20),
                Location = new Point(4, 4),
                BackColor = SupeyTheme.SurfaceElevated,
            };
            ddHistoryLoadingLbl = new Label
            {
                Name = "ddHistoryLoadingLbl",
                AutoSize = false,
                Location = new Point(28, 0),
                Size = new Size(122, 28),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = SupeyTheme.CaptionFont,
                ForeColor = SupeyTheme.AccentPrimary,
                BackColor = SupeyTheme.SurfaceElevated,
                Text = "Loading reports…",
            };
            ddHistoryLoadingHost.Controls.Add(ddHistoryLoadingScan);
            ddHistoryLoadingHost.Controls.Add(ddHistoryLoadingLbl);

            var top = new Panel
            {
                Dock = DockStyle.Top,
                Height = 28,
                BackColor = SupeyTheme.SurfaceElevated,
            };
            top.Controls.Add(ddHistoryLoadingHost);
            // Keep Refresh pinned to the right; the loading indicator uses the empty
            // left side of this toolbar so it does not disturb the button.
            top.Controls.Add(ddRefreshHistoryBtn);

            ddHistoryLv = new SupeyListView
            {
                Name = "ddHistoryLv",
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                BorderStyle = BorderStyle.None,
                MultiSelect = false,
                BackColor = SupeyTheme.ListBody,
                ForeColor = SupeyTheme.ListText,
            };
            try { ddHistoryLv.Font = ListViewOwnerDrawFonts.Cell; } catch { }
            ddHistoryLv.Columns.Add("Date", 90);
            ddHistoryLv.Columns.Add("Driver", 140);
            ddHistoryLv.Columns.Add("Action", 140);
            ddHistoryLv.Columns.Add("Case", 130);
            ddHistoryLv.Columns.Add("Violations", 220);
            ddHistoryLv.DrawColumnHeader += listView_DrawColumnHeader;
            ddHistoryLv.DrawItem += listView_DrawItem;
            ddHistoryLv.DrawSubItem += listView_DrawSubItem;
            ListViewHeaderEmptyAreaPainter.Attach(ddHistoryLv);
            SupeyListViewHelpers.EnableDoubleBufferRecursively(ddHistoryLv);
            ddHistoryLv.DoubleClick += async (_, __) => await OpenSelectedDriverDisciplineAsync();
            ddHistoryLv.MouseUp += async (_, e) =>
            {
                if (e.Button == MouseButtons.Left
                    && ddHistoryLv.HitTest(e.Location).Item != null)
                    await LoadSelectedDriverDisciplineAsync();
            };

            var ctx = new ContextMenuStrip
            {
                Renderer = new DarkContextMenuRenderer(),
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
                Font = SupeyTheme.BodyFont,
                ShowImageMargin = true,
                ShowCheckMargin = false,
                Padding = new Padding(2, 4, 2, 4),
            };

            ToolStripMenuItem MakeItem(string text, Image image, EventHandler onClick)
            {
                var item = new ToolStripMenuItem(text)
                {
                    BackColor = DarkContextMenuRenderer.Background,
                    ForeColor = DarkContextMenuRenderer.ForeColor,
                    Image = image,
                    ImageScaling = ToolStripItemImageScaling.None,
                    Padding = new Padding(6, 6, 12, 6),
                    Margin = new Padding(0, 1, 0, 1),
                };
                if (onClick != null) item.Click += onClick;
                return item;
            }

            var openItem = MakeItem("Open Word document", MenuIconFactory.GetOpenDocIcon(),
                async (_, __) => await OpenSelectedDriverDisciplineAsync());
            var loadItem = MakeItem("Load into form", MenuIconFactory.GetLoadFormIcon(),
                async (_, __) => await LoadSelectedDriverDisciplineAsync());
            var filterItem = MakeItem("Show this driver’s write-ups", MenuIconFactory.GetFilterDriverIcon(),
                async (_, __) =>
                {
                    var it = SelectedDriverDisciplineItem();
                    if (it == null || string.IsNullOrWhiteSpace(it.DriverName)) return;
                    if (ddDriverTb != null) ddDriverTb.Text = it.DriverName;
                    await RefreshDriverDisciplineHistoryAsync(it.DriverName);
                    await RefreshDriverDisciplinePriorsAsync();
                });
            var deleteItem = MakeItem("Delete report", MenuIconFactory.GetClearIcon(),
                async (_, __) => await DeleteSelectedDriverDisciplineAsync());

            ctx.Items.Add(openItem);
            ctx.Items.Add(loadItem);
            ctx.Items.Add(new ToolStripSeparator());
            ctx.Items.Add(filterItem);
            ctx.Items.Add(new ToolStripSeparator());
            ctx.Items.Add(deleteItem);
            ctx.Opening += (_, e) =>
            {
                // Select the row under the cursor so right-click without a prior selection still works.
                var pt = ddHistoryLv.PointToClient(Cursor.Position);
                var hit = ddHistoryLv.HitTest(pt);
                if (hit.Item != null)
                {
                    ddHistoryLv.SelectedItems.Clear();
                    hit.Item.Selected = true;
                    hit.Item.Focused = true;
                }
                bool has = SelectedDriverDisciplineItem() != null;
                openItem.Enabled = has;
                loadItem.Enabled = has;
                filterItem.Enabled = has;
                deleteItem.Enabled = has;
                if (!has) e.Cancel = true;
            };
            ddHistoryLv.ContextMenuStrip = ctx;

            host.Controls.Add(ddHistoryLv);
            host.Controls.Add(top);
            return host;
        }

        private void LayoutDriverDisciplineStack()
        {
            if (ddStack == null || ddScrollBody == null) return;

            // Prefer the toolbar card width so left/right edges stay locked together.
            int w = 0;
            if (ddToolbarCard != null && ddToolbarCard.Width > 100)
                w = ddToolbarCard.Width;
            else if (ddBodyHost != null)
                w = ddBodyHost.DisplayRectangle.Width;
            else
                w = ddScrollBody.ClientSize.Width;
            w = Math.Max(640, w);

            ddStack.SuspendLayout();
            try
            {
                ddStack.Width = w;
                foreach (Control c in ddStack.Controls)
                {
                    c.Width = w;
                    c.Margin = new Padding(0, 0, 0, DdCardGap);
                }
            }
            finally { ddStack.ResumeLayout(true); }
        }

        private static Control BuildDdAccentStripe()
        {
            return new Panel
            {
                Dock = DockStyle.Fill,
                Height = 3,
                BackColor = SupeyTheme.AccentStripe,
                Margin = new Padding(0),
            };
        }

        private SupeyCard MakeDdSectionCard(string title, string subtitle, Control content, int contentHeight)
        {
            const int padTop = 12;
            const int padBottom = 14;
            int headerH = string.IsNullOrEmpty(subtitle) ? 26 : 44;
            int cardH = padTop + headerH + 6 + contentHeight + padBottom;

            var card = new SupeyCard
            {
                SurfaceLevel = SupeyCard.Surface.Elevated,
                ShowBorder = true,
                CornerRadius = 8,
                Padding = new Padding(DdCardPadX, padTop, DdCardPadX, padBottom),
                Margin = new Padding(0, 0, 0, DdCardGap),
                AutoSize = false,
                Height = cardH,
                Width = 800,
            };

            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = headerH,
                BackColor = SupeyTheme.SurfaceElevated,
                Padding = new Padding(0, 0, 0, 4),
            };
            var titleLbl = new SupeyLabel
            {
                Text = title ?? "",
                Dock = DockStyle.Top,
                Height = 22,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = SupeyTheme.SubHeaderFont,
                ForeColor = SupeyTheme.TextPrimary,
                BackColor = SupeyTheme.SurfaceElevated,
            };
            if (!string.IsNullOrEmpty(subtitle))
            {
                var subLbl = new SupeyLabel
                {
                    Text = subtitle,
                    Dock = DockStyle.Top,
                    Height = 18,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Font = SupeyTheme.CaptionFont,
                    ForeColor = SupeyTheme.TextMuted,
                    BackColor = SupeyTheme.SurfaceElevated,
                };
                header.Controls.Add(subLbl);
                header.Controls.Add(titleLbl);
            }
            else
            {
                header.Controls.Add(titleLbl);
            }

            content.Dock = DockStyle.Fill;
            content.BackColor = SupeyTheme.SurfaceElevated;

            card.Controls.Add(content);
            card.Controls.Add(header);

            _ddSectionCards.Add(card);
            return card;
        }

        private static SupeyLabel MakeDdFieldCaption(string text)
        {
            return new SupeyLabel
            {
                Text = text,
                Dock = DockStyle.Top,
                Height = 16,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = SupeyTheme.TextMuted,
                Font = SupeyTheme.CaptionFont,
                BackColor = SupeyTheme.SurfaceElevated,
                Margin = new Padding(0),
                Padding = new Padding(0),
            };
        }

        private Panel BuildDdMemoBlock(out TextBox box, string name)
        {
            box = MakeDdMemo(name);
            var host = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.SurfaceElevated,
                Padding = new Padding(0),
            };
            box.Dock = DockStyle.Fill;
            host.Controls.Add(box);
            return host;
        }

        private TableLayoutPanel BuildDdMetaGrid(int rowH)
        {
            var grid = MakeDdTwoColGrid(2, rowH);
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

        private Panel BuildDdEmployeeGrid(int rowH)
        {
            var host = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.SurfaceElevated,
                Padding = new Padding(0),
            };

            var grid = MakeDdTwoColGrid(2, rowH);
            grid.Dock = DockStyle.Top;
            grid.Height = rowH * 2 + 4;

            ddDriverTb = MakeDdText("ddDriverTb", "Driver name");
            ddEmployeeIdTb = MakeDdText("ddEmployeeIdTb", "Employee ID");
            ddVehicleTb = MakeDdText("ddVehicleTb", "Vehicle");
            ddSupervisorTb = MakeDdText("ddSupervisorTb", "Supervisor");
            EnsureDdDriverFilterTimer();
            ddDriverTb.TextChanged += (_, __) => OnDdDriverNameTextChanged();
            ddDriverTb.Leave += async (_, __) => await RefreshDriverDisciplinePriorsAsync();
            ddEmployeeIdTb.Leave += async (_, __) => await RefreshDriverDisciplinePriorsAsync();

            grid.Controls.Add(LabeledDd("Driver name", ddDriverTb), 0, 0);
            grid.Controls.Add(LabeledDd("Employee ID", ddEmployeeIdTb), 1, 0);
            grid.Controls.Add(LabeledDd("Vehicle", ddVehicleTb), 0, 1);
            grid.Controls.Add(LabeledDd("Supervisor", ddSupervisorTb), 1, 1);

            ddPriorsLbl = new Label
            {
                Name = "ddPriorsLbl",
                Dock = DockStyle.Bottom,
                Height = 24,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "No prior write-ups loaded yet.",
                Font = SupeyTheme.CaptionFont,
                ForeColor = SupeyTheme.TextMuted,
                BackColor = SupeyTheme.SurfaceElevated,
                Cursor = Cursors.Hand,
                Padding = new Padding(0, 2, 0, 0),
            };
            ddPriorsLbl.Click += (_, __) => ApplyDdHistoryFilterFromDriverBox();

            host.Controls.Add(grid);
            host.Controls.Add(ddPriorsLbl);
            return host;
        }

        private TableLayoutPanel BuildDdIncidentGrid(int rowH)
        {
            var grid = MakeDdTwoColGrid(3, rowH);
            ddIncidentDate = MakeDdDate("ddIncidentDate");
            ddIncidentTimeTb = MakeDdText("ddIncidentTimeTb", "e.g. 14:35");
            ddTripRefTb = MakeDdText("ddTripRefTb", "Trip / client ref");
            ddLocationTb = MakeDdText("ddLocationTb", "Location / area");

            grid.Controls.Add(LabeledDd("Incident date", ddIncidentDate), 0, 0);
            grid.Controls.Add(LabeledDd("Approximate time", ddIncidentTimeTb), 1, 0);
            grid.Controls.Add(LabeledDd("Trip / client ref", ddTripRefTb), 0, 1);
            var locHost = LabeledDd("Location / area", ddLocationTb);
            grid.SetColumnSpan(locHost, 2);
            grid.Controls.Add(locHost, 0, 2);
            return grid;
        }

        private Panel BuildDdJudgmentPanel(int violH, int actionH)
        {
            var host = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.SurfaceElevated,
                Padding = new Padding(0),
            };

            ddViolationHost = new Panel
            {
                Name = "ddViolationHost",
                Dock = DockStyle.Top,
                Height = violH,
                BackColor = SupeyTheme.SurfaceElevated,
                Padding = new Padding(0),
            };

            var violGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                BackColor = SupeyTheme.SurfaceElevated,
                Margin = new Padding(0),
                Padding = new Padding(0),
            };
            violGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            violGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

            _ddViolationChecks.Clear();
            string[] violations = DriverDisciplineOptions.Violations;
            int rows = (violations.Length + 1) / 2;
            violGrid.RowCount = rows;
            for (int r = 0; r < rows; r++)
                violGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / rows));

            for (int i = 0; i < violations.Length; i++)
            {
                var chk = new SupeyCheckbox
                {
                    Text = violations[i],
                    Checked = false,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0, 1, 8, 1),
                    BackColor = SupeyTheme.SurfaceElevated,
                };
                _ddViolationChecks.Add(chk);
                violGrid.Controls.Add(chk, i % 2, i / 2);
            }
            ddViolationHost.Controls.Add(violGrid);

            ddActionCombo = new SupeyComboBox
            {
                Name = "ddActionCombo",
                Dock = DockStyle.Top,
                UseTallSize = false,
                Height = 36,
                Hint = "Action level",
            };
            foreach (string level in DriverDisciplineOptions.ActionLevels)
                ddActionCombo.Items.Add(level);
            ddActionCombo.SelectedItem = "Written warning";

            var actionHost = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = actionH,
                BackColor = SupeyTheme.SurfaceElevated,
                Padding = new Padding(0, 6, 0, 0),
            };
            var actionCap = MakeDdFieldCaption("Action level");
            actionHost.Controls.Add(ddActionCombo);
            actionHost.Controls.Add(actionCap);

            // Bottom first, then Top — keeps the action strip pinned and violations above it.
            host.Controls.Add(actionHost);
            host.Controls.Add(ddViolationHost);
            return host;
        }

        private Panel BuildDdEvidencePanel(int folderRowH)
        {
            var host = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.SurfaceElevated,
                Padding = new Padding(0),
            };

            ddFolderTb = MakeDdText("ddFolderTb", "Footage folder path");
            var folderBlock = LabeledDd("Footage folder", ddFolderTb);
            folderBlock.Dock = DockStyle.Top;
            folderBlock.Height = folderRowH;

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
                BackColor = SupeyTheme.ListBody,
                ForeColor = SupeyTheme.ListText,
            };
            try { ddClipsLv.Font = ListViewOwnerDrawFonts.Cell; } catch { }
            ddClipsLv.Columns.Add("Clip file", 220);
            ddClipsLv.Columns.Add("Full path", 480);
            ddClipsLv.DrawColumnHeader += listView_DrawColumnHeader;
            ddClipsLv.DrawItem += listView_DrawItem;
            ddClipsLv.DrawSubItem += listView_DrawSubItem;
            ListViewHeaderEmptyAreaPainter.Attach(ddClipsLv);
            SupeyListViewHelpers.EnableDoubleBufferRecursively(ddClipsLv);

            var clipBlock = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.SurfaceElevated,
                Padding = new Padding(0, 4, 0, 0),
            };
            var clipCap = MakeDdFieldCaption("Attached clips");
            var clipFrame = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.ListBody,
                Padding = new Padding(0),
            };
            clipFrame.Controls.Add(ddClipsLv);
            clipBlock.Controls.Add(clipFrame);
            clipBlock.Controls.Add(clipCap);

            host.Controls.Add(clipBlock);
            host.Controls.Add(folderBlock);
            return host;
        }

        private TableLayoutPanel BuildDdPolicyPriorPanel(int policyRowH)
        {
            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = SupeyTheme.SurfaceElevated,
                Margin = new Padding(0),
                Padding = new Padding(0),
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            ddPolicyTb = MakeDdText("ddPolicyTb", "Policy / handbook section");
            var policyCell = LabeledDd("Policy / rule cited", ddPolicyTb);
            policyCell.Padding = new Padding(0, 0, 10, 0);

            var priorHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.SurfaceElevated,
                Padding = new Padding(10, 0, 0, 0),
            };
            var priorCap = MakeDdFieldCaption("Prior related history");
            ddPriorTb = MakeDdMemo("ddPriorTb");
            ddPriorTb.Dock = DockStyle.Fill;
            priorHost.Controls.Add(ddPriorTb);
            priorHost.Controls.Add(priorCap);

            grid.Controls.Add(policyCell, 0, 0);
            grid.Controls.Add(priorHost, 1, 0);
            return grid;
        }

        private Panel BuildDdCorrectivePanel(int followRowH)
        {
            var host = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.SurfaceElevated,
                Padding = new Padding(0),
            };

            ddFollowUpTb = MakeDdText("ddFollowUpTb", "Follow-up / review date");
            var follow = LabeledDd("Follow-up / review date", ddFollowUpTb);
            follow.Dock = DockStyle.Bottom;
            follow.Height = followRowH;

            ddCorrectiveTb = MakeDdMemo("ddCorrectiveTb");
            ddCorrectiveTb.Dock = DockStyle.Fill;
            var memoHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.SurfaceElevated,
                Padding = new Padding(0, 0, 0, 6),
            };
            var cap = MakeDdFieldCaption("Corrective action required");
            memoHost.Controls.Add(ddCorrectiveTb);
            memoHost.Controls.Add(cap);

            host.Controls.Add(memoHost);
            host.Controls.Add(follow);
            return host;
        }

        private static TableLayoutPanel MakeDdTwoColGrid(int rows, int rowH)
        {
            var grid = new TableLayoutPanel
            {
                ColumnCount = 2,
                RowCount = rows,
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.SurfaceElevated,
                Margin = new Padding(0),
                Padding = new Padding(0),
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            for (int i = 0; i < rows; i++)
                grid.RowStyles.Add(new RowStyle(SizeType.Absolute, rowH));
            return grid;
        }

        private static Panel LabeledDd(string label, Control field)
        {
            var host = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(0, 0, 10, 2),
                BackColor = SupeyTheme.SurfaceElevated,
                Margin = new Padding(0),
            };
            var lbl = MakeDdFieldCaption(label);
            field.Dock = DockStyle.Fill;
            if (field is SupeyTextBox tb)
            {
                tb.UseTallSize = false;
                tb.Dock = DockStyle.Top;
                tb.Height = 36;
            }
            else if (field is SupeyComboBox cb)
            {
                cb.UseTallSize = false;
                cb.Dock = DockStyle.Top;
                cb.Height = 36;
            }
            else if (field is RJDatePicker)
            {
                field.Dock = DockStyle.Top;
                field.Height = 32;
                field.Margin = new Padding(0, 2, 0, 0);
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
                UseTallSize = false,
                Height = 36,
            };
        }

        private static RJDatePicker MakeDdDate(string name)
        {
            return new RJDatePicker
            {
                Name = name,
                Format = DateTimePickerFormat.Short,
                Value = DateTime.Today,
                Width = 180,
                Height = 32,
            };
        }

        private static TextBox MakeDdMemo(string name)
        {
            return new TextBox
            {
                Name = name,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                AcceptsReturn = true,
                BorderStyle = BorderStyle.FixedSingle,
                Font = SupeyTheme.BodyFont,
                BackColor = SupeyTheme.ListBody,
                ForeColor = SupeyTheme.TextPrimary,
                Margin = new Padding(0),
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
            _ddDriverFilterSuppress = true;
            try
            {
                if (ddDriverTb != null) ddDriverTb.Text = "";
                if (ddEmployeeIdTb != null) ddEmployeeIdTb.Text = "";
                if (ddVehicleTb != null) ddVehicleTb.Text = "";
                if (ddSupervisorTb != null) ddSupervisorTb.Text = "";
            }
            finally { _ddDriverFilterSuppress = false; }
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
            _ddPriorsDriverKey = "";
            _ddLoadedCaseId = "";
            ApplyDdHistoryFilterFromDriverBox();
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

        private bool ValidateDriverDisciplineRecord(DriverDisciplineRecord record)
        {
            if (record == null) return false;
            if (string.IsNullOrWhiteSpace(record.DriverName))
            {
                SupeyMessageDialog.ShowWarning(this, "Driver Discipline",
                    "Driver name required",
                    "Enter the driver’s name before saving this write-up.");
                ddDriverTb?.Focus();
                return false;
            }
            if (record.Violations == null || record.Violations.Count == 0)
            {
                SupeyMessageDialog.ShowWarning(this, "Driver Discipline",
                    "Select a violation",
                    "Check at least one violation type that applies to this write-up.");
                return false;
            }
            return true;
        }

        private async Task SaveDriverDisciplineToLibraryAsync()
        {
            if (!_ddBuilt)
                InitializeDriverDisciplineTab();

            var record = CollectDriverDisciplineRecord();
            if (!ValidateDriverDisciplineRecord(record))
                return;

            SetDriverDisciplineStatus("Saving write-up to library…");
            try
            {
                byte[] bytes = await Task.Run(() => DriverDisciplineDocument.ToBytes(record))
                    .ConfigureAwait(true);
                var settings = HiatmeAiSettings.Load();
                var result = await DriverDisciplineStore.SaveAndSyncAsync(record, bytes, settings)
                    .ConfigureAwait(true);

                string msg;
                if (result.LocalOk && result.ServerOk)
                    msg = "Saved locally + AI panel"
                        + (string.IsNullOrEmpty(result.ServerPath) ? "" : " · " + result.ServerPath);
                else if (result.ServerOk)
                    msg = "Saved to AI panel"
                        + (string.IsNullOrEmpty(result.Error) ? "" : " · " + result.Error);
                else if (result.LocalOk)
                    msg = "Saved locally (panel sync failed"
                        + (string.IsNullOrEmpty(result.Error) ? ")" : ": " + result.Error + ")");
                else
                {
                    SetDriverDisciplineStatus("Save failed.");
                    SupeyMessageDialog.ShowWarning(this, "Driver Discipline",
                        "Save failed",
                        "Could not save the write-up.",
                        result.Error);
                    return;
                }

                SetDriverDisciplineStatus(msg);
                await RefreshDriverDisciplineHistoryAsync().ConfigureAwait(true);
                await RefreshDriverDisciplinePriorsAsync().ConfigureAwait(true);

                string openPath = null;
                if (!string.IsNullOrEmpty(result.LocalFolder) && result.Meta != null)
                {
                    openPath = Path.Combine(result.LocalFolder, result.Meta.DocxFilename ?? "");
                    if (!File.Exists(openPath)) openPath = null;
                }

                string where = !string.IsNullOrEmpty(result.ServerPath)
                    ? result.ServerPath
                    : (!string.IsNullOrEmpty(result.LocalFolder) ? result.LocalFolder : "AI panel library");
                var ask = SupeyMessageDialog.Ask(
                    this,
                    SupeyMessageDialog.Kind.Success,
                    "Driver Discipline",
                    "Write-up saved",
                    openPath != null
                        ? "It’s in the library and ready to print or attach."
                        : "Saved to the library.",
                    openPath != null ? "Open Word" : "OK",
                    openPath != null ? "Done" : null,
                    details: where);

                if (openPath != null && ask == DialogResult.Yes)
                    Process.Start(new ProcessStartInfo(openPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                SetDriverDisciplineStatus("Save failed.");
                SupeyMessageDialog.ShowWarning(this, "Driver Discipline", "Save failed",
                    "Could not save the write-up.", ex.Message);
            }
        }

        private void ExportDriverDisciplineCopy()
        {
            if (!_ddBuilt)
                InitializeDriverDisciplineTab();

            var record = CollectDriverDisciplineRecord();
            if (!ValidateDriverDisciplineRecord(record))
                return;

            string safeDriver = string.Join("_", record.DriverName.Split(Path.GetInvalidFileNameChars()));
            string suggested = "CorrectiveAction_" + safeDriver + "_" +
                               record.IncidentDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".docx";

            using (var dlg = new SaveFileDialog
            {
                Title = "Export corrective action Word copy",
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
                    SetDriverDisciplineStatus("Exported copy: " + dlg.FileName);
                    if (SupeyMessageDialog.Ask(
                            this,
                            SupeyMessageDialog.Kind.Success,
                            "Driver Discipline",
                            "Copy exported",
                            "A Word copy was saved outside the library.",
                            "Open Word",
                            "Done",
                            details: dlg.FileName) == DialogResult.Yes)
                    {
                        Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
                    }
                }
                catch (Exception ex)
                {
                    SetDriverDisciplineStatus("Export failed.");
                    SupeyMessageDialog.ShowWarning(this, "Driver Discipline", "Export failed",
                        "Could not export the Word document.", ex.Message);
                }
            }
        }

        private async Task RefreshDriverDisciplinePriorsAsync()
        {
            if (ddPriorsLbl == null) return;
            string name = T(ddDriverTb);
            string emp = T(ddEmployeeIdTb);
            string key = name + "|" + emp;
            if (string.IsNullOrWhiteSpace(name))
            {
                ddPriorsLbl.Text = "Enter a driver name to check prior write-ups.";
                ddPriorsLbl.ForeColor = SupeyTheme.TextMuted;
                _ddPriorsDriverKey = "";
                return;
            }

            // Avoid hammering the API for the same driver mid-typing leave cycles
            if (string.Equals(key, _ddPriorsDriverKey, StringComparison.OrdinalIgnoreCase)
                && ddPriorsLbl.Text.IndexOf("prior", StringComparison.OrdinalIgnoreCase) >= 0)
                return;
            _ddPriorsDriverKey = key;

            try
            {
                var settings = HiatmeAiSettings.Load();
                var priors = await HiatmeAiClient.GetDriverDisciplinePriorsAsync(settings, name, emp)
                    .ConfigureAwait(true);
                if (priors != null && priors.Ok && priors.Count > 0)
                {
                    ddPriorsLbl.Text = string.IsNullOrWhiteSpace(priors.Summary)
                        ? priors.Count + " prior write-up(s) on file. Click to filter history."
                        : priors.Summary + " · click to filter history";
                    ddPriorsLbl.ForeColor = SupeyTheme.WarnText;
                    if (ddPriorTb != null
                        && string.IsNullOrWhiteSpace(ddPriorTb.Text)
                        && !string.IsNullOrWhiteSpace(priors.PriorHistoryText))
                    {
                        ddPriorTb.Text = priors.PriorHistoryText;
                    }
                }
                else
                {
                    // Fall back to local index
                    var local = await DriverDisciplineStore.ListMergedAsync(settings, name)
                        .ConfigureAwait(true);
                    if (local != null && local.Count > 0)
                    {
                        var last = local[0];
                        string date = PrettyDdDate(last.IncidentDate ?? last.CreatedAt);
                        ddPriorsLbl.Text = local.Count + " prior write-up(s) · last "
                            + (last.ActionLevel ?? "write-up") + " on " + date
                            + " · click to filter history";
                        ddPriorsLbl.ForeColor = SupeyTheme.WarnText;
                    }
                    else
                    {
                        ddPriorsLbl.Text = "No prior write-ups on file for this driver.";
                        ddPriorsLbl.ForeColor = SupeyTheme.TextMuted;
                    }
                }
            }
            catch
            {
                ddPriorsLbl.Text = "Could not check prior write-ups (panel offline?).";
                ddPriorsLbl.ForeColor = SupeyTheme.TextMuted;
            }
        }

        private static string PrettyDdDate(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "—";
            string s = raw.Trim();
            if (s.Length >= 10 && s[4] == '-' && s[7] == '-')
                return s.Substring(5, 2) + "/" + s.Substring(8, 2) + "/" + s.Substring(0, 4);
            DateTime dt;
            if (DateTime.TryParse(s, out dt))
                return dt.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture);
            return s.Length > 10 ? s.Substring(0, 10) : s;
        }

        private void EnsureDdDriverFilterTimer()
        {
            if (_ddDriverFilterTimer != null) return;
            _ddDriverFilterTimer = new System.Windows.Forms.Timer { Interval = 280 };
            _ddDriverFilterTimer.Tick += async (_, __) =>
            {
                _ddDriverFilterTimer.Stop();
                await RefreshDriverDisciplinePriorsAsync().ConfigureAwait(true);
            };
        }

        private void OnDdDriverNameTextChanged()
        {
            if (_ddDriverFilterSuppress) return;
            ApplyDdHistoryFilterFromDriverBox();
            EnsureDdDriverFilterTimer();
            _ddDriverFilterTimer.Stop();
            _ddDriverFilterTimer.Start();
        }

        /// <summary>
        /// Instantly filter the cached library list to the driver name being typed.
        /// Empty name shows everyone.
        /// </summary>
        private void ApplyDdHistoryFilterFromDriverBox()
        {
            if (ddHistoryLv == null) return;
            BindDdHistoryRows(FilterDdHistoryCache(T(ddDriverTb)));
        }

        private List<DriverDisciplineIndexItem> FilterDdHistoryCache(string driverFilter)
        {
            var src = _ddHistoryCache ?? new List<DriverDisciplineIndexItem>();
            if (string.IsNullOrWhiteSpace(driverFilter))
                return src.OrderByDescending(i => i.CreatedAt ?? "").ToList();
            return src
                .Where(i => DriverDisciplineStore.MatchesDriverFilter(i, driverFilter))
                .OrderByDescending(i => i.CreatedAt ?? "")
                .ToList();
        }

        private void BindDdHistoryRows(IList<DriverDisciplineIndexItem> items)
        {
            if (ddHistoryLv == null) return;
            ddHistoryLv.BeginUpdate();
            try
            {
                ddHistoryLv.Items.Clear();
                foreach (var it in items ?? Array.Empty<DriverDisciplineIndexItem>())
                {
                    if (it == null) continue;
                    var row = new ListViewItem(PrettyDdDate(it.IncidentDate ?? it.CreatedAt));
                    row.SubItems.Add(it.DriverName ?? "");
                    row.SubItems.Add(it.ActionLevel ?? "");
                    row.SubItems.Add(it.CaseNumber ?? it.Id ?? "");
                    string viol = it.Violations != null
                        ? string.Join("; ", it.Violations.Take(3))
                        : "";
                    row.SubItems.Add(viol);
                    row.Tag = it;
                    ddHistoryLv.Items.Add(row);
                }
            }
            finally { ddHistoryLv.EndUpdate(); }
        }

        /// <param name="driverFilter">
        /// Optional display filter. Null = use the Driver name box. Pass "" to show all
        /// regardless of the box (cache still reloads the full library).
        /// </param>
        private async Task RefreshDriverDisciplineHistoryAsync(string driverFilter = null)
        {
            if (ddHistoryLv == null) return;
            int gen = ++_ddHistoryLoadGen;
            _ddHistoryLoading = true;
            SetDriverDisciplineHistoryLoading(true, "Loading reports…");
            try
            {
                var settings = HiatmeAiSettings.Load();
                // Always pull the full library into cache; filter locally so typing stays snappy.
                var items = await DriverDisciplineStore.ListMergedAsync(settings, null)
                    .ConfigureAwait(true);
                if (gen != _ddHistoryLoadGen) return;

                _ddHistoryCache = items ?? new List<DriverDisciplineIndexItem>();
                string filter = driverFilter ?? T(ddDriverTb);
                BindDdHistoryRows(FilterDdHistoryCache(filter));
            }
            finally
            {
                if (gen == _ddHistoryLoadGen)
                {
                    _ddHistoryLoading = false;
                    SetDriverDisciplineHistoryLoading(false);
                }
            }
        }

        private void SetDriverDisciplineHistoryLoading(bool loading, string message = null)
        {
            if (ddHistoryLoadingHost == null || ddHistoryLoadingHost.IsDisposed)
                return;
            if (!string.IsNullOrWhiteSpace(message) && ddHistoryLoadingLbl != null)
                ddHistoryLoadingLbl.Text = message;
            ddHistoryLoadingHost.Visible = loading;
            if (ddHistoryLoadingScan != null && !ddHistoryLoadingScan.IsDisposed)
                ddHistoryLoadingScan.Scanning = loading;
        }

        private DriverDisciplineIndexItem SelectedDriverDisciplineItem()
        {
            if (ddHistoryLv == null || ddHistoryLv.SelectedItems.Count == 0)
                return null;
            return ddHistoryLv.SelectedItems[0].Tag as DriverDisciplineIndexItem;
        }

        private async Task OpenSelectedDriverDisciplineAsync()
        {
            var it = SelectedDriverDisciplineItem();
            if (it == null || string.IsNullOrWhiteSpace(it.Id)) return;

            // Prefer local file
            string localFolder = DriverDisciplineStore.CaseFolder(it.DriverName, it.Id);
            if (Directory.Exists(localFolder))
            {
                var localDocx = Directory.GetFiles(localFolder, "*.docx").FirstOrDefault();
                if (localDocx != null)
                {
                    Process.Start(new ProcessStartInfo(localDocx) { UseShellExecute = true });
                    return;
                }
            }

            try
            {
                var settings = HiatmeAiSettings.Load();
                byte[] bytes = await HiatmeAiClient.DownloadDriverDisciplineDocxAsync(settings, it.Id)
                    .ConfigureAwait(true);
                if (bytes == null || bytes.Length == 0)
                {
                    SupeyMessageDialog.ShowWarning(this, "Driver Discipline",
                        "Couldn’t open Word",
                        "The document wasn’t found locally and the AI panel didn’t return a copy.");
                    return;
                }
                string tmp = Path.Combine(Path.GetTempPath(),
                    (it.CaseNumber ?? it.Id) + ".docx");
                File.WriteAllBytes(tmp, bytes);
                Process.Start(new ProcessStartInfo(tmp) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                SupeyMessageDialog.ShowWarning(this, "Driver Discipline",
                    "Couldn’t open Word",
                    "Something went wrong opening the document.",
                    ex.Message);
            }
        }

        private async Task LoadSelectedDriverDisciplineAsync()
        {
            var it = SelectedDriverDisciplineItem();
            if (it == null || string.IsNullOrWhiteSpace(it.Id)) return;
            if (_ddLoadingSelectedReport
                || string.Equals(_ddLoadedCaseId, it.Id, StringComparison.OrdinalIgnoreCase))
                return;

            DriverDisciplineMeta meta = null;
            _ddLoadingSelectedReport = true;
            SetDriverDisciplineHistoryLoading(true, "Loading report…");
            try
            {
                // The AI panel is authoritative. A local cache may be stale after another
                // dispatcher edits the same report, so only use it when the panel is unavailable.
                var settings = HiatmeAiSettings.Load();
                meta = await HiatmeAiClient.GetDriverDisciplineMetaAsync(settings, it.Id)
                    .ConfigureAwait(true);
            }
            catch { /* fall back to local cache */ }

            if (meta == null)
            {
                string localMeta = Path.Combine(
                    DriverDisciplineStore.CaseFolder(it.DriverName, it.Id), "meta.json");
                try
                {
                    if (File.Exists(localMeta))
                        meta = Newtonsoft.Json.JsonConvert.DeserializeObject<DriverDisciplineMeta>(
                            File.ReadAllText(localMeta));
                }
                catch { /* report the unavailable metadata below */ }
            }
            try
            {
                if (meta == null)
                {
                    SupeyMessageDialog.ShowWarning(this, "Driver Discipline",
                        "Couldn’t load write-up",
                        "Metadata for this case wasn’t found locally or on the AI panel.");
                    return;
                }

                ApplyDriverDisciplineRecord(DriverDisciplineStore.ToRecord(meta));
                _ddLoadedCaseId = meta.Id ?? it.Id;
                SetDriverDisciplineStatus("Loaded " + (meta.CaseNumber ?? meta.Id) + " into the form for review.");
                await RefreshDriverDisciplinePriorsAsync().ConfigureAwait(true);
            }
            finally
            {
                _ddLoadingSelectedReport = false;
                if (!_ddHistoryLoading)
                    SetDriverDisciplineHistoryLoading(false);
            }
        }

        private async Task DeleteSelectedDriverDisciplineAsync()
        {
            var it = SelectedDriverDisciplineItem();
            if (it == null || string.IsNullOrWhiteSpace(it.Id)) return;

            string label = string.IsNullOrWhiteSpace(it.CaseNumber) ? it.Id : it.CaseNumber;
            if (SupeyMessageDialog.Confirm(
                    this,
                    SupeyMessageDialog.Kind.Warning,
                    "Driver Discipline",
                    "Delete " + label + "?",
                    "This permanently deletes the Word document and saved report from the shared library.",
                    "Delete report",
                    "Keep report") != DialogResult.Yes)
                return;

            SetDriverDisciplineStatus("Deleting " + label + "…");
            SetDriverDisciplineHistoryLoading(true, "Deleting report…");
            try
            {
                var result = await DriverDisciplineStore.DeleteAndSyncAsync(
                    it, HiatmeAiSettings.Load()).ConfigureAwait(true);
                if (!result.ServerOk)
                {
                    SetDriverDisciplineStatus("Delete failed.");
                    SupeyMessageDialog.ShowWarning(this, "Driver Discipline", "Delete failed",
                        "The report was not deleted from the shared library.", result.Error);
                    return;
                }

                if (string.Equals(_ddLoadedCaseId, it.Id, StringComparison.OrdinalIgnoreCase))
                    ResetDriverDisciplineForm(seedCase: true);
                await RefreshDriverDisciplineHistoryAsync().ConfigureAwait(true);
                await RefreshDriverDisciplinePriorsAsync().ConfigureAwait(true);
                SetDriverDisciplineStatus(result.LocalOk
                    ? "Deleted " + label + "."
                    : "Deleted " + label + " from AI panel; local cache cleanup failed.");
            }
            catch (Exception ex)
            {
                SetDriverDisciplineStatus("Delete failed.");
                SupeyMessageDialog.ShowWarning(this, "Driver Discipline", "Delete failed",
                    "The report was not deleted from the shared library.", ex.Message);
            }
            finally
            {
                if (!_ddHistoryLoading)
                    SetDriverDisciplineHistoryLoading(false);
            }
        }

        private void ApplyDriverDisciplineRecord(DriverDisciplineRecord r)
        {
            if (r == null) return;
            if (ddCaseTb != null) ddCaseTb.Text = r.CaseNumber ?? "";
            if (ddPreparedTb != null) ddPreparedTb.Text = r.PreparedBy ?? "";
            if (ddDeptTb != null) ddDeptTb.Text = r.Department ?? "Operations";
            if (ddDriverTb != null) ddDriverTb.Text = r.DriverName ?? "";
            if (ddEmployeeIdTb != null) ddEmployeeIdTb.Text = r.EmployeeId ?? "";
            if (ddVehicleTb != null) ddVehicleTb.Text = r.Vehicle ?? "";
            if (ddSupervisorTb != null) ddSupervisorTb.Text = r.SupervisorName ?? "";
            if (ddNoticeDate != null) ddNoticeDate.Value = r.NoticeDate == default ? DateTime.Today : r.NoticeDate;
            if (ddIncidentDate != null) ddIncidentDate.Value = r.IncidentDate == default ? DateTime.Today : r.IncidentDate;
            if (ddIncidentTimeTb != null) ddIncidentTimeTb.Text = r.IncidentTime ?? "";
            if (ddTripRefTb != null) ddTripRefTb.Text = r.TripOrClientRef ?? "";
            if (ddLocationTb != null) ddLocationTb.Text = r.Location ?? "";
            if (ddActionCombo != null && !string.IsNullOrWhiteSpace(r.ActionLevel))
            {
                if (ddActionCombo.Items.Contains(r.ActionLevel))
                    ddActionCombo.SelectedItem = r.ActionLevel;
                else
                {
                    ddActionCombo.Items.Add(r.ActionLevel);
                    ddActionCombo.SelectedItem = r.ActionLevel;
                }
            }
            var set = new HashSet<string>(r.Violations ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            foreach (var chk in _ddViolationChecks)
                chk.Checked = set.Contains(chk.Text);
            if (ddFootageSummaryTb != null) ddFootageSummaryTb.Text = r.FootageSummary ?? "";
            if (ddNarrativeTb != null) ddNarrativeTb.Text = r.Narrative ?? "";
            if (ddPolicyTb != null) ddPolicyTb.Text = r.PolicyCited ?? "";
            if (ddPriorTb != null) ddPriorTb.Text = r.PriorHistory ?? "";
            if (ddCorrectiveTb != null) ddCorrectiveTb.Text = r.CorrectiveAction ?? "";
            if (ddFollowUpTb != null) ddFollowUpTb.Text = r.FollowUpDate ?? "";
            if (ddDriverStatementTb != null) ddDriverStatementTb.Text = r.DriverStatement ?? "";
            if (ddFolderTb != null) ddFolderTb.Text = r.FootageFolder ?? "";
            _ddClipPaths.Clear();
            if (r.ClipPaths != null) _ddClipPaths.AddRange(r.ClipPaths);
            RefreshDriverDisciplineClipList();
        }

        /// <summary>Prefill from the currently selected Dashcam Videos driver (and optional issue row).</summary>
        internal void PrefillDriverDisciplineFromDashcam()
        {
            if (!_ddBuilt)
                InitializeDriverDisciplineTab();

            if (_dcSelected == null)
            {
                if (!_dcBuilt)
                    InitializeDashcamVideosTab();
            }

            var driver = _dcSelected;
            if (driver == null)
            {
                SupeyMessageDialog.ShowInfo(this, "Driver Discipline",
                    "No Dashcam selection",
                    "Select a driver on the Dashcam Videos tab first, then use From Dashcam.");
                if (tabPageDashcamVideos != null)
                    hiatmeTabControl.SelectedTab = tabPageDashcamVideos;
                return;
            }

            if (ddDriverTb != null) ddDriverTb.Text = driver.Driver ?? "";
            if (ddFolderTb != null) ddFolderTb.Text = driver.FolderPath ?? "";

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
            _ddPriorsDriverKey = "";
            _ = RefreshDriverDisciplinePriorsAsync();
            _ = RefreshDriverDisciplineHistoryAsync(driver.Driver);
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
                if (ddStack != null) ddStack.BackColor = SupeyTheme.Surface;

                foreach (var card in _ddSectionCards)
                {
                    if (card == null) continue;
                    card.SurfaceLevel = SupeyCard.Surface.Elevated;
                    card.BackColor = SupeyTheme.SurfaceElevated;
                    card.ShowBorder = true;
                }

                foreach (var memo in new[] { ddFootageSummaryTb, ddNarrativeTb, ddPriorTb, ddCorrectiveTb, ddDriverStatementTb })
                {
                    if (memo == null) continue;
                    memo.BackColor = SupeyTheme.ListBody;
                    memo.ForeColor = SupeyTheme.TextPrimary;
                    memo.Font = SupeyTheme.BodyFont;
                }
                if (ddViolationHost != null) ddViolationHost.BackColor = SupeyTheme.SurfaceElevated;
                foreach (var chk in _ddViolationChecks)
                    chk.BackColor = SupeyTheme.SurfaceElevated;
                if (ddPriorsLbl != null)
                {
                    ddPriorsLbl.BackColor = SupeyTheme.SurfaceElevated;
                    ddPriorsLbl.Font = SupeyTheme.CaptionFont;
                }
                if (ddHistoryLv != null)
                {
                    ddHistoryLv.BackColor = SupeyTheme.ListBody;
                    ddHistoryLv.ForeColor = SupeyTheme.ListText;
                }

                if (layout)
                    LayoutDriverDisciplineStack();
            }
            catch { }
        }
    }
}
