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

        /// <summary>
        /// Next group starts (first PU) before previous group's last DO appointment — impossible even with zero drive time.
        /// </summary>
        internal static bool GroupsHaveScheduledOverlap(SupeyDriverPlan plan)
        {
            if (plan?.Groups == null || plan.Groups.Count < 2) return false;
            for (int i = 1; i < plan.Groups.Count; i++)
            {
                var prev = plan.Groups[i - 1];
                var curr = plan.Groups[i];
                if (prev == null || curr == null) continue;
                if (SupeyClusterTimeSplit.MinPickupTime(curr) < SupeyClusterTimeSplit.MaxDropoffTime(prev))
                    return true;
            }
            return false;
        }

        internal static bool PlanNeedsGroupOrderRepair(SupeyDriverPlan plan) =>
            GroupsOutOfChronologicalOrder(plan) || GroupsHaveScheduledOverlap(plan);

        internal static bool AnyClusterRowsOutOfPuOrder(SupeyDriverPlan plan)
        {
            if (plan?.Groups == null) return false;
            foreach (var g in plan.Groups)
            {
                if (g?.Trips == null || g.Trips.Count < 2) continue;
                if (SupeyClusterRouting.IsValidVisitOrder(g.PickupOrder, g.Trips.Count)
                    && g.RoadTourOptimized)
                    continue;
                if (!SupeyClusterDisplayOrder.TripsRowsInPuOrder(g))
                    return true;
            }
            return false;
        }

        internal static bool AnyClusterRowsOutOfDisplayOrder(SupeyDriverPlan plan)
        {
            if (plan?.Groups == null) return false;
            foreach (var g in plan.Groups)
            {
                if (g?.Trips == null || g.Trips.Count < 2) continue;
                if (SupeyClusterDisplayOrder.ClusterRowsNeedReorder(g))
                    return true;
            }
            return false;
        }

        internal static bool PlanNeedsOrderRepair(SupeyDriverPlan plan) =>
            PlanNeedsGroupOrderRepair(plan) || AnyClusterRowsOutOfDisplayOrder(plan);

        /// <summary>Sync trip rows to road/desk visit order (does not fix inter-group sequence).</summary>
        internal static bool RepairPlanRowOrder(SupeyDriverPlan plan)
        {
            if (plan == null) return false;
            if (!AnyClusterRowsOutOfDisplayOrder(plan)) return false;
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

                var tPrevPu = SupeyClusterTimeSplit.MinPickupTime(prev);
                var tCurrPu = SupeyClusterTimeSplit.MinPickupTime(curr);
                if (tPrevPu > tCurrPu)
                {
                    list.Add("Group " + prev.GroupNumber + " (" + SupeyTripTimes.FormatTimeOfDay(tPrevPu)
                        + ") before Group " + curr.GroupNumber + " ("
                        + SupeyTripTimes.FormatTimeOfDay(tCurrPu) + ")");
                }

                var tPrevDo = SupeyClusterTimeSplit.MaxDropoffTime(prev);
                if (tCurrPu < tPrevDo)
                {
                    list.Add("Group " + prev.GroupNumber + " → Group " + curr.GroupNumber
                        + " impossible on sheet: Group " + curr.GroupNumber + " first PU "
                        + SupeyTripTimes.FormatTimeOfDay(tCurrPu) + " before Group " + prev.GroupNumber
                        + " last DO " + SupeyTripTimes.FormatTimeOfDay(tPrevDo));
                }
            }

            foreach (var g in plan.Groups)
            {
                if (g == null || g.Trips.Count < 2) continue;
                if (!SupeyClusterDisplayOrder.ClusterRowsNeedReorder(g)) continue;
                list.Add("Group " + g.GroupNumber + " trip rows do not match pickup visit order");
            }

            return list;
        }
    }
}
