using MaterialSkin.Controls;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data.Entity.Core.Common.CommandTrees.ExpressionBuilder;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
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
        private MaterialCard employeeStatPanel { get; set; }
        private TableLayoutPanel primaryTable { get; set; }
        private WellRydePortalSession _wellRydePortalSession;
        private DateTime _tripDate;

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

        private async Task AsyncUpdateLoadingScreen(string txt)
        {
            UpdateLoadingScreen(txt);
            await Task.Delay(2000);
        }
        /// <param name="tripDate">Service date for Modivcare and WellRyde downloads (date component only).</param>
        public async Task InitializeEmployeeDler(Form origform, WellRydePortalSession wellRydePortalSession = null,
            DateTime? tripDate = null)
        {
            _wellRydePortalSession = wellRydePortalSession;
            _tripDate = tripDate?.Date ?? DateTime.Today;

            tabPage.Controls.Clear();
            ShowLoadingScreen();
            await AsyncUpdateLoadingScreen("Checking connections");
            mCTripDownloader = new MCTripDownloader();

            await IntializeConnection();
            await AsyncUpdateLoadingScreen("Downloading trips");
            await AsyncUpdateLoadingScreen("Searching for drivers");
            await BuildDriverList();
            await AsyncUpdateLoadingScreen("Building tables");
            GenerateRowsColumnsAndData();
            BuildMainTables();
            await AsyncUpdateLoadingScreen("Loading driver stats");
            GenerateEmployeesStats();
            await AsyncUpdateLoadingScreen("Finalizing process..");
            HideLoadingScreen();
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
            int rows = 1;
            int drivercounter = 0;
            int maxhorizontalpanels = 5;
            for (int i = 0; i < employeeStats.Count - 1; i++)
            {
                drivercounter++;
                if (drivercounter == maxhorizontalpanels)
                {
                    drivercounter = 0;
                    rows++;
                }
            }

            int totalslots = rows * maxhorizontalpanels;
            EmptySlots = totalslots - employeeStats.Count;
            Columns = maxhorizontalpanels;
            Rows = rows;
        }
        private async void BuildMainTables()
        {
            //tabPage.Controls.Clear();
            //ShowLoadingScreen();
            //create primary employee panel
            primaryTable = new TableLayoutPanel
            {
                Location = new Point(0, 0),
                Dock = DockStyle.Fill,
                AutoSize = true,
                Name = "MainTable",
                ColumnCount = Columns,
                RowCount = Rows,
                AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink,
                GrowStyle = System.Windows.Forms.TableLayoutPanelGrowStyle.AddRows
            };

            for (int i = 0; i < primaryTable.ColumnCount; i++)
            {
                primaryTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            }

            for (int i = 0; i < primaryTable.RowCount; i++)
            {
                primaryTable.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            }


            //Create employee table to add to new panel
            foreach(EmployeeProductionStats employeestat in employeeStats) {

                employeeStatPanel = new MaterialCard { Dock = DockStyle.Fill, Tag = employeestat };

                TableLayoutPanel EmployeeTable = new TableLayoutPanel
                {
                    Location = new Point(0, 0),
                    Dock = DockStyle.Fill,
                    AutoSize = true,
                    Name = "EmployeeTable",
                    ColumnCount = 2,
                    RowCount = 7,
                    AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink,
                    GrowStyle = System.Windows.Forms.TableLayoutPanelGrowStyle.AddRows
                };

                for (int i = 0; i < EmployeeTable.ColumnCount; i++)
                {
                    EmployeeTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
                }

                EmployeeTable.RowStyles.Add(new RowStyle(SizeType.Percent, 10));
                EmployeeTable.RowStyles.Add(new RowStyle(SizeType.Percent, 10));
                EmployeeTable.RowStyles.Add(new RowStyle(SizeType.Percent, 10));
                EmployeeTable.RowStyles.Add(new RowStyle(SizeType.Percent, 10));
                EmployeeTable.RowStyles.Add(new RowStyle(SizeType.Percent, 10));
                EmployeeTable.RowStyles.Add(new RowStyle(SizeType.Percent, 10));
                EmployeeTable.RowStyles.Add(new RowStyle(SizeType.Percent, 10));

                //create controls to add to employee panel rows





                //add first name label
                Label employeefullname = new Label
                {
                    Location = new Point(0, 0),
                    TextAlign = ContentAlignment.MiddleCenter,
                    Dock = DockStyle.Fill,
                    Font = new Font("Segoe UI", 18, FontStyle.Bold),
                    BackColor = Color.FromArgb(64,64,64),
                    ForeColor = Color.Gainsboro,
                    BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle,
                    Text = employeestat.FullName,
                    Name = "EmployeeFullNameLabel"
                };
                EmployeeTable.SetColumnSpan(employeefullname, 2);
                EmployeeTable.Controls.Add(employeefullname);

                //add accuracy controls
                Label accuracylabel = new Label
                {
                    Location = new Point(0, 0),
                    Anchor = AnchorStyles.Left,
                    AutoSize = true,
                    Font = new Font("Segoe UI", 15, FontStyle.Bold),
                    BackColor = Color.FromArgb(80, 80, 80),
                    ForeColor = Color.Gainsboro,
                    BorderStyle = System.Windows.Forms.BorderStyle.None,
                    Text = "Accuracy",
                    Name = "AccuracyLabel"
                };
                EmployeeTable.SetColumnSpan(accuracylabel, 1);
                EmployeeTable.Controls.Add(accuracylabel);

                Label accuracypercentlabel = new Label
                {
                    Location = new Point(0, 0),
                    Anchor = AnchorStyles.Right,
                    AutoSize = true,
                    Font = new Font("Segoe UI", 20, FontStyle.Bold),
                    BackColor = Color.FromArgb(64, 64, 64),
                    ForeColor = Color.Gainsboro,
                    BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle,
                    Text = "0%",
                    Name = "AccuracyPercentLabel"
                };
                employeestat.AccuracyLabel = accuracypercentlabel;
                EmployeeTable.SetColumnSpan(employeestat.AccuracyLabel, 1);
                EmployeeTable.Controls.Add(employeestat.AccuracyLabel);

                ProgressBar accuracyprogressbar = new ProgressBar
                {
                    Location = new Point(0, 0),
                    Dock = DockStyle.Fill,
                    Value = 0,
                    Style = ProgressBarStyle.Continuous,
                    Name = "AccuracyProgressBar"
                };

                employeestat.AccuracyProgressBar = accuracyprogressbar;
                EmployeeTable.SetColumnSpan(employeestat.AccuracyProgressBar, 2);
                EmployeeTable.Controls.Add(employeestat.AccuracyProgressBar);





                //add revenue controls
                Label profitlabel = new Label
                {
                    Location = new Point(0, 0),
                    Anchor = AnchorStyles.Left,
                    AutoSize = true,
                    Font = new Font("Segoe UI", 15, FontStyle.Bold),
                    BackColor = Color.FromArgb(80, 80, 80),
                    ForeColor = Color.Gainsboro,
                    BorderStyle = System.Windows.Forms.BorderStyle.None,
                    Text = "Profit",
                    Name = "ProfitLabel"
                };
                EmployeeTable.SetColumnSpan(profitlabel, 1);
                EmployeeTable.Controls.Add(profitlabel);

                Label profitamountlabel = new Label
                {
                    Location = new Point(0, 0),
                    Anchor = AnchorStyles.Right,
                    AutoSize = true,
                    Font = new Font("Segoe UI", 20, FontStyle.Bold),
                    BackColor = Color.FromArgb(64, 64, 64),
                    ForeColor = Color.Gainsboro,
                    BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle,
                    Text = "$0",
                    Name = "ProfitAmountLabel"
                };
                employeestat.ProfitLabel = profitamountlabel;
                EmployeeTable.SetColumnSpan(employeestat.ProfitLabel, 1);
                EmployeeTable.Controls.Add(employeestat.ProfitLabel);

                ProgressBar profitprogressbar = new ProgressBar
                {
                    Location = new Point(0, 0),
                    Dock = DockStyle.Fill,
                    Value = 0,
                    Style = ProgressBarStyle.Continuous,
                    Name = "ProfitProgressBar"
                };
                
                employeestat.ProfitProgressBar = profitprogressbar;
                EmployeeTable.SetColumnSpan(employeestat.ProfitProgressBar, 2);
                EmployeeTable.Controls.Add(employeestat.ProfitProgressBar);







                //add workload controls
                Label workloadlabel = new Label
                {
                    Location = new Point(0, 0),
                    Anchor = AnchorStyles.Left,
                    AutoSize = true,
                    Font = new Font("Segoe UI", 15, FontStyle.Bold),
                    BackColor = Color.FromArgb(80, 80, 80),
                    ForeColor = Color.Gainsboro,
                    BorderStyle = System.Windows.Forms.BorderStyle.None,
                    Text = "Workload",
                    Name = "WorkloadLabel"
                };
                EmployeeTable.SetColumnSpan(workloadlabel, 1);
                EmployeeTable.Controls.Add(workloadlabel);

                Label workloadpercentlabel = new Label
                {
                    Location = new Point(0, 0),
                    Anchor = AnchorStyles.Right,
                    AutoSize = true,
                    Font = new Font("Segoe UI", 20, FontStyle.Bold),
                    BackColor = Color.FromArgb(64, 64, 64),
                    ForeColor = Color.Gainsboro,
                    BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle,
                    Text = "0%",
                    Name = "WorkloadPercentLabel"
                };
                employeestat.WorkloadLabel = workloadpercentlabel;
                EmployeeTable.SetColumnSpan(employeestat.WorkloadLabel, 1);
                EmployeeTable.Controls.Add(employeestat.WorkloadLabel);

                ProgressBar workloadprogressbar = new ProgressBar
                {
                    Location = new Point(0, 0),
                    Dock = DockStyle.Fill,
                    Value = 0,
                    Style = ProgressBarStyle.Continuous,
                    Name = "WorkloadProgressBar"
                };
               
                employeestat.WorkloadProgressBar = workloadprogressbar;
                EmployeeTable.SetColumnSpan(employeestat.WorkloadProgressBar, 2);
                EmployeeTable.Controls.Add(employeestat.WorkloadProgressBar);





















































                //add chart
                /*
                Chart employeeChart = new Chart
                {
                    Location = new Point(0, 0),
                    Dock = DockStyle.Fill,
                    AutoSize = true,
                    
                    
                    Name = "EmployeeStatChart"
                };
                EmployeeTable.Controls.Add(employeeChart);
                */

                /*
                for (int i = 0; i < EmployeeTable.RowCount; i++)
                {
                    MaterialCard test = new MaterialCard { Dock = DockStyle.Fill };
                    EmployeeTable.Controls.Add(test);
                }
                */


                employeeStatPanel.Controls.Add(EmployeeTable);
                primaryTable.Controls.Add(employeeStatPanel);
            }

            tabPage.Controls.Add(primaryTable);
        }

        private void GenerateEmployeesStats()
        {
            foreach(EmployeeProductionStats eps in employeeStats)
            {
                eps.GenerateEmployeeStats(wRDownloadedTrips, mCDownloadedTrips, employeeStats.Count);
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