namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Keep Trips / PU / DO parallel lists in sync (server JSON has trips only until geocode).</summary>
    internal static class SupeyTripClusterGeo
    {
        internal static void PadListsToTripCount(SupeyTripCluster c)
        {
            if (c == null) return;
            while (c.PickupPoints.Count < c.Trips.Count)
                c.PickupPoints.Add(new GeoPoint(0, 0));
            while (c.DropoffPoints.Count < c.Trips.Count)
                c.DropoffPoints.Add(new GeoPoint(0, 0));
        }

        internal static void InsertTripAt(SupeyTripCluster c, int insertAt, MCDownloadedTrip trip, GeoPoint pu, GeoPoint dof)
        {
            if (c == null || trip == null) return;
            PadListsToTripCount(c);
            if (insertAt < 0) insertAt = 0;
            if (insertAt > c.Trips.Count) insertAt = c.Trips.Count;
            c.Trips.Insert(insertAt, trip);
            c.PickupPoints.Insert(insertAt, pu);
            c.DropoffPoints.Insert(insertAt, dof);
        }
    }
}
