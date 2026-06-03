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
    /// rebuilds the per-group overlays, dead-head overlay, and legend checkboxes from scratch,
    /// but keeps the user's group visibility when refreshing the same driver (e.g. after drag
    /// or route refresh). The user can hide individual groups via the legend, and the dead-head connector
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
        /// <summary>Lewiston/Auburn corridor — default viewport for Maine dispatch maps.</summary>
        public static readonly PointLatLng MaineLewistonCenter = new PointLatLng(44.1004, -70.2148);

        public const int MaineDefaultZoom = 12;

        private readonly GMapControl _map;
        private readonly FlowLayoutPanel _legend;
        private readonly Panel _legendHost;
        private readonly Label _emptyLabel;

        // Per-group overlay registry — re-built fresh on every ShowDriverPlan call.
        private readonly Dictionary<int, GMapOverlay> _groupOverlays = new Dictionary<int, GMapOverlay>();

        /// <summary>When false, map routes and badges use a neutral color (Schedule Builder setting).</summary>
        public bool UseGroupRouteColors { get; set; } = true;

        private static readonly Color NeutralRouteColor = Color.FromArgb(150, 150, 150);
        private readonly Dictionary<int, CheckBox> _groupCheckboxes = new Dictionary<int, CheckBox>();

        private GMapOverlay _deadheadOverlay;
        private GMapOverlay _homeOverlay;
        private CheckBox _deadheadToggle;

        private SupeyDriverPlan _currentPlan;
        private readonly Dictionary<string, List<SupeyDraggableMarker>> _tripMarkers =
            new Dictionary<string, List<SupeyDraggableMarker>>(StringComparer.OrdinalIgnoreCase);

        private static readonly Font MileageValueFont = new Font("Segoe UI Semibold", 22f);
        private static readonly Color MileageCardBack = Color.FromArgb(34, 38, 34);

        private Panel _mileageHudHost;
        private Panel _groupMileageCard;
        private Panel _tripMileageCard;
        private Panel _efficiencyMileageCard;
        private Label _groupMileageTitle;
        private Label _groupMileageValue;
        private Label _groupMileageDetail;
        private Label _tripMileageTitle;
        private Label _tripMileageValue;
        private Label _tripMileageDetail;
        private Label _efficiencyMileageTitle;
        private Label _efficiencyMileageValue;
        private Label _efficiencyMileageDetail;

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
            _map.Position = MaineLewistonCenter;
            _map.Zoom = MaineDefaultZoom;

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

            // Empty-state composition: full-fill backdrop in the panel surface color, with
            // a centered "card" carrying a glyph + headline + sub-line. Reads like a
            // proper empty state, not a stranded run of label text.
            _emptyLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = "",
                BackColor = SupeyTheme.SurfaceBase,
                Visible = true,
            };

            var emptyCard = new TableLayoutPanel
            {
                BackColor = Color.Transparent,
                ColumnCount = 1,
                RowCount = 3,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Anchor = AnchorStyles.None,
            };
            emptyCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            emptyCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            emptyCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var glyph = new Label
            {
                Text = "🗺",
                Font = new Font("Segoe UI Emoji", 36f),
                ForeColor = SupeyTheme.TextMuted,
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleCenter,
                Margin = new Padding(0, 0, 0, 8),
                Anchor = AnchorStyles.None,
            };
            var head = new Label
            {
                Text = "No schedule on screen yet",
                Font = new Font("Segoe UI Semibold", 13f),
                ForeColor = SupeyTheme.TextPrimary,
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleCenter,
                Margin = new Padding(0, 0, 0, 4),
                Anchor = AnchorStyles.None,
            };
            var sub = new Label
            {
                Text = "Pick a service date · LOAD TRIPS · BUILD",
                Font = SupeyTheme.BodyFont,
                ForeColor = SupeyTheme.TextMuted,
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleCenter,
                Anchor = AnchorStyles.None,
            };
            emptyCard.Controls.Add(glyph, 0, 0);
            emptyCard.Controls.Add(head, 0, 1);
            emptyCard.Controls.Add(sub, 0, 2);

            // Center the card in the empty-label surface. Re-anchor on resize so it stays
            // pinned dead-center even when the user drags the splitter.
            _emptyLabel.Controls.Add(emptyCard);
            _emptyLabel.Resize += (s, e) =>
            {
                emptyCard.Left = Math.Max(0, (_emptyLabel.ClientSize.Width - emptyCard.Width) / 2);
                emptyCard.Top = Math.Max(0, (_emptyLabel.ClientSize.Height - emptyCard.Height) / 2);
            };
            _emptyLabel.HandleCreated += (s, e) =>
            {
                emptyCard.Left = Math.Max(0, (_emptyLabel.ClientSize.Width - emptyCard.Width) / 2);
                emptyCard.Top = Math.Max(0, (_emptyLabel.ClientSize.Height - emptyCard.Height) / 2);
            };

            Controls.Add(_map);
            Controls.Add(_legendHost);
            Controls.Add(_emptyLabel);
            BuildMileageHud();
            _emptyLabel.BringToFront();
            // Until ShowDriverPlan is called the legend has nothing to show — keep the rail
            // hidden so the user doesn't see an empty "Groups" panel pinned to the right edge.
            _legendHost.Visible = false;

            _map.OnMapZoomChanged += () => Invalidate();
            _map.OnMarkerClick += OnMarkerClick;
            SupeyMapMarkerDrag.EnsureWired(_map);
        }

        private void OnMarkerClick(GMapMarker item, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right)
                return;
            var info = item?.Tag as SupeyMapMarkerInfo;
            if (info == null)
                return;
            var menu = new ContextMenuStrip();
            var fix = new ToolStripMenuItem("Fix wrong geocode (move pin)…");
            fix.Click += (s, ev) => OpenGeocodeFix(item, info);
            menu.Items.Add(fix);
            menu.Show(_map, e.Location);
        }

        private void OpenGeocodeFix(GMapMarker item, SupeyMapMarkerInfo info)
        {
            var initial = new GeoPoint(item.Position.Lat, item.Position.Lng);
            GeoPoint saved = initial;
            var dlgInfo = new SupeyMapMarkerInfo
            {
                EndpointLabel = info.EndpointLabel,
                Street = info.Street,
                City = info.City,
                State = info.State,
                Zip = info.Zip,
                OnPinSaved = p =>
                {
                    saved = p;
                    info.OnPinSaved?.Invoke(p);
                },
            };
            using (var dlg = new SupeyGeocodeFixForm(dlgInfo, initial))
            {
                if (dlg.ShowDialog(FindForm()) != DialogResult.OK)
                    return;
            }
            item.Position = new PointLatLng(saved.Lat, saved.Lng);
            _map.Refresh();
        }

        /// <summary>Center map on the Lewiston/Auburn service area (Schedule Builder after BUILD, empty states).</summary>
        public void CenterOnMaineHub(int? zoom = null)
        {
            void Apply()
            {
                _map.Position = MaineLewistonCenter;
                _map.Zoom = zoom ?? MaineDefaultZoom;
                _map.Refresh();
            }
            if (InvokeRequired)
                BeginInvoke((Action)Apply);
            else
                Apply();
        }

        internal static bool IsValidGeoPoint(GeoPoint p) =>
            Math.Abs(p.Lat) >= 0.01 || Math.Abs(p.Lng) >= 0.01;

        internal static bool HasValidMapPins(SupeyDriverPlan plan)
        {
            if (plan == null) return false;
            if (plan.HomeGeo.HasValue && IsValidGeoPoint(plan.HomeGeo.Value))
                return true;
            if (plan.Groups == null) return false;
            foreach (var g in plan.Groups)
            {
                if (g == null) continue;
                foreach (var p in g.PickupPoints)
                    if (IsValidGeoPoint(p)) return true;
                foreach (var p in g.DropoffPoints)
                    if (IsValidGeoPoint(p)) return true;
            }
            return false;
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
            _tripMarkers.Clear();
            ClearMileageHud();
            _emptyLabel.Visible = true;
            _legendHost.Visible = false;
            CenterOnMaineHub();
        }

        /// <summary>Schedule Builder: separate panels for group tour miles, trip PU→DO miles, and route efficiency.</summary>
        public void SetMileageHud(
            SupeyTripCluster group,
            MCDownloadedTrip trip,
            double groupMeters,
            double? tripMeters,
            bool tripApprox,
            double? efficiencyScorePercent,
            double soloSumMeters,
            bool efficiencyApprox,
            double? routeChangeMeters)
        {
            if (_mileageHudHost == null || group == null)
            {
                ClearMileageHud();
                return;
            }

            _groupMileageTitle.Text = "Group Mileage";
            _groupMileageValue.ForeColor = SupeyTheme.AccentPrimary;
            _groupMileageValue.Text = SupeyTripTimes.FormatMiles(groupMeters);
            _groupMileageDetail.Text = "Group " + group.GroupNumber
                + (group.IsStraightLineFallback ? " · estimated straight-line" : " · road route");

            if (trip != null)
            {
                string tn = (trip.TripNumber ?? "").Trim();
                _tripMileageTitle.Text = "Trip Mileage";
                _tripMileageValue.Text = tripMeters.HasValue
                    ? SupeyTripTimes.FormatMiles(tripMeters.Value)
                    : "—";
                _tripMileageValue.ForeColor = tripMeters.HasValue
                    ? SupeyTheme.AccentPrimary
                    : SupeyTheme.TextMuted;
                _tripMileageDetail.Text = string.IsNullOrEmpty(tn)
                    ? "Pickup → dropoff"
                    : tn + " · PU → DO"
                    + (tripApprox ? " · estimated" : "");
                _tripMileageCard.Visible = true;
            }
            else
            {
                _tripMileageCard.Visible = false;
            }

            _efficiencyMileageTitle.Text = "Route Efficiency";
            if (efficiencyScorePercent.HasValue)
            {
                _efficiencyMileageValue.Text = efficiencyScorePercent.Value.ToString("0") + "%";
                double score = efficiencyScorePercent.Value;
                _efficiencyMileageValue.ForeColor = score >= 85
                    ? SupeyTheme.AccentPrimary
                    : score >= 70
                        ? Color.FromArgb(220, 180, 70)
                        : Color.FromArgb(220, 120, 90);
                _efficiencyMileageDetail.Text = "";
                _efficiencyMileageDetail.Visible = false;
                _efficiencyMileageCard.Visible = true;
            }
            else
            {
                _efficiencyMileageValue.Text = "—";
                _efficiencyMileageValue.ForeColor = SupeyTheme.TextMuted;
                _efficiencyMileageDetail.Text = "";
                _efficiencyMileageDetail.Visible = false;
                _efficiencyMileageCard.Visible = group.RiderCount > 0;
            }

            _groupMileageCard.Visible = true;
            FitMileageCardHeights();
            _mileageHudHost.Visible = true;
            _mileageHudHost.BringToFront();
            PositionMileageHudHost();
        }

        private void FitMileageCardHeights()
        {
            FitMileageCard(_groupMileageCard, _groupMileageTitle, _groupMileageValue, _groupMileageDetail);
            FitMileageCard(_tripMileageCard, _tripMileageTitle, _tripMileageValue, _tripMileageDetail);
            FitMileageCard(_efficiencyMileageCard, _efficiencyMileageTitle, _efficiencyMileageValue, _efficiencyMileageDetail);
            _mileageHudHost?.PerformLayout();
        }

        private static void FitMileageCard(Panel card, Label title, Label value, Label detail)
        {
            if (card == null || !card.Visible || detail == null) return;

            int textWidth = card.Width - 4 - 20;
            if (textWidth < 80) textWidth = 156;

            int detailH = string.IsNullOrEmpty(detail.Text)
                ? 0
                : TextRenderer.MeasureText(
                    detail.Text,
                    detail.Font,
                    new Size(textWidth, int.MaxValue),
                    TextFormatFlags.WordBreak).Height;

            int contentH = 8 + title.PreferredHeight + 2 + value.PreferredHeight + 2 + detailH + 8;
            card.Height = Math.Max(80, contentH);
        }

        public void ClearMileageHud()
        {
            if (_mileageHudHost != null)
                _mileageHudHost.Visible = false;
        }

        /// <summary>PU/DO positions from markers currently on the map (after ShowDriverPlan).</summary>
        public bool TryGetTripPinGeoPoints(MCDownloadedTrip trip, out GeoPoint pu, out GeoPoint dof)
        {
            pu = dof = default;
            if (trip == null) return false;
            string key = (trip.TripNumber ?? "").Trim();
            if (key.Length == 0 || !_tripMarkers.TryGetValue(key, out var markers) || markers == null)
                return false;

            bool hasPu = false, hasDo = false;
            foreach (var m in markers)
            {
                if (m == null) continue;
                var info = m.Tag as SupeyMapMarkerInfo;
                if (info == null) continue;
                var gp = new GeoPoint(m.Position.Lat, m.Position.Lng);
                if (info.IsPickup)
                {
                    pu = gp;
                    hasPu = true;
                }
                else
                {
                    dof = gp;
                    hasDo = true;
                }
            }

            return hasPu && hasDo && !(pu.Lat == 0 && pu.Lng == 0) && !(dof.Lat == 0 && dof.Lng == 0);
        }

        private void BuildMileageHud()
        {
            _mileageHudHost = new Panel
            {
                AutoSize = true,
                BackColor = Color.Transparent,
                Location = new Point(12, 12),
                Visible = false,
            };

            var stack = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Color.Transparent,
                Padding = new Padding(0),
            };

            _groupMileageCard = CreateMileageCard(
                "Group Mileage",
                out _groupMileageTitle,
                out _groupMileageValue,
                out _groupMileageDetail);
            _tripMileageCard = CreateMileageCard(
                "Trip Mileage",
                out _tripMileageTitle,
                out _tripMileageValue,
                out _tripMileageDetail);
            _tripMileageCard.Margin = new Padding(0, 8, 0, 0);
            _tripMileageCard.Visible = false;

            _efficiencyMileageCard = CreateMileageCard(
                "Route Efficiency",
                out _efficiencyMileageTitle,
                out _efficiencyMileageValue,
                out _efficiencyMileageDetail);
            _efficiencyMileageCard.Margin = new Padding(0, 8, 0, 0);
            _efficiencyMileageCard.Visible = false;

            stack.Controls.Add(_groupMileageCard);
            stack.Controls.Add(_tripMileageCard);
            stack.Controls.Add(_efficiencyMileageCard);
            _mileageHudHost.Controls.Add(stack);
            Controls.Add(_mileageHudHost);
            Resize += (s, e) => PositionMileageHudHost();
        }

        private void PositionMileageHudHost()
        {
            if (_mileageHudHost == null) return;
            _mileageHudHost.Location = new Point(12, 12);
            _mileageHudHost.BringToFront();
        }

        private static Panel CreateMileageCard(
            string defaultTitle,
            out Label title,
            out Label value,
            out Label detail)
        {
            var card = new Panel
            {
                Width = 200,
                MinimumSize = new Size(200, 80),
                Height = 88,
                BackColor = MileageCardBack,
                Margin = new Padding(0),
            };
            card.Paint += MileageCard_Paint;

            var stripe = new Panel
            {
                Dock = DockStyle.Left,
                Width = 4,
                BackColor = SupeyTheme.AccentStripe,
            };

            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = MileageCardBack,
                Padding = new Padding(10, 8, 10, 8),
            };
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            title = new Label
            {
                Text = defaultTitle,
                AutoSize = true,
                ForeColor = SupeyTheme.TextSecondary,
                Font = SupeyTheme.SubHeaderFont,
                BackColor = MileageCardBack,
                Margin = new Padding(0, 0, 0, 2),
            };
            value = new Label
            {
                Text = "0.0 mi",
                AutoSize = true,
                ForeColor = SupeyTheme.AccentPrimary,
                Font = MileageValueFont,
                BackColor = MileageCardBack,
                Margin = new Padding(0, 0, 0, 2),
            };
            detail = new Label
            {
                Text = "",
                AutoSize = true,
                MaximumSize = new Size(176, 0),
                ForeColor = SupeyTheme.TextMuted,
                Font = SupeyTheme.CaptionFont,
                BackColor = MileageCardBack,
            };

            body.Controls.Add(title, 0, 0);
            body.Controls.Add(value, 0, 1);
            body.Controls.Add(detail, 0, 2);
            card.Controls.Add(stripe);
            card.Controls.Add(body);
            return card;
        }

        private static void MileageCard_Paint(object sender, PaintEventArgs e)
        {
            var card = sender as Panel;
            if (card == null) return;
            using (var pen = new Pen(SupeyTheme.BorderSubtle))
                e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
        }

        /// <summary>
        /// Read-only map for Schedule Builder preview: PU/DO pins for one driver tab (or Reserves).
        /// Geocodes must be resolved by the host before calling; missing endpoints are skipped.
        /// </summary>
        public void ShowScheduleBuilderTrips(
            string tabTitle,
            IReadOnlyList<MCDownloadedTrip> trips,
            IReadOnlyDictionary<string, GeoPoint> pickupByTrip,
            IReadOnlyDictionary<string, GeoPoint> dropoffByTrip,
            int paletteIndex)
        {
            _currentPlan = null;
            _map.Overlays.Clear();
            _groupOverlays.Clear();
            _groupCheckboxes.Clear();
            _legend.Controls.Clear();
            _deadheadToggle = null;
            _deadheadOverlay = null;
            _homeOverlay = null;
            _tripMarkers.Clear();
            _legendHost.Visible = false;

            if (trips == null || trips.Count == 0)
            {
                _emptyLabel.Text = string.IsNullOrWhiteSpace(tabTitle)
                    ? "No trips — click BUILD."
                    : tabTitle + " has no trips.";
                _emptyLabel.Visible = true;
                CenterOnMaineHub();
                return;
            }

            var overlay = new GMapOverlay("fs-tab");
            var allPts = new List<PointLatLng>();

            foreach (var trip in trips)
            {
                if (trip == null) continue;
                string key = (trip.TripNumber ?? "").Trim();
                if (string.IsNullOrEmpty(key)) continue;

                if (pickupByTrip != null && pickupByTrip.TryGetValue(key, out var pu)
                    && !(pu.Lat == 0 && pu.Lng == 0))
                {
                    var pos = new PointLatLng(pu.Lat, pu.Lng);
                    allPts.Add(pos);
                    var marker = new SupeyDraggableMarker(pos, GMarkerGoogleType.green_small);
                    ApplyTripEndpointTooltip(marker, trip, isPickup: true, groupColor: null);
                    overlay.Markers.Add(marker);
                    RegisterTripMarker(trip, marker);
                }

                if (dropoffByTrip != null && dropoffByTrip.TryGetValue(key, out var dof)
                    && !(dof.Lat == 0 && dof.Lng == 0))
                {
                    var pos = new PointLatLng(dof.Lat, dof.Lng);
                    allPts.Add(pos);
                    var marker = new SupeyDraggableMarker(pos, GMarkerGoogleType.red_small);
                    ApplyTripEndpointTooltip(marker, trip, isPickup: false, groupColor: null);
                    overlay.Markers.Add(marker);
                    RegisterTripMarker(trip, marker);
                }
            }

            if (allPts.Count == 0)
            {
                _emptyLabel.Text = tabTitle + " — no geocoded stops yet.";
                _emptyLabel.Visible = true;
                CenterOnMaineHub();
                SetSupeyStatusOnHost?.Invoke(_emptyLabel.Text);
                return;
            }

            _emptyLabel.Visible = false;
            _map.Overlays.Add(overlay);
            FitPoints(allPts, zoomSingle: 13, zoomMulti: 11);
            _map.Refresh();
            SetSupeyStatusOnHost?.Invoke(
                tabTitle + " · " + trips.Count + " trip(s) · " + allPts.Count + " pin(s)");
        }

        /// <summary>Fit map to one group's PU/DO pins (route header row selected).</summary>
        public void FocusGroup(SupeyTripCluster group)
        {
            if (group == null) return;
            var pts = new List<PointLatLng>();
            foreach (var p in group.PickupPoints)
                if (!(p.Lat == 0 && p.Lng == 0)) pts.Add(new PointLatLng(p.Lat, p.Lng));
            foreach (var p in group.DropoffPoints)
                if (!(p.Lat == 0 && p.Lng == 0)) pts.Add(new PointLatLng(p.Lat, p.Lng));
            if (pts.Count == 0)
            {
                SetSupeyStatusOnHost?.Invoke("No map pins for group " + group.GroupNumber + ".");
                return;
            }
            FitPoints(pts, zoomSingle: 13, zoomMulti: 11);
            _map.Refresh();
        }

        /// <summary>Pan/zoom map to a trip's existing PU/DO pins (from list selection).</summary>
        public void FocusTrip(MCDownloadedTrip trip)
        {
            if (trip == null || string.IsNullOrWhiteSpace(trip.TripNumber))
                return;
            if (!_tripMarkers.TryGetValue(trip.TripNumber.Trim(), out var markers) || markers.Count == 0)
            {
                SetSupeyStatusOnHost?.Invoke("No map pins for trip " + trip.TripNumber + " — geocode may be missing.");
                return;
            }

            var pts = new List<PointLatLng>(markers.Count);
            foreach (var m in markers)
                pts.Add(m.Position);
            FitPoints(pts, zoomSingle: 14, zoomMulti: 12);
            _map.Refresh();
        }

        /// <summary>Optional status line on the Supey tab (set from Form1).</summary>
        public Action<string> SetSupeyStatusOnHost { get; set; }

        private void FitPoints(List<PointLatLng> pts, int zoomSingle, int zoomMulti)
        {
            if (pts == null || pts.Count == 0)
            {
                CenterOnMaineHub();
                return;
            }
            pts = pts.Where(p => IsValidGeoPoint(new GeoPoint(p.Lat, p.Lng))).ToList();
            if (pts.Count == 0)
            {
                CenterOnMaineHub();
                return;
            }
            if (pts.Count == 1)
            {
                _map.Position = pts[0];
                _map.Zoom = zoomSingle;
                return;
            }
            double minLat = pts.Min(p => p.Lat), maxLat = pts.Max(p => p.Lat);
            double minLng = pts.Min(p => p.Lng), maxLng = pts.Max(p => p.Lng);
            double padLat = Math.Max((maxLat - minLat) * 0.15, 0.01);
            double padLng = Math.Max((maxLng - minLng) * 0.15, 0.01);
            var rect = RectLatLng.FromLTRB(minLng - padLng, maxLat + padLat, maxLng + padLng, minLat - padLat);
            _map.SetZoomToFitRect(rect);
            if (_map.Zoom > zoomMulti) _map.Zoom = zoomMulti;
        }

        private void RegisterTripMarker(MCDownloadedTrip trip, SupeyDraggableMarker marker)
        {
            if (trip == null || marker == null) return;
            string key = (trip.TripNumber ?? "").Trim();
            if (string.IsNullOrEmpty(key)) return;
            if (!_tripMarkers.TryGetValue(key, out var list))
            {
                list = new List<SupeyDraggableMarker>();
                _tripMarkers[key] = list;
            }
            list.Add(marker);
        }

        /// <summary>
        /// Rebuilds the map for the given driver: one overlay per group, one for the home marker,
        /// one for dead-head connectors. Legend checkboxes default to all visible on first show
        /// or driver change; on refresh for the same driver, prior group visibility is restored
        /// (including single-group focus after drag/route updates). Auto-fits to visible groups.
        /// </summary>
        public void ShowDriverPlan(SupeyDriverPlan plan, bool autoFitViewport = true)
        {
            // Post-build may reorder plan.Groups on another thread; snapshot before draw.
            var groups = plan?.Groups != null ? new List<SupeyTripCluster>(plan.Groups) : new List<SupeyTripCluster>();
            var deadHeads = plan?.DeadHeads != null ? new List<SupeyDeadHeadSegment>(plan.DeadHeads) : new List<SupeyDeadHeadSegment>();

            var legendSnap = CaptureLegendVisibility();
            _currentPlan = plan;
            _map.Overlays.Clear();
            _groupOverlays.Clear();
            _groupCheckboxes.Clear();
            _legend.Controls.Clear();
            _tripMarkers.Clear();
            ClearMileageHud();

            if (plan == null || (!plan.HomeGeo.HasValue && plan.Groups.Count == 0))
            {
                _emptyLabel.Text = "No driver selected.";
                _emptyLabel.Visible = true;
                _legendHost.Visible = false;
                return;
            }
            if (groups.Count == 0 && plan.HomeGeo.HasValue)
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

            // Home marker (overlay added last so it sits above trip pins).
            if (plan.HomeGeo.HasValue && IsValidGeoPoint(plan.HomeGeo.Value))
            {
                _homeOverlay = new GMapOverlay("home");
                var homeAccent = Color.FromArgb(100, 150, 220);
                var home = new SupeyDraggableMarker(
                    new PointLatLng(plan.HomeGeo.Value.Lat, plan.HomeGeo.Value.Lng),
                    GMarkerGoogleType.blue_pushpin)
                {
                    BadgeText = "home",
                    BadgeAccentColor = homeAccent,
                    AllowDrag = false,
                };
                ApplyThemedMarkerTooltip(
                    home,
                    (plan.Driver?.Name ?? "Driver") + " · Home",
                    new[] { "Driver start / end location" },
                    homeAccent);
                _homeOverlay.Markers.Add(home);
            }
            else
            {
                _homeOverlay = null;
            }

            // Dead-head overlay — drawn first (under groups).
            _deadheadOverlay = new GMapOverlay("deadhead");
            foreach (var seg in deadHeads)
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
            foreach (var g in groups)
            {
                Color groupColor = ResolveGroupDisplayColor(g);
                var overlay = new GMapOverlay("group-" + g.GroupNumber);
                var pts = new List<PointLatLng>(g.RoutePolyline.Count);
                foreach (var p in g.RoutePolyline) pts.Add(new PointLatLng(p.Lat, p.Lng));
                if (pts.Count >= 2)
                {
                    var route = new GMapRoute(pts, "Group " + g.GroupNumber)
                    {
                        Stroke = new Pen(groupColor, 4f)
                        {
                            DashStyle = g.IsStraightLineFallback ? DashStyle.Dash : DashStyle.Solid,
                        },
                    };
                    overlay.Routes.Add(route);
                }

                int totalStops = SupeyRouteStopNumbers.TotalStops(g);

                // PU markers (one per trip).
                for (int i = 0; i < g.PickupPoints.Count; i++)
                {
                    int idx = i;
                    var pt = g.PickupPoints[i];
                    if (pt.Lat == 0 && pt.Lng == 0) continue;
                    var trip = i < g.Trips.Count ? g.Trips[i] : null;
                    int stop = SupeyRouteStopNumbers.ForEndpoint(g, isPickup: true, tripIndex: i);
                    var marker = new SupeyDraggableMarker(
                        new PointLatLng(pt.Lat, pt.Lng), GMarkerGoogleType.green_small)
                    {
                        RouteStopNumber = stop,
                        BadgeAccentColor = groupColor,
                        Tag = BuildMarkerInfo(trip, "Pickup", true, p => g.PickupPoints[idx] = p),
                    };
                    ApplyRouteStopTooltip(marker, g, stop, totalStops, isPickup: true, trip);
                    overlay.Markers.Add(marker);
                    RegisterTripMarker(trip, marker);
                }

                // DO markers.
                for (int i = 0; i < g.DropoffPoints.Count; i++)
                {
                    int idx = i;
                    var pt = g.DropoffPoints[i];
                    if (pt.Lat == 0 && pt.Lng == 0) continue;
                    var trip = i < g.Trips.Count ? g.Trips[i] : null;
                    int stop = SupeyRouteStopNumbers.ForEndpoint(g, isPickup: false, tripIndex: i);
                    var marker = new SupeyDraggableMarker(
                        new PointLatLng(pt.Lat, pt.Lng), GMarkerGoogleType.red_small)
                    {
                        RouteStopNumber = stop,
                        BadgeAccentColor = groupColor,
                        Tag = BuildMarkerInfo(trip, "Dropoff", false, p => g.DropoffPoints[idx] = p),
                    };
                    ApplyRouteStopTooltip(marker, g, stop, totalStops, isPickup: false, trip);
                    overlay.Markers.Add(marker);
                    RegisterTripMarker(trip, marker);
                }

                _groupOverlays[g.GroupNumber] = overlay;
                _map.Overlays.Add(overlay);
                AddLegendRow(g);
            }

            if (_homeOverlay != null)
                _map.Overlays.Add(_homeOverlay);

            AddDeadheadToggle();
            ApplyLegendVisibility(plan, legendSnap);
            if (autoFitViewport)
                FitToPlan();
            _map.Refresh();
        }

        private sealed class LegendVisibilitySnapshot
        {
            public string DriverKey;
            public Dictionary<string, bool> TripWasVisible;
            public int CheckedGroupCount;
            public bool? DeadheadChecked;
        }

        private LegendVisibilitySnapshot CaptureLegendVisibility()
        {
            if (_currentPlan == null || _groupCheckboxes.Count == 0)
                return null;

            var snap = new LegendVisibilitySnapshot
            {
                DriverKey = _currentPlan.Driver?.Name ?? "",
                TripWasVisible = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            };

            foreach (var g in _currentPlan.Groups)
            {
                if (!_groupCheckboxes.TryGetValue(g.GroupNumber, out var cb))
                    continue;
                bool visible = cb.Checked;
                if (visible)
                    snap.CheckedGroupCount++;
                foreach (var t in g.Trips)
                {
                    var tn = t?.TripNumber;
                    if (string.IsNullOrWhiteSpace(tn))
                        continue;
                    snap.TripWasVisible[tn] = visible;
                }
            }

            snap.DeadheadChecked = _deadheadToggle?.Checked;
            return snap;
        }

        private void ApplyLegendVisibility(SupeyDriverPlan plan, LegendVisibilitySnapshot snap)
        {
            if (snap == null || plan == null || _groupCheckboxes.Count == 0)
                return;

            string driverKey = plan.Driver?.Name ?? "";
            if (!string.Equals(driverKey, snap.DriverKey, StringComparison.OrdinalIgnoreCase))
                return;

            if (snap.CheckedGroupCount == 1 && snap.TripWasVisible.Count > 0)
            {
                int bestGroup = -1;
                int bestScore = 0;
                foreach (var g in plan.Groups)
                {
                    int score = 0;
                    foreach (var t in g.Trips)
                    {
                        var tn = t?.TripNumber;
                        if (string.IsNullOrWhiteSpace(tn))
                            continue;
                        if (snap.TripWasVisible.TryGetValue(tn, out bool was) && was)
                            score++;
                    }
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestGroup = g.GroupNumber;
                    }
                }

                foreach (var kv in _groupCheckboxes)
                    SetGroupVisible(kv.Key, kv.Key == bestGroup && bestScore > 0);
            }
            else if (snap.CheckedGroupCount > 0 && snap.CheckedGroupCount < _groupCheckboxes.Count)
            {
                foreach (var g in plan.Groups)
                {
                    bool show = false;
                    foreach (var t in g.Trips)
                    {
                        var tn = t?.TripNumber;
                        if (string.IsNullOrWhiteSpace(tn))
                        {
                            show = true;
                            break;
                        }
                        if (!snap.TripWasVisible.TryGetValue(tn, out bool was))
                        {
                            show = true;
                            break;
                        }
                        if (was)
                            show = true;
                    }
                    if (g.Trips.Count == 0)
                        show = true;
                    SetGroupVisible(g.GroupNumber, show);
                }
            }

            if (snap.DeadheadChecked.HasValue && _deadheadToggle != null)
            {
                _deadheadToggle.Checked = snap.DeadheadChecked.Value;
                if (_deadheadOverlay != null)
                    _deadheadOverlay.IsVisibile = snap.DeadheadChecked.Value;
            }

            _map.Refresh();
        }

        private void SetGroupVisible(int groupNumber, bool visible)
        {
            if (_groupCheckboxes.TryGetValue(groupNumber, out var cb) && cb.Checked != visible)
                cb.Checked = visible;
            if (_groupOverlays.TryGetValue(groupNumber, out var overlay))
                overlay.IsVisibile = visible;
        }

        private Color ResolveGroupDisplayColor(SupeyTripCluster g)
        {
            if (g == null) return NeutralRouteColor;
            return UseGroupRouteColors ? g.GroupColor : NeutralRouteColor;
        }

        private void AddLegendRow(SupeyTripCluster g)
        {
            Color groupColor = ResolveGroupDisplayColor(g);
            var row = new Panel
            {
                Width = _legend.ClientSize.Width - 20,
                Height = 28,
                BackColor = Color.FromArgb(45, 45, 45),
            };
            var swatch = new Panel
            {
                BackColor = groupColor,
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
            double routeMi = g.IntraClusterMeters * 0.000621371;
            string miText = routeMi > 0.05 ? " · " + routeMi.ToString("0.0") + " mi" : "";
            var lbl = new Label
            {
                Text = "Grp " + g.GroupNumber + " - " + g.RiderCount + (g.RiderCount == 1 ? " rider " : " riders ") +
                       SupeyTripTimes.FormatTimeOfDay(g.EarliestPickup) + miText,
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
            if (_currentPlan.HomeGeo.HasValue && IsValidGeoPoint(_currentPlan.HomeGeo.Value))
                pts.Add(new PointLatLng(_currentPlan.HomeGeo.Value.Lat, _currentPlan.HomeGeo.Value.Lng));
            foreach (var g in _currentPlan.Groups)
            {
                if (_groupCheckboxes.TryGetValue(g.GroupNumber, out var cb) && !cb.Checked)
                    continue;
                foreach (var p in g.PickupPoints)
                    if (IsValidGeoPoint(p)) pts.Add(new PointLatLng(p.Lat, p.Lng));
                foreach (var p in g.DropoffPoints)
                    if (IsValidGeoPoint(p)) pts.Add(new PointLatLng(p.Lat, p.Lng));
            }
            if (pts.Count == 0)
            {
                CenterOnMaineHub();
                return;
            }
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

        private static readonly Color PickupTooltipAccent = Color.FromArgb(120, 170, 95);
        private static readonly Color DropoffTooltipAccent = Color.FromArgb(200, 95, 95);

        private void ApplyThemedMarkerTooltip(
            GMapMarker marker,
            string title,
            IEnumerable<string> detailLines,
            Color accentColor)
        {
            if (marker == null) return;
            var detail = new List<string>();
            if (detailLines != null)
            {
                foreach (var line in detailLines)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                        detail.Add(line.Trim());
                }
            }

            marker.ToolTipText = title ?? "";
            marker.ToolTipMode = MarkerTooltipMode.OnMouseOver;
            marker.ToolTip = new SupeyMapMarkerToolTip(marker, SupeyTheme.SubHeaderFont, SupeyTheme.CaptionFont)
            {
                Title = title ?? "",
                Detail = detail,
                AccentColor = accentColor,
            };
        }

        private void ApplyRouteStopTooltip(
            GMapMarker marker,
            SupeyTripCluster g,
            int stop,
            int totalStops,
            bool isPickup,
            MCDownloadedTrip trip)
        {
            string leg = isPickup ? "Pickup" : "Dropoff";
            string title = leg + " · Group " + (g?.GroupNumber ?? 0);
            var detail = BuildTripTooltipDetail(trip, isPickup, totalStops > 0 ? stop : 0, totalStops);
            Color accent = g != null ? ResolveGroupDisplayColor(g) : (isPickup ? PickupTooltipAccent : DropoffTooltipAccent);
            ApplyThemedMarkerTooltip(marker, title, detail, accent);
        }

        private void ApplyTripEndpointTooltip(
            GMapMarker marker,
            MCDownloadedTrip trip,
            bool isPickup,
            Color? groupColor)
        {
            string title = (isPickup ? "Pickup" : "Dropoff");
            if (!string.IsNullOrWhiteSpace(trip?.TripNumber))
                title += " · " + trip.TripNumber.Trim();
            var detail = BuildTripTooltipDetail(trip, isPickup, 0, 0);
            Color accent = groupColor ?? (isPickup ? PickupTooltipAccent : DropoffTooltipAccent);
            ApplyThemedMarkerTooltip(marker, title, detail, accent);
        }

        private static List<string> BuildTripTooltipDetail(
            MCDownloadedTrip trip,
            bool isPickup,
            int stop,
            int totalStops)
        {
            var detail = new List<string>();
            if (totalStops > 0 && stop > 0)
                detail.Add("Stop " + stop + " of " + totalStops + " on group route");
            if (!string.IsNullOrWhiteSpace(trip?.ClientFullName))
                detail.Add(trip.ClientFullName.Trim());
            string street = (isPickup ? trip?.PUStreet : trip?.DOStreet) ?? "";
            string city = (isPickup ? trip?.PUCity : trip?.DOCITY) ?? "";
            string addr = string.IsNullOrWhiteSpace(street)
                ? city.Trim()
                : string.IsNullOrWhiteSpace(city) ? street.Trim() : street.Trim() + ", " + city.Trim();
            if (!string.IsNullOrWhiteSpace(addr))
                detail.Add(addr);
            string time = isPickup ? trip?.PUTime : trip?.DOTime;
            if (!string.IsNullOrWhiteSpace(time))
                detail.Add((isPickup ? "PU" : "DO") + " " + time.Trim());
            detail.Add("Right-click to fix geocode");
            return detail;
        }

        private static SupeyMapMarkerInfo BuildMarkerInfo(
            MCDownloadedTrip trip,
            string endpointLabel,
            bool isPickup,
            Action<GeoPoint> onSaved)
        {
            if (trip == null)
                return new SupeyMapMarkerInfo { EndpointLabel = endpointLabel, OnPinSaved = onSaved };
            return new SupeyMapMarkerInfo
            {
                Trip = trip,
                EndpointLabel = endpointLabel,
                IsPickup = isPickup,
                Street = isPickup ? trip.PUStreet : trip.DOStreet,
                City = isPickup ? trip.PUCity : trip.DOCITY,
                State = "ME",
                OnPinSaved = onSaved,
            };
        }
    }
}
