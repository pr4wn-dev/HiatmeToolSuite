using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
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
        private bool _supeyDefaultSplitApplied;
        private bool _supeyUserAdjustedMainSplit;
        private bool _applyingSupeyDefaultSplit;
        private SupeyCollapsiblePanel _supeyDriversCollapsible;
        private SupeyCollapsiblePanel _supeyTripsCollapsible;
        private SupeyCollapsiblePanel _supeyRightCollapsible;
        // Draggable bars between the docked side panels. They show only while their
        // collapsible neighbor is expanded — a splitter on a 34px-wide collapsed panel
        // would resize a sliver of the title strip and confuse users.
        private Splitter _supeyDriversSplitter;
        private Splitter _supeyAiSplitter;
        private Splitter _supeyRulesSplitter;
        private Splitter _supeyInfoSplitter;
        private SupeyLabel _supeyTemplateCompareLbl;

        private RJDatePicker _supeyDatePicker;
        private SupeyButton _supeyLoadBtn;
        private SupeyButton _supeyBuildBtn;
        private CheckBox _supeyUseTemplatesCb;
        private CheckBox _supeyFinishRemainingCb;
        private SupeyButton _supeyRefreshNotesBtn;
        private SupeyButton _supeySaveBtn;
        private SupeyButton _supeyCancelBtn;
        private SupeyLabel _supeyScheduleUpdatedLbl;
        private Label _supeyLastBuildLbl;
        private Label _supeyToolbarStatusLbl;
        private SupeyLabel _supeyOsrmStatusLbl;          // legacy alias — not visible
        private SupeyStatusPill _supeyOsrmStatusPill;       // the actual visible OSRM badge

        private ProgressBar _supeyProgressBar;
        private SupeyLabel _supeyStatsLbl;
        private LinkLabel _supeyWarningsLink;
        private LinkLabel _supeyCopyBuildLogLink;

        private ListView _supeyDriversLv;
        private ColumnHeader _supeyDriversColCheck;
        private ColumnHeader _supeyDriversColName;
        private ColumnHeader _supeyDriversColEmail;
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
        private ColumnHeader _supeyPrevColMiles;
        private ColumnHeader _supeyPrevColGeo;
        private ColumnHeader _supeyPrevColEstPu;
        private ColumnHeader _supeyPrevColLate;
        private Font _supeyPreviewRouteFont;
        private const int SupeyPrevColGeoIndex = 3;
        private const int SupeyPrevColEstPuIndex = 5;
        private const int SupeyPrevColPuAddrIndex = 6;
        private const int SupeyPrevColDoAddrIndex = 7;
        private const int SupeyPrevColLateIndex = 9;
        private SupeyPreviewRowTag _supeyDragTripTag;
        private SupeyLabel _supeyPreviewStatsLbl;
        private Label _supeyPreviewEmptyHint;

        private SupeyMapWorkspace _supeyMap;

        // ---------- runtime state ----------
        private List<SupeyDriverProfile> _supeyRoster = new List<SupeyDriverProfile>();
        /// <summary>True after <see cref="LoadSupeyRosterFromDisk"/> or Schedule Builder first load — prevents reloading roster and breaking ListView tags.</summary>
        private bool _supeyRosterLoadedFromDisk;
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

            // Apply 38% trips height after the tab has a real client size (split defaults to ~50px map otherwise).
            tabPageSupey.VisibleChanged += (s, e) =>
            {
                if (tabPageSupey.Visible)
                    EnsureSupeySplitDistance();
            };
            RunWhenReady(EnsureSupeySplitDistance);

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

            var aiSettings = HiatmeAiSettings.Load();
            _supeyUseTemplatesCb = new CheckBox
            {
                Text = "Templates",
                Checked = aiSettings.UseWeekdayTemplates,
                AutoSize = true,
                ForeColor = SupeyTheme.TextSecondary,
                BackColor = SupeyTheme.SurfaceHeader,
                Margin = new Padding(0, 6, 8, 0),
                Font = SupeyTheme.CaptionFont,
            };
            _supeyFinishRemainingCb = new CheckBox
            {
                Text = "Finish remaining",
                Checked = aiSettings.FinishRemainingAfterTemplates,
                AutoSize = true,
                ForeColor = SupeyTheme.TextSecondary,
                BackColor = SupeyTheme.SurfaceHeader,
                Margin = new Padding(0, 6, 8, 0),
                Font = SupeyTheme.CaptionFont,
            };
            _supeyUseTemplatesCb.CheckedChanged += (s, e) =>
            {
                if (_supeyFinishRemainingCb != null)
                    _supeyFinishRemainingCb.Enabled = _supeyUseTemplatesCb.Checked;
                UpdateSupeyTemplateBuildHint();
            };
            _supeyFinishRemainingCb.CheckedChanged += (s, e) => UpdateSupeyTemplateBuildHint();
            _supeyFinishRemainingCb.Enabled = _supeyUseTemplatesCb.Checked;

            _supeyRefreshNotesBtn = new SupeyButton
            {
                Text = "ROUTES & NOTES",
                Kind = SupeyButton.Variant.Secondary,
                Size = new Size(138, 30),
                Margin = new Padding(0, 1, 6, 0),
                Visible = false,
            };
            _supeyRefreshNotesBtn.Click += async (s, e) => await OnSupeyRefreshRoutesAndNotesAsync();

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

            // SupeyLabel forces the MaterialSkin Roboto font + its own line
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
            // Keep the legacy SupeyLabel field as a hidden alias so back-compat
            // callers (RefreshSupeyOsrmStatusAsync etc.) can still write to .Text /
            // .ForeColor without crashing — we project those onto the pill below.
            _supeyOsrmStatusLbl = new SupeyLabel
            {
                Visible = false,
                Width = 1,
                Height = 1,
                Location = new Point(-100, -100),
            };
            var osrmTip = "Road miles and geocode use the office AI server (Maine OSRM).\r\n" +
                "Panel + OSRM must be running on the server PC.\r\n" +
                "Click to refresh.";
            var osrmTipProvider = SupeyToolTip.Create(autoPopDelay: 12000, initialDelay: 400);
            osrmTipProvider.SetToolTip(_supeyOsrmStatusPill, osrmTip);

            // Left group — action cluster, ordered LTR.
            leftFlow.Controls.Add(dateLabel);
            leftFlow.Controls.Add(_supeyDatePicker);
            leftFlow.Controls.Add(sep1);
            leftFlow.Controls.Add(_supeyLoadBtn);
            leftFlow.Controls.Add(_supeyUseTemplatesCb);
            leftFlow.Controls.Add(_supeyFinishRemainingCb);
            leftFlow.Controls.Add(_supeyBuildBtn);
            leftFlow.Controls.Add(_supeyRefreshNotesBtn);
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
                Height = 34,
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

            _supeyProgressBar = new ProgressBar
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

            _supeyCopyBuildLogLink = new LinkLabel
            {
                Text = "Copy BUILD log",
                AutoSize = true,
                Height = 18,
                ForeColor = SupeyTheme.TextSecondary,
                LinkColor = SupeyTheme.AccentPrimary,
                ActiveLinkColor = SupeyTheme.TextPrimary,
                BackColor = SupeyTheme.SurfaceStatusBar,
                LinkBehavior = LinkBehavior.HoverUnderline,
                Font = SupeyTheme.CaptionFont,
            };
            _supeyCopyBuildLogLink.Click += (s, e) => CopySupeyBuildLogToClipboard();
            EnsureSupeyToolTip();
            _supeyToolTip.SetToolTip(_supeyCopyBuildLogLink,
                "Copy timestamped BUILD transcript (desk + server) for Cursor or support.");

            _supeyLastBuildLbl = new Label
            {
                Text = "",
                AutoSize = false,
                Height = 22,
                ForeColor = SupeyTheme.TextSecondary,
                BackColor = SupeyTheme.SurfaceStatusBar,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = SupeyTheme.BodyFont,
                Visible = false,
            };
            EnsureSupeyToolTip();
            _supeyToolTip.SetToolTip(_supeyLastBuildLbl,
                "Last BUILD summary — solver, assigned vs loaded, reserves, and geocode gaps.");

            _supeyStatusStrip.Controls.Add(_supeyProgressBar);
            _supeyStatusStrip.Controls.Add(_supeyLastBuildLbl);
            _supeyStatusStrip.Controls.Add(_supeyCopyBuildLogLink);
            _supeyStatusStrip.Controls.Add(_supeyWarningsLink);

            void Reposition()
            {
                if (_supeyWarningsLink == null || _supeyStatusStrip == null) return;
                int pad = 12;
                int w = _supeyStatusStrip.ClientSize.Width;
                _supeyWarningsLink.Left = Math.Max(0, w - _supeyWarningsLink.Width - pad);
                if (_supeyCopyBuildLogLink != null)
                {
                    _supeyCopyBuildLogLink.Top = 6;
                    _supeyCopyBuildLogLink.Left = Math.Max(
                        pad,
                        _supeyWarningsLink.Left - _supeyCopyBuildLogLink.Width - 12);
                }
                if (_supeyLastBuildLbl != null)
                {
                    int left = _supeyProgressBar.Visible ? _supeyProgressBar.Right + 10 : pad;
                    int right = (_supeyCopyBuildLogLink != null
                        ? _supeyCopyBuildLogLink.Left
                        : _supeyWarningsLink.Left) - 8;
                    _supeyLastBuildLbl.SetBounds(left, 6, Math.Max(80, right - left), 22);
                }
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
            // WinForms fires SplitterMoved during initial layout (default ~50px map height),
            // which used to block our 38% default and left the trips panel almost full-window.
            _supeyMainSplit.SplitterMoved += (s, e) =>
            {
                if (_applyingSupeyDefaultSplit) return;
                if (_supeyDefaultSplitApplied)
                    _supeyUserAdjustedMainSplit = true;
            };

            var workPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.SurfaceBase,
            };
            SupeyCollapsibleSideLayout.EnsureWired(workPanel);

            _supeyMap = new SupeyMapWorkspace { Dock = DockStyle.Fill };
            _supeyMap.SetSupeyStatusOnHost = msg => SetSupeyStatus(msg);

            _supeyDriversCollapsible = new SupeyCollapsiblePanel
            {
                Title = "Drivers",
                ExpandedWidth = 450,
                MinExpandedWidth = 450,
                MaxExpandedWidth = 800,
                Dock = DockStyle.Left,
            };
            BuildSupeyDriversPanel(_supeyDriversCollapsible.ContentPanel);

            BuildSupeyAiPanel();
            BuildSupeyRulesPanel();
            if (_supeyRulesCollapsible != null)
                _supeyRulesCollapsible.Expanded = false;
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
            _supeyRightCollapsible.Expanded = false;

            // ── Workspace dock layout with draggable splitters ────────────────
            // WinForms Dock semantics: when multiple controls share the same Dock side,
            // the LAST one added sits closest to the outer edge. So to land on the
            // intended layout
            //
            //   [Drivers | drvSplit | Map | aiSplit | AI | rulesSplit | Rules | infoSplit | Info]
            //
            // we add (in order):
            //   1. Map        (Fill)        — fills whatever's left
            //   2. drvSplit   (Left)        — pushed inward by step 3
            //   3. Drivers    (Left)        — leftmost
            //   4. aiSplit    (Right)       — pushed inward by steps 5/6/7
            //   5. AI         (Right)
            //   6. rulesSplit (Right)
            //   7. Rules      (Right)
            //   8. infoSplit  (Right)
            //   9. Info       (Right)       — rightmost
            //
            // Each Splitter is a thin draggable bar that resizes the docked control
            // adjacent to it (the "outer" one on its dock side). MinExtra leaves a
            // sensible amount of space for the Map (Fill) so users can't drag a side
            // panel to swallow the whole workspace.
            _supeyDriversSplitter = MakeDockSplitter(DockStyle.Left, _supeyDriversCollapsible, workPanel);
            _supeyAiSplitter = MakeDockSplitter(DockStyle.Right, _supeyAiCollapsible, workPanel);
            _supeyRulesSplitter = MakeDockSplitter(DockStyle.Right, _supeyRulesCollapsible, workPanel);
            _supeyInfoSplitter = MakeDockSplitter(DockStyle.Right, _supeyRightCollapsible, workPanel);

            workPanel.Controls.Add(_supeyMap);
            workPanel.Controls.Add(_supeyMap.GroupKeyPanel);
            workPanel.Controls.Add(_supeyDriversSplitter);
            workPanel.Controls.Add(_supeyDriversCollapsible);
            workPanel.Controls.Add(_supeyAiSplitter);
            workPanel.Controls.Add(_supeyAiCollapsible);
            workPanel.Controls.Add(_supeyRulesSplitter);
            workPanel.Controls.Add(_supeyRulesCollapsible);
            workPanel.Controls.Add(_supeyInfoSplitter);
            workPanel.Controls.Add(_supeyRightCollapsible);
            _supeyDriversCollapsible.ApplyExpandedLayout();
            _supeyAiCollapsible?.ApplyExpandedLayout();
            _supeyRulesCollapsible?.ApplyExpandedLayout();
            _supeyRightCollapsible.ApplyExpandedLayout();
            _supeyMap.GroupKeyPanel.ApplyExpandedLayout();
            _supeyMainSplit.Panel1.Controls.Add(workPanel);

            _supeyTripsCollapsible = new SupeyCollapsiblePanel
            {
                Title = "Trips",
                Dock = DockStyle.Fill,
            };
            BuildSupeyTripsPanel(_supeyTripsCollapsible.ContentPanel);
            _supeyMainSplit.Panel2.Controls.Add(_supeyTripsCollapsible);

            SupeyListViewHelpers.WireSplitContainerSmoothResize(_supeyMainSplit);

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
        private Splitter MakeDockSplitter(DockStyle dock, SupeyCollapsiblePanel target, Control layoutRoot) =>
            SupeyCollapsiblePanel.CreateDockSplitter(dock, target, minExtra: 320, layoutRoot: layoutRoot);

        /// <summary>Default trip list to ~38% of workspace height (max 480px); user drag keeps their choice.</summary>
        private void EnsureSupeySplitDistance()
        {
            if (_supeyMainSplit == null || _supeyUserAdjustedMainSplit || _supeyDefaultSplitApplied) return;
            int total = _supeyMainSplit.Height;
            if (total < 200) return;

            int tripsH = Math.Max(_supeyMainSplit.Panel2MinSize,
                Math.Min(480, (int)(total * 0.38)));
            int mapH = total - tripsH - _supeyMainSplit.SplitterWidth;
            if (mapH < _supeyMainSplit.Panel1MinSize)
                mapH = _supeyMainSplit.Panel1MinSize;

            _applyingSupeyDefaultSplit = true;
            try { _supeyMainSplit.SplitterDistance = mapH; }
            finally { _applyingSupeyDefaultSplit = false; }
            _supeyDefaultSplitApplied = true;
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
            _supeyTemplateCompareLbl = new SupeyLabel
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
            _supeyDriverEditBtn.Click += async (s, e) => await OnSupeyDriverEditAsync();

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

            _supeyDriversLv = new SupeyListView
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
            // CheckBoxes=true paints the box inside the first column; keep it narrow but with a
            // proper header so users see the on/off semantic.
            _supeyDriversColCheck = new ColumnHeader { Text = "Use", Width = 44 };
            _supeyDriversColName = new ColumnHeader { Text = "Driver", Width = 160 };
            _supeyDriversColEmail = new ColumnHeader { Text = "Email", Width = 180 };
            _supeyDriversColCap = new ColumnHeader { Text = "Cap", Width = 48 };
            _supeyDriversColShift = new ColumnHeader { Text = "Shift", Width = 100 };
            _supeyDriversColRelease = new ColumnHeader { Text = "Release", Width = 88 };
            _supeyDriversLv.Columns.AddRange(new[]
            {
                _supeyDriversColCheck, _supeyDriversColName, _supeyDriversColEmail,
                _supeyDriversColCap, _supeyDriversColShift, _supeyDriversColRelease
            });
            _supeyDriversLv.DoubleClick += async (s, e) => await OnSupeyDriverEditAsync();
            // ItemChecked fires per-item during bulk Add() — the rebuild path uses
            // _supeySuppressItemChecked to mute it so we don't recompute button states N times
            // while items are still being constructed.
            _supeyDriversLv.AllowDrop = true;
            _supeyDriversLv.DragEnter += SupeyDriversLv_DragEnter;
            _supeyDriversLv.DragOver += SupeyDriversLv_DragOver;
            _supeyDriversLv.DragDrop += SupeyDriversLv_DragDrop;

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
            _supeyScheduleUpdatedLbl = new SupeyLabel
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
            _supeyStatsLbl = new SupeyLabel
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

            _supeyPreviewStatsLbl = new SupeyLabel
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

            _supeyPreviewLv = new SupeyListView
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
            _supeyPrevColPUTime = new ColumnHeader { Text = "Sched PU", Width = 62 };
            _supeyPrevColEstPu = new ColumnHeader { Text = "Est PU", Width = 62 };
            _supeyPrevColPUStreet = new ColumnHeader { Text = "Pickup", Width = 200 };
            _supeyPrevColPUCity = new ColumnHeader { Text = "Dropoff", Width = 200 };
            _supeyPrevColDOTime = new ColumnHeader { Text = "Sched DO", Width = 62 };
            _supeyPrevColLate = new ColumnHeader { Text = "Late", Width = 72 };
            _supeyPrevColMiles = new ColumnHeader { Text = "Mi", Width = 44 };
            _supeyPrevColGeo = new ColumnHeader { Text = "Geo", Width = 72 };
            _supeyPreviewLv.Columns.AddRange(new[]
            {
                _supeyPrevColGrp, _supeyPrevColTrip, _supeyPrevColClient, _supeyPrevColGeo,
                _supeyPrevColPUTime, _supeyPrevColEstPu, _supeyPrevColPUStreet, _supeyPrevColPUCity,
                _supeyPrevColDOTime, _supeyPrevColLate, _supeyPrevColMiles,
            });
            _supeyPreviewLv.DrawColumnHeader += SupeyPreviewLv_DrawColumnHeader;
            _supeyPreviewLv.DrawItem += SupeyPreviewLv_DrawItem;
            _supeyPreviewLv.DrawSubItem += SupeyPreviewLv_DrawSubItem;
            _supeyPreviewRouteFont = new Font(_supeyPreviewLv.Font, FontStyle.Italic);

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
            SupeyToolTip.WireListViewItems(_supeyPreviewLv);
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
        private ToolStripMenuItem _supeyWarningsCtxCopyForAi;
        private ToolStripMenuItem _supeyWarningsCtxClear;
        private string _supeyLastBuildEngine;
        private string _supeyLastServerSolveError;
        private HiatmeBuildStats _supeyLastBuildStats;
        private string _supeyLastServerProgressLabel;
        private SupeyBuildSessionLog _supeyBuildLog = new SupeyBuildSessionLog();
        private string _supeyLastBuildStopReason;
        private string _supeyLastLoggedProgressLine;

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

            _supeyWarningsCtxCopyForAi = new ToolStripMenuItem("Copy warnings for AI review (Cursor)")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
                Image = MenuIconFactory.GetCopyAllIcon(),
            };
            _supeyWarningsCtxCopyForAi.Click += (s, e) => CopyWarningsForAiReviewToClipboard();

            _supeyWarningsCtxClear = new ToolStripMenuItem("Clear all warnings")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
                Image = MenuIconFactory.GetClearIcon(),
            };
            _supeyWarningsCtxClear.Click += (s, e) => ClearAllWarnings();

            _supeyWarningsCtxMenu.Items.Add(_supeyWarningsCtxCopySelected);
            _supeyWarningsCtxMenu.Items.Add(_supeyWarningsCtxCopyAll);
            _supeyWarningsCtxMenu.Items.Add(_supeyWarningsCtxCopyForAi);
            var copyBuildLog = new ToolStripMenuItem("Copy BUILD log (for Cursor)")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
                Image = MenuIconFactory.GetCopyAllIcon(),
            };
            copyBuildLog.Click += (s, e) => CopySupeyBuildLogToClipboard();
            _supeyWarningsCtxMenu.Items.Add(copyBuildLog);
            _supeyWarningsCtxMenu.Items.Add(new ToolStripSeparator());
            _supeyWarningsCtxMenu.Items.Add(_supeyWarningsCtxClear);

            // Gate visibility on dropdown selection so the menu only appears in Warnings mode.
            // Hooking MouseUp instead of ContextMenuStrip lets us inspect the click at runtime;
            // the ListView's ContextMenuStrip property would show it unconditionally.
            _supeyPreviewLv.MouseUp += SupeyPreviewLv_MouseUp_HandleWarningsContext;
            _supeyPreviewLv.SelectedIndexChanged += SupeyPreviewLv_SelectedTripChanged;
            _supeyPreviewLv.DoubleClick += SupeyPreviewLv_DoubleClickTrip;
            _supeyPreviewLv.AllowDrop = true;
            _supeyPreviewLv.ItemDrag += SupeyPreviewLv_ItemDrag;
            _supeyPreviewLv.DragEnter += SupeyPreviewLv_DragEnter;
            _supeyPreviewLv.DragOver += SupeyPreviewLv_DragOver;
            _supeyPreviewLv.DragDrop += SupeyPreviewLv_DragDrop;
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
                _supeyWarningsCtxCopyForAi.Enabled = _supeyResult != null;
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
                var full = new System.Text.StringBuilder();
                if (!selectedOnly)
                    AppendWarningsClipboardHeader(full);
                full.Append(sb);
                Clipboard.SetText(full.ToString());
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

        /// <summary>Server BUILD lines + reserve summary as Build warnings (shows in Warnings list).</summary>
        private void ApplySupeyBuildDiagnostics(HiatmeAiBuildResponse resp)
        {
            if (_supeyResult == null || resp == null) return;
            SupeyBuildEngineLabel.SyncBuildWarning(_supeyResult, _supeyLastBuildEngine, null);
            if (resp.Warnings != null)
            {
                foreach (var line in resp.Warnings)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var w = new SupeyWarning(
                        SupeyWarningKind.BuildDiagnostic, "", "Build", line.Trim());
                    if (SupeyWarningsUtil.IsDriverTimingWarning(w))
                        continue;
                    _supeyResult.BuildWarnings.Add(w);
                }
            }
            if (resp.BuildStats != null)
            {
                var s = resp.BuildStats;
                if (s.ReservesCount > 0)
                {
                    _supeyResult.BuildWarnings.Add(new SupeyWarning(
                        SupeyWarningKind.UnassignedToReserves,
                        "",
                        "Build",
                        s.ReservesCount + " trip(s) in Reserves — use Warnings → Copy for AI review for trip numbers."));
                }
                if (s.NoGeoCount > 0)
                {
                    _supeyResult.BuildWarnings.Add(new SupeyWarning(
                        SupeyWarningKind.MissingGeo,
                        "",
                        "Build",
                        s.NoGeoCount + " trip(s): address could not be found automatically (in reserves — "
                        + "wait for LOAD cache or BUILD again; pin only if street/city are correct)."));
                }
                if (s.UnassignedGroupsCount > 0)
                {
                    _supeyResult.BuildWarnings.Add(new SupeyWarning(
                        SupeyWarningKind.UnassignedToReserves,
                        "",
                        "Build",
                        s.UnassignedGroupsCount + " ride-share group(s) had geocode but no driver fit (capacity/shift/rules)."));
                }
            }
        }

        private void AppendWarningsClipboardHeader(System.Text.StringBuilder sb)
        {
            if (_supeyResult == null) return;
            string hdr = SupeyWarningsExport.Build(
                _supeyDatePicker.Value,
                _supeyResult,
                _supeyLastBuildEngine,
                _supeyLastBuildStats,
                _supeyLoadedTrips?.Count ?? 0,
                GetCheckedSupeyDrivers());
            int cut = hdr.IndexOf("## Warnings", StringComparison.Ordinal);
            if (cut > 0)
                sb.Append(hdr.Substring(0, cut));
            else
                sb.Append(hdr);
        }

        /// <summary>Full warnings + build stats + reserve trip sample for pasting into Cursor.</summary>
        private void CopyWarningsForAiReviewToClipboard()
        {
            if (_supeyResult == null) return;
            try
            {
                string text = SupeyWarningsExport.Build(
                    _supeyDatePicker.Value,
                    _supeyResult,
                    _supeyLastBuildEngine,
                    _supeyLastBuildStats,
                    _supeyLoadedTrips?.Count ?? 0,
                    GetCheckedSupeyDrivers());
                Clipboard.SetText(text);
                SetSupeyStatus("Copied warnings for AI review — paste into Cursor chat.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not copy to clipboard:\n\n" + ex.Message, "Supey Schedule",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
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
        private JObject _supeyLastRulesContext;
        private ToolStripMenuItem _supeyTripsCtxCopyForReview;

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

            _supeyTripsCtxCopyForReview = new ToolStripMenuItem("Copy for AI review (roster + rules + coords)")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
                Image = MenuIconFactory.GetCopyAllIcon(),
            };
            _supeyTripsCtxCopyForReview.Click += (s, e) => CopyScheduleForAiReviewToClipboard();

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
            _supeyTripsCtxMenu.Items.Add(_supeyTripsCtxCopyForReview);
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
            _supeyTripsCtxCopyForReview.Enabled = _supeyTripsCtxCopyAll.Enabled;
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
        /// <summary>
        /// Full day dump for Cursor review: roster, accepted rules, warnings, group order, lat/lng.
        /// </summary>
        private void CopyScheduleForAiReviewToClipboard()
        {
            if (_supeyResult == null) return;
            try
            {
                string text = SupeyScheduleReviewExport.Build(
                    _supeyResult.ServiceDate,
                    GetCheckedSupeyDrivers(),
                    _supeyResult,
                    _supeyLastRulesContext,
                    _supeyLastBuildEngine);
                Clipboard.SetText(text);
                SetSupeyStatus("Copied schedule for AI review (paste into Cursor chat).");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not copy to clipboard:\n\n" + ex.Message, "Supey Schedule",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

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
                sb.Append(g.GroupNumber).Append("\tRoute\t")
                  .Append(Sanitize(SupeyRouteNoteFormatter.Format(g)))
                  .AppendLine();
                foreach (int ti in SupeyClusterDisplayOrder.PickupVisitIndices(g))
                {
                    var t = g.Trips[ti];
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
              .Append(_supeyResult.TotalReserveCount)
              .Append(" trip").Append(_supeyResult.TotalReserveCount == 1 ? "" : "s")
              .AppendLine(")");
            sb.AppendLine("Trip #\tClient\tPU Time\tPU Street\tPU City\tDO Time\tDO Street\tDO City\tMiles");
            foreach (var t in _supeyResult.ReservesWillCalls)
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

        private static SupeyMaterialButton MakeFlatButton(string text, int x, int y, int width)
        {
            var b = new SupeyMaterialButton
            {
                Text = text,
                Location = new Point(x, y),
                AutoSize = false,
                Size = new Size(width, 32),
                Type = SupeyMaterialButton.MaterialButtonType.Contained,
                Density = SupeyMaterialButton.MaterialButtonDensity.Default,
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
            _supeyRosterLoadedFromDisk = true;
            ScheduleBuilderDriverEmailsRegistry.ApplyLocalRegistryToRoster(_supeyRoster);
            if (!string.IsNullOrEmpty(warning))
            {
                SetSupeyStatus(warning);
            }
            RebuildSupeyDriversList();
        }

        /// <summary>Match roster row by reference, WellRyde SEC id, schedule tab, or name.</summary>
        private int IndexOfSupeyDriver(SupeyDriverProfile profile)
        {
            if (profile == null || _supeyRoster == null || _supeyRoster.Count == 0)
                return -1;

            int idx = _supeyRoster.IndexOf(profile);
            if (idx >= 0)
                return idx;

            string sec = (profile.WellRydeSecId ?? "").Trim();
            if (sec.Length > 0)
            {
                idx = _supeyRoster.FindIndex(d =>
                    d != null && string.Equals((d.WellRydeSecId ?? "").Trim(), sec, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0)
                    return idx;
            }

            string tab = (profile.ScheduleTabKey ?? "").Trim();
            if (tab.Length > 0)
            {
                idx = _supeyRoster.FindIndex(d =>
                    d != null && string.Equals((d.ScheduleTabKey ?? "").Trim(), tab, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0)
                    return idx;
            }

            string name = (profile.Name ?? "").Trim();
            if (name.Length > 0)
            {
                return _supeyRoster.FindIndex(d =>
                    d != null && string.Equals((d.Name ?? "").Trim(), name, StringComparison.OrdinalIgnoreCase));
            }

            return -1;
        }

        private bool TryApplySupeyDriverEdit(SupeyDriverProfile existing, SupeyDriverProfile edited, out string error)
        {
            error = null;
            if (edited == null)
            {
                error = "No driver data returned from the editor.";
                return false;
            }

            if (_supeyRoster == null)
                _supeyRoster = new List<SupeyDriverProfile>();

            int idx = IndexOfSupeyDriver(existing ?? edited);
            if (idx < 0)
            {
                error = "Could not find this driver in the roster — your changes were not saved.";
                return false;
            }

            _supeyRoster[idx] = edited;
            return true;
        }

        /// <summary>Local + server driver-email registry; must not break roster JSON save.</summary>
        private void PersistSupeyDriverEmailsSafely()
        {
            try
            {
                if (_supeyRoster == null || _supeyRoster.Count == 0)
                    return;
                ScheduleBuilderDriverEmailsRegistry.UpdateLocalFromRoster(_supeyRoster, Environment.UserName);
                ScheduleBuilderDriverEmailsRegistry.TryPushToServerFireAndForget(null, Environment.UserName);
            }
            catch
            {
                /* bulk SAVE path — edit uses CommitDriverEditAsync */
            }
        }

        /// <summary>Edit OK: roster JSON, email registry, AI server, optional WellRyde — status bar summary.</summary>
        private async Task CommitDriverEditAsync(
            SupeyDriverProfile existing,
            SupeyDriverProfile edited,
            bool pushWellRyde,
            Action rebuildLists)
        {
            if (!TryApplySupeyDriverEdit(existing, edited, out string applyErr))
            {
                MessageBox.Show(this, applyErr, "Drivers", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SetDriverWellRydePushStatus(applyErr);
                return;
            }

            rebuildLists?.Invoke();

            var disk = SupeyDriverRosterStore.Save(_supeyRoster);
            if (!disk.Ok)
            {
                MessageBox.Show(this, disk.ErrorMessage ?? "Save failed.", "Drivers",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetDriverWellRydePushStatus(disk.ErrorMessage);
                return;
            }

            _supeyRosterLastSaved = disk.SavedAtLocal;
            ScheduleBuilderDriverEmailsRegistry.UpdateLocalFromRoster(_supeyRoster, Environment.UserName);
            bool serverOk = await ScheduleBuilderDriverEmailsRegistry.PushToServerAsync(
                HiatmeAiSettings.Load(), _supeyRoster, Environment.UserName).ConfigureAwait(true);

            string wrLine = null;
            bool wrOk = true;
            if (pushWellRyde)
            {
                var wr = await PushSupeyDriverToWellRydeCoreAsync(edited).ConfigureAwait(true);
                wrOk = wr.Ok;
                wrLine = wr.Ok
                    ? wr.Message
                    : (wr.Message ?? "WellRyde update failed.");
            }

            if (pushWellRyde && wrOk)
            {
                int idx = IndexOfSupeyDriver(edited);
                if (idx >= 0)
                    _supeyRoster[idx] = edited;
                SupeyDriverRosterStore.Save(_supeyRoster);
                rebuildLists?.Invoke();
            }

            string summary = "Local roster: saved.\r\n"
                + (serverOk ? "AI server: synced." : "AI server: failed (start the panel at http://127.0.0.1:8787/).");
            if (pushWellRyde)
                summary += "\r\nWellRyde: " + (wrOk ? "updated." : ("FAILED — " + wrLine));
            else
                summary += "\r\nWellRyde: skipped (new driver — link via Pull from WellRyde first).";

            SetDriverWellRydePushStatus(summary.Replace("\r\n", " · "));
        }

        private void SaveSupeyRosterToDisk(bool showOk)
        {
            // Persist whatever the ListView currently knows about (caller should already have
            // mutated _supeyRoster for any add/edit/remove before calling).
            var saved = SupeyDriverRosterStore.Save(_supeyRoster);
            if (saved.Ok)
            {
                PersistSupeyDriverEmailsSafely();
                _supeyRosterLastSaved = saved.SavedAtLocal;
                if (_supeyRosterFooter != null && _supeyRoster != null)
                {
                    _supeyRosterFooter.Text = _supeyRoster.Count + " drivers · saved "
                        + saved.SavedAtLocal.ToString("HH:mm");
                }
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
                    var item = new ListViewItem(new[] { "", d.Name ?? "", (d.Email ?? "").Trim(), d.CapacityPassengers.ToString(), shift, "" })
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
            ListViewMinWidthEnforcer.Recompute(_supeyDriversLv);
            if (_supeyRosterFooter != null && _supeyRoster != null)
            {
                _supeyRosterFooter.Text = _supeyRoster.Count + " drivers" +
                    (_supeyRosterLastSaved == DateTime.MinValue ? "" : " · saved " + _supeyRosterLastSaved.ToString("HH:mm"));
            }
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

        private async Task OnSupeyDriverEditAsync()
        {
            if (_supeyDriversLv.SelectedItems.Count == 0) return;
            var item = _supeyDriversLv.SelectedItems[0];
            var existing = item.Tag as SupeyDriverProfile;
            if (existing == null) return;

            if (!string.IsNullOrWhiteSpace(existing.WellRydeSecId))
            {
                SetSupeyStatus("Loading " + (existing.Name ?? "driver") + " from WellRyde…");
                bool pulled = await TryRefreshSupeyDriverFromWellRydeAsync(existing).ConfigureAwait(true);
                if (!pulled)
                {
                    var dr = MessageBox.Show(this,
                        "Could not load the latest driver profile from WellRyde (sign-in or network).\r\n\r\n"
                        + "Edit anyway using the last data saved on this PC?",
                        "Supey — WellRyde",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning,
                        MessageBoxDefaultButton.Button2);
                    if (dr != DialogResult.Yes)
                    {
                        SetSupeyStatus("Edit canceled — WellRyde refresh failed.");
                        return;
                    }
                }
                else
                {
                    RebuildSupeyDriversList();
                    SetSupeyStatus("Loaded from WellRyde — capacity and shift unchanged (local).");
                }
            }

            using (var ed = new SupeyDriverEditorForm(existing))
            {
                if (ed.ShowDialog(this) != DialogResult.OK || ed.Result == null) return;
                await CommitDriverEditAsync(
                    existing,
                    ed.Result,
                    ed.SaveToWellRyde,
                    RebuildSupeyDriversList).ConfigureAwait(true);
            }
        }

        private void SetDriverWellRydePushStatus(string message)
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                SetSupeyStatus(message);
                SetScheduleBuilderStatus(message);
            }
        }

        /// <summary>One driver: portal name/home/vehicle; keeps capacity and shift.</summary>
        private async Task<bool> TryRefreshSupeyDriverFromWellRydeAsync(
            SupeyDriverProfile profile,
            CancellationToken cancellationToken = default)
        {
            if (profile == null)
                return false;
            if (string.IsNullOrWhiteSpace(profile.WellRydeSecId))
                return true;

            if (!await EnsureWellRydePortalSessionForSupeyAsync().ConfigureAwait(true)
                || _wellRydeSession == null)
                return false;

            return await FetchWellRydeDetailIntoProfileAsync(profile, cancellationToken).ConfigureAwait(true);
        }

        private async Task<WellRydeUserProfileSync.PushResult> PushSupeyDriverToWellRydeCoreAsync(
            SupeyDriverProfile profile)
        {
            if (profile == null)
                return new WellRydeUserProfileSync.PushResult { Ok = false, Message = "No driver profile." };

            try
            {
                SetDriverWellRydePushStatus("Saving driver to WellRyde…");
                WellRydePortalLog.Info("SUPEY", "PushProfile start driver=" + (profile.Name ?? "")
                    + " email=" + (profile.Email ?? ""));
                if (!await EnsureWellRydePortalSessionForSupeyAsync().ConfigureAwait(true)
                    || _wellRydeSession == null)
                {
                    return new WellRydeUserProfileSync.PushResult
                    {
                        Ok = false,
                        Message = "Sign-in cancelled or failed.",
                    };
                }

                var push = await WellRydeUserProfileSync.PushHomeAddressAsync(_wellRydeSession, profile)
                    .ConfigureAwait(true);
                if (!push.Ok && SupeyWellRydePushLooksLikeSessionFailure(push.Message))
                {
                    InvalidateWellRydePortalSession();
                    SetDriverWellRydePushStatus("WellRyde session expired — signing in again…");
                    if (await EnsureWellRydePortalSessionForSupeyAsync().ConfigureAwait(true)
                        && _wellRydeSession != null)
                    {
                        push = await WellRydeUserProfileSync.PushHomeAddressAsync(_wellRydeSession, profile)
                            .ConfigureAwait(true);
                    }
                }

                return push;
            }
            catch (Exception ex)
            {
                WellRydePortalLog.Error("SUPEY", "PushProfile exception", ex);
                return new WellRydeUserProfileSync.PushResult
                {
                    Ok = false,
                    Message = ex.Message,
                };
            }
        }

        private async Task PushSupeyDriverToWellRydeAsync(SupeyDriverProfile profile)
        {
            var push = await PushSupeyDriverToWellRydeCoreAsync(profile).ConfigureAwait(true);
            if (push.Ok)
            {
                int idx = IndexOfSupeyDriver(profile);
                if (idx >= 0)
                    _supeyRoster[idx] = profile;
                SaveSupeyRosterToDisk(showOk: false);
                RebuildSupeyDriversList();
                if (_fsDriversLv != null)
                    RebuildFsDriversList();
                SetDriverWellRydePushStatus(push.Message);
            }
            else
            {
                SetDriverWellRydePushStatus(push.Message ?? "WellRyde update failed.");
            }
        }

        private static bool SupeyWellRydePushLooksLikeSessionFailure(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;
            var m = message.ToLowerInvariant();
            return m.Contains("login") || m.Contains("session expired") || m.Contains("sign in")
                || m.Contains("jsessionid") || m.Contains("csrf")
                || m.Contains("html page instead of json") || m.Contains("not valid json");
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
                WellRydePortalLog.CopyErrorReport("Pull from WellRyde — sign-in failed", ex);
                return;
            }
            if (!ok || _wellRydeSession == null)
            {
                SetSupeyStatus("WellRyde sign-in cancelled or failed.");
                WellRydePortalLog.CopyErrorReport("Pull from WellRyde — sign-in cancelled or failed.");
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
                        SupeyWellRydeRosterMerge.ApplyPortalDetail(detail, existing, isNewDriver: false);
                        updated++;
                    }
                    else
                    {
                        var profile = new SupeyDriverProfile();
                        SupeyWellRydeRosterMerge.ApplyPortalDetail(detail, profile, isNewDriver: true);
                        _supeyRoster.Add(profile);
                        added++;
                    }
                }

                RebuildSupeyDriversList();
                SaveSupeyRosterToDisk(showOk: false);

                string msg = "Imported from WellRyde: " + added + " new" +
                    (updated > 0 ? ", " + updated + " updated (name/home from portal; capacity/shift kept)" : "") + ".";
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
        /// Re-fetch portal user detail for checked drivers that have a WellRyde SEC id (homes for BUILD).
        /// </summary>
        private async Task<bool> FetchWellRydeDetailIntoProfileAsync(
            SupeyDriverProfile profile,
            CancellationToken cancellationToken)
        {
            if (profile == null || _wellRydeSession == null)
                return false;

            string sec = WellRydePortalSession.NormalizeUserSecId(profile.WellRydeSecId);
            if (sec.Length == 0)
                return true;

            var res = await _wellRydeSession.GetUserDetailHtmlAsync(sec, cancellationToken)
                .ConfigureAwait(true);
            if (!res.IsSuccess || string.IsNullOrWhiteSpace(res.HtmlBody))
                return false;

            var detail = WellRydeUserParser.ParseUserDetail(sec, res.HtmlBody);
            SupeyWellRydeRosterMerge.ApplyPortalDetail(detail, profile, isNewDriver: false);
            return true;
        }

        private async Task<int> RefreshSupeyDriversFromWellRydeAsync(
            IList<SupeyDriverProfile> drivers,
            CancellationToken cancellationToken)
        {
            if (drivers == null || drivers.Count == 0)
                return 0;

            if (!await EnsureWellRydePortalSessionForBillingAsync().ConfigureAwait(true)
                || _wellRydeSession == null)
                return 0;

            int refreshed = 0;
            foreach (var profile in drivers)
            {
                if (profile == null) continue;
                cancellationToken.ThrowIfCancellationRequested();
                if (await FetchWellRydeDetailIntoProfileAsync(profile, cancellationToken).ConfigureAwait(true)
                    && !string.IsNullOrWhiteSpace(profile.WellRydeSecId))
                    refreshed++;
            }

            if (refreshed > 0)
            {
                RebuildSupeyDriversList();
                SaveSupeyRosterToDisk(showOk: false);
            }

            return refreshed;
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
                _ = PrefetchSupeyTripAddressesAfterLoadAsync();
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

        /// <summary>Background after LOAD TRIPS — warms office geocode cache before BUILD.</summary>
        private async Task PrefetchSupeyTripAddressesAfterLoadAsync()
        {
            if (!HiatmeGeoSettings.UseServer || _supeyLoadedTrips == null || _supeyLoadedTrips.Count == 0)
                return;
            try
            {
                SetSupeyStatus("LOAD done — caching trip addresses on office server…");
                var (submitted, already) = await SupeyLoadGeocodePrefetch.PrefetchTripsAsync(_supeyLoadedTrips)
                    .ConfigureAwait(true);
                int n = _supeyLoadedTrips.Count;
                var date = _supeyDatePicker.Value;
                string extra = submitted > 0
                    ? " · " + submitted + " looked up, " + already + " already cached"
                    : (already > 0 ? " · all " + already + " stops already cached" : "");
                SetSupeyStatus(BuildPostLoadStatus(n, date) + extra);
            }
            catch
            {
                /* BUILD will retry geocoding; desk does not need to act */
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
            _supeyLastServerProgressLabel = null;
            _supeyLastBuildStopReason = null;
            _supeyBuildLog.Clear();
            SupeyBuildLogAdd("BUILD started — " + (_supeyLoadedTrips?.Count ?? 0) + " trips loaded");

            try
            {
                int wrLinked = 0;
                foreach (var d in selected)
                {
                    if (d != null && !string.IsNullOrWhiteSpace(d.WellRydeSecId))
                        wrLinked++;
                }
                if (wrLinked > 0)
                {
                    SetSupeyToolbarBusy(true, "Refreshing " + wrLinked + " driver(s) from WellRyde…");
                    int refreshed = await RefreshSupeyDriversFromWellRydeAsync(selected, token)
                        .ConfigureAwait(true);
                    if (refreshed < wrLinked)
                        SetSupeyStatus(
                            "WellRyde refresh partial (" + refreshed + "/" + wrLinked
                            + ") — BUILD uses last portal data we could load.");
                }

                var date = _supeyDatePicker.Value;
                SetSupeyToolbarBusy(true, "Building schedule (geocode + assign)…");

                if (_supeyAiSettings == null)
                    _supeyAiSettings = HiatmeAiSettings.Load();

                SetSupeyToolbarBusy(true, "Checking office server (panel + OSRM + solve)…");
                var (readyOk, readyDetail) = await ScheduleBuildReadyGate.CheckAsync(
                        _supeyAiSettings, token)
                    .ConfigureAwait(true);
                if (!readyOk)
                {
                    MessageBox.Show(this, readyDetail, "Supey — server not ready",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    SetSupeyStatus("BUILD blocked — office server not ready for BUILD.");
                    return;
                }

                if (_supeyAiSettings != null && _supeyAiSettings.UseServerSolve
                    && HiatmeGeoSettings.UseServer)
                {
                    var (geoOk, geoDetail) = await SupeyGeocodeBuildGate.EnsureReadyAsync(
                            _supeyLoadedTrips, token)
                        .ConfigureAwait(true);
                    if (!geoOk)
                    {
                        MessageBox.Show(this, geoDetail, "Supey — geocode not ready",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        SetSupeyStatus("BUILD blocked — geocode cache incomplete.");
                        return;
                    }
                }

                // Rules + no-go areas from office panel when reachable.
                SupeyScheduleRules scheduleRules = SupeyDispatchRulesLoader.Load();
                if (HiatmeGeoSettings.UseServer)
                {
                    try
                    {
                        await HiatmeAiClient.GetOutOfAreaAsync(_supeyAiSettings, token)
                            .ConfigureAwait(true);
                    }
                    catch { SupeyOutOfArea.SetCachedAreas(SupeyOutOfArea.LoadLocalFallback()); }

                    try
                    {
                        var pre = await HiatmeAiClient.PreReviewAsync(_supeyAiSettings, token)
                            .ConfigureAwait(true);
                        if (pre?.RulesContext != null)
                        {
                            _supeyLastRulesContext = pre.RulesContext;
                            scheduleRules = SupeyScheduleRules.FromRulesContext(pre.RulesContext);
                        }
                    }
                    catch { /* local rules already loaded */ }
                }

                var hints = new SupeyTemplateHints(date.DayOfWeek.ToString());
                var startingLocks = _supeyResult?.Locks != null
                    ? new Dictionary<string, string>(_supeyResult.Locks, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                bool useTemplates = _supeyUseTemplatesCb == null || _supeyUseTemplatesCb.Checked;
                bool finishRemaining = _supeyFinishRemainingCb == null || _supeyFinishRemainingCb.Checked;
                if (_supeyAiSettings != null)
                {
                    _supeyAiSettings.UseWeekdayTemplates = useTemplates;
                    _supeyAiSettings.FinishRemainingAfterTemplates = finishRemaining;
                    try { _supeyAiSettings.Save(); } catch { }
                }

                SupeyTemplateMatchResult templateMatch = null;
                if (useTemplates)
                {
                    templateMatch = SupeyTemplateTripMatcher.Run(
                        date.Date, _supeyLoadedTrips, selected);
                    if (templateMatch?.Locks != null)
                    {
                        foreach (var kv in templateMatch.Locks)
                        {
                            if (string.IsNullOrWhiteSpace(kv.Key)) continue;
                            if (!startingLocks.ContainsKey(kv.Key))
                                startingLocks[kv.Key] = kv.Value;
                        }
                    }
                }

                if (startingLocks.Count > 0)
                {
                    int partnerAligned = SupeyPartnerLegHarmonizer.HarmonizeLocks(startingLocks);
                    if (partnerAligned > 0)
                        SupeyBuildLogAdd("Template locks: aligned " + partnerAligned + " B leg(s) to A driver.");
                }

                if (useTemplates && !finishRemaining)
                {
                    var tplAlgo = new SupeyScheduleAlgorithm { ScheduleRules = scheduleRules };
                    var tplProgress = new Progress<string>(msg =>
                    {
                        try
                        {
                            if (IsHandleCreated && !IsDisposed)
                                BeginInvoke((Action)(() => SetSupeyToolbarBusy(true, msg)));
                        }
                        catch { }
                    });
                    _supeyResult = await tplAlgo.BuildFromTemplateLocksAsync(
                        date, _supeyLoadedTrips, selected, templateMatch, startingLocks,
                        tplProgress, token).ConfigureAwait(true);
                    ApplySupeyTemplateBuildMeta(_supeyResult, templateMatch, useTemplates, finishRemaining);
                    _supeyLastBuildEngine = "template locks";
                    await CompleteSupeyBuildUiAsync(
                        date, hints, scheduleRules, serverSolveAttempted: false,
                        builtOnServer: false, templateMatch).ConfigureAwait(true);
                    return;
                }

                bool builtOnServer = false;
                bool serverSolveAttempted = false;

                if (_supeyAiSettings.UseServerSolve && HiatmeGeoSettings.UseServer)
                {
                    serverSolveAttempted = true;
                    try
                    {
                        int tripCount = _supeyLoadedTrips?.Count ?? 0;
                        var solveSw = Stopwatch.StartNew();
                        var solvePollCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                        var solvePollTask = PollServerSolveProgressAsync(
                            _supeyAiSettings, solveSw, tripCount, solvePollCts.Token);

                        SetSupeyToolbarBusy(true,
                            "Server solve — starting… 0:00 · " + tripCount + " trips");
                        bool remainderOnly = startingLocks.Count > 0;
                        var lockedNums = new HashSet<string>(
                            startingLocks.Keys, StringComparer.OrdinalIgnoreCase);
                        var solveCtx = remainderOnly
                            ? HiatmeScheduleContextBuilder.BuildForServerSolve(
                                date, _supeyRoster, _supeyLoadedTrips, selected,
                                t => !lockedNums.Contains(t.TripNumber ?? ""))
                            : HiatmeScheduleContextBuilder.Build(
                                date, _supeyRoster, _supeyLoadedTrips, null, false, selected);
                        ApplyWellRydeDispatcherToAiContext(solveCtx);
                        if (startingLocks.Count > 0)
                            solveCtx["locks"] = JObject.FromObject(startingLocks);
                        if (remainderOnly)
                        {
                            int sent = solveCtx["trip_count"]?.Value<int>() ?? 0;
                            _supeyBuildLog.Add(
                                "Server solve: " + sent + " unlocked trip(s), "
                                + startingLocks.Count + " template lock(s) merged after.");
                        }

                        HiatmeAiBuildResponse solveResp;
                        try
                        {
                            solveResp = await HiatmeAiClient.ScheduleSolveAsync(
                                _supeyAiSettings, solveCtx, token).ConfigureAwait(true);
                        }
                        finally
                        {
                            try { solvePollCts.Cancel(); } catch { }
                            try { solvePollTask.GetAwaiter().GetResult(); } catch { }
                            try { solvePollCts.Dispose(); } catch { }
                        }
                        if (solveResp?.Schedule != null)
                        {
                            _supeyLastServerSolveError = null;
                            if (solveResp.BuildLogLines != null)
                                _supeyBuildLog.AddServerLinesIncremental(solveResp.BuildLogLines);
                            ApplySupeyAiSchedule(solveResp, "BUILD", hydrateMap: false);
                            if (remainderOnly)
                            {
                                if (templateMatch != null)
                                {
                                    var serverAssigned = new HashSet<string>(
                                        StringComparer.OrdinalIgnoreCase);
                                    foreach (var plan in _supeyResult.DriverPlans)
                                    {
                                        if (plan?.Groups == null) continue;
                                        foreach (var g in plan.Groups)
                                        {
                                            if (g?.Trips == null) continue;
                                            foreach (var t in g.Trips)
                                            {
                                                if (!string.IsNullOrWhiteSpace(t?.TripNumber))
                                                    serverAssigned.Add(t.TripNumber);
                                            }
                                        }
                                    }
                                    await SupeyServerSolveMerge.ApplyTemplateScheduleAsync(
                                        _supeyResult,
                                        templateMatch,
                                        startingLocks,
                                        _supeyLoadedTrips,
                                        selected,
                                        serverAssigned,
                                        token).ConfigureAwait(true);
                                }
                                else if (startingLocks.Count > 0)
                                {
                                    SupeyServerSolveMerge.ApplyTemplateLocks(
                                        _supeyResult, startingLocks, _supeyLoadedTrips, selected);
                                }
                            }
                            ApplySupeyTemplateBuildMeta(_supeyResult, templateMatch, useTemplates, finishRemaining);
                            _supeyLastBuildEngine = "server " + (solveResp.Solver ?? "greedy");
                            _supeyLastBuildStats = solveResp.BuildStats;
                            ApplySupeyBuildDiagnostics(solveResp);
                            builtOnServer = true;
                            SetSupeyLastBuildSummary(_supeyLastBuildEngine, solveResp.BuildStats);
                            await CompleteSupeyBuildUiAsync(
                                date, hints, scheduleRules, serverSolveAttempted: true,
                                builtOnServer: true, templateMatch).ConfigureAwait(true);
                        }
                        else if (solveResp != null)
                        {
                            _supeyLastServerSolveError = "Server returned an empty schedule.";
                        }
                    }
                    catch (Exception ex) when (!(ex is OperationCanceledException))
                    {
                        _supeyLastServerSolveError = ex.Message;
                        SetSupeyStatus("Server solve unavailable — using local builder… (" +
                            ex.Message + ")");
                    }
                }

                if (!builtOnServer)
                {
                    if (serverSolveAttempted && _supeyAiSettings != null
                        && !_supeyAiSettings.AllowLocalSolveFallback)
                    {
                        string err = string.IsNullOrWhiteSpace(_supeyLastServerSolveError)
                            ? "Office server solve did not return a schedule."
                            : _supeyLastServerSolveError;
                        MessageBox.Show(this,
                            "BUILD requires the office AI server (POST /api/hiatme/solve).\n\n"
                            + err + "\n\n"
                            + "Fix the panel URL, token, and OSRM on the server PC, then try again.\n"
                            + "Local desktop-only BUILD is disabled for dispatch desks "
                            + "(dev: set AllowLocalSolveFallback in hiatme_ai.json or "
                            + "HIATME_ALLOW_LOCAL_SOLVE_FALLBACK=1).",
                            "Supey — server required",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                        SetSupeyStatus("BUILD stopped — server solve required (" + err + ").");
                        return;
                    }

                    var algo = new SupeyScheduleAlgorithm
                    {
                        Hints = hints,
                        UseTemplateHints = useTemplates,
                        ScheduleRules = scheduleRules,
                    };
                    var progress = new Progress<string>(msg =>
                    {
                        try
                        {
                            if (IsHandleCreated && !IsDisposed)
                                BeginInvoke((Action)(() => SetSupeyToolbarBusy(true, msg)));
                        }
                        catch { }
                    });

                    if (useTemplates && finishRemaining && templateMatch != null)
                    {
                        _supeyResult = await algo.BuildTemplateThenFinishAsync(
                            date, _supeyLoadedTrips, selected, templateMatch, startingLocks,
                            progress, token).ConfigureAwait(true);
                        _supeyLastBuildEngine = "templates + desk-timing finish";
                    }
                    else
                    {
                        _supeyResult = await algo.BuildAsync(
                            date, _supeyLoadedTrips, selected, startingLocks, progress, token)
                            .ConfigureAwait(true);
                        _supeyLastBuildEngine = "local C# (desk timing)";
                    }

                    ApplySupeyTemplateBuildMeta(_supeyResult, templateMatch, useTemplates, finishRemaining);
                    await CompleteSupeyBuildUiAsync(
                        date, hints, scheduleRules, serverSolveAttempted,
                        builtOnServer: false, templateMatch).ConfigureAwait(true);
                }
            }
            catch (OperationCanceledException ex)
            {
                _supeyLastBuildStopReason = DescribeSupeyBuildStop(ex, token);
                SupeyBuildLogAdd(_supeyLastBuildStopReason);
                SetSupeyStatus(_supeyLastBuildStopReason);
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

        private string DescribeSupeyBuildStop(Exception ex, CancellationToken token)
        {
            string phase = string.IsNullOrWhiteSpace(_supeyLastServerProgressLabel)
                ? ""
                : " Stopped during: " + _supeyLastServerProgressLabel.Trim() + ".";
            if (token.IsCancellationRequested)
                return "Build canceled." + phase;
            if (ex is TaskCanceledException && ex.InnerException is TimeoutException)
                return "Build timed out (office server may still be working — wait and retry)." + phase;
            if (ex is TaskCanceledException)
                return "Build stopped (connection or server timeout — you did not need to click Cancel)." + phase;
            if (ex is System.Net.Http.HttpRequestException)
                return "Build stopped (lost connection to office server)." + phase;
            return "Build stopped." + phase;
        }

        private async Task OnSupeyRefreshRoutesAndNotesAsync()
        {
            if (_supeyResult == null || _supeyResult.DriverPlans.Count == 0)
            {
                SetSupeyStatus("Nothing to refresh — build a schedule first.");
                return;
            }

            try
            {
                SetSupeyToolbarBusy(true, "Refreshing routes and notes…");
                await SupeyDriverPlanManualEdit.RefreshEntireScheduleAsync(_supeyResult, CancellationToken.None)
                    .ConfigureAwait(true);
                OnSupeyPreviewDriverChanged();
                if (_supeyPreviewDriverCb?.SelectedItem is SupeyPreviewItem itm && itm.Plan != null)
                    _supeyMap?.ShowDriverPlan(itm.Plan);
                BindSupeyPreview();
                SetSupeyStatus("Routes, group miles, and notes updated — review then SAVE WORKBOOK.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not refresh routes and notes:\n\n" + ex.Message,
                    "Supey Schedule", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SetSupeyStatus("Refresh failed.");
            }
            finally
            {
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
            SupeyBuildLogAdd("Cancel clicked — stopping BUILD.");
            _supeyLastBuildStopReason = "Build canceled (Cancel button).";
            try { _supeyCts?.Cancel(); } catch { }
        }

        // ---------- Preview wiring ----------

        private void BindSupeyPreview()
        {
            if (InvokeRequired)
            {
                RunOnSupeyUiThread(BindSupeyPreview);
                return;
            }
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
            if (_supeyResult.TotalReserveCount > 0)
            {
                string resLabel = "Reserves · " + _supeyResult.TotalReserveCount + " trip(s)";
                if (_supeyResult.ReservesWillCalls.Count > 0)
                    resLabel += " (" + _supeyResult.ReservesWillCalls.Count + " will call)";
                if (_supeyResult.ReservesReroute.Count > 0)
                    resLabel += " (" + _supeyResult.ReservesReroute.Count + " reroute)";
                _supeyPreviewDriverCb.Items.Add(new SupeyPreviewItem(null, resLabel));
            }
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
                            + _supeyResult.TotalReserveCount;
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
            if (_supeyResult?.ReservesWillCalls?.Count > 0)
            {
                for (int i = 0; i < _supeyPreviewDriverCb.Items.Count; i++)
                {
                    var pi = _supeyPreviewDriverCb.Items[i] as SupeyPreviewItem;
                    if (pi != null && pi.Kind == SupeyPreviewItem.ItemKind.Reserves)
                    {
                        pick = i;
                        _supeyPreviewDriverCb.SelectedIndex = pick;
                        return;
                    }
                }
            }
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

        private void BindReservesPreviewSections()
        {
            if (_supeyResult == null) return;
            if (_supeyResult.ReservesWillCalls.Count > 0)
            {
                AddReservesSectionHeader("Will calls (" + _supeyResult.ReservesWillCalls.Count + ")");
                foreach (var t in _supeyResult.ReservesWillCalls)
                    AddReservesTripRow(t, ScheduleBuilderReserveBuckets.WillCallBand);
            }
            if (_supeyResult.Reserves.Count > 0)
            {
                AddReservesSectionHeader("Reservers (" + _supeyResult.Reserves.Count + ")");
                foreach (var t in _supeyResult.Reserves)
                    AddReservesTripRow(t, ScheduleBuilderReserveBuckets.ReserversBand);
            }
            if (_supeyResult.ReservesReroute.Count > 0)
            {
                AddReservesSectionHeader("Reroutes (" + _supeyResult.ReservesReroute.Count + ")");
                foreach (var t in _supeyResult.ReservesReroute)
                    AddReservesTripRow(t, ScheduleBuilderReserveBuckets.RerouteBand);
            }
        }

        private void AddSupeyPreviewGapRow(string noteText = "")
        {
            string note = (noteText ?? "").Trim();
            var cells = new[] { "", "", note, "", "", "", "", "", "", "", "" };
            var blank = new ListViewItem(cells);
            blank.UseItemStyleForSubItems = false;
            blank.SubItems[0].BackColor = SupeyTheme.SurfaceHeader;
            if (note.Length > 0)
            {
                blank.SubItems[2].ForeColor = SupeyTheme.TextSecondary;
                blank.Font = new Font(_supeyPreviewLv.Font, FontStyle.Italic);
            }
            blank.Tag = new SupeyPreviewGapTag(note);
            _supeyPreviewLv.Items.Add(blank);
        }

        private void AddReservesSectionHeader(string title)
        {
            var hdr = new ListViewItem(new[] { "—", "", title, "", "", "", "", "", "", "", "" });
            hdr.UseItemStyleForSubItems = false;
            hdr.Font = new Font(_supeyPreviewLv.Font, FontStyle.Bold);
            hdr.SubItems[2].ForeColor = SupeyTheme.TextPrimary;
            hdr.SubItems[0].BackColor = SupeyTheme.SurfaceHeader;
            _supeyPreviewLv.Items.Add(hdr);
        }

        private void AddReservesTripRow(MCDownloadedTrip t, Color bandColor)
        {
            string puAddr = SupeyTripTimes.FormatEndpoint(t.PUStreet, t.PUCity);
            string doAddr = SupeyTripTimes.FormatEndpoint(t.DOStreet, t.DOCITY);
            var lvi = new ListViewItem(new[]
            {
                "—",
                t.TripNumber ?? "",
                t.ClientFullName ?? "",
                SupeyTripGeocodeStatus.CheckPin,
                t.PUTime ?? "",
                "—",
                puAddr,
                doAddr,
                t.DOTime ?? "",
                "—",
                t.Miles ?? "",
            });
            lvi.UseItemStyleForSubItems = false;
            lvi.SubItems[0].BackColor = bandColor;
            lvi.SubItems[0].ForeColor = Color.White;
            StyleGeoSubItem(lvi.SubItems[SupeyPrevColGeoIndex], SupeyTripGeocodeStatus.CheckPin);
            _supeyPreviewLv.Items.Add(lvi);
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
                RestoreSupeyPreviewListSorter();
                BindWarningsPreview();
                _supeyPreviewLv.EndUpdate();
                ListViewMinWidthEnforcer.Recompute(_supeyPreviewLv);
                return;
            }

            if (item.Kind == SupeyPreviewItem.ItemKind.LoadedTrips)
            {
                RestoreSupeyPreviewListSorter();
                BindLoadedTripsPreview();
                _supeyPreviewLv.EndUpdate();
                ListViewMinWidthEnforcer.Recompute(_supeyPreviewLv);
                return;
            }

            if (item.Plan != null)
            {
                _supeyPreviewLv.ListViewItemSorter = null;
                _supeyPreviewLv.Sorting = SortOrder.None;
                // Rows already reordered in SupeySchedulePostBuild — avoid mutating plan on every preview change.

                if (item.Plan.TemplateDisplaySlots != null)
                {
                    foreach (var slot in item.Plan.TemplateDisplaySlots)
                    {
                        if (slot?.Kind == SupeyTemplateSlot.SlotKind.Gap
                            && !string.IsNullOrWhiteSpace(slot.NoteText))
                            AddSupeyPreviewGapRow(slot.NoteText);
                    }
                }
                foreach (var g in item.Plan.Groups)
                {
                    if (g == null) continue;
                    AddGroupRouteHeaderRow(g, item.Plan);
                    foreach (int ti in SupeyClusterDisplayOrder.PickupVisitIndices(g))
                        AddSupeyPreviewTripRow(g, item.Plan, ti);
                }
                var planForMap = item.Plan;
                _supeyPreviewStatsLbl.Text = "Trips: " + item.Plan.RiderCount + " · groups: " + item.Plan.Groups.Count +
                    " · drive " + SupeyTripTimes.FormatHoursMinutesFromSeconds(item.Plan.TotalDriveSeconds) +
                    " · " + SupeyTripTimes.FormatMiles(item.Plan.TotalMeters) +
                    (item.Plan.LastDropoff.HasValue ? " · last DO " + SupeyTripTimes.FormatTimeOfDay(item.Plan.LastDropoff.Value) : "") +
                    (item.Plan.ReleaseTimeOfDay.HasValue ? " · release " + SupeyTripTimes.FormatTimeOfDay(item.Plan.ReleaseTimeOfDay.Value) : "") +
                    (item.Plan.Warnings.Count > 0 ? " · " + item.Plan.Warnings.Count + " warning(s)" : "");
                BeginInvoke((Action)(() =>
                {
                    if (IsDisposed) return;
                    _supeyMap?.ShowDriverPlan(planForMap);
                    FocusPreviewTripRow(_supeyPreviewLv.SelectedItems.Count > 0 ? _supeyPreviewLv.SelectedItems[0] : null);
                }));
            }
            else
            {
                RestoreSupeyPreviewListSorter();
                BindReservesPreviewSections();
                _supeyMap.Clear();
                int wc = _supeyResult.ReservesWillCalls.Count;
                int need = _supeyResult.Reserves.Count;
                int rer = _supeyResult.ReservesReroute.Count;
                _supeyPreviewStatsLbl.Text = "Reserves: " + _supeyResult.TotalReserveCount
                    + (wc > 0 ? " — " + wc + " will call" : "")
                    + " — " + need + " reserver"
                    + (rer > 0 ? ", " + rer + " reroute" : "")
                    + ". Reroute trips are not auto-assigned on BUILD.";
            }

            _supeyPreviewLv.EndUpdate();
            ListViewMinWidthEnforcer.Recompute(_supeyPreviewLv);
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
                string puAddr = SupeyTripTimes.FormatEndpoint(t.PUStreet, t.PUCity);
                string doAddr = SupeyTripTimes.FormatEndpoint(t.DOStreet, t.DOCITY);
                var lvi = new ListViewItem(new[]
                {
                    "—",
                    t.TripNumber ?? "",
                    t.ClientFullName ?? "",
                    "",
                    t.PUTime ?? "",
                    "—",
                    puAddr,
                    doAddr,
                    t.DOTime ?? t.SchedDOTime ?? "",
                    "—",
                    t.Miles ?? "",
                });
                lvi.Tag = t;
                _supeyPreviewLv.Items.Add(lvi);
            }

            _supeyMap?.Clear();
            _supeyPreviewStatsLbl.Text = sorted.Count
                + " trips downloaded · click BUILD — this list will switch to the AI schedule per driver.";
        }

        private static string SupeyWarningDedupeKey(SupeyWarning w) =>
            SupeyWarningsUtil.DedupeKey(w);

        private void BindWarningsPreview()
        {
            int total = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // Build-level warnings appear under a "Build" pseudo-driver so the user can see at a
            // glance whether the issue is roster-wide (e.g. a driver home that won't geocode) or
            // specific to one trip cluster.
            foreach (var w in _supeyResult.BuildWarnings)
            {
                if (w == null || !seen.Add(SupeyWarningDedupeKey(w))) continue;
                AddWarningRow(w, "Build");
                total++;
            }
            foreach (var p in _supeyResult.DriverPlans)
            {
                foreach (var w in p.Warnings)
                {
                    if (w == null || !seen.Add(SupeyWarningDedupeKey(w))) continue;
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
            string scope = driverName ?? "";
            if (scope == "Build" && !string.IsNullOrWhiteSpace(w.DriverName))
                scope = w.DriverName;
            var lvi = new ListViewItem(new[]
            {
                kindLabel,
                string.IsNullOrEmpty(w.TripNumber) ? "—" : w.TripNumber,
                scope,
                w.Kind == SupeyWarningKind.MissingGeo ? SupeyTripGeocodeStatus.CheckPin : "",
                "",
                "",
                w.Detail ?? "",
                "",
                "",
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
                case SupeyWarningKind.UnassignedToReserves: return "Reserve";
                case SupeyWarningKind.OutOfServiceArea: return "Reroute";
                case SupeyWarningKind.LateArrival: return "Late DO";
                case SupeyWarningKind.TightArrival: return "Tight";
                case SupeyWarningKind.LateNextPickup: return "Late PU";
                case SupeyWarningKind.StraightLineFallback: return "OSRM";
                case SupeyWarningKind.BuildDiagnostic: return "Build";
                default: return k.ToString();
            }
        }

        private static Color ColorForWarningKind(SupeyWarningKind k)
        {
            switch (k)
            {
                case SupeyWarningKind.MissingGeo: return Color.FromArgb(232, 168, 96);    // amber — data quality
                case SupeyWarningKind.UnassignedToReserves: return Color.FromArgb(200, 140, 220); // purple — unassigned
                case SupeyWarningKind.OutOfServiceArea: return Color.FromArgb(200, 140, 80);   // amber — reroute
                case SupeyWarningKind.LateArrival: return Color.FromArgb(232, 96, 96);    // red   — hard miss
                case SupeyWarningKind.LateNextPickup: return Color.FromArgb(232, 96, 96); // red
                case SupeyWarningKind.TightArrival: return Color.FromArgb(232, 220, 96);  // yellow — within margin
                case SupeyWarningKind.StraightLineFallback: return Color.FromArgb(160, 160, 160); // grey — informational
                case SupeyWarningKind.BuildDiagnostic: return Color.FromArgb(120, 180, 220);   // blue — build log
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
            SupeyListViewHelpers.DrawColumnHeader(e);
        }

        private Color SupeyPreviewRowBackground(ListViewItem item, bool selected)
        {
            if (selected) return SupeyLvSel;
            if (item?.Tag is SupeyPreviewGroupHeaderTag tag)
                return SupeyRouteHeaderBackColor(tag.Group.DisplayColor);
            return SupeyLvBg;
        }

        private Color SupeyPreviewCellBackground(ListViewItem item, ListViewItem.ListViewSubItem subItem, int columnIndex, bool selected, Color rowBg)
        {
            if (selected) return SupeyLvSel;
            if (item?.Tag is SupeyPreviewGroupHeaderTag)
                return rowBg;
            if (columnIndex == 0 && subItem != null && subItem.BackColor != Color.Empty && subItem.BackColor != SupeyLvBg)
                return subItem.BackColor;
            return rowBg;
        }

        private void SupeyPreviewLv_DrawItem(object sender, DrawListViewItemEventArgs e)
        {
            SupeyListViewHelpers.SuppressDefaultDrawItem(e);
        }

        private void SupeyPreviewLv_DrawSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            bool sel = e.Item != null && e.Item.Selected;
            bool routeHeader = e.Item?.Tag is SupeyPreviewGroupHeaderTag;
            Color rowBg = SupeyPreviewRowBackground(e.Item, sel);
            Color fill = SupeyPreviewCellBackground(e.Item, e.SubItem, e.ColumnIndex, sel, rowBg);
            SupeyListViewHelpers.DrawSubItemCellBackground(e, fill);

            var bounds = new Rectangle(e.Bounds.Left + 6, e.Bounds.Top, e.Bounds.Width - 6, e.Bounds.Height);
            Color textColor = sel ? SupeyLvSelText : SupeyLvText;
            if (!sel)
            {
                if (e.ColumnIndex == SupeyPrevColGeoIndex && e.SubItem.ForeColor != Color.Empty)
                    textColor = e.SubItem.ForeColor;
                else if (e.ColumnIndex == 0 && e.SubItem.ForeColor != Color.Empty)
                    textColor = e.SubItem.ForeColor;
                else if (routeHeader)
                    textColor = SupeyLvHeaderText;
            }

            Font drawFont = routeHeader && e.ColumnIndex == 2 && _supeyPreviewRouteFont != null
                ? _supeyPreviewRouteFont
                : _supeyPreviewLv.Font;
            TextRenderer.DrawText(e.Graphics,
                SupeyListViewHelpers.GetCellDisplayText(_supeyPreviewLv, e.ColumnIndex, e.SubItem.Text ?? ""),
                drawFont, bounds, textColor,
                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.WordEllipsis | TextFormatFlags.GlyphOverhangPadding);

            SupeyListViewHelpers.DrawCellGridLines(e.Graphics, e.Bounds, _supeyPreviewLv);
        }

        // ---------- Owner-draw for the roster ListView (mirrors preview's look) ----------

        private void SupeyDriversLv_DrawColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            SupeyListViewHelpers.DrawColumnHeader(e);
        }

        private void SupeyDriversLv_DrawItem(object sender, DrawListViewItemEventArgs e)
        {
            SupeyListViewHelpers.SuppressDefaultDrawItem(e);
        }

        private void SupeyDriversLv_DrawSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            bool sel = e.Item != null && e.Item.Selected;
            SupeyListViewHelpers.DrawSubItemCellBackground(e, sel ? SupeyLvSel : SupeyLvBg);

            if (e.ColumnIndex == 0)
            {
                SupeyListViewHelpers.DrawModernCheckbox(e.Graphics, e.Bounds,
                    e.Item != null && e.Item.Checked, sel);
                SupeyListViewHelpers.DrawCellGridLines(e.Graphics, e.Bounds, _supeyDriversLv);
                return;
            }

            var bounds = new Rectangle(e.Bounds.Left + 6, e.Bounds.Top, e.Bounds.Width - 6, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics,
                SupeyListViewHelpers.GetCellDisplayText(_supeyDriversLv, e.ColumnIndex, e.SubItem.Text ?? ""),
                _supeyDriversLv.Font, bounds, sel ? SupeyLvSelText : SupeyLvText,
                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.WordEllipsis | TextFormatFlags.GlyphOverhangPadding);

            SupeyListViewHelpers.DrawCellGridLines(e.Graphics, e.Bounds, _supeyDriversLv);
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
            if (_supeyRefreshNotesBtn != null)
            {
                _supeyRefreshNotesBtn.Visible = _supeyResult != null && _supeyCts == null;
                _supeyRefreshNotesBtn.Enabled = _supeyResult != null
                    && _supeyCts == null
                    && _supeyResult.DriverPlans.Count > 0;
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
            _supeyToolTip = SupeyToolTip.Create(autoPopDelay: 12000, initialDelay: 350);
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

        private void UpdateSupeyTemplateBuildHint()
        {
            if (_supeyToolbarStatusLbl == null) return;
            if (_supeyUseTemplatesCb == null || !_supeyUseTemplatesCb.Checked)
                return;
            string dow = _supeyDatePicker?.Value.DayOfWeek.ToString() ?? "weekday";
            string fin = (_supeyFinishRemainingCb != null && _supeyFinishRemainingCb.Checked)
                ? "Supey will run on leftovers"
                : "Supey will not run — non-template trips → Reservers";
            _supeyToolbarStatusLbl.Text = "Will use " + dow + " CSVs · " + fin;
        }

        private void ApplySupeyTemplateBuildMeta(
            SupeyScheduleResult result,
            SupeyTemplateMatchResult templateMatch,
            bool useTemplates,
            bool finishRemaining)
        {
            if (result == null) return;
            var meta = new SupeyTemplateBuildMeta
            {
                Weekday = _supeyDatePicker?.Value.DayOfWeek.ToString() ?? "",
                FinishRemainingWasOn = finishRemaining,
            };
            if (!useTemplates || templateMatch == null || !templateMatch.HadTemplates)
            {
                meta.Mode = SupeyTemplateBuildMode.SupeyOnly;
                result.TemplateBuild = meta;
                return;
            }

            meta.Mode = finishRemaining
                ? SupeyTemplateBuildMode.TemplateThenSupey
                : SupeyTemplateBuildMode.TemplateSeedOnly;
            meta.TemplateMatched = templateMatch.MatchedCount;
            meta.TemplateUnmatchedRows = templateMatch.UnmatchedTemplateRowCount;
            meta.OrphanTemplateDriverTabs = templateMatch.OrphanTemplateDriverTabs?.Count ?? 0;
            meta.TripsLockedByTemplate = templateMatch.MatchedCount;
            int assigned = HiatmeAiScheduleMapper.CountAssignedTrips(result);
            meta.TripsAssignedBySolver = Math.Max(0, assigned - meta.TripsLockedByTemplate);
            meta.WillCallCount = result.ReservesWillCalls.Count;
            meta.ReserverCount = result.Reserves.Count;
            meta.RerouteCount = result.ReservesReroute.Count;
            meta.TripsToReserversAfterTemplate = result.TotalReserveCount;

            result.TemplateBuild = meta;
            if (meta.Mode == SupeyTemplateBuildMode.TemplateSeedOnly)
            {
                result.BuildWarnings.Add(new SupeyWarning(
                    SupeyWarningKind.UnassignedToReserves,
                    "",
                    "Templates",
                    "Finish remaining is OFF — non-template trips are in Reservers. "
                    + "Turn on Finish remaining to auto-assign them."));
            }
        }

        private string FormatSupeyTemplateCompareText(
            SupeyTemplateCompare compare,
            SupeyTemplateBuildMeta meta)
        {
            if (compare == null)
                return "No template compare.";
            if (meta == null || meta.Mode == SupeyTemplateBuildMode.SupeyOnly)
                return compare.SummaryText;

            var lines = new List<string>();
            lines.Add(meta.FormatTemplatePassLine());
            if (meta.Mode == SupeyTemplateBuildMode.TemplateSeedOnly)
            {
                if (compare.HadTemplates && compare.TemplateTripCount > 0)
                {
                    double pct = 100.0 * meta.TripsLockedByTemplate / compare.TemplateTripCount;
                    lines.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "Locks applied: {0:F0}% ({1}/{2}) on roster.",
                        pct, meta.TripsLockedByTemplate, compare.TemplateTripCount));
                }
                lines.Add("Supey finish was OFF — compare does not judge reserves.");
            }
            else
            {
                lines.Add(compare.SummaryText);
            }

            return string.Join(Environment.NewLine, lines);
        }

        private void RecordSupeyBuildOptions(bool serverSolveAttempted, bool builtOnServer)
        {
            if (_supeyResult == null) return;
            bool useTemplates = _supeyUseTemplatesCb != null && _supeyUseTemplatesCb.Checked;
            bool finishRemaining = useTemplates
                && (_supeyFinishRemainingCb == null
                    || (_supeyFinishRemainingCb.Enabled && _supeyFinishRemainingCb.Checked));
            _supeyResult.BuildOptions = SupeyBuildOptionsSnapshot.Capture(
                _supeyResult.ServiceDate != default(DateTime)
                    ? _supeyResult.ServiceDate
                    : _supeyDatePicker.Value.Date,
                useTemplates,
                finishRemaining,
                _supeyAiSettings,
                serverSolveAttempted,
                builtOnServer,
                _supeyLastBuildEngine,
                _supeyResult.TemplateBuild,
                GetCheckedSupeyDrivers()?.Count ?? 0);
        }

        private async Task CompleteSupeyBuildUiAsync(
            DateTime date,
            SupeyTemplateHints hints,
            SupeyScheduleRules scheduleRules,
            bool serverSolveAttempted,
            bool builtOnServer,
            SupeyTemplateMatchResult templateMatch)
        {
            _supeyTripsPanelView = SupeyTripsPanelView.AiSchedule;
            if (_supeyResult != null)
            {
                SetSupeyToolbarBusy(true, "Preparing routes (all drivers)…");
                bool postBuildOk = true;
                try
                {
                    await Task.Run(() => SupeySchedulePostBuild.FinalizeAsync(
                        _supeyResult,
                        new Progress<string>(msg =>
                        {
                            if (IsHandleCreated && !IsDisposed)
                                SetSupeyToolbarBusy(true, msg);
                        }),
                        CancellationToken.None)).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    postBuildOk = false;
                    SupeyBuildLogAdd("Post-build FAILED: " + ex.Message);
                    _supeyResult.BuildWarnings.Add(new SupeyWarning(
                        SupeyWarningKind.BuildDiagnostic,
                        "",
                        "Post-build",
                        "Post-build routing FAILED — schedule order may be wrong. "
                        + ex.Message + " See BUILD log."));
                }
                if (!postBuildOk)
                    SetSupeyStatus("BUILD finished but post-build routing FAILED — see Warnings.");
            }
            if (_supeyResult != null)
                _supeyLastBuildStats = StatsFromSupeyResult(_supeyResult, _supeyLoadedTrips?.Count ?? 0);
            await HydrateSupeyGeocodeForMapAsync(refreshMapPolylines: false).ConfigureAwait(true);

            // OSRM post-build runs off the UI thread; all WinForms updates must marshal back.
            RunOnSupeyUiThread(() => ApplySupeyBuildUiAfterPostBuild(
                hints, serverSolveAttempted, builtOnServer));
        }

        private void ApplySupeyBuildUiAfterPostBuild(
            SupeyTemplateHints hints,
            bool serverSolveAttempted,
            bool builtOnServer)
        {
            SetSupeyToolbarBusy(false, null);
            if (_supeyResult != null)
            {
                SupeyWarningsUtil.StripTimingFromBuild(_supeyResult);
                SupeyWillCallPickup.EnforceOnResult(_supeyResult, _supeyLoadedTrips);
            }
            BindSupeyPreview();
            _supeyLastTemplateCompare = SupeyTemplateCompare.Run(_supeyResult, hints);
            if (_supeyTemplateCompareLbl != null)
                _supeyTemplateCompareLbl.Text = FormatSupeyTemplateCompareText(
                    _supeyLastTemplateCompare, _supeyResult?.TemplateBuild);
            SyncSupeyScheduleToServer("build");

            if (_supeyResult?.TemplateBuild != null)
            {
                if (_supeyScheduleUpdatedLbl != null)
                {
                    string when = DateTime.Now.ToString("h:mm tt");
                    string hdr = _supeyResult.TemplateBuild.Mode == SupeyTemplateBuildMode.TemplateSeedOnly
                        ? "BUILD · " + _supeyResult.TemplateBuild.Weekday + " templates · Supey did not run"
                        : "BUILD · " + _supeyResult.TemplateBuild.Weekday + " templates seeded · Supey filled remainder";
                    _supeyScheduleUpdatedLbl.Text = "Schedule on screen · updated " + when + " · " + hdr;
                    _supeyScheduleUpdatedLbl.ForeColor = Color.FromArgb(144, 238, 144);
                }
            }
            else
                MarkSupeyScheduleUpdated("BUILD");

            SetSupeyAiLastAppliedLabel("BUILD");
            _ = RefreshSupeyRulesPanelAsync();

            _supeyLastBuildEngine = builtOnServer
                ? _supeyLastBuildEngine
                : (serverSolveAttempted
                    ? "local C# (server failed, desktop fallback)"
                    : "local C#");
            if (_supeyResult?.TemplateBuild != null
                && _supeyResult.TemplateBuild.Mode != SupeyTemplateBuildMode.SupeyOnly)
            {
                _supeyLastBuildEngine = _supeyResult.TemplateBuild.FormatModeTag() + " · " + _supeyLastBuildEngine;
            }

            _supeyLastBuildStats = StatsFromSupeyResult(_supeyResult, _supeyLoadedTrips?.Count ?? 0);
            RefreshScreenAssignmentBuildWarning(_supeyResult, _supeyLastBuildStats);
            RecordSupeyBuildOptions(serverSolveAttempted, builtOnServer);
            SupeyBuildEngineLabel.SyncBuildWarning(
                _supeyResult, _supeyLastBuildEngine, _supeyLastServerSolveError);
            SetSupeyLastBuildSummary(_supeyLastBuildEngine, _supeyLastBuildStats);

            if (_supeyResult != null && _supeyResult.TotalReserveCount > 0)
            {
                _supeyResult.BuildWarnings.Add(new SupeyWarning(
                    SupeyWarningKind.UnassignedToReserves,
                    "",
                    "Build",
                    _supeyResult.TotalReserveCount +
                    " trip(s) in reserves — Warnings → Copy for AI review for trip numbers."));
            }

            if (_supeyResult == null) return;

            if (_supeyResult.HasInfeasibleDriverRejection)
            {
                string names = string.Join(", ", _supeyResult.InfeasibleDriverNames);
                if (_supeyScheduleUpdatedLbl != null)
                {
                    _supeyScheduleUpdatedLbl.Text = "BUILD rejected impossible day(s): " + names
                        + " — trips in reserves; see Warnings.";
                    _supeyScheduleUpdatedLbl.ForeColor = Color.FromArgb(255, 160, 120);
                }
                MessageBox.Show(this,
                    "BUILD cannot use this day for:\n" + names
                    + "\n\nThose trips were moved to reserves. The PU/DO sheet does not fit "
                    + "in real drive time (e.g. two groups at the same pickup minute in different towns). "
                    + "Warnings → Copy for details.",
                    "Supey — day infeasible",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }

            int scheduled = HiatmeAiScheduleMapper.CountAssignedTrips(_supeyResult);
            string engineLabel = builtOnServer ? "server" : "local";
            if (_supeyResult.HasInfeasibleDriverRejection)
            {
                SetSupeyStatus("BUILD finished with rejected day(s) (" + engineLabel + "). "
                    + scheduled + " on screen, " + _supeyResult.TotalReserveCount
                    + " reserve(s) — " + string.Join(", ", _supeyResult.InfeasibleDriverNames)
                    + " could not run in PU/DO windows.");
            }
            else
            {
                SetSupeyStatus("Build complete (" + engineLabel + "). " + _supeyResult.DriverPlans.Count +
                    " driver(s), " + scheduled + " on screen, " +
                    _supeyResult.TotalReserveCount + " reserve(s), " +
                    _supeyResult.WarningCount + " warning(s).");
            }
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
            if (_supeyLastBuildLbl != null)
            {
                _supeyLastBuildLbl.Text = "";
                _supeyLastBuildLbl.Visible = false;
            }
        }

        /// <summary>Persistent BUILD summary on the bottom status strip (always visible after BUILD).</summary>
        private void SetSupeyLastBuildSummary(string engine, HiatmeBuildStats stats)
        {
            if (_supeyLastBuildLbl == null) return;
            string when = DateTime.Now.ToString("h:mm tt");
            string eng = string.IsNullOrWhiteSpace(engine) ? "?" : engine.Trim();
            int total = stats?.TripsTotal ?? 0;
            int assigned = stats?.TripsAssigned ?? 0;
            int reserves = stats?.ReservesCount ?? 0;
            int noGeo = stats?.NoGeoCount ?? 0;
            int unplaced = stats?.UnassignedGroupsCount ?? 0;
            int clusters = stats?.ClusterCount ?? 0;
            int withCoords = stats?.TripsWithCoords ?? 0;

            string line = "Last BUILD " + when + " · " + eng
                + " · " + assigned + "/" + (total > 0 ? total.ToString() : "?") + " assigned"
                + " · " + reserves + " reserves";
            if (noGeo > 0)
                line += " · " + noGeo + " no geocode";
            if (unplaced > 0)
                line += " · " + unplaced + " groups unplaced";
            if (clusters > 0)
                line += " · " + clusters + " groups";

            string tip = line;
            if (total > 0 && withCoords > 0 && withCoords < total)
                tip += Environment.NewLine + withCoords + " of " + total +
                    " trips had PU+DO coordinates for the solver.";
            if (stats != null && stats.GeocodedNew > 0)
                tip += Environment.NewLine + stats.GeocodedNew +
                    " new address(es) geocoded this run (rest from cache).";

            _supeyLastBuildLbl.Text = line;
            _supeyLastBuildLbl.Visible = true;
            EnsureSupeyToolTip();
            _supeyToolTip.SetToolTip(_supeyLastBuildLbl, tip);

            double assignFrac = total > 0 ? (double)assigned / total : 0;
            if (assignFrac >= 0.85 && reserves < 15)
                _supeyLastBuildLbl.ForeColor = Color.FromArgb(144, 238, 144);
            else if (assignFrac >= 0.5)
                _supeyLastBuildLbl.ForeColor = Color.FromArgb(255, 200, 100);
            else
                _supeyLastBuildLbl.ForeColor = Color.FromArgb(255, 140, 140);
        }

        /// <summary>Replace server-only trip counts after template merge lands on screen.</summary>
        private static void RefreshScreenAssignmentBuildWarning(
            SupeyScheduleResult result, HiatmeBuildStats stats)
        {
            if (result?.BuildWarnings == null || stats == null) return;
            result.BuildWarnings.RemoveAll(w =>
            {
                if (w == null || w.Kind != SupeyWarningKind.BuildDiagnostic) return false;
                string d = w.Detail ?? "";
                return d.IndexOf("Built with server", StringComparison.OrdinalIgnoreCase) >= 0
                    || (d.IndexOf("Only ", StringComparison.OrdinalIgnoreCase) >= 0
                        && d.IndexOf("trips on drivers", StringComparison.OrdinalIgnoreCase) >= 0);
            });
            int driversWithTrips = 0;
            if (result.DriverPlans != null)
            {
                foreach (var p in result.DriverPlans)
                {
                    if (p?.Groups != null && p.Groups.Count > 0)
                        driversWithTrips++;
                }
            }
            result.BuildWarnings.Add(new SupeyWarning(
                SupeyWarningKind.BuildDiagnostic,
                "",
                "Build",
                "On screen after template merge: " + stats.TripsAssigned + " trip(s) on "
                + driversWithTrips + " driver(s), " + stats.ReservesCount + " reserve(s)."));
        }

        private static HiatmeBuildStats StatsFromSupeyResult(
            SupeyScheduleResult result, int tripsLoaded)
        {
            if (result == null) return null;
            int assigned = HiatmeAiScheduleMapper.CountAssignedTrips(result);
            int noGeo = 0;
            if (result.BuildWarnings != null)
            {
                foreach (var w in result.BuildWarnings)
                {
                    if (w != null && w.Kind == SupeyWarningKind.MissingGeo)
                        noGeo++;
                }
            }
            int reserves = result.TotalReserveCount;
            return new HiatmeBuildStats
            {
                TripsTotal = tripsLoaded > 0 ? tripsLoaded : assigned + reserves,
                TripsAssigned = assigned,
                ReservesCount = reserves,
                NoGeoCount = noGeo,
                UnassignedGroupsCount = 0,
                ClusterCount = result.DriverPlans.Sum(p => p.Groups.Count),
            };
        }

        private async Task PollServerSolveProgressAsync(
            HiatmeAiSettings settings,
            Stopwatch sw,
            int tripCount,
            CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(1500, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                HiatmeAiClient.BuildProgressStatus prog = null;
                try
                {
                    prog = await HiatmeAiClient.GetBuildProgressAsync(settings, token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch { }

                string msg = FormatServerSolveProgress(prog, sw.Elapsed, tripCount);
                _supeyLastServerProgressLabel = msg;
                if (!string.Equals(msg, _supeyLastLoggedProgressLine, StringComparison.Ordinal))
                {
                    _supeyLastLoggedProgressLine = msg;
                    SupeyBuildLogAdd(msg);
                }
                if (prog?.LogLines != null && prog.LogLines.Count > 0)
                    _supeyBuildLog.AddServerLinesIncremental(prog.LogLines);
                try
                {
                    if (IsHandleCreated && !IsDisposed && _supeyCts != null)
                        BeginInvoke((Action)(() => SetSupeyToolbarBusy(true, msg)));
                }
                catch { }
            }
        }

        private static string FormatServerSolveProgress(
            HiatmeAiClient.BuildProgressStatus prog,
            TimeSpan elapsed,
            int tripCount)
        {
            string clock = elapsed.ToString(@"m\:ss");
            if (prog == null || !prog.Active)
            {
                return "Server solve… " + clock + " · " + tripCount + " trips"
                    + " (waiting for server)";
            }

            string label = string.IsNullOrWhiteSpace(prog.Label)
                ? "Working"
                : prog.Label.Trim();
            string counts = "";
            if (prog.Total > 0)
            {
                int left = Math.Max(0, prog.Total - prog.Done);
                counts = " · " + prog.Done + "/" + prog.Total + " done"
                    + (left > 0 ? ", " + left + " left" : "");
            }

            string eta = "";
            if (prog.EtaSeconds.HasValue && prog.EtaSeconds.Value > 60)
                eta = " · ~" + (prog.EtaSeconds.Value / 60) + " min left";
            else if (prog.EtaSeconds.HasValue && prog.EtaSeconds.Value > 0)
                eta = " · ~" + prog.EtaSeconds.Value + "s left";

            string detail = string.IsNullOrWhiteSpace(prog.Detail)
                ? ""
                : " · " + prog.Detail.Trim();

            string osrm = "";
            if (prog.LogLines != null)
            {
                for (int i = prog.LogLines.Count - 1; i >= 0 && i >= prog.LogLines.Count - 6; i--)
                {
                    string line = prog.LogLines[i] ?? "";
                    if (line.IndexOf("osrm_http", StringComparison.OrdinalIgnoreCase) >= 0
                        || line.IndexOf("table=", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        osrm = " · " + line.Trim();
                        break;
                    }
                }
            }

            return "Server solve — " + label + counts + eta + " · " + clock + detail + osrm;
        }

        private void RunOnSupeyUiThread(Action action)
        {
            if (action == null) return;
            if (InvokeRequired)
            {
                try { Invoke(action); }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
                return;
            }
            action();
        }

        private void SetSupeyToolbarBusy(bool busy, string msg)
        {
            if (_supeyToolbar == null) return;
            if (InvokeRequired)
            {
                try { BeginInvoke((Action)(() => SetSupeyToolbarBusy(busy, msg))); }
                catch { }
                return;
            }
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

        private void SupeyBuildLogAdd(string line)
        {
            _supeyBuildLog?.Add(line);
        }

        private void CopySupeyBuildLogToClipboard()
        {
            try
            {
                var date = _supeyDatePicker != null ? _supeyDatePicker.Value.Date : DateTime.Today;
                if (_supeyAiSettings == null)
                    _supeyAiSettings = HiatmeAiSettings.Load();
                string serverHint = "data/hiatme/last-build-log.txt on office server (PROJECTS_ROOT)";
                string text = SupeyBuildLogExport.Build(
                    date,
                    _supeyBuildLog,
                    _supeyAiSettings,
                    _supeyLoadedTrips?.Count ?? 0,
                    _supeyToolbarStatusLbl?.Text,
                    _supeyLastBuildStopReason,
                    _supeyLastBuildStats,
                    serverHint);
                if (string.IsNullOrWhiteSpace(text))
                {
                    SetSupeyStatus("BUILD log is empty — run BUILD first.");
                    return;
                }
                Clipboard.SetText(text);
                SetSupeyStatus("BUILD log copied — paste into Cursor.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not copy BUILD log:\n\n" + ex.Message,
                    "Supey Schedule", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
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

            void Apply()
            {
                if (_supeyOsrmStatusPill == null || _supeyOsrmStatusPill.IsDisposed) return;
                string label;
                Color dot;
                if (HiatmeGeoSettings.UseServer && serverGeo != null && serverGeo.OsrmLocalOk)
                {
                    label = "OSRM · server";
                    dot = SupeyTheme.SuccessText;
                }
                else if (HiatmeGeoSettings.ServerOnly)
                {
                    label = "OSRM · server required";
                    dot = SupeyTheme.WarnText;
                }
                else if (serverGeo != null)
                {
                    label = "OSRM · server down";
                    dot = SupeyTheme.WarnText;
                }
                else
                {
                    label = "OSRM · offline mode";
                    dot = SupeyTheme.TextMuted;
                }

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
            if (_supeyPreviewLv.SelectedItems.Count == 0)
            {
                _supeyMap?.ClearTripSelectionHighlight();
                return;
            }

            var tripNumbers = new List<string>();
            foreach (ListViewItem row in _supeyPreviewLv.SelectedItems)
            {
                if (row?.Tag is SupeyPreviewRowTag tag && tag.Trip != null)
                {
                    string tn = (tag.Trip.TripNumber ?? "").Trim();
                    if (tn.Length > 0
                        && !tripNumbers.Any(x => string.Equals(x, tn, StringComparison.OrdinalIgnoreCase)))
                        tripNumbers.Add(tn);
                }
            }
            _supeyMap?.SetSelectedTripHighlight(tripNumbers);

            FocusPreviewTripRow(_supeyPreviewLv.SelectedItems[0]);
        }

        private void FocusPreviewTripRow(ListViewItem row)
        {
            if (row?.Tag is SupeyPreviewRowTag tag && tag.Trip != null)
                _supeyMap?.FocusTrip(tag.Trip);
            else if (row?.Tag is SupeyPreviewGroupHeaderTag hdr)
                _supeyMap?.FocusGroup(hdr.Group);
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

        private void RestoreSupeyPreviewListSorter()
        {
            if (_supeyPreviewLv == null) return;
            if (_supeyPreviewLv.ListViewItemSorter == null)
                ListViewSorter.Attach(_supeyPreviewLv);
        }

        private bool SupeyPreviewAllowsTripDrag()
        {
            if (_supeyResult == null) return false;
            var itm = _supeyPreviewDriverCb?.SelectedItem as SupeyPreviewItem;
            return itm != null
                && itm.Kind == SupeyPreviewItem.ItemKind.Driver
                && itm.Plan != null
                && itm.Plan.Groups.Count > 0;
        }

        private List<SupeyDriverPlanManualEdit.PreviewLine> ParseSupeyPreviewLines()
        {
            var lines = new List<SupeyDriverPlanManualEdit.PreviewLine>();
            if (_supeyPreviewLv == null) return lines;
            foreach (ListViewItem item in _supeyPreviewLv.Items)
            {
                if (item.Tag is SupeyPreviewGapTag gapTag)
                {
                    lines.Add(new SupeyDriverPlanManualEdit.PreviewLine
                    {
                        Kind = SupeyDriverPlanManualEdit.PreviewLineKind.Gap,
                        GapNoteText = gapTag.NoteText,
                    });
                    continue;
                }
                if (item.Tag is SupeyPreviewGroupHeaderTag)
                {
                    if (lines.Count > 0 && lines[lines.Count - 1].Kind == SupeyDriverPlanManualEdit.PreviewLineKind.Trip)
                    {
                        lines.Add(new SupeyDriverPlanManualEdit.PreviewLine
                            { Kind = SupeyDriverPlanManualEdit.PreviewLineKind.Gap });
                    }
                    continue;
                }
                if (item.Tag is SupeyPreviewRowTag row)
                {
                    lines.Add(new SupeyDriverPlanManualEdit.PreviewLine
                    {
                        Kind = SupeyDriverPlanManualEdit.PreviewLineKind.Trip,
                        Trip = row.Trip,
                    });
                }
            }
            return lines;
        }

        private int CountSupeyPreviewLines()
        {
            int n = 0;
            if (_supeyPreviewLv == null) return 0;
            foreach (ListViewItem item in _supeyPreviewLv.Items)
            {
                if (item.Tag is SupeyPreviewGapTag || item.Tag is SupeyPreviewRowTag)
                    n++;
            }
            return n;
        }

        private int ListViewIndexToPreviewLineIndex(int itemIndex, out SupeyPreviewRowTag tripTag, out bool isGap)
        {
            tripTag = null;
            isGap = false;
            int line = 0;
            for (int i = 0; i < _supeyPreviewLv.Items.Count; i++)
            {
                var item = _supeyPreviewLv.Items[i];
                if (item.Tag is SupeyPreviewGroupHeaderTag)
                    continue;
                if (i == itemIndex)
                {
                    isGap = item.Tag is SupeyPreviewGapTag;
                    tripTag = item.Tag as SupeyPreviewRowTag;
                    return line;
                }
                if (item.Tag is SupeyPreviewGapTag || item.Tag is SupeyPreviewRowTag)
                    line++;
            }
            return line;
        }

        private bool TryGetSupeyTripDropTarget(Point clientPt, out int insertBeforeLine, out bool mergeOntoTrip,
            out SupeyPreviewRowTag targetTrip)
        {
            insertBeforeLine = 0;
            mergeOntoTrip = false;
            targetTrip = null;
            if (_supeyPreviewLv == null) return false;

            var hit = _supeyPreviewLv.HitTest(clientPt);
            if (hit.Item == null)
            {
                insertBeforeLine = CountSupeyPreviewLines();
                return true;
            }

            if (hit.Item.Tag is SupeyPreviewGroupHeaderTag)
            {
                insertBeforeLine = ListViewIndexToPreviewLineIndex(hit.Item.Index + 1, out _, out _);
                if (insertBeforeLine < 0) insertBeforeLine = CountSupeyPreviewLines();
                return true;
            }

            int lineIdx = ListViewIndexToPreviewLineIndex(hit.Item.Index, out targetTrip, out bool isGap);
            if (isGap)
            {
                insertBeforeLine = lineIdx;
                return true;
            }

            if (targetTrip == null) return false;

            var bounds = hit.Item.GetBounds(ItemBoundsPortion.Entire);
            int relY = clientPt.Y - bounds.Top;
            int h = Math.Max(bounds.Height, 1);
            if (relY < h / 3)
            {
                insertBeforeLine = lineIdx;
                return true;
            }
            if (relY > h * 2 / 3)
            {
                insertBeforeLine = lineIdx + 1;
                return true;
            }

            mergeOntoTrip = true;
            insertBeforeLine = lineIdx;
            return true;
        }

        private void SupeyPreviewLv_ItemDrag(object sender, ItemDragEventArgs e)
        {
            if (!SupeyPreviewAllowsTripDrag()) return;
            if (!(e.Item is ListViewItem lvi) || !(lvi.Tag is SupeyPreviewRowTag tag)) return;
            _supeyDragTripTag = tag;
            DoDragDrop(tag, DragDropEffects.Move);
        }

        private void SupeyPreviewLv_DragEnter(object sender, DragEventArgs e)
        {
            if (!SupeyPreviewAllowsTripDrag()) return;
            if (e.Data.GetDataPresent(typeof(SupeyPreviewRowTag)))
                e.Effect = DragDropEffects.Move;
        }

        private void SupeyPreviewLv_DragOver(object sender, DragEventArgs e)
        {
            if (!SupeyPreviewAllowsTripDrag() || !e.Data.GetDataPresent(typeof(SupeyPreviewRowTag)))
            {
                e.Effect = DragDropEffects.None;
                return;
            }
            e.Effect = DragDropEffects.Move;
        }

        private async void SupeyPreviewLv_DragDrop(object sender, DragEventArgs e)
        {
            if (!SupeyPreviewAllowsTripDrag()) return;
            if (!e.Data.GetDataPresent(typeof(SupeyPreviewRowTag))) return;
            var dragTag = e.Data.GetData(typeof(SupeyPreviewRowTag)) as SupeyPreviewRowTag;
            if (dragTag?.Trip == null || dragTag.Plan == null) return;

            var pt = _supeyPreviewLv.PointToClient(new Point(e.X, e.Y));
            if (!TryGetSupeyTripDropTarget(pt, out int insertLine, out bool merge, out SupeyPreviewRowTag dropOnRow))
                return;

            var lines = ParseSupeyPreviewLines();
            if (lines.Count == 0) return;

            MCDownloadedTrip dropOnTrip = dropOnRow?.Trip;
            SupeyDriverPlanManualEdit.ApplyTripMove(lines, dragTag.Trip, dropOnTrip, insertLine, merge);
            SupeyDriverPlanManualEdit.RebuildGroupsFromLines(dragTag.Plan, lines);

            SetSupeyStatus(merge
                ? "Merged trip — updating route…"
                : "Moved trip — updating route…");
            try
            {
                var planToRefresh = dragTag.Plan;
                await SupeyDriverPlanManualEdit.RefreshRoutingAsync(planToRefresh, CancellationToken.None)
                    .ConfigureAwait(true);
                if (_supeyResult != null)
                    SupeyDriverPlanManualEdit.SyncDriverWarningsToResult(_supeyResult);
                OnSupeyPreviewDriverChanged();
                if (_supeyMap != null && planToRefresh != null)
                    _supeyMap.ShowDriverPlan(planToRefresh);
                SetSupeyStatus("Schedule updated — routes and miles refreshed on map.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not refresh routes after move:\n\n" + ex.Message,
                    "Supey Schedule", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                OnSupeyPreviewDriverChanged();
            }
        }

        private void SupeyDriversLv_DragEnter(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(SupeyPreviewRowTag))) return;
            e.Effect = DragDropEffects.Move;
        }

        private void SupeyDriversLv_DragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(SupeyPreviewRowTag)))
            {
                e.Effect = DragDropEffects.None;
                return;
            }
            e.Effect = DragDropEffects.Move;
        }

        private async void SupeyDriversLv_DragDrop(object sender, DragEventArgs e)
        {
            if (_supeyResult == null) return;
            if (!e.Data.GetDataPresent(typeof(SupeyPreviewRowTag))) return;
            var dragTag = e.Data.GetData(typeof(SupeyPreviewRowTag)) as SupeyPreviewRowTag;
            if (dragTag?.Trip == null || dragTag.Plan == null) return;

            var pt = _supeyDriversLv.PointToClient(new Point(e.X, e.Y));
            var hit = _supeyDriversLv.HitTest(pt);
            var targetDriver = hit.Item?.Tag as SupeyDriverProfile;
            if (targetDriver == null)
                return;

            SupeyDriverPlan toPlan = null;
            foreach (var p in _supeyResult.DriverPlans)
            {
                if (p.Driver != null
                    && string.Equals(p.Driver.Name, targetDriver.Name, StringComparison.OrdinalIgnoreCase))
                {
                    toPlan = p;
                    break;
                }
            }
            if (toPlan == null || ReferenceEquals(toPlan, dragTag.Plan)) return;

            if (!SupeyDriverPlanManualEdit.MoveTripBetweenDrivers(_supeyResult, dragTag.Plan, toPlan, dragTag.Trip))
                return;

            SetSupeyStatus("Moving trip to " + (targetDriver.Name ?? "driver") + "…");
            try
            {
                await SupeyDriverPlanManualEdit.RefreshRoutingAsync(dragTag.Plan, CancellationToken.None)
                    .ConfigureAwait(true);
                await SupeyDriverPlanManualEdit.RefreshRoutingAsync(toPlan, CancellationToken.None)
                    .ConfigureAwait(true);
                SupeyDriverPlanManualEdit.SyncDriverWarningsToResult(_supeyResult);

                foreach (var obj in _supeyPreviewDriverCb.Items)
                {
                    var item = obj as SupeyPreviewItem;
                    if (item == null) continue;
                    if (item.Plan == toPlan)
                    {
                        _supeyPreviewDriverCb.SelectedItem = item;
                        break;
                    }
                }
                OnSupeyPreviewDriverChanged();
                _supeyMap?.ShowDriverPlan(toPlan);
                SetSupeyStatus("Trip moved to " + (targetDriver.Name ?? "driver") + " — solo group at end of day.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not move trip to another driver:\n\n" + ex.Message,
                    "Supey Schedule", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                OnSupeyPreviewDriverChanged();
            }
        }

        private void AddGroupRouteHeaderRow(SupeyTripCluster g, SupeyDriverPlan plan)
        {
            string note = SupeyDriverPlanManualEdit.FormatRouteHeader(g);
            if (plan?.Groups != null && plan.Groups.Count > 1)
            {
                int idx = plan.Groups.IndexOf(g);
                if (idx >= 0)
                    note += " · day order " + (idx + 1) + "/" + plan.Groups.Count;
            }
            var minPu = SupeyClusterTimeSplit.MinPickupTime(g);
            if (minPu != TimeSpan.Zero)
                note += " · first PU " + SupeyTripTimes.FormatTimeOfDay(minPu);
            var lvi = new ListViewItem(new[]
            {
                g.GroupNumber.ToString(),
                "Route",
                note,
                "",
                "",
                "",
                "",
                "",
                "",
                "",
                "",
            });
            lvi.UseItemStyleForSubItems = false;
            lvi.Tag = new SupeyPreviewGroupHeaderTag(g, plan);
            lvi.SubItems[0].BackColor = g.DisplayColor;
            lvi.SubItems[0].ForeColor = Color.Black;
            lvi.ToolTipText = note;
            _supeyPreviewLv.Items.Add(lvi);
        }

        private static Color SupeyRouteHeaderBackColor(Color groupColor)
        {
            int r = Math.Max(0, groupColor.R - 48);
            int gr = Math.Max(0, groupColor.G - 48);
            int b = Math.Max(0, groupColor.B - 48);
            return Color.FromArgb(255, r, gr, b);
        }

        private static SupeyTripCluster FindSupeyClusterForTrip(SupeyDriverPlan plan, MCDownloadedTrip t)
        {
            if (plan?.Groups == null || t == null) return null;
            string tn = t.TripNumber ?? "";
            foreach (var g in plan.Groups)
            {
                if (g?.Trips == null) continue;
                for (int i = 0; i < g.Trips.Count; i++)
                {
                    if (ReferenceEquals(g.Trips[i], t)
                        || (!string.IsNullOrWhiteSpace(tn)
                            && string.Equals(g.Trips[i].TripNumber, tn, StringComparison.OrdinalIgnoreCase)))
                        return g;
                }
            }
            return null;
        }

        private void AddSupeyPreviewTripRow(SupeyTripCluster g, SupeyDriverPlan plan, int ti)
        {
            if (g == null || plan == null || ti < 0 || ti >= g.Trips.Count) return;
            var t = g.Trips[ti];
            string puAddr = SupeyTripTimes.FormatEndpoint(t.PUStreet, t.PUCity);
            string doAddr = SupeyTripTimes.FormatEndpoint(t.DOStreet, t.DOCITY);
            string geo = SupeyTripGeocodeStatus.ForScheduledTrip(t, g, plan, ti);
            SupeyTripProjectedTiming timing = null;
            string tn = (t.TripNumber ?? "").Trim();
            if (!string.IsNullOrEmpty(tn) && plan.TripTimings != null)
                plan.TripTimings.TryGetValue(tn, out timing);
            string estPu = timing?.EstPu.HasValue == true
                ? SupeyTripTimes.FormatTimeOfDay(timing.EstPu) : "—";
            string late = !string.IsNullOrWhiteSpace(timing?.LateLabel)
                ? timing.LateLabel : "—";
            var lvi = new ListViewItem(new[]
            {
                g.GroupNumber.ToString(),
                t.TripNumber ?? "",
                t.ClientFullName ?? "",
                geo,
                t.PUTime ?? "",
                estPu,
                puAddr,
                doAddr,
                t.DOTime ?? "",
                late,
                t.Miles ?? "",
            });
            lvi.UseItemStyleForSubItems = false;
            lvi.Tag = new SupeyPreviewRowTag(g, t, plan, ti);
            lvi.SubItems[0].BackColor = g.DisplayColor;
            lvi.SubItems[0].ForeColor = Color.Black;
            StyleGeoSubItem(lvi.SubItems[SupeyPrevColGeoIndex], geo);
            if (late != "—" && late.IndexOf('L') >= 0)
                lvi.SubItems[SupeyPrevColLateIndex].ForeColor = Color.FromArgb(255, 120, 120);
            _supeyPreviewLv.Items.Add(lvi);
        }

        private sealed class SupeyPreviewGapTag
        {
            public string NoteText { get; }
            public SupeyPreviewGapTag(string noteText) => NoteText = noteText ?? "";
        }

        private sealed class SupeyPreviewGroupHeaderTag
        {
            public SupeyTripCluster Group { get; }
            public SupeyDriverPlan Plan { get; }
            public SupeyPreviewGroupHeaderTag(SupeyTripCluster g, SupeyDriverPlan p)
            {
                Group = g;
                Plan = p;
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
