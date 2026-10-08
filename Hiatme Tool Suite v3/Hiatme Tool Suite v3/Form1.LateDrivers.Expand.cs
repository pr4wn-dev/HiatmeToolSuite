using System;
using System.Collections.Generic;
using System.Drawing;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.Integration;

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
        private static readonly Color LateDriversMcHurtsDetailBg = Color.FromArgb(78, 32, 32);
        private static readonly Color LateDriversMcHurtsDetailFg = Color.FromArgb(255, 168, 140);

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
                "mc_sched_changed",
                "mc_hurts",
                "mc_after_actual",
            };

        private static readonly HashSet<string> LateDriversScheduleChangeFields =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "sched_pu_iso",
                "sched_do_iso",
                "pu_address",
                "do_address",
                "driver",
                "mc_pu_time",
                "mc_do_time",
                "mc_sched_do_time",
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
            ShowLateDriversCancelBurst(focusTrip);
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
                        || string.Equals(tag, "time_changed", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(tag, "mc_sched_changed", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(tag, "mc_hurts", StringComparison.OrdinalIgnoreCase))
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

        private bool LateDriversTripHasMcHurtsRewrite(string tripNo)
        {
            var list = LateDriversChangesForTrip(tripNo);
            if (list == null)
                return false;
            foreach (var row in list)
            {
                if (TripScoutChangeFormat.IsMcHurtsRewrite(row))
                    return true;
            }
            return false;
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
            if (LateDriversTripHasMcHurtsRewrite(tripNo))
                body += " · MC rewrite";
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
            if (s.EndsWith(" · MC rewrite", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(0, s.Length - " · MC rewrite".Length).TrimEnd();
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
            bool mcHurts = TripScoutChangeFormat.IsMcHurtsRewrite(change);

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
            Color bg = mcHurts ? LateDriversMcHurtsDetailBg : LateDriversChangeDetailBg;
            Color fg = mcHurts ? LateDriversMcHurtsDetailFg : LateDriversChangeDetailFg;
            item.BackColor = bg;
            item.ForeColor = fg;
            foreach (ListViewItem.ListViewSubItem si in item.SubItems)
            {
                si.BackColor = bg;
                si.ForeColor = fg;
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
            if (LateDriversTripHasMcHurtsRewrite(tripNo))
                rawTrip += " · MC rewrite";
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

        private LateDriversCancelBurst _ldCancelBurst;

        private static readonly string[] CancelTestAmounts = { "11.25", "21.00", "23.07", "28.21", "45.00" };

        private string LookupBillingLossText(string tripNo)
        {
            string key = (tripNo ?? "").Trim();
            if (string.Equals(key, "TEST CANCEL", StringComparison.OrdinalIgnoreCase))
                return FormatBillingLoss(CancelTestAmounts[new Random().Next(CancelTestAmounts.Length)], null);
            if (key.Length == 0)
                return null;
            string miles = FindCancelledTripMiles(key);
            if (!string.IsNullOrWhiteSpace(miles))
                return FormatBillingLoss(null, miles);
            WRDownloadedTrip billed = FindBillingTrip(key);
            if (billed == null)
                return null;
            return FormatBillingLoss(billed.Price, billed.Miles);
        }

        private static bool CancelTripNumbersMatch(string candidate, string key, string want)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                return false;
            string num = candidate.Trim();
            string formatted = WellRydeFilterDataParser.FormatTripIdForScheduleMatch(num);
            return string.Equals(num, key, StringComparison.OrdinalIgnoreCase)
                || string.Equals(formatted, want, StringComparison.OrdinalIgnoreCase)
                || string.Equals(num, want, StringComparison.OrdinalIgnoreCase);
        }

        private WRDownloadedTrip FindBillingTrip(string key)
        {
            var list = wrBillingTool == null ? null : wrBillingTool.WRTripList;
            if (list == null)
                return null;
            string want = WellRydeFilterDataParser.FormatTripIdForScheduleMatch(key);
            foreach (WRDownloadedTrip trip in list)
            {
                if (trip != null && CancelTripNumbersMatch(trip.TripNumber, key, want))
                    return trip;
            }
            return null;
        }

        private string FindCancelledTripMiles(string key)
        {
            string want = WellRydeFilterDataParser.FormatTripIdForScheduleMatch(key);
            WRDownloadedTrip billed = FindBillingTrip(key);
            if (billed != null && !string.IsNullOrWhiteSpace(billed.Miles))
                return billed.Miles;
            if (_ldScheduleCache == null)
                return null;
            if (_ldScheduleCache.DriverTrips != null)
            {
                foreach (var kv in _ldScheduleCache.DriverTrips)
                {
                    if (kv.Value == null)
                        continue;
                    foreach (MCDownloadedTrip trip in kv.Value)
                    {
                        if (trip != null && CancelTripNumbersMatch(trip.TripNumber, key, want)
                            && !string.IsNullOrWhiteSpace(trip.Miles))
                            return trip.Miles;
                    }
                }
            }
            if (_ldScheduleCache.DriverLines != null)
            {
                foreach (var kv in _ldScheduleCache.DriverLines)
                {
                    if (kv.Value == null)
                        continue;
                    foreach (var line in kv.Value)
                    {
                        MCDownloadedTrip trip = line == null ? null : line.Trip;
                        if (trip != null && CancelTripNumbersMatch(trip.TripNumber, key, want)
                            && !string.IsNullOrWhiteSpace(trip.Miles))
                            return trip.Miles;
                    }
                }
            }
            return null;
        }

        private static string FormatBillingLoss(string price, string miles)
        {
            string raw = (price ?? "").Trim();
            if (raw.Length == 0)
                raw = WellRydeTripPriceCalculator.FromMiles(miles);
            raw = (raw ?? "").Trim().TrimStart('$');
            decimal value;
            if (!decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out value)
                && !decimal.TryParse(raw, NumberStyles.Any, CultureInfo.CurrentCulture, out value))
                return null;
            if (value < 0)
                value = -value;
            return "-$" + value.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private void ShowLateDriversCancelBurst(string tripNo)
        {
            if (IsDisposed || !IsHandleCreated)
                return;
            if (_ldCancelBurst == null || _ldCancelBurst.IsDisposed)
            {
                _ldCancelBurst = new LateDriversCancelBurst();
                Controls.Add(_ldCancelBurst);
            }
            _ldCancelBurst.TripText = (tripNo ?? "").Trim();
            _ldCancelBurst.TripPriceText = LookupBillingLossText(tripNo);
            _ldCancelBurst.Restart();
            int x = Math.Max(0, (ClientSize.Width - _ldCancelBurst.Width) / 2);
            int y = Math.Max(0, (ClientSize.Height - _ldCancelBurst.Height) / 2);
            _ldCancelBurst.Location = new Point(x, y);
            _ldCancelBurst.Visible = true;
            _ldCancelBurst.BringToFront();
        }

        private sealed class LateDriversCancelBurst : Control
        {
            private readonly System.Windows.Forms.Timer _timer;
            private DateTime _startedUtc;
            private readonly Rectangle[] _edgeBoxes;
            private Image[] _prizes = new Image[0];
            private Image[] _icons = new Image[0];
            private Image[] _characters = new Image[0];
            private Image _bigMoney;
            private Image _walkerA;
            private Image _walkerB;
            private Image[] _walkerSteps = new Image[0];
            private Rectangle _walkerErase = Rectangle.Empty;
            private Image _walkerPickup;
            private Image _walkerPocket;
            private Image _walkerWallet;
            private Image _huntBushA;
            private Image _huntBushB;
            private Image _huntTree;
            private Image _huntAim;
            private Image _huntShoot;
            private Image _huntShootUp;
            private Image _huntShootRight;
            private Image _huntMiss;
            private Image _huntDog;
            private Image[] _huntDucks = new Image[0];
            private Image _batCitizen;
            private Image _batCitizenOut;
            private Image _batRobber;
            private Image _batSwoop1;
            private Image _batSwoop2;
            private Image _batLand;
            private Image _batClub;
            private Image _batWindup;
            private Image _batArc1;
            private Image _batSwing;
            private Image _batArc2;
            private Image _batHit;
            private Image _batSmoke;
            private Image _copDriveRed;
            private Image _copDriveBlue;
            private Image _copHoldRed;
            private Image _copHoldBlue;
            private Image _copThrowRed;
            private Image _copThrowBlue;
            private Image _copTicket;
            private readonly Random _pick = new Random();
            private int[] _prizeImageByBox = new int[0];
            private int[] _iconImageByBox = new int[0];
            private int[] _characterImageByBox = new int[0];
            private int _bigMoneyBox = -1;
            private int _landBox;
            private double _chaseOffset;
            private Image[][] _characterFrames = new Image[0][];
            private bool _characterShow;
            private int _showCharacter = -1;
            private int _showFrame;
            private int _cowboyDigit = 3;
            private DateTime _showUtc;
            private readonly Font _titleFont = new Font("Segoe UI Semibold", 28f, FontStyle.Bold);
            private readonly Font _tripFont = new Font("Segoe UI", 16f);
            private int _lastLit = -1;
            private WasapiOut _chaseOut;
            private MediaFoundationReader _chaseReader;
            private bool _chaseAudioOn;
            private WasapiOut _laughOut;
            private MediaFoundationReader _laughReader;
            private bool _tauntPlayed;
            private bool _priceSoundPlayed;
            private WasapiOut _batFlyOut;
            private MediaFoundationReader _batFlyReader;
            private bool _batFlyOn;
            private WasapiOut _clubOut;
            private MediaFoundationReader _clubReader;
            private bool _clubPlayed;
            private WasapiOut _walkOut;
            private MediaFoundationReader _walkReader;
            private ElementHost _centerHost;
            private System.Windows.Controls.MediaElement _centerVideo;
            private bool _picked;
            private bool _centerVideoPlaying;
            private bool _centerVideoEnded;
            private int _huntShotsFired;
            private bool _huntDogLaughed;
            private static readonly byte[] RaceBeepWav = BuildRaceBeepWav();
            private static readonly byte[] SwooshWav = BuildSwooshWav();
            private static readonly byte[] ShotgunWav = BuildShotgunWav();
            private static readonly byte[] SirenWav = BuildSirenWav();

            public LateDriversCancelBurst()
            {
                SetStyle(
                    ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.UserPaint
                    | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.Opaque,
                    true);
                Size = new Size(Pad * 2 + Cell * 6 + Gap * 5, Pad * 2 + Cell * 5 + Gap * 4);
                BackColor = Color.FromArgb(18, 16, 14);
                Visible = false;
                TabStop = false;
                _edgeBoxes = BuildEdgeBoxes(BigBox(Size));
                _timer = new System.Windows.Forms.Timer { Interval = 32 };
                _timer.Tick += (_, __) =>
                {
                    if (_characterShow)
                    {
                        AdvanceCharacterShow();
                        return;
                    }
                    double chaseMs = (DateTime.UtcNow - _startedUtc).TotalMilliseconds;
                    if (!_picked && chaseMs >= 5000)
                    {
                        _picked = true;
                        StopChaseAudio();
                        PlayLandSting();
                        if (!_centerVideoPlaying || _centerVideoEnded)
                        {
                            StopCenterVideo();
                            BeginCharacterShow();
                            return;
                        }
                    }
                    int lit = EdgeLit();
                    if (lit != _lastLit)
                    {
                        _lastLit = lit;
                        if (Parent != null && Parent.Controls.Count > 0 && Parent.Controls[0] != this)
                            BringToFront();
                    }
                    Invalidate();
                };
            }

            public string TripText { get; set; }
            public string TripPriceText { get; set; }

            public void Restart()
            {
                CloseWalkerOverlays();
                _startedUtc = DateTime.UtcNow;
                _lastLit = -1;
                _picked = false;
                _centerVideoPlaying = false;
                _centerVideoEnded = false;
                _characterShow = false;
                _showCharacter = -1;
                ClearWindowRegion();
                Size = BoardSize();
                ReloadPrizeImages();
                AssignPrizeSquares();
                if (!_timer.Enabled)
                    _timer.Start();
                StopTaunt();
                StopBatFly();
                StopClubHit();
                StopWalkTrack();
                StartChaseAudio();
                StartCenterVideo();
                Invalidate();
            }

            private void MaybePlayTaunt(int elapsed, int at)
            {
                if (_tauntPlayed || elapsed < at)
                    return;
                _tauntPlayed = true;
                PlayTaunt();
            }

            private void StartCenterVideo()
            {
                StopCenterVideo();
                string path = FindDropFile("cancel-center", "*.mp4", "*.wmv", "*.mov", "*.avi", "*.m4v");
                if (path == null)
                    return;
                try
                {
                    if (_centerVideo == null)
                    {
                        _centerVideo = new System.Windows.Controls.MediaElement
                        {
                            LoadedBehavior = System.Windows.Controls.MediaState.Manual,
                            UnloadedBehavior = System.Windows.Controls.MediaState.Manual,
                            Stretch = System.Windows.Media.Stretch.Fill,
                            IsHitTestVisible = false
                        };
                        _centerVideo.MediaEnded += (_, __) =>
                        {
                            try
                            {
                                if (IsHandleCreated)
                                    BeginInvoke(new Action(CenterVideoFinished));
                                else
                                    CenterVideoFinished();
                            }
                            catch (Exception)
                            {
                            }
                        };
                        _centerHost = new ElementHost
                        {
                            Child = _centerVideo,
                            BackColor = Color.Black,
                            Visible = false
                        };
                        Controls.Add(_centerHost);
                    }
                    Rectangle box = BigBox(Size);
                    box.Inflate(-3, -3);
                    _centerHost.Bounds = box;
                    _centerHost.Visible = true;
                    _centerHost.BringToFront();
                    _centerVideoEnded = false;
                    _centerVideoPlaying = true;
                    _centerVideo.Stop();
                    _centerVideo.Source = new Uri(path);
                    _centerVideo.Play();
                }
                catch (Exception)
                {
                    StopCenterVideo();
                }
            }

            private bool _closingVideo;

            private void CenterVideoFinished()
            {
                if (_closingVideo || _characterShow)
                    return;
                _centerVideoEnded = true;
                if (!_picked)
                    return;
                _closingVideo = true;
                StopCenterVideo();
                _closingVideo = false;
                BeginCharacterShow();
            }

            private void StopCenterVideo()
            {
                _centerVideoPlaying = false;
                if (_centerVideo != null)
                {
                    try { _centerVideo.Stop(); } catch (Exception) { }
                    _centerVideo.Source = null;
                }
                if (_centerHost != null && !_centerHost.IsDisposed)
                    _centerHost.Visible = false;
            }

            private static string FindDropFile(string folder, params string[] patterns)
            {
                var dirs = new List<string>();
                string baseDir = AppDomain.CurrentDomain.BaseDirectory ?? "";
                if (!string.IsNullOrEmpty(baseDir))
                {
                    dirs.Add(Path.Combine(baseDir, "Resources", folder));
                    DirectoryInfo bin = Directory.GetParent(baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    DirectoryInfo project = bin == null ? null : bin.Parent;
                    if (project != null)
                        dirs.Add(Path.Combine(project.FullName, "Resources", folder));
                }
                foreach (string dir in dirs)
                {
                    if (!Directory.Exists(dir))
                        continue;
                    var found = new List<string>();
                    foreach (string pattern in patterns)
                        found.AddRange(Directory.GetFiles(dir, pattern));
                    if (found.Count == 0)
                        continue;
                    found.Sort(StringComparer.OrdinalIgnoreCase);
                    return found[0];
                }
                return null;
            }

            private static string FindDropMp3(string folder)
            {
                var dirs = new List<string>();
                string baseDir = AppDomain.CurrentDomain.BaseDirectory ?? "";
                if (!string.IsNullOrEmpty(baseDir))
                {
                    dirs.Add(Path.Combine(baseDir, "Resources", folder));
                    DirectoryInfo bin = Directory.GetParent(baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    DirectoryInfo project = bin == null ? null : bin.Parent;
                    if (project != null)
                        dirs.Add(Path.Combine(project.FullName, "Resources", folder));
                }
                foreach (string dir in dirs)
                {
                    if (!Directory.Exists(dir))
                        continue;
                    string[] files = Directory.GetFiles(dir, "*.mp3");
                    if (files.Length == 0)
                        continue;
                    Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                    return files[0];
                }
                return null;
            }

            private static string FindLaughMp3()
            {
                return FindDropMp3("cancel-laugh");
            }

            private void StartClubHit()
            {
                if (_clubPlayed)
                    return;
                string path = FindDropMp3("cancel-batswing");
                if (path == null)
                    return;
                _clubPlayed = true;
                PlayMp3Once(path, "BatClubSwing");
            }

            private void PlayLandSting()
            {
                PlayMp3Once(FindDropMp3("cancel-land"), "CancelLand");
            }

            private static void PlayMp3Once(string path, string name)
            {
                if (string.IsNullOrEmpty(path))
                    return;
                var thread = new Thread(() =>
                {
                    try
                    {
                        using (var reader = new MediaFoundationReader(path))
                        using (var output = new WasapiOut(AudioClientShareMode.Shared, 200))
                        using (var done = new ManualResetEvent(false))
                        {
                            output.PlaybackStopped += (_, __) =>
                            {
                                try { done.Set(); } catch (Exception) { }
                            };
                            output.Init(reader);
                            output.Play();
                            int waitMs = (int)Math.Ceiling(reader.TotalTime.TotalMilliseconds) + 400;
                            if (waitMs < 600)
                                waitMs = 600;
                            if (waitMs > 20000)
                                waitMs = 20000;
                            done.WaitOne(waitMs);
                        }
                    }
                    catch (Exception)
                    {
                    }
                });
                thread.IsBackground = true;
                thread.Name = name;
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
            }

            private void StopClubHit()
            {
                _clubPlayed = false;
                WasapiOut output = _clubOut;
                MediaFoundationReader reader = _clubReader;
                _clubOut = null;
                _clubReader = null;
                if (output != null)
                {
                    try { output.Stop(); } catch (Exception) { }
                    try { output.Dispose(); } catch (Exception) { }
                }
                if (reader != null)
                {
                    try { reader.Dispose(); } catch (Exception) { }
                }
            }

            private void StartBatFly()
            {
                if (_batFlyOn)
                    return;
                string path = FindDropMp3("cancel-batfly");
                if (path == null)
                    return;
                try
                {
                    _batFlyReader = new MediaFoundationReader(path);
                    _batFlyOut = new WasapiOut(AudioClientShareMode.Shared, 40);
                    _batFlyOut.PlaybackStopped += BatFlyStopped;
                    _batFlyOut.Init(_batFlyReader);
                    _batFlyOn = true;
                    _batFlyOut.Play();
                }
                catch (Exception)
                {
                    StopBatFly();
                }
            }

            private void BatFlyStopped(object sender, StoppedEventArgs e)
            {
                if (!_batFlyOn || _batFlyReader == null || _batFlyOut == null)
                    return;
                try
                {
                    _batFlyReader.Position = 0;
                    _batFlyOut.Play();
                }
                catch (Exception)
                {
                    _batFlyOn = false;
                }
            }

            private void StopBatFly()
            {
                _batFlyOn = false;
                WasapiOut output = _batFlyOut;
                MediaFoundationReader reader = _batFlyReader;
                _batFlyOut = null;
                _batFlyReader = null;
                if (output != null)
                {
                    try { output.PlaybackStopped -= BatFlyStopped; } catch (Exception) { }
                    try { output.Stop(); } catch (Exception) { }
                    try { output.Dispose(); } catch (Exception) { }
                }
                if (reader != null)
                {
                    try { reader.Dispose(); } catch (Exception) { }
                }
            }

            private void PlayTaunt()
            {
                StopBatFly();
                StopTaunt();
                string path = FindLaughMp3();
                if (path == null)
                    return;
                try
                {
                    _laughReader = new MediaFoundationReader(path);
                    _laughOut = new WasapiOut(AudioClientShareMode.Shared, 150);
                    _laughOut.Init(_laughReader);
                    _laughOut.Play();
                }
                catch (Exception)
                {
                    StopTaunt();
                }
            }

            private void StopTaunt()
            {
                WasapiOut output = _laughOut;
                MediaFoundationReader reader = _laughReader;
                _laughOut = null;
                _laughReader = null;
                if (output != null)
                {
                    try { output.Stop(); } catch (Exception) { }
                    try { output.Dispose(); } catch (Exception) { }
                }
                if (reader != null)
                {
                    try { reader.Dispose(); } catch (Exception) { }
                }
            }

            private static string FindChaseMp3()
            {
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? "", "Resources", "cancel-chase");
                if (!Directory.Exists(dir))
                    return null;
                string[] files = Directory.GetFiles(dir, "*.mp3");
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                return files.Length == 0 ? null : files[0];
            }

            private void StartChaseAudio()
            {
                StopChaseAudio();
                string path = FindChaseMp3();
                if (path == null)
                    return;
                try
                {
                    _chaseReader = new MediaFoundationReader(path);
                    _chaseOut = new WasapiOut(AudioClientShareMode.Shared, 150);
                    _chaseOut.PlaybackStopped += ChaseAudioStopped;
                    _chaseOut.Init(_chaseReader);
                    _chaseAudioOn = true;
                    _chaseOut.Play();
                }
                catch (Exception)
                {
                    StopChaseAudio();
                }
            }

            private void ChaseAudioStopped(object sender, StoppedEventArgs e)
            {
                if (!_chaseAudioOn || _chaseReader == null || _chaseOut == null)
                    return;
                try
                {
                    _chaseReader.Position = 0;
                    _chaseOut.Play();
                }
                catch (Exception)
                {
                    _chaseAudioOn = false;
                }
            }

            private void StopChaseAudio()
            {
                _chaseAudioOn = false;
                WasapiOut output = _chaseOut;
                MediaFoundationReader reader = _chaseReader;
                _chaseOut = null;
                _chaseReader = null;
                if (output != null)
                {
                    try { output.PlaybackStopped -= ChaseAudioStopped; } catch (Exception) { }
                    try { output.Stop(); } catch (Exception) { }
                    try { output.Dispose(); } catch (Exception) { }
                }
                if (reader != null)
                {
                    try { reader.Dispose(); } catch (Exception) { }
                }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    CloseWalkerOverlays();
                    StopChaseAudio();
                    StopTaunt();
                    StopBatFly();
                    StopClubHit();
                    StopWalkTrack();
                    StopCenterVideo();
                    _timer.Stop();
                    _timer.Dispose();
                    for (int i = 0; i < _prizes.Length; i++)
                        _prizes[i].Dispose();
                    for (int i = 0; i < _icons.Length; i++)
                        _icons[i].Dispose();
                    for (int i = 0; i < _characters.Length; i++)
                        _characters[i].Dispose();
                    if (_bigMoney != null)
                        _bigMoney.Dispose();
                    DisposeOne(_walkerA);
                    DisposeOne(_walkerB);
                    DisposeSteps();
                    DisposeOne(_walkerPickup);
                    DisposeOne(_walkerPocket);
                    DisposeOne(_walkerWallet);
                    DisposeHunt();
                    DisposeBats();
                    DisposeCop();
                    DisposeFrames(_characterFrames);
                    _titleFont.Dispose();
                    _tripFont.Dispose();
                }
                base.Dispose(disposing);
            }

            private const int Cell = 84;
            private const int Gap = 16;
            private const int Pad = 16;
            private const int Step = Cell + Gap;
            private const int LightMs = 220;

            private static Size BoardSize()
            {
                return new Size(Pad * 2 + Cell * 6 + Gap * 5, Pad * 2 + Cell * 5 + Gap * 4);
            }

            private void BeginCharacterShow()
            {
                int character = _landBox >= 0 && _landBox < _characterImageByBox.Length
                    ? _characterImageByBox[_landBox]
                    : -1;
                Image[] frames = FramesFor(character);
                if (character < 0 || frames.Length == 0)
                {
                    _timer.Stop();
                    Visible = false;
                    return;
                }
                _characterShow = true;
                _showCharacter = character;
                _showFrame = 0;
                _showUtc = DateTime.UtcNow;
                _tauntPlayed = false;
                _priceSoundPlayed = false;
                _walkerErase = Rectangle.Empty;
                if (IsWalkerScene())
                {
                    Rectangle covered = Bounds;
                    int band = 360;
                    int width = Parent != null ? Parent.ClientSize.Width : 900;
                    int top = Parent != null ? Math.Max(0, (Parent.ClientSize.Height - band) / 2) : 80;
                    Visible = false;
                    ClearWindowRegion();
                    Size = new Size(Math.Max(200, width), band);
                    Location = new Point(0, top);
                    if (Parent != null)
                        Parent.Invalidate(covered, false);
                    StartWalkTrack();
                    PresentWalker();
                    return;
                }
                if (_showCharacter == 6)
                {
                    Rectangle covered = Bounds;
                    Visible = false;
                    ClearWindowRegion();
                    Size = new Size(820, 760);
                    if (Parent != null)
                    {
                        Location = new Point(
                            Math.Max(0, (Parent.ClientSize.Width - Width) / 2),
                            Math.Max(0, (Parent.ClientSize.Height - Height) / 2));
                        Parent.Invalidate(covered, false);
                    }
                    _showFrame = 0;
                    _cowboyDigit = 4;
                    PlayGetReady();
                    PresentCowboy();
                    return;
                }
                if (_showCharacter == 7)
                {
                    Rectangle covered = Bounds;
                    int band = 480;
                    int width = Parent != null ? Parent.ClientSize.Width : 1000;
                    int top = Parent != null ? Math.Max(0, (Parent.ClientSize.Height - band) / 2) : 40;
                    Visible = false;
                    ClearWindowRegion();
                    Size = new Size(Math.Max(200, width), band);
                    Location = new Point(0, top);
                    if (Parent != null)
                        Parent.Invalidate(covered, false);
                    _huntShotsFired = 0;
                    _huntDogLaughed = false;
                    PresentDuck();
                    return;
                }
                if (_showCharacter == 8)
                {
                    Rectangle covered = Bounds;
                    int band = 520;
                    int width = Parent != null ? Parent.ClientSize.Width : 1000;
                    int top = Parent != null ? Math.Max(0, (Parent.ClientSize.Height - band) / 2) : 40;
                    Visible = false;
                    ClearWindowRegion();
                    Size = new Size(Math.Max(200, width), band);
                    Location = new Point(0, top);
                    if (Parent != null)
                        Parent.Invalidate(covered, false);
                    StartBatFly();
                    PresentBats();
                    return;
                }
                if (_showCharacter == 1)
                {
                    Rectangle covered = Bounds;
                    int band = 460;
                    int width = Parent != null ? Parent.ClientSize.Width : 1000;
                    int top = Parent != null ? Math.Max(0, (Parent.ClientSize.Height - band) / 2) : 40;
                    Visible = false;
                    ClearWindowRegion();
                    Size = new Size(Math.Max(200, width), band);
                    Location = new Point(0, top);
                    if (Parent != null)
                        Parent.Invalidate(covered, false);
                    PlaySiren();
                    PresentCop();
                    return;
                }
                Size = new Size(460, 500);
                if (Parent != null)
                {
                    Location = new Point(
                        Math.Max(0, (Parent.ClientSize.Width - Width) / 2),
                        Math.Max(0, (Parent.ClientSize.Height - Height) / 2));
                }
                BringToFront();
                ApplyCharacterRegion();
                Invalidate();
            }

            private void AdvanceCharacterShow()
            {
                Image[] frames = FramesFor(_showCharacter);
                if (frames.Length == 0)
                {
                    EndCharacterShow();
                    return;
                }
                if (IsWalkerScene())
                {
                    int elapsed = (int)(DateTime.UtcNow - _showUtc).TotalMilliseconds;
                    if (elapsed >= 11040)
                    {
                        EndCharacterShow();
                        return;
                    }
                    MaybePlayTaunt(elapsed, 3840);
                    PresentWalker();
                    return;
                }
                if (_showCharacter == 6)
                {
                    int elapsed = (int)(DateTime.UtcNow - _showUtc).TotalMilliseconds;
                    const int lead = 2600;
                    if (elapsed >= lead + 6000)
                    {
                        EndCharacterShow();
                        return;
                    }
                    int frame = elapsed < lead + 3000 ? 0 : 1;
                    int digit = elapsed < lead ? 4 : elapsed < lead + 1000 ? 3 : elapsed < lead + 2000 ? 2 : elapsed < lead + 3000 ? 1 : 0;
                    if (frame >= frames.Length)
                        frame = Math.Max(0, frames.Length - 1);
                    if (frame != _showFrame || digit != _cowboyDigit)
                    {
                        if (digit >= 1 && digit <= 3 && digit != _cowboyDigit)
                            PlayRaceBeep();
                        if (frame == 1 && _showFrame != 1)
                            PlaySwoosh();
                        _showFrame = frame;
                        _cowboyDigit = digit;
                        PresentCowboy();
                    }
                    MaybePlayTaunt(elapsed, lead + 3500);
                    return;
                }
                if (_showCharacter == 7)
                {
                    int elapsed = (int)(DateTime.UtcNow - _showUtc).TotalMilliseconds;
                    if (elapsed >= 9400)
                    {
                        EndCharacterShow();
                        return;
                    }
                    PresentDuck();
                    return;
                }
                if (_showCharacter == 8)
                {
                    int elapsed = (int)(DateTime.UtcNow - _showUtc).TotalMilliseconds;
                    if (elapsed >= 9200)
                    {
                        EndCharacterShow();
                        return;
                    }
                    if (elapsed >= 5300)
                        StartClubHit();
                    MaybePlayTaunt(elapsed, 8000);
                    PresentBats();
                    return;
                }
                if (_showCharacter == 1)
                {
                    int elapsed = (int)(DateTime.UtcNow - _showUtc).TotalMilliseconds;
                    if (elapsed >= 7600)
                    {
                        EndCharacterShow();
                        return;
                    }
                    MaybePlayTaunt(elapsed, 5000);
                    PresentCop();
                    return;
                }
                int shown = (int)((DateTime.UtcNow - _showUtc).TotalMilliseconds / 420);
                if (shown >= frames.Length * 3)
                {
                    EndCharacterShow();
                    return;
                }
                if (shown != _showFrame)
                {
                    _showFrame = shown;
                    ApplyCharacterRegion();
                    Invalidate();
                }
            }

            private void StartWalkTrack()
            {
                StopWalkTrack();
                string path = FindDropMp3("cancel-wallet");
                if (path == null)
                    return;
                try
                {
                    _walkReader = new MediaFoundationReader(path);
                    _walkReader.CurrentTime = new TimeSpan(0, 2, 31);
                    _walkOut = new WasapiOut(AudioClientShareMode.Shared, 40);
                    _walkOut.Init(_walkReader);
                    _walkOut.Play();
                }
                catch (Exception)
                {
                    StopWalkTrack();
                }
            }

            private void StopWalkTrack()
            {
                WasapiOut output = _walkOut;
                MediaFoundationReader reader = _walkReader;
                _walkOut = null;
                _walkReader = null;
                if (output != null)
                {
                    try { output.Stop(); } catch (Exception) { }
                    try { output.Dispose(); } catch (Exception) { }
                }
                if (reader != null)
                {
                    try { reader.Dispose(); } catch (Exception) { }
                }
            }

            private void EndCharacterShow()
            {
                StopWalkTrack();
                CloseWalkerOverlays();
                if (Parent != null)
                    Parent.Invalidate(Bounds, false);
                _walkerErase = Rectangle.Empty;
                _characterShow = false;
                _timer.Stop();
                ClearWindowRegion();
                Size = BoardSize();
                Visible = false;
            }

            private Image[] FramesFor(int character)
            {
                if (character < 0 || character >= _characterFrames.Length || _characterFrames[character] == null)
                    return new Image[0];
                return _characterFrames[character];
            }

            private static void DisposeFrames(Image[][] frames)
            {
                if (frames == null)
                    return;
                for (int i = 0; i < frames.Length; i++)
                {
                    if (frames[i] == null)
                        continue;
                    for (int f = 0; f < frames[i].Length; f++)
                    {
                        if (frames[i][f] != null)
                            frames[i][f].Dispose();
                    }
                }
            }
            private static Rectangle BigBox(Size size)
            {
                int inset = Pad + Cell + Gap;
                return new Rectangle(inset, inset, size.Width - inset * 2, size.Height - inset * 2);
            }

            private static Rectangle[] BuildEdgeBoxes(Rectangle big)
            {
                int right = big.Right + Gap;
                int bottom = big.Bottom + Gap;
                var boxes = new Rectangle[4 + 4 + 3 + 3 + 4];
                int n = 0;
                boxes[n++] = new Rectangle(Pad, Pad, Cell, Cell);
                for (int i = 0; i < 4; i++)
                    boxes[n++] = new Rectangle(Pad + Step + i * Step, Pad, Cell, Cell);
                boxes[n++] = new Rectangle(right, Pad, Cell, Cell);
                for (int i = 0; i < 3; i++)
                    boxes[n++] = new Rectangle(right, Pad + Step + i * Step, Cell, Cell);
                boxes[n++] = new Rectangle(right, bottom, Cell, Cell);
                for (int i = 3; i >= 0; i--)
                    boxes[n++] = new Rectangle(Pad + Step + i * Step, bottom, Cell, Cell);
                boxes[n++] = new Rectangle(Pad, bottom, Cell, Cell);
                for (int i = 2; i >= 0; i--)
                    boxes[n++] = new Rectangle(Pad, Pad + Step + i * Step, Cell, Cell);
                return boxes;
            }

            private int EdgeLit()
            {
                int lit;
                float frac;
                ChasePosition((DateTime.UtcNow - _startedUtc).TotalMilliseconds, out lit, out frac);
                return lit;
            }

            private void ChasePosition(double elapsed, out int lit, out float frac)
            {
                int count = _edgeBoxes.Length;
                if (count == 0)
                {
                    lit = 0;
                    frac = 0f;
                    return;
                }
                if (elapsed >= 5000)
                {
                    lit = _landBox;
                    frac = 0f;
                    return;
                }
                double shifted = elapsed / LightMs + _chaseOffset;
                lit = (int)Math.Floor(shifted) % count;
                if (lit < 0)
                    lit += count;
                frac = (float)(shifted - Math.Floor(shifted));
            }

            private void ChooseLanding()
            {
                var spots = new List<int>();
                for (int i = 0; i < _characterImageByBox.Length; i++)
                {
                    if (_characterImageByBox[i] >= 0)
                        spots.Add(i);
                }
                int count = Math.Max(1, _edgeBoxes.Length);
                _landBox = spots.Count == 0 ? 0 : spots[_pick.Next(spots.Count)];
                double endSteps = 5000.0 / LightMs;
                int baseStep = (int)Math.Ceiling(endSteps);
                int guard = 0;
                while (baseStep % count != _landBox % count && guard++ < count + 2)
                    baseStep++;
                _chaseOffset = baseStep - endSteps;
            }

            private void PlaySiren()
            {
                try
                {
                    PlaySound(SirenWav, IntPtr.Zero, 0x0001 | 0x0004 | 0x0002);
                }
                catch (Exception)
                {
                }
            }

            private static byte[] BuildSirenWav()
            {
                const int rate = 22050;
                const int samples = 167580;
                var pcm = new byte[samples * 2];
                double phase = 0;
                for (int i = 0; i < samples; i++)
                {
                    double t = i / (double)rate;
                    double sweep = (t / 0.7) % 2.0;
                    if (sweep > 1)
                        sweep = 2 - sweep;
                    double hz = 640 + sweep * 760;
                    phase += 2 * Math.PI * hz / rate;
                    double tone = Math.Sin(phase);
                    double square = tone >= 0 ? 1.0 : -1.0;
                    double edge = 1.0;
                    if (t < 0.06)
                        edge = t / 0.06;
                    else if (t > 7.35)
                        edge = Math.Max(0, (7.6 - t) / 0.25);
                    double sample = Math.Tanh((tone * 0.7 + square * 0.35) * edge * 1.5);
                    short s = (short)(sample * short.MaxValue);
                    pcm[i * 2] = (byte)(s & 0xff);
                    pcm[i * 2 + 1] = (byte)((s >> 8) & 0xff);
                }
                using (var ms = new MemoryStream())
                using (var w = new BinaryWriter(ms))
                {
                    w.Write(new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
                    w.Write(36 + pcm.Length);
                    w.Write(new byte[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });
                    w.Write(new byte[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
                    w.Write(16);
                    w.Write((short)1);
                    w.Write((short)1);
                    w.Write(rate);
                    w.Write(rate * 2);
                    w.Write((short)2);
                    w.Write((short)16);
                    w.Write(new byte[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
                    w.Write(pcm.Length);
                    w.Write(pcm);
                    return ms.ToArray();
                }
            }

            private void PlayGetReady()
            {
                string path = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Resources",
                    "cancel-character-anims",
                    "07",
                    "get-ready.wav");
                if (!File.Exists(path))
                    return;
                try
                {
                    PlaySound(path, IntPtr.Zero, 0x0001 | 0x00020000 | 0x0002);
                }
                catch (Exception)
                {
                }
            }

            private void PlaySwoosh()
            {
                try
                {
                    PlaySound(SwooshWav, IntPtr.Zero, 0x0001 | 0x0004 | 0x0002);
                }
                catch (Exception)
                {
                }
            }

            private static byte[] BuildSwooshWav()
            {
                const int rate = 22050;
                const int samples = 6800;
                var pcm = new byte[samples * 2];
                double phase = 0;
                double overtone = 0;
                for (int i = 0; i < samples; i++)
                {
                    double t = i / (double)samples;
                    double hz = 160 + 1100 * Math.Pow(t, 0.4);
                    phase += 2 * Math.PI * hz / rate;
                    overtone += 2 * Math.PI * hz * 2 / rate;
                    double env = Math.Sin(Math.PI * Math.Pow(t, 0.8));
                    double sample = (Math.Sin(phase) * 0.82 + Math.Sin(overtone) * 0.16) * env;
                    short s = (short)(Math.Tanh(sample * 1.25) * short.MaxValue);
                    pcm[i * 2] = (byte)(s & 0xff);
                    pcm[i * 2 + 1] = (byte)((s >> 8) & 0xff);
                }
                using (var ms = new MemoryStream())
                using (var w = new BinaryWriter(ms))
                {
                    w.Write(new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
                    w.Write(36 + pcm.Length);
                    w.Write(new byte[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });
                    w.Write(new byte[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
                    w.Write(16);
                    w.Write((short)1);
                    w.Write((short)1);
                    w.Write(rate);
                    w.Write(rate * 2);
                    w.Write((short)2);
                    w.Write((short)16);
                    w.Write(new byte[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
                    w.Write(pcm.Length);
                    w.Write(pcm);
                    return ms.ToArray();
                }
            }

            private void PlayRaceBeep()
            {
                try
                {
                    PlaySound(RaceBeepWav, IntPtr.Zero, 0x0001 | 0x0004 | 0x0002);
                }
                catch (Exception)
                {
                }
            }

            private static byte[] BuildRaceBeepWav()
            {
                const int rate = 22050;
                const int samples = 4200;
                var pcm = new byte[samples * 2];
                for (int i = 0; i < samples; i++)
                {
                    double t = i / (double)rate;
                    double env = Math.Exp(-t * 16.0);
                    double tone = Math.Sin(2 * Math.PI * 880 * t);
                    double square = Math.Sin(2 * Math.PI * 880 * t) >= 0 ? 1.0 : -1.0;
                    double sample = Math.Tanh((tone * 0.65 + square * 0.45) * env * 1.8);
                    short s = (short)(sample * short.MaxValue);
                    pcm[i * 2] = (byte)(s & 0xff);
                    pcm[i * 2 + 1] = (byte)((s >> 8) & 0xff);
                }
                using (var ms = new MemoryStream())
                using (var w = new BinaryWriter(ms))
                {
                    w.Write(new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
                    w.Write(36 + pcm.Length);
                    w.Write(new byte[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });
                    w.Write(new byte[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
                    w.Write(16);
                    w.Write((short)1);
                    w.Write((short)1);
                    w.Write(rate);
                    w.Write(rate * 2);
                    w.Write((short)2);
                    w.Write((short)16);
                    w.Write(new byte[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
                    w.Write(pcm.Length);
                    w.Write(pcm);
                    return ms.ToArray();
                }
            }

            private void PlayHuntShots(int elapsed)
            {
                int due = 0;
                if (elapsed >= 900)
                    due = 1;
                if (elapsed >= 2500)
                    due = 2;
                if (elapsed >= 4000)
                    due = 3;
                if (due <= _huntShotsFired)
                    return;
                _huntShotsFired = due;
                try
                {
                    PlaySound(ShotgunWav, IntPtr.Zero, 0x0001 | 0x0004 | 0x0002);
                }
                catch (Exception)
                {
                }
            }

            private void PlayDogLaugh(int elapsed)
            {
                if (_huntDogLaughed || elapsed < 6400)
                    return;
                _huntDogLaughed = true;
                PlayTaunt();
            }

            private static byte[] BuildShotgunWav()
            {
                const int rate = 22050;
                const int samples = 16000;
                var pcm = new byte[samples * 2];
                var noise = new Random(17);
                double phase = 0;
                double brown = 0;
                for (int i = 0; i < samples; i++)
                {
                    double t = i / (double)rate;
                    double white = noise.NextDouble() * 2 - 1;
                    brown = brown * 0.97 + white * 0.03;
                    double freq = 48 + 220 * Math.Exp(-t * 28);
                    phase += 2 * Math.PI * freq / rate;
                    double boom = Math.Sin(phase) * Math.Exp(-t * 2.6);
                    double chest = Math.Sin(2 * Math.PI * 62 * t) * Math.Exp(-t * 3.1);
                    double crack = white * Math.Exp(-t * 42);
                    double body = brown * 3.2 * Math.Exp(-t * 3.6);
                    double sample = Math.Tanh((boom * 1.35 + chest * 0.9 + body + crack * 0.85) * 1.7);
                    short s = (short)(sample * short.MaxValue);
                    pcm[i * 2] = (byte)(s & 0xff);
                    pcm[i * 2 + 1] = (byte)((s >> 8) & 0xff);
                }
                using (var ms = new MemoryStream())
                using (var w = new BinaryWriter(ms))
                {
                    w.Write(new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
                    w.Write(36 + pcm.Length);
                    w.Write(new byte[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });
                    w.Write(new byte[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
                    w.Write(16);
                    w.Write((short)1);
                    w.Write((short)1);
                    w.Write(rate);
                    w.Write(rate * 2);
                    w.Write((short)2);
                    w.Write((short)16);
                    w.Write(new byte[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
                    w.Write(pcm.Length);
                    w.Write(pcm);
                    return ms.ToArray();
                }
            }

            [DllImport("winmm.dll", CharSet = CharSet.Auto)]
            private static extern bool PlaySound(byte[] sound, IntPtr module, uint flags);

            [DllImport("winmm.dll", CharSet = CharSet.Auto)]
            private static extern bool PlaySound(string sound, IntPtr module, uint flags);

            private void ReloadPrizeImages()
            {
                ReplaceImages(ref _prizes, LoadImages("cancel-squares"));
                ReplaceImages(ref _icons, LoadImages("cancel-prizes"));
                ReplaceImages(ref _characters, LoadImages("cancel-characters"));
                ReplaceFrames(LoadCharacterFrames(_characters.Length));
                LoadWalkerImages();
                LoadHuntImages();
                LoadBatImages();
                LoadCopImages();
                Image freshBig = LoadSingleImage("cancel-big-money.jpg");
                Image previousBig = _bigMoney;
                _bigMoney = freshBig;
                if (previousBig != null)
                    previousBig.Dispose();
            }

            private static void ReplaceImages(ref Image[] slot, Image[] fresh)
            {
                Image[] previous = slot;
                slot = fresh;
                for (int i = 0; i < previous.Length; i++)
                {
                    if (previous[i] != null)
                        previous[i].Dispose();
                }
            }

            private static Image[] LoadImages(string folder, bool fitCell = true)
            {
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", folder);
                if (!Directory.Exists(dir))
                    return new Image[0];
                var files = Directory.GetFiles(dir, "*.jpg")
                    .Concat(Directory.GetFiles(dir, "*.png"))
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var images = new List<Image>();
                foreach (string file in files)
                {
                    try
                    {
                        Image image = Image.FromFile(file);
                        images.Add(fitCell ? FitCell(image) : image);
                    }
                    catch (Exception)
                    {
                    }
                }
                return images.ToArray();
            }

            private void ReplaceFrames(Image[][] fresh)
            {
                Image[][] previous = _characterFrames;
                _characterFrames = fresh ?? new Image[0][];
                DisposeFrames(previous);
            }

            private static Image[][] LoadCharacterFrames(int count)
            {
                var all = new Image[count][];
                for (int i = 0; i < count; i++)
                    all[i] = LoadImages("cancel-character-anims\\" + (i + 1).ToString("00"), false);
                return all;
            }

            private static Image LoadSingleImage(string fileName)
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", fileName);
                if (!File.Exists(path))
                    return null;
                try
                {
                    return FitCell(Image.FromFile(path));
                }
                catch (Exception)
                {
                    return null;
                }
            }

            private void AssignPrizeSquares()
            {
                var dollars = new int[_edgeBoxes.Length];
                var icons = new int[_edgeBoxes.Length];
                var characters = new int[_edgeBoxes.Length];
                for (int i = 0; i < _edgeBoxes.Length; i++)
                {
                    dollars[i] = -1;
                    icons[i] = -1;
                    characters[i] = -1;
                }
                int[] boxes = Shuffle(Enumerable.Range(0, _edgeBoxes.Length).ToArray());
                int dollarCount = Math.Min(10, Math.Min(_prizes.Length, boxes.Length));
                if (dollarCount > 0)
                {
                    int[] images = Shuffle(Enumerable.Range(0, _prizes.Length).ToArray());
                    for (int i = 0; i < dollarCount; i++)
                        dollars[boxes[i]] = images[i];
                }
                int iconCount = Math.Min(3, Math.Min(_icons.Length, boxes.Length - dollarCount));
                if (iconCount > 0)
                {
                    int[] images = Shuffle(Enumerable.Range(0, _icons.Length).ToArray());
                    for (int i = 0; i < iconCount; i++)
                        icons[boxes[dollarCount + i]] = images[i];
                }
                int openStart = dollarCount + iconCount;
                _bigMoneyBox = _bigMoney != null && openStart < boxes.Length
                    ? boxes[openStart + _pick.Next(boxes.Length - openStart)]
                    : -1;
                var open = new List<int>();
                for (int i = openStart; i < boxes.Length; i++)
                {
                    if (boxes[i] != _bigMoneyBox)
                        open.Add(boxes[i]);
                }
                int characterCount = Math.Min(4, Math.Min(_characters.Length, open.Count));
                if (characterCount > 0)
                {
                    int[] animated = { 1, 2, 6, 7, 8 };
                    var pick = new List<int>();
                    foreach (int index in animated)
                    {
                        if (index >= 0 && index < _characters.Length)
                            pick.Add(index);
                    }
                    int[] pickOrder = Shuffle(pick.ToArray());
                    pick.Clear();
                    for (int i = 0; i < pickOrder.Length && pick.Count < characterCount; i++)
                        pick.Add(pickOrder[i]);
                    for (int i = 0; i < pick.Count && i < open.Count; i++)
                        characters[open[i]] = pick[i];
                }
                _prizeImageByBox = dollars;
                _iconImageByBox = icons;
                _characterImageByBox = characters;
                ChooseLanding();
            }

            private int[] Shuffle(int[] values)
            {
                for (int i = values.Length - 1; i > 0; i--)
                {
                    int j = _pick.Next(i + 1);
                    int swap = values[i];
                    values[i] = values[j];
                    values[j] = swap;
                }
                return values;
            }

            private static Image FitCell(Image source)
            {
                var bmp = new Bitmap(Cell, Cell);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(source, new Rectangle(0, 0, Cell, Cell));
                }
                source.Dispose();
                return bmp;
            }

            private static void DrawPrizeImage(Graphics g, Image image, Rectangle dest, float glow)
            {
                g.DrawImageUnscaled(image, dest.Location);
                int wash = (int)(130 * glow);
                if (wash > 0)
                {
                    using (var b = new SolidBrush(Color.FromArgb(wash, 255, 186, 64)))
                        g.FillRectangle(b, dest);
                }
            }

            private void PaintCharacterShow(Graphics g)
            {
                if (IsWalkerScene())
                {
                    PaintWalker(g);
                    return;
                }
                Image[] frames = FramesFor(_showCharacter);
                if (frames.Length == 0)
                    return;
                Image image = frames[_showFrame % frames.Length];
                int side = Math.Max(1, Math.Min(Width, Height) - 24);
                var dest = new Rectangle((Width - side) / 2, (Height - side) / 2, side, side);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(image, dest);
            }

            private bool IsWalkerScene()
            {
                bool steps = _walkerSteps.Length > 0 || (_walkerA != null && _walkerB != null);
                return _showCharacter == 2 && steps && _walkerWallet != null;
            }

            private void LoadWalkerImages()
            {
                DisposeOne(_walkerA);
                DisposeOne(_walkerB);
                DisposeSteps();
                DisposeOne(_walkerPickup);
                DisposeOne(_walkerPocket);
                DisposeOne(_walkerWallet);
                _walkerA = LoadLoose(@"cancel-character-anims\03-walk\walk-a.png");
                _walkerB = LoadLoose(@"cancel-character-anims\03-walk\walk-b.png");
                var steps = new List<Image>();
                for (int i = 1; i <= 4; i++)
                {
                    Image step = LoadLoose(@"cancel-character-anims\03-walk\walk-" + i + ".png");
                    if (step != null)
                        steps.Add(step);
                }
                _walkerSteps = steps.ToArray();
                _walkerPickup = LoadLoose(@"cancel-character-anims\03-walk\pickup.png");
                _walkerPocket = LoadLoose(@"cancel-character-anims\03-walk\pocket.png");
                _walkerWallet = LoadLoose(@"cancel-character-anims\03-walk\wallet.png");
            }

            private void DisposeSteps()
            {
                for (int i = 0; i < _walkerSteps.Length; i++)
                    DisposeOne(_walkerSteps[i]);
                _walkerSteps = new Image[0];
            }

            private Image WalkStep(int elapsed)
            {
                if (_walkerSteps.Length > 0)
                    return _walkerSteps[(Math.Max(0, elapsed) / 480) % _walkerSteps.Length];
                if ((Math.Max(0, elapsed) / 480) % 2 == 0)
                    return _walkerA;
                return _walkerB ?? _walkerA;
            }

            private static Image LoadLoose(string relative)
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", relative);
                if (!File.Exists(path))
                    return null;
                try
                {
                    return Image.FromFile(path);
                }
                catch (Exception)
                {
                    return null;
                }
            }

            private static void DisposeOne(Image image)
            {
                if (image != null)
                    image.Dispose();
            }

            private void WalkerPose(int elapsed, out Rectangle guy, out Rectangle wallet, out bool showWallet, out Image pose)
            {
                int guyH = Math.Min(300, Math.Max(120, Height - 20));
                int guyW = guyH;
                int walletW = 96;
                int walletH = 70;
                int ground = Height - 8;
                int meetX = Width / 2 - (int)(guyW * 0.55);
                int startX = -guyW - 8;
                int endX = Width + 8;
                int x;
                if (elapsed < 3840)
                {
                    float t = elapsed / 3840f;
                    x = startX + (int)((meetX - startX) * t);
                    pose = WalkStep(elapsed);
                    showWallet = true;
                }
                else if (elapsed < 5000)
                {
                    x = meetX;
                    pose = _walkerPickup ?? WalkStep(0);
                    showWallet = false;
                }
                else if (elapsed < 7200)
                {
                    x = meetX;
                    pose = _walkerPocket ?? WalkStep(0);
                    showWallet = false;
                }
                else
                {
                    float t = Math.Min(1f, (elapsed - 7200) / 3840f);
                    x = meetX + (int)((endX - meetX) * t);
                    pose = WalkStep(elapsed - 7200);
                    showWallet = false;
                }
                guy = new Rectangle(x, ground - guyH, guyW, guyH);
                wallet = new Rectangle(Width / 2 - walletW / 2, ground - walletH - 4, walletW, walletH);
            }

            private WalkerOverlay _guyOverlay;
            private WalkerOverlay _walletOverlay;

            private void LoadHuntImages()
            {
                DisposeHunt();
                _huntBushA = LoadLoose(@"cancel-character-anims\08-hunt\bush-a.png");
                _huntBushB = LoadLoose(@"cancel-character-anims\08-hunt\bush-b.png");
                _huntTree = LoadLoose(@"cancel-character-anims\08-hunt\tree.png");
                _huntAim = LoadLoose(@"cancel-character-anims\08-hunt\aim.png");
                _huntShoot = LoadLoose(@"cancel-character-anims\08-hunt\shoot.png");
                _huntShootUp = LoadLoose(@"cancel-character-anims\08-hunt\shoot-up.png");
                _huntShootRight = LoadLoose(@"cancel-character-anims\08-hunt\shoot-right.png");
                _huntMiss = LoadLoose(@"cancel-character-anims\08-hunt\miss.png");
                _huntDog = LoadLoose(@"cancel-character-anims\08-hunt\dog.png");
                var ducks = new List<Image>();
                for (int i = 1; i <= 4; i++)
                {
                    Image duck = LoadLoose(@"cancel-character-anims\08-hunt\duck-" + i + ".png");
                    if (duck != null)
                        ducks.Add(duck);
                }
                _huntDucks = ducks.ToArray();
            }

            private void DisposeHunt()
            {
                DisposeOne(_huntBushA);
                DisposeOne(_huntBushB);
                DisposeOne(_huntTree);
                DisposeOne(_huntAim);
                DisposeOne(_huntShoot);
                DisposeOne(_huntShootUp);
                DisposeOne(_huntShootRight);
                DisposeOne(_huntMiss);
                DisposeOne(_huntDog);
                for (int i = 0; i < _huntDucks.Length; i++)
                    DisposeOne(_huntDucks[i]);
                _huntBushA = null;
                _huntBushB = null;
                _huntTree = null;
                _huntAim = null;
                _huntShoot = null;
                _huntShootUp = null;
                _huntShootRight = null;
                _huntMiss = null;
                _huntDog = null;
                _huntDucks = new Image[0];
            }

            private static void DrawZigDuck(Graphics g, Image duck, int elapsed, int w, int h, int duckW, int duckH, bool first)
            {
                if (duck == null)
                    return;
                float[] xs = first
                    ? new[] { 0.36f, 0.70f, 0.82f, 0.48f, 0.16f, 0.08f, 1.15f }
                    : new[] { 0.52f, 0.22f, 0.15f, 0.58f, 0.86f, 0.40f, 1.20f };
                float[] ys = first
                    ? new[] { 0.66f, 0.26f, 0.44f, 0.10f, 0.42f, 0.18f, -0.06f }
                    : new[] { 0.72f, 0.34f, 0.55f, 0.16f, 0.40f, 0.22f, 0.04f };
                int[] spans = first
                    ? new[] { 1100, 700, 900, 800, 900, 1800 }
                    : new[] { 1000, 800, 1000, 800, 1000, 1800 };
                int t = Math.Max(0, elapsed);
                int acc = 0;
                int seg = spans.Length - 1;
                float u = 1f;
                for (int i = 0; i < spans.Length; i++)
                {
                    if (t < acc + spans[i])
                    {
                        seg = i;
                        u = (t - acc) / (float)spans[i];
                        break;
                    }
                    acc += spans[i];
                }
                float x = xs[seg] + (xs[seg + 1] - xs[seg]) * u;
                float y = ys[seg] + (ys[seg + 1] - ys[seg]) * u;
                int left = (int)(x * w);
                int top = (int)(y * h);
                bool faceLeft = xs[seg + 1] < xs[seg];
                GraphicsState state = g.Save();
                if (faceLeft)
                {
                    g.TranslateTransform(left + duckW, top);
                    g.ScaleTransform(-1f, 1f);
                    g.DrawImage(duck, 0, 0, duckW, duckH);
                }
                else
                {
                    g.DrawImage(duck, left, top, duckW, duckH);
                }
                g.Restore(state);
            }

            private void PresentDuck()
            {
                if (Parent == null)
                    return;
                if (_huntAim == null)
                    LoadHuntImages();
                if (_huntAim == null || _huntDucks.Length == 0)
                {
                    EndCharacterShow();
                    return;
                }
                if (_guyOverlay == null || _guyOverlay.IsDisposed)
                {
                    _guyOverlay = new WalkerOverlay();
                    Form owner = FindForm();
                    if (owner != null)
                        _guyOverlay.Show(owner);
                    else
                        _guyOverlay.Show();
                }
                int elapsed = (int)(DateTime.UtcNow - _showUtc).TotalMilliseconds;
                PlayHuntShots(elapsed);
                PlayDogLaugh(elapsed);
                using (var bmp = new Bitmap(Math.Max(2, Width), Math.Max(2, Height), PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Transparent);
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        int w = bmp.Width;
                        int h = bmp.Height;
                        int treeH = (int)(h * 0.92);
                        int treeW = (int)(treeH * 0.72);
                        if (_huntTree != null)
                        {
                            g.DrawImage(_huntTree, new Rectangle(6, h - treeH, treeW, treeH));
                            g.DrawImage(_huntTree, new Rectangle(w - treeW - 6, h - treeH + 8, treeW, treeH));
                        }
                        int wing = (Math.Max(0, elapsed) / 220) % _huntDucks.Length;
                        DrawZigDuck(g, _huntDucks[wing], elapsed, w, h, 150, 100, true);
                        DrawZigDuck(g, _huntDucks[(wing + 2) % _huntDucks.Length], elapsed, w, h, 124, 84, false);
                        if (elapsed > 6400 && _huntDog != null)
                        {
                            float rise = Math.Min(1f, (elapsed - 6400) / 900f);
                            int dogW = 240;
                            int dogH = 260;
                            int hidden = h - 10;
                            int shown = h - dogH - 36;
                            int dogY = hidden + (int)((shown - hidden) * rise);
                            g.DrawImage(_huntDog, new Rectangle(w / 2 - dogW / 2, dogY, dogW, dogH));
                        }
                        Image hunter = _huntAim;
                        if (elapsed >= 900 && elapsed < 1550)
                            hunter = _huntShoot ?? _huntAim;
                        else if (elapsed >= 2500 && elapsed < 3150)
                            hunter = _huntShootUp ?? _huntShoot ?? _huntAim;
                        else if (elapsed >= 4000 && elapsed < 4650)
                            hunter = _huntShootRight ?? _huntAim;
                        else if (elapsed >= 5000)
                            hunter = _huntMiss ?? _huntAim;
                        int guyH = (int)(h * 0.7);
                        g.DrawImage(hunter, new Rectangle(18, h - guyH - 4, guyH, guyH));
                        int bushH = 156;
                        int ground = h - bushH + 18;
                        if (_huntBushB != null)
                        {
                            g.DrawImage(_huntBushB, new Rectangle(30, ground + 20, 210, 130));
                            g.DrawImage(_huntBushB, new Rectangle(w / 2 - 30, ground, 250, 140));
                        }
                        if (_huntBushA != null)
                        {
                            g.DrawImage(_huntBushA, new Rectangle(w / 2 - 210, ground + 8, 180, bushH));
                            g.DrawImage(_huntBushA, new Rectangle(w / 2 + 170, ground + 14, 170, bushH));
                            g.DrawImage(_huntBushA, new Rectangle(w - 200, ground + 6, 180, bushH));
                        }
                        DrawTripPrice(g, elapsed, 6400);
                    }
                    _guyOverlay.Present(bmp, Parent.PointToScreen(Location));
                }
            }

            private void LoadBatImages()
            {
                DisposeBats();
                _batCitizen = LoadLoose(@"cancel-character-anims\09-bats\citizen.png");
                _batCitizenOut = LoadLoose(@"cancel-character-anims\09-bats\citizen-out.png");
                _batRobber = LoadLoose(@"cancel-character-anims\09-bats\robber.png");
                _batSwoop1 = LoadLoose(@"cancel-character-anims\09-bats\swoop-1.png");
                _batSwoop2 = LoadLoose(@"cancel-character-anims\09-bats\swoop-2.png");
                _batLand = LoadLoose(@"cancel-character-anims\09-bats\land.png");
                _batClub = LoadLoose(@"cancel-character-anims\09-bats\club.png");
                _batWindup = LoadLoose(@"cancel-character-anims\09-bats\face-windup.png");
                _batArc1 = LoadLoose(@"cancel-character-anims\09-bats\face-arc1.png");
                _batSwing = LoadLoose(@"cancel-character-anims\09-bats\face-swing.png");
                _batArc2 = LoadLoose(@"cancel-character-anims\09-bats\face-arc2.png");
                _batHit = LoadLoose(@"cancel-character-anims\09-bats\face-hit.png");
                _batSmoke = LoadLoose(@"cancel-character-anims\09-bats\smoke.png");
            }

            private void DisposeBats()
            {
                DisposeOne(_batCitizen);
                DisposeOne(_batCitizenOut);
                DisposeOne(_batRobber);
                DisposeOne(_batSwoop1);
                DisposeOne(_batSwoop2);
                DisposeOne(_batLand);
                DisposeOne(_batClub);
                DisposeOne(_batWindup);
                DisposeOne(_batArc1);
                DisposeOne(_batSwing);
                DisposeOne(_batArc2);
                DisposeOne(_batHit);
                DisposeOne(_batSmoke);
                _batCitizen = null;
                _batCitizenOut = null;
                _batRobber = null;
                _batSwoop1 = null;
                _batSwoop2 = null;
                _batLand = null;
                _batClub = null;
                _batWindup = null;
                _batArc1 = null;
                _batSwing = null;
                _batArc2 = null;
                _batHit = null;
                _batSmoke = null;
            }

            private static Rectangle FitHeight(Image image, int x, int bottom, int height)
            {
                float aspect = image.Width / (float)Math.Max(1, image.Height);
                int w = Math.Max(2, (int)(height * aspect));
                return new Rectangle(x, bottom - height, w, height);
            }

            private static Rectangle PlaceFeet(Image image, int feetX, int bottom, int height, float feetFrac)
            {
                Rectangle box = FitHeight(image, 0, bottom, height);
                box.X = feetX - (int)(box.Width * feetFrac);
                return box;
            }

            private void PresentBats()
            {
                if (Parent == null)
                    return;
                if (_batRobber == null || _batCitizen == null)
                    LoadBatImages();
                if (_batRobber == null || _batCitizen == null || _batSwoop1 == null)
                {
                    EndCharacterShow();
                    return;
                }
                if (_guyOverlay == null || _guyOverlay.IsDisposed)
                {
                    _guyOverlay = new WalkerOverlay();
                    Form owner = FindForm();
                    if (owner != null)
                        _guyOverlay.Show(owner);
                    else
                        _guyOverlay.Show();
                }
                int elapsed = (int)(DateTime.UtcNow - _showUtc).TotalMilliseconds;
                using (var bmp = new Bitmap(Math.Max(2, Width), Math.Max(2, Height), PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Transparent);
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        int w = bmp.Width;
                        int h = bmp.Height;
                        int ground = h - 8;
                        int guyH = (int)(h * 0.62f);
                        int robberW = (int)(guyH * (_batRobber.Width / (float)Math.Max(1, _batRobber.Height)));
                        int citizenW = (int)(guyH * (_batCitizen.Width / (float)Math.Max(1, _batCitizen.Height)));
                        Image standRef = _batLand ?? _batSwoop1;
                        int heroW = (int)(guyH * (standRef.Width / (float)Math.Max(1, standRef.Height)));
                        int groupW = robberW + citizenW + (int)(heroW * 0.55f) + (int)(guyH * 0.40f);
                        int origin = Math.Max(12, (w - groupW) / 2);
                        Rectangle robber = FitHeight(_batRobber, origin, ground, guyH);
                        int citizenX = robber.Right - 52;
                        bool down = elapsed >= 5650;
                        int standW = (int)(guyH * (_batCitizen.Width / (float)Math.Max(1, _batCitizen.Height)));
                        int feetX = citizenX + standW + (int)(guyH * 0.34f);
                        Image citizenImg = down ? (_batCitizenOut ?? _batCitizen) : _batCitizen;
                        int citizenH = down ? (int)(guyH * 0.58f) : guyH;
                        Rectangle citizen = FitHeight(citizenImg, citizenX, ground - (down ? 4 : 0), citizenH);
                        int landX = citizen.X + (int)(guyH * 0.46f);
                        bool showHero = elapsed >= 900 && elapsed < 8000;
                        Rectangle hero = Rectangle.Empty;
                        Image heroImg = null;
                        if (showHero)
                        {
                            if (elapsed < 2800)
                            {
                                float t = (elapsed - 900) / 1900f;
                                if (t < 0f)
                                    t = 0f;
                                if (t > 1f)
                                    t = 1f;
                                float ease = 1f - (1f - t) * (1f - t);
                                heroImg = ((elapsed / 240) % 2 == 0 ? _batSwoop1 : _batSwoop2) ?? _batSwoop1;
                                int swoopH = (int)(h * 0.50f);
                                Rectangle planted = _batWindup != null
                                    ? PlaceFeet(_batWindup, feetX, ground, guyH, 0.472f)
                                    : FitHeight(heroImg, feetX - guyH / 2, ground, guyH);
                                Rectangle landed = FitHeight(heroImg, planted.X, ground, swoopH);
                                int startX = landX + Math.Max(80, w / 7);
                                int startY = (int)(-swoopH * 0.28f);
                                hero = new Rectangle(
                                    startX + (int)((landed.X - startX) * ease),
                                    startY + (int)((landed.Y - startY) * ease),
                                    landed.Width,
                                    landed.Height);
                            }
                            else if (elapsed < 5300)
                            {
                                heroImg = _batWindup ?? _batLand ?? _batClub;
                                hero = PlaceFeet(heroImg, feetX, ground, guyH, 0.472f);
                            }
                            else
                            {
                                int step = (elapsed - 5300) / 55;
                                if (step < 1)
                                {
                                    heroImg = _batArc1 ?? _batSwing ?? _batWindup;
                                    hero = PlaceFeet(heroImg, feetX, ground, guyH, _batArc1 != null ? 0.587f : 0.616f);
                                }
                                else if (step < 2)
                                {
                                    heroImg = _batSwing ?? _batArc2 ?? _batArc1;
                                    hero = PlaceFeet(heroImg, feetX, ground, guyH, _batSwing != null ? 0.616f : 0.623f);
                                }
                                else if (step < 3)
                                {
                                    heroImg = _batArc2 ?? _batHit ?? _batSwing;
                                    hero = PlaceFeet(heroImg, feetX, ground, guyH, _batArc2 != null ? 0.623f : 0.661f);
                                }
                                else
                                {
                                    heroImg = _batHit ?? _batArc2 ?? _batSwing;
                                    hero = PlaceFeet(heroImg, feetX, ground, guyH, _batHit != null ? 0.661f : 0.623f);
                                }
                            }
                        }
                        g.DrawImage(_batRobber, robber);
                        g.DrawImage(citizenImg, citizen);
                        if (heroImg != null)
                            g.DrawImage(heroImg, hero);
                        if (elapsed >= 7200 && elapsed < 9200 && _batSmoke != null)
                        {
                            float grow = elapsed < 8000
                                ? Math.Min(1f, (elapsed - 7200) / 600f)
                                : Math.Max(0f, 1f - (elapsed - 8000) / 1100f);
                            int smokeH = 24 + (int)(guyH * 0.92f * grow);
                            Rectangle cover = hero.Width > 0
                                ? hero
                                : FitHeight(_batLand ?? _batClub ?? _batSwoop1, landX, ground, guyH);
                            int smokeW = (int)(smokeH * (_batSmoke.Width / (float)Math.Max(1, _batSmoke.Height)));
                            var smoke = new Rectangle(
                                cover.X + cover.Width / 2 - smokeW / 2,
                                cover.Y + cover.Height / 2 - smokeH / 2,
                                smokeW,
                                smokeH);
                            g.DrawImage(_batSmoke, smoke);
                        }
                        DrawTripPrice(g, elapsed, 5300);
                    }
                    _guyOverlay.Present(bmp, Parent.PointToScreen(Location));
                }
            }

            private void LoadCopImages()
            {
                DisposeCop();
                _copDriveRed = LoadLoose(@"cancel-character-anims\02-cop\drive-red.png");
                _copDriveBlue = LoadLoose(@"cancel-character-anims\02-cop\drive-blue.png");
                _copHoldRed = LoadLoose(@"cancel-character-anims\02-cop\hold-red.png");
                _copHoldBlue = LoadLoose(@"cancel-character-anims\02-cop\hold-blue.png");
                _copThrowRed = LoadLoose(@"cancel-character-anims\02-cop\throw-red.png");
                _copThrowBlue = LoadLoose(@"cancel-character-anims\02-cop\throw-blue.png");
                _copTicket = LoadLoose(@"cancel-character-anims\02-cop\ticket.png");
            }

            private void DisposeCop()
            {
                DisposeOne(_copDriveRed);
                DisposeOne(_copDriveBlue);
                DisposeOne(_copHoldRed);
                DisposeOne(_copHoldBlue);
                DisposeOne(_copThrowRed);
                DisposeOne(_copThrowBlue);
                DisposeOne(_copTicket);
                _copDriveRed = null;
                _copDriveBlue = null;
                _copHoldRed = null;
                _copHoldBlue = null;
                _copThrowRed = null;
                _copThrowBlue = null;
                _copTicket = null;
            }

            private void PresentCop()
            {
                if (Parent == null)
                    return;
                if (_copDriveRed == null)
                    LoadCopImages();
                if (_copDriveRed == null)
                {
                    EndCharacterShow();
                    return;
                }
                if (_guyOverlay == null || _guyOverlay.IsDisposed)
                {
                    _guyOverlay = new WalkerOverlay();
                    Form owner = FindForm();
                    if (owner != null)
                        _guyOverlay.Show(owner);
                    else
                        _guyOverlay.Show();
                }
                int elapsed = (int)(DateTime.UtcNow - _showUtc).TotalMilliseconds;
                using (var bmp = new Bitmap(Math.Max(2, Width), Math.Max(2, Height), PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Transparent);
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        int w = bmp.Width;
                        int h = bmp.Height;
                        int ground = h - 8;
                        int carH = (int)(h * 0.62f);
                        bool flash = ((Math.Max(0, elapsed) / 140) % 2) == 0;
                        Image sized;
                        Image shown;
                        if (elapsed < 2000 || elapsed >= 5000)
                        {
                            sized = _copDriveRed;
                            shown = flash ? _copDriveRed : (_copDriveBlue ?? _copDriveRed);
                        }
                        else if (elapsed < 3600)
                        {
                            sized = _copHoldRed ?? _copDriveRed;
                            shown = flash ? sized : (_copHoldBlue ?? sized);
                        }
                        else
                        {
                            sized = _copThrowRed ?? _copHoldRed ?? _copDriveRed;
                            shown = flash ? sized : (_copThrowBlue ?? sized);
                        }
                        int carW = Math.Max(2, (int)(carH * (sized.Width / (float)Math.Max(1, sized.Height))));
                        int centerX = (w - carW) / 2;
                        int x;
                        int bob = 0;
                        if (elapsed < 2000)
                        {
                            float t = Math.Max(0f, elapsed / 2000f);
                            if (t > 1f)
                                t = 1f;
                            float ease = 1f - (1f - t) * (1f - t);
                            x = -carW + (int)((centerX + carW) * ease);
                            bob = (int)(Math.Sin(elapsed / 90.0) * 5);
                        }
                        else if (elapsed < 5000)
                        {
                            x = centerX;
                        }
                        else
                        {
                            float t = (elapsed - 5000) / 2400f;
                            if (t < 0f)
                                t = 0f;
                            if (t > 1f)
                                t = 1f;
                            float ease = t * t;
                            x = centerX + (int)((w + 30 - centerX) * ease);
                            bob = (int)(Math.Sin(elapsed / 80.0) * 5);
                        }
                        var car = new Rectangle(x, ground - carH - bob, carW, carH);
                        g.DrawImage(shown, car);
                        if (elapsed >= 3600 && elapsed < 5400 && _copTicket != null)
                        {
                            float t = (elapsed - 3600) / 1400f;
                            if (t < 0f)
                                t = 0f;
                            if (t > 1f)
                                t = 1f;
                            float ease = t * t;
                            int tw = 80 + (int)((Math.Min(w, h) * 0.72f - 80f) * ease);
                            int th = Math.Max(2, (int)(tw * (_copTicket.Height / (float)Math.Max(1, _copTicket.Width))));
                            int sx = car.X + (int)(car.Width * 0.46f);
                            int sy = car.Y + (int)(car.Height * 0.18f);
                            int ex = (w - tw) / 2;
                            int ey = (h - th) / 2 + (int)(h * 0.08f * ease);
                            int tx = sx + (int)((ex - sx) * ease);
                            int ty = sy + (int)((ey - sy) * ease);
                            GraphicsState state = g.Save();
                            g.TranslateTransform(tx + tw / 2f, ty + th / 2f);
                            g.RotateTransform(-18f * ease);
                            g.DrawImage(_copTicket, -tw / 2, -th / 2, tw, th);
                            g.Restore(state);
                        }
                        DrawTripPrice(g, elapsed, 3600);
                    }
                    _guyOverlay.Present(bmp, Parent.PointToScreen(Location));
                }
            }

            private void PresentCowboy()
            {
                if (Parent == null)
                    return;
                Image[] frames = FramesFor(6);
                if (frames.Length == 0)
                    return;
                Image pose = frames[Math.Min(_showFrame, frames.Length - 1)];
                if (_guyOverlay == null || _guyOverlay.IsDisposed)
                {
                    _guyOverlay = new WalkerOverlay();
                    Form owner = FindForm();
                    if (owner != null)
                        _guyOverlay.Show(owner);
                    else
                        _guyOverlay.Show();
                }
                using (var bmp = new Bitmap(Math.Max(2, Width), Math.Max(2, Height), PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Transparent);
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        int side = Math.Max(80, Math.Min(Width - 16, Height - 170));
                        var dest = new Rectangle((Width - side) / 2, Height - side - 6, side, side);
                        g.DrawImage(pose, dest);
                        if (_cowboyDigit > 0)
                        {
                            bool ready = _cowboyDigit == 4;
                            string text = ready ? "GET READY!" : _cowboyDigit.ToString();
                            float px = ready ? 110f : 150f;
                            if (ready)
                            {
                                while (px > 56f)
                                {
                                    using (var probe = new Font("Arial", px, FontStyle.Bold, GraphicsUnit.Pixel))
                                    {
                                        if (g.MeasureString(text, probe).Width <= Width - 28)
                                            break;
                                    }
                                    px -= 4f;
                                }
                            }
                            using (var font = new Font("Arial", px, FontStyle.Bold, GraphicsUnit.Pixel))
                            using (var path = new GraphicsPath())
                            {
                                var measured = g.MeasureString(text, font);
                                float x = (Width - measured.Width) / 2f;
                                path.AddString(
                                    text,
                                    font.FontFamily,
                                    (int)FontStyle.Bold,
                                    px,
                                    new PointF(x, 10f),
                                    StringFormat.GenericTypographic);
                                using (var pen = new Pen(Color.Black, ready ? 12f : 16f) { LineJoin = LineJoin.Round })
                                    g.DrawPath(pen, path);
                                using (var brush = new SolidBrush(Color.White))
                                    g.FillPath(brush, path);
                            }
                        }
                        int cowboyElapsed = (int)(DateTime.UtcNow - _showUtc).TotalMilliseconds;
                        DrawTripPrice(g, cowboyElapsed, 5600);
                    }
                    Point screen = Parent.PointToScreen(Location);
                    _guyOverlay.Present(bmp, screen);
                }
            }

            private void PresentWalker()
            {
                if (Parent == null)
                    return;
                int elapsed = (int)(DateTime.UtcNow - _showUtc).TotalMilliseconds;
                Rectangle guy;
                Rectangle wallet;
                bool showWallet;
                Image pose;
                WalkerPose(elapsed, out guy, out wallet, out showWallet, out pose);
                _guyOverlay = PresentSprite(_guyOverlay, pose, guy);
                if (showWallet)
                    _walletOverlay = PresentSprite(_walletOverlay, _walkerWallet, wallet);
                else if (_walletOverlay != null)
                    _walletOverlay.Hide();
                PresentTripPrice(elapsed, 3840);
            }

            private WalkerOverlay _priceOverlay;

            private void PresentTripPrice(int elapsed, int showAt)
            {
                bool show = !string.IsNullOrEmpty(TripPriceText)
                    && elapsed >= showAt
                    && elapsed < showAt + 3000;
                if (!show || Parent == null)
                {
                    if (_priceOverlay != null && !_priceOverlay.IsDisposed)
                        _priceOverlay.Hide();
                    return;
                }
                int w = Math.Max(240, Width);
                int h = 160;
                using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                    DrawTripPrice(g, elapsed, showAt, w);
                    if (_priceOverlay == null || _priceOverlay.IsDisposed)
                    {
                        _priceOverlay = new WalkerOverlay();
                        Form owner = FindForm();
                        if (owner != null)
                            _priceOverlay.Show(owner);
                        else
                            _priceOverlay.Show();
                    }
                    _priceOverlay.Present(bmp, Parent.PointToScreen(Location));
                }
            }

            private void DrawTripPrice(Graphics g, int elapsed, int showAt)
            {
                DrawTripPrice(g, elapsed, showAt, Width);
            }

            private void DrawTripPrice(Graphics g, int elapsed, int showAt, int canvasWidth)
            {
                if (string.IsNullOrEmpty(TripPriceText))
                    return;
                if (elapsed < showAt || elapsed >= showAt + 3000)
                    return;
                if (!_priceSoundPlayed)
                {
                    _priceSoundPlayed = true;
                    PlayMp3Once(FindDropMp3("cancel-price"), "CancelPrice");
                }
                string text = TripPriceText;
                float px = 140f;
                while (px > 48f)
                {
                    using (var probe = new Font("Arial", px, FontStyle.Bold, GraphicsUnit.Pixel))
                    {
                        if (g.MeasureString(text, probe).Width <= canvasWidth - 24)
                            break;
                    }
                    px -= 4f;
                }
                using (var font = new Font("Arial", px, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var path = new GraphicsPath())
                {
                    var measured = g.MeasureString(text, font);
                    float x = (canvasWidth - measured.Width) / 2f;
                    path.AddString(
                        text,
                        font.FontFamily,
                        (int)FontStyle.Bold,
                        px,
                        new PointF(x, 6f),
                        StringFormat.GenericTypographic);
                    using (var pen = new Pen(Color.Black, 10f) { LineJoin = LineJoin.Round })
                        g.DrawPath(pen, path);
                    using (var brush = new SolidBrush(Color.Red))
                        g.FillPath(brush, path);
                }
            }

            private WalkerOverlay PresentSprite(WalkerOverlay overlay, Image image, Rectangle dest)
            {
                if (image == null || dest.Width < 2 || dest.Height < 2 || Parent == null)
                    return overlay;
                if (overlay == null || overlay.IsDisposed)
                {
                    overlay = new WalkerOverlay();
                    Form owner = FindForm();
                    if (owner != null)
                        overlay.Show(owner);
                    else
                        overlay.Show();
                }
                using (var bmp = new Bitmap(dest.Width, dest.Height, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Transparent);
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(image, new Rectangle(0, 0, dest.Width, dest.Height));
                    }
                    Point screen = Parent.PointToScreen(new Point(Location.X + dest.X, Location.Y + dest.Y));
                    overlay.Present(bmp, screen);
                }
                return overlay;
            }

            private void CloseWalkerOverlays()
            {
                CloseOverlay(ref _guyOverlay);
                CloseOverlay(ref _walletOverlay);
                CloseOverlay(ref _priceOverlay);
            }

            private static void CloseOverlay(ref WalkerOverlay overlay)
            {
                if (overlay == null)
                    return;
                WalkerOverlay found = overlay;
                overlay = null;
                if (!found.IsDisposed)
                {
                    found.Hide();
                    found.Dispose();
                }
            }

            private void PaintWalker(Graphics g)
            {
                int elapsed = (int)(DateTime.UtcNow - _showUtc).TotalMilliseconds;
                Rectangle guy;
                Rectangle wallet;
                bool showWallet;
                Image pose;
                WalkerPose(elapsed, out guy, out wallet, out showWallet, out pose);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                if (showWallet && _walkerWallet != null)
                    g.DrawImage(_walkerWallet, wallet);
                if (pose != null)
                    g.DrawImage(pose, guy);
            }

            private void ApplyCharacterRegion()
            {
                if (IsWalkerScene())
                {
                    ApplyWalkerRegion();
                    return;
                }
                Image[] frames = FramesFor(_showCharacter);
                if (frames.Length == 0 || Width < 2 || Height < 2)
                    return;
                Image image = frames[_showFrame % frames.Length];
                int side = Math.Max(1, Math.Min(Width, Height) - 24);
                var dest = new Rectangle((Width - side) / 2, (Height - side) / 2, side, side);
                Region next = RegionFromAlpha(image, dest, Size);
                Region previous = Region;
                Region = next;
                if (previous != null)
                    previous.Dispose();
            }

            private void ClearWindowRegion()
            {
                Region previous = Region;
                Region = null;
                if (previous != null)
                    previous.Dispose();
            }

            private void ApplyWalkerRegion()
            {
                if (Width < 2 || Height < 2)
                    return;
                int elapsed = (int)(DateTime.UtcNow - _showUtc).TotalMilliseconds;
                Rectangle guy;
                Rectangle wallet;
                bool showWallet;
                Image pose;
                WalkerPose(elapsed, out guy, out wallet, out showWallet, out pose);
                var next = new Region();
                next.MakeEmpty();
                bool any = AddSpriteRegion(next, pose, guy);
                if (showWallet)
                    any |= AddSpriteRegion(next, _walkerWallet, wallet);
                if (!any)
                {
                    next.Dispose();
                    next = new Region(new Rectangle(0, 0, Width, Height));
                }
                Rectangle sprite = guy;
                if (showWallet)
                    sprite = Rectangle.Union(sprite, wallet);
                sprite.Inflate(8, 8);
                Rectangle erase = _walkerErase.IsEmpty ? sprite : Rectangle.Union(_walkerErase, sprite);
                _walkerErase = sprite;
                Region previous = Region;
                Region = next;
                if (previous != null)
                    previous.Dispose();
                if (Parent != null)
                    Parent.Invalidate(new Rectangle(Left + erase.X, Top + erase.Y, erase.Width, erase.Height), false);
            }

            private static bool AddSpriteRegion(Region region, Image image, Rectangle dest)
            {
                if (image == null || dest.Width < 2 || dest.Height < 2)
                    return false;
                using (var bmp = new Bitmap(dest.Width, dest.Height, PixelFormat.Format32bppArgb))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.DrawImage(image, new Rectangle(0, 0, dest.Width, dest.Height));
                    using (Region part = RegionFromBitmap(bmp, 1))
                    {
                        part.Translate(dest.X, dest.Y);
                        region.Union(part);
                    }
                }
                return true;
            }

            private static Region RegionFromAlpha(Image image, Rectangle dest, Size size)
            {
                using (var bmp = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height), PixelFormat.Format32bppArgb))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.DrawImage(image, dest);
                    return RegionFromBitmap(bmp);
                }
            }

            private static Region RegionFromBitmap(Bitmap bmp, int step = 1)
            {
                if (step < 1)
                    step = 1;
                var path = new GraphicsPath();
                var bits = bmp.LockBits(
                    new Rectangle(0, 0, bmp.Width, bmp.Height),
                    ImageLockMode.ReadOnly,
                    PixelFormat.Format32bppArgb);
                try
                {
                    int stride = bits.Stride;
                    var raw = new byte[stride * bmp.Height];
                    System.Runtime.InteropServices.Marshal.Copy(bits.Scan0, raw, 0, raw.Length);
                    for (int y = 0; y < bmp.Height; y += step)
                    {
                        int row = y * stride;
                        int start = -1;
                        int tall = Math.Min(step, bmp.Height - y);
                        for (int x = 0; x < bmp.Width; x += step)
                        {
                            if (raw[row + x * 4 + 3] > 16)
                            {
                                if (start < 0)
                                    start = x;
                            }
                            else if (start >= 0)
                            {
                                path.AddRectangle(new Rectangle(start, y, x - start, tall));
                                start = -1;
                            }
                        }
                        if (start >= 0)
                            path.AddRectangle(new Rectangle(start, y, bmp.Width - start, tall));
                    }
                }
                finally
                {
                    bmp.UnlockBits(bits);
                }
                if (path.PointCount == 0)
                    path.AddRectangle(new Rectangle(0, 0, Math.Max(1, bmp.Width), Math.Max(1, bmp.Height)));
                var region = new Region(path);
                path.Dispose();
                return region;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                if (_characterShow)
                {
                    PaintCharacterShow(g);
                    return;
                }
                g.Clear(BackColor);
                var big = BigBox(Size);
                double elapsed = (DateTime.UtcNow - _startedUtc).TotalMilliseconds;
                int lit;
                float frac;
                ChasePosition(elapsed, out lit, out frac);
                float pulse = 0.55f + 0.45f * (float)Math.Sin(elapsed / 140.0);

                using (var fill = new SolidBrush(Color.FromArgb(32, 26, 18)))
                    g.FillRectangle(fill, big);
                using (var pen = new Pen(Color.FromArgb(210, 140, 40), 3f))
                    g.DrawRectangle(pen, big);

                int next = _edgeBoxes.Length == 0 ? 0 : (lit + 1) % _edgeBoxes.Length;
                for (int i = 0; i < _edgeBoxes.Length; i++)
                {
                    float glow = i == lit ? 1f - frac : i == next ? frac : 0f;
                    int r = 72 + (int)((255 - 72) * glow);
                    int gg = 48 + (int)((186 - 48) * glow);
                    int b = 22 + (int)((64 - 22) * glow);
                    using (var box = new SolidBrush(Color.FromArgb(r, gg, b)))
                        g.FillRectangle(box, _edgeBoxes[i]);
                    int prize = i < _prizeImageByBox.Length ? _prizeImageByBox[i] : -1;
                    int icon = i < _iconImageByBox.Length ? _iconImageByBox[i] : -1;
                    int character = i < _characterImageByBox.Length ? _characterImageByBox[i] : -1;
                    if (prize >= 0 && prize < _prizes.Length)
                        DrawPrizeImage(g, _prizes[prize], _edgeBoxes[i], glow);
                    else if (icon >= 0 && icon < _icons.Length)
                        DrawPrizeImage(g, _icons[icon], _edgeBoxes[i], glow);
                    else if (i == _bigMoneyBox && _bigMoney != null)
                        DrawPrizeImage(g, _bigMoney, _edgeBoxes[i], glow);
                    else if (character >= 0 && character < _characters.Length)
                        DrawPrizeImage(g, _characters[character], _edgeBoxes[i], glow);
                    using (var p = new Pen(Color.FromArgb(210 + (int)(45 * glow), 140 + (int)(88 * glow), 40 + (int)(100 * glow)), glow > 0.5f ? 3f : 1f))
                        g.DrawRectangle(p, _edgeBoxes[i]);
                }

                string title = "Cancelled";
                string trip = (TripText ?? "").Trim();
                using (var ink = new SolidBrush(Color.FromArgb(
                    255,
                    (int)(160 + 80 * pulse),
                    (int)(70 + 40 * pulse))))
                {
                    var titleSize = g.MeasureString(title, _titleFont);
                    float tx = big.Left + (big.Width - titleSize.Width) / 2f;
                    float ty = big.Top + big.Height / 2f - titleSize.Height;
                    g.DrawString(title, _titleFont, ink, tx, ty);
                    if (trip.Length > 0)
                    {
                        var tripSize = g.MeasureString(trip, _tripFont);
                        g.DrawString(
                            trip,
                            _tripFont,
                            ink,
                            big.Left + (big.Width - tripSize.Width) / 2f,
                            ty + titleSize.Height + 6f);
                    }
                }
            }

            private sealed class WalkerOverlay : Form
            {
                public WalkerOverlay()
                {
                    FormBorderStyle = FormBorderStyle.None;
                    ShowInTaskbar = false;
                    StartPosition = FormStartPosition.Manual;
                    ShowIcon = false;
                }

                protected override bool ShowWithoutActivation
                {
                    get { return true; }
                }

                protected override CreateParams CreateParams
                {
                    get
                    {
                        const int WS_EX_LAYERED = 0x00080000;
                        const int WS_EX_TOOLWINDOW = 0x00000080;
                        const int WS_EX_NOACTIVATE = 0x08000000;
                        CreateParams cp = base.CreateParams;
                        cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                        return cp;
                    }
                }

                public void Present(Bitmap bitmap, Point screen)
                {
                    if (bitmap == null || IsDisposed)
                        return;
                    if (!IsHandleCreated)
                        CreateHandle();
                    IntPtr screenDc = GetDC(IntPtr.Zero);
                    IntPtr memDc = CreateCompatibleDC(screenDc);
                    IntPtr dib = IntPtr.Zero;
                    IntPtr old = IntPtr.Zero;
                    try
                    {
                        var info = new BITMAPINFO();
                        info.biSize = Marshal.SizeOf(typeof(BITMAPINFO));
                        info.biWidth = bitmap.Width;
                        info.biHeight = -bitmap.Height;
                        info.biPlanes = 1;
                        info.biBitCount = 32;
                        IntPtr bits;
                        dib = CreateDIBSection(screenDc, ref info, 0, out bits, IntPtr.Zero, 0);
                        if (dib == IntPtr.Zero || bits == IntPtr.Zero)
                            return;
                        var data = bitmap.LockBits(
                            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                            ImageLockMode.ReadOnly,
                            PixelFormat.Format32bppArgb);
                        try
                        {
                            int widthBytes = bitmap.Width * 4;
                            var row = new byte[widthBytes];
                            for (int y = 0; y < bitmap.Height; y++)
                            {
                                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, widthBytes);
                                for (int x = 0; x < widthBytes; x += 4)
                                {
                                    byte a = row[x + 3];
                                    row[x] = (byte)(row[x] * a / 255);
                                    row[x + 1] = (byte)(row[x + 1] * a / 255);
                                    row[x + 2] = (byte)(row[x + 2] * a / 255);
                                }
                                Marshal.Copy(row, 0, bits + y * widthBytes, widthBytes);
                            }
                        }
                        finally
                        {
                            bitmap.UnlockBits(data);
                        }
                        old = SelectObject(memDc, dib);
                        var size = new SIZE { cx = bitmap.Width, cy = bitmap.Height };
                        var dst = new POINT { x = screen.X, y = screen.Y };
                        var src = new POINT();
                        var blend = new BLENDFUNCTION
                        {
                            BlendOp = 0,
                            SourceConstantAlpha = 255,
                            AlphaFormat = 1
                        };
                        UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, 2);
                    }
                    finally
                    {
                        if (old != IntPtr.Zero)
                            SelectObject(memDc, old);
                        if (dib != IntPtr.Zero)
                            DeleteObject(dib);
                        DeleteDC(memDc);
                        ReleaseDC(IntPtr.Zero, screenDc);
                    }
                }

                [DllImport("user32.dll", SetLastError = true)]
                private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

                [DllImport("user32.dll", SetLastError = true)]
                private static extern IntPtr GetDC(IntPtr hWnd);

                [DllImport("user32.dll")]
                private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

                [DllImport("gdi32.dll", SetLastError = true)]
                private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

                [DllImport("gdi32.dll")]
                private static extern bool DeleteDC(IntPtr hdc);

                [DllImport("gdi32.dll")]
                private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

                [DllImport("gdi32.dll")]
                private static extern bool DeleteObject(IntPtr hObject);

                [DllImport("gdi32.dll", SetLastError = true)]
                private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint usage, out IntPtr bits, IntPtr section, uint offset);

                [StructLayout(LayoutKind.Sequential)]
                private struct POINT
                {
                    public int x;
                    public int y;
                }

                [StructLayout(LayoutKind.Sequential)]
                private struct SIZE
                {
                    public int cx;
                    public int cy;
                }

                [StructLayout(LayoutKind.Sequential)]
                private struct BLENDFUNCTION
                {
                    public byte BlendOp;
                    public byte BlendFlags;
                    public byte SourceConstantAlpha;
                    public byte AlphaFormat;
                }

                [StructLayout(LayoutKind.Sequential, Pack = 1)]
                private struct BITMAPINFO
                {
                    public int biSize;
                    public int biWidth;
                    public int biHeight;
                    public short biPlanes;
                    public short biBitCount;
                    public int biCompression;
                    public int biSizeImage;
                    public int biXPelsPerMeter;
                    public int biYPelsPerMeter;
                    public int biClrUsed;
                    public int biClrImportant;
                }
            }
        }
    }
}
