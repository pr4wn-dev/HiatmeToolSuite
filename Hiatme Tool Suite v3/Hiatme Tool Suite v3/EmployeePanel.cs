using CustomControls.RJControls;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data.Entity.Core.Common.CommandTrees.ExpressionBuilder;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.UI.WebControls;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;
using Chart = System.Windows.Forms.DataVisualization.Charting.Chart;
using Label = System.Windows.Forms.Label;
using Point = System.Drawing.Point;
using TextBox = System.Windows.Forms.TextBox;


namespace Hiatme_Tool_Suite_v3
{
    internal class EmployeeStatManager
    {
        private TabPage tabPage { get; set; }
        private MCLoginHandler mCLoginHandler { get; set; }
        private MCTripDownloader mCTripDownloader { get; set; }
        private List<WRDownloadedTrip> wRDownloadedTrips { get; set; }
        private List<MCDownloadedTrip> mCDownloadedTrips { get; set; }
        private List<EmployeeProductionStats> employeeStats { get; set; }
        private int Columns { get; set; }
        private int Rows { get; set; }
        private int EmptySlots { get; set; }
        private bool Run { get; set; }
        private SupeyCard employeeStatPanel { get; set; }
        private TableLayoutPanel primaryTable { get; set; }
        private WellRydePortalSession _wellRydePortalSession;
        private DateTime _tripDate;
        private SupeyMapLoadingOverlay _loadingOverlay;
        private int _loadingDepth;
        private bool _loadingControlAddedHooked;

        public EmployeeStatManager(TabPage formtabpage, MCLoginHandler mclh)
        {
            tabPage = formtabpage;
            mCLoginHandler = mclh;
        }

        public delegate void UpdateLoadingScreenHandler(string text);
        public delegate void ShowLoadingScreenHandler();
        public delegate void HideLoadingScreenHandler();

        public event UpdateLoadingScreenHandler UpdateLoadingScreen;
        public event ShowLoadingScreenHandler ShowLoadingScreen;
        public event HideLoadingScreenHandler HideLoadingScreen;

        private async Task AsyncUpdateLoadingScreen(string txt, CancellationToken cancellationToken = default)
        {
            UpdateLocalLoading(txt);
            UpdateLoadingScreen?.Invoke(txt);
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
        }
        /// <param name="tripDate">Service date for Modivcare and WellRyde downloads (date component only).</param>
        public async Task InitializeEmployeeDler(Form origform, WellRydePortalSession wellRydePortalSession = null,
            DateTime? tripDate = null, CancellationToken cancellationToken = default)
        {
            bool showedLoadingScreen = false;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _wellRydePortalSession = wellRydePortalSession;
                _tripDate = tripDate?.Date ?? DateTime.Today;

                tabPage.Controls.Clear();
                cancellationToken.ThrowIfCancellationRequested();
                ShowLocalLoading("Checking connections");
                ShowLoadingScreen?.Invoke();
                showedLoadingScreen = true;
                await AsyncUpdateLoadingScreen("Checking connections", cancellationToken);
                mCTripDownloader = new MCTripDownloader();

                await AsyncUpdateLoadingScreen("Downloading trips…", cancellationToken);
                await IntializeConnection();
                cancellationToken.ThrowIfCancellationRequested();
                await AsyncUpdateLoadingScreen("Building driver list…", cancellationToken);
                await BuildDriverList();
                cancellationToken.ThrowIfCancellationRequested();
                await AsyncUpdateLoadingScreen("Building tables…", cancellationToken);
                GenerateRowsColumnsAndData();
                BuildMainTables();
                cancellationToken.ThrowIfCancellationRequested();
                await AsyncUpdateLoadingScreen("Loading driver stats…", cancellationToken);
                GenerateEmployeesStats();
                await AsyncUpdateLoadingScreen("Finalizing…", cancellationToken);
            }
            finally
            {
                HideLocalLoading();
                if (showedLoadingScreen)
                    HideLoadingScreen?.Invoke();
            }
        }
        private async Task IntializeConnection()
        {
            wRDownloadedTrips = new List<WRDownloadedTrip>();
            mCDownloadedTrips = new List<MCDownloadedTrip>();
            mCDownloadedTrips = await mCTripDownloader.DownloadTripRecords(_tripDate, mCLoginHandler)
                ?? new List<MCDownloadedTrip>();

            if (_wellRydePortalSession == null)
                return;

            try
            {
                var fd = await _wellRydePortalSession.PostTripFilterDataAsync(_tripDate,
                    maxResults: WellRydePortalSession.DefaultTripFilterMaxResult).ConfigureAwait(false);
                if (fd.IsSuccess)
                    wRDownloadedTrips = WellRydeFilterDataParser.ParseTrips(fd.JsonBody, out _) ?? new List<WRDownloadedTrip>();
                else
                    Console.WriteLine("Employee stats: WellRyde filterdata failed: " + (fd.ErrorMessage ?? ""));
            }
            catch (Exception ex)
            {
                Console.WriteLine("Employee stats: WellRyde load failed: " + ex.Message);
            }
        }

        private static string NormalizeDriverKey(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;
            var s = name.Trim();
            while (s.Contains("  "))
                s = s.Replace("  ", " ");
            return s.ToUpperInvariant();
        }

        private static bool SkipDriverNameForStats(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return true;
            var key = NormalizeDriverKey(raw);
            return key.Contains("RESERVE") || key == "UNKNOWN" || key == "N/A";
        }

        private Task BuildDriverList()
        {
            employeeStats = new List<EmployeeProductionStats>();
            var displayByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            void AddDriverName(string raw)
            {
                if (SkipDriverNameForStats(raw))
                    return;
                var key = NormalizeDriverKey(raw);
                if (string.IsNullOrEmpty(key))
                    return;
                if (!displayByKey.ContainsKey(key))
                    displayByKey[key] = raw.Trim();
            }

            foreach (var mct in mCDownloadedTrips ?? Enumerable.Empty<MCDownloadedTrip>())
                AddDriverName(mct.DriverNameParsed);
            foreach (var wr in wRDownloadedTrips ?? Enumerable.Empty<WRDownloadedTrip>())
                AddDriverName(wr.DriverName);

            foreach (var kv in displayByKey.OrderBy(x => x.Value, StringComparer.OrdinalIgnoreCase))
            {
                string key = kv.Key;
                string display = kv.Value;
                var wrForDriver = (wRDownloadedTrips ?? new List<WRDownloadedTrip>())
                    .Where(w => NormalizeDriverKey(w.DriverName) == key)
                    .ToList();

                employeeStats.Add(new EmployeeProductionStats
                {
                    FullName = display,
                    FirstName = SplitFirstName(display),
                    LastName = SplitLastName(display),
                    DriverWRTripList = wrForDriver,
                });
            }

            return Task.CompletedTask;
        }

        private static string SplitFirstName(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
                return string.Empty;
            int comma = fullName.IndexOf(',');
            if (comma >= 0)
            {
                var after = fullName.Substring(comma + 1).Trim();
                return after.Length == 0 ? fullName.Trim() : after.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
            }
            var parts = fullName.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 0 ? string.Empty : parts[0];
        }

        private static string SplitLastName(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
                return string.Empty;
            int comma = fullName.IndexOf(',');
            if (comma >= 0)
                return fullName.Substring(0, comma).Trim();
            var parts = fullName.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length <= 1)
                return string.Empty;
            return parts[parts.Length - 1];
        }


        private void GenerateRowsColumnsAndData()
        {
            int employeeCount = employeeStats?.Count ?? 0;
            if (employeeCount <= 0)
            {
                Columns = 1;
                Rows = 1;
                EmptySlots = 0;
                return;
            }

            int maxhorizontalpanels = ComputeResponsiveColumnCount();
            int rows = (int)Math.Ceiling(employeeCount / (double)maxhorizontalpanels);

            int totalslots = rows * maxhorizontalpanels;
            EmptySlots = totalslots - employeeCount;
            Columns = maxhorizontalpanels;
            Rows = rows;
        }

        private int ComputeResponsiveColumnCount()
        {
            const int minCardWidth = 320;
            const int maxColumns = 5;
            const int minColumns = 2;

            int clientWidth = tabPage?.ClientSize.Width ?? 0;
            if (clientWidth <= 0)
                return 4;

            // Approximate tab padding/scrollbar + card margins to avoid squished content.
            int usable = Math.Max(1, clientWidth - 40);
            int cols = usable / minCardWidth;
            cols = Math.Max(minColumns, Math.Min(maxColumns, cols));
            return cols;
        }
        private void BuildMainTables()
        {
            tabPage.BackColor = SupeyTheme.SurfaceBase;
            tabPage.ForeColor = SupeyTheme.TextPrimary;

            var scroller = new System.Windows.Forms.Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = SupeyTheme.SurfaceBase,
                Padding = new Padding(12),
                Name = "ProductionScroller",
            };

            primaryTable = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Name = "MainTable",
                ColumnCount = Math.Max(1, Columns),
                RowCount = Math.Max(1, Rows),
                GrowStyle = TableLayoutPanelGrowStyle.AddRows,
                BackColor = SupeyTheme.SurfaceBase,
                Margin = new Padding(0),
                Padding = new Padding(0),
            };

            float colPct = 100f / Math.Max(1, primaryTable.ColumnCount);
            for (int i = 0; i < primaryTable.ColumnCount; i++)
                primaryTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, colPct));

            for (int i = 0; i < primaryTable.RowCount; i++)
                primaryTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            foreach (EmployeeProductionStats employeestat in employeeStats)
            {
                employeeStatPanel = new SupeyCard
                {
                    Dock = DockStyle.Fill,
                    Tag = employeestat,
                    Margin = new Padding(8),
                    Padding = new Padding(10),
                    SurfaceLevel = SupeyCard.Surface.Elevated,
                    ShowBorder = true,
                    CornerRadius = 8,
                    ForeColor = SupeyTheme.TextPrimary,
                    MouseState = SupeyMouseState.HOVER,
                    MinimumSize = new Size(300, 250),
                };

                TableLayoutPanel employeeTable = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    AutoSize = false,
                    Name = "EmployeeTable",
                    ColumnCount = 2,
                    RowCount = 7,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    GrowStyle = TableLayoutPanelGrowStyle.AddRows,
                    BackColor = SupeyTheme.SurfaceElevated,
                    Margin = new Padding(0),
                    Padding = new Padding(4, 2, 4, 2),
                };

                employeeTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
                employeeTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
                employeeTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f)); // header
                employeeTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f)); // accuracy row
                employeeTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f)); // accuracy bar
                employeeTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f)); // profit row
                employeeTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f)); // profit bar
                employeeTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f)); // workload row
                employeeTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f)); // workload bar

                Label fullName = new Label
                {
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Segoe UI Semibold", 14f, FontStyle.Bold),
                    BackColor = SupeyTheme.SurfaceHeader,
                    ForeColor = SupeyTheme.TextPrimary,
                    BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle,
                    Text = employeestat.FullName ?? "",
                    Name = "EmployeeFullNameLabel",
                    Height = 40,
                    Margin = new Padding(0, 0, 0, 8),
                    Padding = new Padding(4, 4, 4, 4),
                };
                employeeTable.SetColumnSpan(fullName, 2);
                employeeTable.Controls.Add(fullName);

                var accuracyLabel = CreateMetricCaptionLabel("Promptness", "AccuracyLabel");
                employeeTable.Controls.Add(accuracyLabel);

                var accuracyValue = CreateMetricValueLabel("0%", "AccuracyPercentLabel");
                employeestat.AccuracyLabel = accuracyValue;
                employeeTable.Controls.Add(accuracyValue);

                var accuracyProgress = CreateMetricProgressBar("AccuracyProgressBar");
                employeestat.AccuracyProgressBar = accuracyProgress;
                employeeTable.SetColumnSpan(accuracyProgress, 2);
                employeeTable.Controls.Add(accuracyProgress);

                var profitLabel = CreateMetricCaptionLabel("Profit", "ProfitLabel");
                employeeTable.Controls.Add(profitLabel);

                var profitValue = CreateMetricValueLabel("$0", "ProfitAmountLabel");
                employeestat.ProfitLabel = profitValue;
                employeeTable.Controls.Add(profitValue);

                var profitProgress = CreateMetricProgressBar("ProfitProgressBar");
                employeestat.ProfitProgressBar = profitProgress;
                employeeTable.SetColumnSpan(profitProgress, 2);
                employeeTable.Controls.Add(profitProgress);

                var workloadLabel = CreateMetricCaptionLabel("Workload", "WorkloadLabel");
                employeeTable.Controls.Add(workloadLabel);

                var workloadValue = CreateMetricValueLabel("0%", "WorkloadPercentLabel");
                employeestat.WorkloadLabel = workloadValue;
                employeeTable.Controls.Add(workloadValue);

                var workloadProgress = CreateMetricProgressBar("WorkloadProgressBar");
                employeestat.WorkloadProgressBar = workloadProgress;
                employeeTable.SetColumnSpan(workloadProgress, 2);
                employeeTable.Controls.Add(workloadProgress);

                employeeStatPanel.Controls.Add(employeeTable);
                primaryTable.Controls.Add(employeeStatPanel);
            }

            scroller.Controls.Add(primaryTable);
            tabPage.Controls.Add(scroller);
            if (_loadingDepth > 0 && _loadingOverlay != null && !_loadingOverlay.IsDisposed)
                _loadingOverlay.BringToFront();
        }

        private static Label CreateMetricCaptionLabel(string text, string name)
        {
            return new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = true,
                Font = SupeyTheme.SubHeaderFont,
                BackColor = SupeyTheme.SurfaceElevated,
                ForeColor = SupeyTheme.TextSecondary,
                BorderStyle = System.Windows.Forms.BorderStyle.None,
                Text = text,
                Name = name,
                Margin = new Padding(0, 6, 0, 0),
            };
        }

        private static Label CreateMetricValueLabel(string text, string name)
        {
            return new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                AutoSize = true,
                AutoEllipsis = true,
                Font = new Font("Segoe UI Semibold", 13f, FontStyle.Bold),
                BackColor = SupeyTheme.SurfaceElevated,
                ForeColor = SupeyTheme.TextPrimary,
                BorderStyle = System.Windows.Forms.BorderStyle.None,
                Text = text,
                Name = name,
                Margin = new Padding(0, 2, 0, 0),
            };
        }

        private static ProgressBar CreateMetricProgressBar(string name)
        {
            var bar = new RJProgressBar
            {
                Dock = DockStyle.Fill,
                Height = 16,
                MinimumSize = new Size(0, 16),
                MaximumSize = new Size(int.MaxValue, 16),
                Margin = new Padding(0, 3, 0, 3),
                Value = 0,
                Style = ProgressBarStyle.Continuous,
                Name = name,
                ChannelColor = Color.FromArgb(56, 62, 73),
                SliderHeight = 16,
                ChannelHeight = 16,
                ShowValue = TextPosition.None,
                UseValueGradient = true,
                GradientLowColor = Color.FromArgb(205, 74, 74),
                GradientMidColor = Color.FromArgb(231, 182, 74),
                GradientHighColor = Color.FromArgb(88, 194, 118),
            };
            // Keep all metric bars visually consistent even if parent layout refreshes.
            bar.SizeChanged += (s, e) =>
            {
                if (bar.IsDisposed) return;
                if (bar.Height != 16) bar.Height = 16;
            };
            return bar;
        }

        private void GenerateEmployeesStats()
        {
            foreach(EmployeeProductionStats eps in employeeStats)
            {
                eps.GenerateEmployeeStats(wRDownloadedTrips, mCDownloadedTrips, employeeStats.Count);
            }
        }

        private void EnsureLocalLoadingOverlay()
        {
            if (tabPage == null || tabPage.IsDisposed) return;
            if (tabPage.InvokeRequired)
            {
                tabPage.BeginInvoke((MethodInvoker)EnsureLocalLoadingOverlay);
                return;
            }

            if (_loadingOverlay == null || _loadingOverlay.IsDisposed)
            {
                _loadingOverlay = new SupeyMapLoadingOverlay { Visible = false, Dock = DockStyle.Fill };
                tabPage.Resize += (s, e) => SyncLocalLoadingOverlayBounds();
            }
            else if (_loadingOverlay.Dock != DockStyle.Fill)
            {
                _loadingOverlay.Dock = DockStyle.Fill;
            }

            if (!_loadingControlAddedHooked)
            {
                _loadingControlAddedHooked = true;
                tabPage.ControlAdded += (s, e) =>
                {
                    if (_loadingDepth > 0)
                        RefreshLocalLoadingZOrder();
                };
            }

            if (!ReferenceEquals(_loadingOverlay.Parent, tabPage))
                tabPage.Controls.Add(_loadingOverlay);

            SyncLocalLoadingOverlayBounds();
        }

        private void SyncLocalLoadingOverlayBounds()
        {
            if (tabPage == null || tabPage.IsDisposed || _loadingOverlay == null || _loadingOverlay.IsDisposed)
                return;

            if (_loadingOverlay.Dock != DockStyle.Fill)
                _loadingOverlay.Bounds = tabPage.ClientRectangle;

            RefreshLocalLoadingZOrder();
        }

        private void RefreshLocalLoadingZOrder()
        {
            if (_loadingDepth <= 0 || _loadingOverlay == null || _loadingOverlay.IsDisposed || !_loadingOverlay.Visible)
                return;

            _loadingOverlay.BringToFront();
        }

        private void ShowLocalLoading(string message = null)
        {
            if (tabPage == null || tabPage.IsDisposed) return;
            if (tabPage.InvokeRequired)
            {
                tabPage.BeginInvoke((MethodInvoker)(() => ShowLocalLoading(message)));
                return;
            }

            EnsureLocalLoadingOverlay();
            if (_loadingOverlay == null || _loadingOverlay.IsDisposed)
                return;

            if (!string.IsNullOrWhiteSpace(message))
                _loadingOverlay.Message = message.Trim();

            _loadingDepth++;
            if (_loadingDepth == 1)
            {
                SyncLocalLoadingOverlayBounds();
                _loadingOverlay.Visible = true;
                _loadingOverlay.IsAnimating = true;
                RefreshLocalLoadingZOrder();
            }
        }

        private void UpdateLocalLoading(string message)
        {
            if (tabPage == null || tabPage.IsDisposed) return;
            if (tabPage.InvokeRequired)
            {
                tabPage.BeginInvoke((MethodInvoker)(() => UpdateLocalLoading(message)));
                return;
            }

            if (_loadingDepth <= 0 || string.IsNullOrWhiteSpace(message))
                return;

            EnsureLocalLoadingOverlay();
            if (_loadingOverlay == null || _loadingOverlay.IsDisposed)
                return;

            _loadingOverlay.Message = message.Trim();
            RefreshLocalLoadingZOrder();
        }

        private void HideLocalLoading()
        {
            if (tabPage == null || tabPage.IsDisposed) return;
            if (tabPage.InvokeRequired)
            {
                tabPage.BeginInvoke((MethodInvoker)HideLocalLoading);
                return;
            }

            if (_loadingDepth <= 0 || _loadingOverlay == null || _loadingOverlay.IsDisposed)
                return;

            _loadingDepth--;
            if (_loadingDepth == 0)
            {
                _loadingOverlay.IsAnimating = false;
                _loadingOverlay.Visible = false;
            }
        }

        public void PushLoadingOverlay(string message = null)
        {
            ShowLocalLoading(message);
        }

        public void SetLoadingOverlayMessage(string message)
        {
            UpdateLocalLoading(message);
        }

        public void PopLoadingOverlay()
        {
            HideLocalLoading();
        }

        /// <summary>Re-apply bordered card chrome after a theme switch or tab revisit.</summary>
        public void ApplyVisualTheme()
        {
            if (tabPage == null || tabPage.IsDisposed) return;
            tabPage.BackColor = SupeyTheme.SurfaceBase;
            tabPage.ForeColor = SupeyTheme.TextPrimary;

            foreach (Control c in tabPage.Controls)
            {
                if (c is System.Windows.Forms.Panel scroller && scroller.Name == "ProductionScroller")
                {
                    scroller.BackColor = SupeyTheme.SurfaceBase;
                    foreach (Control child in scroller.Controls)
                    {
                        if (child is TableLayoutPanel table)
                            table.BackColor = SupeyTheme.SurfaceBase;
                    }
                }
            }

            if (primaryTable == null || primaryTable.IsDisposed) return;
            foreach (Control cell in primaryTable.Controls)
            {
                if (cell is SupeyCard card)
                {
                    card.SurfaceLevel = SupeyCard.Surface.Elevated;
                    card.ShowBorder = true;
                    card.CornerRadius = 8;
                    card.ForeColor = SupeyTheme.TextPrimary;
                }
            }
        }










    }
}
public static class ModifyProgressBarColor
{
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = false)]
    static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr w, IntPtr l);
    public static void SetState(this ProgressBar pBar, int state)
    {
        SendMessage(pBar.Handle, 1040, (IntPtr)state, IntPtr.Zero);
    }
}