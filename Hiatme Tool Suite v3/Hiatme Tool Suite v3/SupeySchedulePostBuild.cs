using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>After server + template merge: tour order, driver sequencing, quality warnings.</summary>
    internal static class SupeySchedulePostBuild
    {
        private const double MinRoadMetersForOsrmOk = 500.0;

        private static readonly TimeSpan BaseTourBudget = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan BaseSequenceBudget = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan MaxTourBudget = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan MaxSequenceBudget = TimeSpan.FromMinutes(8);
        private static readonly TimeSpan PerGroupTourCap = TimeSpan.FromSeconds(120);

        public static async Task FinalizeAsync(
            SupeyScheduleResult result,
            IProgress<string> progress,
            CancellationToken token)
        {
            if (result == null) return;

            var ai = HiatmeAiSettings.Load();
            HiatmeGeoSettings.Configure(ai);
            if (ai.UseServerGeo)
            {
                progress?.Report("Post-build: checking office panel for road miles…");
                await HiatmeGeoSettings.RefreshConnectivityAsync(ai, token).ConfigureAwait(false);
            }

            SupeyOsrmLegs.BeginBuildSession();
            var algo = new SupeyScheduleAlgorithm();

            result.BuildWarnings.Add(new SupeyWarning(
                SupeyWarningKind.BuildDiagnostic,
                "",
                "Post-build",
                SupeyClusterRouteBuilder.PipelineTag
                    + " — PU/DO windows; first stop uses real arrival, not forced early/on-time)."));

            progress?.Report("Post-build: geocoding assigned trips…");
            await SupeyScheduleGeocoder.HydratePlansOnlyAsync(result, token).ConfigureAwait(false);

            SupeyWarningsUtil.StripTimingFromBuild(result);
            SupeyWarningsUtil.ClearAllDriverWarnings(result);

            var plans = result.DriverPlans
                .Where(p => p?.Groups != null && p.Groups.Count > 0)
                .ToList();
            int planTotal = plans.Count;

            for (int pass = 0; pass < 2; pass++)
            {
                progress?.Report(pass == 0
                    ? "Post-build: pairing A/B partner legs…"
                    : "Post-build: re-routing after final A/B pairing…");
                SupeyPartnerLegHarmonizer.HarmonizeSchedule(result);

                foreach (var plan in plans)
                {
                    token.ThrowIfCancellationRequested();
                    PrepareDriverRouting(algo, plan);
                }

                int plansDone = 0;
                foreach (var plan in plans)
                {
                    token.ThrowIfCancellationRequested();
                    string name = plan.Driver?.Name ?? "Driver";
                    try
                    {
                        await FinalizeDriverAsync(
                            algo, plan, result, name, progress, plansDone + 1, planTotal, token)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        result.BuildWarnings.Add(new SupeyWarning(
                            SupeyWarningKind.BuildDiagnostic,
                            "",
                            "Post-build",
                            name + ": sequencing error (" + ex.Message + ")."));
                    }

                    plansDone++;
                }
            }

            progress?.Report("Post-build: verifying chronological order…");
            await EnforceChronologicalDayOrderAsync(algo, plans, result, progress, token).ConfigureAwait(false);

            progress?.Report("Post-build: sync deadheads to final group order…");
            foreach (var plan in plans)
            {
                if (plan?.Groups == null || plan.Groups.Count == 0) continue;
                await EnsureDeadheadsMatchGroupOrderAsync(algo, plan, token).ConfigureAwait(false);
            }

            progress?.Report("Post-build: final OSRM tours + deadheads for all drivers…");
            int finalDone = 0;
            foreach (var plan in plans)
            {
                if (plan?.Groups == null || plan.Groups.Count == 0) continue;
                token.ThrowIfCancellationRequested();
                await RepairDriverPlanOrderAsync(algo, plan, token).ConfigureAwait(false);

                finalDone++;
                string name = plan.Driver?.Name ?? "Driver";
                progress?.Report("Post-build: final road route " + name + " ("
                    + finalDone + "/" + plans.Count + ")…");
                try
                {
                    await RerouteDriverAfterOrderRepairAsync(algo, plan, result, name, token)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    result.BuildWarnings.Add(new SupeyWarning(
                        SupeyWarningKind.BuildDiagnostic,
                        "",
                        "Post-build",
                        name + ": final road route failed (" + ex.Message + ")."));
                }
            }

            progress?.Report("Post-build: timing warnings…");
            SupeyWarningsUtil.StripTimingFromBuild(result);
            SupeyWarningsUtil.ClearAllDriverWarnings(result);
            foreach (var plan in plans)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    await EvaluateDriverWarningsOnlyAsync(algo, plan, token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    result.BuildWarnings.Add(new SupeyWarning(
                        SupeyWarningKind.BuildDiagnostic,
                        "",
                        "Post-build",
                        (plan.Driver?.Name ?? "Driver") + ": timing check failed (" + ex.Message + ")."));
                }
            }

            SupeyTripLegConsistency.AppendSplitLegWarnings(result);
            SupeyClusterRouteAudit.AppendViolations(result);
            AppendReserveDiagnostics(result);
            SupeyWarningsUtil.StripTimingFromBuild(result);
            AppendOrderAuditFailures(plans, result);
            AppendDriversMissingRoadMiles(plans, result);
            foreach (var plan in result.DriverPlans)
            {
                if (plan == null) continue;
                plan.PreferChronologicalGroupPreview = true;
                plan.TemplateSeedGroupCount = 0;
            }
            foreach (var plan in plans)
                SupeyScheduleDeskOrder.SyncDisplayRowsToRoadOrder(plan);

            progress?.Report("Post-build: full-day feasibility gate…");
            SupeyDriverDayFeasibilityGate.ApplyToSchedule(result);

            progress?.Report(result.HasInfeasibleDriverRejection
                ? "Post-build complete — infeasible driver day(s) rejected (see Warnings)."
                : "Post-build complete (chronological order + leg timing refresh).");
        }

        private static void AppendDriversMissingRoadMiles(List<SupeyDriverPlan> plans, SupeyScheduleResult result)
        {
            if (plans == null || result == null) return;
            var names = new List<string>();
            foreach (var plan in plans)
            {
                if (plan?.Groups == null || plan.Groups.Count == 0) continue;
                if (plan.TotalMeters >= MinRoadMetersForOsrmOk && plan.TotalDriveSeconds > 0)
                    continue;
                names.Add(plan.Driver?.Name ?? "Driver");
            }
            if (names.Count == 0) return;
            result.BuildWarnings.Add(new SupeyWarning(
                SupeyWarningKind.BuildDiagnostic,
                "",
                "Post-build",
                names.Count + " driver(s) missing road miles after post-build ("
                + string.Join(", ", names) + ") — OSRM tour/sequence may have timed out; rebuild or check panel."));
        }

        private static void AppendOrderAuditFailures(List<SupeyDriverPlan> plans, SupeyScheduleResult result)
        {
            if (plans == null || result == null) return;
            foreach (var plan in plans)
            {
                if (plan == null) continue;
                var violations = SupeyScheduleOrderAudit.DescribeViolations(plan);
                if (violations.Count == 0) continue;
                string name = plan.Driver?.Name ?? "Driver";
                result.BuildWarnings.Add(new SupeyWarning(
                    SupeyWarningKind.BuildDiagnostic,
                    "",
                    "Post-build",
                    name + ": still out of chronological order — " + string.Join("; ", violations)));
            }
        }

        private static async Task EnforceChronologicalDayOrderAsync(
            SupeyScheduleAlgorithm algo,
            List<SupeyDriverPlan> plans,
            SupeyScheduleResult result,
            IProgress<string> progress,
            CancellationToken token)
        {
            var repaired = new List<SupeyDriverPlan>();
            var seen = new HashSet<SupeyDriverPlan>();
            foreach (var plan in plans)
            {
                if (plan == null) continue;
                bool groupBad = SupeyScheduleOrderAudit.PlanNeedsGroupOrderRepair(plan);
                bool rowBad = SupeyScheduleOrderAudit.AnyClusterRowsOutOfDisplayOrder(plan);
                if (!groupBad && !rowBad) continue;
                if (groupBad)
                    await algo.OrderDriverDayGroupsPublicAsync(plan, token).ConfigureAwait(false);
                if (rowBad)
                    SupeyScheduleOrderAudit.RepairPlanRowOrder(plan);
                if (seen.Add(plan))
                    repaired.Add(plan);
            }
            if (repaired.Count == 0) return;

            int done = 0;
            foreach (var plan in repaired)
            {
                token.ThrowIfCancellationRequested();
                done++;
                string name = plan.Driver?.Name ?? "Driver";
                progress?.Report("Post-build: re-routing " + name + " after order repair ("
                    + done + "/" + repaired.Count + ")…");
                try
                {
                    await RerouteDriverAfterOrderRepairAsync(algo, plan, result, name, token)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    result.BuildWarnings.Add(new SupeyWarning(
                        SupeyWarningKind.BuildDiagnostic,
                        "",
                        "Post-build",
                        name + ": re-route after order repair failed (" + ex.Message + ")."));
                }
            }

            result.BuildWarnings.Add(new SupeyWarning(
                SupeyWarningKind.BuildDiagnostic,
                "",
                "Post-build",
                "Re-ordered " + repaired.Count + " driver(s) into feasible group sequence (appointments / drive windows)."));
        }

        private static async Task RepairDriverPlanOrderAsync(
            SupeyScheduleAlgorithm algo,
            SupeyDriverPlan plan,
            CancellationToken token)
        {
            if (plan == null) return;
            if (SupeyScheduleOrderAudit.PlanNeedsGroupOrderRepair(plan))
                await algo.OrderDriverDayGroupsPublicAsync(plan, token).ConfigureAwait(false);
            SupeyScheduleOrderAudit.RepairPlanRowOrder(plan);
        }

        /// <summary>Full OSRM tours + deadheads after group/row order was repaired.</summary>
        private static async Task RerouteDriverAfterOrderRepairAsync(
            SupeyScheduleAlgorithm algo,
            SupeyDriverPlan plan,
            SupeyScheduleResult result,
            string driverName,
            CancellationToken token)
        {
            SyncAndNormalizeGroups(algo, plan);
            if (plan.Groups.Count >= 2)
                await algo.OrderDriverDayGroupsPublicAsync(plan, token).ConfigureAwait(false);
            PrepareDriverRouting(algo, plan);
            await OptimizeDriverGroupsAsync(algo, plan, result, driverName, null, token)
                .ConfigureAwait(false);

            plan.TotalMeters = 0;
            plan.TotalDriveSeconds = 0;
            plan.DeadHeads.Clear();
            plan.FirstPickup = null;
            plan.LastDropoff = null;
            plan.ReleaseTimeOfDay = null;

            await AlignClustersBeforeDeadheadsAsync(algo, plan, token).ConfigureAwait(false);
            SupeyScheduleDeskOrder.SyncDisplayRowsToRoadOrder(plan);
            await algo.SequenceDriverPublicAsync(plan, token).ConfigureAwait(false);
            await RefreshDropoffLegSecondsAsync(plan, token).ConfigureAwait(false);
            SupeyScheduleDeskOrder.SyncDisplayRowsToRoadOrder(plan);
        }

        private static async Task EvaluateDriverWarningsOnlyAsync(
            SupeyScheduleAlgorithm algo,
            SupeyDriverPlan plan,
            CancellationToken token)
        {
            bool roadOk = plan.TotalMeters >= MinRoadMetersForOsrmOk
                && plan.DeadHeads != null && plan.DeadHeads.Any(d => d.DistanceMeters > 100);
            plan.Warnings.Clear();
            plan.TripTimings.Clear();
            if (!roadOk) return;
            if (!DriverHasTimingFingerprints(plan))
                await RefreshDropoffLegSecondsAsync(plan, token).ConfigureAwait(false);
            if (!DriverHasTimingFingerprints(plan))
                return;
            algo.EvaluateWarningsAndTimingsPublic(plan);
        }

        private static TimeSpan TourBudgetFor(SupeyDriverPlan plan)
        {
            int groups = plan?.Groups?.Count ?? 0;
            // Each group can use up to PerGroupTourCap; cap total so 14-group days still finish.
            double sec = BaseTourBudget.TotalSeconds + groups * PerGroupTourCap.TotalSeconds;
            if (sec > MaxTourBudget.TotalSeconds) sec = MaxTourBudget.TotalSeconds;
            return TimeSpan.FromSeconds(Math.Max(sec, PerGroupTourCap.TotalSeconds));
        }

        private static TimeSpan SequenceBudgetFor(SupeyDriverPlan plan)
        {
            int groups = plan?.Groups?.Count ?? 0;
            double sec = BaseSequenceBudget.TotalSeconds + groups * 20.0;
            if (sec > MaxSequenceBudget.TotalSeconds) sec = MaxSequenceBudget.TotalSeconds;
            return TimeSpan.FromSeconds(sec);
        }

        /// <summary>Budget timeout (CancelAfter), not user cancel on <paramref name="userToken"/>.</summary>
        private static bool IsBudgetTimeout(OperationCanceledException ex, CancellationToken userToken)
        {
            if (userToken.IsCancellationRequested) return false;
            return ex != null;
        }

        private static async Task FinalizeDriverAsync(
            SupeyScheduleAlgorithm algo,
            SupeyDriverPlan plan,
            SupeyScheduleResult result,
            string driverName,
            IProgress<string> progress,
            int driverIndex,
            int driverTotal,
            CancellationToken token)
        {
            SyncAndNormalizeGroups(algo, plan);
            if (plan.Groups.Count >= 2)
                await algo.OrderDriverDayGroupsPublicAsync(plan, token).ConfigureAwait(false);
            SupeyScheduleDeskOrder.ApplyDeskRowSortToPlan(plan);

            bool tourBudgetHit = false;
            int groupTotal = plan.Groups.Count(g => g != null && g.Trips.Count > 0);
            int groupDone = 0;
            using (var tourBudget = new CancellationTokenSource())
            {
                tourBudget.CancelAfter(TourBudgetFor(plan));
                using (var tourLinked = CancellationTokenSource.CreateLinkedTokenSource(token, tourBudget.Token))
                {
                    try
                    {
                        await OptimizeDriverGroupsAsync(
                            algo, plan, result, driverName,
                            () =>
                            {
                                groupDone++;
                                progress?.Report("Sequencing " + driverName + " · tours "
                                    + groupDone + "/" + groupTotal + " · driver "
                                    + driverIndex + "/" + driverTotal);
                            },
                            tourLinked.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException ex) when (IsBudgetTimeout(ex, token))
                    {
                        tourBudgetHit = true;
                    }
                }
            }

            progress?.Report("Sequencing " + driverName + " · deadheads · driver "
                + driverIndex + "/" + driverTotal);

            plan.TotalMeters = 0;
            plan.TotalDriveSeconds = 0;
            plan.DeadHeads.Clear();
            plan.FirstPickup = null;
            plan.LastDropoff = null;
            plan.ReleaseTimeOfDay = null;

            await AlignClustersBeforeDeadheadsAsync(algo, plan, token).ConfigureAwait(false);

            bool sequenceOk = false;
            using (var seqBudget = new CancellationTokenSource())
            {
                seqBudget.CancelAfter(SequenceBudgetFor(plan));
                using (var seqLinked = CancellationTokenSource.CreateLinkedTokenSource(token, seqBudget.Token))
                {
                    try
                    {
                        await algo.SequenceDriverPublicAsync(plan, seqLinked.Token).ConfigureAwait(false);
                        sequenceOk = true;
                    }
                    catch (OperationCanceledException ex) when (IsBudgetTimeout(ex, token))
                    {
                        sequenceOk = false;
                    }
                }
            }

            if (sequenceOk)
                await RefreshDropoffLegSecondsAsync(plan, token).ConfigureAwait(false);

            bool roadOk = plan.TotalMeters >= MinRoadMetersForOsrmOk
                && plan.DeadHeads.Any(d => d.DistanceMeters > 100);
            if (!roadOk)
            {
                ApplyPartialRoadTotalsIfAny(plan);
                string detail = !sequenceOk
                    ? "driver OSRM sequencing timed out"
                    : tourBudgetHit
                        ? "in-group OSRM tour timed out; deadheads may be missing"
                        : "office OSRM/panel did not return road legs";
                result.BuildWarnings.Add(new SupeyWarning(
                    SupeyWarningKind.BuildDiagnostic,
                    "",
                    "Post-build",
                    driverName + ": " + detail
                    + " — fleet header uses road OSRM only (no WellRyde mile substitute)."));
            }
            else if (!DriverHasTimingFingerprints(plan))
            {
                result.BuildWarnings.Add(new SupeyWarning(
                    SupeyWarningKind.BuildDiagnostic,
                    "",
                    "Post-build",
                    driverName + ": road miles OK but in-group leg timing missing — late-DO warnings deferred."));
            }

            SupeyScheduleDeskOrder.SyncDisplayRowsToRoadOrder(plan);
        }

        private static bool DriverHasTimingFingerprints(SupeyDriverPlan plan)
        {
            if (plan?.Groups == null) return false;
            foreach (var g in plan.Groups)
            {
                if (g == null || g.Trips.Count == 0) continue;
                if (g.IntraClusterDriveSeconds <= 0) return false;
                int n = g.DropoffOrder != null && g.DropoffOrder.Count > 0
                    ? g.DropoffOrder.Count : g.Trips.Count;
                if (n > 0 && (g.DropoffLegSeconds == null || g.DropoffLegSeconds.Count < n))
                    return false;
            }
            return true;
        }

        /// <summary>When full sequencing failed, sum only legs that already have real OSRM meters.</summary>
        private static void ApplyPartialRoadTotalsIfAny(SupeyDriverPlan plan)
        {
            if (plan == null) return;
            if (plan.TotalMeters >= MinRoadMetersForOsrmOk && plan.TotalDriveSeconds > 0)
                return;

            double meters = 0;
            double seconds = 0;
            if (plan.Groups != null)
            {
                foreach (var g in plan.Groups)
                {
                    if (g == null || !g.RoadTourOptimized || g.IntraClusterMeters <= 0)
                        continue;
                    meters += g.IntraClusterMeters;
                    seconds += g.IntraClusterDriveSeconds;
                }
            }
            if (plan.DeadHeads != null)
            {
                foreach (var dh in plan.DeadHeads)
                {
                    if (dh == null || dh.DistanceMeters <= 100) continue;
                    meters += dh.DistanceMeters;
                    seconds += dh.DurationSeconds;
                }
            }
            if (meters > 0)
            {
                plan.TotalMeters = meters;
                plan.TotalDriveSeconds = seconds;
            }
            else
            {
                plan.TotalMeters = 0;
                plan.TotalDriveSeconds = 0;
            }
        }

        private static async Task OptimizeDriverGroupsAsync(
            SupeyScheduleAlgorithm algo,
            SupeyDriverPlan plan,
            SupeyScheduleResult result,
            string driverName,
            Action onGroupTourDone,
            CancellationToken token)
        {
            GeoPoint? cursor = plan.HomeGeo;
            foreach (var g in plan.Groups)
            {
                if (g == null || g.Trips.Count == 0) continue;
                token.ThrowIfCancellationRequested();
                g.RoadTourOptimized = false;
                if (!HasGeocodedStops(g))
                {
                    NoteDeskOrderOnly(result, driverName, g, "stops not geocoded");
                    onGroupTourDone?.Invoke();
                    continue;
                }

                using (var groupBudget = new CancellationTokenSource())
                {
                    groupBudget.CancelAfter(PerGroupTourCap);
                    using (var groupLinked = CancellationTokenSource.CreateLinkedTokenSource(token, groupBudget.Token))
                    {
                        try
                        {
                            await SupeyClusterOsrmTable.TryBindClusterAsync(g, groupLinked.Token)
                                .ConfigureAwait(false);
                            try
                            {
                                if (g.Trips.Count >= 2)
                                {
                                    await SupeyClusterRouteBuilder.ApplyRoadRouteAsync(
                                        g, groupLinked.Token, cursor)
                                        .ConfigureAwait(false);
                                    SupeyClusterOsrmTable.Current?.TryApplyTourMetrics(g);
                                }
                                else
                                {
                                    SupeyClusterRouting.ApplyOrdersForSingleTrip(g);
                                    g.RoadTourOptimized = true;
                                }
                                await algo.PopulateClusterPolylinePublicAsync(g, groupLinked.Token)
                                    .ConfigureAwait(false);
                                if (g.RoadTourOptimized && g.IntraClusterMeters <= 0)
                                    SupeyClusterOsrmTable.Current?.TryApplyTourMetrics(g);
                                if (!g.RoadTourOptimized)
                                {
                                    string why = SupeyClusterOsrmTable.Current != null
                                        ? "OSRM table OK but tour legs incomplete"
                                        : "OSRM table unavailable";
                                    NoteDeskOrderOnly(result, driverName, g, why);
                                }
                            }
                            finally
                            {
                                SupeyClusterOsrmTable.Clear();
                            }
                        }
                        catch (OperationCanceledException) when (token.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (OperationCanceledException)
                        {
                            NoteDeskOrderOnly(result, driverName, g, "OSRM tour timed out");
                            break;
                        }
                        catch (Exception ex)
                        {
                            NoteDeskOrderOnly(result, driverName, g, ex.Message);
                        }
                    }
                }

                onGroupTourDone?.Invoke();
                cursor = LastClusterDropoff(g) ?? cursor;
            }
        }

        /// <summary>
        /// Re-sequence when group list or row order changed without a matching deadhead rebuild
        /// (never shuffle rows without SequenceDriver — that was corrupting LateDO).
        /// </summary>
        private static async Task EnsureDeadheadsMatchGroupOrderAsync(
            SupeyScheduleAlgorithm algo,
            SupeyDriverPlan plan,
            CancellationToken token)
        {
            bool orderBad = SupeyScheduleOrderAudit.PlanNeedsOrderRepair(plan);
            bool deadheadsStale = DeadheadsStaleForGroupOrder(plan);
            if (!orderBad && !deadheadsStale) return;

            if (orderBad)
                await RepairDriverPlanOrderAsync(algo, plan, token).ConfigureAwait(false);

            await AlignClustersBeforeDeadheadsAsync(algo, plan, token).ConfigureAwait(false);
            SupeyScheduleDeskOrder.SyncDisplayRowsToRoadOrder(plan);
            plan.DeadHeads.Clear();
            plan.TotalMeters = 0;
            plan.TotalDriveSeconds = 0;
            plan.FirstPickup = null;
            plan.LastDropoff = null;
            plan.ReleaseTimeOfDay = null;
            await algo.SequenceDriverPublicAsync(plan, token).ConfigureAwait(false);
            await RefreshDropoffLegSecondsAsync(plan, token).ConfigureAwait(false);
            SupeyScheduleDeskOrder.SyncDisplayRowsToRoadOrder(plan);
        }

        /// <summary>True when inter-group deadhead labels no longer match current Groups[] order.</summary>
        private static bool DeadheadsStaleForGroupOrder(SupeyDriverPlan plan)
        {
            if (plan?.Groups == null || plan.Groups.Count == 0) return false;
            if (plan.DeadHeads == null || plan.DeadHeads.Count == 0) return true;

            bool hasHome = plan.HomeGeo.HasValue;
            int expected = hasHome ? plan.Groups.Count + 1 : plan.Groups.Count;
            if (plan.DeadHeads.Count != expected) return true;

            int dh = 0;
            if (hasHome)
            {
                string want = "Home → Group " + plan.Groups[0].GroupNumber;
                if (!LabelMatches(plan.DeadHeads[dh].Label, want)) return true;
                dh++;
            }
            else
            {
                if (!LabelMatches(plan.DeadHeads[dh].Label, "Start → Group " + plan.Groups[0].GroupNumber))
                    return true;
                dh++;
            }

            for (int i = 1; i < plan.Groups.Count; i++)
            {
                string wantLeg = "Group " + plan.Groups[i - 1].GroupNumber
                    + " → Group " + plan.Groups[i].GroupNumber;
                if (dh >= plan.DeadHeads.Count || !LabelMatches(plan.DeadHeads[dh].Label, wantLeg))
                    return true;
                dh++;
            }

            if (hasHome)
            {
                string wantHome = "Group " + plan.Groups[plan.Groups.Count - 1].GroupNumber + " → Home";
                if (dh >= plan.DeadHeads.Count || !LabelMatches(plan.DeadHeads[dh].Label, wantHome))
                    return true;
            }

            return false;
        }

        private static bool LabelMatches(string actual, string expected) =>
            string.Equals((actual ?? "").Trim(), (expected ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

        private static void SyncAndNormalizeGroups(SupeyScheduleAlgorithm algo, SupeyDriverPlan plan)
        {
            if (plan?.Groups == null) return;
            foreach (var g in plan.Groups)
            {
                if (g != null && g.Trips.Count > 0)
                    algo.SyncClusterMetadataPublic(g);
            }
            SupeyClusterTimeSplit.NormalizeSplitsOnly(plan);
        }

        private static GeoPoint? LastClusterDropoff(SupeyTripCluster g)
        {
            if (g?.DropoffPoints == null || g.DropoffPoints.Count == 0) return null;
            if (g.DropoffOrder != null && g.DropoffOrder.Count > 0)
            {
                int idx = g.DropoffOrder[g.DropoffOrder.Count - 1];
                if (idx >= 0 && idx < g.DropoffPoints.Count)
                    return g.DropoffPoints[idx];
            }
            return g.DropoffPoints[g.DropoffPoints.Count - 1];
        }

        private static void NoteDeskOrderOnly(
            SupeyScheduleResult result,
            string driverName,
            SupeyTripCluster g,
            string reason)
        {
            if (result == null || g == null) return;
            g.RoadTourOptimized = false;
            string msg = (driverName ?? "Driver") + " Group " + g.GroupNumber
                + ": not road-routed (" + reason + ") — PU/DO order may backtrack.";
            foreach (var w in result.BuildWarnings)
            {
                if (w != null && string.Equals(w.Detail, msg, StringComparison.Ordinal))
                    return;
            }
            result.BuildWarnings.Add(new SupeyWarning(
                SupeyWarningKind.BuildDiagnostic,
                "",
                "Post-build",
                msg));
        }

        private static void PrepareDriverRouting(SupeyScheduleAlgorithm algo, SupeyDriverPlan plan)
        {
            if (plan?.Groups == null) return;
            foreach (var g in plan.Groups)
            {
                if (g == null || g.Trips.Count == 0) continue;
                algo.SyncClusterMetadataPublic(g);
            }
        }

        /// <summary>OSRM PU/DO tours per group (deadheads run after all groups are routed).</summary>
        private static async Task AlignClustersBeforeDeadheadsAsync(
            SupeyScheduleAlgorithm algo,
            SupeyDriverPlan plan,
            CancellationToken token)
        {
            if (plan?.Groups == null) return;

            GeoPoint? cursor = plan.HomeGeo;
            foreach (var g in plan.Groups)
            {
                if (g == null || g.Trips.Count == 0) continue;
                algo.SyncClusterMetadataPublic(g);
                token.ThrowIfCancellationRequested();

                if (g.Trips.Count >= 2 && HasGeocodedStops(g))
                {
                    try
                    {
                        await SupeyClusterOsrmTable.TryBindClusterAsync(g, token).ConfigureAwait(false);
                        try
                        {
                            await SupeyClusterRouteBuilder.ApplyRoadRouteAsync(
                                g, token, cursor)
                                .ConfigureAwait(false);
                            SupeyClusterOsrmTable.Current?.TryApplyTourMetrics(g);
                        }
                        finally
                        {
                            SupeyClusterOsrmTable.Clear();
                        }
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch
                    {
                        g.RoadTourOptimized = false;
                    }
                }
                else if (g.Trips.Count == 1)
                    SupeyClusterRouting.ApplyOrdersForSingleTrip(g);

                try
                {
                    await algo.PopulateClusterPolylinePublicAsync(g, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    // map line optional
                }

                cursor = LastClusterDropoff(g) ?? cursor;
            }
        }

        private static async Task RefreshDropoffLegSecondsAsync(SupeyDriverPlan plan, CancellationToken token)
        {
            if (plan?.Groups == null) return;
            foreach (var g in plan.Groups)
            {
                if (g == null || g.Trips.Count < 2) continue;
                if (!HasGeocodedStops(g)) continue;
                if (g.DropoffOrder == null || g.DropoffOrder.Count == 0
                    || g.PickupOrder == null || g.PickupOrder.Count == 0)
                    continue;
                try
                {
                    await SupeyClusterOsrmTable.TryRefreshTourMetricsAsync(g, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    // timing warnings deferred for this group
                }
            }
        }

        private static bool HasGeocodedStops(SupeyTripCluster g)
        {
            if (g.Trips.Count == 0) return false;
            if (g.PickupPoints == null || g.DropoffPoints == null) return false;
            if (g.PickupPoints.Count < g.Trips.Count || g.DropoffPoints.Count < g.Trips.Count)
                return false;
            for (int i = 0; i < g.Trips.Count; i++)
            {
                if (i >= g.PickupPoints.Count || i >= g.DropoffPoints.Count) return false;
                var pu = g.PickupPoints[i];
                var dof = g.DropoffPoints[i];
                if (pu.Lat == 0 && pu.Lng == 0) return false;
                if (dof.Lat == 0 && dof.Lng == 0) return false;
            }
            return true;
        }

        private static void AppendReserveDiagnostics(SupeyScheduleResult result)
        {
            if (result == null) return;
            void Note(MCDownloadedTrip t, string reason)
            {
                if (t == null || string.IsNullOrWhiteSpace(reason)) return;
                string tn = t.TripNumber ?? "";
                string msg = "Trip " + tn + " in reserves — " + reason;
                foreach (var w in result.BuildWarnings)
                {
                    if (w != null && string.Equals(w.TripNumber, tn, StringComparison.OrdinalIgnoreCase)
                        && (w.Detail ?? "").IndexOf(reason, StringComparison.OrdinalIgnoreCase) >= 0)
                        return;
                }
                result.BuildWarnings.Add(new SupeyWarning(
                    SupeyWarningKind.UnassignedToReserves,
                    tn,
                    "Reserve",
                    msg));
            }

            foreach (var t in result.Reserves)
            {
                if (t == null) continue;
                string ooa = SupeyOutOfArea.MatchTrip(t);
                if (!string.IsNullOrEmpty(ooa))
                {
                    Note(t, "out-of-area (" + ooa + ").");
                    continue;
                }
                if (!AddressGeocoder.IsCached(t.PUStreet, t.PUCity, "ME", "", "us")
                    || !AddressGeocoder.IsCached(t.DOStreet, t.DOCITY, "ME", "", "us"))
                {
                    Note(t, "address not in geocode cache — fix address or use Fix geocode on map.");
                    continue;
                }
                var pu = SupeyTripTimes.TryParsePU(t);
                if (pu.HasValue && pu.Value.TotalHours < 6)
                    Note(t, "very early pickup — no driver fit in BUILD.");
                else
                    Note(t, "no driver fit (capacity, shift, or timing) during BUILD.");
            }
        }
    }
}
