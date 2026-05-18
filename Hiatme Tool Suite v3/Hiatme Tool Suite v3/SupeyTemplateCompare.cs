using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Compares a built schedule against weekday template CSVs for learning / QA — advisory only.
    /// </summary>
    internal sealed class SupeyTemplateCompare
    {
        public int TemplateTripCount { get; private set; }
        public int AssignedTripCount { get; private set; }
        public int SameDriverMatches { get; private set; }
        public int DriverOrderInversions { get; private set; }
        public double AssignedPercent => TemplateTripCount == 0 ? 0 : 100.0 * AssignedTripCount / TemplateTripCount;
        public double DriverMatchPercent => TemplateTripCount == 0 ? 0 : 100.0 * SameDriverMatches / TemplateTripCount;
        public bool HadTemplates { get; private set; }
        public string SummaryText { get; private set; } = "";

        public static SupeyTemplateCompare Run(SupeyScheduleResult result, SupeyTemplateHints hints)
        {
            var report = new SupeyTemplateCompare();
            if (result == null || hints == null || !hints.HasAnyTemplate)
            {
                report.SummaryText = "No weekday templates loaded — build is standalone.";
                return report;
            }

            report.HadTemplates = true;
            var assignedByTrip = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var plan in result.DriverPlans)
            {
                foreach (var g in plan.Groups)
                {
                    foreach (var t in g.Trips)
                    {
                        if (!string.IsNullOrEmpty(t.TripNumber))
                            assignedByTrip[t.TripNumber] = plan.Driver.Name ?? "";
                    }
                }
            }

            foreach (var kv in hints.PreferredDriverByTrip)
            {
                report.TemplateTripCount++;
                if (assignedByTrip.TryGetValue(kv.Key, out var actual))
                {
                    report.AssignedTripCount++;
                    if (string.Equals(actual, kv.Value, StringComparison.OrdinalIgnoreCase))
                        report.SameDriverMatches++;
                }
            }

            foreach (var kv in hints.DriverTripOrder)
            {
                var templateOrder = kv.Value;
                if (templateOrder == null || templateOrder.Count < 2) continue;
                var actualOrder = new List<string>();
                foreach (var plan in result.DriverPlans)
                {
                    if (!string.Equals(plan.Driver.Name, kv.Key, StringComparison.OrdinalIgnoreCase))
                        continue;
                    foreach (var g in plan.Groups.OrderBy(x => x.EarliestPickup))
                    {
                        foreach (var t in g.Trips)
                        {
                            if (!string.IsNullOrEmpty(t.TripNumber))
                                actualOrder.Add(t.TripNumber);
                        }
                    }
                }
                report.DriverOrderInversions += CountInversions(templateOrder, actualOrder);
            }

            var sb = new StringBuilder();
            sb.AppendFormat("Template compare ({0}): ", hints.Weekday);
            sb.AppendFormat("{0:F0}% assigned ({1}/{2}), ", report.AssignedPercent,
                report.AssignedTripCount, report.TemplateTripCount);
            sb.AppendFormat("{0:F0}% same driver ({1}/{2}). ", report.DriverMatchPercent,
                report.SameDriverMatches, report.TemplateTripCount);
            if (report.DriverOrderInversions > 0)
                sb.Append(report.DriverOrderInversions).Append(" PU-order inversions vs templates. ");
            sb.Append("Home addresses in roster may differ from template days.");
            report.SummaryText = sb.ToString();
            return report;
        }

        private static int CountInversions(List<string> templateOrder, List<string> actualOrder)
        {
            var pos = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < actualOrder.Count; i++)
            {
                if (!pos.ContainsKey(actualOrder[i]))
                    pos[actualOrder[i]] = i;
            }
            int inv = 0;
            for (int i = 0; i < templateOrder.Count; i++)
            {
                if (!pos.TryGetValue(templateOrder[i], out int pi)) continue;
                for (int j = i + 1; j < templateOrder.Count; j++)
                {
                    if (!pos.TryGetValue(templateOrder[j], out int pj)) continue;
                    if (pi > pj) inv++;
                }
            }
            return inv;
        }

        public string ToTabSeparatedSummary()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Metric\tValue");
            sb.AppendLine("Had templates\t" + HadTemplates);
            sb.AppendLine("Template trips\t" + TemplateTripCount);
            sb.AppendLine("Assigned (in build)\t" + AssignedTripCount);
            sb.AppendLine("Same driver as template\t" + SameDriverMatches);
            sb.AppendLine("PU order inversions\t" + DriverOrderInversions);
            sb.AppendLine("Summary\t" + SummaryText);
            return sb.ToString();
        }
    }
}
