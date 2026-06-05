using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
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

        private readonly Panel _header;
        private readonly Panel _accentStripe;
        private readonly Label _titleLabel;
        private readonly Label _toggleBtn;
        private readonly Panel _bottomDivider;
        private bool _expanded = true;
        private bool _applyingExpandedState;

        public Panel ContentPanel { get; }

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
                ApplyExpandedState();
                ExpandedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private int _expandedWidth = 280;
        private int _expandedHeight = 220;
        private int _minExpandedWidth = 180;
        private int _maxExpandedWidth = 600;

        /// <summary>Preferred width when expanded (Left/Right dock). Applied on dock and when this
        /// value changes after <see cref="Dock"/> is already set — object initializers often set
        /// <c>Dock</c> before width, which would otherwise leave the panel at the 280px default.</summary>
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

        /// <summary>
        /// Width when collapsed on Left/Right dock. Must exceed accent + chevron (35px) so the
        /// title can render vertically on the rail — <see cref="CollapsedThickness"/> alone is
        /// only tall enough for a top header row, not a readable side label.
        /// </summary>
        public int CollapsedRailWidth { get; set; } = 72;

        /// <summary>Lower bound for splitter-driven resizes when docked Left/Right. The splitter's
        /// MinExtra also enforces this from the other side, but we double-up here so direct
        /// programmatic Width assignments can't accidentally squish the panel either.</summary>
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

        /// <summary>Upper bound (in pixels) so users can't drag a single side panel to consume
        /// more than its share of the workspace. 0 disables the cap.</summary>
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

        /// <summary>Re-applies expanded/collapsed size from <see cref="ExpandedWidth"/> /
        /// <see cref="MinExpandedWidth"/> after the panel is parented or layout properties change.</summary>
        public void ApplyExpandedLayout() => ApplyExpandedState();

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
            _header.Click += (s, e) => Expanded = !Expanded;

            // Thin accent stripe on the left of the header — the only color note in the
            // chrome, immediately marking each panel without resorting to colored
            // backgrounds. 3px wide, full header height.
            _accentStripe = new Panel
            {
                Dock = DockStyle.Left,
                Width = 3,
                BackColor = SupeyTheme.AccentStripe,
            };

            _titleLabel = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = SupeyTheme.TextPrimary,
                BackColor = SupeyTheme.SurfaceHeader,
                Font = SupeyTheme.HeaderFont,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 0, 0, 0),
                Text = "Section",
                Cursor = Cursors.Hand,
            };
            _titleLabel.Click += (s, e) => Expanded = !Expanded;

            // Chevron rendered as a label so it inherits the same font color tint and
            // doesn't give us the 3D button border that the previous version had.
            _toggleBtn = new Label
            {
                Dock = DockStyle.Right,
                Width = 32,
                ForeColor = SupeyTheme.TextSecondary,
                BackColor = SupeyTheme.SurfaceHeader,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                Text = "◀",
                Cursor = Cursors.Hand,
            };
            _toggleBtn.Click += (s, e) => Expanded = !Expanded;

            // 1px bottom divider on the header for crisp separation from the content. The
            // ContentPanel's BackColor differs from the header's so the line is subtle but
            // unambiguous.
            _bottomDivider = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 1,
                BackColor = SupeyTheme.Divider,
            };

            // Order matters: dock last-added closest to the edge, so add the right-most
            // (toggle) first, the divider second, then the title (Fill) consumes the
            // remainder. Accent stripe is its own dock so it sits left of everything.
            _header.Controls.Add(_titleLabel);
            _header.Controls.Add(_toggleBtn);
            _header.Controls.Add(_bottomDivider);
            _header.Controls.Add(_accentStripe);

            ContentPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.Surface,
                Padding = new Padding(8),
                AutoScroll = true,
            };

            Controls.Add(ContentPanel);
            Controls.Add(_header);

            EnableDoubleBuffered(_header);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            Click += OnRailClick;
        }

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

        private void OnRailClick(object sender, EventArgs e)
        {
            if (!_expanded && (IsHorizontalDock() || Dock == DockStyle.Fill))
                Expanded = true;
        }

        public string Title
        {
            get => _titleLabel.Text;
            set
            {
                _titleLabel.Text = value ?? "";
                if (!_expanded && IsHorizontalDock())
                    Invalidate();
            }
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

        private void ApplyExpandedState()
        {
            _applyingExpandedState = true;
            try
            {
                ContentPanel.Visible = _expanded;
                bool sideRail = IsHorizontalDock() && !_expanded;
                // Fill-docked trips (split panel) and Top/Bottom keep the header strip when collapsed.
                _header.Visible = _expanded || IsVerticalDock() || Dock == DockStyle.Fill;
                Cursor = sideRail ? Cursors.Hand : Cursors.Default;

                if (Dock == DockStyle.Left || Dock == DockStyle.Right)
                {
                    Width = _expanded ? ResolveExpandedWidth() : CollapsedRailWidth;
                    _toggleBtn.Text = Dock == DockStyle.Left
                        ? (_expanded ? "◀" : "▶")
                        : (_expanded ? "▶" : "◀");
                }
                else if (Dock == DockStyle.Bottom || Dock == DockStyle.Top)
                {
                    int h = _expandedHeight;
                    if (MinExpandedHeight > 0 && h < MinExpandedHeight) h = MinExpandedHeight;
                    Height = _expanded ? h : CollapsedThickness;
                    _toggleBtn.Text = _expanded ? "▼" : "▲";
                }
                else if (Dock == DockStyle.Fill)
                {
                    ContentPanel.Visible = _expanded;
                    _toggleBtn.Text = _expanded ? "▼" : "▲";
                }

                Invalidate();
            }
            finally { _applyingExpandedState = false; }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (!_expanded && IsHorizontalDock())
            {
                using (var bg = new SolidBrush(SupeyTheme.SurfaceHeader))
                    e.Graphics.FillRectangle(bg, ClientRectangle);
                return;
            }
            base.OnPaintBackground(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (_expanded || !IsHorizontalDock())
            {
                base.OnPaint(e);
                return;
            }

            // Parent split drags often invalidate only a strip; repaint the whole rail so
            // vertical title glyphs from the previous height are not left behind.
            var state = e.Graphics.Save();
            try
            {
                e.Graphics.SetClip(ClientRectangle);
                PaintCollapsedSideRail(e.Graphics);
            }
            finally
            {
                e.Graphics.Restore(state);
            }
        }

        private void PaintCollapsedSideRail(Graphics g)
        {
            if (g == null) return;

            var bounds = ClientRectangle;
            if (bounds.Width <= 0 || bounds.Height <= 0) return;

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            using (var bg = new SolidBrush(SupeyTheme.SurfaceHeader))
                g.FillRectangle(bg, bounds);

            using (var accent = new SolidBrush(SupeyTheme.AccentStripe))
                g.FillRectangle(accent, bounds.Left, bounds.Top, 3, bounds.Height);

            string chevron = Dock == DockStyle.Left ? "▶" : "◀";
            var chevronRect = new Rectangle(bounds.Left + 3, bounds.Top + 4, bounds.Width - 3, HeaderHeight);
            using (var chevronFont = new Font("Segoe UI", 10f, FontStyle.Bold))
            {
                TextRenderer.DrawText(g, chevron, chevronFont, chevronRect, SupeyTheme.TextSecondary,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            string title = (Title ?? "").Trim();
            if (title.Length == 0) return;

            var titleRect = new Rectangle(
                bounds.Left + 8,
                bounds.Top + HeaderHeight + 4,
                bounds.Width - 10,
                Math.Max(0, bounds.Height - HeaderHeight - 8));

            using (var textBrush = new SolidBrush(SupeyTheme.TextPrimary))
            using (var sf = new StringFormat(StringFormatFlags.DirectionVertical | StringFormatFlags.NoWrap)
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
            })
            {
                g.DrawString(title, SupeyTheme.HeaderFont, textBrush, titleRect, sf);
            }
        }

        /// <summary>
        /// When the panel is expanded and the user drags the adjacent <see cref="Splitter"/>,
        /// our Width changes underneath us. Treat that change as a new preferred ExpandedWidth
        /// so toggling collapse/expand restores the user's choice instead of reverting to the
        /// default.
        /// </summary>
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (_applyingExpandedState) return;

            if (!_expanded && IsHorizontalDock())
            {
                Invalidate(true);
                return;
            }

            if (_header.Visible)
                _header.Invalidate(true);

            if (!_expanded) return;
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
            if (Parent != null && _expanded)
                ApplyExpandedState();
        }

        /// <summary>
        /// Draggable resize bar for a docked <see cref="SupeyCollapsiblePanel"/>. Hidden while
        /// collapsed; clamps width/height to the panel's min/max expanded bounds.
        /// </summary>
        public static Splitter CreateDockSplitter(DockStyle dock, SupeyCollapsiblePanel target, int minExtra = 280)
        {
            var s = new Splitter
            {
                Dock = dock,
                Width = 4,
                Height = 4,
                BackColor = SupeyTheme.Divider,
                MinSize = target?.MinExpandedWidth > 0 ? target.MinExpandedWidth : 180,
                MinExtra = minExtra,
                Cursor = (dock == DockStyle.Left || dock == DockStyle.Right) ? Cursors.VSplit : Cursors.HSplit,
                Visible = target?.Expanded ?? true,
            };
            s.MouseEnter += (sender, e) => { s.BackColor = SupeyTheme.BorderSubtle; };
            s.MouseLeave += (sender, e) => { s.BackColor = SupeyTheme.Divider; };
            if (target != null)
            {
                target.ExpandedChanged += (sender, e) =>
                {
                    if (target.Visible)
                        s.Visible = target.Expanded;
                };
                SplitterEventHandler onResize = (sender, e) => ApplyDockSplitterResize(s, target, dock);
                s.SplitterMoving += onResize;
                s.SplitterMoved += onResize;
            }
            return s;
        }

        /// <summary>Width/height from splitter drag — avoids fighting WinForms split-target when siblings hide/show.</summary>
        internal void ApplySplitterResize(int widthOrHeight)
        {
            int size = widthOrHeight;
            if (Dock == DockStyle.Left || Dock == DockStyle.Right)
            {
                if (_minExpandedWidth > 0 && size < _minExpandedWidth) size = _minExpandedWidth;
                if (_maxExpandedWidth > 0 && size > _maxExpandedWidth) size = _maxExpandedWidth;
                if (!_expanded || Width == size && _expandedWidth == size) return;
                _applyingExpandedState = true;
                try
                {
                    _expandedWidth = size;
                    Width = size;
                }
                finally { _applyingExpandedState = false; }
            }
            else if (Dock == DockStyle.Top || Dock == DockStyle.Bottom)
            {
                if (MinExpandedHeight > 0 && size < MinExpandedHeight) size = MinExpandedHeight;
                if (!_expanded || Height == size && _expandedHeight == size) return;
                _applyingExpandedState = true;
                try
                {
                    _expandedHeight = size;
                    Height = size;
                }
                finally { _applyingExpandedState = false; }
            }
        }

        private static void ApplyDockSplitterResize(Splitter splitter, SupeyCollapsiblePanel target, DockStyle dock)
        {
            if (target?.Parent == null || !target.Visible || !target.Expanded) return;
            var parent = target.Parent;
            if (dock == DockStyle.Right)
            {
                int outer = SumOtherDockedWidth(parent, target, splitter, DockStyle.Right);
                target.ApplySplitterResize(Math.Max(0, parent.ClientSize.Width - splitter.Left - outer));
            }
            else if (dock == DockStyle.Left)
            {
                int outer = SumOtherDockedWidth(parent, target, splitter, DockStyle.Left);
                target.ApplySplitterResize(Math.Max(0, splitter.Right - outer));
            }
            else if (dock == DockStyle.Bottom)
            {
                int outer = SumOtherDockedWidth(parent, target, splitter, DockStyle.Bottom);
                target.ApplySplitterResize(Math.Max(0, parent.ClientSize.Height - splitter.Top - outer));
            }
            else if (dock == DockStyle.Top)
            {
                int outer = SumOtherDockedWidth(parent, target, splitter, DockStyle.Top);
                target.ApplySplitterResize(Math.Max(0, splitter.Bottom - outer));
            }
        }

        private static int SumOtherDockedWidth(Control parent, SupeyCollapsiblePanel target, Splitter splitter, DockStyle dock)
        {
            int sum = 0;
            foreach (Control c in parent.Controls)
            {
                if (ReferenceEquals(c, target) || ReferenceEquals(c, splitter) || !c.Visible || c.Dock != dock)
                    continue;
                sum += dock == DockStyle.Top || dock == DockStyle.Bottom ? c.Height : c.Width;
            }
            return sum;
        }
    }
}
