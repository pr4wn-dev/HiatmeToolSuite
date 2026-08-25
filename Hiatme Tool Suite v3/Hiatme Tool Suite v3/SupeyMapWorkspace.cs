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

        private const int GroupKeyPanelWidth = 272;

        private readonly Panel _mapHost;
        private readonly GMapControl _map;
        private SupeyMapLoadingOverlay _mapLoadingOverlay;
        private int _mapLoadingDepth;
        private readonly FlowLayoutPanel _legend;
        private readonly Panel _legendFooter;
        private readonly SupeyCollapsiblePanel _groupKeyCollapsible;
        private readonly Label _emptyLabel;

        /// <summary>Fixed-width dock on the workspace host (not inside the map). Not user-resizable.</summary>
        public SupeyCollapsiblePanel GroupKeyPanel => _groupKeyCollapsible;

        /// <summary>Inner host for group-key legend — may be reparented into a unified side panel.</summary>
        public Panel GroupKeyContentPanel => _groupKeyCollapsible.ContentPanel;

        private bool _groupKeyEmbedded;

        private static readonly Font GroupKeyTitleFont = new Font("Segoe UI Semibold", 9f);
        private static readonly Font GroupKeyDetailFont = new Font("Segoe UI", 8.25f);

        // Per-group overlay registry — re-built fresh on every ShowDriverPlan call.
        private readonly Dictionary<int, GMapOverlay> _groupOverlays = new Dictionary<int, GMapOverlay>();

        /// <summary>When false, map routes and badges use a neutral color (Schedule Builder setting).</summary>
        public bool UseGroupRouteColors { get; set; } = true;

        /// <summary>One PU→DO route per trip; no group legend or dead-head connectors.</summary>
        public bool TripFlatMapMode { get; set; }

        private static readonly Color NeutralRouteColor = Color.FromArgb(150, 150, 150);
        private readonly Dictionary<int, CheckBox> _groupCheckboxes = new Dictionary<int, CheckBox>();

        private GMapOverlay _deadheadOverlay;
        private GMapOverlay _homeOverlay;
        private GMapOverlay _selectionRouteOverlay;
        private GMapOverlay _selectionMarkerOverlay;
        private readonly Dictionary<string, SelectionTopRouteVisual> _selectionTopRoutes =
            new Dictionary<string, SelectionTopRouteVisual>(StringComparer.OrdinalIgnoreCase);
        private CheckBox _deadheadToggle;

        private SupeyDriverPlan _currentPlan;
        private readonly Dictionary<string, List<SupeyDraggableMarker>> _tripMarkers =
            new Dictionary<string, List<SupeyDraggableMarker>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<SupeyDraggableMarker, GMapOverlay> _markerHomeOverlay =
            new Dictionary<SupeyDraggableMarker, GMapOverlay>();
        private readonly Dictionary<string, LegendVisibilitySnapshot> _legendSnapByTabKey =
            new Dictionary<string, LegendVisibilitySnapshot>(StringComparer.OrdinalIgnoreCase);
        private bool _deferSelectionOverlaySync;

        // Last ApplyMapDisplayFilter pass — used to restore group tours after selection overlay sync.
        private int? _filterSelectedGroupNumber;
        private readonly HashSet<string> _filterSelectedTrips =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, bool> _filterGroupTourVisible =
            new Dictionary<int, bool>();
        private readonly Dictionary<int, bool> _filterGroupOverlayVisible =
            new Dictionary<int, bool>();

        private readonly Dictionary<string, TripLegRouteVisual> _tripLegRoutes =
            new Dictionary<string, TripLegRouteVisual>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _highlightedTripNumbers =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Timer _selectionPulseTimer;
        private float _selectionPulsePhase;

        private sealed class TripLegRouteVisual
        {
            public GMapRoute Route;
            public List<PointLatLng> Points;
            public Color BaseColor;
            public float BaseWidth;
            public DashStyle BaseDash;
            public bool FilterVisible = true;
        }

        private sealed class SelectionTopRouteVisual
        {
            public GMapRoute BorderRoute;
            public GMapRoute ColorRoute;
        }

        private const float SelectionRouteBorderExtraWidth = 3f;

        private ScheduleBuilderMapMileageHud _mileageHud;
        private Panel _selectionHiddenHint;
        private Label _selectionHiddenHintLbl;

        public SupeyMapWorkspace()
        {
            DoubleBuffered = true;
            BackColor = Color.FromArgb(40, 40, 40);

            GMapInitializer.EnsureInitialized();

            _mapHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(30, 30, 30),
            };

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
            _mapHost.Controls.Add(_map);

            _legend = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                BackColor = SupeyTheme.Surface,
                Padding = new Padding(0, 4, 0, 0),
            };
            _legend.Resize += (s, e) => RelayoutLegendRows();

            _legendFooter = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 36,
                BackColor = SupeyTheme.Surface,
                Padding = new Padding(0, 6, 0, 0),
            };

            _groupKeyCollapsible = new SupeyCollapsiblePanel
            {
                Title = "Group key",
                Dock = DockStyle.Right,
                ExpandedWidth = GroupKeyPanelWidth,
                MinExpandedWidth = GroupKeyPanelWidth,
                MaxExpandedWidth = GroupKeyPanelWidth,
                Expanded = true,
            };
            BuildGroupKeyContentHost();

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

            Controls.Add(_mapHost);
            BuildMapLoadingOverlay();
            Controls.Add(_emptyLabel);
            BuildMileageHud();
            BuildSelectionHiddenHint();
            _emptyLabel.BringToFront();
            SetGroupKeyDockVisible(false);

            _mapHost.Resize += (s, e) => SyncMapLoadingOverlayBounds();
            Resize += (s, e) => SyncMapLoadingOverlayBounds();

            _map.OnMapZoomChanged += () => Invalidate();
            _map.OnMarkerClick += OnMarkerClick;
            SupeyMapMarkerDrag.EnsureWired(_map);

            _selectionPulseTimer = new Timer { Interval = 45 };
            _selectionPulseTimer.Tick += (s, e) => AdvanceSelectionPulse();
        }

        private void BuildMapLoadingOverlay()
        {
            _mapLoadingOverlay = new SupeyMapLoadingOverlay();
            Controls.Add(_mapLoadingOverlay);
            SyncMapLoadingOverlayBounds();
        }

        private void SyncMapLoadingOverlayBounds()
        {
            if (_mapLoadingOverlay == null || _mapHost == null || _mapHost.IsDisposed)
                return;

            _mapLoadingOverlay.Bounds = _mapHost.Bounds;
        }

        private void ShowMapLoadingLayer()
        {
            SyncMapLoadingOverlayBounds();
            _mapLoadingOverlay.Visible = true;
            _mapLoadingOverlay.BringToFront();
            if (_mileageHud != null && _mileageHud.Visible)
                _mileageHud.BringToFront();
        }

        /// <summary>Ref-counted map loading veil (spinner). Pair with <see cref="PopMapLoading"/>.</summary>
        public void PushMapLoading(string message = null)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => PushMapLoading(message)));
                return;
            }

            if (!string.IsNullOrWhiteSpace(message))
                _mapLoadingOverlay.Message = message.Trim();

            _mapLoadingDepth++;
            if (_mapLoadingDepth == 1)
            {
                ShowMapLoadingLayer();
                _mapLoadingOverlay.IsAnimating = true;
            }
        }

        public void PopMapLoading()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(PopMapLoading));
                return;
            }

            if (_mapLoadingDepth <= 0)
                return;

            _mapLoadingDepth--;
            if (_mapLoadingDepth == 0)
            {
                _mapLoadingOverlay.IsAnimating = false;
                _mapLoadingOverlay.Visible = false;
            }
        }

        public void SetMapLoadingMessage(string message)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => SetMapLoadingMessage(message)));
                return;
            }

            if (string.IsNullOrWhiteSpace(message) || _mapLoadingDepth <= 0)
                return;

            _mapLoadingOverlay.Message = message.Trim();
        }

        private void BuildGroupKeyContentHost()
        {
            var host = _groupKeyCollapsible.ContentPanel;
            host.BackColor = SupeyTheme.Surface;
            host.Padding = new Padding(10, 8, 10, 8);

            var hint = new Label
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                MaximumSize = new Size(GroupKeyPanelWidth - 20, 0),
                Text = "Show or hide groups on the map. Colors match trip list stripes and routes.",
                ForeColor = SupeyTheme.TextMuted,
                BackColor = SupeyTheme.Surface,
                Font = new Font("Segoe UI", 8.75f),
                Padding = new Padding(0, 0, 0, 8),
            };

            var footerDivider = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 1,
                BackColor = SupeyTheme.Divider,
            };

            host.Controls.Add(_legend);
            host.Controls.Add(_legendFooter);
            host.Controls.Add(footerDivider);
            host.Controls.Add(hint);
        }

        private void SetGroupKeyDockVisible(bool visible)
        {
            if (!_groupKeyEmbedded)
                _groupKeyCollapsible.Visible = visible;
            if (!visible) return;
            if (!_groupKeyEmbedded)
                _groupKeyCollapsible.ApplyExpandedLayout();
            RelayoutLegendRows();
        }

        public void SetGroupKeyEmbedded(bool embedded)
        {
            _groupKeyEmbedded = embedded;
            if (embedded)
                _groupKeyCollapsible.Visible = false;
        }

        public void RelayoutGroupKeyIfNeeded() => RelayoutLegendRows();

        private int GetLegendRowWidth() => Math.Max(160, _legend.ClientSize.Width - 4);

        private void RelayoutLegendRows()
        {
            int w = GetLegendRowWidth();
            foreach (Control c in _legend.Controls)
            {
                if (!(c is Panel row)) continue;
                row.Width = w;
                foreach (Control child in row.Controls)
                {
                    if (child is Label lbl && lbl.Anchor.HasFlag(AnchorStyles.Right))
                        lbl.Width = Math.Max(80, w - lbl.Left - 4);
                }
            }
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

        private void ClearLegend()
        {
            foreach (Control c in _legend.Controls)
                c.Dispose();
            _legend.Controls.Clear();
            foreach (Control c in _legendFooter.Controls)
                c.Dispose();
            _legendFooter.Controls.Clear();
            _deadheadToggle = null;
        }

        private static void DisposeRouteStrokesInOverlay(GMap.NET.WindowsForms.GMapOverlay overlay)
        {
            if (overlay?.Routes == null)
                return;
            foreach (GMap.NET.WindowsForms.GMapRoute route in overlay.Routes)
                route.Stroke?.Dispose();
        }

        private void DisposeAllOverlayRouteStrokes()
        {
            if (_map?.Overlays != null)
            {
                foreach (var overlay in _map.Overlays)
                    DisposeRouteStrokesInOverlay(overlay);
            }
            foreach (var kv in _groupOverlays)
                DisposeRouteStrokesInOverlay(kv.Value);
            DisposeRouteStrokesInOverlay(_deadheadOverlay);
            DisposeRouteStrokesInOverlay(_homeOverlay);
            DisposeRouteStrokesInOverlay(_selectionRouteOverlay);
            DisposeRouteStrokesInOverlay(_selectionMarkerOverlay);
        }

        /// <summary>Clears the map + legend so the host can show "no driver selected" state.</summary>
        public void Clear()
        {
            _selectionPulseTimer?.Stop();
            ClearSelectionTopRoutes();
            DisposeAllOverlayRouteStrokes();
            _map.Overlays.Clear();
            _groupOverlays.Clear();
            _groupCheckboxes.Clear();
            _deadheadOverlay = null;
            _homeOverlay = null;
            _selectionRouteOverlay = null;
            _selectionMarkerOverlay = null;
            _selectionTopRoutes.Clear();
            _markerHomeOverlay.Clear();
            ClearLegend();
            _currentPlan = null;
            _tripMarkers.Clear();
            ClearTripSelectionHighlight();
            ClearMileageHud();
            _emptyLabel.Visible = true;
            SetGroupKeyDockVisible(false);
            CenterOnMaineHub();
        }

        /// <summary>Schedule Builder: compact group / efficiency / trip mileage overlay.</summary>
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
            if (_mileageHud == null || group == null)
            {
                ClearMileageHud();
                return;
            }

            _mileageHud.SetValues(
                group,
                trip,
                groupMeters,
                tripMeters,
                tripApprox,
                efficiencyScorePercent,
                efficiencyApprox,
                routeChangeMeters);
            PositionMileageHudHost();
        }

        public void ClearMileageHud()
        {
            _mileageHud?.HideHud();
        }

        /// <summary>Show mileage overlay immediately with placeholders while OSRM work runs.</summary>
        public void SetMileageHudBusy(SupeyTripCluster group, MCDownloadedTrip trip)
        {
            if (_mileageHud == null || group == null)
            {
                ClearMileageHud();
                return;
            }

            _mileageHud.SetBusy(group, trip);
            PositionMileageHudHost();
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
            _mileageHud = new ScheduleBuilderMapMileageHud();
            Controls.Add(_mileageHud);
            Resize += (s, e) => PositionMileageHudHost();
        }

        private void BuildSelectionHiddenHint()
        {
            _selectionHiddenHintLbl = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = SupeyTheme.WarnText,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9f),
                Padding = new Padding(8, 0, 8, 0),
            };

            _selectionHiddenHint = new Panel
            {
                Visible = false,
                Height = 34,
                BackColor = Color.FromArgb(240, 34, 38, 34),
                BorderStyle = BorderStyle.FixedSingle,
            };
            _selectionHiddenHint.Paint += (s, e) =>
            {
                using (var pen = new Pen(SupeyTheme.WarnText, 1f))
                    e.Graphics.DrawRectangle(pen, 0, 0, _selectionHiddenHint.Width - 1, _selectionHiddenHint.Height - 1);
            };
            _selectionHiddenHint.Controls.Add(_selectionHiddenHintLbl);
            Controls.Add(_selectionHiddenHint);
            Resize += (s, e) => PositionSelectionHiddenHint();
        }

        private void PositionSelectionHiddenHint()
        {
            if (_selectionHiddenHint == null || !_selectionHiddenHint.Visible)
                return;

            int maxW = Math.Max(280, Math.Min(520, ClientSize.Width - 24));
            _selectionHiddenHint.Width = maxW;
            _selectionHiddenHint.Left = Math.Max(12, (ClientSize.Width - maxW) / 2);
            _selectionHiddenHint.Top = Math.Max(12, ClientSize.Height - _selectionHiddenHint.Height - 12);
            _selectionHiddenHint.BringToFront();
        }

        private void PositionMileageHudHost()
        {
            _mileageHud?.FitToMap(ClientSize);
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
            ClearLegend();
            _deadheadOverlay = null;
            _homeOverlay = null;
            _tripMarkers.Clear();
            _markerHomeOverlay.Clear();
            SetGroupKeyDockVisible(false);

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
                    RegisterTripMarker(trip, marker, overlay);
                }

                if (dropoffByTrip != null && dropoffByTrip.TryGetValue(key, out var dof)
                    && !(dof.Lat == 0 && dof.Lng == 0))
                {
                    var pos = new PointLatLng(dof.Lat, dof.Lng);
                    allPts.Add(pos);
                    var marker = new SupeyDraggableMarker(pos, GMarkerGoogleType.red_small);
                    ApplyTripEndpointTooltip(marker, trip, isPickup: false, groupColor: null);
                    overlay.Markers.Add(marker);
                    RegisterTripMarker(trip, marker, overlay);
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

        private void RegisterTripMarker(MCDownloadedTrip trip, SupeyDraggableMarker marker, GMapOverlay homeOverlay)
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
            if (homeOverlay != null)
                _markerHomeOverlay[marker] = homeOverlay;
        }

        /// <summary>Pulse-highlight PU/DO pins and the trip PU→DO route for list selection.</summary>
        public void SetSelectedTripHighlight(IEnumerable<string> tripNumbers)
        {
            _highlightedTripNumbers.Clear();
            if (tripNumbers != null)
            {
                foreach (var raw in tripNumbers)
                {
                    string tn = (raw ?? "").Trim();
                    if (tn.Length > 0)
                        _highlightedTripNumbers.Add(tn);
                }
            }

            foreach (var kv in _tripMarkers)
            {
                bool on = _highlightedTripNumbers.Contains(kv.Key);
                foreach (var marker in kv.Value)
                {
                    marker.IsSelectionHighlighted = on;
                    if (!on)
                        marker.SelectionPulsePhase = 0f;
                }
            }

            ApplyTripRouteHighlightStyles();

            if (_highlightedTripNumbers.Count == 0)
            {
                _selectionPulseTimer.Stop();
                _selectionPulsePhase = 0f;
            }
            else if (!_selectionPulseTimer.Enabled)
            {
                _selectionPulseTimer.Start();
            }

            UpdateSelectionHiddenHint();
            SyncSelectionRouteOverlay();
            _map.Refresh();
        }

        public void ClearTripSelectionHighlight()
        {
            SetSelectedTripHighlight(null);
        }

        private void EnsureSelectionRouteOverlayOnTop()
        {
            if (_selectionRouteOverlay == null)
                _selectionRouteOverlay = new GMapOverlay("selection-routes");
            _selectionRouteOverlay.IsVisibile = true;

            if (!_map.Overlays.Contains(_selectionRouteOverlay))
                _map.Overlays.Add(_selectionRouteOverlay);
            else
            {
                _map.Overlays.Remove(_selectionRouteOverlay);
                _map.Overlays.Add(_selectionRouteOverlay);
            }

            EnsureSelectionMarkerOverlayOnTop();
        }

        private void EnsureSelectionMarkerOverlayOnTop()
        {
            if (_selectionMarkerOverlay == null)
                _selectionMarkerOverlay = new GMapOverlay("selection-markers");

            if (!_map.Overlays.Contains(_selectionMarkerOverlay))
                _map.Overlays.Add(_selectionMarkerOverlay);
            else
            {
                _map.Overlays.Remove(_selectionMarkerOverlay);
                _map.Overlays.Add(_selectionMarkerOverlay);
            }
        }

        private void RestoreSelectionMarkersToHome()
        {
            if (_selectionMarkerOverlay == null || _selectionMarkerOverlay.Markers.Count == 0)
                return;

            var moving = new List<SupeyDraggableMarker>();
            foreach (var item in _selectionMarkerOverlay.Markers)
            {
                if (item is SupeyDraggableMarker dm)
                    moving.Add(dm);
            }

            foreach (var marker in moving)
            {
                _selectionMarkerOverlay.Markers.Remove(marker);
                if (_markerHomeOverlay.TryGetValue(marker, out var home) && home != null)
                    home.Markers.Add(marker);
            }
        }

        /// <summary>Draw selected trip PU/DO pins on a top overlay so they sit above other markers.</summary>
        private void SyncSelectionMarkerOverlay()
        {
            RestoreSelectionMarkersToHome();

            if (_highlightedTripNumbers.Count == 0)
                return;

            EnsureSelectionMarkerOverlayOnTop();

            foreach (var tn in _highlightedTripNumbers)
            {
                if (!_tripMarkers.TryGetValue(tn, out var markers))
                    continue;
                foreach (var marker in markers)
                {
                    if (!marker.IsVisible)
                        continue;
                    if (!_markerHomeOverlay.TryGetValue(marker, out var home) || home == null)
                        continue;
                    if (!home.Markers.Contains(marker))
                        continue;

                    home.Markers.Remove(marker);
                    _selectionMarkerOverlay.Markers.Add(marker);
                }
            }
        }

        private void ClearSelectionTopRoutes()
        {
            if (_selectionRouteOverlay != null)
            {
                foreach (var route in _selectionRouteOverlay.Routes)
                    route.Stroke?.Dispose();
                _selectionRouteOverlay.Routes.Clear();
            }
            _selectionTopRoutes.Clear();
        }

        private static bool IsTripLegRouteName(string routeName) =>
            routeName != null && routeName.StartsWith("trip-leg:", StringComparison.OrdinalIgnoreCase);

        private bool IsGroupLegendVisible(int groupNumber)
        {
            if (TripFlatMapMode)
                return true;
            if (_groupCheckboxes.TryGetValue(groupNumber, out var cb))
                return cb.Checked;
            return true;
        }

        private bool IsGroupRouteVisible(int groupNumber)
        {
            if (!_groupOverlays.ContainsKey(groupNumber))
                return false;
            return IsGroupLegendVisible(groupNumber);
        }

        /// <summary>
        /// GMap.NET can hide routes when overlay.IsVisibile is false while markers still draw.
        /// Keep group overlays enabled; filter with route/marker IsVisible instead.
        /// </summary>
        private void EnsureGroupOverlaysEnabled()
        {
            foreach (var kv in _groupOverlays)
                kv.Value.IsVisibile = true;
        }

        private bool ResolveTripLegRouteVisible(string tripNumber)
        {
            string tn = (tripNumber ?? "").Trim();
            if (tn.Length == 0)
                return true;
            if (!TryGetTripGroupNumber(tn, out int gn))
                return true;
            if (!IsGroupLegendVisible(gn))
                return false;

            if (MapDisplayFilter == FsMapDisplayMode.AllDriverTrips)
                return true;

            if (_tripLegRoutes.TryGetValue(tn, out var visual))
                return visual.FilterVisible;
            return true;
        }

        /// <summary>Sync map workspace filter mode without re-applying visibility (tab switches).</summary>
        public void SetMapDisplayFilterMode(FsMapDisplayMode mode) => MapDisplayFilter = mode;

        private bool ShouldShowGroupTour(int groupNumber)
        {
            if (!IsGroupLegendVisible(groupNumber))
                return false;
            if (MapDisplayFilter == FsMapDisplayMode.AllDriverTrips)
                return true;
            return _filterGroupTourVisible.TryGetValue(groupNumber, out bool filtered) && filtered;
        }

        private void RestoreNonTripLegBaseRoutes()
        {
            if (_currentPlan?.Groups == null)
                return;

            foreach (var g in _currentPlan.Groups)
            {
                if (!_groupOverlays.TryGetValue(g.GroupNumber, out var overlay))
                    continue;

                bool showTour = ShouldShowGroupTour(g.GroupNumber);

                foreach (var route in overlay.Routes)
                {
                    if (route == null || IsTripLegRouteName(route.Name))
                        continue;
                    route.IsVisible = showTour;
                }
            }
        }

        private static Pen CreateRoutePen(Color color, float width, DashStyle dash)
        {
            return new Pen(color, width)
            {
                DashStyle = dash,
                LineJoin = LineJoin.Round,
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };
        }

        /// <summary>Draw selected trip PU→DO legs on a top overlay so they sit above other routes.</summary>
        private void SyncSelectionRouteOverlay()
        {
            ClearSelectionTopRoutes();

            if (_highlightedTripNumbers.Count == 0)
            {
                foreach (var kv in _tripLegRoutes)
                {
                    if (kv.Value?.Route == null)
                        continue;
                    bool vis = ResolveTripLegRouteVisible(kv.Key);
                    kv.Value.FilterVisible = vis;
                    kv.Value.Route.IsVisible = vis;
                }
                RestoreNonTripLegBaseRoutes();
                ApplyTripRouteHighlightStyles();
                SyncSelectionMarkerOverlay();
                return;
            }

            if (_tripLegRoutes.Count == 0)
            {
                ApplyTripRouteHighlightStyles();
                SyncSelectionMarkerOverlay();
                return;
            }

            EnsureSelectionRouteOverlayOnTop();

            foreach (var kv in _tripLegRoutes)
            {
                var visual = kv.Value;
                if (visual?.Route == null)
                    continue;

                bool isHighlighted = _highlightedTripNumbers.Contains(kv.Key);
                bool legVisible = ResolveTripLegRouteVisible(kv.Key);
                var sourcePts = visual.Points ?? visual.Route.Points;
                if (isHighlighted && legVisible && sourcePts != null && sourcePts.Count >= 2)
                {
                    visual.Route.IsVisible = false;
                    var pts = new List<PointLatLng>(sourcePts);
                    var border = new GMapRoute(pts, "sel-trip-leg-border:" + kv.Key);
                    var top = new GMapRoute(pts, "sel-trip-leg:" + kv.Key);
                    _selectionRouteOverlay.Routes.Add(border);
                    _selectionRouteOverlay.Routes.Add(top);
                    _selectionTopRoutes[kv.Key] = new SelectionTopRouteVisual
                    {
                        BorderRoute = border,
                        ColorRoute = top,
                    };
                }
                else
                {
                    bool vis = ResolveTripLegRouteVisible(kv.Key);
                    visual.FilterVisible = vis;
                    visual.Route.IsVisible = vis;
                }
            }

            RestoreNonTripLegBaseRoutes();
            ApplyTripRouteHighlightStyles();
            SyncSelectionMarkerOverlay();
        }

        private void UpdateSelectionHiddenHint()
        {
            if (_selectionHiddenHint == null || _selectionHiddenHintLbl == null)
                return;

            if (TripFlatMapMode
                || _groupCheckboxes.Count == 0
                || MapDisplayFilter != FsMapDisplayMode.AllDriverTrips
                || _highlightedTripNumbers.Count == 0)
            {
                _selectionHiddenHint.Visible = false;
                return;
            }

            var hiddenGroups = new SortedSet<int>();
            foreach (var tn in _highlightedTripNumbers)
            {
                if (IsTripVisibleOnMap(tn))
                    continue;
                if (TryGetTripGroupNumber(tn, out int gn)
                    && _groupCheckboxes.TryGetValue(gn, out var cb)
                    && !cb.Checked)
                {
                    hiddenGroups.Add(gn);
                }
            }

            if (hiddenGroups.Count == 0)
            {
                _selectionHiddenHint.Visible = false;
                return;
            }

            string groupLabel = hiddenGroups.Count == 1
                ? "Group " + hiddenGroups.Min()
                : "Groups " + string.Join(", ", hiddenGroups);
            string verb = hiddenGroups.Count == 1 ? "is" : "are";
            string pronoun = hiddenGroups.Count == 1 ? "it" : "them";
            string tripLabel = _highlightedTripNumbers.Count == 1 ? "this trip" : "these trips";
            _selectionHiddenHintLbl.Text =
                groupLabel + " " + verb + " hidden in the Group key — enable "
                + pronoun + " to see " + tripLabel + " on the map.";
            _selectionHiddenHint.Visible = true;
            PositionSelectionHiddenHint();
        }

        private bool IsTripVisibleOnMap(string tripNumber)
        {
            string key = (tripNumber ?? "").Trim();
            if (key.Length == 0 || !_tripMarkers.TryGetValue(key, out var markers))
                return false;
            foreach (var marker in markers)
            {
                if (marker.IsVisible)
                    return true;
            }
            return false;
        }

        private bool TryGetTripGroupNumber(string tripNumber, out int groupNumber)
        {
            groupNumber = 0;
            if (string.IsNullOrWhiteSpace(tripNumber) || _currentPlan?.Groups == null)
                return false;
            string key = tripNumber.Trim();
            foreach (var g in _currentPlan.Groups)
            {
                if (g?.Trips == null)
                    continue;
                foreach (var t in g.Trips)
                {
                    if (string.Equals((t?.TripNumber ?? "").Trim(), key, StringComparison.OrdinalIgnoreCase))
                    {
                        groupNumber = g.GroupNumber;
                        return true;
                    }
                }
            }
            return false;
        }

        private void AdvanceSelectionPulse()
        {
            if (_highlightedTripNumbers.Count == 0)
            {
                _selectionPulseTimer.Stop();
                return;
            }

            _selectionPulsePhase += 0.045f;
            if (_selectionPulsePhase > 1f)
                _selectionPulsePhase -= 1f;

            foreach (var tn in _highlightedTripNumbers)
            {
                if (!_tripMarkers.TryGetValue(tn, out var markers))
                    continue;
                foreach (var marker in markers)
                {
                    if (marker.IsVisible)
                        marker.SelectionPulsePhase = _selectionPulsePhase;
                }
            }

            ApplyTripRouteHighlightStyles();
            _map.Refresh();
        }

        private void ApplyTripRouteHighlightStyles()
        {
            float wave = (float)((Math.Sin(_selectionPulsePhase * Math.PI * 2) + 1) * 0.5);
            float visibility = 1f - wave;

            foreach (var kv in _tripLegRoutes)
            {
                var visual = kv.Value;
                if (visual?.Route == null || _selectionTopRoutes.ContainsKey(kv.Key))
                    continue;

                visual.Route.Stroke?.Dispose();
                visual.Route.Stroke = CreateRoutePen(visual.BaseColor, visual.BaseWidth, visual.BaseDash);
            }

            foreach (var kv in _selectionTopRoutes)
            {
                if (!_tripLegRoutes.TryGetValue(kv.Key, out var visual))
                    continue;

                var top = kv.Value;
                if (top?.BorderRoute == null || top.ColorRoute == null)
                    continue;

                Color accent = visual.BaseColor;
                float borderWidth = visual.BaseWidth + SelectionRouteBorderExtraWidth;

                top.BorderRoute.Stroke?.Dispose();
                top.BorderRoute.Stroke = CreateRoutePen(Color.Black, borderWidth, DashStyle.Solid);
                top.BorderRoute.IsVisible = true;

                int alpha = (int)(255 * visibility);
                top.ColorRoute.Stroke?.Dispose();
                if (alpha < 8)
                {
                    top.ColorRoute.IsVisible = false;
                    continue;
                }

                top.ColorRoute.Stroke = CreateRoutePen(
                    Color.FromArgb(alpha, accent.R, accent.G, accent.B),
                    visual.BaseWidth,
                    visual.BaseDash);
                top.ColorRoute.IsVisible = true;
            }
        }

        private void RegisterTripLegRoute(
            string tripNumber,
            GMapRoute route,
            Color color,
            float width,
            bool straightFallback)
        {
            string tn = (tripNumber ?? "").Trim();
            if (tn.Length == 0 || route == null)
                return;

            List<PointLatLng> pts = null;
            if (route.Points != null && route.Points.Count >= 2)
                pts = new List<PointLatLng>(route.Points);

            _tripLegRoutes[tn] = new TripLegRouteVisual
            {
                Route = route,
                Points = pts,
                BaseColor = color,
                BaseWidth = width,
                BaseDash = straightFallback ? DashStyle.Dash : DashStyle.Solid,
            };
        }

        private static string TripLegRouteName(string tripNumber) =>
            "trip-leg:" + (tripNumber ?? "").Trim();

        /// <summary>
        /// Rebuilds the map for the given driver: one overlay per group, one for the home marker,
        /// one for dead-head connectors. Legend checkboxes default to all visible on first show
        /// or driver change; on refresh for the same driver, prior group visibility is restored
        /// (including single-group focus after drag/route updates). Auto-fits to visible groups.
        /// </summary>
        public void ShowDriverPlan(SupeyDriverPlan plan, bool autoFitViewport = true, bool restoreSavedLegend = true)
        {
            // Post-build may reorder plan.Groups on another thread; snapshot before draw.
            var groups = plan?.Groups != null ? new List<SupeyTripCluster>(plan.Groups) : new List<SupeyTripCluster>();
            var deadHeads = plan?.DeadHeads != null ? new List<SupeyDeadHeadSegment>(plan.DeadHeads) : new List<SupeyDeadHeadSegment>();

            var legendSnap = restoreSavedLegend ? ResolveLegendSnapshot(plan) : null;
            _currentPlan = plan;
            _selectionPulseTimer?.Stop();
            ClearSelectionTopRoutes();
            DisposeAllOverlayRouteStrokes();
            _map.Overlays.Clear();
            _groupOverlays.Clear();
            _groupCheckboxes.Clear();
            _selectionRouteOverlay = null;
            _selectionMarkerOverlay = null;
            _selectionTopRoutes.Clear();
            _markerHomeOverlay.Clear();
            ClearLegend();
            _tripMarkers.Clear();
            _tripLegRoutes.Clear();
            _filterGroupTourVisible.Clear();
            _filterSelectedTrips.Clear();
            _filterSelectedGroupNumber = null;
            _filterGroupOverlayVisible.Clear();
            ClearTripSelectionHighlight();
            ClearMileageHud();

            if (plan == null || (!plan.HomeGeo.HasValue && plan.Groups.Count == 0))
            {
                _emptyLabel.Text = "No driver selected.";
                _emptyLabel.Visible = true;
                SetGroupKeyDockVisible(false);
                return;
            }
            if (groups.Count == 0 && plan.HomeGeo.HasValue)
            {
                _emptyLabel.Text = (plan.Driver?.Name ?? "Driver") + " has no assigned groups.";
                _emptyLabel.Visible = true;
                SetGroupKeyDockVisible(false);
            }
            else
            {
                _emptyLabel.Visible = false;
                SetGroupKeyDockVisible(!TripFlatMapMode);
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

            // Dead-head overlay — group mode only.
            _deadheadOverlay = new GMapOverlay("deadhead");
            if (!TripFlatMapMode)
            {
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
            }
            _map.Overlays.Add(_deadheadOverlay);

            // Per-group (or per-trip in flat mode) overlays.
            foreach (var g in groups)
            {
                Color groupColor = ResolveGroupDisplayColor(g);
                string overlayKey = TripFlatMapMode
                    ? "trip-" + g.GroupNumber
                    : "group-" + g.GroupNumber;
                var overlay = new GMapOverlay(overlayKey);

                if (TripFlatMapMode)
                {
                    string tn = g.Trips.Count > 0 ? (g.Trips[0]?.TripNumber ?? "").Trim() : "";
                    var route = AddClusterRouteToOverlay(overlay, g.RoutePolyline, TripLegRouteName(tn),
                        groupColor, g.IsStraightLineFallback, thick: true);
                    RegisterTripLegRoute(tn, route, groupColor, 4f, g.IsStraightLineFallback);
                }
                else
                {
                    if (g.Trips.Count > 1)
                    {
                        AddClusterRouteToOverlay(overlay, g.RoutePolyline, "group-tour",
                            groupColor, g.IsStraightLineFallback, thick: true);
                    }

                    foreach (var leg in g.TripLegPolylines)
                    {
                        if (leg?.Points == null || leg.Points.Count < 2)
                            continue;
                        string legName = TripLegRouteName(leg.TripNumber);
                        var route = AddClusterRouteToOverlay(overlay, leg.Points, legName,
                            groupColor, leg.IsStraightLineFallback, thick: false);
                        RegisterTripLegRoute(leg.TripNumber, route, groupColor, 2.5f, leg.IsStraightLineFallback);
                    }

                    // Fallback when leg build was skipped but geocodes exist.
                    if (g.TripLegPolylines.Count == 0 && g.Trips.Count == 1)
                    {
                        var fallback = new List<GeoPoint>();
                        if (g.PickupPoints.Count > 0 && g.DropoffPoints.Count > 0)
                        {
                            fallback.Add(g.PickupPoints[0]);
                            fallback.Add(g.DropoffPoints[0]);
                        }
                        string tn = (g.Trips[0]?.TripNumber ?? "").Trim();
                        var route = AddClusterRouteToOverlay(overlay, fallback, TripLegRouteName(tn),
                            groupColor, straightFallback: true, thick: false);
                        RegisterTripLegRoute(tn, route, groupColor, 2.5f, straightFallback: true);
                    }
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
                    RegisterTripMarker(trip, marker, overlay);
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
                    RegisterTripMarker(trip, marker, overlay);
                }

                _groupOverlays[g.GroupNumber] = overlay;
                _map.Overlays.Add(overlay);
                if (!TripFlatMapMode)
                    AddLegendRow(g);
            }

            if (_homeOverlay != null)
                _map.Overlays.Add(_homeOverlay);

            EnsureSelectionRouteOverlayOnTop();

            if (!TripFlatMapMode)
            {
                AddDeadheadToggle();
                ApplyLegendVisibility(plan, legendSnap);
                if (legendSnap == null)
                    ApplyDefaultLegendVisibility();
                RelayoutLegendRows();
            }
            else
            {
                ClearLegend();
            }

            EnsureGroupOverlaysEnabled();

            SyncSelectionRouteOverlay();
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
                DriverKey = PlanTabKey(_currentPlan),
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

        private static string PlanTabKey(SupeyDriverPlan plan)
        {
            if (plan?.Driver == null)
                return "";
            string key = (plan.Driver.ScheduleTabKey ?? "").Trim();
            if (key.Length > 0)
                return key;
            return (plan.Driver.Name ?? "").Trim();
        }

        private LegendVisibilitySnapshot ResolveLegendSnapshot(SupeyDriverPlan plan)
        {
            string tabKey = PlanTabKey(plan);
            if (tabKey.Length > 0
                && _legendSnapByTabKey.TryGetValue(tabKey, out var saved))
            {
                return saved;
            }

            return null;
        }

        /// <summary>Persist Group key checkbox state for a driver tab (Schedule Builder tab switches).</summary>
        public void SaveLegendSnapshotForTab(string tabKey)
        {
            tabKey = (tabKey ?? "").Trim();
            if (tabKey.Length == 0)
                return;

            var snap = CaptureLegendVisibility();
            if (snap == null)
                return;

            snap.DriverKey = tabKey;
            _legendSnapByTabKey[tabKey] = snap;
        }

        /// <summary>Save legend only when the loaded plan belongs to <paramref name="tabKey"/>.</summary>
        public void SaveLegendSnapshotForTabIfLoaded(string tabKey)
        {
            tabKey = (tabKey ?? "").Trim();
            if (tabKey.Length == 0 || _currentPlan == null)
                return;
            if (!string.Equals(PlanTabKey(_currentPlan), tabKey, StringComparison.OrdinalIgnoreCase))
                return;
            SaveLegendSnapshotForTab(tabKey);
        }

        private void PersistLegendSnapshotForCurrentPlan()
        {
            if (_currentPlan == null)
                return;
            string tabKey = PlanTabKey(_currentPlan);
            if (tabKey.Length == 0)
                return;

            var snap = CaptureLegendVisibility();
            if (snap == null)
                return;

            snap.DriverKey = tabKey;
            _legendSnapByTabKey[tabKey] = snap;
        }

        private void ApplyLegendDrivenGroupVisibility(int groupNumber, out bool showOverlay, out bool showRoute)
        {
            if (!TripFlatMapMode && _groupCheckboxes.TryGetValue(groupNumber, out var legendCb))
            {
                showOverlay = legendCb.Checked;
                showRoute = legendCb.Checked;
            }
            else
            {
                showOverlay = true;
                showRoute = true;
            }
        }

        private bool ApplyLegendDrivenTripLegVisibility(int groupNumber, string tripNumber)
        {
            if (!TripFlatMapMode && _groupCheckboxes.TryGetValue(groupNumber, out var legendCb))
                return legendCb.Checked;
            return true;
        }

        private void ApplyDefaultLegendVisibility()
        {
            _deferSelectionOverlaySync = true;
            try
            {
                foreach (var kv in _groupOverlays)
                    ApplyGroupLegendVisibility(kv.Key, true);
            }
            finally
            {
                _deferSelectionOverlaySync = false;
            }

            SyncSelectionRouteOverlay();
        }

        private static bool ComputeGroupShownFromSnapshot(SupeyTripCluster g, LegendVisibilitySnapshot snap)
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
            return show;
        }

        private void RestoreLegendCheckboxesFromSnapshot(LegendVisibilitySnapshot snap)
        {
            if (snap == null || _currentPlan == null || _groupCheckboxes.Count == 0)
                return;

            string tabKey = PlanTabKey(_currentPlan);
            if (tabKey.Length == 0 || !string.Equals(tabKey, snap.DriverKey, StringComparison.OrdinalIgnoreCase))
                return;

            _syncingLegend = true;
            try
            {
                foreach (var g in _currentPlan.Groups)
                {
                    bool show = ComputeGroupShownFromSnapshot(g, snap);
                    if (_groupCheckboxes.TryGetValue(g.GroupNumber, out var cb))
                        cb.Checked = show;
                }
            }
            finally
            {
                _syncingLegend = false;
            }
        }

        private void ApplyLegendVisibility(SupeyDriverPlan plan, LegendVisibilitySnapshot snap)
        {
            if (snap == null || plan == null || _groupCheckboxes.Count == 0)
                return;

            string tabKey = PlanTabKey(plan);
            if (tabKey.Length == 0 || !string.Equals(tabKey, snap.DriverKey, StringComparison.OrdinalIgnoreCase))
                return;

            if (snap.DeadheadChecked.HasValue && _deadheadToggle != null)
            {
                _deadheadToggle.Checked = snap.DeadheadChecked.Value;
                if (_deadheadOverlay != null)
                    _deadheadOverlay.IsVisibile = snap.DeadheadChecked.Value;
            }

            RestoreLegendFromSnapshot(plan, snap);
            UpdateSelectionHiddenHint();
            _map.Refresh();
        }

        private void RestoreLegendFromSnapshot(SupeyDriverPlan plan, LegendVisibilitySnapshot snap)
        {
            _deferSelectionOverlaySync = true;
            try
            {
                foreach (var g in plan.Groups)
                    SetGroupVisible(g.GroupNumber, ComputeGroupShownFromSnapshot(g, snap));
            }
            finally
            {
                _deferSelectionOverlaySync = false;
            }

            SyncSelectionRouteOverlay();
            PersistLegendSnapshotForCurrentPlan();
        }

        private void ApplyGroupLegendVisibility(int groupNumber, bool visible)
        {
            if (_groupOverlays.TryGetValue(groupNumber, out var overlay))
            {
                overlay.IsVisibile = true;
                foreach (var route in overlay.Routes)
                {
                    if (route == null)
                        continue;
                    if (!visible && IsTripLegRouteName(route.Name))
                    {
                        string tn = route.Name.Substring("trip-leg:".Length);
                        if (_highlightedTripNumbers.Contains(tn))
                        {
                            route.IsVisible = false;
                            continue;
                        }
                    }
                    else if (visible && IsTripLegRouteName(route.Name))
                    {
                        string tn = route.Name.Substring("trip-leg:".Length);
                        if (_highlightedTripNumbers.Contains(tn))
                            continue;
                    }
                    route.IsVisible = visible;
                }
            }

            if (MapDisplayFilter == FsMapDisplayMode.AllDriverTrips)
                _filterGroupTourVisible[groupNumber] = visible;
            else if (!visible)
                _filterGroupTourVisible[groupNumber] = false;

            SyncTripMarkerVisibilityForGroup(groupNumber, visible);
            UpdateTripLegFilterForGroup(groupNumber, visible);
            if (!_deferSelectionOverlaySync)
                SyncSelectionRouteOverlay();
        }

        private void SetGroupVisible(int groupNumber, bool visible)
        {
            _syncingLegend = true;
            try
            {
                if (_groupCheckboxes.TryGetValue(groupNumber, out var cb) && cb.Checked != visible)
                    cb.Checked = visible;
            }
            finally
            {
                _syncingLegend = false;
            }

            ApplyGroupLegendVisibility(groupNumber, visible);
        }

        private void SyncTripMarkerVisibilityForGroup(int groupNumber, bool visible)
        {
            if (_currentPlan?.Groups == null)
                return;
            foreach (var g in _currentPlan.Groups)
            {
                if (g.GroupNumber != groupNumber || g.Trips == null)
                    continue;
                foreach (var t in g.Trips)
                {
                    string tn = (t?.TripNumber ?? "").Trim();
                    if (tn.Length == 0 || !_tripMarkers.TryGetValue(tn, out var markers))
                        continue;
                    foreach (var marker in markers)
                        marker.IsVisible = visible;
                }
            }
        }

        private bool IsTripLegendVisible(string tripNumber)
        {
            if (string.IsNullOrWhiteSpace(tripNumber) || _currentPlan?.Groups == null)
                return true;
            string key = tripNumber.Trim();
            foreach (var g in _currentPlan.Groups)
            {
                if (g.Trips == null)
                    continue;
                foreach (var t in g.Trips)
                {
                    if (!string.Equals((t?.TripNumber ?? "").Trim(), key, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (_groupCheckboxes.TryGetValue(g.GroupNumber, out var cb))
                        return cb.Checked;
                    return IsGroupLegendVisible(g.GroupNumber);
                }
            }
            return true;
        }

        private Color ResolveGroupDisplayColor(SupeyTripCluster g)
        {
            if (g == null) return NeutralRouteColor;
            if (!UseGroupRouteColors)
                return NeutralRouteColor;
            return g.DisplayColor;
        }

        private void AddLegendRow(SupeyTripCluster g)
        {
            Color groupColor = ResolveGroupDisplayColor(g);
            int rowWidth = GetLegendRowWidth();
            var row = new Panel
            {
                Width = rowWidth,
                Height = 40,
                BackColor = SupeyTheme.Surface,
                Margin = new Padding(0, 0, 0, 2),
            };

            var cb = new CheckBox
            {
                Checked = true,
                Width = 20,
                Height = 20,
                Location = new Point(0, 10),
                Tag = g.GroupNumber,
                BackColor = SupeyTheme.Surface,
            };
            cb.CheckedChanged += OnGroupChecked;

            var swatch = new Panel
            {
                BackColor = groupColor,
                Width = 4,
                Height = 26,
                Location = new Point(24, 7),
            };

            double routeMi = g.IntraClusterMeters * 0.000621371;
            string miText = routeMi > 0.05 ? routeMi.ToString("0.0") + " mi" : "";
            string riders = g.RiderCount + (g.RiderCount == 1 ? " rider" : " riders");
            string timeText = SupeyTripTimes.FormatTimeOfDay(g.EarliestPickup);
            string detail = riders + " · " + timeText
                + (string.IsNullOrEmpty(miText) ? "" : " · " + miText)
                + (g.IsStraightLineFallback ? " · est." : "");

            var titleLbl = new Label
            {
                Text = "Group " + g.GroupNumber,
                AutoSize = false,
                Location = new Point(34, 6),
                Size = new Size(rowWidth - 38, 16),
                ForeColor = SupeyTheme.TextPrimary,
                Font = GroupKeyTitleFont,
                TextAlign = ContentAlignment.MiddleLeft,
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right,
                AutoEllipsis = true,
            };
            var detailLbl = new Label
            {
                Text = detail,
                AutoSize = false,
                Location = new Point(34, 22),
                Size = new Size(rowWidth - 38, 14),
                ForeColor = SupeyTheme.TextMuted,
                Font = GroupKeyDetailFont,
                TextAlign = ContentAlignment.MiddleLeft,
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right,
                AutoEllipsis = true,
            };

            row.Controls.Add(cb);
            row.Controls.Add(swatch);
            row.Controls.Add(titleLbl);
            row.Controls.Add(detailLbl);
            _legend.Controls.Add(row);
            _groupCheckboxes[g.GroupNumber] = cb;
        }

        private void AddDeadheadToggle()
        {
            _legendFooter.Controls.Clear();
            _deadheadToggle = new CheckBox
            {
                Text = "Dead-head connectors",
                Checked = true,
                Dock = DockStyle.Fill,
                ForeColor = SupeyTheme.TextPrimary,
                BackColor = SupeyTheme.Surface,
                Font = new Font("Segoe UI", 9f),
                Padding = new Padding(2, 0, 0, 0),
            };
            _deadheadToggle.CheckedChanged += (s, e) =>
            {
                if (_deadheadOverlay == null) return;
                _deadheadOverlay.IsVisibile = _deadheadToggle.Checked;
                _map.Refresh();
            };
            _legendFooter.Controls.Add(_deadheadToggle);
        }

        private bool _syncingLegend;

        /// <summary>Active list-driven map filter; legend clicks are ignored while filtered.</summary>
        public FsMapDisplayMode MapDisplayFilter { get; private set; } = FsMapDisplayMode.SelectedGroup;

        private static GMapRoute AddClusterRouteToOverlay(
            GMapOverlay overlay,
            IList<GeoPoint> polyline,
            string routeName,
            Color groupColor,
            bool straightFallback,
            bool thick)
        {
            if (overlay == null || polyline == null || polyline.Count < 2)
                return null;

            var pts = new List<PointLatLng>(polyline.Count);
            foreach (var p in polyline)
                pts.Add(new PointLatLng(p.Lat, p.Lng));

            var route = new GMapRoute(pts, routeName)
            {
                Stroke = new Pen(groupColor, thick ? 4f : 2.5f)
                {
                    DashStyle = straightFallback ? DashStyle.Dash : DashStyle.Solid,
                    LineJoin = LineJoin.Round,
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                },
            };
            overlay.Routes.Add(route);
            return route;
        }

        /// <summary>Show every group overlay/route and check all legend rows (Schedule Builder "all driver trips").</summary>
        public void ShowAllGroups(bool syncLegendOnly = false)
        {
            _syncingLegend = true;
            _deferSelectionOverlaySync = true;
            try
            {
                foreach (var kv in _groupCheckboxes)
                    kv.Value.Checked = true;

                if (syncLegendOnly)
                    return;

                EnsureGroupOverlaysEnabled();
                foreach (var kv in _groupOverlays)
                    ApplyGroupLegendVisibility(kv.Key, true);
            }
            finally
            {
                _deferSelectionOverlaySync = false;
                _syncingLegend = false;
            }

            if (!syncLegendOnly)
            {
                SyncSelectionRouteOverlay();
                PersistLegendSnapshotForCurrentPlan();
                _map.Refresh();
            }
        }

        private void OnGroupChecked(object sender, EventArgs e)
        {
            if (_syncingLegend)
                return;
            var cb = sender as CheckBox;
            if (cb == null || !(cb.Tag is int groupNumber)) return;

            // Group key only filters in all-driver mode; other toolbar modes drive visibility.
            if (MapDisplayFilter != FsMapDisplayMode.AllDriverTrips)
            {
                _syncingLegend = true;
                try
                {
                    cb.Checked = _filterGroupOverlayVisible.TryGetValue(groupNumber, out bool shown) && shown;
                }
                finally
                {
                    _syncingLegend = false;
                }
                return;
            }

            ApplyGroupLegendVisibility(groupNumber, cb.Checked);
            _filterGroupOverlayVisible[groupNumber] = cb.Checked;
            PersistLegendSnapshotForCurrentPlan();
            UpdateSelectionHiddenHint();
            _map.Refresh();
        }

        private void UpdateTripLegFilterForGroup(int groupNumber, bool groupVisible)
        {
            if (_currentPlan?.Groups == null)
                return;
            foreach (var g in _currentPlan.Groups)
            {
                if (g.GroupNumber != groupNumber || g.Trips == null)
                    continue;
                foreach (var t in g.Trips)
                {
                    string tn = (t?.TripNumber ?? "").Trim();
                    if (tn.Length > 0 && _tripLegRoutes.TryGetValue(tn, out var visual))
                        visual.FilterVisible = groupVisible;
                }
            }
        }

        /// <summary>
        /// Filters overlays/markers without rebuilding the plan. Used by Schedule Builder list
        /// display modes (all driver trips, selected group, selected trips).
        /// </summary>
        public void ApplyMapDisplayFilter(
            FsMapDisplayMode mode,
            int? selectedGroupNumber,
            IReadOnlyCollection<string> selectedTripNumbers,
            bool autoFit = true)
        {
            FsMapDisplayMode previousFilter = MapDisplayFilter;

            if (previousFilter == FsMapDisplayMode.AllDriverTrips && mode != FsMapDisplayMode.AllDriverTrips)
                PersistLegendSnapshotForCurrentPlan();

            MapDisplayFilter = mode;
            if (_currentPlan == null)
                return;

            if (mode == FsMapDisplayMode.AllDriverTrips && previousFilter != FsMapDisplayMode.AllDriverTrips)
                ShowAllGroups();

            _filterSelectedGroupNumber = selectedGroupNumber;
            _filterSelectedTrips.Clear();
            _filterGroupTourVisible.Clear();
            _filterGroupOverlayVisible.Clear();

            var selectedTrips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (selectedTripNumbers != null)
            {
                foreach (var tn in selectedTripNumbers)
                {
                    if (!string.IsNullOrWhiteSpace(tn))
                        selectedTrips.Add(tn.Trim());
                }
            }

            foreach (var tn in selectedTrips)
                _filterSelectedTrips.Add(tn);

            foreach (var g in _currentPlan.Groups)
            {
                int gn = g.GroupNumber;
                if (!_groupOverlays.TryGetValue(gn, out var overlay))
                    continue;

                bool showOverlay;
                bool showRoute;
                switch (mode)
                {
                    case FsMapDisplayMode.SelectedGroup:
                        if (!selectedGroupNumber.HasValue)
                            ApplyLegendDrivenGroupVisibility(gn, out showOverlay, out showRoute);
                        else
                        {
                            showOverlay = gn == selectedGroupNumber.Value;
                            showRoute = showOverlay;
                        }
                        break;
                    case FsMapDisplayMode.SelectedTrips:
                        if (selectedTrips.Count == 0)
                        {
                            ApplyLegendDrivenGroupVisibility(gn, out showOverlay, out showRoute);
                        }
                        else
                        {
                            int tripsInGroup = g.Trips?.Count ?? 0;
                            int selectedInGroup = 0;
                            if (g.Trips != null)
                            {
                                foreach (var t in g.Trips)
                                {
                                    string key = (t?.TripNumber ?? "").Trim();
                                    if (key.Length > 0 && selectedTrips.Contains(key))
                                        selectedInGroup++;
                                }
                            }
                            showOverlay = selectedInGroup > 0;
                            showRoute = showOverlay && (TripFlatMapMode || selectedInGroup >= tripsInGroup);
                        }
                        break;
                    default:
                        ApplyLegendDrivenGroupVisibility(gn, out showOverlay, out showRoute);
                        break;
                }

                _filterGroupOverlayVisible[gn] = showOverlay;
                overlay.IsVisibile = true;
                foreach (var route in overlay.Routes)
                {
                    bool isTripLeg = route.Name != null
                        && route.Name.StartsWith("trip-leg:", StringComparison.OrdinalIgnoreCase);
                    if (isTripLeg)
                    {
                        string tn = route.Name.Substring("trip-leg:".Length);
                        bool showLeg;
                        switch (mode)
                        {
                            case FsMapDisplayMode.SelectedTrips:
                                if (selectedTrips.Count == 0)
                                    showLeg = ApplyLegendDrivenTripLegVisibility(gn, tn);
                                else
                                    showLeg = tn.Length > 0 && selectedTrips.Contains(tn);
                                break;
                            case FsMapDisplayMode.SelectedGroup:
                                if (!selectedGroupNumber.HasValue)
                                    showLeg = ApplyLegendDrivenTripLegVisibility(gn, tn);
                                else
                                    showLeg = showOverlay;
                                break;
                            default:
                                showLeg = ApplyLegendDrivenTripLegVisibility(gn, tn);
                                break;
                        }
                        bool legVisible = showLeg && showOverlay;
                        if (_tripLegRoutes.TryGetValue(tn, out var legVisual))
                            legVisual.FilterVisible = legVisible;
                        route.IsVisible = legVisible;
                    }
                    else
                    {
                        bool tourVisible = showRoute && showOverlay;
                        _filterGroupTourVisible[gn] = tourVisible;
                        route.IsVisible = tourVisible;
                    }
                }
            }

            foreach (var kv in _tripMarkers)
            {
                bool showMarker;
                if (mode == FsMapDisplayMode.AllDriverTrips)
                    showMarker = IsTripLegendVisible(kv.Key);
                else if (mode == FsMapDisplayMode.SelectedGroup)
                    showMarker = TripBelongsToGroup(kv.Key, selectedGroupNumber);
                else
                    showMarker = selectedTrips.Contains(kv.Key);

                foreach (var marker in kv.Value)
                    marker.IsVisible = showMarker;
            }

            if (_homeOverlay != null)
                _homeOverlay.IsVisibile = mode != FsMapDisplayMode.SelectedTrips;

            if (_deadheadOverlay != null)
            {
                bool allowDeadhead = mode == FsMapDisplayMode.AllDriverTrips && !TripFlatMapMode;
                _deadheadOverlay.IsVisibile = allowDeadhead && (_deadheadToggle?.Checked ?? true);
            }

            if (mode != FsMapDisplayMode.AllDriverTrips
                && !TripFlatMapMode
                && _groupCheckboxes.Count > 0)
            {
                bool allTripsSelected = AllPlanTripsAreSelected(selectedTrips);

                _syncingLegend = true;
                try
                {
                    foreach (var kv in _groupCheckboxes)
                    {
                        bool shown = allTripsSelected
                            || (_filterGroupOverlayVisible.TryGetValue(kv.Key, out bool vis) && vis);
                        kv.Value.Checked = shown;
                    }
                }
                finally
                {
                    _syncingLegend = false;
                }

                if (allTripsSelected)
                {
                    foreach (var kv in _groupOverlays)
                        ApplyGroupLegendVisibility(kv.Key, true);
                }
            }

            EnsureGroupOverlaysEnabled();

            if (autoFit)
                FitToVisibleMapContent(mode, selectedGroupNumber, selectedTrips);

            _map.Refresh();
        }

        private bool TripBelongsToGroup(string tripNumber, int? groupNumber)
        {
            if (!groupNumber.HasValue || string.IsNullOrWhiteSpace(tripNumber) || _currentPlan?.Groups == null)
                return false;
            foreach (var g in _currentPlan.Groups)
            {
                if (g.GroupNumber != groupNumber.Value)
                    continue;
                if (g.Trips == null)
                    continue;
                foreach (var t in g.Trips)
                {
                    if (string.Equals((t?.TripNumber ?? "").Trim(), tripNumber.Trim(), StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            return false;
        }

        private bool AllPlanTripsAreSelected(IReadOnlyCollection<string> selectedTripNumbers)
        {
            if (_currentPlan?.Groups == null || selectedTripNumbers == null || selectedTripNumbers.Count == 0)
                return false;

            var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tn in selectedTripNumbers)
            {
                if (!string.IsNullOrWhiteSpace(tn))
                    selected.Add(tn.Trim());
            }

            if (selected.Count == 0)
                return false;

            int planTrips = 0;
            foreach (var g in _currentPlan.Groups)
            {
                if (g.Trips == null)
                    continue;
                foreach (var t in g.Trips)
                {
                    string tn = (t?.TripNumber ?? "").Trim();
                    if (tn.Length == 0)
                        continue;
                    planTrips++;
                    if (!selected.Contains(tn))
                        return false;
                }
            }

            return planTrips > 0;
        }

        private void FitToVisibleMapContent(
            FsMapDisplayMode mode,
            int? selectedGroupNumber,
            HashSet<string> selectedTrips)
        {
            if (mode == FsMapDisplayMode.AllDriverTrips)
            {
                FitToPlan();
                return;
            }

            var pts = new List<PointLatLng>();
            if (mode == FsMapDisplayMode.SelectedGroup
                && _currentPlan?.HomeGeo is GeoPoint home
                && IsValidGeoPoint(home))
            {
                pts.Add(new PointLatLng(home.Lat, home.Lng));
            }

            foreach (var kv in _tripMarkers)
            {
                bool include = mode == FsMapDisplayMode.SelectedGroup
                    ? TripBelongsToGroup(kv.Key, selectedGroupNumber)
                    : selectedTrips.Contains(kv.Key);
                if (!include)
                    continue;
                foreach (var marker in kv.Value)
                {
                    if (marker.IsVisible)
                        pts.Add(marker.Position);
                }
            }

            FitPoints(pts, zoomSingle: 14, zoomMulti: 11);
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
