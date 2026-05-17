using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using GMap.NET;
using GMap.NET.MapProviders;
using GMap.NET.WindowsForms;
using GMap.NET.WindowsForms.Markers;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Map + legend pair embedded in the Supey schedule tab. One <see cref="SupeyMapWorkspace"/>
    /// instance lives next to the trip preview ListView; calling <see cref="ShowDriverPlan"/>
    /// rebuilds the per-group overlays, dead-head overlay, and the legend checkboxes from
    /// scratch. The user can hide individual groups via the legend, and the dead-head connector
    /// trails get their own toggle so dispatchers can mute the "between groups" line noise.
    /// </summary>
    /// <remarks>
    /// Uses 8 dark-mode-readable hues from <see cref="SupeyGroupPalette"/>; a group's color is
    /// stable across builds because <see cref="SupeyTripCluster.GroupNumber"/> drives the palette
    /// lookup. Straight-line fallback legs (when OSRM is unreachable) get drawn dashed so the
    /// user knows that piece of geometry is approximate.
    /// </remarks>
    internal sealed class SupeyMapWorkspace : UserControl
    {
        private readonly GMapControl _map;
        private readonly FlowLayoutPanel _legend;
        private readonly Panel _legendHost;
        private readonly Label _emptyLabel;

        // Per-group overlay registry — re-built fresh on every ShowDriverPlan call.
        private readonly Dictionary<int, GMapOverlay> _groupOverlays = new Dictionary<int, GMapOverlay>();
        private readonly Dictionary<int, CheckBox> _groupCheckboxes = new Dictionary<int, CheckBox>();

        private GMapOverlay _deadheadOverlay;
        private GMapOverlay _homeOverlay;
        private CheckBox _deadheadToggle;

        private SupeyDriverPlan _currentPlan;

        public SupeyMapWorkspace()
        {
            DoubleBuffered = true;
            BackColor = Color.FromArgb(40, 40, 40);

            GMapInitializer.EnsureInitialized();

            _map = new GMapControl
            {
                Dock = DockStyle.Fill,
                ShowCenter = false,
                MapProvider = GMapProviders.OpenStreetMap,
                MinZoom = 3,
                MaxZoom = 18,
                Zoom = 11,
                CanDragMap = true,
                MouseWheelZoomEnabled = true,
                DragButton = MouseButtons.Left,
                BackColor = Color.FromArgb(30, 30, 30),
            };
            _map.Position = new PointLatLng(39.7589, -84.1916); // Dayton OH centroid — neutral starting point.

            _legend = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                BackColor = Color.FromArgb(45, 45, 45),
                Padding = new Padding(8),
            };

            _legendHost = new Panel
            {
                Dock = DockStyle.Right,
                Width = 220,
                BackColor = Color.FromArgb(45, 45, 45),
            };
            var legendHeader = new Label
            {
                Dock = DockStyle.Top,
                Height = 28,
                Text = "Groups",
                ForeColor = Color.Gainsboro,
                BackColor = Color.FromArgb(35, 35, 35),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 0, 0, 0),
                Font = new Font("Segoe UI Semibold", 9f),
            };
            _legendHost.Controls.Add(_legend);
            _legendHost.Controls.Add(legendHeader);

            _emptyLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = "Build a schedule to see driver routes here.",
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.Silver,
                BackColor = Color.FromArgb(40, 40, 40),
                Font = new Font("Segoe UI", 11f),
                Visible = true,
            };

            Controls.Add(_map);
            Controls.Add(_legendHost);
            Controls.Add(_emptyLabel);
            _emptyLabel.BringToFront();
            // Until ShowDriverPlan is called the legend has nothing to show — keep the rail
            // hidden so the user doesn't see an empty "Groups" panel pinned to the right edge.
            _legendHost.Visible = false;

            _map.OnMapZoomChanged += () => Invalidate();
        }

        /// <summary>Clears the map + legend so the host can show "no driver selected" state.</summary>
        public void Clear()
        {
            _map.Overlays.Clear();
            _groupOverlays.Clear();
            _groupCheckboxes.Clear();
            _deadheadOverlay = null;
            _homeOverlay = null;
            _legend.Controls.Clear();
            _deadheadToggle = null;
            _currentPlan = null;
            _emptyLabel.Text = "Build a schedule to see driver routes here.";
            _emptyLabel.Visible = true;
            _legendHost.Visible = false;
        }

        /// <summary>
        /// Rebuilds the map for the given driver: one overlay per group, one for the home marker,
        /// one for dead-head connectors. Resets the legend so the user starts with everything
        /// visible. Auto-fits the viewport to the driver's full footprint.
        /// </summary>
        public void ShowDriverPlan(SupeyDriverPlan plan)
        {
            _currentPlan = plan;
            _map.Overlays.Clear();
            _groupOverlays.Clear();
            _groupCheckboxes.Clear();
            _legend.Controls.Clear();

            if (plan == null || (!plan.HomeGeo.HasValue && plan.Groups.Count == 0))
            {
                _emptyLabel.Text = "No driver selected.";
                _emptyLabel.Visible = true;
                _legendHost.Visible = false;
                return;
            }
            if (plan.Groups.Count == 0 && plan.HomeGeo.HasValue)
            {
                _emptyLabel.Text = (plan.Driver?.Name ?? "Driver") + " has no assigned groups.";
                _emptyLabel.Visible = true;
                _legendHost.Visible = false;
            }
            else
            {
                _emptyLabel.Visible = false;
                _legendHost.Visible = true;
            }

            // Home overlay (single marker, always on).
            if (plan.HomeGeo.HasValue)
            {
                _homeOverlay = new GMapOverlay("home");
                var home = new GMarkerGoogle(new PointLatLng(plan.HomeGeo.Value.Lat, plan.HomeGeo.Value.Lng),
                    GMarkerGoogleType.blue_dot)
                {
                    ToolTipText = (plan.Driver?.Name ?? "Driver") + " home",
                    ToolTipMode = MarkerTooltipMode.OnMouseOver,
                };
                _homeOverlay.Markers.Add(home);
                _map.Overlays.Add(_homeOverlay);
            }

            // Dead-head overlay — drawn first (under groups).
            _deadheadOverlay = new GMapOverlay("deadhead");
            foreach (var seg in plan.DeadHeads)
            {
                var pts = new List<PointLatLng>(seg.Polyline.Count);
                foreach (var p in seg.Polyline) pts.Add(new PointLatLng(p.Lat, p.Lng));
                if (pts.Count < 2) continue;
                var route = new GMapRoute(pts, seg.Label)
                {
                    Stroke = new Pen(seg.IsStraightLineFallback ? Color.FromArgb(180, 200, 60, 60)
                                                                : Color.FromArgb(180, 130, 130, 130), 3f)
                    {
                        DashStyle = seg.IsStraightLineFallback ? DashStyle.Dash : DashStyle.Solid,
                    },
                };
                _deadheadOverlay.Routes.Add(route);
            }
            _map.Overlays.Add(_deadheadOverlay);

            // Per-group overlays.
            foreach (var g in plan.Groups)
            {
                var overlay = new GMapOverlay("group-" + g.GroupNumber);
                var pts = new List<PointLatLng>(g.RoutePolyline.Count);
                foreach (var p in g.RoutePolyline) pts.Add(new PointLatLng(p.Lat, p.Lng));
                if (pts.Count >= 2)
                {
                    var route = new GMapRoute(pts, "Group " + g.GroupNumber)
                    {
                        Stroke = new Pen(g.GroupColor, 4f)
                        {
                            DashStyle = g.IsStraightLineFallback ? DashStyle.Dash : DashStyle.Solid,
                        },
                    };
                    overlay.Routes.Add(route);
                }

                // PU markers (one per trip).
                for (int i = 0; i < g.PickupPoints.Count; i++)
                {
                    var pt = g.PickupPoints[i];
                    var trip = i < g.Trips.Count ? g.Trips[i] : null;
                    var marker = new GMarkerGoogle(new PointLatLng(pt.Lat, pt.Lng), GMarkerGoogleType.green_small)
                    {
                        ToolTipText = "Group " + g.GroupNumber + " - PU\n" +
                            (trip?.ClientFullName ?? "") + "\n" + (trip?.PUStreet ?? "") + ", " + (trip?.PUCity ?? "") +
                            "\nPU: " + (trip?.PUTime ?? ""),
                        ToolTipMode = MarkerTooltipMode.OnMouseOver,
                    };
                    overlay.Markers.Add(marker);
                }

                // DO markers.
                for (int i = 0; i < g.DropoffPoints.Count; i++)
                {
                    var pt = g.DropoffPoints[i];
                    var trip = i < g.Trips.Count ? g.Trips[i] : null;
                    var marker = new GMarkerGoogle(new PointLatLng(pt.Lat, pt.Lng), GMarkerGoogleType.red_small)
                    {
                        ToolTipText = "Group " + g.GroupNumber + " - DO\n" +
                            (trip?.ClientFullName ?? "") + "\n" + (trip?.DOStreet ?? "") + ", " + (trip?.DOCITY ?? "") +
                            "\nDO: " + (trip?.DOTime ?? ""),
                        ToolTipMode = MarkerTooltipMode.OnMouseOver,
                    };
                    overlay.Markers.Add(marker);
                }

                _groupOverlays[g.GroupNumber] = overlay;
                _map.Overlays.Add(overlay);
                AddLegendRow(g);
            }

            AddDeadheadToggle();
            FitToPlan();
        }

        private void AddLegendRow(SupeyTripCluster g)
        {
            var row = new Panel
            {
                Width = _legend.ClientSize.Width - 20,
                Height = 28,
                BackColor = Color.FromArgb(45, 45, 45),
            };
            var swatch = new Panel
            {
                BackColor = g.GroupColor,
                Width = 14,
                Height = 14,
                Location = new Point(28, 7),
            };
            var cb = new CheckBox
            {
                Checked = true,
                Width = 22,
                Location = new Point(0, 4),
                Tag = g.GroupNumber,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.Gainsboro,
            };
            cb.CheckedChanged += OnGroupChecked;
            var lbl = new Label
            {
                Text = "Grp " + g.GroupNumber + " - " + g.RiderCount + (g.RiderCount == 1 ? " rider " : " riders ") +
                       SupeyTripTimes.FormatTimeOfDay(g.EarliestPickup),
                AutoSize = false,
                Width = 160,
                Height = 18,
                Location = new Point(48, 5),
                ForeColor = Color.Gainsboro,
                Font = new Font("Segoe UI", 8.5f),
                TextAlign = ContentAlignment.MiddleLeft,
            };

            row.Controls.Add(cb);
            row.Controls.Add(swatch);
            row.Controls.Add(lbl);
            _legend.Controls.Add(row);
            _groupCheckboxes[g.GroupNumber] = cb;
        }

        private void AddDeadheadToggle()
        {
            var row = new Panel
            {
                Width = _legend.ClientSize.Width - 20,
                Height = 30,
                BackColor = Color.FromArgb(45, 45, 45),
                Margin = new Padding(0, 6, 0, 0),
            };
            _deadheadToggle = new CheckBox
            {
                Text = "Dead-heads",
                Checked = true,
                AutoSize = false,
                Width = 160,
                Height = 22,
                Location = new Point(4, 4),
                ForeColor = Color.Gainsboro,
                BackColor = Color.FromArgb(45, 45, 45),
                Font = new Font("Segoe UI", 8.75f),
            };
            _deadheadToggle.CheckedChanged += (s, e) =>
            {
                if (_deadheadOverlay == null) return;
                _deadheadOverlay.IsVisibile = _deadheadToggle.Checked;
                _map.Refresh();
            };
            row.Controls.Add(_deadheadToggle);
            _legend.Controls.Add(row);
        }

        private void OnGroupChecked(object sender, EventArgs e)
        {
            var cb = sender as CheckBox;
            if (cb == null || !(cb.Tag is int groupNumber)) return;
            if (_groupOverlays.TryGetValue(groupNumber, out var overlay))
            {
                overlay.IsVisibile = cb.Checked;
                _map.Refresh();
            }
        }

        /// <summary>Fit the viewport so home + every group is visible with a small margin.</summary>
        private void FitToPlan()
        {
            if (_currentPlan == null) return;
            var pts = new List<PointLatLng>();
            if (_currentPlan.HomeGeo.HasValue)
                pts.Add(new PointLatLng(_currentPlan.HomeGeo.Value.Lat, _currentPlan.HomeGeo.Value.Lng));
            foreach (var g in _currentPlan.Groups)
            {
                foreach (var p in g.PickupPoints) pts.Add(new PointLatLng(p.Lat, p.Lng));
                foreach (var p in g.DropoffPoints) pts.Add(new PointLatLng(p.Lat, p.Lng));
            }
            if (pts.Count == 0) return;
            if (pts.Count == 1)
            {
                _map.Position = pts[0];
                _map.Zoom = 12;
                return;
            }
            double minLat = pts.Min(p => p.Lat), maxLat = pts.Max(p => p.Lat);
            double minLng = pts.Min(p => p.Lng), maxLng = pts.Max(p => p.Lng);
            // Pad ~5% on each side so markers aren't clipped at the edges.
            double padLat = (maxLat - minLat) * 0.10;
            double padLng = (maxLng - minLng) * 0.10;
            if (padLat == 0) padLat = 0.01;
            if (padLng == 0) padLng = 0.01;
            var rect = RectLatLng.FromLTRB(minLng - padLng, maxLat + padLat, maxLng + padLng, minLat - padLat);
            _map.SetZoomToFitRect(rect);
        }

        public void RefitToCurrentPlan() => FitToPlan();
    }
}
