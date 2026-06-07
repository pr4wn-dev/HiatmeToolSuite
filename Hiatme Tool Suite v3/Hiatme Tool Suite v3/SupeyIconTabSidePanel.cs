using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// One collapsible right-side panel with an icon tab strip and stacked page hosts
    /// (Rules, Drivers, Group key, Settings on Schedule Builder).
    /// </summary>
    internal sealed class SupeyIconTabSidePanel
    {
        private const int IconStripHeight = 44;
        private const int IconButtonSize = 34;

        private readonly FlowLayoutPanel _iconFlow;
        private readonly Panel _iconStrip;
        private readonly Panel _bodyHost;
        private readonly List<PageSlot> _pages = new List<PageSlot>();
        private int _selectedIndex;

        public SupeyCollapsiblePanel Panel { get; }

        public int SelectedIndex => _selectedIndex;

        public event EventHandler SelectedIndexChanged;

        private sealed class PageSlot
        {
            public Panel Host;
            public SupeyIconTabButton Tab;
            public string Title;
            public bool Enabled = true;
            public int RecommendedWidth;
        }

        public SupeyIconTabSidePanel()
        {
            Panel = new SupeyCollapsiblePanel
            {
                Title = "Options",
                Dock = DockStyle.Right,
                ExpandedWidth = 430,
                MinExpandedWidth = 280,
                MaxExpandedWidth = 720,
                Expanded = true,
            };

            Panel.ExpandedChanged += (_, __) => OnPanelExpandedChanged();

            Panel content = Panel.ContentPanel;
            content.Padding = Padding.Empty;
            content.BackColor = SupeyTheme.Surface;
            content.AutoScroll = false;

            _iconStrip = new Panel
            {
                Dock = DockStyle.Top,
                Height = IconStripHeight,
                BackColor = SupeyTheme.SurfaceHeader,
                Padding = new Padding(8, 6, 8, 0),
            };

            _iconFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = SupeyTheme.SurfaceHeader,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };

            var stripDivider = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 1,
                BackColor = SupeyTheme.Divider,
            };

            _iconStrip.Controls.Add(_iconFlow);
            _iconStrip.Controls.Add(stripDivider);

            _bodyHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.Surface,
            };

            content.Controls.Add(_bodyHost);
            content.Controls.Add(_iconStrip);
        }

        private void OnPanelExpandedChanged()
        {
            if (!Panel.Expanded)
                return;
            if (Panel.IsHandleCreated)
                Panel.BeginInvoke(new Action(RefreshExpandedContent));
            else
                RefreshExpandedContent();
        }

        private void RefreshExpandedContent()
        {
            if (Panel.IsDisposed || !Panel.Expanded)
                return;

            Panel content = Panel.ContentPanel;
            _iconStrip.Visible = true;
            _iconStrip.Dock = DockStyle.Top;
            _iconStrip.Height = IconStripHeight;

            if (!content.Controls.Contains(_iconStrip))
            {
                content.Controls.Add(_iconStrip);
                content.Controls.SetChildIndex(_iconStrip, content.Controls.Count - 1);
            }
            if (!content.Controls.Contains(_bodyHost))
                content.Controls.Add(_bodyHost);

            content.PerformLayout();
            _iconStrip.Invalidate(true);
            _iconFlow.Invalidate(true);
        }

        public void FinalizePages(int defaultPageIndex = 0)
        {
            FinalizeLayoutWidths();
            SelectPage(defaultPageIndex);
            Panel.ApplyExpandedLayout();
            if (Panel.Expanded)
                RefreshExpandedContent();
        }

        public Panel AddPage(string title, string iconGlyph, string tooltip, int recommendedExpandedWidth = 0)
        {
            int index = _pages.Count;
            var host = new Panel
            {
                Dock = DockStyle.Fill,
                Visible = index == 0,
                BackColor = SupeyTheme.Surface,
            };
            _bodyHost.Controls.Add(host);

            var tab = new SupeyIconTabButton
            {
                Glyph = iconGlyph,
                TooltipText = tooltip,
                Selected = index == 0,
                Margin = new Padding(0, 0, 6, 0),
            };
            int captured = index;
            tab.Click += (s, e) =>
            {
                if (_pages[captured].Enabled)
                    SelectPage(captured);
            };

            _iconFlow.Controls.Add(tab);
            _pages.Add(new PageSlot
            {
                Host = host,
                Tab = tab,
                Title = title ?? "Page",
                RecommendedWidth = Math.Max(0, recommendedExpandedWidth),
            });

            return host;
        }

        /// <summary>After all pages are registered, size the panel for the widest page.</summary>
        public void FinalizeLayoutWidths()
        {
            int max = 0;
            foreach (PageSlot page in _pages)
                if (page.RecommendedWidth > max)
                    max = page.RecommendedWidth;
            if (max <= 0) return;

            Panel.ExpandedWidth = Math.Max(Panel.ExpandedWidth, max);
            Panel.MaxExpandedWidth = Math.Max(Panel.MaxExpandedWidth, max + 240);
        }

        public void SetPageRecommendedWidth(int index, int recommendedExpandedWidth)
        {
            if (index < 0 || index >= _pages.Count) return;
            _pages[index].RecommendedWidth = Math.Max(0, recommendedExpandedWidth);
            if (_pages[index].RecommendedWidth > Panel.MaxExpandedWidth)
                Panel.MaxExpandedWidth = _pages[index].RecommendedWidth + 120;
            if (_selectedIndex == index)
                ApplyWidthForPage(index);
            else
                FinalizeLayoutWidths();
        }

        private void ApplyWidthForPage(int index)
        {
            if (index < 0 || index >= _pages.Count) return;
            int needed = _pages[index].RecommendedWidth;
            if (needed <= 0) return;

            if (needed > Panel.MaxExpandedWidth)
                Panel.MaxExpandedWidth = needed + 120;

            if (!Panel.Expanded) return;

            int target = Math.Max(Panel.Width, needed);
            if (target > Panel.MaxExpandedWidth)
                target = Panel.MaxExpandedWidth;
            if (target > Panel.ExpandedWidth)
                Panel.ExpandedWidth = target;
        }

        public void SelectPage(int index)
        {
            if (index < 0 || index >= _pages.Count) return;
            if (!_pages[index].Enabled) return;
            if (_selectedIndex == index && _pages[index].Host.Visible) return;

            _selectedIndex = index;
            for (int i = 0; i < _pages.Count; i++)
            {
                _pages[i].Host.Visible = i == index;
                _pages[i].Tab.Selected = i == index;
            }

            Panel.Title = "Options";
            ApplyWidthForPage(index);
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SetPageEnabled(int index, bool enabled)
        {
            if (index < 0 || index >= _pages.Count) return;
            _pages[index].Enabled = enabled;
            _pages[index].Tab.Enabled = enabled;
            if (!enabled && _selectedIndex == index)
            {
                for (int i = 0; i < _pages.Count; i++)
                {
                    if (_pages[i].Enabled)
                    {
                        SelectPage(i);
                        return;
                    }
                }
            }
        }
    }

    /// <summary>Small square icon tab for <see cref="SupeyIconTabSidePanel"/>.</summary>
    internal sealed class SupeyIconTabButton : Control
    {
        private bool _selected;
        private bool _hover;

        public string Glyph { get; set; } = "•";
        public string TooltipText { get; set; }

        public bool Selected
        {
            get => _selected;
            set
            {
                if (_selected == value) return;
                _selected = value;
                Invalidate();
            }
        }

        public SupeyIconTabButton()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw,
                true);
            Size = new Size(34, 34);
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI Emoji", 13f);
            DoubleBuffered = true;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            if (!string.IsNullOrEmpty(TooltipText))
                ToolTipHelper.Show(this, TooltipText);
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var rect = new Rectangle(1, 1, Width - 3, Height - 3);

            Color back = SupeyTheme.SurfaceHeader;
            if (!Enabled)
                back = SupeyTheme.SurfaceHeader;
            else if (_selected)
                back = SupeyTheme.Surface;
            else if (_hover)
                back = Color.FromArgb(48, SupeyTheme.Surface.R, SupeyTheme.Surface.G, SupeyTheme.Surface.B);

            using (var brush = new SolidBrush(back))
                g.FillRectangle(brush, rect);

            if (_selected)
            {
                using (var pen = new Pen(SupeyTheme.AccentStripe, 2f))
                    g.DrawLine(pen, 2, Height - 2, Width - 3, Height - 2);
            }
            else if (_hover && Enabled)
            {
                using (var pen = new Pen(SupeyTheme.BorderSubtle))
                    g.DrawRectangle(pen, rect);
            }

            Color textColor = Enabled
                ? (_selected ? SupeyTheme.TextPrimary : SupeyTheme.TextSecondary)
                : SupeyTheme.TextMuted;
            var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter;
            TextRenderer.DrawText(g, Glyph ?? "", Font, ClientRectangle, textColor, flags);
        }
    }

    /// <summary>Single shared tooltip for icon tabs (avoids one ToolTip per button).</summary>
    internal static class ToolTipHelper
    {
        private static ToolTip _tip;

        public static void Show(Control owner, string text)
        {
            if (owner == null || string.IsNullOrEmpty(text)) return;
            if (_tip == null)
            {
                _tip = SupeyToolTip.Create(autoPopDelay: 4000, initialDelay: 400);
            }
            _tip.SetToolTip(owner, text);
        }
    }
}
