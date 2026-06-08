using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using GMap.NET;
using GMap.NET.WindowsForms.Markers;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Map marker the dispatcher can drag (via <see cref="SupeyMapMarkerDrag"/> on the host <c>GMapControl</c>).</summary>
    internal sealed class SupeyDraggableMarker : GMarkerGoogle
    {
        private static readonly Color BadgeFill = Color.FromArgb(252, 34, 38, 34);
        private static readonly Font BadgeFont = new Font("Segoe UI Semibold", 8f, FontStyle.Bold, GraphicsUnit.Point);

        /// <summary>1-based stop index along the group route (PU legs, then DO legs). 0 = no badge.</summary>
        public int RouteStopNumber { get; set; }

        /// <summary>Text badge (e.g. "home") — used when <see cref="RouteStopNumber"/> is 0.</summary>
        public string BadgeText { get; set; }

        /// <summary>Group route color for the badge ring (defaults to Supey accent).</summary>
        public Color BadgeAccentColor { get; set; } = SupeyTheme.AccentStripe;

        /// <summary>When false, map drag handler ignores this marker (home pin).</summary>
        public bool AllowDrag { get; set; } = true;

        /// <summary>Expanding ripple rings from Schedule Builder / Supey list pick.</summary>
        public bool IsSelectionHighlighted { get; set; }

        /// <summary>0–1 animation phase; advanced by <see cref="SupeyMapWorkspace"/>.</summary>
        public float SelectionPulsePhase { get; set; }

        public SupeyDraggableMarker(PointLatLng pos, GMarkerGoogleType type)
            : base(pos, type)
        {
            Offset = new Point(-10, -10);
        }

        public event Action<SupeyDraggableMarker> DragEnded;

        internal void NotifyDragEnded() => DragEnded?.Invoke(this);

        public override void OnRender(Graphics g)
        {
            if (g == null) return;

            if (IsSelectionHighlighted && IsVisible)
                DrawSelectionRipples(g);

            base.OnRender(g);

            string text = null;
            bool pill = false;
            if (!string.IsNullOrWhiteSpace(BadgeText))
            {
                text = BadgeText.Trim();
                pill = true;
            }
            else if (RouteStopNumber > 0)
            {
                text = RouteStopNumber.ToString();
            }
            else
            {
                return;
            }

            var prevSmooth = g.SmoothingMode;
            var prevHint = g.TextRenderingHint;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Color ring = BadgeAccentColor.A > 0 ? BadgeAccentColor : SupeyTheme.AccentStripe;

            if (pill)
            {
                SizeF sz = g.MeasureString(text, BadgeFont);
                int padX = 6;
                int badgeW = Math.Max(28, (int)Math.Ceiling(sz.Width) + padX * 2);
                int badgeH = 18;
                int x = LocalPosition.X + (Size.Width / 2) - (badgeW / 2);
                int y = LocalPosition.Y - badgeH - 4;
                var rect = new Rectangle(x, y, badgeW, badgeH);
                int radius = badgeH / 2;

                for (int d = 3; d >= 1; d--)
                {
                    int alpha = 40 / d;
                    using (var sh = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0)))
                    {
                        var shadow = new Rectangle(x + d, y + d, badgeW, badgeH);
                        using (var path = RoundedRect(shadow, radius))
                            g.FillPath(sh, path);
                    }
                }

                using (var fill = new SolidBrush(BadgeFill))
                using (var path = RoundedRect(rect, radius))
                    g.FillPath(fill, path);

                using (var border = new Pen(ring, 2f))
                using (var path = RoundedRect(rect, radius))
                    g.DrawPath(border, path);

                using (var textBrush = new SolidBrush(SupeyTheme.AccentPrimary))
                    g.DrawString(text, BadgeFont, textBrush,
                        x + (badgeW - sz.Width) / 2f,
                        y + (badgeH - sz.Height) / 2f - 0.5f);
            }
            else
            {
                int badge = text.Length >= 3 ? 26 : (text.Length >= 2 ? 22 : 18);
                int x = LocalPosition.X + (Size.Width / 2) - (badge / 2);
                int y = LocalPosition.Y - badge - 4;
                var ellipse = new Rectangle(x, y, badge, badge);

                for (int d = 3; d >= 1; d--)
                {
                    int alpha = 40 / d;
                    using (var sh = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0)))
                        g.FillEllipse(sh, new Rectangle(x + d, y + d, badge, badge));
                }

                using (var fill = new SolidBrush(BadgeFill))
                    g.FillEllipse(fill, ellipse);

                using (var border = new Pen(ring, 2f))
                    g.DrawEllipse(border, ellipse);

                SizeF sz = g.MeasureString(text, BadgeFont);
                using (var textBrush = new SolidBrush(SupeyTheme.AccentPrimary))
                    g.DrawString(text, BadgeFont, textBrush,
                        x + (badge - sz.Width) / 2f,
                        y + (badge - sz.Height) / 2f - 0.5f);
            }

            g.SmoothingMode = prevSmooth;
            g.TextRenderingHint = prevHint;
        }

        private void DrawSelectionRipples(Graphics g)
        {
            int cx = LocalPosition.X + Size.Width / 2;
            int cy = LocalPosition.Y + Size.Height / 2;
            Color accent = BadgeAccentColor.A > 0 ? BadgeAccentColor : SupeyTheme.AccentPrimary;

            var prevSmooth = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            for (int ring = 0; ring < 2; ring++)
            {
                float p = (SelectionPulsePhase + ring * 0.5f) % 1f;
                int alpha = (int)(220 * (1f - p));
                if (alpha <= 0) continue;
                int radius = 12 + (int)(20 * p);
                using (var pen = new Pen(Color.FromArgb(alpha, accent), 2.5f))
                    g.DrawEllipse(pen, cx - radius, cy - radius, radius * 2, radius * 2);
            }

            g.SmoothingMode = prevSmooth;
        }

        private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
