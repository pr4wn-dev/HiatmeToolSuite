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
    /// 1px border and an accent treatment when active) and nothing else.
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

        /// <summary>How an active card announces itself.</summary>
        public enum AccentLook
        {
            /// <summary>Flat 3px bar along <see cref="Accent"/>'s edge.</summary>
            Strip,
            /// <summary>Cut corners, accent outline glowing inward, solder dots in the cuts.</summary>
            Hud,
        }

        /// <summary>Depth of the corner cuts, clamped against the card's short side.</summary>
        private const int HudCutMin = 6;
        private const int HudCutMax = 14;
        private const int HudGlowLayers = 4;

        private Surface _surface = Surface.Standard;
        private bool _showBorder;
        private int _cornerRadius;
        private AccentEdge _accentEdge = AccentEdge.None;
        // Strip stays the default: Hud outlines the whole card, which reads as "the container is
        // lit" on big wrapper cards. Opt individual buttons/tiles in instead.
        private AccentLook _accentStyle = AccentLook.Strip;
        private Color? _borderColorOverride;
        private Color? _accentColorOverride;

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

        /// <summary>Marks the card active. Under <see cref="AccentLook.Hud"/> the edge is ignored.</summary>
        public AccentEdge Accent
        {
            get => _accentEdge;
            set { _accentEdge = value; Invalidate(); }
        }

        /// <summary>Which active-state treatment to paint.</summary>
        public AccentLook AccentStyle
        {
            get => _accentStyle;
            set { _accentStyle = value; Invalidate(); }
        }

        /// <summary>When set, paints the border in this color instead of <see cref="SupeyTheme.BorderSubtle"/>.</summary>
        public Color? BorderColorOverride
        {
            get => _borderColorOverride;
            set { _borderColorOverride = value; Invalidate(); }
        }

        /// <summary>When set, paints the accent stripe in this color instead of <see cref="SupeyTheme.AccentPrimary"/>.</summary>
        public Color? AccentColorOverride
        {
            get => _accentColorOverride;
            set { _accentColorOverride = value; Invalidate(); }
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
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            if (rect.Width <= 0 || rect.Height <= 0)
                return;

            Color fill = ResolveSurface();
            Color accent = _accentColorOverride ?? SupeyTheme.AccentPrimary;
            bool active = _accentEdge != AccentEdge.None;
            // The keyed silhouette is permanent so idle and lit buttons share one border; only
            // the lighting changes when the card goes active.
            bool keyed = _accentStyle == AccentLook.Hud;
            bool lit = keyed && active;

            if (!keyed && _cornerRadius <= 0)
            {
                using (var b = new SolidBrush(fill))
                    g.FillRectangle(b, ClientRectangle);
                if (_showBorder)
                {
                    using (var p = new Pen(_borderColorOverride ?? SupeyTheme.BorderSubtle))
                        g.DrawRectangle(p, rect);
                }
                if (active)
                    PaintStrip(g, accent);
                return;
            }

            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Rounds and cuts fall outside the shape, and OnPaintBackground has already flooded
            // those pixels with our own surface tone — repaint them in the parent's tone or the
            // silhouette never reads.
            using (var b = new SolidBrush(AncestorBackColor()))
                g.FillRectangle(b, ClientRectangle);

            int cut = keyed ? HudCut(rect) : 0;
            using (var path = keyed ? KeyedRect(rect, _cornerRadius, cut) : RoundedRect(rect, _cornerRadius))
            {
                using (var b = new SolidBrush(fill))
                    g.FillPath(b, path);

                if (lit)
                    PaintInnerGlow(g, path, accent);

                if (lit || _showBorder)
                {
                    Color line = lit ? accent : (_borderColorOverride ?? SupeyTheme.BorderSubtle);
                    using (var p = new Pen(line, lit ? 1.5f : 1f) { Alignment = PenAlignment.Inset })
                        g.DrawPath(p, path);
                }

                if (keyed)
                {
                    // Solder dots ride the cuts either way — they just wake up when lit.
                    Color dot = lit ? accent : (_borderColorOverride ?? SupeyTheme.BorderSubtle);
                    using (var b = new SolidBrush(dot))
                    {
                        g.FillEllipse(b, rect.Left + cut - 2f, rect.Top, 4f, 4f);
                        g.FillEllipse(b, rect.Right - cut - 2f, rect.Bottom - 4f, 4f, 4f);
                    }
                }
            }

            if (active && !keyed)
                PaintStrip(g, accent);
            g.SmoothingMode = SmoothingMode.None;
        }

        private void PaintStrip(Graphics g, Color accent)
        {
            using (var b = new SolidBrush(accent))
            {
                if (_accentEdge == AccentEdge.Top)
                    g.FillRectangle(b, 0, 0, Width, 3);
                else if (_accentEdge == AccentEdge.Left)
                    g.FillRectangle(b, 0, 0, 3, Height);
            }
        }

        private static int HudCut(Rectangle r)
        {
            int shortSide = Math.Min(r.Width, r.Height);
            return Math.Max(HudCutMin, Math.Min(HudCutMax, shortSide / 5));
        }

        /// <summary>Concentric inset strokes standing in for a real inner glow.</summary>
        private static void PaintInnerGlow(Graphics g, GraphicsPath path, Color accent)
        {
            for (int i = HudGlowLayers; i >= 1; i--)
            {
                using (var p = new Pen(Color.FromArgb(46 * i / HudGlowLayers, accent), 1.4f * i)
                {
                    Alignment = PenAlignment.Inset,
                })
                    g.DrawPath(p, path);
            }
        }

        /// <summary>Rounded rect with the top-left and bottom-right corners sheared off flat.</summary>
        private static GraphicsPath KeyedRect(Rectangle r, int radius, int cut)
        {
            int d = Math.Max(0, radius * 2);
            var path = new GraphicsPath();
            path.AddLine(r.Left + cut, r.Top, r.Right - radius, r.Top);
            if (d > 0)
                path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddLine(r.Right, r.Bottom - cut, r.Right - cut, r.Bottom);
            path.AddLine(r.Right - cut, r.Bottom, r.Left + radius, r.Bottom);
            if (d > 0)
                path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.AddLine(r.Left, r.Top + cut, r.Left + cut, r.Top);
            path.CloseFigure();
            return path;
        }

        /// <summary>Nearest ancestor tone we can actually paint with; transparent parents are skipped.</summary>
        private Color AncestorBackColor()
        {
            for (Control p = Parent; p != null; p = p.Parent)
            {
                if (p.BackColor.A != 0)
                    return p.BackColor;
            }
            return SupeyTheme.SurfaceBase;
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
