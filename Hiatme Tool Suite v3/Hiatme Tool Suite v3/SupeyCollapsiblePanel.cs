using System;
using System.Drawing;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Dark-themed collapsible panel with a header bar and chevron toggle for the Supey tab.
    /// </summary>
    internal sealed class SupeyCollapsiblePanel : Panel
    {
        private readonly Panel _header;
        private readonly Label _titleLabel;
        private readonly Button _toggleBtn;
        private bool _expanded = true;

        public Panel ContentPanel { get; }
        public bool Expanded
        {
            get => _expanded;
            set { if (_expanded != value) { _expanded = value; ApplyExpandedState(); } }
        }

        public int ExpandedWidth { get; set; } = 280;
        public int ExpandedHeight { get; set; } = 220;
        public int CollapsedThickness { get; set; } = 32;

        public SupeyCollapsiblePanel()
        {
            BackColor = Color.FromArgb(35, 35, 35);
            Padding = new Padding(0);

            _header = new Panel
            {
                Dock = DockStyle.Top,
                Height = CollapsedThickness,
                BackColor = Color.FromArgb(28, 28, 28),
                Padding = new Padding(8, 4, 8, 4),
            };

            _titleLabel = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.Gainsboro,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI Semibold", 9.5f),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "Section",
            };

            _toggleBtn = new Button
            {
                Dock = DockStyle.Right,
                Width = 28,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.Gainsboro,
                Text = "◀",
                TabStop = false,
            };
            _toggleBtn.FlatAppearance.BorderSize = 0;
            _toggleBtn.Click += (s, e) => Expanded = !Expanded;

            _header.Controls.Add(_titleLabel);
            _header.Controls.Add(_toggleBtn);
            _header.Click += (s, e) => Expanded = !Expanded;
            _titleLabel.Click += (s, e) => Expanded = !Expanded;

            ContentPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(35, 35, 35),
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

        protected override void OnDockChanged(EventArgs e)
        {
            base.OnDockChanged(e);
            ApplyExpandedState();
        }
    }
}
