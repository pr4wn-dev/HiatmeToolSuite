using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Dashcam Videos — tracks each driver's offloaded footage, flags missing clips / lost days
    /// from the SD-card sequence counter, and warns when a chip is about to loop-overwrite itself.
    /// </summary>
    partial class Form1
    {
        // tabPageDashcamVideos is declared in Form1.Designer.cs.
        private SupeyCard dcMainCard;
        private SupeyCard dcStatusCard;
        private SupeyLabel dcStatusLbl;
        private Panel dcToolbar;
        private SupeyCard dcToolbarCard;
        private Panel dcToolbarInner;
        private SupeyMaterialButton dcScanBtn;
        private SupeyCheckbox dcProblemsOnlyChk;
        private SupeyLabel dcRootLbl;
        private Panel dcBodyHost;
        private Panel dcListHost;
        private Panel dcDivider;
        private SupeyListView dcDriverLv;
        private Panel dcDetailHost;
        private SupeyLabel dcDetailSummary;
        private Panel dcDetailBtnRow;
        private SupeyMaterialButton dcCap256Btn;
        private SupeyMaterialButton dcCap512Btn;
        private SupeyMaterialButton dcIgnoreGapBtn;
        private SupeyMaterialButton dcOpenFolderBtn;
        private SupeyMaterialButton dcWriteUpBtn;
        private SupeyListView dcIssuesLv;

        private const int DashcamToolbarH = 48;
        private const int DashcamListWidth = 720;

        private bool _dcBuilt;
        private bool _dcFirstLoadDone;
        private bool _dcScanInFlight;
        private CancellationTokenSource _dcScanCts;
        private DashcamVideoStore.Data _dcSettings;
        private DashcamVideoLibrary.ScanResult _dcResult;
        private DashcamVideoLibrary.DriverReport _dcSelected;

        private void InitializeDashcamVideosTab()
        {
            if (_dcBuilt || hiatmeTabControl == null || tabPageDashcamVideos == null)
                return;

            try
            {
                tabPageDashcamVideos.SuspendLayout();
                tabPageDashcamVideos.Controls.Clear();
                dcMainCard = null;
                dcStatusCard = null;

                if (tabImageList != null && tabImageList.Images.ContainsKey("cctv-custom.png"))
                    tabPageDashcamVideos.ImageKey = "cctv-custom.png";

                tabPageDashcamVideos.Text = "Dashcam Videos";
                tabPageDashcamVideos.BackColor = SupeyTheme.SurfaceBase;
                tabPageDashcamVideos.ForeColor = SupeyTheme.TextPrimary;
                tabPageDashcamVideos.Padding = new Padding(ToolTabInset);

                _dcSettings = DashcamVideoStore.Load();

                // Status strip — dock BOTTOM so it always gets a real height.
                dcStatusCard = new SupeyCard
                {
                    Name = "dcStatusCard",
                    Dock = DockStyle.Bottom,
                    Height = ToolTabStatusH,
                    SurfaceLevel = SupeyCard.Surface.StatusBar,
                    ShowBorder = true,
                    CornerRadius = 6,
                };
                dcStatusLbl = new SupeyLabel
                {
                    Name = "dcStatusLbl",
                    Text = "Status: ready — press Scan Now (or wait for auto-scan).",
                    AutoSize = false,
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(10, 0, 10, 0),
                    ForeColor = SupeyTheme.TextSecondary,
                    Font = SupeyTheme.BodyFont,
                    BackColor = SupeyTheme.SurfaceStatusBar,
                };
                dcStatusCard.Controls.Add(dcStatusLbl);

                // Main card — dock FILL.
                dcMainCard = new SupeyCard
                {
                    Name = "dcMainCard",
                    Dock = DockStyle.Fill,
                    SurfaceLevel = SupeyCard.Surface.Standard,
                    ShowBorder = true,
                    CornerRadius = 8,
                    Margin = new Padding(0, 0, 0, ToolTabGap),
                };

                BuildDashcamToolbar();
                BuildDashcamBody();

                // Dock order inside main: Fill body first, then Top toolbar (last Top wins).
                dcMainCard.Controls.Add(dcBodyHost);
                dcMainCard.Controls.Add(dcToolbar);

                // Dock order on tab: Fill main first, then Bottom status (last Bottom wins).
                tabPageDashcamVideos.Controls.Add(dcMainCard);
                tabPageDashcamVideos.Controls.Add(dcStatusCard);

                tabPageDashcamVideos.VisibleChanged -= DashcamTab_VisibleChanged;
                tabPageDashcamVideos.VisibleChanged += DashcamTab_VisibleChanged;
                tabPageDashcamVideos.Resize -= DashcamTab_Resize;
                tabPageDashcamVideos.Resize += DashcamTab_Resize;

                ApplyDashcamVisualTheme(layout: true);
                _dcBuilt = true;
                tabPageDashcamVideos.ResumeLayout(true);
                LayoutDashcamTabPanels();
            }
            catch (Exception ex)
            {
                _dcBuilt = false;
                try { tabPageDashcamVideos.ResumeLayout(true); } catch { }
                try
                {
                    tabPageDashcamVideos.Controls.Clear();
                    tabPageDashcamVideos.Controls.Add(new Label
                    {
                        Dock = DockStyle.Fill,
                        TextAlign = ContentAlignment.MiddleCenter,
                        BackColor = Color.FromArgb(40, 20, 20),
                        ForeColor = Color.OrangeRed,
                        Font = new Font("Segoe UI", 11f),
                        Text = "Dashcam Videos failed to build:\r\n\r\n" + ex.Message + "\r\n\r\n" + ex.GetType().Name,
                    });
                }
                catch { }
                try
                {
                    MessageBox.Show(
                        "Dashcam Videos tab failed to build:\r\n\r\n" + ex,
                        "Dashcam Videos",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
                catch { }
            }
        }

        private void DashcamTab_VisibleChanged(object sender, EventArgs e)
        {
            if (tabPageDashcamVideos == null || !tabPageDashcamVideos.Visible) return;
            if (!_dcBuilt)
                InitializeDashcamVideosTab();
            LayoutDashcamTabPanels();
            EnsureDashcamFirstScan();
        }

        private void DashcamTab_Resize(object sender, EventArgs e)
        {
            if (_dcBuilt) LayoutDashcamTabPanels();
        }

        private void EnsureDashcamFirstScan()
        {
            if (_dcFirstLoadDone || !_dcBuilt) return;
            _dcFirstLoadDone = true;
            // Let the UI paint before the heavy scan starts.
            try
            {
                BeginInvoke(new Action(() => _ = DashcamScanAsync()));
            }
            catch
            {
                _ = DashcamScanAsync();
            }
        }

        private void BuildDashcamToolbar()
        {
            // Same chrome pattern as Driver Habits / Billing: host pad → elevated SupeyCard →
            // opaque inner surface (SupeyCheckbox cannot parent onto Transparent).
            dcToolbar = new Panel
            {
                Name = "dcToolbar",
                Dock = DockStyle.Top,
                Height = DashcamToolbarH + 12,
                Padding = new Padding(10, 6, 10, 4),
                BackColor = SupeyTheme.Surface,
            };
            dcToolbarCard = new SupeyCard
            {
                Name = "dcToolbarCard",
                Dock = DockStyle.Fill,
                SurfaceLevel = SupeyCard.Surface.Elevated,
                ShowBorder = true,
                CornerRadius = 8,
                Padding = Padding.Empty,
            };
            dcToolbarInner = new Panel
            {
                Name = "dcToolbarInner",
                Dock = DockStyle.Fill,
                Padding = new Padding(12, 8, 12, 8),
                BackColor = SupeyTheme.SurfaceElevated,
            };

            dcScanBtn = new SupeyMaterialButton
            {
                Name = "dcScanBtn",
                Text = "Scan Now",
                Type = SupeyMaterialButton.MaterialButtonType.Contained,
                UseAccentColor = true,
                Size = new Size(104, 32),
                Location = new Point(0, 2),
            };
            dcScanBtn.Click += (_, __) => _ = DashcamScanAsync();

            dcProblemsOnlyChk = new SupeyCheckbox
            {
                Name = "dcProblemsOnlyChk",
                Text = "Only show problems",
                Size = new Size(170, 24),
                Location = new Point(118, 6),
                Checked = false,
                BackColor = SupeyTheme.SurfaceElevated,
            };
            dcProblemsOnlyChk.CheckedChanged += (_, __) => RenderDashcamDriverList();

            dcRootLbl = new SupeyLabel
            {
                Name = "dcRootLbl",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleRight,
                Dock = DockStyle.Right,
                Width = 480,
                BackColor = SupeyTheme.SurfaceElevated,
                ForeColor = SupeyTheme.TextMuted,
                Font = SupeyTheme.CaptionFont,
                Text = DashcamVideoLibrary.DefaultRoot,
            };

            dcToolbarInner.Controls.Add(dcRootLbl);
            dcToolbarInner.Controls.Add(dcProblemsOnlyChk);
            dcToolbarInner.Controls.Add(dcScanBtn);
            dcToolbarCard.Controls.Add(dcToolbarInner);
            dcToolbar.Controls.Add(dcToolbarCard);
        }

        private void BuildDashcamBody()
        {
            dcBodyHost = new Panel
            {
                Name = "dcBodyHost",
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.Surface,
            };

            dcListHost = new Panel
            {
                Name = "dcListHost",
                Dock = DockStyle.Left,
                Width = DashcamListWidth,
                BackColor = SupeyTheme.Surface,
            };
            dcDivider = new Panel
            {
                Name = "dcDivider",
                Dock = DockStyle.Left,
                Width = 1,
                BackColor = SupeyTheme.Divider,
            };

            dcDriverLv = CreateDashcamListView("dcDriverLv");
            dcDriverLv.Columns.Add("Driver", 140);
            dcDriverLv.Columns.Add("Backup status", 120);
            dcDriverLv.Columns.Add("Chip", 60);
            dcDriverLv.Columns.Add("Runway", 100);
            dcDriverLv.Columns.Add("Last offload", 90);
            dcDriverLv.Columns.Add("Days", 48);
            dcDriverLv.Columns.Add("Miss clips", 70);
            dcDriverLv.Columns.Add("Lost days", 66);
            dcDriverLv.Columns.Add("No pair", 58);
            dcDriverLv.Columns.Add("Total", 72);
            dcDriverLv.SelectedIndexChanged += (_, __) => OnDashcamDriverSelected();
            dcDriverLv.DoubleClick += (_, __) => OpenSelectedDashcamFolder();
            dcListHost.Controls.Add(dcDriverLv);

            dcDetailHost = new Panel
            {
                Name = "dcDetailHost",
                Dock = DockStyle.Fill,
                Padding = new Padding(12),
                BackColor = SupeyTheme.Surface,
            };

            dcDetailSummary = new SupeyLabel
            {
                Name = "dcDetailSummary",
                Dock = DockStyle.Top,
                Height = 150,
                AutoSize = false,
                TextAlign = ContentAlignment.TopLeft,
                ForeColor = SupeyTheme.TextPrimary,
                Font = SupeyTheme.BodyFont,
                BackColor = SupeyTheme.Surface,
                Text = "Select a driver to see missing footage and backup timing.\r\n\r\nPress Scan Now if the list is empty.",
            };

            dcDetailBtnRow = new Panel
            {
                Name = "dcDetailBtnRow",
                Dock = DockStyle.Top,
                Height = 78,
                Padding = new Padding(0, 4, 0, 6),
                BackColor = SupeyTheme.Surface,
            };
            dcCap256Btn = MakeDashcamButton("dcCap256Btn", "Chip: 256 GB", 108);
            dcCap512Btn = MakeDashcamButton("dcCap512Btn", "Chip: 512 GB", 108);
            dcIgnoreGapBtn = MakeDashcamButton("dcIgnoreGapBtn", "Ignore selected gap", 148);
            dcOpenFolderBtn = MakeDashcamButton("dcOpenFolderBtn", "Open folder", 104);
            dcWriteUpBtn = new SupeyMaterialButton
            {
                Name = "dcWriteUpBtn",
                Text = "Create discipline write-up",
                Type = SupeyMaterialButton.MaterialButtonType.Contained,
                UseAccentColor = true,
                Size = new Size(200, 30),
            };
            dcCap256Btn.Location = new Point(0, 6);
            dcCap512Btn.Location = new Point(116, 6);
            dcIgnoreGapBtn.Location = new Point(232, 6);
            dcOpenFolderBtn.Location = new Point(388, 6);
            dcWriteUpBtn.Location = new Point(0, 42);
            dcCap256Btn.Click += (_, __) => SetSelectedDashcamCapacity(256);
            dcCap512Btn.Click += (_, __) => SetSelectedDashcamCapacity(512);
            dcIgnoreGapBtn.Click += (_, __) => IgnoreSelectedDashcamGap();
            dcOpenFolderBtn.Click += (_, __) => OpenSelectedDashcamFolder();
            dcWriteUpBtn.Click += (_, __) => PrefillDriverDisciplineFromDashcam();
            dcDetailBtnRow.Controls.Add(dcCap256Btn);
            dcDetailBtnRow.Controls.Add(dcCap512Btn);
            dcDetailBtnRow.Controls.Add(dcIgnoreGapBtn);
            dcDetailBtnRow.Controls.Add(dcOpenFolderBtn);
            dcDetailBtnRow.Controls.Add(dcWriteUpBtn);

            dcIssuesLv = CreateDashcamListView("dcIssuesLv");
            dcIssuesLv.Columns.Add("Issue", 130);
            dcIssuesLv.Columns.Add("When", 150);
            dcIssuesLv.Columns.Add("Detail", 360);

            // Detail dock: Fill issues first, then Top buttons, then Top summary.
            dcDetailHost.Controls.Add(dcIssuesLv);
            dcDetailHost.Controls.Add(dcDetailBtnRow);
            dcDetailHost.Controls.Add(dcDetailSummary);

            // Body dock: Fill detail first, then Left divider, then Left list.
            dcBodyHost.Controls.Add(dcDetailHost);
            dcBodyHost.Controls.Add(dcDivider);
            dcBodyHost.Controls.Add(dcListHost);
        }

        private SupeyMaterialButton MakeDashcamButton(string name, string text, int width)
        {
            return new SupeyMaterialButton
            {
                Name = name,
                Text = text,
                Type = SupeyMaterialButton.MaterialButtonType.Outlined,
                Size = new Size(width, 30),
            };
        }

        private SupeyListView CreateDashcamListView(string name)
        {
            var lv = new SupeyListView
            {
                Name = name,
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                HeaderStyle = ColumnHeaderStyle.Clickable,
                BorderStyle = BorderStyle.None,
                MultiSelect = false,
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.ListBody,
                ForeColor = SupeyTheme.ListText,
            };
            try { lv.Font = ListViewOwnerDrawFonts.Cell; } catch { }
            lv.DrawColumnHeader += listView_DrawColumnHeader;
            lv.DrawItem += listView_DrawItem;
            lv.DrawSubItem += listView_DrawSubItem;
            ListViewSorter.Attach(lv);
            ListViewHeaderEmptyAreaPainter.Attach(lv);
            SupeyListViewHelpers.EnableDoubleBufferRecursively(lv);
            return lv;
        }

        // ── Scan ──────────────────────────────────────────────────────────

        private async Task DashcamScanAsync()
        {
            if (_dcScanInFlight) return;
            if (!_dcBuilt) InitializeDashcamVideosTab();
            if (!_dcBuilt) return;

            _dcScanInFlight = true;
            if (dcScanBtn != null) dcScanBtn.Enabled = false;

            _dcScanCts?.Dispose();
            _dcScanCts = new CancellationTokenSource();
            var token = _dcScanCts.Token;
            var settings = _dcSettings ?? (_dcSettings = DashcamVideoStore.Load());
            string root = string.IsNullOrWhiteSpace(settings.Root) ? DashcamVideoLibrary.DefaultRoot : settings.Root;

            SetDashcamStatus("Scanning " + root + " …");

            try
            {
                var result = await Task.Run(() => DashcamVideoLibrary.Scan(
                    root, settings,
                    (driver, i, total) =>
                    {
                        if (driver == null) return;
                        try
                        {
                            BeginInvoke(new Action(() =>
                                SetDashcamStatus($"Scanning {driver} ({i + 1}/{total}) …")));
                        }
                        catch { }
                    },
                    token), token);

                _dcResult = result;
                RenderDashcamDriverList();

                if (!string.IsNullOrEmpty(result.Warning))
                    SetDashcamStatus("Warning: " + result.Warning);
                else
                    SetDashcamStatus(DashcamHeadlineSummary(result));
            }
            catch (OperationCanceledException)
            {
                SetDashcamStatus("Scan cancelled.");
            }
            catch (Exception ex)
            {
                SetDashcamStatus("Scan failed: " + ex.Message);
                try
                {
                    MessageBox.Show(
                        "Dashcam scan failed:\r\n\r\n" + ex.Message,
                        "Dashcam Videos",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
                catch { }
            }
            finally
            {
                _dcScanInFlight = false;
                if (dcScanBtn != null && !dcScanBtn.IsDisposed) dcScanBtn.Enabled = true;
            }
        }

        private static string DashcamHeadlineSummary(DashcamVideoLibrary.ScanResult r)
        {
            if (r == null || r.Drivers.Count == 0) return "No footage found.";
            int overdue = r.Drivers.Count(d => d.Status == DashcamVideoLibrary.BackupStatus.Overdue);
            int soon = r.Drivers.Count(d => d.Status == DashcamVideoLibrary.BackupStatus.Soon);
            int missing = r.Drivers.Sum(d => d.MissingClipCount);
            int lost = r.Drivers.Sum(d => d.LostDays.Count);
            return string.Format(CultureInfo.CurrentCulture,
                "Scanned {0} drivers at {1:t}.  {2} OVERDUE, {3} back up soon.  {4} missing clips, {5} lost days flagged.",
                r.Drivers.Count, r.ScannedAtLocal, overdue, soon, missing, lost);
        }

        // ── Driver list ───────────────────────────────────────────────────

        private void RenderDashcamDriverList()
        {
            if (dcDriverLv == null || dcDriverLv.IsDisposed) return;
            if (dcRootLbl != null && _dcResult != null)
                dcRootLbl.Text = _dcResult.Root + "   ·   scanned " + _dcResult.ScannedAtLocal.ToString("g", CultureInfo.CurrentCulture);

            dcDriverLv.BeginUpdate();
            try
            {
                dcDriverLv.Items.Clear();
                if (_dcResult == null) return;

                bool onlyProblems = dcProblemsOnlyChk != null && dcProblemsOnlyChk.Checked;
                var rows = _dcResult.Drivers.AsEnumerable();
                if (onlyProblems) rows = rows.Where(d => d.HasProblems);

                rows = rows
                    .OrderBy(d => StatusRank(d.Status))
                    .ThenByDescending(d => d.MissingClipCount + d.LostDays.Count)
                    .ThenBy(d => d.Driver, StringComparer.OrdinalIgnoreCase);

                foreach (var d in rows)
                {
                    var item = new ListViewItem(d.Driver) { Tag = d };
                    item.SubItems.Add(StatusText(d.Status));
                    item.SubItems.Add(d.CapacityGb + (d.CapacityIsGuessed ? "?" : "") + " GB");
                    item.SubItems.Add(RunwayText(d));
                    item.SubItems.Add(d.LastClip.ToString("MM/dd/yy", CultureInfo.InvariantCulture));
                    item.SubItems.Add(d.DaysSinceOffload.ToString(CultureInfo.InvariantCulture));
                    item.SubItems.Add(d.MissingClipCount > 0 ? d.MissingClipCount.ToString(CultureInfo.InvariantCulture) : "—");
                    item.SubItems.Add(d.LostDays.Count > 0 ? d.LostDays.Count.ToString(CultureInfo.InvariantCulture) : "—");
                    item.SubItems.Add(d.ChannelMismatchCount > 0 ? d.ChannelMismatchCount.ToString(CultureInfo.InvariantCulture) : "—");
                    item.SubItems.Add(FormatSize(d.TotalGb));
                    dcDriverLv.Items.Add(item);
                }
            }
            finally { dcDriverLv.EndUpdate(); }
        }

        private static int StatusRank(DashcamVideoLibrary.BackupStatus s)
        {
            switch (s)
            {
                case DashcamVideoLibrary.BackupStatus.Overdue: return 0;
                case DashcamVideoLibrary.BackupStatus.Soon: return 1;
                case DashcamVideoLibrary.BackupStatus.Ok: return 2;
                case DashcamVideoLibrary.BackupStatus.Unknown: return 3;
                default: return 4;
            }
        }

        private static string StatusText(DashcamVideoLibrary.BackupStatus s)
        {
            switch (s)
            {
                case DashcamVideoLibrary.BackupStatus.Overdue: return "OVERDUE";
                case DashcamVideoLibrary.BackupStatus.Soon: return "Back up soon";
                case DashcamVideoLibrary.BackupStatus.Ok: return "OK";
                case DashcamVideoLibrary.BackupStatus.Archived: return "Archived";
                default: return "Unknown";
            }
        }

        private static string RunwayText(DashcamVideoLibrary.DriverReport d)
        {
            if (d.Status == DashcamVideoLibrary.BackupStatus.Archived) return "—";
            if (double.IsNaN(d.DaysUntilOverwrite)) return "?";
            if (d.DaysUntilOverwrite < 0)
                return string.Format(CultureInfo.InvariantCulture, "over by ~{0:0}d", Math.Abs(d.DaysUntilOverwrite));
            return string.Format(CultureInfo.InvariantCulture, "~{0:0} work-days", d.DaysUntilOverwrite);
        }

        private static string FormatSize(double gb)
        {
            if (gb >= 1024) return string.Format(CultureInfo.InvariantCulture, "{0:0.0} TB", gb / 1024.0);
            return string.Format(CultureInfo.InvariantCulture, "{0:0} GB", gb);
        }

        // ── Detail panel ──────────────────────────────────────────────────

        private void OnDashcamDriverSelected()
        {
            if (dcDriverLv == null || dcDriverLv.SelectedItems.Count == 0)
            {
                _dcSelected = null;
                return;
            }
            _dcSelected = dcDriverLv.SelectedItems[0].Tag as DashcamVideoLibrary.DriverReport;
            RenderDashcamDetail();
        }

        private void RenderDashcamDetail()
        {
            var d = _dcSelected;
            if (d == null || dcDetailSummary == null) return;

            string capNote = d.CapacityIsGuessed ? "assumed" : "set by you";
            string runway = d.Status == DashcamVideoLibrary.BackupStatus.Archived
                ? "parked / archived — no active overwrite risk"
                : double.IsNaN(d.DaysUntilOverwrite)
                    ? "not enough data to estimate"
                    : d.DaysUntilOverwrite < 0
                        ? string.Format(CultureInfo.InvariantCulture, "already overwriting — was full ~{0:0} work-days ago", Math.Abs(d.DaysUntilOverwrite))
                        : string.Format(CultureInfo.InvariantCulture, "about {0:0} work-days before the card starts overwriting", d.DaysUntilOverwrite);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(d.Driver + "   [" + StatusText(d.Status) + "]");
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "Chip {0} GB ({1}) · fill ~{2:0.0} GB/hr · ~{3:0.0} min clips · ~{4:0.0} hrs/active day",
                d.CapacityGb, capNote, d.FillGbPerHour, d.ClipMinutes, d.AvgDailyHours));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "Last offload {0:MM/dd/yyyy} ({1} days ago) · ~{2:0}/{3:0} card-hours used since → {4}",
                d.LastClip, d.DaysSinceOffload, d.HoursSinceOffload, d.CapacityHours, runway));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "Library: {0:n0} clips · {1} · {2:MM/dd/yyyy}–{3:MM/dd/yyyy} · {4} active days",
                d.ClipInstants, FormatSize(d.TotalGb), d.FirstClip, d.LastClip, d.ActiveDays));
            sb.Append(string.Format(CultureInfo.InvariantCulture,
                "Flags: {0} missing-clip gaps ({1} clips) · {2} lost days · {3} unpaired · {4} card/van boundaries",
                d.SeqGaps.Count, d.MissingClipCount, d.LostDays.Count, d.ChannelMismatchCount, d.LargeJumpCount));
            dcDetailSummary.Text = sb.ToString();

            dcIssuesLv.BeginUpdate();
            try
            {
                dcIssuesLv.Items.Clear();
                foreach (var g in d.SeqGaps.OrderByDescending(x => x.MissingCount))
                {
                    var it = new ListViewItem("Missing clips") { Tag = g };
                    it.SubItems.Add(g.TimeBefore.ToString("MM/dd/yy HH:mm", CultureInfo.InvariantCulture));
                    it.SubItems.Add(string.Format(CultureInfo.InvariantCulture,
                        "{0} clip(s) gone — seq {1}→{2}",
                        g.MissingCount, g.LastSeqBefore, g.NextSeqAfter));
                    dcIssuesLv.Items.Add(it);
                }
                foreach (var day in d.LostDays)
                {
                    var it = new ListViewItem("Lost day") { Tag = day };
                    it.SubItems.Add(day.ToString("ddd MM/dd/yy", CultureInfo.InvariantCulture));
                    it.SubItems.Add("No footage this day, but days on either side have footage.");
                    dcIssuesLv.Items.Add(it);
                }
                foreach (var lg in d.LongGaps)
                {
                    var it = new ListViewItem("Coverage gap");
                    it.SubItems.Add(lg.Item1.ToString("MM/dd/yy", CultureInfo.InvariantCulture) + "–" + lg.Item2.ToString("MM/dd/yy", CultureInfo.InvariantCulture));
                    it.SubItems.Add(string.Format(CultureInfo.InvariantCulture,
                        "{0} days with no footage (likely time off — verify).", (int)(lg.Item2 - lg.Item1).TotalDays - 1));
                    dcIssuesLv.Items.Add(it);
                }
                if (d.FrontOnlyCount > 0)
                {
                    var it = new ListViewItem("Front-only clips");
                    it.SubItems.Add("—");
                    it.SubItems.Add(d.FrontOnlyCount + " clip(s) have a front angle but no rear file.");
                    dcIssuesLv.Items.Add(it);
                }
                if (d.RearOnlyCount > 0)
                {
                    var it = new ListViewItem("Rear-only clips");
                    it.SubItems.Add("—");
                    it.SubItems.Add(d.RearOnlyCount + " clip(s) have a rear angle but no front file.");
                    dcIssuesLv.Items.Add(it);
                }
                if (dcIssuesLv.Items.Count == 0)
                {
                    var it = new ListViewItem("No issues");
                    it.SubItems.Add("—");
                    it.SubItems.Add("No missing clips or lost days detected for this driver.");
                    dcIssuesLv.Items.Add(it);
                }
            }
            finally { dcIssuesLv.EndUpdate(); }

            UpdateDashcamCapacityButtons();
        }

        private void UpdateDashcamCapacityButtons()
        {
            if (_dcSelected == null) return;
            bool is256 = _dcSelected.CapacityGb == 256;
            if (dcCap256Btn != null) dcCap256Btn.UseAccentColor = is256;
            if (dcCap512Btn != null) dcCap512Btn.UseAccentColor = !is256 && _dcSelected.CapacityGb == 512;
            try { dcCap256Btn?.Invalidate(); dcCap512Btn?.Invalidate(); } catch { }
        }

        private void SetSelectedDashcamCapacity(int gb)
        {
            if (_dcSelected == null || _dcSettings == null) return;
            _dcSettings.SetCapacity(_dcSelected.Driver, gb);
            DashcamVideoStore.Save(_dcSettings);
            DashcamVideoLibrary.ApplyCapacity(_dcSelected, gb);
            RenderDashcamDetail();
            RefreshDashcamRow(_dcSelected);
        }

        private void IgnoreSelectedDashcamGap()
        {
            if (_dcSelected == null || _dcSettings == null || dcIssuesLv == null || dcIssuesLv.SelectedItems.Count == 0)
                return;
            var tag = dcIssuesLv.SelectedItems[0].Tag;
            if (tag is DashcamVideoLibrary.SeqGap gap)
            {
                _dcSettings.AcknowledgeGap(_dcSelected.Driver, gap.Key);
                _dcSelected.SeqGaps.RemoveAll(g => g.Key == gap.Key);
                _dcSelected.MissingClipCount = _dcSelected.SeqGaps.Sum(g => g.MissingCount);
            }
            else if (tag is DateTime day)
            {
                _dcSettings.AcknowledgeLostDay(_dcSelected.Driver, day);
                _dcSelected.LostDays.RemoveAll(x => x == day);
            }
            else return;

            DashcamVideoStore.Save(_dcSettings);
            RenderDashcamDetail();
            RefreshDashcamRow(_dcSelected);
        }

        private void RefreshDashcamRow(DashcamVideoLibrary.DriverReport d)
        {
            if (dcDriverLv == null) return;
            foreach (ListViewItem it in dcDriverLv.Items)
            {
                if (!ReferenceEquals(it.Tag, d)) continue;
                it.SubItems[1].Text = StatusText(d.Status);
                it.SubItems[2].Text = d.CapacityGb + (d.CapacityIsGuessed ? "?" : "") + " GB";
                it.SubItems[3].Text = RunwayText(d);
                it.SubItems[6].Text = d.MissingClipCount > 0 ? d.MissingClipCount.ToString(CultureInfo.InvariantCulture) : "—";
                it.SubItems[7].Text = d.LostDays.Count > 0 ? d.LostDays.Count.ToString(CultureInfo.InvariantCulture) : "—";
                dcDriverLv.Invalidate(it.Bounds);
                break;
            }
        }

        private void OpenSelectedDashcamFolder()
        {
            if (_dcSelected == null || string.IsNullOrEmpty(_dcSelected.FolderPath)) return;
            try
            {
                if (System.IO.Directory.Exists(_dcSelected.FolderPath))
                    System.Diagnostics.Process.Start("explorer.exe", "\"" + _dcSelected.FolderPath + "\"");
            }
            catch { }
        }

        // ── Status / theme / layout ───────────────────────────────────────

        private void SetDashcamStatus(string text)
        {
            if (dcStatusLbl == null || dcStatusLbl.IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(() => SetDashcamStatus(text))); } catch { }
                return;
            }
            dcStatusLbl.Text = "Status: " + text;
        }

        private void ApplyDashcamVisualTheme(bool layout = true)
        {
            if (tabPageDashcamVideos != null)
            {
                tabPageDashcamVideos.BackColor = SupeyTheme.SurfaceBase;
                tabPageDashcamVideos.ForeColor = SupeyTheme.TextPrimary;
            }
            StyleToolTabCard(dcMainCard, SupeyCard.Surface.Standard);
            StyleToolTabStatusBar(dcStatusCard);
            if (dcMainCard != null) dcMainCard.Dock = DockStyle.Fill;
            if (dcStatusCard != null)
            {
                dcStatusCard.Dock = DockStyle.Bottom;
                dcStatusCard.Height = ToolTabStatusH;
            }
            if (dcStatusLbl != null)
            {
                dcStatusLbl.ForeColor = SupeyTheme.TextSecondary;
                dcStatusLbl.Font = SupeyTheme.BodyFont;
                dcStatusLbl.BackColor = SupeyTheme.SurfaceStatusBar;
            }
            if (dcToolbar != null) dcToolbar.BackColor = SupeyTheme.Surface;
            if (dcToolbarCard != null)
            {
                StyleToolTabCard(dcToolbarCard, SupeyCard.Surface.Elevated);
                dcToolbarCard.Dock = DockStyle.Fill;
            }
            if (dcToolbarInner != null) dcToolbarInner.BackColor = SupeyTheme.SurfaceElevated;
            if (dcProblemsOnlyChk != null) dcProblemsOnlyChk.BackColor = SupeyTheme.SurfaceElevated;
            if (dcRootLbl != null)
            {
                dcRootLbl.ForeColor = SupeyTheme.TextMuted;
                dcRootLbl.Font = SupeyTheme.CaptionFont;
                dcRootLbl.BackColor = SupeyTheme.SurfaceElevated;
            }
            if (dcDetailSummary != null)
            {
                dcDetailSummary.ForeColor = SupeyTheme.TextPrimary;
                dcDetailSummary.Font = SupeyTheme.BodyFont;
                dcDetailSummary.BackColor = SupeyTheme.Surface;
            }
            if (dcDetailBtnRow != null) dcDetailBtnRow.BackColor = SupeyTheme.Surface;
            if (dcBodyHost != null) dcBodyHost.BackColor = SupeyTheme.Surface;
            if (dcListHost != null) dcListHost.BackColor = SupeyTheme.Surface;
            if (dcDivider != null) dcDivider.BackColor = SupeyTheme.Divider;
            if (dcDetailHost != null) dcDetailHost.BackColor = SupeyTheme.Surface;
            if (dcDriverLv != null)
            {
                dcDriverLv.BackColor = SupeyTheme.ListBody;
                dcDriverLv.ForeColor = SupeyTheme.ListText;
            }
            if (dcIssuesLv != null)
            {
                dcIssuesLv.BackColor = SupeyTheme.ListBody;
                dcIssuesLv.ForeColor = SupeyTheme.ListText;
            }
            try { SupeyDarkScrollBars.Apply(tabPageDashcamVideos); } catch { }

            if (layout) LayoutDashcamTabPanels();
        }

        private void LayoutDashcamTabPanels()
        {
            if (!_dcBuilt || tabPageDashcamVideos == null) return;
            if (dcStatusCard != null)
            {
                dcStatusCard.Dock = DockStyle.Bottom;
                dcStatusCard.Height = ToolTabStatusH;
                dcStatusCard.BringToFront();
            }
            if (dcMainCard != null)
            {
                dcMainCard.Dock = DockStyle.Fill;
                dcMainCard.SendToBack();
            }
            if (dcListHost != null && dcBodyHost != null && dcBodyHost.ClientSize.Width > 400)
            {
                int w = dcBodyHost.ClientSize.Width;
                dcListHost.Width = Math.Max(420, Math.Min(DashcamListWidth, w - 340));
            }
            try { tabPageDashcamVideos.PerformLayout(); } catch { }
        }
    }
}
