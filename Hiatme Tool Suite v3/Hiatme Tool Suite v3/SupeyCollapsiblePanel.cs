using System;
using System.Drawing;
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

        public int ExpandedWidth { get; set; } = 280;
        public int ExpandedHeight { get; set; } = 220;
        public int CollapsedThickness { get; set; } = HeaderHeight;

        /// <summary>Lower bound for splitter-driven resizes when docked Left/Right. The splitter's
        /// MinExtra also enforces this from the other side, but we double-up here so direct
        /// programmatic Width assignments can't accidentally squish the panel either.</summary>
        public int MinExpandedWidth { get; set; } = 180;

        /// <summary>Upper bound (in pixels) so users can't drag a single side panel to consume
        /// more than its share of the workspace. 0 disables the cap.</summary>
        public int MaxExpandedWidth { get; set; } = 600;

        public int MinExpandedHeight { get; set; } = 120;

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
                BackColor = Color.Transparent,
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
                BackColor = Color.Transparent,
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

        private void ApplyExpandedState()
        {
            _applyingExpandedState = true;
            try
            {
                ContentPanel.Visible = _expanded;
                if (Dock == DockStyle.Left || Dock == DockStyle.Right)
                {
                    Width = _expanded ? ExpandedWidth : CollapsedThickness;
                    _toggleBtn.Text = Dock == DockStyle.Left
                        ? (_expanded ? "◀" : "▶")
                        : (_expanded ? "▶" : "◀");
                }
                else if (Dock == DockStyle.Bottom || Dock == DockStyle.Top)
                {
                    Height = _expanded ? ExpandedHeight : CollapsedThickness;
                    _toggleBtn.Text = _expanded ? "▼" : "▲";
                }
                else if (Dock == DockStyle.Fill)
                {
                    ContentPanel.Visible = _expanded;
                    _toggleBtn.Text = _expanded ? "▼" : "▲";
                }
            }
            finally { _applyingExpandedState = false; }
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
    }
}
