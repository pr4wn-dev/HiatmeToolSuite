using System;
using System.Drawing;
using GMap.NET;
using GMap.NET.WindowsForms.Markers;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Map marker the dispatcher can drag (via <see cref="SupeyMapMarkerDrag"/> on the host <c>GMapControl</c>).</summary>
    internal sealed class SupeyDraggableMarker : GMarkerGoogle
    {
        public SupeyDraggableMarker(PointLatLng pos, GMarkerGoogleType type)
            : base(pos, type)
        {
            Offset = new Point(-10, -10);
        }

        public event Action<SupeyDraggableMarker> DragEnded;

        internal void NotifyDragEnded() => DragEnded?.Invoke(this);
    }
}
