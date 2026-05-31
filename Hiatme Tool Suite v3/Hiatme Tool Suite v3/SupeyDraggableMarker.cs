using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using GMap.NET;
using GMap.NET.WindowsForms.Markers;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Map marker the dispatcher can drag (via <see cref="SupeyMapMarkerDrag"/> on the host <c>GMapControl</c>).</summary>
    internal sealed class SupeyDraggableMarker : GMarkerGoogle
    {
        /// <summary>1-based stop index along the group route (PU legs, then DO legs). 0 = no badge.</summary>
        public int RouteStopNumber { get; set; }

        public SupeyDraggableMarker(PointLatLng pos, GMarkerGoogleType type)
            : base(pos, type)
        {
            Offset = new Point(-10, -10);
        }

        public event Action<SupeyDraggableMarker> DragEnded;

        internal void NotifyDragEnded() => DragEnded?.Invoke(this);

        public override void OnRender(Graphics g)
        {
            base.OnRender(g);
            if (RouteStopNumber <= 0 || g == null)
                return;

            string text = RouteStopNumber.ToString();
            const int badge = 16;
            int x = LocalPosition.X + (Size.Width / 2) - (badge / 2);
            int y = LocalPosition.Y - badge - 3;

            var prev = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var fill = new SolidBrush(Color.FromArgb(245, 255, 255, 255)))
            using (var border = new Pen(Color.FromArgb(50, 50, 50), 1.5f))
            using (var font = new Font("Segoe UI", 7.5f, FontStyle.Bold, GraphicsUnit.Point))
            {
                g.FillEllipse(fill, x, y, badge, badge);
                g.DrawEllipse(border, x, y, badge, badge);
                SizeF sz = g.MeasureString(text, font);
                g.DrawString(text, font, Brushes.Black,
                    x + (badge - sz.Width) / 2f,
                    y + (badge - sz.Height) / 2f - 0.5f);
            }
            g.SmoothingMode = prev;
        }
    }
}
