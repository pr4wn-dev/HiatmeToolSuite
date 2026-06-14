using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    internal sealed class ScheduleBuilderDriverSuggestProgress
    {
        public string Phase { get; set; } = "";
        public int DriverIndex { get; set; }
        public int DriverTotal { get; set; }
        public string DriverDisplayName { get; set; } = "";
    }

    internal enum ScheduleBuilderSuggestPlacementKind
    {
        MergeIntoGroup,
        NewGroupAfterGroup,
        NewGroupAtStart,
        NewGroupAtEnd,
    }

    internal sealed class ScheduleBuilderDriverSuggestion
    {
        public string DriverTab { get; set; }
        public string DriverDisplayName { get; set; }
        public ScheduleBuilderSuggestPlacementKind Kind { get; set; }
        public int TargetGroupNumber { get; set; }
        public int InsertBeforeLineIndex { get; set; }
        public bool InsertGapBeforeTrip { get; set; }
        public MCDownloadedTrip MergeAfterTrip { get; set; }
        public string Headline { get; set; }
        public string Summary { get; set; }
        public List<string> Reasons { get; } = new List<string>();
        public double Score { get; set; }
        public bool Feasible { get; set; }
    }

    /// <summary>Scores cross-driver placements for a selected trip (merge vs new group).</summary>
    internal static class ScheduleBuilderDriverSuggest
    {
        private const int MaxParallelDrivers = 5;
        /// <summary>Buffer after trip DO before next group PU ???? merge rejected if trip ends before this.</summary>
        private const double MergeMinGapAfterTripDoMinutes = 5.0;
        /// <summary>Same as day-feasibility gate ???? cannot PU two riders at once far apart.</summary>
        private const double MergeConcurrentPuMaxMeters = 2500.0;
        private const double MergeConcurrentPuMaxMinutesApart = 3.0;

        private const double TemplateHintScoreBonusMeters = 8000.0;
        private const double DropWaveMatchScoreBonusMeters = 5000.0;
        private const double DoAnchorMatchMaxMinutes = 30.0;

        public static Task<List<ScheduleBuilderDriverSuggestion>> SuggestAsync(
            MCDownloadedTrip trip,
            string sourceTab,
            IReadOnlyDictionary<string, List<ScheduleBuilderPreviewLine>> linesByTab,
            IReadOnlyList<SupeyDriverProfile> roster,
            CancellationToken token) =>
            SuggestAsync(trip, sourceTab, linesByTab, roster, serviceDate: null, progress: null, token);

        public static Task<List<ScheduleBuilderDriverSuggestion>> SuggestAsync(
            MCDownloadedTrip trip,
            string sourceTab,
            IReadOnlyDictionary<string, List<ScheduleBuilderPreviewLine>> linesByTab,
            IReadOnlyList<SupeyDriverProfile> roster,
            IProgress<ScheduleBuilderDriverSuggestProgress> progress,
            CancellationToken token) =>
            SuggestAsync(trip, sourceTab, linesByTab, roster, serviceDate: null, progress, token);

        public static async Task<List<ScheduleBuilderDriverSuggestion>> SuggestAsync(
            MCDownloadedTrip trip,
            string sourceTab,
            IReadOnlyDictionary<string, List<ScheduleBuilderPreviewLine>> linesByTab,
            IReadOnlyList<SupeyDriverProfile> roster,
            DateTime? serviceDate,
            IProgress<ScheduleBuilderDriverSuggestProgress> progress,
            CancellationToken token)
        {
            var results = new List<ScheduleBuilderDriverSuggestion>();
            if (trip == null || linesByTab == null || linesByTab.Count == 0)
                return results;

            SupeyTemplateHints templateHints = null;
            if (serviceDate.HasValue)
                templateHints = new SupeyTemplateHints(serviceDate.Value.DayOfWeek.ToString());

            progress?.Report(new ScheduleBuilderDriverSuggestProgress { Phase = "geocode" });

            var pickupByTrip = new Dictionary<string, GeoPoint>(StringComparer.OrdinalIgnoreCase);
            var dropoffByTrip = new Dictionary<string, GeoPoint>(StringComparer.OrdinalIgnoreCase);
            var allTrips = CollectTripsForGeocode(linesByTab, trip);
            await ScheduleBuilderMapGeocode.ResolveTripsForMapAsync(
                allTrips, pickupByTrip, dropoffByTrip, token).ConfigureAwait(false);

            var prepCache = new ScheduleBuilderDriverSuggestPrepCache();
            var driverJobs = BuildDriverJobs(linesByTab, roster);
            if (driverJobs.Count == 0)
                return results;

            var resultsLock = new object();
            int driversStarted = 0;
            using (var driverGate = new SemaphoreSlim(MaxParallelDrivers, MaxParallelDrivers))
            {
                var tasks = driverJobs.Select(async job =>
                {
                    await driverGate.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        int idx = Interlocked.Increment(ref driversStarted);
                        progress?.Report(new ScheduleBuilderDriverSuggestProgress
                        {
                            Phase = "driver",
                            DriverIndex = idx,
                            DriverTotal = driverJobs.Count,
                            DriverDisplayName = job.DisplayName,
                        });

                        var driverResults = await SuggestForDriverAsync(
                            trip, job, templateHints, pickupByTrip, dropoffByTrip, prepCache, token).ConfigureAwait(false);

                        if (driverResults.Count == 0)
                            return;

                        lock (resultsLock)
                        {
                            results.AddRange(driverResults);
                        }
                    }
                    finally
                    {
                        driverGate.Release();
                    }
                }).ToList();

                await Task.WhenAll(tasks).ConfigureAwait(false);
            }

            progress?.Report(new ScheduleBuilderDriverSuggestProgress { Phase = "rank" });

            string tripNum2 = (trip.TripNumber ?? "").Trim();
            bool tripGeocoded2 = tripNum2.Length > 0
                && pickupByTrip.ContainsKey(tripNum2)
                && dropoffByTrip.ContainsKey(tripNum2);
            foreach (var s in results)
            {
                if (s.Reasons.Count > 0)
                    continue;
                string pu = SupeyTripTimes.FormatTimeOfDay(SupeyTripTimes.TryParsePU(trip));
                string dof = SupeyTripTimes.FormatTimeOfDay(SupeyTripTimes.TryParseDO(trip));
                if (pu.Length > 0)
                    s.Reasons.Add("Trip pickup " + pu + (dof.Length > 0 ? ", dropoff " + dof : "") + ".");
                if (!tripGeocoded2)
                    s.Reasons.Add("Trip geocode missing ???? drive times are approximate.");
            }

            var ranked = results
                .OrderByDescending(s => s.Feasible)
                .ThenBy(s => s.Score)
                .ThenBy(s => ChronologyPenalty(s, trip))
                .ThenBy(s => s.DriverDisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var feasibleOnly = ranked.Where(s => s.Feasible).ToList();
            if (feasibleOnly.Count > 0)
                return feasibleOnly.Take(24).ToList();

            // Never surface impossible merges; new-group slots may still be worth a look.
            return ranked
                .Where(s => s.Kind != ScheduleBuilderSuggestPlacementKind.MergeIntoGroup)
                .Take(12)
                .ToList();
        }

        /// <summary>Target driver lines after applying a suggestion (for preview / confirm).</summary>
        public static List<ScheduleBuilderPreviewLine> BuildPlacedTargetLines(
            MCDownloadedTrip trip,
            ScheduleBuilderDriverSuggestion suggestion,
            IReadOnlyDictionary<string, List<ScheduleBuilderPreviewLine>> linesByTab)
        {
            if (trip == null || suggestion == null || linesByTab == null)
                return new List<ScheduleBuilderPreviewLine>();

            if (!linesByTab.TryGetValue(suggestion.DriverTab, out var baseLines) || baseLines == null)
                baseLines = new List<ScheduleBuilderPreviewLine>();

            var lines = CloneLines(baseLines);
            if (ScheduleBuilderPreviewDrag.FindTripLineIndex(lines, trip) >= 0)
                ScheduleBuilderPreviewDrag.TryRemoveTrip(lines, trip);

            ApplyPlacementToLines(lines, trip, suggestion);
            return lines;
        }

        /// <summary>Inserts trip + gap rows so preview groups match the suggestion kind.</summary>
        internal static void ApplyPlacementToLines(
            IList<ScheduleBuilderPreviewLine> lines,
            MCDownloadedTrip trip,
            ScheduleBuilderDriverSuggestion suggestion,
            Color? reserveBand = null,
            bool rerouted = false)
        {
            if (lines == null || trip == null || suggestion == null)
                return;

            ApplyPlacementToLines(
                lines,
                trip,
                suggestion.Kind,
                suggestion.InsertBeforeLineIndex,
                suggestion.InsertGapBeforeTrip,
                reserveBand,
                rerouted);
        }

        internal static void ApplyPlacementToLines(
            IList<ScheduleBuilderPreviewLine> lines,
            MCDownloadedTrip trip,
            ScheduleBuilderSuggestPlacementKind kind,
            int insertBeforeLine,
            bool insertGapBeforeTrip,
            Color? reserveBand = null,
            bool rerouted = false)
        {
            if (lines == null || trip == null)
                return;

            int insertAt = Math.Max(0, Math.Min(insertBeforeLine, lines.Count));

            if (kind == ScheduleBuilderSuggestPlacementKind.NewGroupAtStart)
            {
                ScheduleBuilderPreviewDrag.InsertTripLine(lines, trip, insertAt, reserveBand, rerouted);
                ScheduleBuilderPreviewDrag.InsertGapLine(lines, insertAt + 1);
                return;
            }

            if (insertGapBeforeTrip)
            {
                ScheduleBuilderPreviewDrag.InsertGapLine(lines, insertAt);
                insertAt++;
            }

            ScheduleBuilderPreviewDrag.InsertTripLine(lines, trip, insertAt, reserveBand, rerouted);
        }

        private sealed class DriverSuggestJob
        {
            public string Tab { get; set; }
            public string DisplayName { get; set; }
            public TimeSpan ShiftStart { get; set; }
            public SupeyDriverProfile Profile { get; set; }
            public List<ScheduleBuilderPreviewLine> Lines { get; set; }
        }

        private static List<DriverSuggestJob> BuildDriverJobs(
            IReadOnlyDictionary<string, List<ScheduleBuilderPreviewLine>> linesByTab,
            IReadOnlyList<SupeyDriverProfile> roster)
        {
            var jobs = new List<DriverSuggestJob>();
            foreach (var kv in linesByTab.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                string tab = kv.Key;
                if (tab.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
                    continue;

                var lines = kv.Value ?? new List<ScheduleBuilderPreviewLine>();

                var profile = ScheduleBuilderDriverMapRouting.FindProfileForScheduleTab(roster, tab);
                jobs.Add(new DriverSuggestJob
                {
                    Tab = tab,
                    DisplayName = (profile?.Name ?? tab).Trim(),
                    ShiftStart = profile?.ParseShiftStart() ?? new TimeSpan(6, 0, 0),
                    Profile = profile,
                    Lines = lines,
                });
            }
            return jobs;
        }

        private static async Task<List<ScheduleBuilderDriverSuggestion>> SuggestForDriverAsync(
            MCDownloadedTrip trip,
            DriverSuggestJob job,
            SupeyTemplateHints templateHints,
            Dictionary<string, GeoPoint> pickupByTrip,
            Dictionary<string, GeoPoint> dropoffByTrip,
            ScheduleBuilderDriverSuggestPrepCache prepCache,
            CancellationToken token)
        {
            var results = new List<ScheduleBuilderDriverSuggestion>();
            var lines = job.Lines;

            GeoPoint? homeGeo = null;
            if (job.Profile != null)
            {
                homeGeo = await ScheduleBuilderDriverMapRouting.ResolveHomeGeoAsync(job.Profile, token)
                    .ConfigureAwait(false);
            }

            var linesWithoutTrip = CloneLines(lines);
            if (ScheduleBuilderPreviewDrag.FindTripLineIndex(linesWithoutTrip, trip) >= 0)
                ScheduleBuilderPreviewDrag.TryRemoveTrip(linesWithoutTrip, trip);

            var baselineGroups = ScheduleBuilderPreviewGroups.BuildFromPreviewLines(linesWithoutTrip);
            var baseline = await BuildDriverDayBaselineAsync(
                baselineGroups, job.ShiftStart, pickupByTrip, dropoffByTrip, prepCache, token)
                .ConfigureAwait(false);

            var groups = ScheduleBuilderPreviewGroups.BuildFromPreviewLines(lines);
            var tripPu = SupeyTripTimes.TryParsePU(trip);
            int capacityPassengers = DriverCapacityPassengers(job.Profile);

            if (groups.Count == 0)
            {
                await TryAddCandidateAsync(results, job.Tab, job.DisplayName, trip, lines, job.ShiftStart,
                    ScheduleBuilderSuggestPlacementKind.NewGroupAtEnd,
                    targetGroupNumber: 1,
                    insertBeforeLine: lines.Count,
                    insertGap: false,
                    mergeAfterTrip: null,
                    pickupByTrip, dropoffByTrip, prepCache, baseline, homeGeo, capacityPassengers,
                    templateHints, null, token).ConfigureAwait(false);
                return results;
            }

            if (TryFindFirstTripLine(lines, out int firstTripLine)
                && ShouldTryNewGroupAtStart(groups, tripPu))
            {
                await TryAddCandidateAsync(results, job.Tab, job.DisplayName, trip, lines, job.ShiftStart,
                    ScheduleBuilderSuggestPlacementKind.NewGroupAtStart,
                    targetGroupNumber: 1,
                    insertBeforeLine: firstTripLine,
                    insertGap: false,
                    mergeAfterTrip: null,
                    pickupByTrip, dropoffByTrip, prepCache, baseline, homeGeo, capacityPassengers,
                    templateHints, null, token).ConfigureAwait(false);
            }

            for (int gn = 1; gn <= groups.Count; gn++)
            {
                var group = groups[gn - 1];
                if (group == null)
                    continue;

                if (PuSpreadAllowsMerge(group, trip)
                    && SharesMorningDropWave(group, trip)
                    && MergeFitsCapacity(group, trip, capacityPassengers))
                {
                    if (TryFindGroupLastTrip(lines, gn, out var lastTrip, out int lastTripLine))
                    {
                        await TryAddCandidateAsync(results, job.Tab, job.DisplayName, trip, lines, job.ShiftStart,
                            ScheduleBuilderSuggestPlacementKind.MergeIntoGroup,
                            targetGroupNumber: gn,
                            insertBeforeLine: lastTripLine + 1,
                            insertGap: false,
                            mergeAfterTrip: lastTrip,
                            pickupByTrip, dropoffByTrip, prepCache, baseline, homeGeo, capacityPassengers,
                            templateHints, group, token).ConfigureAwait(false);
                    }
                }

                if (TryFindLineAfterGroup(lines, gn, out int afterGroupLine)
                    && ShouldTryNewGroupAfterGroup(groups, gn, tripPu, baseline, trip, pickupByTrip))
                {
                    await TryAddCandidateAsync(results, job.Tab, job.DisplayName, trip, lines, job.ShiftStart,
                        ScheduleBuilderSuggestPlacementKind.NewGroupAfterGroup,
                        targetGroupNumber: gn + 1,
                        insertBeforeLine: afterGroupLine,
                        insertGap: NeedsGapBeforeNewGroup(lines, afterGroupLine),
                        mergeAfterTrip: null,
                        pickupByTrip, dropoffByTrip, prepCache, baseline, homeGeo, capacityPassengers,
                        templateHints, null, token).ConfigureAwait(false);
                }
            }

            if (ShouldTryNewGroupAtEnd(groups, tripPu))
            {
                await TryAddCandidateAsync(results, job.Tab, job.DisplayName, trip, lines, job.ShiftStart,
                    ScheduleBuilderSuggestPlacementKind.NewGroupAtEnd,
                    targetGroupNumber: groups.Count + 1,
                    insertBeforeLine: lines.Count,
                    insertGap: NeedsGapBeforeNewGroup(lines, lines.Count),
                    mergeAfterTrip: null,
                    pickupByTrip, dropoffByTrip, prepCache, baseline, homeGeo, capacityPassengers,
                    templateHints, null, token).ConfigureAwait(false);
            }

            return results;
        }

        private static double ClusterWindowMinutes =>
            SupeyScheduleAlgorithm.ClusterTimeWindowMinutesPublic;

        /// <summary>Start slot only when trip PU is before the driver's first existing pickup.</summary>
        private static bool ShouldTryNewGroupAtStart(IList<SupeyTripCluster> groups, TimeSpan? tripPu)
        {
            if (!tripPu.HasValue || groups == null || groups.Count == 0)
                return true;

            var first = groups[0];
            if (first.EarliestPickup == TimeSpan.MaxValue)
                return true;

            return tripPu.Value < first.EarliestPickup;
        }

        private static bool ShouldTryNewGroupAtEnd(IList<SupeyTripCluster> groups, TimeSpan? tripPu)
        {
            if (!tripPu.HasValue || groups == null || groups.Count == 0)
                return true;

            var last = groups[groups.Count - 1];
            if (last.EarliestPickup == TimeSpan.MaxValue)
                return true;

            TimeSpan anchor = last.LatestPickup > last.EarliestPickup
                ? last.LatestPickup
                : last.EarliestPickup;
            return tripPu.Value >= anchor.Subtract(TimeSpan.FromMinutes(ClusterWindowMinutes));
        }

        private static bool ShouldTryNewGroupAfterGroup(
            IList<SupeyTripCluster> groups,
            int afterGroupNumber,
            TimeSpan? tripPu,
            DriverDayBaseline baseline,
            MCDownloadedTrip trip,
            Dictionary<string, GeoPoint> pickupByTrip)
        {
            if (groups == null || afterGroupNumber < 1 || afterGroupNumber > groups.Count)
                return false;

            if (!tripPu.HasValue)
                return PassesCheapNewGroupAfterFilter(baseline, afterGroupNumber, trip, pickupByTrip);

            // Driver must be free — prior batch finished before this trip's PU window closes.
            int priorIdx = afterGroupNumber - 1;
            if (baseline != null && priorIdx >= 0 && priorIdx < baseline.ClockAfterGroup.Count)
            {
                char leg = SupeyScheduleAlgorithm.DetectLegPublic(trip?.TripNumber);
                double puLateCap = leg == 'A' ? 14.0 : 29.0;
                if (baseline.ClockAfterGroup[priorIdx] > tripPu.Value.Add(TimeSpan.FromMinutes(puLateCap)))
                    return false;
            }

            // Slot is before the next group's first pickup (dispatcher gap between batches).
            if (afterGroupNumber < groups.Count)
            {
                var next = groups[afterGroupNumber];
                if (next.EarliestPickup != TimeSpan.MaxValue
                    && tripPu.Value >= next.EarliestPickup)
                    return false;
            }

            return PassesCheapNewGroupAfterFilter(baseline, afterGroupNumber, trip, pickupByTrip);
        }

        private static async Task TryAddCandidateAsync(
            List<ScheduleBuilderDriverSuggestion> results,
            string tab,
            string displayName,
            MCDownloadedTrip trip,
            IList<ScheduleBuilderPreviewLine> baseLines,
            TimeSpan shiftStart,
            ScheduleBuilderSuggestPlacementKind kind,
            int targetGroupNumber,
            int insertBeforeLine,
            bool insertGap,
            MCDownloadedTrip mergeAfterTrip,
            Dictionary<string, GeoPoint> pickupByTrip,
            Dictionary<string, GeoPoint> dropoffByTrip,
            ScheduleBuilderDriverSuggestPrepCache prepCache,
            DriverDayBaseline baseline,
            GeoPoint? homeGeo,
            int capacityPassengers,
            SupeyTemplateHints templateHints,
            SupeyTripCluster mergeGroup,
            CancellationToken token)
        {
            var trialLines = CloneLines(baseLines);
            int removedAt = ScheduleBuilderPreviewDrag.FindTripLineIndex(trialLines, trip);
            bool relocateOnSameDriver = removedAt >= 0;
            if (relocateOnSameDriver)
                ScheduleBuilderPreviewDrag.TryRemoveTrip(trialLines, trip);

            int storedInsertBeforeLine = insertBeforeLine;
            if (relocateOnSameDriver && storedInsertBeforeLine > removedAt)
                storedInsertBeforeLine--;

            if (relocateOnSameDriver && storedInsertBeforeLine == removedAt && !insertGap
                && kind != ScheduleBuilderSuggestPlacementKind.NewGroupAtStart)
                return;

            ApplyPlacementToLines(
                trialLines, trip, kind, storedInsertBeforeLine, insertGap);

            if (!PassesCheapPlacementFilter(
                trialLines, trip, kind, targetGroupNumber, shiftStart, baseline, pickupByTrip))
                return;

            var eval = await EvaluateLinesAsync(
                trialLines, trip, kind, shiftStart, pickupByTrip, dropoffByTrip, prepCache, baseline, homeGeo,
                capacityPassengers, token)
                .ConfigureAwait(false);

            ApplyPlacementScoreBonuses(eval, kind, displayName, trip, templateHints, mergeGroup);

            if (!eval.Feasible && kind == ScheduleBuilderSuggestPlacementKind.MergeIntoGroup)
                return;

            var suggestion = BuildSuggestion(
                tab, displayName, kind, targetGroupNumber, storedInsertBeforeLine, insertGap,
                mergeAfterTrip, eval, trip, relocateOnSameDriver, capacityPassengers);

            if (results.Any(r =>
                string.Equals(r.DriverTab, suggestion.DriverTab, StringComparison.OrdinalIgnoreCase)
                && r.Kind == suggestion.Kind
                && r.InsertBeforeLineIndex == suggestion.InsertBeforeLineIndex))
                return;

            results.Add(suggestion);
        }

        private static ScheduleBuilderDriverSuggestion BuildSuggestion(
            string tab,
            string displayName,
            ScheduleBuilderSuggestPlacementKind kind,
            int targetGroupNumber,
            int insertBeforeLine,
            bool insertGap,
            MCDownloadedTrip mergeAfterTrip,
            PlacementEval eval,
            MCDownloadedTrip trip,
            bool relocateOnSameDriver,
            int capacityPassengers)
        {
            string tripLabel = (trip.TripNumber ?? "").Trim();
            if (tripLabel.Length == 0)
                tripLabel = (trip.ClientFullName ?? "trip").Trim();

            var s = new ScheduleBuilderDriverSuggestion
            {
                DriverTab = tab,
                DriverDisplayName = displayName,
                Kind = kind,
                TargetGroupNumber = targetGroupNumber,
                InsertBeforeLineIndex = insertBeforeLine,
                InsertGapBeforeTrip = insertGap,
                MergeAfterTrip = mergeAfterTrip,
                Feasible = eval.Feasible,
                Score = eval.Score,
            };

            string sameDriver = relocateOnSameDriver ? " (same driver, new slot)" : "";

            switch (kind)
            {
                case ScheduleBuilderSuggestPlacementKind.MergeIntoGroup:
                    s.Headline = "Merge into group " + targetGroupNumber + " on " + displayName + sameDriver;
                    s.Summary = eval.Feasible
                        ? "Fits with the existing route group ???? shared pickup window and drive times work."
                        : "May merge into group " + targetGroupNumber + ", but timing is tight or impossible.";
                    break;
                case ScheduleBuilderSuggestPlacementKind.NewGroupAtStart:
                    s.Headline = "New group at start of " + displayName + "'s day" + sameDriver;
                    s.Summary = eval.Feasible
                        ? "Trip starts a new first group ???? deadhead and PU/DO windows fit."
                        : "New first group on " + displayName + " ???? check timing.";
                    break;
                case ScheduleBuilderSuggestPlacementKind.NewGroupAtEnd:
                    s.Headline = "New group at end of " + displayName + "'s day" + sameDriver;
                    s.Summary = eval.Feasible
                        ? "Trip becomes a new last group ???? fits after existing routes."
                        : "New last group on " + displayName + " ???? may not fit shift/timing.";
                    break;
                default:
                    s.Headline = "New group after group " + (targetGroupNumber - 1) + " on " + displayName + sameDriver;
                    s.Summary = eval.Feasible
                        ? "Trip in its own group between existing routes ???? times and drive fit."
                        : "Separate group on " + displayName + " ???? timing may not work.";
                    break;
            }

            s.Reasons.AddRange(eval.Reasons);
            if (eval.ExtraDeadheadMeters > 0)
                s.Reasons.Add("Adds about " + FormatMiles(eval.ExtraDeadheadMeters) + " driving for this placement.");
            else if (kind == ScheduleBuilderSuggestPlacementKind.MergeIntoGroup)
                s.Reasons.Add("Minimal extra driving ???? piggybacks on the group's existing tour.");

            if (kind == ScheduleBuilderSuggestPlacementKind.MergeIntoGroup && eval.MergedGroupRiders > 0)
            {
                s.Reasons.Add("Merged group would carry " + eval.MergedGroupRiders + " rider"
                    + (eval.MergedGroupRiders == 1 ? "" : "s") + " (vehicle capacity " + capacityPassengers + ").");
            }

            if (!eval.Feasible && eval.FailureReason.Length > 0)
                s.Reasons.Add("Issue: " + eval.FailureReason);

            if (relocateOnSameDriver)
                s.Reasons.Add("Relocate on " + displayName + " ???? trip stays on this driver, different group/slot.");

            s.Reasons.Add("Trip " + tripLabel + " ??? " + displayName + " tab.");
            return s;
        }

        private sealed class PlacementEval
        {
            public bool Feasible { get; set; }
            public double Score { get; set; }
            public double ExtraDeadheadMeters { get; set; }
            public string FailureReason { get; set; } = "";
            public List<string> Reasons { get; } = new List<string>();
            public int MergedGroupRiders { get; set; }
        }

        /// <summary>Per-driver day without the focus trip ???? reused to skip unchanged prefix OSRM.</summary>
        private sealed class DriverDayBaseline
        {
            public List<string> GroupFingerprints { get; } = new List<string>();
            public List<TimeSpan> ClockAfterGroup { get; } = new List<TimeSpan>();
            public List<GeoPoint> LastDoAfterGroup { get; } = new List<GeoPoint>();
            public List<bool> HasDoAfterGroup { get; } = new List<bool>();
            public List<double> DeadheadAfterGroup { get; } = new List<double>();
            public bool Feasible { get; set; } = true;
        }

        private static async Task<DriverDayBaseline> BuildDriverDayBaselineAsync(
            IList<SupeyTripCluster> groups,
            TimeSpan shiftStart,
            Dictionary<string, GeoPoint> pickupByTrip,
            Dictionary<string, GeoPoint> dropoffByTrip,
            ScheduleBuilderDriverSuggestPrepCache prepCache,
            CancellationToken token)
        {
            var baseline = new DriverDayBaseline();
            if (groups == null || groups.Count == 0)
                return baseline;

            foreach (var g in groups)
                baseline.GroupFingerprints.Add(ScheduleBuilderDriverSuggestPrepCache.Fingerprint(g));

            await prepCache.PrewarmDriverGroupsAsync(groups, pickupByTrip, dropoffByTrip, token)
                .ConfigureAwait(false);

            TimeSpan clock = shiftStart;
            GeoPoint prevLastDo = default;
            bool hasPrevDo = false;
            double totalDeadhead = 0;

            for (int i = 0; i < groups.Count; i++)
            {
                var g = groups[i];
                ScheduleBuilderPreviewGroups.ApplyGeocodes(g, pickupByTrip, dropoffByTrip);

                GeoPoint firstPu = GetClusterFirstPickup(g);
                if (hasPrevDo && SupeyOsrmLegs.IsRoutable(prevLastDo) && SupeyOsrmLegs.IsRoutable(firstPu))
                {
                    var dh = await SupeyOsrmLegs.GetLegAsync(prevLastDo, firstPu, token).ConfigureAwait(false);
                    double sec = dh.Seconds > 0 ? dh.Seconds : EstimateLegSeconds(prevLastDo, firstPu);
                    totalDeadhead += dh.Meters > 0 ? dh.Meters : StraightMeters(prevLastDo, firstPu);
                    clock = clock.Add(TimeSpan.FromSeconds(sec));
                }
                else if (i == 0)
                {
                    clock = shiftStart;
                }

                var (ok, end, _, _) = SupeyScheduleAlgorithm.ProjectClusterFeasibilityPublic(g, clock);
                if (!ok)
                    baseline.Feasible = false;

                clock = end;
                prevLastDo = GetClusterLastDropoff(g);
                hasPrevDo = SupeyOsrmLegs.IsRoutable(prevLastDo);

                baseline.ClockAfterGroup.Add(clock);
                baseline.LastDoAfterGroup.Add(prevLastDo);
                baseline.HasDoAfterGroup.Add(hasPrevDo);
                baseline.DeadheadAfterGroup.Add(totalDeadhead);
            }

            return baseline;
        }

        private static int FindFirstChangedGroupIndex(DriverDayBaseline baseline, IList<SupeyTripCluster> trialGroups)
        {
            if (baseline?.GroupFingerprints == null || trialGroups == null || trialGroups.Count == 0)
                return 0;

            int n = Math.Min(baseline.GroupFingerprints.Count, trialGroups.Count);
            for (int i = 0; i < n; i++)
            {
                string fp = ScheduleBuilderDriverSuggestPrepCache.Fingerprint(trialGroups[i]);
                if (fp != baseline.GroupFingerprints[i])
                    return i;
            }

            if (trialGroups.Count != baseline.GroupFingerprints.Count)
                return n;

            return trialGroups.Count;
        }

        private static int FindBaselineGroupIndexByFingerprint(DriverDayBaseline baseline, SupeyTripCluster group)
        {
            if (baseline?.GroupFingerprints == null || group == null)
                return -1;

            string fp = ScheduleBuilderDriverSuggestPrepCache.Fingerprint(group);
            if (fp.Length == 0)
                return -1;

            for (int i = 0; i < baseline.GroupFingerprints.Count; i++)
            {
                if (baseline.GroupFingerprints[i] == fp)
                    return i;
            }

            return -1;
        }

        private static bool GroupContainsFocusTrip(SupeyTripCluster group, MCDownloadedTrip focusTrip)
        {
            if (group?.Trips == null || focusTrip == null)
                return false;

            string tripNum = (focusTrip.TripNumber ?? "").Trim();
            if (tripNum.Length > 0)
            {
                return group.Trips.Any(t =>
                    t != null
                    && string.Equals((t.TripNumber ?? "").Trim(), tripNum, StringComparison.OrdinalIgnoreCase));
            }

            return group.Trips.Any(t => ReferenceEquals(t, focusTrip));
        }

        private static bool PassesCheapNewGroupAfterFilter(
            DriverDayBaseline baseline,
            int afterGroupNumber,
            MCDownloadedTrip trip,
            Dictionary<string, GeoPoint> pickupByTrip)
        {
            var tripPu = SupeyTripTimes.TryParsePU(trip);
            if (!tripPu.HasValue || baseline == null)
                return true;

            int priorIdx = afterGroupNumber - 1;
            if (priorIdx < 0 || priorIdx >= baseline.ClockAfterGroup.Count)
                return true;

            // Prior batch must finish before this trip's pickup window closes (A 14 / B-C 29 min late).
            char leg = SupeyScheduleAlgorithm.DetectLegPublic(trip?.TripNumber);
            double puLateCap = leg == 'A' ? 14.0 : 29.0;
            if (baseline.ClockAfterGroup[priorIdx] > tripPu.Value.Add(TimeSpan.FromMinutes(puLateCap)))
                return false;

            string tripNum = (trip.TripNumber ?? "").Trim();
            if (tripNum.Length == 0 || !pickupByTrip.TryGetValue(tripNum, out var tripPuPt))
                return true;

            if (!baseline.HasDoAfterGroup[priorIdx])
                return true;

            return !IsDefinitelyTooLateForPickup(
                baseline.LastDoAfterGroup[priorIdx],
                baseline.ClockAfterGroup[priorIdx],
                tripPuPt,
                tripPu.Value);
        }

        private static bool PassesCheapPlacementFilter(
            IList<ScheduleBuilderPreviewLine> trialLines,
            MCDownloadedTrip trip,
            ScheduleBuilderSuggestPlacementKind kind,
            int targetGroupNumber,
            TimeSpan shiftStart,
            DriverDayBaseline baseline,
            Dictionary<string, GeoPoint> pickupByTrip)
        {
            var tripPu = SupeyTripTimes.TryParsePU(trip);
            if (!tripPu.HasValue)
                return true;

            string tripNum = (trip.TripNumber ?? "").Trim();
            if (tripNum.Length == 0 || !pickupByTrip.TryGetValue(tripNum, out var tripPuPt))
                return true;

            var groups = ScheduleBuilderPreviewGroups.BuildFromPreviewLines(trialLines);
            if (groups.Count == 0)
                return true;

            int groupIdx = Math.Max(0, Math.Min(targetGroupNumber - 1, groups.Count - 1));
            if (kind == ScheduleBuilderSuggestPlacementKind.NewGroupAtStart)
                groupIdx = 0;

            TimeSpan clock;
            GeoPoint prevDo = default;
            bool hasPrev = false;

            if (groupIdx > 0 && baseline != null && groupIdx - 1 < baseline.ClockAfterGroup.Count)
            {
                clock = baseline.ClockAfterGroup[groupIdx - 1];
                hasPrev = baseline.HasDoAfterGroup[groupIdx - 1];
                prevDo = baseline.LastDoAfterGroup[groupIdx - 1];
            }
            else
            {
                clock = shiftStart;
            }

            if (kind == ScheduleBuilderSuggestPlacementKind.MergeIntoGroup)
            {
                var g = groups[groupIdx];
                if (g?.Trips != null && g.Trips.Count > 0)
                {
                    ScheduleBuilderPreviewGroups.ApplyGeocodes(g, pickupByTrip, null);
                    GeoPoint firstPu = GetClusterFirstPickup(g);
                    if (hasPrev && SupeyOsrmLegs.IsRoutable(prevDo) && SupeyOsrmLegs.IsRoutable(firstPu))
                    {
                        if (IsDefinitelyTooLateForPickup(prevDo, clock, firstPu, g.EarliestPickup))
                            return false;
                    }
                }
                return true;
            }

            if (hasPrev && SupeyOsrmLegs.IsRoutable(prevDo) && SupeyOsrmLegs.IsRoutable(tripPuPt))
            {
                if (IsDefinitelyTooLateForPickup(prevDo, clock, tripPuPt, tripPu.Value))
                    return false;
            }

            return true;
        }

        /// <summary>Optimistic drive ???? only rejects placements that cannot fit even best-case routing.</summary>
        private static bool IsDefinitelyTooLateForPickup(
            GeoPoint from,
            TimeSpan clock,
            GeoPoint to,
            TimeSpan pickupAppt)
        {
            if (pickupAppt == TimeSpan.MaxValue)
                return false;
            double optimisticSec = StraightMeters(from, to) / 15.0;
            var arrival = clock.Add(TimeSpan.FromSeconds(optimisticSec));
            var latest = pickupAppt.Add(
                TimeSpan.FromMinutes(SupeyScheduleAlgorithm.ClusterTimeWindowMinutesPublic));
            return arrival > latest;
        }

        private static async Task<PlacementEval> EvaluateLinesAsync(
            IList<ScheduleBuilderPreviewLine> lines,
            MCDownloadedTrip focusTrip,
            ScheduleBuilderSuggestPlacementKind placementKind,
            TimeSpan shiftStart,
            Dictionary<string, GeoPoint> pickupByTrip,
            Dictionary<string, GeoPoint> dropoffByTrip,
            ScheduleBuilderDriverSuggestPrepCache prepCache,
            DriverDayBaseline baseline,
            GeoPoint? homeGeo,
            int capacityPassengers,
            CancellationToken token)
        {
            var eval = new PlacementEval { Feasible = true, Score = 0 };
            var groups = ScheduleBuilderPreviewGroups.BuildFromPreviewLines(lines);
            if (groups.Count == 0)
            {
                eval.Feasible = false;
                eval.FailureReason = "No trips on driver tab.";
                eval.Score = 1e9;
                return eval;
            }

            foreach (var g in groups)
            {
                if (g?.Trips == null)
                    continue;
                if (g.Trips.Count > capacityPassengers)
                {
                    eval.Feasible = false;
                    eval.FailureReason = "Group " + g.GroupNumber + " would have " + g.Trips.Count
                        + " riders ???? " + capacityPassengers + " passenger capacity.";
                    eval.Score = 1e9;
                    return eval;
                }
            }

            int firstChanged = FindFirstChangedGroupIndex(baseline, groups);
            if (firstChanged >= groups.Count)
            {
                eval.Score = baseline?.DeadheadAfterGroup.Count > 0
                    ? baseline.DeadheadAfterGroup[baseline.DeadheadAfterGroup.Count - 1]
                    : 0;
                eval.ExtraDeadheadMeters = eval.Score;
                return eval;
            }

            TimeSpan current = firstChanged > 0
                && baseline != null
                && firstChanged <= baseline.ClockAfterGroup.Count
                ? baseline.ClockAfterGroup[firstChanged - 1]
                : shiftStart;
            double totalDeadhead = firstChanged > 0
                && baseline != null
                && firstChanged <= baseline.DeadheadAfterGroup.Count
                ? baseline.DeadheadAfterGroup[firstChanged - 1]
                : 0;
            GeoPoint prevLastDo = firstChanged > 0
                && baseline != null
                && firstChanged <= baseline.LastDoAfterGroup.Count
                ? baseline.LastDoAfterGroup[firstChanged - 1]
                : default;
            bool hasPrevDo = firstChanged > 0
                && baseline != null
                && firstChanged <= baseline.HasDoAfterGroup.Count
                && baseline.HasDoAfterGroup[firstChanged - 1];

            for (int i = firstChanged; i < groups.Count; i++)
            {
                var g = groups[i];
                ScheduleBuilderPreviewGroups.ApplyGeocodes(g, pickupByTrip, dropoffByTrip);

                bool groupHasFocusTrip = GroupContainsFocusTrip(g, focusTrip);
                int baselineMatch = FindBaselineGroupIndexByFingerprint(baseline, g);
                bool unchangedExistingGroup = !groupHasFocusTrip && baselineMatch >= 0;

                TimeSpan arrivalAtFirstPU;
                if (i == firstChanged && i == 0
                    && homeGeo.HasValue
                    && SupeyOsrmLegs.IsRoutable(homeGeo.Value))
                {
                    GeoPoint firstPu = SupeyScheduleAlgorithm.FirstPickupGeoPublic(g);
                    if (SupeyOsrmLegs.IsRoutable(firstPu))
                    {
                        var dh = await SupeyOsrmLegs.GetLegAsync(homeGeo.Value, firstPu, token)
                            .ConfigureAwait(false);
                        double sec = dh.Seconds > 0 ? dh.Seconds : EstimateLegSeconds(homeGeo.Value, firstPu);
                        totalDeadhead += dh.Meters > 0 ? dh.Meters : StraightMeters(homeGeo.Value, firstPu);
                        arrivalAtFirstPU = shiftStart.Add(TimeSpan.FromSeconds(sec));
                        if (groupHasFocusTrip)
                        {
                            eval.Reasons.Add("About " + (sec / 60.0).ToString("0")
                                + " min from home to first pickup.");
                        }
                    }
                    else
                    {
                        arrivalAtFirstPU = shiftStart;
                    }
                }
                else if (hasPrevDo && SupeyOsrmLegs.IsRoutable(prevLastDo))
                {
                    GeoPoint firstPu = SupeyScheduleAlgorithm.FirstPickupGeoPublic(g);
                    if (SupeyOsrmLegs.IsRoutable(firstPu))
                    {
                        var dh = await SupeyOsrmLegs.GetLegAsync(prevLastDo, firstPu, token).ConfigureAwait(false);
                        double sec = dh.Seconds > 0 ? dh.Seconds : EstimateLegSeconds(prevLastDo, firstPu);
                        totalDeadhead += dh.Meters > 0 ? dh.Meters : StraightMeters(prevLastDo, firstPu);
                        arrivalAtFirstPU = current.Add(TimeSpan.FromSeconds(sec));
                        if (groupHasFocusTrip && i > 0 && dh.Seconds > 0)
                        {
                            eval.Reasons.Add("About " + (sec / 60.0).ToString("0") + " min drive from prior group "
                                + groups[i - 1].GroupNumber + " to group " + g.GroupNumber + ".");
                        }
                    }
                    else
                    {
                        arrivalAtFirstPU = current;
                    }
                }
                else
                {
                    arrivalAtFirstPU = i == 0 ? shiftStart : current;
                }

                TimeSpan scheduledFirstPu = SupeyClusterTimeSplit.MinPickupTime(g);
                double puCap = SupeyScheduleAlgorithm.LegPuLateCapMinutesPublic(g);
                if (scheduledFirstPu > TimeSpan.Zero
                    && arrivalAtFirstPU > scheduledFirstPu.Add(TimeSpan.FromMinutes(puCap)))
                {
                    eval.Feasible = false;
                    eval.FailureReason = "Would arrive about "
                        + (arrivalAtFirstPU - scheduledFirstPu).TotalMinutes.ToString("0")
                        + " min late for first pickup in group " + g.GroupNumber + ".";
                    eval.Score += 5000 + totalDeadhead;
                    break;
                }

                if (i == firstChanged && scheduledFirstPu > TimeSpan.Zero && scheduledFirstPu < shiftStart)
                {
                    eval.Feasible = false;
                    eval.FailureReason = "Trip pickup "
                        + SupeyTripTimes.FormatTimeOfDay(scheduledFirstPu)
                        + " is before driver shift start "
                        + SupeyTripTimes.FormatTimeOfDay(shiftStart) + ".";
                    eval.Score += 5000;
                    break;
                }

                if (unchangedExistingGroup
                    && baselineMatch < baseline.ClockAfterGroup.Count)
                {
                    current = baseline.ClockAfterGroup[baselineMatch];
                    prevLastDo = baseline.LastDoAfterGroup[baselineMatch];
                    hasPrevDo = baselineMatch < baseline.HasDoAfterGroup.Count
                        && baseline.HasDoAfterGroup[baselineMatch];
                    continue;
                }

                await ScheduleBuilderDriverSuggestRouting.PrepareClusterForFeasibilityAsync(
                    g, prepCache, token).ConfigureAwait(false);

                if (groupHasFocusTrip
                    && g.Trips.Count >= 2
                    && g.PickupOrder != null
                    && g.PickupOrder.Count >= 2
                    && SupeyClusterRouting.IsValidVisitOrder(g.PickupOrder, g.Trips.Count))
                {
                    bool puTourOk = await SupeyClusterRouting.PickupOrderMeetsScheduledWindowsAsync(
                        g, g.PickupOrder, token, routeStart: null, arrivalAtFirstPU)
                        .ConfigureAwait(false);
                    if (!puTourOk)
                    {
                        eval.Feasible = false;
                        eval.FailureReason = "OSRM pickup tour cannot hit every scheduled window in group "
                            + g.GroupNumber + ".";
                        eval.Score += 8000 + totalDeadhead;
                        break;
                    }
                }

                double clusterDoCap = SupeyTripTimingPolicy.DoLateCapMinutesForCluster(g);
                var (ok, end, worstIdx, lateMin) = SupeyScheduleAlgorithm.ProjectClusterFeasibilityPublic(
                    g, arrivalAtFirstPU, clusterDoCap);
                if (!ok && worstIdx >= 0 && worstIdx < g.Trips.Count && lateMin > 0)
                {
                    double cap = SupeyTripTimingPolicy.DoLateCapMinutes(g.Trips[worstIdx]);
                    if (lateMin <= cap)
                        ok = true;
                }

                if (!ok)
                {
                    eval.Feasible = false;
                    eval.FailureReason = lateMin > 0
                        ? "Drop-off would run about " + lateMin.ToString("0") + " min late in group " + g.GroupNumber + "."
                        : "Pickup/drop timing fails in group " + g.GroupNumber + ".";
                    eval.Score += 5000 + lateMin * 100 + totalDeadhead;
                }
                else
                {
                    eval.Score += totalDeadhead + g.IntraClusterMeters * 0.1;
                    if (g.Trips.Count > 1)
                    {
                        eval.Reasons.Add("Group " + g.GroupNumber + ": " + g.Trips.Count + " trips share a route.");
                        eval.MergedGroupRiders = Math.Max(eval.MergedGroupRiders, g.Trips.Count);
                    }
                }

                current = end;
                prevLastDo = ScheduleBuilderDriverMapRouting.LastDropoffPoint(g);
                hasPrevDo = SupeyOsrmLegs.IsRoutable(prevLastDo);
            }

            eval.ExtraDeadheadMeters = totalDeadhead;
            if (eval.Feasible)
                eval.Score = totalDeadhead;
            return eval;
        }

        private static bool PuSpreadAllowsMerge(SupeyTripCluster group, MCDownloadedTrip trip)
        {
            if (group?.Trips == null || group.Trips.Count == 0)
                return false;

            var tripPu = SupeyTripTimes.TryParsePU(trip);
            if (!tripPu.HasValue)
                return true;

            TimeSpan earliest = group.EarliestPickup;
            TimeSpan latest = group.LatestPickup;
            if (earliest == TimeSpan.MaxValue)
                return true;
            if (latest < earliest)
                latest = earliest;

            double clusterWindow = SupeyScheduleAlgorithm.ClusterTimeWindowMinutesPublic;

            // Trip pickup is before this group's window ???? belongs in its own earlier group, not merged in.
            if (tripPu.Value < earliest.Subtract(TimeSpan.FromMinutes(clusterWindow)))
                return false;

            // Trip is done before this group even starts pickups ???? not a shared route batch.
            var tripDo = SupeyTripTimes.TryParseDO(trip);
            if (tripDo.HasValue)
            {
                var tripDone = tripDo.Value.Add(TimeSpan.FromMinutes(MergeMinGapAfterTripDoMinutes));
                if (tripDone < earliest)
                    return false;

                // Early trip (PU before group) but finishes before group's DO appointment ???? separate run.
                if (tripPu.Value < earliest)
                {
                    TimeSpan groupDo = GetGroupDropoffAnchor(group);
                    if (groupDo != TimeSpan.MaxValue && tripDone < groupDo)
                        return false;
                }
            }

            TimeSpan spanStart = earliest < tripPu.Value ? earliest : tripPu.Value;
            TimeSpan spanEnd = latest > tripPu.Value ? latest : tripPu.Value;
            return (spanEnd - spanStart).TotalMinutes <= clusterWindow;
        }

        private static TimeSpan GetGroupDropoffAnchor(SupeyTripCluster group)
        {
            if (group?.Trips == null || group.Trips.Count == 0)
                return TimeSpan.MaxValue;

            TimeSpan anchor = TimeSpan.MinValue;
            foreach (var t in group.Trips)
            {
                if (t == null)
                    continue;
                var dof = SupeyTripTimes.TryParseDO(t);
                if (dof.HasValue && dof.Value > anchor)
                    anchor = dof.Value;
            }

            return anchor == TimeSpan.MinValue ? TimeSpan.MaxValue : anchor;
        }

        private static int DriverCapacityPassengers(SupeyDriverProfile profile) =>
            profile?.CapacityPassengers > 0 ? profile.CapacityPassengers : 4;

        /// <summary>Merge adds one rider unless the trip is already counted in this group.</summary>
        private static bool MergeFitsCapacity(SupeyTripCluster group, MCDownloadedTrip trip, int capacityPassengers)
        {
            if (group?.Trips == null || group.Trips.Count == 0)
                return true;

            int ridersAfter = group.Trips.Count;
            string tripNum = (trip?.TripNumber ?? "").Trim();
            bool alreadyInGroup;
            if (tripNum.Length > 0)
            {
                alreadyInGroup = group.Trips.Any(t =>
                    t != null && string.Equals((t.TripNumber ?? "").Trim(), tripNum, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                alreadyInGroup = group.Trips.Any(t => ReferenceEquals(t, trip));
            }

            if (!alreadyInGroup)
                ridersAfter++;

            return ridersAfter <= capacityPassengers;
        }

        /// <summary>
        /// Morning A-legs merge into the same clinic drop wave (BUILD hub key + DO time band).
        /// B/C returns merge on shared pickup hub within the cluster PU window.
        /// </summary>
        private static bool SharesMorningDropWave(SupeyTripCluster group, MCDownloadedTrip trip)
        {
            if (group?.Trips == null || group.Trips.Count == 0 || trip == null)
                return false;

            string tripKey = SupeyClusterRouting.MergeKeyForTrip(trip);
            char tripLeg = SupeyScheduleAlgorithm.DetectLegPublic(trip.TripNumber);

            if (tripLeg == 'A')
            {
                var tripDo = SupeyTripTimes.TryParseDO(trip);
                if (!tripDo.HasValue)
                    return false;

                foreach (var other in group.Trips)
                {
                    if (other == null)
                        continue;
                    if (SupeyScheduleAlgorithm.DetectLegPublic(other.TripNumber) != 'A')
                        continue;

                    string otherKey = SupeyClusterRouting.MergeKeyForTrip(other);
                    if (!string.Equals(tripKey, otherKey, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var otherDo = SupeyTripTimes.TryParseDO(other);
                    if (!otherDo.HasValue)
                        continue;

                    if (Math.Abs((tripDo.Value - otherDo.Value).TotalMinutes) <= DoAnchorMatchMaxMinutes)
                        return true;
                }

                return false;
            }

            var tripPu = SupeyTripTimes.TryParsePU(trip);
            if (!tripPu.HasValue)
                return false;

            double puWindow = SupeyScheduleAlgorithm.ClusterTimeWindowMinutesPublic;
            foreach (var other in group.Trips)
            {
                if (other == null)
                    continue;
                if (SupeyScheduleAlgorithm.DetectLegPublic(other.TripNumber) == 'A')
                    continue;

                string otherKey = SupeyClusterRouting.MergeKeyForTrip(other);
                if (!string.Equals(tripKey, otherKey, StringComparison.OrdinalIgnoreCase))
                    continue;

                var otherPu = SupeyTripTimes.TryParsePU(other);
                if (!otherPu.HasValue)
                    continue;

                if (Math.Abs((tripPu.Value - otherPu.Value).TotalMinutes) <= puWindow)
                    return true;
            }

            return false;
        }

        private static void ApplyPlacementScoreBonuses(
            PlacementEval eval,
            ScheduleBuilderSuggestPlacementKind kind,
            string displayName,
            MCDownloadedTrip trip,
            SupeyTemplateHints templateHints,
            SupeyTripCluster mergeGroup)
        {
            if (eval == null)
                return;

            if (kind == ScheduleBuilderSuggestPlacementKind.MergeIntoGroup
                && mergeGroup != null
                && SharesMorningDropWave(mergeGroup, trip))
            {
                eval.Score -= DropWaveMatchScoreBonusMeters;
                eval.Reasons.Add("Same clinic drop wave as this group — typical dispatcher merge.");
            }

            if (templateHints != null)
            {
                string tripNum = (trip?.TripNumber ?? "").Trim();
                string preferred = templateHints.PreferredDriverFor(tripNum);
                if (preferred != null
                    && string.Equals(preferred, displayName, StringComparison.OrdinalIgnoreCase))
                {
                    eval.Score -= TemplateHintScoreBonusMeters;
                    eval.Reasons.Add("Template driver for trip " + tripNum + " on " + templateHints.Weekday + ".");
                }
            }
        }

        /// <summary>
        /// Reject merge unless the trip shares a BUILD-style pickup cluster with at least one
        /// group mate (time window + PU radius). Also reject concurrent PU times that are far apart.
        /// </summary>
        private static bool MergePickupLocationsAllow(
            SupeyTripCluster group,
            MCDownloadedTrip trip,
            Dictionary<string, GeoPoint> pickupByTrip)
        {
            if (group?.Trips == null || group.Trips.Count == 0 || trip == null)
                return true;

            var tripPu = SupeyTripTimes.TryParsePU(trip);
            if (!tripPu.HasValue)
                return true;

            string tripNum = (trip.TripNumber ?? "").Trim();
            if (tripNum.Length == 0 || !pickupByTrip.TryGetValue(tripNum, out var tripPt))
                return true;
            if (!SupeyOsrmLegs.IsRoutable(tripPt))
                return true;

            double puWindow = SupeyScheduleAlgorithm.ClusterTimeWindowMinutesPublic;
            double tripPuRadius = MergePuClusterRadiusMeters(trip);
            bool sharesPickupCluster = false;

            foreach (var other in group.Trips)
            {
                if (other == null)
                    continue;
                var otherPu = SupeyTripTimes.TryParsePU(other);
                if (!otherPu.HasValue)
                    continue;

                string otherNum = (other.TripNumber ?? "").Trim();
                if (otherNum.Length == 0 || !pickupByTrip.TryGetValue(otherNum, out var otherPt))
                    continue;
                if (!SupeyOsrmLegs.IsRoutable(otherPt))
                    continue;

                double minutesApart = Math.Abs((tripPu.Value - otherPu.Value).TotalMinutes);
                if (minutesApart <= MergeConcurrentPuMaxMinutesApart
                    && StraightMeters(tripPt, otherPt) > MergeConcurrentPuMaxMeters)
                    return false;

                double otherPuRadius = MergePuClusterRadiusMeters(other);
                double pairRadius = Math.Min(tripPuRadius, otherPuRadius);
                if (minutesApart <= puWindow && StraightMeters(tripPt, otherPt) <= pairRadius)
                    sharesPickupCluster = true;
            }

            return sharesPickupCluster;
        }

        private static double MergePuClusterRadiusMeters(MCDownloadedTrip trip) =>
            SupeyScheduleAlgorithm.DetectLegPublic(trip?.TripNumber) == 'A' ? 25000.0 : 6500.0;

        private static double ChronologyPenalty(ScheduleBuilderDriverSuggestion s, MCDownloadedTrip trip)
        {
            if (s == null || trip == null)
                return 0;
            var tripPu = SupeyTripTimes.TryParsePU(trip);
            if (!tripPu.HasValue)
                return 0;
            if (s.Kind == ScheduleBuilderSuggestPlacementKind.NewGroupAtStart)
                return 0;
            if (s.Kind == ScheduleBuilderSuggestPlacementKind.MergeIntoGroup)
                return 0;
            // Prefer lower target group numbers when pickup is early (start of day).
            if (tripPu.Value < new TimeSpan(8, 0, 0))
                return s.TargetGroupNumber * 10;
            return s.TargetGroupNumber;
        }

        private static bool TryFindFirstTripLine(IList<ScheduleBuilderPreviewLine> lines, out int firstTripLine)
        {
            firstTripLine = -1;
            if (lines == null)
                return false;
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i]?.Kind == ScheduleBuilderPreviewLine.LineKind.Trip && lines[i].Trip != null)
                {
                    firstTripLine = i;
                    return true;
                }
            }
            return false;
        }

        private static List<MCDownloadedTrip> CollectTripsForGeocode(
            IReadOnlyDictionary<string, List<ScheduleBuilderPreviewLine>> linesByTab,
            MCDownloadedTrip focusTrip)
        {
            var trips = new List<MCDownloadedTrip>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Add(MCDownloadedTrip t)
            {
                if (t == null)
                    return;
                string tn = (t.TripNumber ?? "").Trim();
                if (tn.Length > 0)
                {
                    if (!seen.Add(tn))
                        return;
                }
                else if (trips.Contains(t))
                {
                    return;
                }

                trips.Add(t);
            }

            Add(focusTrip);
            if (linesByTab == null)
                return trips;

            foreach (var kv in linesByTab)
            {
                if (kv.Value == null)
                    continue;
                foreach (var line in kv.Value)
                {
                    if (line?.Kind == ScheduleBuilderPreviewLine.LineKind.Trip && line.Trip != null)
                        Add(line.Trip);
                }
            }

            return trips;
        }

        private static bool TryFindGroupLastTrip(
            IList<ScheduleBuilderPreviewLine> lines,
            int groupNumber,
            out MCDownloadedTrip lastTrip,
            out int lastTripLine)
        {
            lastTrip = null;
            lastTripLine = -1;
            int groupCount = 0;
            int segStart = -1;
            for (int i = 0; i <= lines.Count; i++)
            {
                bool boundary = i == lines.Count
                    || lines[i].Kind == ScheduleBuilderPreviewLine.LineKind.Gap
                    || lines[i].Kind == ScheduleBuilderPreviewLine.LineKind.SectionHeader
                    || lines[i].Kind == ScheduleBuilderPreviewLine.LineKind.GroupHeader;

                if (boundary && segStart >= 0)
                {
                    groupCount++;
                    if (groupCount == groupNumber)
                    {
                        for (int j = segStart; j < i; j++)
                        {
                            if (lines[j]?.Kind == ScheduleBuilderPreviewLine.LineKind.Trip)
                            {
                                lastTrip = lines[j].Trip;
                                lastTripLine = j;
                            }
                        }
                        return lastTrip != null;
                    }
                    segStart = -1;
                }

                if (i < lines.Count && lines[i].Kind == ScheduleBuilderPreviewLine.LineKind.Trip && segStart < 0)
                    segStart = i;
            }
            return false;
        }

        private static bool TryFindLineAfterGroup(IList<ScheduleBuilderPreviewLine> lines, int groupNumber, out int lineAfter)
        {
            lineAfter = lines.Count;
            int groupCount = 0;
            int segStart = -1;
            for (int i = 0; i <= lines.Count; i++)
            {
                bool boundary = i == lines.Count
                    || lines[i].Kind == ScheduleBuilderPreviewLine.LineKind.Gap
                    || lines[i].Kind == ScheduleBuilderPreviewLine.LineKind.SectionHeader
                    || lines[i].Kind == ScheduleBuilderPreviewLine.LineKind.GroupHeader;

                if (boundary && segStart >= 0)
                {
                    groupCount++;
                    if (groupCount == groupNumber)
                    {
                        lineAfter = i;
                        return true;
                    }
                    segStart = -1;
                }

                if (i < lines.Count && lines[i].Kind == ScheduleBuilderPreviewLine.LineKind.Trip && segStart < 0)
                    segStart = i;
            }
            return false;
        }

        private static bool NeedsGapBeforeNewGroup(IList<ScheduleBuilderPreviewLine> lines, int insertIndex)
        {
            if (insertIndex <= 0 || insertIndex >= lines.Count)
                return insertIndex > 0;
            var prev = lines[insertIndex - 1];
            return prev?.Kind == ScheduleBuilderPreviewLine.LineKind.Trip;
        }

        private static List<ScheduleBuilderPreviewLine> CloneLines(IList<ScheduleBuilderPreviewLine> lines)
        {
            var copy = new List<ScheduleBuilderPreviewLine>(lines.Count);
            foreach (var line in lines)
            {
                if (line == null)
                    continue;
                copy.Add(new ScheduleBuilderPreviewLine
                {
                    Kind = line.Kind,
                    Trip = line.Trip,
                    GapNoteText = line.GapNoteText,
                    GroupNoteText = line.GroupNoteText,
                    GroupNumber = line.GroupNumber,
                    SectionTitle = line.SectionTitle,
                    ReserveBandColor = line.ReserveBandColor,
                    ReroutedOnModivcare = line.ReroutedOnModivcare,
                });
            }
            return copy;
        }

        private static GeoPoint GetClusterFirstPickup(SupeyTripCluster g) =>
            g.PickupOrder.Count > 0 ? GetTripPickup(g, g.PickupOrder[0]) : default;

        private static GeoPoint GetClusterLastDropoff(SupeyTripCluster g) =>
            g.DropoffOrder.Count > 0 ? GetTripDropoff(g, g.DropoffOrder[g.DropoffOrder.Count - 1]) : default;

        private static GeoPoint GetTripPickup(SupeyTripCluster c, int tripIdx)
        {
            if (tripIdx >= 0 && tripIdx < c.PickupPoints.Count)
                return c.PickupPoints[tripIdx];
            return default;
        }

        private static GeoPoint GetTripDropoff(SupeyTripCluster c, int tripIdx)
        {
            if (tripIdx >= 0 && tripIdx < c.DropoffPoints.Count)
                return c.DropoffPoints[tripIdx];
            return default;
        }

        private static double EstimateLegSeconds(GeoPoint from, GeoPoint to) =>
            StraightMeters(from, to) / 12.0;

        private static double StraightMeters(GeoPoint a, GeoPoint b)
        {
            if (!SupeyOsrmLegs.IsRoutable(a) || !SupeyOsrmLegs.IsRoutable(b))
                return 0;
            const double R = 6371000;
            double dLat = (b.Lat - a.Lat) * Math.PI / 180;
            double dLng = (b.Lng - a.Lng) * Math.PI / 180;
            double x = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(a.Lat * Math.PI / 180) * Math.Cos(b.Lat * Math.PI / 180)
                * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
            return R * 2 * Math.Atan2(Math.Sqrt(x), Math.Sqrt(1 - x)) * 1.25;
        }

        private static string FormatMiles(double meters) =>
            (meters / 1609.344).ToString("0.#") + " mi";
    }
}
