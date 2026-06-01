using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Paste-friendly Schedule Builder dump for Cursor / desk review.</summary>
    internal static class ScheduleBuilderReviewExport
    {
        private const string TripHeader =
            "Trip #\tClient\tPU Time\tPU Street\tPU City\tDO Time\tDO Street\tDO City\tMiles\tComments\tBucket";

        public static string BuildFull(
            DateTime serviceDate,
            string weekdayFolder,
            FullScheduleBuilder builder,
            IReadOnlyDictionary<string, List<ScheduleBuilderPreviewLine>> linesByTab,
            string activeTab)
        {
            if (builder == null) return "";
            var sb = new StringBuilder();
            sb.AppendLine("# Hiatme Schedule Builder — paste into Cursor for review");
            sb.AppendLine("Service date: " + serviceDate.ToString("yyyy-MM-dd"));
            if (!string.IsNullOrWhiteSpace(weekdayFolder))
                sb.AppendLine("Template weekday folder: " + weekdayFolder.Trim());
            sb.AppendLine("UI tab when copied: " + (string.IsNullOrWhiteSpace(activeTab) ? "(none)" : activeTab.Trim()));
            sb.AppendLine();

            AppendSummary(sb, builder);
            AppendRules(sb);
            sb.AppendLine("---");
            sb.AppendLine();

            if (linesByTab == null || linesByTab.Count == 0)
            {
                sb.AppendLine("(No preview — run BUILD first.)");
                return sb.ToString();
            }

            var tabOrder = linesByTab.Keys
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
            int resIdx = tabOrder.FindIndex(n =>
                string.Equals(n, "Reserves", StringComparison.OrdinalIgnoreCase));
            if (resIdx > 0)
            {
                string res = tabOrder[resIdx];
                tabOrder.RemoveAt(resIdx);
                tabOrder.Insert(0, res);
            }

            foreach (string tab in tabOrder)
            {
                if (!linesByTab.TryGetValue(tab, out var lines) || lines == null) continue;
                AppendTab(sb, tab, lines, builder);
            }

            AppendUnmatchedDownloadTrips(sb, builder, linesByTab);
            return sb.ToString();
        }

        public static string BuildTab(
            DateTime serviceDate,
            string tabName,
            IList<ScheduleBuilderPreviewLine> lines,
            FullScheduleBuilder builder)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Hiatme Schedule Builder — " + (tabName ?? "tab"));
            sb.AppendLine("Service date: " + serviceDate.ToString("yyyy-MM-dd"));
            sb.AppendLine();
            if (lines == null || lines.Count == 0)
                sb.AppendLine("(empty tab)");
            else
                AppendTab(sb, tabName, lines, builder);
            return sb.ToString();
        }

        public static string BuildSingleTrip(MCDownloadedTrip trip, string tabName)
        {
            if (trip == null) return "";
            var sb = new StringBuilder();
            sb.AppendLine("# Schedule Builder — selected trip");
            if (!string.IsNullOrWhiteSpace(tabName))
                sb.AppendLine("Tab: " + tabName);
            sb.AppendLine(TripHeader);
            AppendTripRow(sb, trip, ClassifyBucket(trip));
            return sb.ToString();
        }

        private static void AppendSummary(StringBuilder sb, FullScheduleBuilder builder)
        {
            int onDrivers = 0;
            int wcOnDrivers = 0;
            if (builder.PreviewDriverLines != null)
            {
                foreach (var kv in builder.PreviewDriverLines)
                {
                    foreach (var line in kv.Value ?? Enumerable.Empty<ScheduleBuilderPreviewLine>())
                    {
                        if (line?.Kind != ScheduleBuilderPreviewLine.LineKind.Trip || line.Trip == null)
                            continue;
                        onDrivers++;
                        if (SupeyWillCallPickup.IsPickupWillCall(line.Trip))
                            wcOnDrivers++;
                    }
                }
            }

            int dl = builder.MCTripList?.Count ?? 0;
            int wc = builder.PreviewReservesWillCalls?.Count ?? 0;
            int res = builder.PreviewReserves?.Count ?? 0;
            int rer = builder.PreviewReservesReroute?.Count ?? 0;
            int ban = builder.PreviewReservesBanned?.Count ?? 0;
            int wcDl = builder.WillCallsInDownloadCount;
            int wcPu = builder.WillCallsPuMidnightInDownloadCount;
            int wcCmt = builder.WillCallsCommentInDownloadCount;
            int doMidnight = SupeyWillCallPickup.CountDropoffMidnightInList(builder.MCTripList);

            sb.AppendLine("## Summary");
            sb.AppendLine("Downloaded from Modivcare: " + dl + " trip(s)");
            sb.AppendLine("On driver tabs: " + onDrivers + " trip(s)"
                + (wcOnDrivers > 0 ? " — WARNING: " + wcOnDrivers + " will-call trip(s) still on drivers" : ""));
            sb.AppendLine("Will calls = 00:00 / 12:00 AM **pickup (PU)** only (WILL CALL in comments does not count).");
            sb.AppendLine("Reserves — Will calls: " + wc + " (in download: " + wcDl
                + " with midnight PU" + (wcCmt > 0 ? "; " + wcCmt + " trip(s) have WILL CALL comment but non-midnight PU" : "") + ")");
            if (doMidnight > 0)
            {
                sb.AppendLine("Also: " + doMidnight + " trip(s) with 00:00 **dropoff (DO)** (B/C return legs — not counted as will calls).");
            }
            sb.AppendLine("Reserves — Reservers: " + res);
            sb.AppendLine("Reserves — Reroutes: " + rer);
            sb.AppendLine("Reserves — Banned: " + ban);
            sb.AppendLine();
        }

        private static void AppendRules(StringBuilder sb)
        {
            sb.AppendLine("## No-go areas (reroute)");
            var areas = SupeyOutOfArea.CachedAreas;
            if (areas == null || areas.Count == 0)
                sb.AppendLine("(none loaded)");
            else
                sb.AppendLine(string.Join(", ", areas));
            sb.AppendLine();

            sb.AppendLine("## Banned clients");
            var banned = ScheduleBuilderBannedClients.CachedClients;
            if (banned == null || banned.Count == 0)
                sb.AppendLine("(none)");
            else
            {
                foreach (var c in banned)
                {
                    if (c == null) continue;
                    sb.Append("- ").Append(Sanitize(ScheduleBuilderBannedClients.FormatListLabel(c)));
                    if (!string.IsNullOrWhiteSpace(c.AddedFromTripNumber))
                        sb.Append(" (trip ").Append(Sanitize(c.AddedFromTripNumber)).Append(")");
                    sb.AppendLine();
                }
            }
            sb.AppendLine();
        }

        private static void AppendTab(
            StringBuilder sb,
            string tabName,
            IList<ScheduleBuilderPreviewLine> lines,
            FullScheduleBuilder builder)
        {
            int tripCount = lines.Count(l => l?.Kind == ScheduleBuilderPreviewLine.LineKind.Trip && l.Trip != null);
            sb.Append("=== ").Append(tabName ?? "(tab)").Append(" === (")
              .Append(tripCount).Append(" trip").Append(tripCount == 1 ? "" : "s")
              .AppendLine(")");
            sb.AppendLine(TripHeader);

            foreach (var line in lines)
            {
                if (line == null) continue;
                if (line.Kind == ScheduleBuilderPreviewLine.LineKind.SectionHeader)
                {
                    sb.AppendLine();
                    sb.Append("--- ").Append(Sanitize(line.SectionTitle)).AppendLine(" ---");
                    continue;
                }
                if (line.Kind == ScheduleBuilderPreviewLine.LineKind.Gap)
                {
                    string note = (line.GapNoteText ?? "").Trim();
                    sb.AppendLine(string.IsNullOrEmpty(note) ? "[template gap]" : "[gap] " + Sanitize(note));
                    continue;
                }
                if (line.Kind == ScheduleBuilderPreviewLine.LineKind.Trip && line.Trip != null)
                {
                    string bucket = ReserveBandLabel(line.ReserveBandColor);
                    if (bucket.Length == 0 && tabName != null
                        && !tabName.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
                        bucket = "driver tab";
                    AppendTripRow(sb, line.Trip, bucket);
                }
            }
            sb.AppendLine();
        }

        private static void AppendUnmatchedDownloadTrips(
            StringBuilder sb,
            FullScheduleBuilder builder,
            IReadOnlyDictionary<string, List<ScheduleBuilderPreviewLine>> linesByTab)
        {
            if (builder.MCTripList == null || builder.MCTripList.Count == 0) return;

            var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (linesByTab != null)
            {
                foreach (var kv in linesByTab)
                {
                    foreach (var line in kv.Value ?? Enumerable.Empty<ScheduleBuilderPreviewLine>())
                    {
                        if (line?.Trip == null) continue;
                        string tn = (line.Trip.TripNumber ?? "").Trim();
                        if (tn.Length > 0) placed.Add(tn);
                    }
                }
            }

            var missing = builder.MCTripList
                .Where(t => t != null && (t.TripNumber ?? "").Trim().Length > 0
                    && !placed.Contains((t.TripNumber ?? "").Trim()))
                .ToList();
            if (missing.Count == 0) return;

            sb.Append("=== Unmatched download (not on any tab) === (")
              .Append(missing.Count).AppendLine(" trip(s))");
            sb.AppendLine(TripHeader);
            foreach (var t in missing.OrderBy(x => x?.ClientFullName ?? ""))
                AppendTripRow(sb, t, ClassifyBucket(t) + " · unmatched");
            sb.AppendLine();
        }

        private static string ClassifyBucket(MCDownloadedTrip trip)
        {
            if (ScheduleBuilderReserveBuckets.IsWillCallTrip(trip)
                && ScheduleBuilderReserveBuckets.Classify(trip) != ScheduleBuilderReserveBuckets.ReserveBucket.WillCall)
                return "will call (blocked: banned/no-go)";
            switch (ScheduleBuilderReserveBuckets.Classify(trip))
            {
                case ScheduleBuilderReserveBuckets.ReserveBucket.WillCall: return "will call";
                case ScheduleBuilderReserveBuckets.ReserveBucket.Reroute: return "reroute";
                case ScheduleBuilderReserveBuckets.ReserveBucket.Banned: return "banned";
                default: return "reserver";
            }
        }

        private static string ReserveBandLabel(Color? band)
        {
            if (!band.HasValue) return "";
            if (band.Value == ScheduleBuilderReserveBuckets.WillCallBand) return "will call";
            if (band.Value == ScheduleBuilderReserveBuckets.RerouteBand) return "reroute";
            if (band.Value == ScheduleBuilderReserveBuckets.BannedBand) return "banned";
            if (band.Value == ScheduleBuilderReserveBuckets.ReserversBand) return "reserver";
            return "";
        }

        private static void AppendTripRow(StringBuilder sb, MCDownloadedTrip t, string bucket)
        {
            sb.Append(Sanitize(t.TripNumber)).Append('\t')
              .Append(Sanitize(t.ClientFullName)).Append('\t')
              .Append(Sanitize(t.PUTime));
            if (SupeyWillCallPickup.IsPickupWillCall(t))
                sb.Append(" [will-call:midnight-PU]");
            else if (ScheduleBuilderReserveBuckets.HasWillCallComment(t.Comments))
                sb.Append(" [WILL CALL comment — not midnight PU]");
            sb.Append('\t')
              .Append(Sanitize(t.PUStreet)).Append('\t')
              .Append(Sanitize(t.PUCity)).Append('\t')
              .Append(Sanitize(t.DOTime)).Append('\t')
              .Append(Sanitize(t.DOStreet)).Append('\t')
              .Append(Sanitize(t.DOCITY)).Append('\t')
              .Append(Sanitize(t.Miles)).Append('\t')
              .Append(Sanitize(t.Comments)).Append('\t')
              .Append(Sanitize(bucket))
              .AppendLine();
        }

        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");
        }
    }
}
