using System;
using System.Collections.Generic;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Detect and repair driver-day group / row order regressions after merge or harmonize.</summary>
    internal static class SupeyScheduleOrderAudit
    {
        internal static bool GroupsOutOfChronologicalOrder(SupeyDriverPlan plan)
        {
            if (plan?.Groups == null || plan.Groups.Count < 2) return false;
            for (int i = 1; i < plan.Groups.Count; i++)
            {
                var prev = plan.Groups[i - 1];
                var curr = plan.Groups[i];
                if (prev == null || curr == null) continue;
                if (SupeyClusterTimeSplit.MinPickupTime(prev) > SupeyClusterTimeSplit.MinPickupTime(curr))
                    return true;
            }
            return false;
        }

        internal static bool AnyClusterRowsOutOfPuOrder(SupeyDriverPlan plan)
        {
            if (plan?.Groups == null) return false;
            foreach (var g in plan.Groups)
            {
                if (g?.Trips == null || g.Trips.Count < 2) continue;
                TimeSpan prev = TimeSpan.MinValue;
                foreach (var t in g.Trips)
                {
                    var pu = SupeyTripTimes.TryParsePU(t);
                    if (!pu.HasValue) continue;
                    if (prev != TimeSpan.MinValue && pu.Value < prev)
                        return true;
                    prev = pu.Value;
                }
            }
            return false;
        }

        /// <summary>Re-sort groups and desk PU row order. Returns true if a repair was applied.</summary>
        internal static bool RepairPlanOrder(SupeyDriverPlan plan)
        {
            if (plan == null) return false;
            bool bad = GroupsOutOfChronologicalOrder(plan) || AnyClusterRowsOutOfPuOrder(plan);
            if (!bad) return false;
            SupeyClusterTimeSplit.SortGroupsByEarliestPickup(plan);
            SupeyScheduleDeskOrder.ApplyToPlan(plan);
            return true;
        }

        internal static List<string> DescribeViolations(SupeyDriverPlan plan)
        {
            var list = new List<string>();
            if (plan?.Groups == null) return list;
            for (int i = 1; i < plan.Groups.Count; i++)
            {
                var prev = plan.Groups[i - 1];
                var curr = plan.Groups[i];
                if (prev == null || curr == null) continue;
                var tPrev = SupeyClusterTimeSplit.MinPickupTime(prev);
                var tCurr = SupeyClusterTimeSplit.MinPickupTime(curr);
                if (tPrev > tCurr)
                {
                    list.Add("Group " + prev.GroupNumber + " (" + SupeyTripTimes.FormatTimeOfDay(tPrev)
                        + ") before Group " + curr.GroupNumber + " ("
                        + SupeyTripTimes.FormatTimeOfDay(tCurr) + ")");
                }
            }
            return list;
        }
    }
}
