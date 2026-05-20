using System;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Context for a Supey map PU/DO marker (geocode fix flow).</summary>
    internal sealed class SupeyMapMarkerInfo
    {
        public MCDownloadedTrip Trip { get; set; }
        public string EndpointLabel { get; set; }
        public bool IsPickup { get; set; }
        public string Street { get; set; }
        public string City { get; set; }
        public string State { get; set; } = "ME";
        public string Zip { get; set; }
        public Action<GeoPoint> OnPinSaved { get; set; }
    }
}
