using System;

using System.Collections.Generic;



namespace Hiatme_Tool_Suite_v3

{

    /// <summary>Split ride-share groups when PU times span too long (A AM + B PM in one group).</summary>

    internal static class SupeyClusterTimeSplit

    {

        private static readonly TimeSpan MinGapToSplit = TimeSpan.FromMinutes(90);



        /// <summary>Split any group on the plan with a wide PU span (template, server, or post-harmonize).</summary>

        internal static void SplitWidePickupGroups(SupeyDriverPlan plan)

        {

            if (plan?.Groups == null || plan.Groups.Count == 0) return;



            int gi = 0;

            while (gi < plan.Groups.Count)

            {

                var g = plan.Groups[gi];

                if (g == null || g.Trips.Count < 2)

                {

                    gi++;

                    continue;

                }



                var parts = TrySplitCluster(g);

                if (parts == null || parts.Count < 2)

                {

                    gi++;

                    continue;

                }



                plan.Groups.RemoveAt(gi);

                plan.Groups.InsertRange(gi, parts);

                if (gi < plan.TemplateSeedGroupCount)

                    plan.TemplateSeedGroupCount += parts.Count - 1;

                gi += parts.Count;

            }



            RenumberGroups(plan);

        }



        internal static void SplitWideTemplateGroups(SupeyDriverPlan plan) =>

            SplitWidePickupGroups(plan);



        /// <summary>AM A legs and PM B legs never share one ride-share group.</summary>

        internal static void SplitMixedPartnerLegs(SupeyDriverPlan plan)

        {

            if (plan?.Groups == null || plan.Groups.Count == 0) return;



            int gi = 0;

            while (gi < plan.Groups.Count)

            {

                var g = plan.Groups[gi];

                if (g == null || g.Trips.Count < 2)

                {

                    gi++;

                    continue;

                }



                bool hasA = false, hasB = false;

                for (int i = 0; i < g.Trips.Count; i++)

                {

                    char leg = LegLetter(g.Trips[i]?.TripNumber);

                    if (leg == 'A') hasA = true;

                    if (leg == 'B') hasB = true;

                }

                if (!hasA || !hasB)

                {

                    gi++;

                    continue;

                }



                var aIdx = new List<int>();

                var bIdx = new List<int>();

                for (int i = 0; i < g.Trips.Count; i++)

                {

                    if (LegLetter(g.Trips[i]?.TripNumber) == 'B')

                        bIdx.Add(i);

                    else

                        aIdx.Add(i);

                }

                if (aIdx.Count == 0 || bIdx.Count == 0)

                {

                    gi++;

                    continue;

                }



                var parts = new List<SupeyTripCluster>(2);

                if (aIdx.Count > 0)

                    parts.Add(ExtractSubCluster(g, aIdx));

                if (bIdx.Count > 0)

                    parts.Add(ExtractSubCluster(g, bIdx));



                plan.Groups.RemoveAt(gi);

                plan.Groups.InsertRange(gi, parts);

                gi += parts.Count;

            }



            RenumberGroups(plan);

        }



        /// <summary>Run groups in clock order (fixes template slot order vs split B-leg inserts).</summary>

        internal static void SortGroupsByEarliestPickup(SupeyDriverPlan plan)

        {

            if (plan?.Groups == null || plan.Groups.Count < 2) return;

            plan.Groups.Sort((a, b) =>

            {

                int cmp = MinPickupTime(a).CompareTo(MinPickupTime(b));

                return cmp != 0 ? cmp : a.GroupNumber.CompareTo(b.GroupNumber);

            });

            RenumberGroups(plan);

        }



        /// <summary>Split wide-span / mixed-leg groups only — does not re-sort the day list.</summary>
        internal static void NormalizeSplitsOnly(SupeyDriverPlan plan)
        {
            if (plan == null) return;
            SplitWidePickupGroups(plan);
            SplitMixedPartnerLegs(plan);
        }

        internal static void NormalizeDayGroupOrder(SupeyDriverPlan plan)

        {

            if (plan == null) return;

            NormalizeSplitsOnly(plan);

            SortGroupsByEarliestPickup(plan);

        }



        /// <summary>PU order by clock, not OSRM, when A/B or AM/PM are mixed in one cluster.</summary>

        internal static bool NeedsChronologicalPickup(SupeyTripCluster g)

        {

            if (g?.Trips == null || g.Trips.Count < 2) return false;

            bool hasA = false, hasB = false;

            TimeSpan min = TimeSpan.MaxValue, max = TimeSpan.MinValue;

            foreach (var t in g.Trips)

            {

                char leg = LegLetter(t?.TripNumber);

                if (leg == 'A') hasA = true;

                if (leg == 'B') hasB = true;

                var pu = SupeyTripTimes.TryParsePU(t);

                if (!pu.HasValue) continue;

                if (pu.Value < min) min = pu.Value;

                if (pu.Value > max) max = pu.Value;

            }

            if (hasA && hasB) return true;

            if (min != TimeSpan.MaxValue && max != TimeSpan.MinValue

                && (max - min).TotalMinutes >= MinGapToSplit.TotalMinutes)

                return true;

            return false;

        }



        /// <summary>Partner leg must not land in a cluster whose PU window is far from this trip.</summary>

        internal static bool WouldExceedPickupGap(SupeyTripCluster g, MCDownloadedTrip incoming)

        {

            if (g?.Trips == null || g.Trips.Count == 0 || incoming == null) return false;

            var incomingPu = SupeyTripTimes.TryParsePU(incoming);

            if (!incomingPu.HasValue) return false;



            TimeSpan min = TimeSpan.MaxValue, max = TimeSpan.MinValue;

            foreach (var t in g.Trips)

            {

                var pu = SupeyTripTimes.TryParsePU(t);

                if (!pu.HasValue) continue;

                if (pu.Value < min) min = pu.Value;

                if (pu.Value > max) max = pu.Value;

            }

            if (min == TimeSpan.MaxValue) return false;



            double span = (max - min).TotalMinutes;

            if (span >= MinGapToSplit.TotalMinutes) return true;



            double toMin = Math.Abs((incomingPu.Value - min).TotalMinutes);

            double toMax = Math.Abs((incomingPu.Value - max).TotalMinutes);

            return toMin >= MinGapToSplit.TotalMinutes || toMax >= MinGapToSplit.TotalMinutes;

        }



        private static List<SupeyTripCluster> TrySplitCluster(SupeyTripCluster g)

        {

            int n = g.Trips.Count;

            var keyed = new List<(int idx, TimeSpan pu, char leg)>(n);

            for (int i = 0; i < n; i++)

            {

                var pu = SupeyTripTimes.TryParsePU(g.Trips[i]) ?? TimeSpan.MaxValue;

                keyed.Add((i, pu, LegLetter(g.Trips[i]?.TripNumber)));

            }

            keyed.Sort((a, b) =>

            {

                int cmp = a.pu.CompareTo(b.pu);

                return cmp != 0 ? cmp : a.idx.CompareTo(b.idx);

            });



            var spans = new List<List<int>>();

            var current = new List<int> { keyed[0].idx };

            TimeSpan spanStart = keyed[0].pu;

            TimeSpan spanEnd = keyed[0].pu;



            for (int k = 1; k < keyed.Count; k++)

            {

                var item = keyed[k];

                if (item.pu == TimeSpan.MaxValue)

                {

                    current.Add(item.idx);

                    continue;

                }

                if (spanStart == TimeSpan.MaxValue)

                {

                    spanStart = item.pu;

                    spanEnd = item.pu;

                    current.Add(item.idx);

                    continue;

                }

                double gapMin = (item.pu - spanEnd).TotalMinutes;

                if (gapMin >= MinGapToSplit.TotalMinutes)

                {

                    spans.Add(current);

                    current = new List<int>();

                    spanStart = item.pu;

                }

                spanEnd = item.pu;

                current.Add(item.idx);

            }

            if (current.Count > 0)

                spans.Add(current);

            if (spans.Count < 2)

                return null;



            var clusters = new List<SupeyTripCluster>(spans.Count);

            foreach (var indices in spans)

            {

                var part = ExtractSubCluster(g, indices);

                if (part.Trips.Count > 0)

                    clusters.Add(part);

            }

            return clusters.Count >= 2 ? clusters : null;

        }



        private static SupeyTripCluster ExtractSubCluster(SupeyTripCluster g, List<int> indices)

        {

            var part = new SupeyTripCluster

            {

                GroupNumber = g.GroupNumber,

                GroupColor = g.GroupColor,

            };

            foreach (int i in indices)

            {

                part.Trips.Add(g.Trips[i]);

                if (g.PickupPoints != null && i < g.PickupPoints.Count)

                    part.PickupPoints.Add(g.PickupPoints[i]);

                if (g.DropoffPoints != null && i < g.DropoffPoints.Count)

                    part.DropoffPoints.Add(g.DropoffPoints[i]);

            }

            part.PickupOrder.Clear();

            part.DropoffOrder.Clear();

            part.RoutePolyline.Clear();

            part.RoadTourOptimized = false;

            return part;

        }



        private static void RenumberGroups(SupeyDriverPlan plan) => RenumberGroupsPublic(plan);

        internal static void RenumberGroupsPublic(SupeyDriverPlan plan)
        {
            if (plan?.Groups == null) return;
            for (int i = 0; i < plan.Groups.Count; i++)
            {
                plan.Groups[i].GroupNumber = i + 1;
                plan.Groups[i].GroupColor = SupeyGroupPalette.For(i + 1);
            }
        }



        internal static TimeSpan MinPickupTime(SupeyTripCluster g)
        {
            if (g?.Trips == null || g.Trips.Count == 0)
                return g?.EarliestPickup ?? TimeSpan.Zero;
            TimeSpan min = TimeSpan.MaxValue;
            foreach (var t in g.Trips)
            {
                var pu = SupeyTripTimes.TryParsePU(t);
                if (pu.HasValue && pu.Value < min)
                    min = pu.Value;
            }
            return min == TimeSpan.MaxValue ? g.EarliestPickup : min;
        }

        /// <summary>Latest scheduled DO across the cluster (appointment sheet).</summary>
        internal static TimeSpan MaxDropoffTime(SupeyTripCluster g)
        {
            if (g?.Trips == null || g.Trips.Count == 0)
                return g?.HardestDropoff ?? TimeSpan.Zero;
            TimeSpan max = TimeSpan.MinValue;
            foreach (var t in g.Trips)
            {
                var d = SupeyTripTimes.TryParseDO(t);
                if (d.HasValue && d.Value > max)
                    max = d.Value;
            }
            if (max != TimeSpan.MinValue) return max;
            if (g.HardestDropoff > TimeSpan.Zero) return g.HardestDropoff;
            return g.LatestPickup.Add(TimeSpan.FromMinutes(30));
        }

        private static char LegLetter(string tripNumber)

        {

            string s = (tripNumber ?? "").Trim();

            if (s.Length < 2) return '\0';

            char c = char.ToUpperInvariant(s[s.Length - 1]);

            return c == 'A' || c == 'B' ? c : '\0';

        }

    }

}

