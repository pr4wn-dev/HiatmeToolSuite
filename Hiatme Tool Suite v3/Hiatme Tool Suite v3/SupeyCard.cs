using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Flat, theme-driven surface panel — the Supey replacement for MaterialCard. MaterialCard paints
    /// its own elevation/rounded chrome from MaterialSkin's fixed palette, which fought our black/green
    /// look; SupeyCard instead paints a single themed surface (optionally rounded, optionally with a
    /// 1px border and a thin accent stripe along one edge) and nothing else.
    ///
    /// It reads every color from <see cref="SupeyTheme"/> and repaints automatically when the active
    /// theme changes, so dropping these in (or recoloring existing panels to match) keeps the whole
    /// window consistent across presets.
    /// </summary>
    internal class SupeyCard : Panel
    {
        public enum Surface
        {
            /// <summary>Whole-tab background tone.</summary>
            Base,
            /// <summary>Standard panel surface.</summary>
            Standard,
            /// <summary>Elevated card (status row, prompt box).</summary>
            Elevated,
            /// <summary>Header / toolbar strip tone.</summary>
            Header,
            /// <summary>Status-bar tone (darkest).</summary>
            StatusBar,
        }

        public enum AccentEdge { None, Top, Left }

        private Surface _surface = Surface.Standard;
        private bool _showBorder;
        private int _cornerRadius;
        private AccentEdge _accentEdge = AccentEdge.None;

        public SupeyCard()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.UserPaint,
                true);
            DoubleBuffered = true;
            ForeColor = SupeyTheme.TextPrimary;
            BackColor = ResolveSurface();
            SupeyThemeManager.ThemeChanged += OnThemeChanged;
        }

        /// <summary>Which surface tone from the palette this card paints.</summary>
        public Surface SurfaceLevel
        {
            get => _surface;
            set { _surface = value; BackColor = ResolveSurface(); Invalidate(); }
        }

        /// <summary>Draw a 1px subtle themed border around the card.</summary>
        public bool ShowBorder
        {
            get => _showBorder;
            set { _showBorder = value; Invalidate(); }
        }

        /// <summary>Corner radius in px (0 = square, matches the flat Schedule Builder look).</summary>
        public int CornerRadius
        {
            get => _cornerRadius;
            set { _cornerRadius = Math.Max(0, value); Invalidate(); }
        }

        /// <summary>Optional lime accent stripe along one edge (used for active/section cards).</summary>
        public AccentEdge Accent
        {
            get => _accentEdge;
            set { _accentEdge = value; Invalidate(); }
        }

        /// <summary>Accepted for Designer compatibility (MaterialCard elevation); unused by the flat skin.</summary>
        public int Depth { get; set; }

        /// <summary>Accepted for Designer compatibility (MaterialSkin tracked mouse state); unused.</summary>
        public SupeyMouseState MouseState { get; set; } = SupeyMouseState.OUT;

        private Color ResolveSurface()
        {
            switch (_surface)
            {
                case Surface.Base: return SupeyTheme.SurfaceBase;
                case Surface.Elevated: return SupeyTheme.SurfaceElevated;
                case Surface.Header: return SupeyTheme.SurfaceHeader;
                case Surface.StatusBar: return SupeyTheme.SurfaceStatusBar;
                case Surface.Standard:
                default: return SupeyTheme.Surface;
            }
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            if (IsDisposed) return;
            ForeColor = SupeyTheme.TextPrimary;
            BackColor = ResolveSurface();
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                SupeyThemeManager.ThemeChanged -= OnThemeChanged;
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Color fill = ResolveSurface();

            if (_cornerRadius <= 0)
            {
                using (var b = new SolidBrush(fill))
                    g.FillRectangle(b, ClientRectangle);

                if (_showBorder)
                {
                    using (var p = new Pen(SupeyTheme.BorderSubtle))
                        g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
                }
            }
            else
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var rect = new Rectangle(0, 0, Width - 1, Height - 1);
                using (var path = RoundedRect(rect, _cornerRadius))
                {
                    using (var b = new SolidBrush(fill))
                        g.FillPath(b, path);
                    if (_showBorder)
                    {
                        using (var p = new Pen(SupeyTheme.BorderSubtle))
                            g.DrawPath(p, path);
                    }
                }
                g.SmoothingMode = SmoothingMode.None;
            }

            if (_accentEdge == AccentEdge.Top)
            {
                using (var b = new SolidBrush(SupeyTheme.AccentPrimary))
                    g.FillRectangle(b, 0, 0, Width, 2);
            }
            else if (_accentEdge == AccentEdge.Left)
            {
                using (var b = new SolidBrush(SupeyTheme.AccentPrimary))
                    g.FillRectangle(b, 0, 0, 2, Height);
            }
        }

        private static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            int d = Math.Max(0, radius * 2);
            var path = new GraphicsPath();
            if (d <= 0)
            {
                path.AddRectangle(r);
                return path;
            }
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
