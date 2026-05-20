using System;
using System.Drawing;
using System.Windows.Forms;
using GMap.NET;
using GMap.NET.MapProviders;
using GMap.NET.WindowsForms;
using GMap.NET.WindowsForms.Markers;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Move a pin and save the corrected geocode to the company AI server cache.</summary>
    internal sealed class SupeyGeocodeFixForm : Form
    {
        private readonly SupeyDraggableMarker _marker;
        private readonly GMapControl _map;
        private readonly Label _addrLbl;
        private readonly SupeyMapMarkerInfo _info;

        public SupeyGeocodeFixForm(SupeyMapMarkerInfo info, GeoPoint initial)
        {
            _info = info ?? throw new ArgumentNullException(nameof(info));
            Text = "Fix geocode — " + (info.EndpointLabel ?? "stop");
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(720, 520);
            BackColor = Color.FromArgb(40, 40, 40);
            ForeColor = Color.Gainsboro;
            MinimumSize = new Size(560, 420);

            GMapInitializer.EnsureInitialized();

            _addrLbl = new Label
            {
                Dock = DockStyle.Top,
                Height = 44,
                Padding = new Padding(12, 8, 12, 4),
                ForeColor = Color.Gainsboro,
                BackColor = Color.FromArgb(35, 35, 35),
                Font = new Font("Segoe UI", 9.5f),
                Text = FormatAddress(),
            };

            var hint = new Label
            {
                Dock = DockStyle.Top,
                Height = 28,
                Text = "Drag the pin to the correct spot, then click Save for this company-wide.",
                ForeColor = Color.Silver,
                BackColor = Color.FromArgb(35, 35, 35),
                Padding = new Padding(12, 0, 12, 0),
                Font = new Font("Segoe UI", 8.75f),
            };

            _map = new GMapControl
            {
                Dock = DockStyle.Fill,
                MapProvider = GMapProviders.OpenStreetMap,
                MinZoom = 5,
                MaxZoom = 18,
                Zoom = 14,
                CanDragMap = true,
                MouseWheelZoomEnabled = true,
                DragButton = MouseButtons.Right,
            };
            var start = new PointLatLng(initial.Lat, initial.Lng);
            _map.Position = start;

            var overlay = new GMapOverlay("fix");
            _marker = new SupeyDraggableMarker(start, GMarkerGoogleType.yellow_pushpin)
            {
                ToolTipText = info.EndpointLabel ?? "Drag me",
                ToolTipMode = MarkerTooltipMode.Always,
            };
            overlay.Markers.Add(_marker);
            _map.Overlays.Add(overlay);
            SupeyMapMarkerDrag.EnsureWired(_map);

            var bar = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 48,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8),
                BackColor = Color.FromArgb(35, 35, 35),
            };
            var cancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                AutoSize = true,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.Gainsboro,
                BackColor = Color.FromArgb(55, 55, 55),
            };
            var save = new Button
            {
                Text = "Save pin for this address",
                AutoSize = true,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(46, 125, 50),
            };
            save.Click += OnSaveClicked;
            bar.Controls.Add(cancel);
            bar.Controls.Add(save);

            Controls.Add(_map);
            Controls.Add(bar);
            Controls.Add(hint);
            Controls.Add(_addrLbl);
            CancelButton = cancel;
            AcceptButton = save;
        }

        private string FormatAddress()
        {
            var parts = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrWhiteSpace(_info.Street)) parts.Add(_info.Street.Trim());
            if (!string.IsNullOrWhiteSpace(_info.City)) parts.Add(_info.City.Trim());
            if (!string.IsNullOrWhiteSpace(_info.State)) parts.Add(_info.State.Trim());
            return parts.Count > 0 ? string.Join(", ", parts) : "(address)";
        }

        private async void OnSaveClicked(object sender, EventArgs e)
        {
            var btn = sender as Button;
            if (btn != null) btn.Enabled = false;
            try
            {
                double lat = _marker.Position.Lat;
                double lng = _marker.Position.Lng;
                var pt = new GeoPoint(lat, lng);
                await AddressGeocoder.ConfirmPinAsync(
                    _info.Street,
                    _info.City,
                    _info.State,
                    _info.Zip,
                    pt).ConfigureAwait(true);
                _info.OnPinSaved?.Invoke(pt);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "Could not save geocode:\n\n" + ex.Message,
                    "Fix geocode",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            finally
            {
                if (btn != null) btn.Enabled = true;
            }
        }
    }
}
