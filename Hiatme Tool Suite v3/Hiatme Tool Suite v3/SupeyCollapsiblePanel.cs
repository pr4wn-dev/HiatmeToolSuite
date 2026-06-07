using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Dark-themed collapsible panel used as the dock container for every section on the
    /// Supey tab (Drivers, AI Assistant, Info, Trips). Single-source of truth for header
    /// chrome — every panel ends up with the same height, the same divider stripe, the
    /// same chevron, the same typography. Pulls all colors / fonts from
    /// <see cref="SupeyTheme"/>.
    /// </summary>
    internal sealed class SupeyCollapsiblePanel : Panel
    {
        private const int HeaderHeight = 34;
        private const int CollapsedSideHeaderHeight = 42;
        private const int CollapsedToggleWidth = 32;
        private const int CollapsedAccentWidth = 3;
        private const int CollapsedMinRailWidth = 88;

        private readonly Panel _header;
        private readonly TableLayoutPanel _headerLayout;
        private readonly Panel _accentStripe;
        private readonly Label _titleLabel;
        private readonly Label _toggleBtn;
        private readonly Panel _bottomDivider;
        private Control _belowHeaderToolbar;
        private bool _contentPanelDetached;
        private bool _expanded = true;
        private bool _applyingExpandedState;
        private DockStyle _savedSideDock = DockStyle.None;

        public Panel ContentPanel { get; }

        internal int CollapsedSideHeaderHeightPx => CollapsedSideHeaderHeight;

        /// <summary>Height of optional toolbar row shown below the title when side-collapsed.</summary>
        public int CollapsedSideToolbarHeight { get; set; }

        internal int CollapsedSideStackHeightPx =>
            CollapsedSideHeaderHeight
            + (_belowHeaderToolbar != null && !_expanded ? CollapsedSideToolbarHeight : 0);

        internal bool IsInCollapsedSideStack(DockStyle edge) =>
            !_expanded
            && _savedSideDock == edge
            && Dock == DockStyle.None;

        /// <summary>Fired whenever the panel toggles between expanded and collapsed.
        /// The workspace builder uses this to hide the resize splitter when collapsed
        /// (a splitter on a 34px-wide collapsed panel is more confusing than useful).</summary>
        public event EventHandler ExpandedChanged;

        public bool Expanded
        {
            get => _expanded;
            set
            {
                if (_expanded == value) return;
                _expanded = value;

                Control host = Parent;
                host?.SuspendLayout();
                SuspendLayout();
                try
                {
                    ApplyExpandedState();
                }
                finally
                {
                    ResumeLayout(false);
                    host?.ResumeLayout(true);
                }

                ExpandedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private int _expandedWidth = 280;
        private int _expandedHeight = 220;
        private int _minExpandedWidth = 180;
        private int _maxExpandedWidth = 600;

        public int ExpandedWidth
        {
            get => _expandedWidth;
            set
            {
                if (_expandedWidth == value) return;
                _expandedWidth = value;
                if (_expanded && IsHorizontalDock()) ApplyExpandedState();
            }
        }

        public int ExpandedHeight
        {
            get => _expandedHeight;
            set
            {
                if (_expandedHeight == value) return;
                _expandedHeight = value;
                if (_expanded && IsVerticalDock()) ApplyExpandedState();
            }
        }

        public int CollapsedThickness { get; set; } = HeaderHeight;

        /// <summary>Narrow width for collapsed Left/Right panels — header row only, no custom paint.</summary>
        public int CollapsedRailWidth { get; set; } = 112;

        public int MinExpandedWidth
        {
            get => _minExpandedWidth;
            set
            {
                if (_minExpandedWidth == value) return;
                _minExpandedWidth = value;
                if (_expanded && IsHorizontalDock()) ApplyExpandedState();
            }
        }

        public int MaxExpandedWidth
        {
            get => _maxExpandedWidth;
            set
            {
                if (_maxExpandedWidth == value) return;
                _maxExpandedWidth = value;
                if (_expanded && IsHorizontalDock()) ApplyExpandedState();
            }
        }

        public int MinExpandedHeight { get; set; } = 120;

        public void ApplyExpandedLayout() => ApplyExpandedState();

        internal void EnsureCollapsedHeaderVisible() => RefreshCollapsedHeaderLayout();

        /// <summary>Dock a toolbar strip under the title bar (stays visible when side-collapsed).</summary>
        public void SetBelowHeaderToolbar(Control toolbar, int collapsedHeight)
        {
            if (_belowHeaderToolbar != null)
                Controls.Remove(_belowHeaderToolbar);
            _belowHeaderToolbar = toolbar;
            CollapsedSideToolbarHeight = Math.Max(0, collapsedHeight);
            if (toolbar == null) return;
            toolbar.Dock = DockStyle.Top;
            Controls.Add(toolbar);
            Controls.SetChildIndex(toolbar, Controls.GetChildIndex(_header));
            ApplyExpandedState();
        }

        public SupeyCollapsiblePanel()
        {
            BackColor = SupeyTheme.Surface;
            Padding = new Padding(0);

            _header = new Panel
            {
                Dock = DockStyle.Top,
                Height = HeaderHeight,
                BackColor = SupeyTheme.SurfaceHeader,
                Cursor = Cursors.Hand,
            };

            _headerLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = SupeyTheme.SurfaceHeader,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            _headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 3f));
            _headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            _headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32f));
            _headerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            _accentStripe = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                BackColor = SupeyTheme.AccentStripe,
            };

            _titleLabel = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = SupeyTheme.TextPrimary,
                BackColor = SupeyTheme.SurfaceHeader,
                Font = SupeyTheme.HeaderFont,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 0, 0),
                Text = "Section",
                Cursor = Cursors.Hand,
                AutoEllipsis = true,
            };

            _toggleBtn = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = SupeyTheme.TextSecondary,
                BackColor = SupeyTheme.SurfaceHeader,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                Text = "◀",
                Cursor = Cursors.Hand,
                AutoSize = false,
            };

            _bottomDivider = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 1,
                BackColor = SupeyTheme.Divider,
            };

            _headerLayout.Controls.Add(_accentStripe, 0, 0);
            _headerLayout.Controls.Add(_titleLabel, 1, 0);
            _headerLayout.Controls.Add(_toggleBtn, 2, 0);
            _header.Controls.Add(_headerLayout);
            _header.Controls.Add(_bottomDivider);

            ContentPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.Surface,
                Padding = new Padding(8),
                AutoScroll = false,
            };

            Controls.Add(ContentPanel);
            Controls.Add(_header);

            EnableDoubleBuffered(_header);
            EnableDoubleBuffered(_headerLayout);
            EnableDoubleBuffered(this);

            _header.Click += ToggleExpanded;
            _headerLayout.Click += ToggleExpanded;
            _titleLabel.Click += ToggleExpanded;
            _toggleBtn.Click += ToggleExpanded;
        }

        private void ToggleExpanded(object sender, EventArgs e) => Expanded = !Expanded;

        private static void EnableDoubleBuffered(Control control)
        {
            if (control == null) return;
            typeof(Control).InvokeMember(
                "DoubleBuffered",
                BindingFlags.SetProperty | BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                control,
                new object[] { true });
        }

        public string Title
        {
            get => _titleLabel.Text;
            set => _titleLabel.Text = value ?? "";
        }

        public DockStyle PanelDock
        {
            set
            {
                Dock = value;
                ApplyExpandedState();
            }
        }

        private static bool IsHorizontalDock(DockStyle dock) =>
            dock == DockStyle.Left || dock == DockStyle.Right;

        private static bool IsVerticalDock(DockStyle dock) =>
            dock == DockStyle.Top || dock == DockStyle.Bottom;

        private bool IsHorizontalDock() => IsHorizontalDock(Dock);

        private bool IsVerticalDock() => IsVerticalDock(Dock);

        private int ResolveExpandedWidth()
        {
            int w = _expandedWidth;
            if (_minExpandedWidth > 0 && w < _minExpandedWidth) w = _minExpandedWidth;
            if (_maxExpandedWidth > 0 && w > _maxExpandedWidth) w = _maxExpandedWidth;
            return w;
        }

        private DockStyle EffectiveSideDock
        {
            get
            {
                if (_savedSideDock == DockStyle.Left || _savedSideDock == DockStyle.Right)
                    return _savedSideDock;
                if (Dock == DockStyle.Left || Dock == DockStyle.Right)
                    return Dock;
                return DockStyle.None;
            }
        }

        private void ApplyExpandedState()
        {
            _applyingExpandedState = true;
            try
            {
                DockStyle sideDock = EffectiveSideDock;
                bool sideCollapsed = !_expanded && (sideDock == DockStyle.Left || sideDock == DockStyle.Right);
                bool verticalCollapsed = !_expanded && !sideCollapsed;

                BackColor = sideCollapsed ? SupeyTheme.SurfaceHeader : SupeyTheme.Surface;
                Cursor = Cursors.Hand;

                _header.Height = sideCollapsed ? CollapsedSideHeaderHeight : HeaderHeight;
                _titleLabel.AutoEllipsis = !sideCollapsed;
                _toggleBtn.Visible = true;

                if (sideCollapsed)
                {
                    if (_savedSideDock == DockStyle.None)
                        _savedSideDock = sideDock;

                    DetachContentPanel();

                    if (_belowHeaderToolbar != null)
                        _belowHeaderToolbar.Visible = false;

                    // Collapsed chip is pinned to the screen edge — keep chevron on the inner side
                    // so the map/workspace cannot paint over it.
                    bool toggleLeading = _savedSideDock == DockStyle.Right;
                    LayoutHeaderChrome(toggleLeading);

                    _header.Dock = DockStyle.Fill;
                    _header.Visible = true;

                    int railW = ResolveCollapsedRailWidth(toggleLeading);
                    CollapsedRailWidth = railW;

                    Dock = DockStyle.None;
                    Anchor = _savedSideDock == DockStyle.Right
                        ? AnchorStyles.Top | AnchorStyles.Right
                        : AnchorStyles.Top | AnchorStyles.Left;
                    Width = railW;
                    Height = CollapsedSideHeaderHeight;
                    MinimumSize = new Size(railW, CollapsedSideHeaderHeight);
                    MaximumSize = new Size(railW, CollapsedSideHeaderHeight);
                    _toggleBtn.Text = _savedSideDock == DockStyle.Left ? "▶" : "◀";
                }
                else if (verticalCollapsed)
                {
                    DetachContentPanel();
                    _header.Dock = DockStyle.Top;
                    _header.Visible = true;

                    if (Dock == DockStyle.Bottom || Dock == DockStyle.Top)
                        Height = CollapsedThickness;
                    _toggleBtn.Text = "▲";
                }
                else
                {
                    AttachContentPanel();
                    LayoutHeaderChrome(toggleLeading: false);
                    MinimumSize = Size.Empty;
                    MaximumSize = Size.Empty;
                    _header.Dock = DockStyle.Top;
                    _header.Height = HeaderHeight;
                    _header.Visible = true;
                    if (_belowHeaderToolbar != null)
                        _belowHeaderToolbar.Visible = true;

                    if (_savedSideDock != DockStyle.None && Dock == DockStyle.None)
                    {
                        Anchor = AnchorStyles.None;
                        Dock = _savedSideDock;
                    }

                    if (Dock == DockStyle.Left || Dock == DockStyle.Right)
                    {
                        Width = ResolveExpandedWidth();
                        _toggleBtn.Text = Dock == DockStyle.Left ? "◀" : "▶";
                    }
                    else if (Dock == DockStyle.Bottom || Dock == DockStyle.Top)
                    {
                        int h = _expandedHeight;
                        if (MinExpandedHeight > 0 && h < MinExpandedHeight) h = MinExpandedHeight;
                        Height = h;
                        _toggleBtn.Text = "▼";
                    }
                    else if (Dock == DockStyle.Fill)
                    {
                        _toggleBtn.Text = "▼";
                    }

                    ResetContentLayout();
                }

                if (Parent != null)
                {
                    SupeyCollapsibleSideLayout.EnsureWired(Parent);
                    if (sideCollapsed)
                        SupeyCollapsibleSideLayout.RelayoutCollapsed(Parent);
                    Parent.PerformLayout();
                }
            }
            finally
            {
                _applyingExpandedState = false;
                if (!_expanded && EffectiveSideDock != DockStyle.None)
                    RefreshCollapsedHeaderLayout();
            }
        }

        private int ResolveCollapsedRailWidth(bool toggleLeading)
        {
            int titleW = TextRenderer.MeasureText(_titleLabel.Text ?? "", _titleLabel.Font).Width + 10;
            int w = CollapsedAccentWidth + CollapsedToggleWidth + titleW;
            if (!toggleLeading)
                w = CollapsedAccentWidth + titleW + CollapsedToggleWidth;
            return Math.Max(CollapsedMinRailWidth, w);
        }

        private void LayoutHeaderChrome(bool toggleLeading)
        {
            _headerLayout.SuspendLayout();
            _headerLayout.Controls.Clear();
            _headerLayout.ColumnStyles.Clear();
            _headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, CollapsedAccentWidth));
            if (toggleLeading)
            {
                _headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, CollapsedToggleWidth));
                _headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                _titleLabel.Padding = new Padding(4, 0, 6, 0);
            }
            else
            {
                _headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
                _headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, CollapsedToggleWidth));
                _titleLabel.Padding = new Padding(8, 0, 0, 0);
            }
            if (toggleLeading)
            {
                _headerLayout.Controls.Add(_accentStripe, 0, 0);
                _headerLayout.Controls.Add(_toggleBtn, 1, 0);
                _headerLayout.Controls.Add(_titleLabel, 2, 0);
            }
            else
            {
                _headerLayout.Controls.Add(_accentStripe, 0, 0);
                _headerLayout.Controls.Add(_titleLabel, 1, 0);
                _headerLayout.Controls.Add(_toggleBtn, 2, 0);
            }
            _headerLayout.RowStyles.Clear();
            _headerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            _headerLayout.ResumeLayout(true);
            _toggleBtn.Invalidate();
        }

        private void DetachContentPanel()
        {
            ContentPanel.Visible = false;
            if (Controls.Contains(ContentPanel))
                Controls.Remove(ContentPanel);
            _contentPanelDetached = true;
        }

        private void AttachContentPanel()
        {
            ContentPanel.Dock = DockStyle.Fill;
            ContentPanel.Visible = true;
            if (!Controls.Contains(ContentPanel))
            {
                Controls.Add(ContentPanel);
                Controls.SetChildIndex(ContentPanel, 0);
            }
            if (!Controls.Contains(_header))
            {
                Controls.Add(_header);
            }
            Controls.SetChildIndex(_header, Controls.Count - 1);
            _contentPanelDetached = false;
        }

        private void ResetContentLayout()
        {
            ContentPanel.SuspendLayout();
            try
            {
                if (ContentPanel.AutoScroll)
                    ContentPanel.AutoScrollPosition = new Point(0, 0);
                ContentPanel.PerformLayout();
            }
            finally
            {
                ContentPanel.ResumeLayout(true);
            }
        }

        private void RefreshCollapsedHeaderLayout()
        {
            if (_expanded) return;
            _toggleBtn.Visible = true;
            _headerLayout.PerformLayout();
            _toggleBtn.Invalidate();
            Invalidate(true);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (_applyingExpandedState || !_expanded) return;
            if (Dock == DockStyle.Left || Dock == DockStyle.Right)
            {
                if (Width >= MinExpandedWidth) ExpandedWidth = Width;
            }
            else if (Dock == DockStyle.Top || Dock == DockStyle.Bottom)
            {
                if (Height >= MinExpandedHeight) ExpandedHeight = Height;
            }
        }

        protected override void OnDockChanged(EventArgs e)
        {
            base.OnDockChanged(e);
            ApplyExpandedState();
        }

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            if (Parent != null)
            {
                SupeyCollapsibleSideLayout.EnsureWired(Parent);
                ApplyExpandedState();
            }
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible && Parent != null && !_expanded && EffectiveSideDock != DockStyle.None)
                RefreshCollapsedHeaderLayout();
        }

        public static Splitter CreateDockSplitter(DockStyle dock, SupeyCollapsiblePanel target, int minExtra = 280, Control layoutRoot = null)
        {
            bool expanded = target?.Expanded ?? true;
            var s = new Splitter
            {
                Dock = dock,
                Width = 4,
                Height = 4,
                BackColor = SupeyTheme.Divider,
                MinSize = target?.MinExpandedWidth > 0 ? target.MinExpandedWidth : 180,
                MinExtra = minExtra,
                Cursor = (dock == DockStyle.Left || dock == DockStyle.Right) ? Cursors.VSplit : Cursors.HSplit,
                Visible = expanded,
            };
            s.MouseEnter += (sender, e) => { s.BackColor = SupeyTheme.BorderSubtle; };
            s.MouseLeave += (sender, e) => { s.BackColor = SupeyTheme.Divider; };
            if (target != null)
            {
                s.ParentChanged += (sender, e) =>
                {
                    if (s.Parent != null)
                        SetSplitterExpanded(s, dock, target, layoutRoot, target.Expanded);
                };
                target.ExpandedChanged += (sender, e) =>
                {
                    if (!target.Visible || target.IsDisposed) return;
                    if (!target.Expanded)
                    {
                        SetSplitterExpanded(s, dock, target, layoutRoot, false);
                        return;
                    }

                    target.BeginInvoke(new Action(() =>
                    {
                        if (target.IsDisposed) return;
                        SetSplitterExpanded(s, dock, target, layoutRoot, true);
                    }));
                };
                SupeyListViewHelpers.WireSplitterSmoothResize(s, layoutRoot ?? target.Parent);
                s.SplitterMoved += (sender, e) => PersistWidthFromSplitter(s, target, dock);
            }
            return s;
        }

        /// <summary>WinForms splitters must stay docked L/R/T/B — hide by removing from the host when collapsed.</summary>
        private static void SetSplitterExpanded(Splitter splitter, DockStyle dock, SupeyCollapsiblePanel target, Control layoutRoot, bool expanded)
        {
            Control host = splitter.Parent ?? layoutRoot ?? target.Parent;
            if (host == null) return;

            if (expanded)
            {
                splitter.Dock = dock;
                splitter.Visible = true;
                if (!host.Controls.Contains(splitter))
                {
                    int insertAt = host.Controls.GetChildIndex(target);
                    host.Controls.Add(splitter);
                    host.Controls.SetChildIndex(splitter, Math.Max(0, insertAt));
                }
            }
            else
            {
                splitter.Visible = false;
                if (host.Controls.Contains(splitter))
                    host.Controls.Remove(splitter);
            }

            host.PerformLayout();
        }

        private static void PersistWidthFromSplitter(Splitter splitter, SupeyCollapsiblePanel target, DockStyle dock)
        {
            if ((Control.MouseButtons & MouseButtons.Left) != 0) return;
            if (target?.Parent == null || !target.Visible || !target.Expanded) return;

            int size = target.Width;
            if (dock == DockStyle.Right || dock == DockStyle.Left)
            {
                if (target._minExpandedWidth > 0 && size < target._minExpandedWidth)
                    size = target._minExpandedWidth;
                if (target._maxExpandedWidth > 0 && size > target._maxExpandedWidth)
                    size = target._maxExpandedWidth;
                target._expandedWidth = size;
                if (target.Width != size)
                {
                    target._applyingExpandedState = true;
                    try { target.Width = size; }
                    finally { target._applyingExpandedState = false; }
                }
            }
            else if (dock == DockStyle.Bottom || dock == DockStyle.Top)
            {
                size = target.Height;
                if (target.MinExpandedHeight > 0 && size < target.MinExpandedHeight)
                    size = target.MinExpandedHeight;
                target._expandedHeight = size;
            }
        }
    }
}
