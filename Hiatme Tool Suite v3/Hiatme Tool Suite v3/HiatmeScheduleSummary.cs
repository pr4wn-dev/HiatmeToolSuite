using System.Linq;
using System.Text;

namespace Hiatme_Tool_Suite_v3
{
    internal static class HiatmeScheduleSummary
    {
        public static string ForMemory(SupeyScheduleResult result)
        {
            if (result == null) return "";
            int trips = 0;
            foreach (var p in result.DriverPlans)
            {
                foreach (var g in p.Groups)
                    trips += g.Trips.Count;
            }
            trips += result.Reserves.Count;
            var sb = new StringBuilder();
            sb.Append("Approved schedule ");
            sb.Append(result.ServiceDate.ToString("yyyy-MM-dd"));
            sb.Append(": ");
            sb.Append(result.DriverPlans.Count);
            sb.Append(" drivers, ");
            sb.Append(trips);
            sb.Append(" trips (");
            sb.Append(result.Reserves.Count);
            sb.Append(" reserves).");
            return sb.ToString();
        }
    }
}
