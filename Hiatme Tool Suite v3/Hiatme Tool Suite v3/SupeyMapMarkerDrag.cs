using System;
using System.Collections.Generic;
using System.Windows.Forms;
using GMap.NET;
using GMap.NET.WindowsForms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Wires left-drag on <see cref="SupeyDraggableMarker"/> pins (GMap.NET 2.1.7 has no marker OnMouse* overrides).</summary>
    internal sealed class SupeyMapMarkerDrag
    {
        private static readonly object WireGate = new object();
        private static readonly HashSet<GMapControl> Wired = new HashSet<GMapControl>();

        public static void EnsureWired(GMapControl map)
        {
            if (map == null)
                return;
            lock (WireGate)
            {
                if (Wired.Contains(map))
                    return;
                Wired.Add(map);
            }
            new SupeyMapMarkerDrag(map);
        }

        private readonly GMapControl _map;
        private SupeyDraggableMarker _active;
        private bool _restoreCanDragMap;

        private SupeyMapMarkerDrag(GMapControl map)
        {
            _map = map;
            _map.MouseDown += OnMapMouseDown;
            _map.MouseMove += OnMapMouseMove;
            _map.MouseUp += OnMapMouseUp;
        }

        private void OnMapMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;
            var marker = HitDraggableMarker();
            if (marker == null)
                return;
            _active = marker;
            _restoreCanDragMap = _map.CanDragMap;
            _map.CanDragMap = false;
        }

        private void OnMapMouseMove(object sender, MouseEventArgs e)
        {
            if (_active == null)
                return;
            _active.Position = _map.FromLocalToLatLng(e.X, e.Y);
            _map.Invalidate();
        }

        private void OnMapMouseUp(object sender, MouseEventArgs e)
        {
            if (_active == null)
                return;
            var marker = _active;
            _active = null;
            _map.CanDragMap = _restoreCanDragMap;
            marker.NotifyDragEnded();
            _map.Invalidate();
        }

        private SupeyDraggableMarker HitDraggableMarker()
        {
            if (_map.Overlays == null)
                return null;
            foreach (var overlay in _map.Overlays)
            {
                if (overlay?.Markers == null)
                    continue;
                foreach (var item in overlay.Markers)
                {
                    if (item is SupeyDraggableMarker dm && dm.IsVisible && dm.IsMouseOver)
                        return dm;
                }
            }
            return null;
        }
    }
}
