using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Owner-painted flat button used across the Supey tab. Replaces MaterialButton in
    /// the spots where its washed-out disabled rendering, opaque MaterialSkin background,
    /// and text-clipping behavior in narrow cells were causing real visual problems.
    /// </summary>
    /// <remarks>
    /// Every state (normal / hover / pressed / disabled) is painted from
    /// <see cref="SupeyTheme"/> palette colors so the button always agrees with whatever
    /// surface it's docked on — no more "this MaterialButton is disabled so it's white,
    /// but the panel underneath is dark gray, so it looks like a hole in the toolbar."
    /// </remarks>
    internal sealed class SupeyButton : Control
    {
        public enum Variant
        {
            /// <summary>Solid accent fill — primary call to action (one per panel).</summary>
            Primary,
            /// <summary>Subtle elevated fill — neutral default for most actions.</summary>
            Secondary,
            /// <summary>Outlined / hollow — destructive or de-emphasized actions.</summary>
            Outlined,
            /// <summary>Text-only / no border — for tertiary/inline actions like ⚙.</summary>
            Ghost,
        }

        private Variant _variant = Variant.Secondary;
        private bool _hover;
        private bool _pressed;
        private int _cornerRadius = 4;

        public SupeyButton()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.UserPaint
                | ControlStyles.SupportsTransparentBackColor,
                true);
            BackColor = Color.Transparent;
            ForeColor = SupeyTheme.TextPrimary;
            Font = new Font("Segoe UI Semibold", 9.25f);
            Cursor = Cursors.Hand;
            Size = new Size(96, 30);
            DoubleBuffered = true;
        }

        public Variant Kind
        {
            get => _variant;
            set { _variant = value; Invalidate(); }
        }

        public int CornerRadius
        {
            get => _cornerRadius;
            set { _cornerRadius = Math.Max(0, value); Invalidate(); }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            _pressed = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _pressed = true;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _pressed = false;
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            // Resolve fill / border / text colors based on state and variant. Disabled
            // takes priority so a disabled button always reads as "I'm not clickable"
            // regardless of which Variant the caller picked.
            Color fill, border, text;
            ResolveColors(out fill, out border, out text);

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = RoundedRect(rect, _cornerRadius))
            {
                if (fill != Color.Empty)
                {
                    using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                }
                if (border != Color.Empty)
                {
                    using (var p = new Pen(border, 1)) g.DrawPath(p, path);
                }
            }

            TextRenderer.DrawText(g, Text ?? "", Font, ClientRectangle, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }

        private void ResolveColors(out Color fill, out Color border, out Color text)
        {
            // Disabled: always the same washed look regardless of variant.
            if (!Enabled)
            {
                fill = SupeyTheme.SurfaceElevated;
                border = SupeyTheme.BorderSubtle;
                text = SupeyTheme.TextMuted;
                return;
            }

            switch (_variant)
            {
                case Variant.Primary:
                    {
                        var baseFill = SupeyTheme.AccentPrimary;
                        if (_pressed) baseFill = Darken(baseFill, 0.15f);
                        else if (_hover) baseFill = Lighten(baseFill, 0.10f);
                        fill = baseFill;
                        border = Color.Empty;
                        // Dark text on a green accent reads as confident, primary action.
                        text = Color.FromArgb(20, 28, 12);
                        break;
                    }
                case Variant.Outlined:
                    {
                        fill = _pressed ? SupeyTheme.SurfaceElevated
                             : _hover ? Color.FromArgb(48, 48, 48)
                             : Color.Empty;
                        border = SupeyTheme.BorderSubtle;
                        text = ForeColor;
                        break;
                    }
                case Variant.Ghost:
                    {
                        fill = _pressed ? SupeyTheme.SurfaceElevated
                             : _hover ? Color.FromArgb(48, 48, 48)
                             : Color.Empty;
                        border = Color.Empty;
                        text = ForeColor;
                        break;
                    }
                case Variant.Secondary:
                default:
                    {
                        fill = _pressed ? Color.FromArgb(34, 34, 34)
                             : _hover ? Color.FromArgb(52, 52, 52)
                             : SupeyTheme.SurfaceElevated;
                        border = SupeyTheme.BorderSubtle;
                        text = SupeyTheme.TextPrimary;
                        break;
                    }
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

        private static Color Lighten(Color c, float amount)
        {
            amount = Math.Max(0, Math.Min(1, amount));
            return Color.FromArgb(
                c.A,
                (int)Math.Min(255, c.R + (255 - c.R) * amount),
                (int)Math.Min(255, c.G + (255 - c.G) * amount),
                (int)Math.Min(255, c.B + (255 - c.B) * amount));
        }

        private static Color Darken(Color c, float amount)
        {
            amount = Math.Max(0, Math.Min(1, amount));
            return Color.FromArgb(
                c.A,
                (int)Math.Max(0, c.R * (1 - amount)),
                (int)Math.Max(0, c.G * (1 - amount)),
                (int)Math.Max(0, c.B * (1 - amount)));
        }
    }
}
