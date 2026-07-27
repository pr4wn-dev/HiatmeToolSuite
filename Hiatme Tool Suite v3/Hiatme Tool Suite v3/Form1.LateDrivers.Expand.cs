using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Driver Habits schedule expand: show WR trip-change diffs (time / address / driver / cancel)
    /// under a trip, Trip Scout style — not routine status flips.
    /// </summary>
    partial class Form1
    {
        private static readonly Color LateDriversChangeDetailBg = Color.FromArgb(58, 58, 62);
        private static readonly Color LateDriversChangeDetailFg = Color.FromArgb(255, 220, 160);

        private readonly HashSet<string> _ldExpandedTripNos =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<HiatmeAiClient.TripScoutChangeRow>> _ldChangesByTrip =
            new Dictionary<string, List<HiatmeAiClient.TripScoutChangeRow>>(StringComparer.OrdinalIgnoreCase);
        private string _ldChangesServiceDate = "";
        private string _ldChangesHash = "";
        private readonly Dictionary<string, HiatmeAiClient.ModivcareDayTripRow> _ldMcTripsByTripNo =
            new Dictionary<string, HiatmeAiClient.ModivcareDayTripRow>(StringComparer.OrdinalIgnoreCase);
        private string _ldMcTripsServiceDate = "";
        private string _ldMcTripsHash = "";
        private bool _ldExpandSourcesAttempted;

        private static readonly HashSet<string> LateDriversScheduleChangeTags =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "sched_time_changed",
                "address_changed",
                "driver_changed",
                "cancelled",
            };

        private static readonly HashSet<string> LateDriversScheduleChangeFields =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "sched_pu_iso",
                "sched_do_iso",
                "pu_address",
                "do_address",
                "driver",
            };

        private static string LateDriversNormalizeChangeTripNo(string tripNo)
        {
            if (string.IsNullOrWhiteSpace(tripNo))
                return "";
            return tripNo.Trim().TrimStart('+');
        }

        /// <summary>
        /// TimeSpan custom formats reject <c>HH</c> (DateTime-only) — use explicit clock parts.
        /// </summary>
        private static string LateDriversFormatTimeSpanClock(TimeSpan t)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:00}:{1:00}:{2:00}",
                t.Hours,
                t.Minutes,
                t.Seconds);
        }

        private static bool LateDriversIsScheduleRelevantChange(HiatmeAiClient.TripScoutChangeRow row)
        {
            if (row == null)
                return false;

            string kind = (row.Kind ?? "").Trim().ToLowerInvariant();
            if (kind == "removed")
                return true;

            var tags = row.Tags;
            if (tags != null)
            {
                foreach (var tag in tags)
                {
                    if (string.IsNullOrWhiteSpace(tag))
                        continue;
                    string t = tag.Trim();
                    if (LateDriversScheduleChangeTags.Contains(t))
                        return true;
                    // Legacy tag when sched fields moved.
                    if (string.Equals(t, "time_changed", StringComparison.OrdinalIgnoreCase)
                        && LateDriversChangeHasScheduleField(row))
                        return true;
                }
            }

            return LateDriversChangeHasScheduleField(row);
        }

        private static bool LateDriversChangeHasScheduleField(HiatmeAiClient.TripScoutChangeRow row)
        {
            if (row?.Fields == null)
                return false;
            foreach (var f in row.Fields)
            {
                if (f == null || string.IsNullOrWhiteSpace(f.Field))
                    continue;
                if (LateDriversScheduleChangeFields.Contains(f.Field.Trim()))
                    return true;
            }
            return false;
        }

        /// <summary>Drop status/actual-only fields so expand text stays schedule-focused.</summary>
        private static HiatmeAiClient.TripScoutChangeRow LateDriversScheduleChangeView(
            HiatmeAiClient.TripScoutChangeRow row)
        {
            if (row == null)
                return null;

            bool keepAllFields = false;
            if (row.Tags != null)
            {
                foreach (var tag in row.Tags)
                {
                    if (string.Equals(tag, "cancelled", StringComparison.OrdinalIgnoreCase))
                    {
                        keepAllFields = true;
                        break;
                    }
                }
            }
            string kind = (row.Kind ?? "").Trim().ToLowerInvariant();
            if (kind == "removed" || kind == "added")
                keepAllFields = true;

            if (keepAllFields || row.Fields == null || row.Fields.Count == 0)
                return row;

            var filtered = row.Fields
                .Where(f => f != null
                    && !string.IsNullOrWhiteSpace(f.Field)
                    && LateDriversScheduleChangeFields.Contains(f.Field.Trim()))
                .ToList();
            if (filtered.Count == 0 || filtered.Count == row.Fields.Count)
                return row;

            return new HiatmeAiClient.TripScoutChangeRow
            {
                Ts = row.Ts,
                ServiceDate = row.ServiceDate,
                TripNo = row.TripNo,
                Client = row.Client,
                Driver = row.Driver,
                Kind = row.Kind,
                Tags = row.Tags,
                Summary = row.Summary,
                Fields = filtered,
            };
        }

        private void RebuildLateDriversChangesByTrip(IEnumerable<HiatmeAiClient.TripScoutChangeRow> changes)
        {
            _ldChangesByTrip.Clear();
            if (changes == null)
                return;

            foreach (var row in changes)
            {
                if (!LateDriversIsScheduleRelevantChange(row))
                    continue;
                string key = LateDriversNormalizeChangeTripNo(row.TripNo);
                if (key.Length == 0)
                    continue;
                if (!_ldChangesByTrip.TryGetValue(key, out var list))
                {
                    list = new List<HiatmeAiClient.TripScoutChangeRow>();
                    LateDriversIndexChangeList(key, list);
                }
                list.Add(LateDriversScheduleChangeView(row) ?? row);
            }

            // Deduped list instances (aliases share references).
            var seen = new HashSet<List<HiatmeAiClient.TripScoutChangeRow>>();
            foreach (var list in _ldChangesByTrip.Values)
            {
                if (list == null || !seen.Add(list))
                    continue;
                list.Sort((a, b) => (a?.Ts ?? 0).CompareTo(b?.Ts ?? 0));
            }
        }

        private void LateDriversIndexChangeList(
            string primaryKey,
            List<HiatmeAiClient.TripScoutChangeRow> list)
        {
            if (string.IsNullOrWhiteSpace(primaryKey) || list == null)
                return;

            void Put(string k)
            {
                k = LateDriversNormalizeChangeTripNo(k);
                if (k.Length == 0)
                    return;
                _ldChangesByTrip[k] = list;
            }

            Put(primaryKey);
            Put(TripScoutCanonicalTripNo(primaryKey));
            Put(ScheduleBuilderModivcareTripMatch.NormalizeTripNumber(primaryKey));
            Put(ScheduleBuilderPreviewDrag.TripLegKey(primaryKey));
            Put(WellRydeFilterDataParser.FormatTripIdForScheduleMatch(primaryKey));
        }

        private async Task RefreshLateDriversScheduleChangesAsync(
            HiatmeAiSettings settings,
            string serviceDateIso,
            CancellationToken cancellationToken = default)
        {
            string sd = (serviceDateIso ?? "").Trim();
            if (settings == null || sd.Length == 0)
                return;

            try
            {
                var changesTask = HiatmeAiClient.GetTripScoutDayChangesAsync(
                    settings, sd, cancellationToken: cancellationToken);
                var mcTask = HiatmeAiClient.GetModivcareDayStatusAsync(
                    settings, sd, includeTrips: true, cancellationToken);
                await Task.WhenAll(changesTask, mcTask).ConfigureAwait(true);

                var payload = await changesTask.ConfigureAwait(true);
                if (payload != null && payload.Ok)
                {
                    string hash = payload.ContentHash ?? "";
                    if (!(string.Equals(sd, _ldChangesServiceDate, StringComparison.Ordinal)
                        && string.Equals(hash, _ldChangesHash ?? "", StringComparison.Ordinal)
                        && _ldChangesByTrip.Count > 0))
                    {
                        _ldChangesServiceDate = sd;
                        _ldChangesHash = hash;
                        RebuildLateDriversChangesByTrip(payload.Changes);
                        LateDriversPruneExpandedTrips();
                    }

                    ProcessLateDriversCancelAlerts(payload.Changes, sd);
                }

                var mc = await mcTask.ConfigureAwait(true);
                if (mc != null && mc.Ok)
                {
                    string mcHash = mc.ContentHash ?? "";
                    if (!(string.Equals(sd, _ldMcTripsServiceDate, StringComparison.Ordinal)
                        && string.Equals(mcHash, _ldMcTripsHash ?? "", StringComparison.Ordinal)
                        && _ldMcTripsByTripNo.Count > 0))
                    {
                        _ldMcTripsServiceDate = sd;
                        _ldMcTripsHash = mcHash;
                        RebuildLateDriversMcTripsByTrip(mc.Trips);
                        LateDriversPruneExpandedTrips();
                    }
                }

                _ldExpandSourcesAttempted = true;
            }
            catch
            {
                // Expand is optional — don't fail the habits refresh.
                _ldExpandSourcesAttempted = true;
            }
        }

        /// <summary>
        /// First load baselines cancel ts (no alert). Later cancels: amber blink, sound,
        /// expand, and queue a jump to the newest trip after the list rebinds.
        /// </summary>
        private void ProcessLateDriversCancelAlerts(
            IEnumerable<HiatmeAiClient.TripScoutChangeRow> changes,
            string serviceDateIso)
        {
            string sd = (serviceDateIso ?? "").Trim();
            if (string.IsNullOrEmpty(sd) || changes == null)
                return;

            if (!string.Equals(sd, _ldCancelAlertServiceDate, StringComparison.Ordinal))
            {
                _ldCancelAlertServiceDate = sd;
                _ldCancelAlertCursorTs = 0;
                _ldCancelAlertNeedsBaseline = true;
                _ldCancelHotUntil.Clear();
                _ldPendingCancelFocusTrip = null;
            }

            var cancels = changes
                .Where(r => r != null && LateDriversChangeIsCancelAlert(r))
                .ToList();

            if (_ldCancelAlertNeedsBaseline)
            {
                _ldCancelAlertNeedsBaseline = false;
                _ldCancelAlertCursorTs = cancels.Count == 0
                    ? 0
                    : cancels.Max(r => r.Ts ?? 0);
                return;
            }

            var incoming = cancels
                .Where(r => (r.Ts ?? 0) > _ldCancelAlertCursorTs)
                .OrderByDescending(r => r.Ts ?? 0)
                .ToList();
            if (incoming.Count == 0)
                return;

            double maxTs = incoming.Max(r => r.Ts ?? 0);
            if (maxTs > _ldCancelAlertCursorTs)
                _ldCancelAlertCursorTs = maxTs;

            DateTime until = DateTime.UtcNow.AddSeconds(LateDriversAlertWindowSeconds);
            string focusTrip = null;
            foreach (var row in incoming)
            {
                string trip = LateDriversNormalizeChangeTripNo(row.TripNo);
                if (trip.Length == 0)
                    continue;

                _ldCancelHotUntil[trip] = until;
                string canon = LateDriversCanonicalChangeTripKey(trip);
                if (canon.Length > 0 && !string.Equals(canon, trip, StringComparison.OrdinalIgnoreCase))
                    _ldCancelHotUntil[canon] = until;

                if (canon.Length > 0)
                    _ldExpandedTripNos.Add(canon);
                else
                    _ldExpandedTripNos.Add(trip);

                if (focusTrip == null)
                    focusTrip = trip;
            }

            if (string.IsNullOrEmpty(focusTrip))
                return;

            _ldPendingCancelFocusTrip = focusTrip;
            TryPlayLateDriversCancelSoundOnce();
            SyncLateDriversDriverAlertBlink();
        }

        private static bool LateDriversChangeIsCancelAlert(HiatmeAiClient.TripScoutChangeRow row)
        {
            if (row == null)
                return false;
            string kind = (row.Kind ?? "").Trim().ToLowerInvariant();
            if (kind == "removed")
                return true;
            return TripScoutChangeIsCancelled(row);
        }

        /// <summary>
        /// Jump to the newest cancel after BindLateDriversTripPane so selection sticks.
        /// </summary>
        private void FlushLateDriversPendingCancelFocus()
        {
            string trip = (_ldPendingCancelFocusTrip ?? "").Trim();
            _ldPendingCancelFocusTrip = null;
            if (trip.Length == 0)
                return;

            try
            {
                GoToLateDriversTripSearch(trip);
                FocusLateDriversTripInList(trip);
                SetLateDriversStatus("Status: Cancelled trip " + trip);
            }
            catch
            {
                /* navigation best-effort */
            }
        }

        private void RebuildLateDriversMcTripsByTrip(
            IEnumerable<HiatmeAiClient.ModivcareDayTripRow> trips)
        {
            _ldMcTripsByTripNo.Clear();
            if (trips == null)
                return;

            foreach (var t in trips)
            {
                if (t == null || string.IsNullOrWhiteSpace(t.TripNumber))
                    continue;
                IndexLateDriversMcTrip(t);
            }
        }

        private void IndexLateDriversMcTrip(HiatmeAiClient.ModivcareDayTripRow t)
        {
            if (t == null || string.IsNullOrWhiteSpace(t.TripNumber))
                return;

            void Put(string raw)
            {
                string key = LateDriversNormalizeChangeTripNo(raw);
                if (key.Length == 0)
                    return;
                if (_ldMcTripsByTripNo.TryGetValue(key, out var existing)
                    && existing != null
                    && !ReferenceEquals(existing, t))
                {
                    // Prefer row with more clock fields filled.
                    int scoreNew = LateDriversMcTripScore(t);
                    int scoreOld = LateDriversMcTripScore(existing);
                    if (scoreOld >= scoreNew)
                        return;
                }
                _ldMcTripsByTripNo[key] = t;
            }

            string primary = t.TripNumber.Trim();
            Put(primary);
            Put(TripScoutCanonicalTripNo(primary));
            Put(ScheduleBuilderModivcareTripMatch.NormalizeTripNumber(primary));
            Put(ScheduleBuilderPreviewDrag.TripLegKey(primary));
            Put(WellRydeFilterDataParser.FormatTripIdForScheduleMatch(primary));
        }

        private static int LateDriversMcTripScore(HiatmeAiClient.ModivcareDayTripRow t)
        {
            if (t == null) return 0;
            return (!string.IsNullOrWhiteSpace(t.PuTime) ? 1 : 0)
                + (!string.IsNullOrWhiteSpace(t.DoTime) ? 1 : 0)
                + (!string.IsNullOrWhiteSpace(t.SchedDoTime) ? 1 : 0);
        }

        private HiatmeAiClient.ModivcareDayTripRow FindLateDriversMcTrip(string tripNo)
        {
            if (_ldMcTripsByTripNo.Count == 0 || string.IsNullOrWhiteSpace(tripNo))
                return null;

            string raw = LateDriversNormalizeChangeTripNo(tripNo);
            if (_ldMcTripsByTripNo.TryGetValue(raw, out var byRaw))
                return byRaw;

            foreach (var alt in new[]
            {
                TripScoutCanonicalTripNo(raw),
                ScheduleBuilderModivcareTripMatch.NormalizeTripNumber(raw),
                ScheduleBuilderPreviewDrag.TripLegKey(raw),
                WellRydeFilterDataParser.FormatTripIdForScheduleMatch(raw),
            })
            {
                string a = LateDriversNormalizeChangeTripNo(alt);
                if (a.Length == 0) continue;
                if (_ldMcTripsByTripNo.TryGetValue(a, out var hit))
                    return hit;
            }

            foreach (var kv in _ldMcTripsByTripNo)
            {
                if (kv.Value == null) continue;
                if (LateDriversTripNosEqualForChip(raw, kv.Key)
                    || LateDriversTripNosEqualForChip(raw, kv.Value.TripNumber))
                    return kv.Value;
            }
            return null;
        }

        /// <summary>
        /// If changes were never loaded (opened a driver before refresh finished), fetch then rebind.
        /// </summary>
        private void LateDriversEnsureChangesLoadedForBind(string serviceDateIso)
        {
            string sd = (serviceDateIso ?? "").Trim();
            string mode = LateDriversSelectedMode();
            if (mode != "day" && mode != "live")
                return;
            if (sd.Length == 0)
                return;
            if (string.Equals(sd, _ldChangesServiceDate, StringComparison.Ordinal)
                && _ldExpandSourcesAttempted
                && (string.Equals(sd, _ldMcTripsServiceDate, StringComparison.Ordinal)
                    || _ldChangesByTrip.Count > 0
                    || _ldMcTripsByTripNo.Count > 0))
                return;

            var settings = LateDriversAiSettings();
            if (settings == null || string.IsNullOrWhiteSpace(settings.BaseUrl))
                return;

            _ = LateDriversLoadChangesThenRebindAsync(settings, sd);
        }

        private async Task LateDriversLoadChangesThenRebindAsync(
            HiatmeAiSettings settings,
            string serviceDateIso)
        {
            try
            {
                // Force rebuild even if a prior empty/partial hash was cached.
                _ldChangesHash = "";
                _ldMcTripsHash = "";
                _ldExpandSourcesAttempted = false;
                await RefreshLateDriversScheduleChangesAsync(settings, serviceDateIso)
                    .ConfigureAwait(true);
                if (IsDisposed || ldTripLv == null || ldTripLv.IsDisposed)
                    return;
                if (InvokeRequired)
                    BeginInvoke(new Action(BindLateDriversTripPane));
                else
                    BindLateDriversTripPane();
            }
            catch { }
        }

        private void LateDriversPruneExpandedTrips()
        {
            if (_ldExpandedTripNos.Count == 0)
                return;
            var stale = _ldExpandedTripNos
                .Where(key => LateDriversScheduleChangeCount(key) <= 0)
                .ToList();
            foreach (var key in stale)
                _ldExpandedTripNos.Remove(key);
        }

        /// <summary>Resolve journal + print-vs-WR schedule diffs for a trip.</summary>
        private List<HiatmeAiClient.TripScoutChangeRow> LateDriversChangesForTrip(string tripNo)
        {
            string key = LateDriversNormalizeChangeTripNo(tripNo);
            if (key.Length == 0)
                return null;

            var merged = new List<HiatmeAiClient.TripScoutChangeRow>();
            var journal = LateDriversJournalChangesForTrip(key);
            if (journal != null && journal.Count > 0)
                merged.AddRange(journal);

            bool journalHasSched = false;
            bool journalHasDriver = false;
            foreach (var row in merged)
            {
                if (row?.Tags == null) continue;
                foreach (var tag in row.Tags)
                {
                    if (string.Equals(tag, "sched_time_changed", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(tag, "time_changed", StringComparison.OrdinalIgnoreCase))
                        journalHasSched = true;
                    if (string.Equals(tag, "driver_changed", StringComparison.OrdinalIgnoreCase))
                        journalHasDriver = true;
                }
            }

            // WR often already has the new clock on first pull — never journals sched_time_changed —
            // and the printed workbook may already match WR. Compare Modivcare snapshot vs live WR
            // (e.g. Cutler MC 6:55 → WR 7:25).
            if (!journalHasSched)
            {
                var mcDiff = LateDriversBuildMcVsWrSchedChange(key);
                if (mcDiff != null)
                    merged.Insert(0, mcDiff);
            }

            // Print sheet owner ≠ live WR driver (reassign) — expandable even when WR never
            // journaled driver_changed (already on the new driver at first pull).
            if (!journalHasDriver)
            {
                var driverDiff = LateDriversBuildPrintVsWrDriverChange(key);
                if (driverDiff != null)
                    merged.Insert(0, driverDiff);
            }

            return merged.Count > 0 ? merged : null;
        }

        private List<HiatmeAiClient.TripScoutChangeRow> LateDriversJournalChangesForTrip(string tripNo)
        {
            string key = LateDriversNormalizeChangeTripNo(tripNo);
            if (key.Length == 0 || _ldChangesByTrip.Count == 0)
                return null;

            if (_ldChangesByTrip.TryGetValue(key, out var exact) && exact != null && exact.Count > 0)
                return exact;

            foreach (var alt in new[]
            {
                TripScoutCanonicalTripNo(key),
                ScheduleBuilderModivcareTripMatch.NormalizeTripNumber(key),
                ScheduleBuilderPreviewDrag.TripLegKey(key),
                WellRydeFilterDataParser.FormatTripIdForScheduleMatch(key),
            })
            {
                string a = LateDriversNormalizeChangeTripNo(alt);
                if (a.Length == 0)
                    continue;
                if (_ldChangesByTrip.TryGetValue(a, out var hit) && hit != null && hit.Count > 0)
                    return hit;
            }

            string norm = ScheduleBuilderModivcareTripMatch.NormalizeTripNumber(key);
            string leg = ScheduleBuilderPreviewDrag.TripLegKey(key);
            foreach (var kv in _ldChangesByTrip)
            {
                if (kv.Value == null || kv.Value.Count == 0)
                    continue;
                string cand = kv.Key;
                if (TripScoutTripNosMatch(cand, key))
                    return kv.Value;
                if (norm.Length > 0
                    && string.Equals(
                        ScheduleBuilderModivcareTripMatch.NormalizeTripNumber(cand),
                        norm,
                        StringComparison.OrdinalIgnoreCase))
                    return kv.Value;
                if (leg.Length > 0
                    && ScheduleBuilderPreviewDrag.TripLegKeysMatch(cand, key))
                    return kv.Value;
            }
            return null;
        }

        /// <summary>
        /// Synthetic sched change when Modivcare day clocks differ from live WellRyde.
        /// Workbook print often already matches WR after mid-day edits — MC is the baseline.
        /// Ignores the common ~8 minute DO representation drift.
        /// </summary>
        private HiatmeAiClient.TripScoutChangeRow LateDriversBuildMcVsWrSchedChange(string tripNo)
        {
            var wr = FindLateDriversWrTrip(tripNo);
            var mc = FindLateDriversMcTrip(tripNo);
            if (wr == null || mc == null)
                return null;

            var fields = new List<HiatmeAiClient.TripScoutChangeFieldRow>();
            string sd = LateDriversSelectedServiceDateIso();
            if (string.IsNullOrWhiteSpace(sd))
                sd = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            TimeSpan? mcPu = SupeyTripTimes.TryParse(mc.PuTime);
            if (mcPu.HasValue && TryParseLateDriversIso(wr.SchedPuIso, out var wrPuDt))
            {
                double mins = (wrPuDt.TimeOfDay - mcPu.Value).TotalMinutes;
                if (Math.Abs(mins) >= 1.0)
                {
                    fields.Add(new HiatmeAiClient.TripScoutChangeFieldRow
                    {
                        Field = "sched_pu_iso",
                        Before = sd + "T" + LateDriversFormatTimeSpanClock(mcPu.Value),
                        After = wr.SchedPuIso,
                    });
                }
            }

            // Match scoreboard DO: A-leg prefers appointment (do_time), else scheduled_dropoff.
            bool aLeg = McTripTimingRules.IsALeg(mc.TripNumber ?? tripNo);
            TimeSpan? mcDo = aLeg
                ? (SupeyTripTimes.TryParse(mc.DoTime) ?? SupeyTripTimes.TryParse(mc.SchedDoTime))
                : (SupeyTripTimes.TryParse(mc.SchedDoTime) ?? SupeyTripTimes.TryParse(mc.DoTime));
            if (mcDo.HasValue
                && mcDo.Value != TimeSpan.Zero
                && TryParseLateDriversIso(wr.SchedDoIso, out var wrDoDt))
            {
                double mins = (wrDoDt.TimeOfDay - mcDo.Value).TotalMinutes;
                if (Math.Abs(mins) >= 10.0)
                {
                    fields.Add(new HiatmeAiClient.TripScoutChangeFieldRow
                    {
                        Field = "sched_do_iso",
                        Before = sd + "T" + LateDriversFormatTimeSpanClock(mcDo.Value),
                        After = wr.SchedDoIso,
                    });
                }
            }

            if (fields.Count == 0)
                return null;

            return new HiatmeAiClient.TripScoutChangeRow
            {
                ServiceDate = sd,
                TripNo = LateDriversNormalizeChangeTripNo(tripNo),
                Client = wr.Client ?? mc.Client,
                Driver = wr.Driver ?? mc.Driver,
                Kind = "updated",
                Tags = new List<string> { "sched_time_changed", "time_changed", "mc_vs_wr", "updated" },
                Fields = fields,
                Summary = "Schedule time differs from Modivcare",
            };
        }

        /// <summary>
        /// Synthetic driver change when printed sheet owner differs from live WellRyde driver.
        /// </summary>
        private HiatmeAiClient.TripScoutChangeRow LateDriversBuildPrintVsWrDriverChange(string tripNo)
        {
            var wr = FindLateDriversWrTrip(tripNo);
            if (wr == null)
                return null;

            string wrDriver = (wr.Driver ?? "").Trim();
            bool wrUnassigned = string.IsNullOrEmpty(wrDriver)
                || wrDriver.Equals("(unassigned)", StringComparison.OrdinalIgnoreCase)
                || wrDriver.IndexOf("unassign", StringComparison.OrdinalIgnoreCase) >= 0;

            string sheetOwner = FindLateDriversWorkbookOwnerForTrip(tripNo) ?? "";
            bool sheetEmpty = string.IsNullOrWhiteSpace(sheetOwner)
                || LateDriversIsReservesTabName(sheetOwner);

            string before = sheetEmpty ? null : sheetOwner.Trim();
            string after = wrUnassigned ? null : wrDriver;

            if (string.IsNullOrWhiteSpace(before) && string.IsNullOrWhiteSpace(after))
                return null;
            if (!string.IsNullOrWhiteSpace(before)
                && !string.IsNullOrWhiteSpace(after)
                && LateDriversDriverNamesMatch(before, after))
                return null;

            string sd = LateDriversSelectedServiceDateIso();
            if (string.IsNullOrWhiteSpace(sd))
                sd = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            return new HiatmeAiClient.TripScoutChangeRow
            {
                ServiceDate = sd,
                TripNo = LateDriversNormalizeChangeTripNo(tripNo),
                Client = wr.Client,
                Driver = wrDriver,
                Kind = "updated",
                Tags = new List<string> { "driver_changed", "print_vs_wr", "updated" },
                Fields = new List<HiatmeAiClient.TripScoutChangeFieldRow>
                {
                    new HiatmeAiClient.TripScoutChangeFieldRow
                    {
                        Field = "driver",
                        Before = before,
                        After = after,
                    },
                },
                Summary = "Driver differs from printed sheet",
            };
        }

        private int LateDriversScheduleChangeCount(string tripNo)
        {
            var list = LateDriversChangesForTrip(tripNo);
            return list?.Count ?? 0;
        }

        private bool LateDriversTripHasExpandableChanges(string tripNo)
            => LateDriversScheduleChangeCount(tripNo) > 0;

        private bool LateDriversTripIsExpanded(string tripNo)
        {
            string key = LateDriversCanonicalChangeTripKey(tripNo);
            return key.Length > 0 && _ldExpandedTripNos.Contains(key);
        }

        /// <summary>Stable expand-set key (prefer journal trip_no when fuzzy-matched).</summary>
        private string LateDriversCanonicalChangeTripKey(string tripNo)
        {
            string key = LateDriversNormalizeChangeTripNo(tripNo);
            if (key.Length == 0)
                return "";
            if (_ldChangesByTrip.ContainsKey(key))
                return key;
            var journal = LateDriversJournalChangesForTrip(key);
            if (journal != null && journal.Count > 0)
            {
                string fromRow = LateDriversNormalizeChangeTripNo(journal[0]?.TripNo);
                if (fromRow.Length > 0)
                    return fromRow;
            }
            return key;
        }

        private void LateDriversToggleTripExpanded(string tripNo)
        {
            string key = LateDriversCanonicalChangeTripKey(tripNo);
            if (key.Length == 0 || LateDriversScheduleChangeCount(key) <= 0)
                return;

            if (_ldExpandedTripNos.Contains(key))
                _ldExpandedTripNos.Remove(key);
            else
                _ldExpandedTripNos.Add(key);

            BindLateDriversTripPane();
        }

        private bool LateDriversTryToggleExpandFromListItem(ListViewItem item)
        {
            if (item == null)
                return false;
            if (item.Tag is LateDriversTripRowTag detail
                && (detail.IsChangeDetail || detail.IsGroupHeader || detail.IsGap))
                return false;

            string tripNo = null;
            if (item.Tag is LateDriversTripRowTag row)
                tripNo = row.TripNo;
            else
                tripNo = LateDriversHabitFromTag(item.Tag)?.TripNo;

            tripNo = LateDriversNormalizeChangeTripNo(tripNo);
            if (!LateDriversTripHasExpandableChanges(tripNo))
                return false;

            LateDriversToggleTripExpanded(tripNo);
            return true;
        }

        /// <summary>Owner-draw Trip column: always show ▶/▼ from the change journal.</summary>
        private string LateDriversTripColumnDisplayText(ListViewItem item, int columnIndex, string raw)
        {
            if (ldTripLv == null || ldTripLv.IsDisposed || item == null)
                return raw ?? "";
            if (columnIndex < 0 || columnIndex >= ldTripLv.Columns.Count)
                return raw ?? "";
            if (!string.Equals(ldTripLv.Columns[columnIndex].Text, "Trip", StringComparison.OrdinalIgnoreCase))
                return raw ?? "";

            string tripNo = null;
            if (item.Tag is LateDriversTripRowTag tag)
            {
                if (tag.IsChangeDetail || tag.IsGroupHeader || tag.IsGap)
                    return raw ?? "";
                tripNo = tag.TripNo;
            }
            else
            {
                tripNo = LateDriversHabitFromTag(item.Tag)?.TripNo;
            }

            int n = LateDriversScheduleChangeCount(tripNo);
            if (n <= 0)
                return raw ?? "";

            bool expanded = LateDriversTripIsExpanded(tripNo);
            string prefix = expanded ? "▼ " : "▶ ";
            string body = LateDriversStripExpandChromeText(raw);
            if (string.IsNullOrWhiteSpace(body))
                body = LateDriversNormalizeChangeTripNo(tripNo);
            return prefix + body;
        }

        private static string LateDriversStripExpandChromeText(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "";
            string s = raw.Trim();
            if (s.StartsWith("▼ ", StringComparison.Ordinal) || s.StartsWith("▶ ", StringComparison.Ordinal))
                s = s.Substring(2).TrimStart();
            if (s.StartsWith("+", StringComparison.Ordinal))
                s = s.Substring(1).TrimStart();
            // Drop trailing " (N)" change count from older builds.
            int idx = s.LastIndexOf(" (", StringComparison.Ordinal);
            if (idx > 0 && s.EndsWith(")", StringComparison.Ordinal))
            {
                string maybe = s.Substring(idx + 2, s.Length - idx - 3);
                if (int.TryParse(maybe, out _))
                    s = s.Substring(0, idx).TrimEnd();
            }
            return s;
        }

        private ListViewItem CreateLateDriversChangeDetailItem(
            HiatmeAiClient.TripScoutChangeRow change,
            string tripNo,
            bool showDriver)
        {
            string when = LateDriversFormatChangeTime(change?.Ts);
            string headline = TripScoutChangeFormat.FormatHeadline(change) ?? "Updated";
            string diff = TripScoutChangeFormat.FormatDiff(change);

            var tag = new LateDriversTripRowTag
            {
                IsChangeDetail = true,
                ChangeEvent = change,
                TripNo = LateDriversNormalizeChangeTripNo(tripNo),
                ServiceDate = change?.ServiceDate ?? _ldChangesServiceDate,
                Client = change?.Client ?? "",
                DriverDisplay = change?.Driver ?? "",
            };

            var item = new ListViewItem("↳");
            item.UseItemStyleForSubItems = false;
            item.Tag = tag;
            item.SubItems.Add(when);
            item.SubItems.Add(headline);
            if (showDriver)
                item.SubItems.Add(string.IsNullOrWhiteSpace(tag.DriverDisplay) ? "—" : tag.DriverDisplay);
            item.SubItems.Add(""); // Habit
            item.SubItems.Add(diff); // Client column holds the change summary
            item.SubItems.Add(""); // PU Street
            item.SubItems.Add(""); // PU City
            item.SubItems.Add(""); // Sched PU
            item.SubItems.Add(""); // Actual PU
            item.SubItems.Add(""); // DO Street
            item.SubItems.Add(""); // DO City
            item.SubItems.Add(""); // Sched DO
            item.SubItems.Add(""); // Actual DO
            item.SubItems.Add(""); // Mins
            item.SubItems.Add(""); // Status
            item.SubItems.Add(""); // State
            item.BackColor = LateDriversChangeDetailBg;
            item.ForeColor = LateDriversChangeDetailFg;
            foreach (ListViewItem.ListViewSubItem si in item.SubItems)
            {
                si.BackColor = LateDriversChangeDetailBg;
                si.ForeColor = LateDriversChangeDetailFg;
            }
            return item;
        }

        private static string LateDriversFormatChangeTime(double? ts)
        {
            if (!ts.HasValue || ts.Value <= 0)
                return "";
            try
            {
                var dt = DateTimeOffset.FromUnixTimeSeconds((long)ts.Value).LocalDateTime;
                return dt.ToString("h:mm tt", CultureInfo.CurrentCulture);
            }
            catch
            {
                return "";
            }
        }

        private void LateDriversApplyExpandChrome(ListViewItem item, string tripNo, string tripDisplayText = null)
        {
            if (item == null)
                return;

            int changeCount = LateDriversScheduleChangeCount(tripNo);
            if (changeCount <= 0)
                return;

            bool expanded = LateDriversTripIsExpanded(tripNo);
            string prefix = expanded ? "▼ " : "▶ ";

            // Trip column is index 2 (Group, Date, Trip…).
            int tripCol = 2;
            while (item.SubItems.Count <= tripCol)
                item.SubItems.Add("");

            string rawTrip = tripDisplayText;
            if (string.IsNullOrWhiteSpace(rawTrip))
                rawTrip = LateDriversNormalizeChangeTripNo(tripNo);
            rawTrip = LateDriversStripExpandChromeText(rawTrip);
            item.SubItems[tripCol].Text = prefix + rawTrip;
        }

        private void LateDriversApplyExpandChrome(ListViewItem item, LateDriversTripRowTag row, bool showDriver)
        {
            if (item == null || row == null || row.IsGroupHeader || row.IsGap || row.IsChangeDetail)
                return;

            string rawTrip = LateDriversNormalizeChangeTripNo(row.TripNo);
            LateDriversApplyExpandChrome(item, row.TripNo, rawTrip);
        }

        private void LateDriversAppendExpandedChangeRows(
            ListView.ListViewItemCollection items,
            string tripNo,
            bool showDriver)
        {
            if (items == null || !LateDriversTripIsExpanded(tripNo))
                return;
            var changes = LateDriversChangesForTrip(tripNo);
            if (changes == null || changes.Count == 0)
                return;
            string key = LateDriversCanonicalChangeTripKey(tripNo);
            foreach (var change in changes)
                items.Add(CreateLateDriversChangeDetailItem(change, key, showDriver));
        }

        private void LateDriversAppendExpandedChangeRows(
            ListView.ListViewItemCollection items,
            LateDriversTripRowTag row,
            bool showDriver)
        {
            if (items == null || row == null || row.IsGroupHeader || row.IsGap || row.IsChangeDetail)
                return;
            LateDriversAppendExpandedChangeRows(items, row.TripNo, showDriver);
        }
    }
}
