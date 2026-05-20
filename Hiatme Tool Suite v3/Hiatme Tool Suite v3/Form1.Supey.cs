using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MaterialSkin.Controls;
using Newtonsoft.Json.Linq;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Partial class file for the "Supey Schedule" tab — an end-to-end schedule builder that
    /// pulls Modivcare trips, geocodes everything, clusters into ride-share groups, scores +
    /// greedy-assigns clusters to a roster of drivers, and previews the result on a list +
    /// map before saving as a Modivcare-format Excel workbook.
    /// </summary>
    /// <remarks>
    /// All UI is built programmatically in <see cref="InitializeSupeyTab"/> rather than via the
    /// designer to keep <c>Form1.Designer.cs</c> clean. The constructor in <c>Form1.cs</c>
    /// calls <see cref="InitializeSupeyTab"/> once, after <c>InitializeComponent</c> has placed
    /// the empty <c>tabPageSupey</c>.
    /// </remarks>
    public partial class Form1
    {
        // ---------- UI controls (all owned by tabPageSupey) ----------
        private Panel _supeyToolbar;
        private Panel _supeyStatusStrip;
        private Panel _supeyMainHost;
        private SplitContainer _supeyMainSplit;
        private bool _supeySplitDistanceInitialized;
        private SupeyCollapsiblePanel _supeyDriversCollapsible;
        private SupeyCollapsiblePanel _supeyTripsCollapsible;
        private SupeyCollapsiblePanel _supeyRightCollapsible;
        // Draggable bars between the docked side panels. They show only while their
        // collapsible neighbor is expanded — a splitter on a 34px-wide collapsed panel
        // would resize a sliver of the title strip and confuse users.
        private Splitter _supeyDriversSplitter;
        private Splitter _supeyAiSplitter;
        private Splitter _supeyInfoSplitter;
        private MaterialLabel _supeyTemplateCompareLbl;

        private RJDatePicker _supeyDatePicker;
        private SupeyButton _supeyLoadBtn;
        private SupeyButton _supeyBuildBtn;
        private SupeyButton _supeySaveBtn;
        private SupeyButton _supeyCancelBtn;
        private MaterialLabel _supeyScheduleUpdatedLbl;
        private Label _supeyToolbarStatusLbl;
        private MaterialLabel _supeyOsrmStatusLbl;          // legacy alias — not visible
        private SupeyStatusPill _supeyOsrmStatusPill;       // the actual visible OSRM badge

        private MaterialProgressBar _supeyProgressBar;
        private MaterialLabel _supeyStatsLbl;
        private LinkLabel _supeyWarningsLink;

        private ListView _supeyDriversLv;
        private ColumnHeader _supeyDriversColCheck;
        private ColumnHeader _supeyDriversColName;
        private ColumnHeader _supeyDriversColCap;
        private ColumnHeader _supeyDriversColShift;
        private ColumnHeader _supeyDriversColRelease;
        private SupeyButton _supeyDriverAddBtn;
        private SupeyButton _supeyDriverEditBtn;
        private SupeyButton _supeyDriverRemoveBtn;
        private SupeyButton _supeyDriverSaveBtn;
        private SupeyButton _supeyDriverPullBtn;
        private Label _supeyRosterFooter;
        private Label _supeyDriversEmptyHint;

        private ComboBox _supeyPreviewDriverCb;
        private ListView _supeyPreviewLv;
        private ColumnHeader _supeyPrevColGrp;
        private ColumnHeader _supeyPrevColTrip;
        private ColumnHeader _supeyPrevColClient;
        private ColumnHeader _supeyPrevColPUTime;
        private ColumnHeader _supeyPrevColPUStreet;
        private ColumnHeader _supeyPrevColPUCity;
        private ColumnHeader _supeyPrevColDOTime;
        private ColumnHeader _supeyPrevColDOStreet;
        private ColumnHeader _supeyPrevColDOCity;
        private ColumnHeader _supeyPrevColMiles;
        private ColumnHeader _supeyPrevColGeo;
        private const int SupeyPrevColGeoIndex = 3;
        private MaterialLabel _supeyPreviewStatsLbl;
        private Label _supeyPreviewEmptyHint;

        private SupeyMapWorkspace _supeyMap;

        // ---------- runtime state ----------
        private List<SupeyDriverProfile> _supeyRoster = new List<SupeyDriverProfile>();
        private List<MCDownloadedTrip> _supeyLoadedTrips = new List<MCDownloadedTrip>();
        private SupeyScheduleResult _supeyResult;
        private CancellationTokenSource _supeyCts;

        /// <summary>Loaded pool = raw Modivcare rows; AiSchedule = BUILD/revise JSON mapped to drivers.</summary>
        private enum SupeyTripsPanelView { Empty, LoadedPool, AiSchedule }
        private SupeyTripsPanelView _supeyTripsPanelView = SupeyTripsPanelView.Empty;
        private DateTime _supeyRosterLastSaved;

        /// <summary>
        /// Builds the Supey Schedule tab UI on top of the designer-placed empty
        /// <see cref="tabPageSupey"/>. Idempotent — bailing out if controls have already been
        /// added prevents accidental double-initialization during designer experiments.
        /// </summary>
        private void InitializeSupeyTab()
        {
            if (tabPageSupey == null) return;
            if (tabPageSupey.Controls.Count > 0) return;

            tabPageSupey.BackColor = Color.FromArgb(33, 33, 33);
            tabPageSupey.UseVisualStyleBackColor = false;

            BuildSupeyToolbar();
            BuildSupeyStatusStrip();
            BuildSupeyWorkspace();

            tabPageSupey.Controls.Add(_supeyMainHost);
            tabPageSupey.Controls.Add(_supeyStatusStrip);
            tabPageSupey.Controls.Add(_supeyToolbar);

            LoadSupeyRosterFromDisk();
            UpdateSupeyButtonStates();
            SetSupeyStatus("Ready. Pick a service date and click Load Trips.");
            _ = RefreshSupeyOsrmStatusAsync();

            // Belt-and-suspenders: the constructor-time SupeyDarkScrollBars.Apply
            // walked the form before this tab was built. We hook ControlAdded
            // recursively so descendants are picked up automatically, but a
            // direct call here guarantees every control under tabPageSupey gets
            // the DarkMode_Explorer theme even on the very first render — no
            // scrollbar should ever appear bright gray on this tab.
            SupeyDarkScrollBars.Apply(tabPageSupey);
        }

        private void BuildSupeyToolbar()
        {
            // Toolbar = 56px header strip with a 1px bottom divider. Left group holds the
            // action controls (date + load + build + save + cancel), right group holds the
            // status pills (OSRM badge + free-form status text). Anchoring the right group
            // to the right edge means the action cluster never gets pushed off-screen by a
            // long status string, which used to happen with the old single-flow layout.
            _supeyToolbar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 56,
                BackColor = SupeyTheme.SurfaceHeader,
                Padding = new Padding(0),
            };

            var divider = new Panel
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
                BackColor = SupeyTheme.SurfaceHeader,
                Padding = new Padding(12, 12, 0, 0),
                Width = 800,
            };

            var rightFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                AutoSize = true,
                BackColor = SupeyTheme.SurfaceHeader,
                Padding = new Padding(0, 12, 12, 0),
                Width = 720,
            };

            var dateLabel = new Label
            {
                Text = "Service date",
                AutoSize = true,
                ForeColor = SupeyTheme.TextSecondary,
                BackColor = SupeyTheme.SurfaceHeader,
                Font = SupeyTheme.CaptionFont,
                Margin = new Padding(0, 8, 10, 0),
            };
            // RJDatePicker needed more breathing room — at 190px the long-form date string
            // ("Tuesday, May 19, 2026") was crashing into the calendar glyph. 230 fits it
            // comfortably with weekday + month + day + year.
            _supeyDatePicker = new RJDatePicker
            {
                Size = new Size(232, 30),
                Margin = new Padding(0, 1, 12, 0),
                BorderColor = SupeyTheme.BorderSubtle,
                BorderSize = 1,
                Font = new Font("Segoe UI", 9.5f),
                SkinColor = SupeyTheme.SurfaceElevated,
                TextColor = SupeyTheme.TextPrimary,
            };

            var sep1 = MakeToolbarSeparator();

            _supeyLoadBtn = new SupeyButton
            {
                Text = "LOAD TRIPS",
                Kind = SupeyButton.Variant.Primary,
                Size = new Size(120, 30),
                Margin = new Padding(0, 1, 6, 0),
            };
            _supeyLoadBtn.Click += async (s, e) => await OnSupeyLoadClickedAsync();

            _supeyBuildBtn = new SupeyButton
            {
                Text = "BUILD",
                Kind = SupeyButton.Variant.Primary,
                Size = new Size(96, 30),
                Margin = new Padding(0, 1, 6, 0),
                Visible = false,
            };
            _supeyBuildBtn.Click += async (s, e) => await OnSupeyBuildClickedAsync();

            _supeySaveBtn = new SupeyButton
            {
                Text = "SAVE WORKBOOK",
                Kind = SupeyButton.Variant.Secondary,
                Size = new Size(146, 30),
                Margin = new Padding(0, 1, 6, 0),
                Visible = false,
            };
            _supeySaveBtn.Click += async (s, e) => await OnSupeySaveClickedAsync();

            _supeyCancelBtn = new SupeyButton
            {
                Text = "CANCEL",
                Kind = SupeyButton.Variant.Outlined,
                Size = new Size(92, 30),
                Margin = new Padding(0, 1, 0, 0),
                Visible = false,
            };
            _supeyCancelBtn.Click += (s, e) => OnSupeyCancelClicked();

            // MaterialLabel forces the MaterialSkin Roboto font + its own line
            // metrics, which clipped the top of every letter on this label
            // ("Loaded 145 trips for May 29..." rendered with the upper half of
            // each character cut off). Plain Label respects Height/Font cleanly
            // — 28px is enough breathing room for Segoe UI 9.5pt with 4-5px of
            // padding above and below.
            _supeyToolbarStatusLbl = new Label
            {
                Text = "Ready",
                AutoSize = false,
                Width = 460,
                Height = 28,
                ForeColor = SupeyTheme.TextSecondary,
                BackColor = SupeyTheme.SurfaceHeader,
                TextAlign = ContentAlignment.MiddleRight,
                Font = SupeyTheme.BodyFont,
                Margin = new Padding(0, 3, 8, 0),
            };

            // OSRM as a real pill on the right side of the toolbar. Click to refresh.
            _supeyOsrmStatusPill = new SupeyStatusPill
            {
                Label = "OSRM …",
                DotColor = SupeyTheme.TextMuted,
                Margin = new Padding(0, 5, 0, 0),
                Cursor = Cursors.Hand,
            };
            _supeyOsrmStatusPill.Click += async (s, e) => await RefreshSupeyOsrmStatusAsync();
            // Keep the legacy MaterialLabel field as a hidden alias so back-compat
            // callers (RefreshSupeyOsrmStatusAsync etc.) can still write to .Text /
            // .ForeColor without crashing — we project those onto the pill below.
            _supeyOsrmStatusLbl = new MaterialLabel
            {
                Visible = false,
                Width = 1,
                Height = 1,
                Location = new Point(-100, -100),
            };
            var osrmTip = "Local OSRM at " + OsrmSettings.LocalBaseUrl + "\r\n" +
                "Start: tools\\osrm\\scripts\\start-osrm.ps1\r\n" +
                "See: tools\\osrm\\README.md\r\nClick to refresh.";
            var osrmTipProvider = new ToolTip { AutoPopDelay = 12000, InitialDelay = 400 };
            osrmTipProvider.SetToolTip(_supeyOsrmStatusPill, osrmTip);

            // Left group — action cluster, ordered LTR.
            leftFlow.Controls.Add(dateLabel);
            leftFlow.Controls.Add(_supeyDatePicker);
            leftFlow.Controls.Add(sep1);
            leftFlow.Controls.Add(_supeyLoadBtn);
            leftFlow.Controls.Add(_supeyBuildBtn);
            leftFlow.Controls.Add(_supeySaveBtn);
            leftFlow.Controls.Add(_supeyCancelBtn);

            // Right group — status pills, ordered RTL so the OSRM badge sits at the far
            // right with the status text wrapping toward the center.
            rightFlow.Controls.Add(_supeyOsrmStatusPill);
            rightFlow.Controls.Add(_supeyToolbarStatusLbl);

            _supeyToolbar.Controls.Add(rightFlow);
            _supeyToolbar.Controls.Add(leftFlow);
            _supeyToolbar.Controls.Add(divider);
        }

        /// <summary>
        /// 1×24px vertical hairline used inside the toolbar between logical groups.
        /// Looks like a CSS border-left, just rendered as a thin Panel.
        /// </summary>
        private static Panel MakeToolbarSeparator()
        {
            return new Panel
            {
                Width = 1,
                Height = 24,
                BackColor = SupeyTheme.Divider,
                Margin = new Padding(4, 6, 12, 0),
            };
        }

        private void BuildSupeyStatusStrip()
        {
            // Bottom strip is now intentionally minimal: the marquee progress bar on
            // the left while a build/AI request is in flight, and the warnings link
            // pinned to the right. Fleet totals moved up into the Trips panel header
            // (where they sit directly above the schedule they summarize) and free-
            // form status messages live in the toolbar status pill at the top.
            _supeyStatusStrip = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 28,
                BackColor = SupeyTheme.SurfaceStatusBar,
                Padding = new Padding(12, 4, 12, 4),
            };
            var statusTop = new Panel
            {
                Dock = DockStyle.Top,
                Height = 1,
                BackColor = SupeyTheme.Divider,
            };
            _supeyStatusStrip.Controls.Add(statusTop);

            _supeyProgressBar = new MaterialProgressBar
            {
                Location = new Point(0, 8),
                Width = 240,
                Height = 10,
                Style = ProgressBarStyle.Marquee,
                Visible = false,
            };

            _supeyWarningsLink = new LinkLabel
            {
                Text = "0 warnings",
                Location = new Point(0, 6),
                Width = 220,
                Height = 18,
                ForeColor = SupeyTheme.WarnText,
                LinkColor = SupeyTheme.WarnText,
                ActiveLinkColor = SupeyTheme.TextPrimary,
                BackColor = SupeyTheme.SurfaceStatusBar,
                LinkBehavior = LinkBehavior.HoverUnderline,
                TextAlign = ContentAlignment.MiddleRight,
                Font = SupeyTheme.CaptionFont,
            };
            _supeyWarningsLink.Click += (s, e) => OnSupeyWarningsLinkClicked();

            _supeyStatusStrip.Controls.Add(_supeyProgressBar);
            _supeyStatusStrip.Controls.Add(_supeyWarningsLink);

            void Reposition()
            {
                if (_supeyWarningsLink == null || _supeyStatusStrip == null) return;
                _supeyWarningsLink.Left = Math.Max(0, _supeyStatusStrip.ClientSize.Width - _supeyWarningsLink.Width - 12);
            }
            _supeyStatusStrip.Resize += (s, e) => Reposition();
            _supeyStatusStrip.HandleCreated += (s, e) => Reposition();
            Reposition();
        }

        /// <summary>
        /// Click handler for the bottom-right warnings link. Selects the dropdown's "Warnings"
        /// entry so the inline list renders — a much better UX than the giant text-modal we used
        /// to throw, especially for builds with hundreds of warnings.
        /// </summary>
        private void OnSupeyWarningsLinkClicked()
        {
            if (_supeyResult == null || _supeyResult.WarningCount == 0) return;
            if (_supeyPreviewDriverCb == null) return;
            for (int i = 0; i < _supeyPreviewDriverCb.Items.Count; i++)
            {
                if (_supeyPreviewDriverCb.Items[i] is SupeyPreviewItem itm &&
                    itm.Kind == SupeyPreviewItem.ItemKind.Warnings)
                {
                    _supeyPreviewDriverCb.SelectedIndex = i;
                    return;
                }
            }
            // Fallback: still hand them the modal if for any reason the dropdown entry didn't get
            // added (e.g. RebuildPreviewDropdown skipped it because WarningCount was 0).
            ShowSupeyWarningsModal();
        }

        private void BuildSupeyWorkspace()
        {
            _supeyMainHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.SurfaceBase,
                Padding = new Padding(0),
            };

            _supeyMainSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                BackColor = SupeyTheme.Divider,
                Panel1MinSize = 120,
                Panel2MinSize = 72,
                SplitterWidth = 6,
                FixedPanel = FixedPanel.None,
            };
            _supeyMainSplit.Panel1.BackColor = SupeyTheme.SurfaceBase;
            _supeyMainSplit.Panel2.BackColor = SupeyTheme.Surface;
            _supeyMainSplit.SizeChanged += (s, e) => EnsureSupeySplitDistance();
            _supeyMainSplit.SplitterMoved += (s, e) => { _supeySplitDistanceInitialized = true; };

            var workPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.SurfaceBase,
            };

            _supeyMap = new SupeyMapWorkspace { Dock = DockStyle.Fill };
            _supeyMap.SetSupeyStatusOnHost = msg => SetSupeyStatus(msg);

            _supeyDriversCollapsible = new SupeyCollapsiblePanel
            {
                Title = "Drivers",
                Dock = DockStyle.Left,
                ExpandedWidth = 300,
                MinExpandedWidth = 220,
                MaxExpandedWidth = 520,
            };
            BuildSupeyDriversPanel(_supeyDriversCollapsible.ContentPanel);

            BuildSupeyAiPanel();
            // Pulled the AI panel's resize bounds out so users can collapse the AI a bit
            // without losing the prompt and stretch it wider when transcripts get long.
            if (_supeyAiCollapsible != null)
            {
                _supeyAiCollapsible.MinExpandedWidth = 260;
                _supeyAiCollapsible.MaxExpandedWidth = 560;
            }

            _supeyRightCollapsible = new SupeyCollapsiblePanel
            {
                Title = "Info",
                Dock = DockStyle.Right,
                ExpandedWidth = 280,
                MinExpandedWidth = 220,
                MaxExpandedWidth = 520,
            };
            BuildSupeyRightPanel(_supeyRightCollapsible.ContentPanel);

            // ── Workspace dock layout with draggable splitters ────────────────
            // WinForms Dock semantics: when multiple controls share the same Dock side,
            // the LAST one added sits closest to the outer edge. So to land on the
            // intended layout
            //
            //   [Drivers | drvSplit | Map | aiSplit | AI | infoSplit | Info]
            //
            // we add (in order):
            //   1. Map        (Fill)        — fills whatever's left
            //   2. drvSplit   (Left)        — pushed inward by step 3
            //   3. Drivers    (Left)        — leftmost
            //   4. aiSplit    (Right)       — pushed inward by steps 5/6/7
            //   5. AI         (Right)       — pushed inward by 6/7
            //   6. infoSplit  (Right)       — pushed inward by 7
            //   7. Info       (Right)       — rightmost
            //
            // Each Splitter is a thin draggable bar that resizes the docked control
            // adjacent to it (the "outer" one on its dock side). MinExtra leaves a
            // sensible amount of space for the Map (Fill) so users can't drag a side
            // panel to swallow the whole workspace.
            _supeyDriversSplitter = MakeDockSplitter(DockStyle.Left, _supeyDriversCollapsible);
            _supeyAiSplitter = MakeDockSplitter(DockStyle.Right, _supeyAiCollapsible);
            _supeyInfoSplitter = MakeDockSplitter(DockStyle.Right, _supeyRightCollapsible);

            workPanel.Controls.Add(_supeyMap);
            workPanel.Controls.Add(_supeyDriversSplitter);
            workPanel.Controls.Add(_supeyDriversCollapsible);
            workPanel.Controls.Add(_supeyAiSplitter);
            workPanel.Controls.Add(_supeyAiCollapsible);
            workPanel.Controls.Add(_supeyInfoSplitter);
            workPanel.Controls.Add(_supeyRightCollapsible);
            _supeyMainSplit.Panel1.Controls.Add(workPanel);

            _supeyTripsCollapsible = new SupeyCollapsiblePanel
            {
                Title = "Trips",
                Dock = DockStyle.Fill,
            };
            BuildSupeyTripsPanel(_supeyTripsCollapsible.ContentPanel);
            _supeyMainSplit.Panel2.Controls.Add(_supeyTripsCollapsible);

            _supeyMainHost.Controls.Add(_supeyMainSplit);
        }

        /// <summary>
        /// Builds a styled <see cref="Splitter"/> that sits next to a docked
        /// <see cref="SupeyCollapsiblePanel"/> and lets the user drag-resize it.
        /// The splitter:
        ///   • is hidden while the panel is collapsed (a 34px-tall slice is useless to drag);
        ///   • clamps the panel's expanded size to the panel's MinExpandedWidth/MaxExpandedWidth;
        ///   • leaves a sensible MinExtra so the central Map (Fill) can't be squished to nothing.
        /// </summary>
        private Splitter MakeDockSplitter(DockStyle dock, SupeyCollapsiblePanel target)
        {
            var s = new Splitter
            {
                Dock = dock,
                Width = 4,
                Height = 4,
                BackColor = SupeyTheme.Divider,
                MinSize = target?.MinExpandedWidth > 0 ? target.MinExpandedWidth : 180,
                MinExtra = 320,
                Cursor = (dock == DockStyle.Left || dock == DockStyle.Right) ? Cursors.VSplit : Cursors.HSplit,
                Visible = target?.Expanded ?? true,
            };
            // Subtle hover affordance — bar lightens so users notice it's draggable.
            s.MouseEnter += (sender, e) => { s.BackColor = SupeyTheme.BorderSubtle; };
            s.MouseLeave += (sender, e) => { s.BackColor = SupeyTheme.Divider; };

            if (target != null)
            {
                target.ExpandedChanged += (sender, e) => { s.Visible = target.Expanded; };

                // Enforce the panel's MaxExpandedWidth on splitter drag — WinForms only
                // honors MinExtra/MinSize directly, so we clamp on SplitterMoved.
                s.SplitterMoved += (sender, e) =>
                {
                    if (target.MaxExpandedWidth > 0 &&
                        (dock == DockStyle.Left || dock == DockStyle.Right) &&
                        target.Width > target.MaxExpandedWidth)
                    {
                        target.Width = target.MaxExpandedWidth;
                    }
                };
            }
            return s;
        }

        /// <summary>Default trip list to ~38% of workspace height; user drags the split bar after that.</summary>
        private void EnsureSupeySplitDistance()
        {
            if (_supeyMainSplit == null || _supeySplitDistanceInitialized) return;
            int total = _supeyMainSplit.Height;
            if (total < 200) return;

            int tripsH = Math.Max(_supeyMainSplit.Panel2MinSize,
                Math.Min(480, (int)(total * 0.38)));
            int mapH = total - tripsH - _supeyMainSplit.SplitterWidth;
            if (mapH < _supeyMainSplit.Panel1MinSize)
                mapH = _supeyMainSplit.Panel1MinSize;

            _supeyMainSplit.SplitterDistance = mapH;
            _supeySplitDistanceInitialized = true;
        }

        private void BuildSupeyRightPanel(Panel host)
        {
            host.Padding = new Padding(10, 8, 10, 10);
            host.BackColor = SupeyTheme.Surface;

            // Two stacked card sections: Warnings (top), Past Weekdays (fill). Each has a
            // small section header inside the card, NOT a separate label outside, which
            // gives the panel a proper "documented sections" feel instead of stacked
            // floating labels.
            var pastCard = MakeInfoCard(out Label pastTitle, out Panel pastBody);
            pastCard.Dock = DockStyle.Fill;
            pastCard.Margin = new Padding(0, 8, 0, 0);
            pastTitle.Text = "Past weekdays · reference";

            // Use a slightly tighter caption font so the body text doesn't have to
            // shatter into 3-4-line fragments inside this narrow column.
            _supeyTemplateCompareLbl = new MaterialLabel
            {
                Dock = DockStyle.Fill,
                Text = "An optional after-BUILD diff against saved weekday CSVs. Reference only — the AI uses its own memory.",
                ForeColor = SupeyTheme.TextSecondary,
                BackColor = SupeyTheme.SurfaceElevated,
                AutoSize = false,
                Font = SupeyTheme.CaptionFont,
                Padding = new Padding(2, 4, 2, 2),
            };
            pastBody.Controls.Add(_supeyTemplateCompareLbl);

            var warnCard = MakeInfoCard(out Label warnTitle, out Panel warnBody);
            warnCard.Dock = DockStyle.Top;
            warnCard.Height = 78;
            warnTitle.Text = "Warnings";

            var warnLink = new LinkLabel
            {
                Dock = DockStyle.Fill,
                Text = "View warnings…",
                LinkColor = SupeyTheme.TextLink,
                ActiveLinkColor = SupeyTheme.TextPrimary,
                VisitedLinkColor = SupeyTheme.TextLink,
                BackColor = SupeyTheme.SurfaceElevated,
                Font = SupeyTheme.BodyFont,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(2, 0, 0, 0),
            };
            warnLink.Click += (s, e) => ShowSupeyWarningsModal();
            warnBody.Controls.Add(warnLink);

            host.Controls.Add(pastCard);
            host.Controls.Add(warnCard);
        }

        /// <summary>
        /// Build one of the elevated card sections used inside the Info panel. Each card
        /// has its own 22px title strip on top, a 1px divider, and a body region that the
        /// caller can populate. Same chrome for every card → consistent reading rhythm.
        /// </summary>
        private static Panel MakeInfoCard(out Label title, out Panel body)
        {
            var card = new Panel
            {
                BackColor = SupeyTheme.SurfaceElevated,
                Padding = new Padding(10, 6, 10, 10),
                Margin = new Padding(0),
            };
            title = new Label
            {
                Dock = DockStyle.Top,
                Height = 22,
                Text = "",
                ForeColor = SupeyTheme.TextPrimary,
                BackColor = SupeyTheme.SurfaceElevated,
                Font = SupeyTheme.SubHeaderFont,
                TextAlign = ContentAlignment.MiddleLeft,
            };
            var sep = new Panel
            {
                Dock = DockStyle.Top,
                Height = 1,
                BackColor = SupeyTheme.Divider,
            };
            body = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.SurfaceElevated,
                Padding = new Padding(0, 6, 0, 0),
            };
            card.Controls.Add(body);
            card.Controls.Add(sep);
            card.Controls.Add(title);
            return card;
        }

        private void BuildSupeyDriversPanel(Panel host)
        {
            // SupeyCollapsiblePanel already paints the section header ("Drivers") at the top
            // of the panel. We used to add a second redundant in-content "Drivers" label here
            // which gave the left side that "Drivers / Drivers" double-header look — dropped.

            // The button area is now a 3-row TableLayoutPanel instead of absolute pixel
            // positioning. That solves a long-standing "buttons clip / overflow on resize"
            // problem and gives consistent gutters between every cell. Top row = primary
            // PULL action (full width), middle row = ADD / EDIT / REMOVE / SAVE (4 equal
            // cells), bottom row = roster footer.
            var btnRow = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 132,
                BackColor = SupeyTheme.SurfaceHeader,
                Padding = new Padding(10, 10, 10, 10),
                ColumnCount = 4,
                RowCount = 3,
            };
            btnRow.ColumnStyles.Clear();
            for (int i = 0; i < 4; i++)
                btnRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            btnRow.RowStyles.Clear();
            btnRow.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
            btnRow.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));
            btnRow.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));

            _supeyDriverPullBtn = new SupeyButton
            {
                Text = "PULL FROM WELLRYDE",
                Kind = SupeyButton.Variant.Primary,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 6),
            };
            _supeyDriverPullBtn.Click += async (s, e) => await OnSupeyPullFromWellRydeAsync();
            btnRow.SetColumnSpan(_supeyDriverPullBtn, 4);

            _supeyDriverAddBtn = new SupeyButton
            {
                Text = "ADD",
                Kind = SupeyButton.Variant.Secondary,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 4, 0),
            };
            _supeyDriverAddBtn.Click += (s, e) => OnSupeyDriverAdd();

            _supeyDriverEditBtn = new SupeyButton
            {
                Text = "EDIT",
                Kind = SupeyButton.Variant.Secondary,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 4, 0),
            };
            _supeyDriverEditBtn.Click += (s, e) => OnSupeyDriverEdit();

            _supeyDriverRemoveBtn = new SupeyButton
            {
                Text = "REMOVE",
                Kind = SupeyButton.Variant.Outlined,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 4, 0),
            };
            _supeyDriverRemoveBtn.Click += (s, e) => OnSupeyDriverRemove();

            _supeyDriverSaveBtn = new SupeyButton
            {
                Text = "SAVE",
                Kind = SupeyButton.Variant.Secondary,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
            };
            _supeyDriverSaveBtn.Click += (s, e) => SaveSupeyRosterToDisk(showOk: true);

            _supeyRosterFooter = new Label
            {
                Text = "0 drivers",
                AutoSize = false,
                Dock = DockStyle.Fill,
                ForeColor = SupeyTheme.TextMuted,
                BackColor = SupeyTheme.SurfaceHeader,
                Font = SupeyTheme.CaptionFont,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0),
            };

            btnRow.Controls.Add(_supeyDriverPullBtn, 0, 0);
            btnRow.Controls.Add(_supeyDriverAddBtn, 0, 1);
            btnRow.Controls.Add(_supeyDriverEditBtn, 1, 1);
            btnRow.Controls.Add(_supeyDriverRemoveBtn, 2, 1);
            btnRow.Controls.Add(_supeyDriverSaveBtn, 3, 1);
            btnRow.Controls.Add(_supeyRosterFooter, 0, 2);
            btnRow.SetColumnSpan(_supeyRosterFooter, 4);

            _supeyDriversLv = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                BackColor = SupeyTheme.ListBody,
                ForeColor = SupeyTheme.ListText,
                FullRowSelect = true,
                // GridLines = true is purely declarative under owner-draw — the framework no
                // longer paints them — but we set it for accessibility tools and for parity with
                // the legacy listviews that report "GridLines: True" in the designer.
                GridLines = true,
                HideSelection = false,
                MultiSelect = false,
                CheckBoxes = true,
                Font = new Font("Archivo Medium", 10f),
                OwnerDraw = true,
                UseCompatibleStateImageBehavior = false,
            };
            // Match the trips preview's owner-draw dark theme (header 51/51/51, body 70/70/70,
            // RoyalBlue selection). Column 0 carries the standard checkbox glyph rendered via
            // DrawDefault — see SupeyDriversLv_DrawSubItem for the per-column dispatch.
            _supeyDriversLv.DrawColumnHeader += SupeyDriversLv_DrawColumnHeader;
            _supeyDriversLv.DrawItem += SupeyDriversLv_DrawItem;
            _supeyDriversLv.DrawSubItem += SupeyDriversLv_DrawSubItem;
            SupeyListViewHelpers.EnableDoubleBuffer(_supeyDriversLv);
            // CheckBoxes=true paints the box inside the first column; keep it narrow but with a
            // proper header so users see the on/off semantic.
            _supeyDriversColCheck = new ColumnHeader { Text = "Use", Width = 44 };
            _supeyDriversColName = new ColumnHeader { Text = "Driver", Width = 130 };
            _supeyDriversColCap = new ColumnHeader { Text = "Cap", Width = 44 };
            _supeyDriversColShift = new ColumnHeader { Text = "Shift", Width = 86 };
            _supeyDriversColRelease = new ColumnHeader { Text = "Release", Width = 76 };
            _supeyDriversLv.Columns.AddRange(new[]
            {
                _supeyDriversColCheck, _supeyDriversColName,
                _supeyDriversColCap, _supeyDriversColShift, _supeyDriversColRelease
            });
            _supeyDriversLv.DoubleClick += (s, e) => OnSupeyDriverEdit();
            // ItemChecked fires per-item during bulk Add() — the rebuild path uses
            // _supeySuppressItemChecked to mute it so we don't recompute button states N times
            // while items are still being constructed.
            _supeyDriversLv.ItemChecked += (s, e) =>
            {
                if (_supeySuppressItemChecked) return;
                UpdateSupeyButtonStates();
            };

            // Empty-state overlay — visible only while the roster is empty so the user knows
            // exactly where to start.
            _supeyDriversEmptyHint = new Label
            {
                Dock = DockStyle.Fill,
                Text = "No drivers in the roster\n\nADD a driver, or PULL FROM WELLRYDE",
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = SupeyTheme.TextSecondary,
                BackColor = SupeyTheme.ListBody,
                Font = new Font("Segoe UI", 10f),
                Visible = true,
            };

            host.Controls.Add(_supeyDriversEmptyHint);
            host.Controls.Add(_supeyDriversLv);
            host.Controls.Add(btnRow);
            _supeyDriversEmptyHint.BringToFront();

            // Apply the standard custom-listview behaviors: click-to-sort, content-driven min
            // column widths, dark header empty-area paint. Same treatment as the trips preview
            // ListView so the roster has consistent UX across the tab.
            try
            {
                ListViewSorter.Attach(_supeyDriversLv);
                ListViewMinWidthEnforcer.Attach(_supeyDriversLv);
                ListViewHeaderEmptyAreaPainter.Attach(_supeyDriversLv);
            }
            catch { }
        }

        private void BuildSupeyTripsPanel(Panel host)
        {
            // Top toolbar of the Trips panel: Driver dropdown on the left, status +
            // fleet totals stacked on the right. 56px tall now since the right side
            // carries two lines of context — the per-build status note ("Updated 12:34
            // · AI applied") on top and the fleet rollup ("Fleet 8h 30m · 145 mi ·
            // earliest 14:30") below it. This is the natural home for fleet stats —
            // they sit directly above the schedule they describe instead of being
            // exiled to a tiny bottom strip the user can't even see when collapsed.
            var topPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 56,
                BackColor = SupeyTheme.SurfaceHeader,
                Padding = new Padding(0),
            };
            var topDivider = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 1,
                BackColor = SupeyTheme.Divider,
            };

            var lbl = new Label
            {
                Text = "Driver",
                Location = new Point(12, 20),
                AutoSize = true,
                ForeColor = SupeyTheme.TextSecondary,
                BackColor = SupeyTheme.SurfaceHeader,
                Font = SupeyTheme.CaptionFont,
            };
            _supeyPreviewDriverCb = new ComboBox
            {
                Location = new Point(56, 15),
                Width = 360,
                Height = 26,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = SupeyTheme.SurfaceElevated,
                ForeColor = SupeyTheme.TextPrimary,
                FlatStyle = FlatStyle.Flat,
                DrawMode = DrawMode.OwnerDrawFixed,
                Font = SupeyTheme.BodyFont,
            };
            _supeyPreviewDriverCb.DrawItem += SupeyDarkComboDrawItem;
            _supeyPreviewDriverCb.SelectedIndexChanged += (s, e) => OnSupeyPreviewDriverChanged();

            // Right side: two stacked lines, both right-aligned, anchored Right so they
            // glide as the panel resizes. Top line — schedule-applied status. Bottom
            // line — fleet rollup. Bottom row is intentionally a hair brighter so the
            // numerical info reads first.
            _supeyScheduleUpdatedLbl = new MaterialLabel
            {
                Location = new Point(430, 8),
                AutoSize = false,
                Size = new Size(520, 20),
                Text = "No AI schedule on screen yet.",
                ForeColor = SupeyTheme.TextMuted,
                BackColor = SupeyTheme.SurfaceHeader,
                Font = SupeyTheme.CaptionFont,
                TextAlign = ContentAlignment.MiddleRight,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };
            _supeyStatsLbl = new MaterialLabel
            {
                Location = new Point(430, 28),
                AutoSize = false,
                Size = new Size(520, 22),
                Text = "",
                ForeColor = SupeyTheme.TextSecondary,
                BackColor = SupeyTheme.SurfaceHeader,
                Font = SupeyTheme.BodyFont,
                TextAlign = ContentAlignment.MiddleRight,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };
            topPanel.Controls.Add(lbl);
            topPanel.Controls.Add(_supeyPreviewDriverCb);
            topPanel.Controls.Add(_supeyScheduleUpdatedLbl);
            topPanel.Controls.Add(_supeyStatsLbl);
            topPanel.Controls.Add(topDivider);

            _supeyPreviewStatsLbl = new MaterialLabel
            {
                Dock = DockStyle.Bottom,
                Height = 28,
                Text = "",
                ForeColor = SupeyTheme.TextMuted,
                Padding = new Padding(12, 4, 12, 4),
                BackColor = SupeyTheme.SurfaceHeader,
                Font = SupeyTheme.CaptionFont,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
            };

            _supeyPreviewLv = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                BackColor = SupeyTheme.ListBody,
                ForeColor = SupeyTheme.ListText,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                MultiSelect = true,
                Font = new Font("Archivo Medium", 9.5f),
                OwnerDraw = true,
                HeaderStyle = ColumnHeaderStyle.Clickable,
            };
            _supeyPrevColGrp = new ColumnHeader { Text = "Grp", Width = 44 };
            _supeyPrevColTrip = new ColumnHeader { Text = "Trip #", Width = 88 };
            _supeyPrevColClient = new ColumnHeader { Text = "Client", Width = 140 };
            _supeyPrevColPUTime = new ColumnHeader { Text = "PU", Width = 58 };
            _supeyPrevColPUStreet = new ColumnHeader { Text = "Route", Width = -2 };
            _supeyPrevColPUCity = new ColumnHeader { Text = "PU→DO detail", Width = 0 };
            _supeyPrevColDOTime = new ColumnHeader { Text = "DO", Width = 58 };
            _supeyPrevColDOStreet = new ColumnHeader { Text = "PU St", Width = 0 };
            _supeyPrevColDOCity = new ColumnHeader { Text = "DO St", Width = 0 };
            _supeyPrevColMiles = new ColumnHeader { Text = "Mi", Width = 44 };
            _supeyPrevColGeo = new ColumnHeader { Text = "Geo", Width = 72 };
            _supeyPreviewLv.Columns.AddRange(new[]
            {
                _supeyPrevColGrp, _supeyPrevColTrip, _supeyPrevColClient, _supeyPrevColGeo,
                _supeyPrevColPUTime, _supeyPrevColPUStreet,
                _supeyPrevColDOTime, _supeyPrevColMiles,
            });
            _supeyPreviewLv.DrawColumnHeader += SupeyPreviewLv_DrawColumnHeader;
            _supeyPreviewLv.DrawItem += SupeyPreviewLv_DrawItem;
            _supeyPreviewLv.DrawSubItem += SupeyPreviewLv_DrawSubItem;
            // Owner-drawn details listviews single-buffer by default — first selection paint
            // after items load can flash gray-without-text. Double-buffering paints each row
            // off-screen and commits atomically so the user never sees the half-painted state.
            SupeyListViewHelpers.EnableDoubleBuffer(_supeyPreviewLv);

            // Empty-state hint over the trips area until a build runs. We toggle Visible
            // from RebuildPreviewDropdown / OnSupeyPreviewDriverChanged based on what's
            // loaded. Bg is the same (70,70,70) as the ListView so they read as one
            // continuous surface (per request: don't disturb ListView colors). The hint
            // is styled like a 3-step "getting started" callout instead of a wall of
            // numbered text.
            _supeyPreviewEmptyHint = new Label
            {
                Dock = DockStyle.Fill,
                Text = "No trips on screen yet\n\n"
                       + "①  LOAD TRIPS — pulls Modivcare trips for the chosen date\n"
                       + "②  Pick drivers in the roster on the left\n"
                       + "③  BUILD — schedule appears here\n\n"
                       + "Talk to the AI on the right to refine after build.",
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = SupeyTheme.TextSecondary,
                BackColor = SupeyTheme.ListBody,
                Font = new Font("Segoe UI", 10.5f),
                Visible = true,
            };

            host.Controls.Add(_supeyPreviewEmptyHint);
            host.Controls.Add(_supeyPreviewLv);
            host.Controls.Add(_supeyPreviewStatsLbl);
            host.Controls.Add(topPanel);
            _supeyPreviewEmptyHint.BringToFront();

            try
            {
                ListViewSorter.Attach(_supeyPreviewLv);
                ListViewMinWidthEnforcer.Attach(_supeyPreviewLv);
                ListViewHeaderEmptyAreaPainter.Attach(_supeyPreviewLv);
            }
            catch { }

            BuildSupeyWarningsContextMenu();
            BuildSupeyTripsContextMenu();
        }

        // ----------------------------------------------------------------------
        // Warnings list right-click menu — Copy selected / Copy all / Clear all.
        // ----------------------------------------------------------------------

        private ContextMenuStrip _supeyWarningsCtxMenu;
        private ToolStripMenuItem _supeyWarningsCtxCopySelected;
        private ToolStripMenuItem _supeyWarningsCtxCopyAll;
        private ToolStripMenuItem _supeyWarningsCtxClear;

        /// <summary>
        /// Builds the dark-themed context menu shown when the user right-clicks the preview
        /// ListView while it's displaying warnings. The menu is gated by a MouseUp handler that
        /// checks the current dropdown selection — right-clicking inside a Driver or Reserves
        /// view never shows it, so the trips view stays uncluttered.
        /// </summary>
        private void BuildSupeyWarningsContextMenu()
        {
            _supeyWarningsCtxMenu = new ContextMenuStrip
            {
                Renderer = new DarkContextMenuRenderer(),
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
                ShowImageMargin = true,
            };

            _supeyWarningsCtxCopySelected = new ToolStripMenuItem("Copy selected warning")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
                ShortcutKeys = Keys.Control | Keys.C,
                ShowShortcutKeys = true,
                Image = MenuIconFactory.GetCopyIcon(),
            };
            _supeyWarningsCtxCopySelected.Click += (s, e) => CopyWarningsToClipboard(selectedOnly: true);

            _supeyWarningsCtxCopyAll = new ToolStripMenuItem("Copy all warnings")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
                ShortcutKeys = Keys.Control | Keys.Shift | Keys.C,
                ShowShortcutKeys = true,
                Image = MenuIconFactory.GetCopyAllIcon(),
            };
            _supeyWarningsCtxCopyAll.Click += (s, e) => CopyWarningsToClipboard(selectedOnly: false);

            _supeyWarningsCtxClear = new ToolStripMenuItem("Clear all warnings")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
                Image = MenuIconFactory.GetClearIcon(),
            };
            _supeyWarningsCtxClear.Click += (s, e) => ClearAllWarnings();

            _supeyWarningsCtxMenu.Items.Add(_supeyWarningsCtxCopySelected);
            _supeyWarningsCtxMenu.Items.Add(_supeyWarningsCtxCopyAll);
            _supeyWarningsCtxMenu.Items.Add(new ToolStripSeparator());
            _supeyWarningsCtxMenu.Items.Add(_supeyWarningsCtxClear);

            // Gate visibility on dropdown selection so the menu only appears in Warnings mode.
            // Hooking MouseUp instead of ContextMenuStrip lets us inspect the click at runtime;
            // the ListView's ContextMenuStrip property would show it unconditionally.
            _supeyPreviewLv.MouseUp += SupeyPreviewLv_MouseUp_HandleWarningsContext;
            _supeyPreviewLv.SelectedIndexChanged += SupeyPreviewLv_SelectedTripChanged;
            _supeyPreviewLv.DoubleClick += SupeyPreviewLv_DoubleClickTrip;
        }

        private void SupeyPreviewLv_MouseUp_HandleWarningsContext(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;
            if (_supeyResult == null) return;
            if (!(_supeyPreviewDriverCb?.SelectedItem is SupeyPreviewItem itm)) return;

            if (itm.Kind == SupeyPreviewItem.ItemKind.Warnings)
            {
                int selected = _supeyPreviewLv.SelectedItems.Count;
                _supeyWarningsCtxCopySelected.Enabled = selected > 0;
                _supeyWarningsCtxCopySelected.Text = selected > 1
                    ? "Copy " + selected + " selected warnings"
                    : "Copy selected warning";
                _supeyWarningsCtxCopyAll.Enabled = _supeyPreviewLv.Items.Count > 0;
                _supeyWarningsCtxClear.Enabled = _supeyResult.WarningCount > 0;
                _supeyWarningsCtxMenu.Show(_supeyPreviewLv, e.Location);
                return;
            }

            // Driver / Reserves view → show the trips context menu, with menu labels rewritten
            // to reflect what the user is actually copying.
            ShowSupeyTripsContextMenu(itm, e.Location);
        }

        /// <summary>
        /// Serializes warnings to a tab-separated table on the clipboard so they paste cleanly into
        /// Excel / a ticket / a chat. Header row included; <paramref name="selectedOnly"/> is true
        /// for the "Copy selected" menu item, false for "Copy all".
        /// </summary>
        private void CopyWarningsToClipboard(bool selectedOnly)
        {
            if (_supeyPreviewLv == null || _supeyPreviewLv.Items.Count == 0) return;

            IEnumerable<ListViewItem> rows;
            if (selectedOnly)
            {
                if (_supeyPreviewLv.SelectedItems.Count == 0) return;
                var list = new List<ListViewItem>(_supeyPreviewLv.SelectedItems.Count);
                foreach (ListViewItem r in _supeyPreviewLv.SelectedItems) list.Add(r);
                rows = list;
            }
            else
            {
                var list = new List<ListViewItem>(_supeyPreviewLv.Items.Count);
                foreach (ListViewItem r in _supeyPreviewLv.Items) list.Add(r);
                rows = list;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Kind\tTrip #\tDriver / Scope\tDetail");
            foreach (var r in rows)
            {
                if (r == null) continue;
                var w = r.Tag as SupeyWarning;
                string kind = r.SubItems.Count > 0 ? (r.SubItems[0].Text ?? "") : "";
                string trip = r.SubItems.Count > 1 ? (r.SubItems[1].Text ?? "") : "";
                string scope = r.SubItems.Count > 2 ? (r.SubItems[2].Text ?? "") : "";
                string detail = w?.Detail ?? (r.SubItems.Count > 4 ? (r.SubItems[4].Text ?? "") : "");
                // Strip tabs/newlines from any field so the TSV survives a paste into Excel.
                sb.Append(Sanitize(kind)).Append('\t')
                  .Append(Sanitize(trip)).Append('\t')
                  .Append(Sanitize(scope)).Append('\t')
                  .Append(Sanitize(detail))
                  .AppendLine();
            }
            try
            {
                Clipboard.SetText(sb.ToString());
                int count = (rows is ICollection<ListViewItem> coll) ? coll.Count : 0;
                if (count == 0) foreach (var _ in rows) count++;
                SetSupeyStatus("Copied " + count + " warning" + (count == 1 ? "" : "s") + " to the clipboard.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not copy to clipboard:\n\n" + ex.Message, "Supey Schedule",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        }

        // ----------------------------------------------------------------------
        // Trips list right-click menu — Copy this driver / Copy all drivers.
        // Used so the user can grab the schedule, paste it back into the chat,
        // and we can A/B against the historical 2026 schedules to figure out
        // where the auto-builder differs from the dispatcher's real-world calls.
        // ----------------------------------------------------------------------

        private ContextMenuStrip _supeyTripsCtxMenu;
        private ToolStripMenuItem _supeyTripsCtxCopyThis;
        private ToolStripMenuItem _supeyTripsCtxCopyAll;
        private ToolStripMenuItem _supeyTripsCtxCopyCompare;
        private SupeyTemplateCompare _supeyLastTemplateCompare;

        private void BuildSupeyTripsContextMenu()
        {
            _supeyTripsCtxMenu = new ContextMenuStrip
            {
                Renderer = new DarkContextMenuRenderer(),
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
                ShowImageMargin = true,
            };

            _supeyTripsCtxCopyThis = new ToolStripMenuItem("Copy this driver's schedule")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
                ShortcutKeys = Keys.Control | Keys.C,
                ShowShortcutKeys = true,
                Image = MenuIconFactory.GetCopyIcon(),
            };
            _supeyTripsCtxCopyThis.Click += (s, e) => CopyCurrentScheduleToClipboard();

            _supeyTripsCtxCopyAll = new ToolStripMenuItem("Copy schedule for all drivers")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
                ShortcutKeys = Keys.Control | Keys.Shift | Keys.C,
                ShowShortcutKeys = true,
                Image = MenuIconFactory.GetCopyAllIcon(),
            };
            _supeyTripsCtxCopyAll.Click += (s, e) => CopyAllSchedulesToClipboard();

            _supeyTripsCtxCopyCompare = new ToolStripMenuItem("Copy template compare (TSV)")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
                Image = MenuIconFactory.GetCopyIcon(),
            };
            _supeyTripsCtxCopyCompare.Click += (s, e) =>
            {
                if (_supeyLastTemplateCompare == null) return;
                try
                {
                    Clipboard.SetText(_supeyLastTemplateCompare.ToTabSeparatedSummary());
                    SetSupeyStatus("Template compare copied to clipboard.");
                }
                catch (Exception ex)
                {
                    SetSupeyStatus("Could not copy: " + ex.Message);
                }
            };

            _supeyTripsCtxMenu.Items.Add(_supeyTripsCtxCopyThis);
            _supeyTripsCtxMenu.Items.Add(_supeyTripsCtxCopyAll);
            _supeyTripsCtxMenu.Items.Add(new ToolStripSeparator());
            _supeyTripsCtxMenu.Items.Add(_supeyTripsCtxCopyCompare);
        }

        private void ShowSupeyTripsContextMenu(SupeyPreviewItem itm, System.Drawing.Point location)
        {
            // Tailor the "Copy this..." label to whatever the user is currently looking at.
            // Reserves view has no driver name, so we frame it as "Copy reserves list" — same
            // gesture, different scope. Disable when there's nothing to copy so the menu
            // can't paste an empty TSV onto the clipboard.
            if (itm.Kind == SupeyPreviewItem.ItemKind.Reserves)
            {
                _supeyTripsCtxCopyThis.Text = "Copy reserves list";
                _supeyTripsCtxCopyThis.Enabled = _supeyResult.Reserves.Count > 0;
            }
            else
            {
                string driverName = itm.Plan?.Driver?.Name;
                _supeyTripsCtxCopyThis.Text = string.IsNullOrEmpty(driverName)
                    ? "Copy this driver's schedule"
                    : "Copy " + driverName + "'s schedule";
                _supeyTripsCtxCopyThis.Enabled = itm.Plan != null && itm.Plan.Groups.Count > 0;
            }
            _supeyTripsCtxCopyAll.Enabled = _supeyResult.DriverPlans.Count > 0
                || _supeyResult.Reserves.Count > 0;
            _supeyTripsCtxCopyCompare.Enabled = _supeyLastTemplateCompare != null && _supeyLastTemplateCompare.HadTemplates;

            _supeyTripsCtxMenu.Show(_supeyPreviewLv, location);
        }

        /// <summary>
        /// Copies just the currently-selected driver's schedule (or the Reserves list, if that's
        /// what's on screen) to the clipboard as TSV. Header row + "=== driver name ===" banner
        /// so the user can paste several copies into one chat message and the boundaries stay
        /// readable. Falls through silently when nothing is selected.
        /// </summary>
        private void CopyCurrentScheduleToClipboard()
        {
            if (_supeyResult == null) return;
            if (!(_supeyPreviewDriverCb?.SelectedItem is SupeyPreviewItem itm)) return;

            var sb = new System.Text.StringBuilder();
            sb.Append("Service date: ").AppendLine(_supeyResult.ServiceDate.ToString("yyyy-MM-dd"));
            sb.AppendLine();

            string descriptor;
            if (itm.Kind == SupeyPreviewItem.ItemKind.Reserves)
            {
                AppendReservesToClipboard(sb);
                descriptor = "reserves list";
            }
            else if (itm.Plan != null)
            {
                AppendDriverScheduleToClipboard(sb, itm.Plan);
                descriptor = "schedule for " + (itm.Plan.Driver?.Name ?? "driver");
            }
            else
            {
                return;
            }

            try
            {
                Clipboard.SetText(sb.ToString());
                SetSupeyStatus("Copied " + descriptor + " to the clipboard.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not copy to clipboard:\n\n" + ex.Message, "Supey Schedule",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>
        /// Copies every driver's schedule (and Reserves) into one big TSV blob — a section per
        /// driver, separated by "=== name ===" banners. This is the form the user pastes back
        /// into chat so we can compare the auto-built day against the historical 2026 schedules
        /// without making them flip through the dropdown.
        /// </summary>
        private void CopyAllSchedulesToClipboard()
        {
            if (_supeyResult == null) return;

            var sb = new System.Text.StringBuilder();
            sb.Append("Service date: ").AppendLine(_supeyResult.ServiceDate.ToString("yyyy-MM-dd"));
            sb.Append("Drivers: ").Append(_supeyResult.DriverPlans.Count)
              .Append(", reserves: ").Append(_supeyResult.Reserves.Count).AppendLine();
            if (_supeyResult.FleetActiveSeconds > 0)
            {
                sb.Append("Fleet active: ")
                  .Append(SupeyTripTimes.FormatHoursMinutesFromSeconds(_supeyResult.FleetActiveSeconds))
                  .Append(" · ").Append(SupeyTripTimes.FormatMiles(_supeyResult.FleetMeters)).AppendLine();
            }
            sb.AppendLine();

            int driversWithTrips = 0;
            foreach (var plan in _supeyResult.DriverPlans)
            {
                AppendDriverScheduleToClipboard(sb, plan);
                if (plan.Groups.Count > 0) driversWithTrips++;
            }
            if (_supeyResult.Reserves.Count > 0)
                AppendReservesToClipboard(sb);

            try
            {
                Clipboard.SetText(sb.ToString());
                SetSupeyStatus("Copied " + driversWithTrips + " driver schedule(s)" +
                    (_supeyResult.Reserves.Count > 0
                        ? " + " + _supeyResult.Reserves.Count + " reserves"
                        : "") +
                    " to the clipboard.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not copy to clipboard:\n\n" + ex.Message, "Supey Schedule",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>
        /// Renders one driver's schedule as a markdown-friendly TSV block: a banner with the
        /// driver name + per-day stats, then a header row, then one row per trip in group +
        /// pickup-time order. Drivers with no trips assigned still get a banner so it's obvious
        /// in the dump that they participated in the build but weren't given anything.
        /// </summary>
        private static void AppendDriverScheduleToClipboard(System.Text.StringBuilder sb, SupeyDriverPlan plan)
        {
            string driverName = plan.Driver?.Name ?? "(driver)";
            if (plan.Groups.Count == 0)
            {
                sb.Append("=== ").Append(driverName).AppendLine(" === — no trips assigned");
                sb.AppendLine();
                return;
            }

            int riders = plan.RiderCount;
            int groups = plan.Groups.Count;
            sb.Append("=== ").Append(driverName).Append(" === (")
              .Append(riders).Append(" trip").Append(riders == 1 ? "" : "s")
              .Append(", ").Append(groups).Append(" group").Append(groups == 1 ? "" : "s");
            if (plan.FirstPickup.HasValue)
                sb.Append(", first PU ").Append(SupeyTripTimes.FormatTimeOfDay(plan.FirstPickup.Value));
            if (plan.LastDropoff.HasValue)
                sb.Append(", last DO ").Append(SupeyTripTimes.FormatTimeOfDay(plan.LastDropoff.Value));
            if (plan.ReleaseTimeOfDay.HasValue)
                sb.Append(", release ").Append(SupeyTripTimes.FormatTimeOfDay(plan.ReleaseTimeOfDay.Value));
            sb.Append(", ").Append(SupeyTripTimes.FormatHoursMinutesFromSeconds(plan.TotalDriveSeconds))
              .Append(" / ").Append(SupeyTripTimes.FormatMiles(plan.TotalMeters));
            sb.AppendLine(")");

            sb.AppendLine("Grp\tTrip #\tClient\tPU Time\tPU Street\tPU City\tDO Time\tDO Street\tDO City\tMiles");
            foreach (var g in plan.Groups)
            {
                foreach (var t in g.Trips)
                {
                    sb.Append(g.GroupNumber).Append('\t')
                      .Append(Sanitize(t.TripNumber)).Append('\t')
                      .Append(Sanitize(t.ClientFullName)).Append('\t')
                      .Append(Sanitize(t.PUTime)).Append('\t')
                      .Append(Sanitize(t.PUStreet)).Append('\t')
                      .Append(Sanitize(t.PUCity)).Append('\t')
                      .Append(Sanitize(t.DOTime)).Append('\t')
                      .Append(Sanitize(t.DOStreet)).Append('\t')
                      .Append(Sanitize(t.DOCITY)).Append('\t')
                      .Append(Sanitize(t.Miles))
                      .AppendLine();
                }
            }
            sb.AppendLine();
        }

        private void AppendReservesToClipboard(System.Text.StringBuilder sb)
        {
            sb.Append("=== RESERVES === (")
              .Append(_supeyResult.Reserves.Count)
              .Append(" trip").Append(_supeyResult.Reserves.Count == 1 ? "" : "s")
              .AppendLine(" left unassigned)");
            sb.AppendLine("Trip #\tClient\tPU Time\tPU Street\tPU City\tDO Time\tDO Street\tDO City\tMiles");
            foreach (var t in _supeyResult.Reserves)
            {
                sb.Append(Sanitize(t.TripNumber)).Append('\t')
                  .Append(Sanitize(t.ClientFullName)).Append('\t')
                  .Append(Sanitize(t.PUTime)).Append('\t')
                  .Append(Sanitize(t.PUStreet)).Append('\t')
                  .Append(Sanitize(t.PUCity)).Append('\t')
                  .Append(Sanitize(t.DOTime)).Append('\t')
                  .Append(Sanitize(t.DOStreet)).Append('\t')
                  .Append(Sanitize(t.DOCITY)).Append('\t')
                  .Append(Sanitize(t.Miles))
                  .AppendLine();
            }
            sb.AppendLine();
        }

        /// <summary>
        /// Wipes every warning out of the current build result (both the build-level list and each
        /// driver plan's list) and refreshes the preview UI. The schedule itself is unchanged — only
        /// the diagnostic messages disappear, useful when the user has reviewed them and wants the
        /// view to stop nagging.
        /// </summary>
        private void ClearAllWarnings()
        {
            if (_supeyResult == null || _supeyResult.WarningCount == 0) return;

            var confirm = MessageBox.Show(this,
                "Clear all " + _supeyResult.WarningCount + " warnings from this build?\n\n" +
                "This only removes the diagnostic messages — your schedule (drivers, groups, and reserves) is not affected.",
                "Clear warnings", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.Yes) return;

            int removed = _supeyResult.WarningCount;
            _supeyResult.BuildWarnings.Clear();
            foreach (var p in _supeyResult.DriverPlans) p.Warnings.Clear();

            // The Warnings dropdown entry no longer makes sense; rebuild the preview so it
            // disappears, then BindSupeyPreview will drop the user back onto the first available
            // view (driver 0 or reserves).
            BindSupeyPreview();
            // Refresh the bottom-right status link too.
            if (_supeyWarningsLink != null) _supeyWarningsLink.Text = "0 warnings";
            SetSupeyStatus("Cleared " + removed + " warning" + (removed == 1 ? "" : "s") + ".");
        }

        private void BuildSupeyMapPanel(Panel host)
        {
            _supeyMap = new SupeyMapWorkspace { Dock = DockStyle.Fill };
            _supeyMap.SetSupeyStatusOnHost = msg => SetSupeyStatus(msg);
            host.Controls.Add(_supeyMap);
        }

        /// <summary>
        /// Owner-draw handler that paints a ComboBox row in our dark palette. Hook this on
        /// any ComboBox where we want it to actually look dark — DropDownList combos ignore
        /// BackColor on modern Windows themes, so we have to paint the surface ourselves.
        /// </summary>
        private void SupeyDarkComboDrawItem(object sender, DrawItemEventArgs e)
        {
            if (!(sender is ComboBox cb)) return;
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            Color bg = selected ? Color.FromArgb(80, 100, 130) : Color.FromArgb(60, 60, 60);
            Color fg = Color.Gainsboro;
            using (var b = new SolidBrush(bg))
                e.Graphics.FillRectangle(b, e.Bounds);

            string text = "";
            if (e.Index >= 0 && e.Index < cb.Items.Count)
            {
                text = cb.GetItemText(cb.Items[e.Index]) ?? "";
            }
            var bounds = new Rectangle(e.Bounds.Left + 4, e.Bounds.Top, e.Bounds.Width - 4, e.Bounds.Height);
            TextRenderer.DrawText(
                e.Graphics, text, cb.Font, bounds, fg,
                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.WordEllipsis);
        }

        private static MaterialButton MakeFlatButton(string text, int x, int y, int width)
        {
            var b = new MaterialButton
            {
                Text = text,
                Location = new Point(x, y),
                AutoSize = false,
                Size = new Size(width, 32),
                Type = MaterialButton.MaterialButtonType.Contained,
                Density = MaterialButton.MaterialButtonDensity.Default,
                UseAccentColor = false,
                HighEmphasis = true,
            };
            return b;
        }

        // ---------- Roster ----------

        private void LoadSupeyRosterFromDisk()
        {
            string warning;
            _supeyRoster = SupeyDriverRosterStore.Load(out warning);
            if (!string.IsNullOrEmpty(warning))
            {
                SetSupeyStatus(warning);
            }
            RebuildSupeyDriversList();
        }

        private void SaveSupeyRosterToDisk(bool showOk)
        {
            // Persist whatever the ListView currently knows about (caller should already have
            // mutated _supeyRoster for any add/edit/remove before calling).
            var saved = SupeyDriverRosterStore.Save(_supeyRoster);
            if (saved.Ok)
            {
                _supeyRosterLastSaved = saved.SavedAtLocal;
                _supeyRosterFooter.Text = _supeyRoster.Count + " drivers · saved " + saved.SavedAtLocal.ToString("HH:mm");
                if (showOk) SetSupeyStatus("Roster saved.");
            }
            else
            {
                SetSupeyStatus(saved.ErrorMessage);
            }
        }

        private bool _supeySuppressItemChecked;

        private void RebuildSupeyDriversList()
        {
            if (_supeyDriversLv == null) return;
            _supeySuppressItemChecked = true;
            try
            {
                _supeyDriversLv.BeginUpdate();
                _supeyDriversLv.Items.Clear();
                foreach (var d in _supeyRoster)
                {
                    string shift = (d.ShiftStart ?? "—") + "-" + (d.ShiftEnd ?? "—");
                    var item = new ListViewItem(new[] { "", d.Name ?? "", d.CapacityPassengers.ToString(), shift, "" })
                    {
                        Tag = d,
                        Checked = true,
                    };
                    _supeyDriversLv.Items.Add(item);
                }
                _supeyDriversLv.EndUpdate();
            }
            finally
            {
                _supeySuppressItemChecked = false;
            }
            // Auto-fit each column to the widest header / cell after binding so long driver names
            // and shift strings aren't clipped. Same pattern used by every other listview on Form1.
            ListViewMinWidthEnforcer.ScheduleRecompute(_supeyDriversLv);
            _supeyRosterFooter.Text = _supeyRoster.Count + " drivers" +
                (_supeyRosterLastSaved == DateTime.MinValue ? "" : " · saved " + _supeyRosterLastSaved.ToString("HH:mm"));
            if (_supeyDriversEmptyHint != null)
                _supeyDriversEmptyHint.Visible = _supeyRoster.Count == 0;
            UpdateSupeyButtonStates();
        }

        private void OnSupeyDriverAdd()
        {
            using (var ed = new SupeyDriverEditorForm(null))
            {
                if (ed.ShowDialog(this) != DialogResult.OK || ed.Result == null) return;
                _supeyRoster.Add(ed.Result);
                RebuildSupeyDriversList();
                SaveSupeyRosterToDisk(showOk: false);
            }
        }

        private void OnSupeyDriverEdit()
        {
            if (_supeyDriversLv.SelectedItems.Count == 0) return;
            var item = _supeyDriversLv.SelectedItems[0];
            var existing = item.Tag as SupeyDriverProfile;
            if (existing == null) return;

            using (var ed = new SupeyDriverEditorForm(existing))
            {
                if (ed.ShowDialog(this) != DialogResult.OK || ed.Result == null) return;
                int idx = _supeyRoster.IndexOf(existing);
                if (idx >= 0) _supeyRoster[idx] = ed.Result;
                RebuildSupeyDriversList();
                SaveSupeyRosterToDisk(showOk: false);
            }
        }

        private void OnSupeyDriverRemove()
        {
            if (_supeyDriversLv.SelectedItems.Count == 0) return;
            var item = _supeyDriversLv.SelectedItems[0];
            var existing = item.Tag as SupeyDriverProfile;
            if (existing == null) return;

            var dr = MessageBox.Show(this,
                "Remove " + existing.Name + " from the roster?",
                "Remove driver", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (dr != DialogResult.Yes) return;

            _supeyRoster.Remove(existing);
            RebuildSupeyDriversList();
            SaveSupeyRosterToDisk(showOk: false);
        }

        /// <summary>
        /// Opens the WellRyde driver-import picker, lets the user pick which active drivers to
        /// add to the roster, and merges the selected detail records into <see cref="_supeyRoster"/>
        /// (matching by <see cref="SupeyDriverProfile.WellRydeSecId"/> so re-pulls update existing
        /// rows rather than duplicating them). Falls back to "match by name" for any drivers added
        /// manually before this feature shipped — a courtesy to existing rosters.
        /// </summary>
        /// <remarks>
        /// Login is gated by <see cref="EnsureWellRydePortalSessionForBillingAsync"/>, the same
        /// flow Trip Scout / billing use, so the user gets a familiar prompt if they're not
        /// signed in. The persisted JSON is rewritten in one shot at the end so a crash midway
        /// never leaves a half-merged roster on disk.
        /// </remarks>
        private async Task OnSupeyPullFromWellRydeAsync()
        {
            // Reuse the existing WellRyde gate; it handles "session expired" → re-prompt /
            // "no creds saved" → MessageBox automatically. Returns false on user-visible failure.
            bool ok;
            try
            {
                ok = await EnsureWellRydePortalSessionForBillingAsync();
            }
            catch (Exception ex)
            {
                SetSupeyStatus("WellRyde sign-in failed: " + (ex.Message ?? "unknown error"));
                return;
            }
            if (!ok || _wellRydeSession == null)
            {
                SetSupeyStatus("WellRyde sign-in cancelled or failed.");
                return;
            }

            var alreadyImportedSecIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in _supeyRoster)
            {
                if (d != null && !string.IsNullOrEmpty(d.WellRydeSecId))
                    alreadyImportedSecIds.Add(d.WellRydeSecId);
            }

            using (var dlg = new SupeyImportFromWellRydeForm(_wellRydeSession, alreadyImportedSecIds))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK)
                    return;
                var picks = dlg.SelectedDetails ?? new List<WellRydeUserDetail>();
                if (picks.Count == 0) return;

                int added = 0;
                int updated = 0;
                foreach (var detail in picks)
                {
                    if (detail == null || string.IsNullOrEmpty(detail.SecId)) continue;
                    var existing = FindRosterDriverBySecIdOrName(detail.SecId, detail.FullName);
                    if (existing != null)
                    {
                        ApplyWellRydeDetailToProfile(detail, existing);
                        updated++;
                    }
                    else
                    {
                        var profile = new SupeyDriverProfile();
                        ApplyWellRydeDetailToProfile(detail, profile);
                        _supeyRoster.Add(profile);
                        added++;
                    }
                }

                RebuildSupeyDriversList();
                SaveSupeyRosterToDisk(showOk: false);

                string msg = "Imported from WellRyde: " + added + " new" +
                    (updated > 0 ? ", " + updated + " refreshed" : "") + ".";
                SetSupeyStatus(msg);
            }
        }

        /// <summary>
        /// Match an incoming WellRyde detail back to a roster row. Prefer SEC id (stable),
        /// fall back to a case-insensitive name match so manually-entered drivers get linked up
        /// to their WellRyde record on first import.
        /// </summary>
        private SupeyDriverProfile FindRosterDriverBySecIdOrName(string secId, string fullName)
        {
            if (!string.IsNullOrEmpty(secId))
            {
                foreach (var d in _supeyRoster)
                {
                    if (d != null && string.Equals(d.WellRydeSecId, secId, StringComparison.OrdinalIgnoreCase))
                        return d;
                }
            }
            string name = (fullName ?? "").Trim();
            if (name.Length > 0)
            {
                foreach (var d in _supeyRoster)
                {
                    if (d != null && string.Equals((d.Name ?? "").Trim(), name, StringComparison.OrdinalIgnoreCase))
                        return d;
                }
            }
            return null;
        }

        /// <summary>
        /// Copies fields from a WellRyde <see cref="WellRydeUserDetail"/> into a roster profile.
        /// Address / name / vehicle label come straight from WellRyde; capacity and shift are
        /// preserved from the existing profile when re-syncing (or seeded with defaults on first
        /// import) since WellRyde doesn't expose those.
        /// </summary>
        private static void ApplyWellRydeDetailToProfile(WellRydeUserDetail detail, SupeyDriverProfile profile)
        {
            if (detail == null || profile == null) return;

            profile.WellRydeSecId = detail.SecId ?? "";
            profile.WellRydeUsername = detail.Username ?? "";
            profile.WellRydeSyncedAtUtc = DateTime.UtcNow;
            profile.Name = !string.IsNullOrWhiteSpace(detail.FullName) ? detail.FullName.Trim() : profile.Name;

            // Address: take WellRyde's value if present, otherwise leave the existing local value
            // alone — protects manual edits the user made before the import.
            string street = (detail.FullStreet ?? "").Trim();
            if (street.Length > 0) profile.HomeStreet = street;
            string city = (detail.City ?? "").Trim();
            if (city.Length > 0) profile.HomeCity = city;
            string state = (detail.State ?? "").Trim();
            if (state.Length > 0) profile.HomeState = state;
            string zip = (detail.Zip ?? "").Trim();
            if (zip.Length > 0) profile.HomeZip = zip;

            string vehicleLabel = (detail.VehicleLabel ?? "").Trim();
            if (vehicleLabel.Length > 0 && string.IsNullOrWhiteSpace(profile.VehicleLabel))
                profile.VehicleLabel = vehicleLabel;

            // Capacity and shift defaults only on first creation — the test for "is this a fresh
            // profile" is that capacity is still the constructor default 0 (would be 4 from the
            // SupeyDriverProfile default initializer if any other code touched it).
            if (profile.CapacityPassengers <= 0) profile.CapacityPassengers = 4;
            if (string.IsNullOrWhiteSpace(profile.ShiftStart)) profile.ShiftStart = "06:00";
            if (string.IsNullOrWhiteSpace(profile.ShiftEnd)) profile.ShiftEnd = "18:00";
        }

        // ---------- Load / Build / Save / Cancel ----------

        private async Task OnSupeyLoadClickedAsync()
        {
            if (_supeyCts != null) return;
            _supeyCts = new CancellationTokenSource();
            try
            {
                SetSupeyToolbarBusy(true, "Loading Modivcare trips...");
                if (!await EnsureModivcareSessionAsync())
                {
                    SetSupeyStatus("Modivcare sign-in required.");
                    return;
                }
                var date = _supeyDatePicker.Value;
                _supeyLoadedTrips = await SupeyScheduleBuilder.DownloadTripsAsync(date, mcLoginHandler);
                _supeyResult = null;
                _supeyTripsPanelView = SupeyTripsPanelView.LoadedPool;
                ClearSupeyScheduleUpdatedLabel();
                BindSupeyLoadedTripsList();
                SetSupeyStatus(BuildPostLoadStatus(_supeyLoadedTrips.Count, date));
                _ = ShowSupeyPreReviewWarningsAsync();
            }
            catch (ScheduleBuilderException ex)
            {
                MessageBox.Show(this, ex.Message, "Supey Schedule", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SetSupeyStatus("Trip load failed.");
            }
            catch (OperationCanceledException)
            {
                SetSupeyStatus("Load canceled.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Unexpected error loading trips:\n\n" + ex.Message, "Supey Schedule",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetSupeyStatus("Trip load failed.");
            }
            finally
            {
                // Order matters: clear _supeyCts BEFORE refreshing the toolbar. UpdateSupeyButtonStates
                // gates BUILD on `_supeyCts == null`, so refreshing first leaves the button disabled
                // even after a successful load. (Lost ~10 minutes to this — leaving a comment.)
                try { _supeyCts?.Dispose(); } catch { }
                _supeyCts = null;
                SetSupeyToolbarBusy(false, null);
            }
        }

        private async Task OnSupeyBuildClickedAsync()
        {
            if (_supeyCts != null) return;
            if (_supeyLoadedTrips == null || _supeyLoadedTrips.Count == 0)
            {
                SetSupeyStatus("No trips loaded — click Load Trips first.");
                return;
            }
            var selected = GetCheckedSupeyDrivers();
            if (selected.Count == 0)
            {
                SetSupeyStatus("Check at least one driver in the roster.");
                return;
            }

            _supeyCts = new CancellationTokenSource();
            var token = _supeyCts.Token;

            try
            {
                var date = _supeyDatePicker.Value;
                SetSupeyToolbarBusy(true, "Building schedule (geocode + assign)…");

                if (_supeyAiSettings == null)
                    _supeyAiSettings = HiatmeAiSettings.Load();

                SupeyScheduleRules scheduleRules = null;
                try
                {
                    var pre = await HiatmeAiClient.PreReviewAsync(_supeyAiSettings, token).ConfigureAwait(true);
                    if (pre?.RulesContext != null)
                        scheduleRules = SupeyScheduleRules.FromRulesContext(pre.RulesContext);
                }
                catch { /* BUILD proceeds without remote rules if panel is down */ }

                var hints = new SupeyTemplateHints(date.DayOfWeek.ToString());
                var algo = new SupeyScheduleAlgorithm
                {
                    Hints = hints,
                    UseTemplateHints = false,
                    ScheduleRules = scheduleRules,
                };
                var startingLocks = _supeyResult?.Locks ?? new Dictionary<string, string>();
                var progress = new Progress<string>(msg =>
                {
                    try
                    {
                        if (IsHandleCreated && !IsDisposed)
                            BeginInvoke((Action)(() => SetSupeyToolbarBusy(true, msg)));
                    }
                    catch { }
                });

                _supeyResult = await algo.BuildAsync(
                    date, _supeyLoadedTrips, selected, startingLocks, progress, token).ConfigureAwait(true);

                _supeyTripsPanelView = SupeyTripsPanelView.AiSchedule;
                BindSupeyPreview();
                _ = HydrateSupeyGeocodeForMapAsync();
                _supeyLastTemplateCompare = SupeyTemplateCompare.Run(_supeyResult, hints);
                if (_supeyTemplateCompareLbl != null)
                    _supeyTemplateCompareLbl.Text = _supeyLastTemplateCompare.SummaryText;
                SyncSupeyScheduleToServer("build");
                MarkSupeyScheduleUpdated("BUILD");
                SetSupeyAiLastAppliedLabel("BUILD");

                int scheduled = HiatmeAiScheduleMapper.CountAssignedTrips(_supeyResult);
                SetSupeyStatus("Build complete. " + _supeyResult.DriverPlans.Count + " driver(s), " +
                    scheduled + " on screen, " + _supeyResult.Reserves.Count + " reserve(s), " +
                    _supeyResult.WarningCount + " warning(s).");

                _ = RequestAiBuildReviewAsync(date, selected, _supeyResult, _supeyAiSettings);
            }
            catch (OperationCanceledException)
            {
                SetSupeyStatus("Build canceled.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Build failed:\n\n" + ex.Message, "Supey Schedule",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetSupeyStatus("Build failed — see message above.");
            }
            finally
            {
                // Same ordering rule as OnSupeyLoadClickedAsync: dispose + null the CTS first
                // so the toolbar refresh sees `_supeyCts == null` and re-enables BUILD/SAVE.
                try { _supeyCts?.Dispose(); } catch { }
                _supeyCts = null;
                SetSupeyToolbarBusy(false, null);
            }
        }

        private async Task OnSupeySaveClickedAsync()
        {
            if (_supeyResult == null)
            {
                SetSupeyStatus("Nothing to save — click Build first.");
                return;
            }
            try
            {
                SetSupeyToolbarBusy(true, "Saving workbook...");
                await SupeyScheduleBuilder.SaveWorkbookAsync(_supeyResult, this);
                if (_supeyAiSettings == null)
                    _supeyAiSettings = HiatmeAiSettings.Load();
                try
                {
                    var ctx = HiatmeScheduleContextBuilder.Build(
                        _supeyDatePicker.Value,
                        _supeyRoster,
                        _supeyLoadedTrips,
                        _supeyResult,
                        true,
                        GetCheckedSupeyDrivers());
                    await HiatmeAiClient.SyncScheduleAsync(
                        _supeyAiSettings, ctx, "save").ConfigureAwait(true);
                }
                catch
                {
                    // non-fatal
                }
                if (_supeyAiSettings.RememberOnSave)
                {
                    try
                    {
                        var summary = HiatmeScheduleSummary.ForMemory(_supeyResult);
                        var dispCtx = new JObject();
                        ApplyWellRydeDispatcherToAiContext(dispCtx);
                        await HiatmeAiClient.AddMemoryAsync(
                            _supeyAiSettings, summary, dispCtx).ConfigureAwait(true);
                    }
                    catch
                    {
                        // non-fatal
                    }
                }
                SetSupeyStatus("Workbook saved.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not save workbook:\n\n" + ex.Message, "Supey Schedule",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetSupeyStatus("Save failed.");
            }
            finally
            {
                SetSupeyToolbarBusy(false, null);
            }
        }

        private void OnSupeyCancelClicked()
        {
            try { _supeyCts?.Cancel(); } catch { }
        }

        // ---------- Preview wiring ----------

        private void BindSupeyPreview()
        {
            _supeyPreviewDriverCb.Items.Clear();
            if (_supeyResult == null)
            {
                _supeyMap?.Clear();
                _supeyStatsLbl.Text = "";
                _supeyWarningsLink.Text = "0 warnings";
                if (_supeyTripsPanelView == SupeyTripsPanelView.LoadedPool
                    && (_supeyLoadedTrips?.Count ?? 0) > 0)
                {
                    BindSupeyLoadedTripsList();
                    return;
                }
                _supeyPreviewLv.Items.Clear();
                _supeyPreviewStatsLbl.Text = "";
                if (_supeyPreviewEmptyHint != null) _supeyPreviewEmptyHint.Visible = true;
                return;
            }

            _supeyTripsPanelView = SupeyTripsPanelView.AiSchedule;
            if (_supeyPreviewEmptyHint != null) _supeyPreviewEmptyHint.Visible = false;

            // Refresh per-driver release time in the roster ListView.
            foreach (ListViewItem item in _supeyDriversLv.Items)
            {
                if (item == null) continue;
                var d = item.Tag as SupeyDriverProfile;
                if (d == null) continue;
                var plan = _supeyResult.DriverPlans.FirstOrDefault(p => ReferenceEquals(p.Driver, d) ||
                    string.Equals(p.Driver?.Name, d.Name, StringComparison.OrdinalIgnoreCase));
                item.SubItems[4].Text = plan?.ReleaseTimeOfDay.HasValue == true
                    ? SupeyTripTimes.FormatTimeOfDay(plan.ReleaseTimeOfDay.Value) : "";
            }

            foreach (var plan in _supeyResult.DriverPlans)
            {
                string label = plan.Driver?.Name ?? "(driver)";
                if (plan.ReleaseTimeOfDay.HasValue)
                    label += " · done " + SupeyTripTimes.FormatTimeOfDay(plan.ReleaseTimeOfDay.Value);
                else if (plan.Groups.Count == 0)
                    label += " · no trips";
                _supeyPreviewDriverCb.Items.Add(new SupeyPreviewItem(plan, label));
            }
            if (_supeyResult.Reserves.Count > 0)
                _supeyPreviewDriverCb.Items.Add(new SupeyPreviewItem(null, "Reserves · " + _supeyResult.Reserves.Count + " trip(s)"));
            if (_supeyResult.WarningCount > 0)
                _supeyPreviewDriverCb.Items.Add(new SupeyPreviewItem(
                    SupeyPreviewItem.ItemKind.Warnings,
                    "Warnings · " + _supeyResult.WarningCount));

            // Drop the "Fleet: " prefix — the label sits inside the Trips header now,
            // directly above the schedule, so the context is unambiguous. We also fold
            // in the driver count and total trip count up front so the user sees the
            // scope of the build without having to expand the dropdown.
            int driverCount = _supeyResult.DriverPlans?.Count ?? 0;
            int tripCount = (_supeyResult.DriverPlans?.Sum(p => p?.Groups?.Sum(g => g?.Trips?.Count ?? 0) ?? 0) ?? 0)
                            + (_supeyResult.Reserves?.Count ?? 0);
            string fleet = driverCount + " driver" + (driverCount == 1 ? "" : "s")
                + " · " + tripCount + " trip" + (tripCount == 1 ? "" : "s")
                + " · " + SupeyTripTimes.FormatHoursMinutesFromSeconds(_supeyResult.FleetActiveSeconds) + " drive"
                + " · " + SupeyTripTimes.FormatMiles(_supeyResult.FleetMeters)
                + (_supeyResult.EarliestRelease.HasValue
                    ? " · earliest " + SupeyTripTimes.FormatTimeOfDay(_supeyResult.EarliestRelease.Value)
                    : "");
            _supeyStatsLbl.Text = fleet;
            _supeyWarningsLink.Text = _supeyResult.WarningCount + " warning" + (_supeyResult.WarningCount == 1 ? "" : "s");

            SelectSupeyPreviewDriverWithTrips();
        }

        private void SelectSupeyPreviewDriverWithTrips()
        {
            if (_supeyPreviewDriverCb == null || _supeyPreviewDriverCb.Items.Count == 0) return;
            int pick = 0;
            for (int i = 0; i < _supeyPreviewDriverCb.Items.Count; i++)
            {
                var pi = _supeyPreviewDriverCb.Items[i] as SupeyPreviewItem;
                if (pi?.Plan == null) continue;
                int n = pi.Plan.Groups?.Sum(g => g?.Trips?.Count ?? 0) ?? 0;
                if (n > 0)
                {
                    pick = i;
                    break;
                }
            }
            _supeyPreviewDriverCb.SelectedIndex = pick;
        }

        private void OnSupeyPreviewDriverChanged()
        {
            var item = _supeyPreviewDriverCb.SelectedItem as SupeyPreviewItem;
            _supeyPreviewLv.BeginUpdate();
            _supeyPreviewLv.Items.Clear();

            if (item == null)
            {
                _supeyPreviewLv.EndUpdate();
                _supeyMap.Clear();
                _supeyPreviewStatsLbl.Text = "";
                return;
            }

            if (item.Kind == SupeyPreviewItem.ItemKind.Warnings)
            {
                BindWarningsPreview();
                _supeyPreviewLv.EndUpdate();
                ListViewMinWidthEnforcer.ScheduleRecompute(_supeyPreviewLv);
                return;
            }

            if (item.Kind == SupeyPreviewItem.ItemKind.LoadedTrips)
            {
                BindLoadedTripsPreview();
                _supeyPreviewLv.EndUpdate();
                ListViewMinWidthEnforcer.ScheduleRecompute(_supeyPreviewLv);
                return;
            }

            if (item.Plan != null)
            {
                int rowIdx = 0;
                foreach (var g in item.Plan.Groups)
                {
                    for (int ti = 0; ti < g.Trips.Count; ti++)
                    {
                        var t = g.Trips[ti];
                        string route = ((t.PUCity ?? "").Trim() + " → " + (t.DOCITY ?? "").Trim()).Trim();
                        string geo = SupeyTripGeocodeStatus.ForScheduledTrip(t, g, item.Plan, ti);
                        var lvi = new ListViewItem(new[]
                        {
                            g.GroupNumber.ToString(),
                            t.TripNumber ?? "",
                            t.ClientFullName ?? "",
                            geo,
                            t.PUTime ?? "",
                            route,
                            t.DOTime ?? "",
                            t.Miles ?? "",
                        });
                        lvi.UseItemStyleForSubItems = false;
                        lvi.Tag = new SupeyPreviewRowTag(g, t, item.Plan, ti);
                        lvi.SubItems[0].BackColor = g.GroupColor;
                        lvi.SubItems[0].ForeColor = Color.Black;
                        StyleGeoSubItem(lvi.SubItems[SupeyPrevColGeoIndex], geo);
                        _supeyPreviewLv.Items.Add(lvi);
                        rowIdx++;
                    }
                }
                _supeyMap.ShowDriverPlan(item.Plan);
                FocusPreviewTripRow(_supeyPreviewLv.SelectedItems.Count > 0 ? _supeyPreviewLv.SelectedItems[0] : null);
                _supeyPreviewStatsLbl.Text = "Trips: " + item.Plan.RiderCount + " · groups: " + item.Plan.Groups.Count +
                    " · drive " + SupeyTripTimes.FormatHoursMinutesFromSeconds(item.Plan.TotalDriveSeconds) +
                    " · " + SupeyTripTimes.FormatMiles(item.Plan.TotalMeters) +
                    (item.Plan.LastDropoff.HasValue ? " · last DO " + SupeyTripTimes.FormatTimeOfDay(item.Plan.LastDropoff.Value) : "") +
                    (item.Plan.ReleaseTimeOfDay.HasValue ? " · release " + SupeyTripTimes.FormatTimeOfDay(item.Plan.ReleaseTimeOfDay.Value) : "") +
                    (item.Plan.Warnings.Count > 0 ? " · " + item.Plan.Warnings.Count + " warning(s)" : "");
            }
            else
            {
                // Reserves
                foreach (var t in _supeyResult.Reserves)
                {
                    string route = ((t.PUCity ?? "").Trim() + " → " + (t.DOCITY ?? "").Trim()).Trim();
                    var lvi = new ListViewItem(new[]
                    {
                        "—",
                        t.TripNumber ?? "",
                        t.ClientFullName ?? "",
                        SupeyTripGeocodeStatus.CheckPin,
                        t.PUTime ?? "",
                        route,
                        t.DOTime ?? "",
                        t.Miles ?? "",
                    });
                    lvi.UseItemStyleForSubItems = false;
                    lvi.SubItems[0].BackColor = Color.DimGray;
                    lvi.SubItems[0].ForeColor = Color.White;
                    StyleGeoSubItem(lvi.SubItems[SupeyPrevColGeoIndex], SupeyTripGeocodeStatus.CheckPin);
                    _supeyPreviewLv.Items.Add(lvi);
                }
                _supeyMap.Clear();
                _supeyPreviewStatsLbl.Text = "Reserves: " + _supeyResult.Reserves.Count + " trip(s) — drag onto a driver above (manual reassign in a follow-up build).";
            }

            _supeyPreviewLv.EndUpdate();
            ListViewMinWidthEnforcer.ScheduleRecompute(_supeyPreviewLv);
        }

        /// <summary>Raw Modivcare download (not the AI schedule) — only before BUILD.</summary>
        private void BindSupeyLoadedTripsList()
        {
            if (_supeyPreviewDriverCb == null) return;
            int n = _supeyLoadedTrips?.Count ?? 0;
            _supeyPreviewDriverCb.Items.Clear();
            if (n == 0)
            {
                _supeyPreviewLv.Items.Clear();
                _supeyPreviewStatsLbl.Text = "";
                if (_supeyPreviewEmptyHint != null) _supeyPreviewEmptyHint.Visible = true;
                return;
            }

            _supeyPreviewDriverCb.Items.Add(new SupeyPreviewItem(
                SupeyPreviewItem.ItemKind.LoadedTrips,
                "Loaded pool (not scheduled) · " + n));
            if (_supeyPreviewEmptyHint != null) _supeyPreviewEmptyHint.Visible = false;
            _supeyPreviewDriverCb.SelectedIndex = 0;
        }

        private void BindLoadedTripsPreview()
        {
            _supeyPreviewLv.Items.Clear();
            if (_supeyLoadedTrips == null || _supeyLoadedTrips.Count == 0) return;

            var sorted = new List<MCDownloadedTrip>(_supeyLoadedTrips);
            sorted.Sort((a, b) => string.Compare(a?.PUTime, b?.PUTime, StringComparison.OrdinalIgnoreCase));

            foreach (var t in sorted)
            {
                if (t == null) continue;
                string route = ((t.PUStreet ?? "").Trim() + ", " + (t.PUCity ?? "").Trim()).Trim(',', ' ');
                if (!string.IsNullOrWhiteSpace(t.DOStreet) || !string.IsNullOrWhiteSpace(t.DOCITY))
                    route += " → " + ((t.DOStreet ?? "").Trim() + ", " + (t.DOCITY ?? "").Trim()).Trim(',', ' ');
                var lvi = new ListViewItem(new[]
                {
                    "—",
                    t.TripNumber ?? "",
                    t.ClientFullName ?? "",
                    "",
                    t.PUTime ?? "",
                    route,
                    t.DOTime ?? t.SchedDOTime ?? "",
                    t.Miles ?? "",
                });
                lvi.Tag = t;
                _supeyPreviewLv.Items.Add(lvi);
            }

            _supeyMap?.Clear();
            _supeyPreviewStatsLbl.Text = sorted.Count
                + " trips downloaded · click BUILD — this list will switch to the AI schedule per driver.";
        }

        private void BindWarningsPreview()
        {
            int total = 0;
            // Build-level warnings appear under a "Build" pseudo-driver so the user can see at a
            // glance whether the issue is roster-wide (e.g. a driver home that won't geocode) or
            // specific to one trip cluster.
            foreach (var w in _supeyResult.BuildWarnings)
            {
                AddWarningRow(w, "Build");
                total++;
            }
            foreach (var p in _supeyResult.DriverPlans)
            {
                foreach (var w in p.Warnings)
                {
                    AddWarningRow(w, p.Driver?.Name ?? "(driver)");
                    total++;
                }
            }
            _supeyMap.Clear();
            _supeyPreviewStatsLbl.Text = "Warnings: " + total +
                " — sort the Grp column to group by kind, then dig into specific issues.";
        }

        private void AddWarningRow(SupeyWarning w, string driverName)
        {
            string kindLabel = FormatWarningKind(w.Kind);
            var lvi = new ListViewItem(new[]
            {
                kindLabel,
                string.IsNullOrEmpty(w.TripNumber) ? "—" : w.TripNumber,
                driverName ?? "",
                w.Kind == SupeyWarningKind.MissingGeo ? SupeyTripGeocodeStatus.CheckPin : "",
                "",
                w.Detail ?? "",
                "",
                "",
            });
            lvi.UseItemStyleForSubItems = false;
            lvi.SubItems[0].BackColor = ColorForWarningKind(w.Kind);
            lvi.SubItems[0].ForeColor = Color.Black;
            if (w.Kind == SupeyWarningKind.MissingGeo)
                StyleGeoSubItem(lvi.SubItems[SupeyPrevColGeoIndex], SupeyTripGeocodeStatus.CheckPin);
            // Stash the warning on the row so a future double-click can jump to the offending
            // trip / driver in the preview.
            lvi.Tag = w;
            _supeyPreviewLv.Items.Add(lvi);
        }

        private static string FormatWarningKind(SupeyWarningKind k)
        {
            switch (k)
            {
                case SupeyWarningKind.MissingGeo: return "Geo";
                case SupeyWarningKind.LateArrival: return "Late DO";
                case SupeyWarningKind.TightArrival: return "Tight";
                case SupeyWarningKind.LateNextPickup: return "Late PU";
                case SupeyWarningKind.StraightLineFallback: return "OSRM";
                default: return k.ToString();
            }
        }

        private static Color ColorForWarningKind(SupeyWarningKind k)
        {
            switch (k)
            {
                case SupeyWarningKind.MissingGeo: return Color.FromArgb(232, 168, 96);    // amber — data quality
                case SupeyWarningKind.LateArrival: return Color.FromArgb(232, 96, 96);    // red   — hard miss
                case SupeyWarningKind.LateNextPickup: return Color.FromArgb(232, 96, 96); // red
                case SupeyWarningKind.TightArrival: return Color.FromArgb(232, 220, 96);  // yellow — within margin
                case SupeyWarningKind.StraightLineFallback: return Color.FromArgb(160, 160, 160); // grey — informational
                default: return Color.FromArgb(200, 200, 200);
            }
        }

        // ---------- Owner-draw for the preview ListView ----------
        //
        // Aliases pulled from SupeyTheme so the listviews stay consistent with the
        // rest of the dark palette. The previous flat #464646 read like a
        // placeholder; theming through SupeyTheme.List* slots them into the same
        // surface ladder as everything else and uses the muted blue selection
        // color so green stays reserved for primary actions / "checked" state.

        private static Color SupeyLvBg => SupeyTheme.ListBody;
        private static Color SupeyLvSel => SupeyTheme.ListSelected;
        private static Color SupeyLvText => SupeyTheme.ListText;
        private static Color SupeyLvSelText => SupeyTheme.ListSelectedText;
        private static Color SupeyLvGrid => SupeyTheme.ListGrid;
        private static Color SupeyLvHeader => SupeyTheme.ListHeader;
        private static Color SupeyLvHeaderText => SupeyTheme.ListHeaderText;

        private void SupeyPreviewLv_DrawColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            using (var brush = new SolidBrush(SupeyLvHeader))
                e.Graphics.FillRectangle(brush, e.Bounds);
            // 1px bottom hairline gives the header weight without us needing a
            // gradient or 3D edge. Same divider color used elsewhere on the tab.
            using (var pen = new Pen(SupeyTheme.Divider, 1f))
                e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            using (var fnt = new Font("Archivo Medium", 11f))
            {
                var rect = new Rectangle(e.Bounds.Left + 6, e.Bounds.Top, e.Bounds.Width - 6, e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, e.Header.Text ?? "", fnt, rect, SupeyLvHeaderText,
                    TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter);
            }
        }

        private void SupeyPreviewLv_DrawItem(object sender, DrawListViewItemEventArgs e)
        {
            bool sel = e.Item != null && e.Item.Selected;
            using (var bg = new SolidBrush(sel ? SupeyLvSel : SupeyLvBg))
                e.Graphics.FillRectangle(bg, e.Bounds);
        }

        private void SupeyPreviewLv_DrawSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            bool sel = e.Item != null && e.Item.Selected;
            // Group swatch column gets the cluster's color even on selection so legend / list correlate.
            Color fill = sel ? SupeyLvSel : SupeyLvBg;
            if (e.ColumnIndex == 0 && !sel && e.SubItem.BackColor != Color.Empty &&
                e.SubItem.BackColor != SupeyLvBg)
            {
                fill = e.SubItem.BackColor;
            }
            using (var bg = new SolidBrush(fill))
                e.Graphics.FillRectangle(bg, e.Bounds);

            var bounds = new Rectangle(e.Bounds.Left + 6, e.Bounds.Top, e.Bounds.Width - 6, e.Bounds.Height);
            Color textColor = sel ? SupeyLvSelText : SupeyLvText;
            // Per-cell ForeColor overrides only apply when the row isn't selected;
            // on selection we always want the readable contrast color.
            if (!sel)
            {
                if (e.ColumnIndex == SupeyPrevColGeoIndex && e.SubItem.ForeColor != Color.Empty)
                    textColor = e.SubItem.ForeColor;
                else if (e.ColumnIndex == 0 && e.SubItem.ForeColor != Color.Empty)
                    textColor = e.SubItem.ForeColor;
            }
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text ?? "", _supeyPreviewLv.Font, bounds, textColor,
                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.WordEllipsis | TextFormatFlags.GlyphOverhangPadding);

            SupeyListViewHelpers.DrawCellGridLines(e.Graphics, e.Bounds);
        }

        // ---------- Owner-draw for the roster ListView (mirrors preview's look) ----------

        private void SupeyDriversLv_DrawColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            using (var brush = new SolidBrush(SupeyLvHeader))
                e.Graphics.FillRectangle(brush, e.Bounds);
            using (var pen = new Pen(SupeyTheme.Divider, 1f))
                e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            using (var fnt = new Font("Archivo Medium", 11f))
            {
                var rect = new Rectangle(e.Bounds.Left + 6, e.Bounds.Top, e.Bounds.Width - 6, e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, e.Header.Text ?? "", fnt, rect, SupeyLvHeaderText,
                    TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter);
            }
        }

        private void SupeyDriversLv_DrawItem(object sender, DrawListViewItemEventArgs e)
        {
            bool sel = e.Item != null && e.Item.Selected;
            using (var bg = new SolidBrush(sel ? SupeyLvSel : SupeyLvBg))
                e.Graphics.FillRectangle(bg, e.Bounds);
        }

        private void SupeyDriversLv_DrawSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            // Column 0 is the checkbox column. We background-fill the cell ourselves
            // so the selection highlight runs edge-to-edge, then paint a custom
            // modern checkbox (square, accent-filled when checked) instead of the
            // chunky grey Win32 default. Click-to-toggle is still handled by the
            // ListView because CheckBoxes=true — we just override the visual.
            bool sel = e.Item != null && e.Item.Selected;
            using (var bg = new SolidBrush(sel ? SupeyLvSel : SupeyLvBg))
                e.Graphics.FillRectangle(bg, e.Bounds);

            if (e.ColumnIndex == 0)
            {
                SupeyListViewHelpers.DrawModernCheckbox(e.Graphics, e.Bounds,
                    e.Item != null && e.Item.Checked, sel);
                SupeyListViewHelpers.DrawCellGridLines(e.Graphics, e.Bounds);
                return;
            }

            var bounds = new Rectangle(e.Bounds.Left + 6, e.Bounds.Top, e.Bounds.Width - 6, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text ?? "", _supeyDriversLv.Font, bounds, sel ? SupeyLvSelText : SupeyLvText,
                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.WordEllipsis | TextFormatFlags.GlyphOverhangPadding);

            SupeyListViewHelpers.DrawCellGridLines(e.Graphics, e.Bounds);
        }

        // ---------- Helpers ----------

        private List<SupeyDriverProfile> GetCheckedSupeyDrivers()
        {
            var list = new List<SupeyDriverProfile>();
            if (_supeyDriversLv == null) return list;
            // Items can transiently expose null entries during a BeginUpdate / Add cycle when the
            // ItemChecked event handler re-enters this method mid-populate. Null-guard rather than
            // forbid the re-entry — the bulk-rebuild path already wraps in BeginUpdate and the
            // event will fire again with the final state when EndUpdate completes.
            foreach (ListViewItem item in _supeyDriversLv.Items)
            {
                if (item == null) continue;
                if (!item.Checked) continue;
                if (item.Tag is SupeyDriverProfile d) list.Add(d);
            }
            return list;
        }

        private void UpdateSupeyButtonStates()
        {
            int loaded = _supeyLoadedTrips?.Count ?? 0;
            int checkedDrivers = GetCheckedSupeyDrivers().Count;
            bool buildOk = loaded > 0 && checkedDrivers > 0 && _supeyCts == null;
            // MaterialButton's disabled paint for the Contained type washes out to nearly-white
            // against our dark toolbar (text becomes invisible). We instead hide the button
            // when it can't be clicked — that way the toolbar shows only the actions that are
            // currently meaningful, and the next-step hint goes through the status label and
            // the BUILD tooltip below.
            if (_supeyBuildBtn != null)
            {
                _supeyBuildBtn.Visible = loaded > 0 && _supeyCts == null;
                _supeyBuildBtn.Enabled = buildOk;
            }
            if (_supeySaveBtn != null)
            {
                _supeySaveBtn.Visible = _supeyResult != null && _supeyCts == null;
                _supeySaveBtn.Enabled = _supeyResult != null && _supeyCts == null;
            }

            // Tooltip explains the disabled state — without this, the user just sees a greyed-out
            // button after Load Trips and has no idea what's missing.
            EnsureSupeyToolTip();
            if (_supeyBuildBtn != null && _supeyToolTip != null)
            {
                string tip;
                if (_supeyCts != null) tip = "A build is in progress. Click Cancel to stop it.";
                else if (loaded == 0) tip = "Load trips for a service date first.";
                else if (_supeyRoster.Count == 0) tip = "Add drivers to the roster first (use ADD on the left).";
                else if (checkedDrivers == 0) tip = "Check at least one driver in the roster to include them.";
                else tip = "Build the schedule for the selected drivers.";
                _supeyToolTip.SetToolTip(_supeyBuildBtn, tip);
            }
        }

        private ToolTip _supeyToolTip;
        private void EnsureSupeyToolTip()
        {
            if (_supeyToolTip != null) return;
            _supeyToolTip = new ToolTip
            {
                AutoPopDelay = 12000,
                InitialDelay = 350,
                ReshowDelay = 200,
                ShowAlways = true,
            };
        }

        /// <summary>
        /// Composes the status string after Load Trips finishes. When the roster is empty or no
        /// driver is checked we tack on the next-step hint so the user knows BUILD is blocked
        /// for a recoverable reason — not because Load itself failed.
        /// </summary>
        private string BuildPostLoadStatus(int tripCount, DateTime date)
        {
            string lead = "Loaded " + tripCount + " trips for " + date.ToString("MMM d, yyyy") + ".";
            if (_supeyRoster.Count == 0)
                return lead + " Add drivers (ADD on the left), check the ones you want, then click BUILD.";
            int checkedCount = GetCheckedSupeyDrivers().Count;
            if (checkedCount == 0)
                return lead + " Check at least one driver in the roster, then click BUILD.";
            return lead + " Click BUILD — AI schedule loads on the list automatically.";
        }

        /// <summary>Shown above the trip list after BUILD or AI Send applies changes.</summary>
        private void MarkSupeyScheduleUpdated(string source)
        {
            if (_supeyScheduleUpdatedLbl == null) return;
            string when = DateTime.Now.ToString("h:mm tt");
            string text = "Schedule on screen · updated " + when;
            if (!string.IsNullOrWhiteSpace(source))
                text += " · " + source.Trim();
            _supeyScheduleUpdatedLbl.Text = text;
            _supeyScheduleUpdatedLbl.ForeColor = Color.FromArgb(144, 238, 144);
        }

        private void ClearSupeyScheduleUpdatedLabel()
        {
            if (_supeyScheduleUpdatedLbl == null) return;
            _supeyScheduleUpdatedLbl.Text = "No AI schedule on screen yet.";
            _supeyScheduleUpdatedLbl.ForeColor = Color.Gray;
        }

        private void SetSupeyToolbarBusy(bool busy, string msg)
        {
            if (_supeyToolbar == null) return;
            _supeyLoadBtn.Enabled = !busy;
            _supeyDatePicker.Enabled = !busy;
            _supeyCancelBtn.Visible = busy;
            _supeyProgressBar.Visible = busy;
            if (msg != null) SetSupeyStatus(msg);
            UpdateSupeyButtonStates();
        }

        private void SetSupeyStatus(string text)
        {
            if (_supeyToolbarStatusLbl == null) return;
            if (InvokeRequired) { try { BeginInvoke((Action)(() => SetSupeyStatus(text))); } catch { } return; }
            _supeyToolbarStatusLbl.Text = text ?? "";
        }

        private async Task RefreshSupeyOsrmStatusAsync()
        {
            if (_supeyOsrmStatusLbl == null) return;
            HiatmeGeoSettings.Refresh();
            if (_supeyAiSettings == null)
                _supeyAiSettings = HiatmeAiSettings.Load();

            HiatmeGeoClient.GeoStatus serverGeo = null;
            if (HiatmeGeoSettings.UseServer)
            {
                serverGeo = await HiatmeGeoClient.GetStatusAsync(_supeyAiSettings).ConfigureAwait(true);
            }

            OsrmSettings.InvalidateHealthCache();
            bool localOk = serverGeo?.OsrmLocalOk == true
                || await Task.Run(() => OsrmSettings.TryHealthCheckAsync()).ConfigureAwait(true);

            void Apply()
            {
                if (_supeyOsrmStatusPill == null || _supeyOsrmStatusPill.IsDisposed) return;
                // OSRM health badge — text on the pill, dot color carries the semantic.
                string label;
                Color dot;
                if (HiatmeGeoSettings.UseServer && serverGeo != null)
                {
                    if (serverGeo.OsrmLocalOk) { label = "OSRM · server"; dot = SupeyTheme.SuccessText; }
                    else { label = "OSRM · server fallback"; dot = SupeyTheme.WarnText; }
                }
                else if (localOk) { label = "OSRM · local"; dot = SupeyTheme.SuccessText; }
                else if (OsrmSettings.PreferLocal) { label = "OSRM · offline"; dot = SupeyTheme.WarnText; }
                else { label = "OSRM · public demo"; dot = SupeyTheme.TextMuted; }

                _supeyOsrmStatusPill.DotColor = dot;
                _supeyOsrmStatusPill.Label = label;
                _supeyOsrmStatusPill.Parent?.PerformLayout();
            }
            if (InvokeRequired)
                BeginInvoke((Action)Apply);
            else
                Apply();
        }

        private void ShowSupeyWarningsModal()
        {
            if (_supeyResult == null) return;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Build warnings:");
            sb.AppendLine();
            if (_supeyResult.BuildWarnings.Count > 0)
            {
                sb.AppendLine("[Build]");
                foreach (var w in _supeyResult.BuildWarnings) sb.AppendLine(" - " + w.Detail);
                sb.AppendLine();
            }
            foreach (var p in _supeyResult.DriverPlans)
            {
                if (p.Warnings.Count == 0) continue;
                sb.AppendLine("[" + (p.Driver?.Name ?? "Driver") + "]");
                foreach (var w in p.Warnings) sb.AppendLine(" - " + w.Detail);
                sb.AppendLine();
            }
            if (sb.Length == 0) sb.AppendLine("No warnings.");
            MessageBox.Show(this, sb.ToString(), "Supey Schedule warnings",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ---------- Combo / row tag wrappers ----------

        private sealed class SupeyPreviewItem
        {
            public enum ItemKind { Driver, Reserves, Warnings, LoadedTrips }
            public ItemKind Kind { get; }
            public SupeyDriverPlan Plan { get; }
            public string Display { get; }
            public SupeyPreviewItem(SupeyDriverPlan plan, string display)
            {
                Plan = plan;
                Kind = plan != null ? ItemKind.Driver : ItemKind.Reserves; // legacy ctor
                Display = display;
            }
            public SupeyPreviewItem(ItemKind kind, string display)
            {
                Kind = kind;
                Plan = null;
                Display = display;
            }
            public override string ToString() => Display ?? "";
        }

        private void StyleGeoSubItem(ListViewItem.ListViewSubItem sub, string geoLabel)
        {
            if (sub == null) return;
            if (SupeyTripGeocodeStatus.NeedsAttention(geoLabel))
            {
                sub.ForeColor = Color.FromArgb(255, 90, 90);
                sub.Font = new Font(_supeyPreviewLv.Font, FontStyle.Bold);
            }
            else
            {
                sub.ForeColor = Color.Empty;
                sub.Font = null;
            }
        }

        private void SupeyPreviewLv_SelectedTripChanged(object sender, EventArgs e)
        {
            if (_supeyPreviewLv.SelectedItems.Count == 0) return;
            FocusPreviewTripRow(_supeyPreviewLv.SelectedItems[0]);
        }

        private void FocusPreviewTripRow(ListViewItem row)
        {
            if (row?.Tag is SupeyPreviewRowTag tag && tag.Trip != null)
                _supeyMap?.FocusTrip(tag.Trip);
        }

        private void SupeyPreviewLv_DoubleClickTrip(object sender, EventArgs e)
        {
            if (_supeyPreviewLv.SelectedItems.Count == 0) return;
            var tag = _supeyPreviewLv.SelectedItems[0].Tag as SupeyPreviewRowTag;
            if (tag?.Trip == null || tag.Group == null) return;
            string geo = SupeyTripGeocodeStatus.ForScheduledTrip(tag.Trip, tag.Group, tag.Plan, tag.TripIndex);
            if (!SupeyTripGeocodeStatus.NeedsAttention(geo)) return;
            bool needPu = tag.TripIndex < 0 || tag.TripIndex >= tag.Group.PickupPoints.Count;
            OpenGeocodeFixForTrip(tag, needPu);
        }

        private void OpenGeocodeFixForTrip(SupeyPreviewRowTag tag, bool pickup)
        {
            if (tag?.Trip == null) return;
            GeoPoint initial;
            if (pickup && tag.TripIndex >= 0 && tag.TripIndex < tag.Group.PickupPoints.Count
                && !(tag.Group.PickupPoints[tag.TripIndex].Lat == 0 && tag.Group.PickupPoints[tag.TripIndex].Lng == 0))
                initial = tag.Group.PickupPoints[tag.TripIndex];
            else if (!pickup && tag.TripIndex >= 0 && tag.TripIndex < tag.Group.DropoffPoints.Count
                && !(tag.Group.DropoffPoints[tag.TripIndex].Lat == 0 && tag.Group.DropoffPoints[tag.TripIndex].Lng == 0))
                initial = tag.Group.DropoffPoints[tag.TripIndex];
            else
                initial = new GeoPoint(44.8, -68.77);
            var info = new SupeyMapMarkerInfo
            {
                Trip = tag.Trip,
                EndpointLabel = pickup ? "Pickup" : "Dropoff",
                IsPickup = pickup,
                Street = pickup ? tag.Trip.PUStreet : tag.Trip.DOStreet,
                City = pickup ? tag.Trip.PUCity : tag.Trip.DOCITY,
                State = "ME",
                OnPinSaved = p =>
                {
                    if (pickup)
                    {
                        while (tag.Group.PickupPoints.Count <= tag.TripIndex)
                            tag.Group.PickupPoints.Add(p);
                        tag.Group.PickupPoints[tag.TripIndex] = p;
                    }
                    else
                    {
                        while (tag.Group.DropoffPoints.Count <= tag.TripIndex)
                            tag.Group.DropoffPoints.Add(p);
                        tag.Group.DropoffPoints[tag.TripIndex] = p;
                    }
                    RefreshPreviewGeoCell(tag);
                    _supeyMap?.ShowDriverPlan(tag.Plan);
                    _supeyMap?.FocusTrip(tag.Trip);
                },
            };
            using (var dlg = new SupeyGeocodeFixForm(info, initial))
            {
                dlg.ShowDialog(FindForm());
            }
        }

        private void RefreshPreviewGeoCell(SupeyPreviewRowTag tag)
        {
            if (tag == null || _supeyPreviewLv == null) return;
            foreach (ListViewItem row in _supeyPreviewLv.Items)
            {
                if (row.Tag != tag) continue;
                string geo = SupeyTripGeocodeStatus.ForScheduledTrip(tag.Trip, tag.Group, tag.Plan, tag.TripIndex);
                row.SubItems[SupeyPrevColGeoIndex].Text = geo;
                StyleGeoSubItem(row.SubItems[SupeyPrevColGeoIndex], geo);
                break;
            }
        }

        private sealed class SupeyPreviewRowTag
        {
            public SupeyTripCluster Group { get; }
            public MCDownloadedTrip Trip { get; }
            public SupeyDriverPlan Plan { get; }
            public int TripIndex { get; }
            public SupeyPreviewRowTag(SupeyTripCluster g, MCDownloadedTrip t, SupeyDriverPlan p, int tripIndex)
            {
                Group = g;
                Trip = t;
                Plan = p;
                TripIndex = tripIndex;
            }
        }
    }
}
