using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Matches live Modivcare trips to weekday template CSV rows (same rules as <see cref="FullScheduleBuilder"/>).</summary>
    internal static class SupeyTemplateTripMatcher
    {
        public static SupeyTemplateMatchResult Run(
            DateTime serviceDate,
            IList<MCDownloadedTrip> liveTrips,
            IList<SupeyDriverProfile> selectedRoster)
        {
            var dayOfWeek = serviceDate.DayOfWeek;
            var result = new SupeyTemplateMatchResult
            {
                Weekday = dayOfWeek.ToString(),
            };

            string dir = TemplateBuilder.GetDayTemplateDirectory(dayOfWeek.ToString());
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                return result;

            var csvFiles = Directory.GetFiles(dir, "*.csv");
            if (csvFiles.Length == 0)
                return result;

            result.HadTemplates = true;
            liveTrips = liveTrips ?? new List<MCDownloadedTrip>();
            selectedRoster = selectedRoster ?? new List<SupeyDriverProfile>();

            var rosterSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in selectedRoster)
            {
                if (d != null && !string.IsNullOrWhiteSpace(d.Name))
                    rosterSet.Add(d.Name.Trim());
            }

            foreach (var path in csvFiles.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                string tab = Path.GetFileNameWithoutExtension(path) ?? "";
                if (IsSkippedTab(tab)) continue;

                var fileSlots = SupeyTemplateCsvLoader.LoadSlotsFromFile(path);
                int tripSlots = 0;
                foreach (var s in fileSlots)
                    if (s.Kind == SupeyTemplateSlot.SlotKind.Trip) tripSlots++;

                if (tripSlots == 0 && fileSlots.Count == 0) continue;

                string rosterName = SupeyTemplateDriverNames.MapTabToRoster(tab, selectedRoster);
                bool onRoster = rosterName != null && rosterSet.Contains(rosterName);

                if (!onRoster)
                {
                    if (tripSlots > 0)
                        result.OrphanTemplateDriverTabs.Add(tab);
                    continue;
                }

                var orderedSlots = new List<SupeyTemplateSlot>();
                result.OrderedSlotsByRosterDriver[rosterName] = orderedSlots;

                foreach (var slot in fileSlots)
                {
                    if (slot.Kind == SupeyTemplateSlot.SlotKind.Gap)
                    {
                        orderedSlots.Add(new SupeyTemplateSlot
                        {
                            Kind = SupeyTemplateSlot.SlotKind.Gap,
                            NoteText = slot.NoteText,
                        });
                        continue;
                    }

                    var templateTrip = slot.TemplateTrip;
                    if (templateTrip == null) continue;

                    result.TemplateRowCount++;
                    var outSlot = new SupeyTemplateSlot
                    {
                        Kind = SupeyTemplateSlot.SlotKind.Trip,
                        TemplateTrip = templateTrip,
                    };

                    MCDownloadedTrip match = null;
                    // Same pool as legacy Schedule Builder (full download); one live trip → one driver lock.
                    foreach (var live in liveTrips)
                    {
                        string liveTn = live?.TripNumber ?? "";
                        if (liveTn.Length > 0 && result.MatchedLiveTripNumbers.Contains(liveTn))
                            continue;
                        if (TemplateTripMatchRules.TripsMatch(templateTrip, live))
                        {
                            match = live;
                            break;
                        }
                    }

                    if (match == null)
                    {
                        result.UnmatchedTemplateRowCount++;
                        // Like legacy Schedule Builder: only matched Modivcare rows go on the schedule.
                        continue;
                    }

                    string tn = match.TripNumber ?? "";
                    if (result.MatchedLiveTripNumbers.Contains(tn))
                    {
                        result.Warnings.Add(new SupeyWarning(
                            SupeyWarningKind.UnassignedToReserves,
                            tn,
                            rosterName,
                            "Template row matched live trip " + tn + " more than once — only first match used."));
                        result.UnmatchedTemplateRowCount++;
                        continue;
                    }

                    result.MatchedLiveTripNumbers.Add(tn);
                    result.MatchedCount++;
                    outSlot.MatchedLiveTrip = match;
                    outSlot.TemplateTrip = null;
                    if (!result.Locks.ContainsKey(tn))
                        result.Locks[tn] = rosterName;
                    orderedSlots.Add(outSlot);
                }
            }

            if (result.OrphanTemplateDriverTabs.Count > 0)
            {
                result.Warnings.Add(new SupeyWarning(
                    SupeyWarningKind.UnassignedToReserves,
                    "",
                    "Templates",
                    result.OrphanTemplateDriverTabs.Count +
                    " template driver tab(s) not on checked roster — those rows were not assigned."));
            }

            if (result.UnmatchedTemplateRowCount > 0)
            {
                result.Warnings.Add(new SupeyWarning(
                    SupeyWarningKind.UnassignedToReserves,
                    "",
                    "Templates",
                    result.UnmatchedTemplateRowCount +
                    " template row(s) had no matching live Modivcare trip (not shown on driver schedule)."));
            }

            return result;
        }

        private static bool IsSkippedTab(string tab)
        {
            if (string.IsNullOrWhiteSpace(tab)) return true;
            if (tab.IndexOf("Reserves", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (tab.IndexOf("Schedule", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (tab.IndexOf("LGTC", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (tab.StartsWith("_", StringComparison.Ordinal)) return true;
            return false;
        }

    }
}
