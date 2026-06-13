using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Hiatme_Tool_Suite_v3
{
    internal sealed class DriverEmailRecord
    {
        [JsonProperty("wellryde_sec_id")]
        public string WellRydeSecId { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("schedule_tab_key")]
        public string ScheduleTabKey { get; set; }

        [JsonProperty("email")]
        public string Email { get; set; }

        [JsonProperty("updated_at")]
        public string UpdatedAt { get; set; }

        [JsonProperty("updated_by")]
        public string UpdatedBy { get; set; }

        public static DriverEmailRecord FromProfile(SupeyDriverProfile profile, string updatedBy)
        {
            if (profile == null)
                return null;

            string name = (profile.Name ?? "").Trim();
            string sec = (profile.WellRydeSecId ?? "").Trim();
            string tab = (profile.ScheduleTabKey ?? "").Trim();
            string email = (profile.Email ?? "").Trim();
            if (string.IsNullOrEmpty(sec) && string.IsNullOrEmpty(tab) && string.IsNullOrEmpty(name))
                return null;

            return new DriverEmailRecord
            {
                WellRydeSecId = sec,
                Name = name,
                ScheduleTabKey = tab,
                Email = email,
                UpdatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                UpdatedBy = (updatedBy ?? "").Trim(),
            };
        }
    }

    internal sealed class DriverEmailSyncResult
    {
        public bool ServerUsed { get; set; }
        public bool ServerUnreachable { get; set; }
        public bool RosterChanged { get; set; }
        public bool LocalSaved { get; set; }
        public bool ServerPushed { get; set; }
    }

    /// <summary>
    /// Office-shared driver emails — local JSON under hiatme_config, synced with AI panel when online.
    /// Mailer reads roster + local registry only (works offline).
    /// </summary>
    internal static class ScheduleBuilderDriverEmailsRegistry
    {
        public static string LocalRosterPath =>
            Path.Combine(AppContext.BaseDirectory ?? "", "hiatme_config", "driver_emails", "roster.json");

        public static List<DriverEmailRecord> LoadLocal()
        {
            try
            {
                string path = LocalRosterPath;
                if (!File.Exists(path))
                    return new List<DriverEmailRecord>();

                var root = JObject.Parse(File.ReadAllText(path));
                var arr = root["drivers"] as JArray;
                if (arr == null || arr.Count == 0)
                    return new List<DriverEmailRecord>();

                return arr.ToObject<List<DriverEmailRecord>>()
                    ?? new List<DriverEmailRecord>();
            }
            catch
            {
                return new List<DriverEmailRecord>();
            }
        }

        public static bool TrySaveLocal(IList<DriverEmailRecord> drivers)
        {
            try
            {
                string path = LocalRosterPath;
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                var doc = new JObject
                {
                    ["version"] = 1,
                    ["drivers"] = JArray.FromObject(drivers ?? new List<DriverEmailRecord>()),
                    ["updated_at"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                };
                File.WriteAllText(path, doc.ToString(Formatting.Indented));
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void ApplyLocalRegistryToRoster(IReadOnlyList<SupeyDriverProfile> roster)
        {
            if (roster == null || roster.Count == 0)
                return;

            var records = LoadLocal();
            if (records == null || records.Count == 0)
                return;

            ApplyRecordsToRoster(roster, records, fillEmptyOnly: true);
        }

        public static bool UpdateLocalFromRoster(IList<SupeyDriverProfile> roster, string updatedBy)
        {
            if (roster == null || roster.Count == 0)
                return false;

            try
            {
                var merged = MergeRecordLists(LoadLocal(), RecordsFromRoster(roster, updatedBy));
                return TrySaveLocal(merged);
            }
            catch
            {
                return false;
            }
        }

        public static async Task<DriverEmailSyncResult> SyncWithServerAsync(
            HiatmeAiSettings settings,
            IList<SupeyDriverProfile> rosterMutable,
            string updatedBy,
            CancellationToken cancellationToken = default)
        {
            var result = new DriverEmailSyncResult();
            if (rosterMutable == null)
                return result;

            var roster = (IReadOnlyList<SupeyDriverProfile>)rosterMutable;

            var local = LoadLocal();
            ApplyRecordsToRoster(roster, local, fillEmptyOnly: true);

            if (HiatmeGeoSettings.UseServer && settings != null
                && !string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                result.ServerUsed = true;
                try
                {
                    var server = await HiatmeAiClient.GetDriverEmailsAsync(settings, cancellationToken)
                        .ConfigureAwait(false);
                    if (server != null)
                    {
                        local = MergeRecordLists(local, server);
                        result.LocalSaved = TrySaveLocal(local);
                        // Roster email is source of truth on this PC — never stomp edits from stale server rows.
                        bool changed = ApplyRecordsToRoster(roster, local, fillEmptyOnly: true);
                        result.RosterChanged = changed;

                        var push = RecordsFromRoster(rosterMutable, updatedBy);
                        local = MergeRecordLists(local, push);
                        TrySaveLocal(local);
                        result.ServerPushed = await HiatmeAiClient.MergeDriverEmailsAsync(
                            settings, local, updatedBy, cancellationToken).ConfigureAwait(false);
                    }
                    else
                        result.ServerUnreachable = true;
                }
                catch
                {
                    result.ServerUnreachable = true;
                }
            }
            else
            {
                var push = RecordsFromRoster(rosterMutable, updatedBy);
                local = MergeRecordLists(local, push);
                result.LocalSaved = TrySaveLocal(local);
            }

            return result;
        }

        /// <summary>Merge roster emails into local registry and POST to the AI panel (awaited).</summary>
        public static async Task<bool> PushToServerAsync(
            HiatmeAiSettings settings,
            IList<SupeyDriverProfile> roster,
            string updatedBy,
            CancellationToken cancellationToken = default)
        {
            if (roster == null || roster.Count == 0)
                return false;

            var merged = MergeRecordLists(LoadLocal(), RecordsFromRoster(roster, updatedBy));
            if (!TrySaveLocal(merged))
                return false;

            settings = settings ?? HiatmeAiSettings.Load();
            if (settings == null || string.IsNullOrWhiteSpace(settings.BaseUrl))
                return false;
            if (!HiatmeGeoSettings.UseServer)
                return false;

            try
            {
                return await HiatmeAiClient.MergeDriverEmailsAsync(
                    settings, merged, updatedBy, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Background push to the AI panel. Never blocks or throws on the UI thread —
        /// settings are resolved inside <see cref="Task.Run"/> when omitted.
        /// </summary>
        public static void TryPushToServerFireAndForget(HiatmeAiSettings settings, string updatedBy)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    settings = settings ?? HiatmeAiSettings.Load();
                    if (settings == null || string.IsNullOrWhiteSpace(settings.BaseUrl))
                        return;
                    if (!HiatmeGeoSettings.UseServer)
                        return;

                    var local = LoadLocal();
                    if (local == null || local.Count == 0)
                        return;

                    await HiatmeAiClient.MergeDriverEmailsAsync(
                        settings, local, updatedBy, CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    /* offline — local copy remains */
                }
            });
        }

        public static List<DriverEmailRecord> RecordsFromRoster(
            IEnumerable<SupeyDriverProfile> roster,
            string updatedBy)
        {
            var list = new List<DriverEmailRecord>();
            if (roster == null)
                return list;

            foreach (var profile in roster)
            {
                var record = DriverEmailRecord.FromProfile(profile, updatedBy);
                if (record != null)
                    UpsertRecord(list, record);
            }

            return list;
        }

        internal static List<DriverEmailRecord> MergeRecordLists(
            IList<DriverEmailRecord> local,
            IList<DriverEmailRecord> server)
        {
            var merged = new List<DriverEmailRecord>();
            if (local != null)
            {
                foreach (var record in local)
                {
                    if (record != null)
                        UpsertRecord(merged, record);
                }
            }

            if (server != null)
            {
                foreach (var record in server)
                {
                    if (record != null)
                        UpsertRecord(merged, record);
                }
            }

            return merged;
        }

        private static bool ApplyRecordsToRoster(
            IReadOnlyList<SupeyDriverProfile> roster,
            IList<DriverEmailRecord> records,
            bool fillEmptyOnly)
        {
            if (roster == null || records == null || records.Count == 0)
                return false;

            bool changed = false;
            foreach (var record in records)
            {
                if (record == null || string.IsNullOrWhiteSpace(record.Email))
                    continue;

                var profile = FindProfileForRecord(roster, record);
                if (profile == null)
                    continue;

                string incoming = record.Email.Trim();
                string existing = (profile.Email ?? "").Trim();
                if (string.Equals(existing, incoming, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (fillEmptyOnly)
                {
                    if (string.IsNullOrEmpty(existing))
                    {
                        profile.Email = incoming;
                        changed = true;
                    }

                    continue;
                }

                if (string.IsNullOrEmpty(existing))
                {
                    profile.Email = incoming;
                    changed = true;
                    continue;
                }

                double inTs = ParseUpdatedAt(record.UpdatedAt);
                double rosterTs = RegistryTimestampForProfile(profile);
                if (inTs > rosterTs)
                {
                    profile.Email = incoming;
                    changed = true;
                }
            }

            return changed;
        }

        private static double RegistryTimestampForProfile(SupeyDriverProfile profile)
        {
            if (profile == null)
                return 0;

            string key = DriverKeyFromProfile(profile);
            if (key.Length == 0)
                return 0;

            foreach (var rec in LoadLocal())
            {
                if (rec != null && DriverKey(rec) == key)
                    return ParseUpdatedAt(rec.UpdatedAt);
            }

            return 0;
        }

        private static SupeyDriverProfile FindProfileForRecord(
            IReadOnlyList<SupeyDriverProfile> roster,
            DriverEmailRecord record)
        {
            if (roster == null || record == null)
                return null;

            string sec = (record.WellRydeSecId ?? "").Trim();
            if (sec.Length > 0)
            {
                var hit = roster.FirstOrDefault(p =>
                    p != null && string.Equals(p.WellRydeSecId, sec, StringComparison.OrdinalIgnoreCase));
                if (hit != null)
                    return hit;
            }

            string tab = (record.ScheduleTabKey ?? "").Trim();
            if (tab.Length > 0)
            {
                var hit = ScheduleBuilderDriverMapRouting.FindProfileForScheduleTab(roster, tab);
                if (hit != null)
                    return hit;
            }

            string name = NormalizeName(record.Name);
            if (name.Length > 0)
            {
                return roster.FirstOrDefault(p =>
                    p != null && string.Equals(NormalizeName(p.Name), name, StringComparison.OrdinalIgnoreCase));
            }

            return null;
        }

        private static void UpsertRecord(IList<DriverEmailRecord> drivers, DriverEmailRecord record)
        {
            if (drivers == null || record == null)
                return;

            string key = DriverKey(record);
            if (key.Length == 0)
                return;

            for (int i = 0; i < drivers.Count; i++)
            {
                var existing = drivers[i];
                if (existing == null)
                    continue;
                if (DriverKey(existing) == key)
                {
                    drivers[i] = MergePair(existing, record);
                    return;
                }
            }

            drivers.Add(record);
        }

        private static DriverEmailRecord MergePair(DriverEmailRecord existing, DriverEmailRecord incoming)
        {
            if (existing == null)
                return incoming;
            if (incoming == null)
                return existing;

            var merged = new DriverEmailRecord
            {
                WellRydeSecId = !string.IsNullOrWhiteSpace(existing.WellRydeSecId)
                    ? existing.WellRydeSecId
                    : incoming.WellRydeSecId,
                Name = !string.IsNullOrWhiteSpace(existing.Name) ? existing.Name : incoming.Name,
                ScheduleTabKey = !string.IsNullOrWhiteSpace(existing.ScheduleTabKey)
                    ? existing.ScheduleTabKey
                    : incoming.ScheduleTabKey,
                UpdatedBy = incoming.UpdatedBy ?? existing.UpdatedBy,
            };

            string exEmail = (existing.Email ?? "").Trim();
            string inEmail = (incoming.Email ?? "").Trim();
            double exTs = ParseUpdatedAt(existing.UpdatedAt);
            double inTs = ParseUpdatedAt(incoming.UpdatedAt);

            if (!string.IsNullOrEmpty(inEmail) && string.IsNullOrEmpty(exEmail))
            {
                merged.Email = inEmail;
                merged.UpdatedAt = incoming.UpdatedAt ?? existing.UpdatedAt;
            }
            else if (!string.IsNullOrEmpty(exEmail) && string.IsNullOrEmpty(inEmail))
            {
                merged.Email = exEmail;
                merged.UpdatedAt = existing.UpdatedAt;
            }
            else if (!string.IsNullOrEmpty(inEmail))
            {
                if (inTs >= exTs)
                {
                    merged.Email = inEmail;
                    merged.UpdatedAt = incoming.UpdatedAt ?? existing.UpdatedAt;
                }
                else
                {
                    merged.Email = exEmail;
                    merged.UpdatedAt = existing.UpdatedAt;
                }
            }

            return merged;
        }

        private static string DriverKey(DriverEmailRecord record)
        {
            if (record == null)
                return string.Empty;

            string sec = (record.WellRydeSecId ?? "").Trim();
            if (sec.Length > 0)
                return "sec:" + sec.ToLowerInvariant();

            string tab = (record.ScheduleTabKey ?? "").Trim();
            if (tab.Length > 0)
                return "tab:" + tab.ToLowerInvariant();

            string name = NormalizeName(record.Name).ToLowerInvariant();
            return name.Length > 0 ? "name:" + name : string.Empty;
        }

        private static string DriverKeyFromProfile(SupeyDriverProfile profile) =>
            DriverKey(DriverEmailRecord.FromProfile(profile, ""));

        private static string NormalizeName(string raw) =>
            string.Join(" ", (raw ?? "").Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)).Trim();

        private static double ParseUpdatedAt(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return 0;

            string s = raw.Trim();
            if (s.EndsWith("Z", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(0, s.Length - 1) + "+00:00";

            if (DateTimeOffset.TryParse(s, out var dto))
                return dto.UtcDateTime.Ticks;

            return 0;
        }
    }
}
