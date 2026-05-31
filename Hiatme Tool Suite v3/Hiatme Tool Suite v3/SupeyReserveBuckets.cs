using System;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Routes unassigned trips into Will calls, Reservers, or Reroutes lists on <see cref="SupeyScheduleResult"/>.</summary>
    internal static class SupeyReserveBuckets
    {
        public static bool IsWillCall(MCDownloadedTrip t)
        {
            if (t == null) return false;
            string pu = (t.PUTime ?? "").Replace(" ", "");
            return pu == "00:00" || pu == "00:00:00" || pu == "12:00AM" || pu == "12:00:00AM";
        }

        public static void AddToReserves(SupeyScheduleResult result, MCDownloadedTrip trip)
        {
            if (result == null || trip == null) return;
            RemoveFromAll(result, trip);
            if (SupeyOutOfArea.MatchTrip(trip) != null)
                result.ReservesReroute.Add(trip);
            else if (IsWillCall(trip))
                result.ReservesWillCalls.Add(trip);
            else
                result.Reserves.Add(trip);
        }

        public static void RemoveFromAll(SupeyScheduleResult result, MCDownloadedTrip trip)
        {
            if (result == null || trip == null) return;
            result.Reserves.Remove(trip);
            result.ReservesReroute.Remove(trip);
            result.ReservesWillCalls.Remove(trip);
        }
    }
}
