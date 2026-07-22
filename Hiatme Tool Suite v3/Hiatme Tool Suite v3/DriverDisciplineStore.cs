using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Local library + AI panel sync for Driver Discipline write-ups.
    /// Canonical folder: <c>F:\Write ups</c> (override with env HIATME_DISCIPLINE_ROOT).
    /// </summary>
    internal static class DriverDisciplineStore
    {
        public static string LocalRoot
        {
            get
            {
                string env = (Environment.GetEnvironmentVariable("HIATME_DISCIPLINE_ROOT") ?? "").Trim();
                if (!string.IsNullOrEmpty(env))
                    return env;
                return @"F:\Write ups";
            }
        }

        public static string LocalIndexPath => Path.Combine(LocalRoot, "index.json");

        public static string DriverSlug(string name)
        {
            string slug = Regex.Replace((name ?? "unknown").Trim(), @"[^\w\s-]", "");
            slug = Regex.Replace(slug, @"\s+", "_").ToLowerInvariant();
            if (slug.Length > 80) slug = slug.Substring(0, 80);
            return string.IsNullOrEmpty(slug) ? "unknown" : slug;
        }

        public static string CaseIdSafe(string raw)
        {
            string s = Regex.Replace((raw ?? "").Trim(), @"[^\w.\-]+", "_").Trim('.', '_');
            if (s.Length > 80) s = s.Substring(0, 80);
            if (string.IsNullOrEmpty(s))
                s = "CA-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            return s;
        }

        public static string CaseFolder(string driverName, string caseId)
        {
            return Path.Combine(LocalRoot, "drivers", DriverSlug(driverName), CaseIdSafe(caseId));
        }

        public static List<DriverDisciplineIndexItem> LoadLocalIndex()
        {
            try
            {
                if (!File.Exists(LocalIndexPath))
                    return new List<DriverDisciplineIndexItem>();
                var root = JObject.Parse(File.ReadAllText(LocalIndexPath));
                var arr = root["items"] as JArray;
                if (arr == null || arr.Count == 0)
                    return new List<DriverDisciplineIndexItem>();
                return arr.ToObject<List<DriverDisciplineIndexItem>>()
                    ?? new List<DriverDisciplineIndexItem>();
            }
            catch
            {
                return new List<DriverDisciplineIndexItem>();
            }
        }

        public static void SaveLocalIndex(IList<DriverDisciplineIndexItem> items)
        {
            Directory.CreateDirectory(LocalRoot);
            var root = new JObject
            {
                ["version"] = 1,
                ["updated_at"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ["items"] = JArray.FromObject(items ?? new List<DriverDisciplineIndexItem>()),
            };
            string tmp = LocalIndexPath + ".tmp";
            File.WriteAllText(tmp, root.ToString(Formatting.Indented));
            if (File.Exists(LocalIndexPath))
                File.Delete(LocalIndexPath);
            File.Move(tmp, LocalIndexPath);
        }

        public static DriverDisciplineMeta ToMeta(DriverDisciplineRecord r, string docxFilename = null)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));
            string caseId = CaseIdSafe(r.CaseNumber);
            string safeDriver = Regex.Replace(r.DriverName ?? "Driver", @"[^\w\-]+", "_").Trim('_');
            if (string.IsNullOrEmpty(safeDriver)) safeDriver = "Driver";
            string incident = r.IncidentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(docxFilename))
                docxFilename = "CorrectiveAction_" + safeDriver + "_" +
                               r.IncidentDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".docx";

            return new DriverDisciplineMeta
            {
                Version = 1,
                Id = caseId,
                CaseNumber = string.IsNullOrWhiteSpace(r.CaseNumber) ? caseId : r.CaseNumber.Trim(),
                DriverName = (r.DriverName ?? "").Trim(),
                DriverSlug = DriverSlug(r.DriverName),
                EmployeeId = (r.EmployeeId ?? "").Trim(),
                Vehicle = (r.Vehicle ?? "").Trim(),
                SupervisorName = (r.SupervisorName ?? "").Trim(),
                Department = (r.Department ?? "").Trim(),
                NoticeDate = r.NoticeDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                IncidentDate = incident,
                IncidentTime = (r.IncidentTime ?? "").Trim(),
                TripOrClientRef = (r.TripOrClientRef ?? "").Trim(),
                Location = (r.Location ?? "").Trim(),
                Violations = r.Violations != null
                    ? r.Violations.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).ToList()
                    : new List<string>(),
                ActionLevel = (r.ActionLevel ?? "").Trim(),
                FootageSummary = r.FootageSummary ?? "",
                Narrative = r.Narrative ?? "",
                PolicyCited = r.PolicyCited ?? "",
                PriorHistory = r.PriorHistory ?? "",
                CorrectiveAction = r.CorrectiveAction ?? "",
                FollowUpDate = r.FollowUpDate ?? "",
                DriverStatement = r.DriverStatement ?? "",
                FootageFolder = r.FootageFolder ?? "",
                ClipPaths = r.ClipPaths != null
                    ? r.ClipPaths.Where(p => !string.IsNullOrWhiteSpace(p)).ToList()
                    : new List<string>(),
                PreparedBy = (r.PreparedBy ?? "").Trim(),
                DocxFilename = docxFilename,
                CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                UpdatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                UpdatedBy = (r.PreparedBy ?? Environment.UserName ?? "").Trim(),
            };
        }

        public static DriverDisciplineRecord ToRecord(DriverDisciplineMeta m)
        {
            if (m == null) return new DriverDisciplineRecord();
            return new DriverDisciplineRecord
            {
                CaseNumber = m.CaseNumber ?? m.Id ?? "",
                NoticeDate = ParseDate(m.NoticeDate),
                DriverName = m.DriverName ?? "",
                EmployeeId = m.EmployeeId ?? "",
                Vehicle = m.Vehicle ?? "",
                SupervisorName = m.SupervisorName ?? "",
                Department = string.IsNullOrWhiteSpace(m.Department) ? "Operations" : m.Department,
                IncidentDate = ParseDate(m.IncidentDate),
                IncidentTime = m.IncidentTime ?? "",
                TripOrClientRef = m.TripOrClientRef ?? "",
                Location = m.Location ?? "",
                Violations = m.Violations ?? new List<string>(),
                ActionLevel = m.ActionLevel ?? "Written warning",
                FootageSummary = m.FootageSummary ?? "",
                Narrative = m.Narrative ?? "",
                PolicyCited = m.PolicyCited ?? "",
                PriorHistory = m.PriorHistory ?? "",
                CorrectiveAction = m.CorrectiveAction ?? "",
                FollowUpDate = m.FollowUpDate ?? "",
                DriverStatement = m.DriverStatement ?? "",
                FootageFolder = m.FootageFolder ?? "",
                ClipPaths = m.ClipPaths ?? new List<string>(),
                PreparedBy = m.PreparedBy ?? "",
            };
        }

        private static DateTime ParseDate(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return DateTime.Today;
            DateTime dt;
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out dt))
                return dt.Date;
            if (DateTime.TryParse(s, out dt))
                return dt.Date;
            return DateTime.Today;
        }

        public static DriverDisciplineIndexItem ToIndexItem(DriverDisciplineMeta m)
        {
            return new DriverDisciplineIndexItem
            {
                Id = m.Id,
                CaseNumber = m.CaseNumber,
                DriverName = m.DriverName,
                DriverSlug = m.DriverSlug,
                EmployeeId = m.EmployeeId,
                IncidentDate = m.IncidentDate,
                ActionLevel = m.ActionLevel,
                Violations = m.Violations != null ? new List<string>(m.Violations) : new List<string>(),
                CreatedAt = m.CreatedAt,
                PreparedBy = m.PreparedBy,
            };
        }

        /// <summary>Write meta + docx under local cache and update local index.</summary>
        public static string SaveLocal(DriverDisciplineMeta meta, byte[] docxBytes)
        {
            if (meta == null) throw new ArgumentNullException(nameof(meta));
            if (docxBytes == null || docxBytes.Length == 0)
                throw new ArgumentException("docx bytes required", nameof(docxBytes));

            string folder = CaseFolder(meta.DriverName, meta.Id);
            Directory.CreateDirectory(folder);
            string metaPath = Path.Combine(folder, "meta.json");
            string docxPath = Path.Combine(folder, meta.DocxFilename ?? "writeup.docx");

            // Preserve created_at on overwrite
            try
            {
                if (File.Exists(metaPath))
                {
                    var existing = JsonConvert.DeserializeObject<DriverDisciplineMeta>(File.ReadAllText(metaPath));
                    if (existing != null && !string.IsNullOrWhiteSpace(existing.CreatedAt))
                        meta.CreatedAt = existing.CreatedAt;
                }
            }
            catch { /* ignore */ }

            meta.UpdatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            File.WriteAllText(metaPath, JsonConvert.SerializeObject(meta, Formatting.Indented));
            File.WriteAllBytes(docxPath, docxBytes);

            var index = LoadLocalIndex();
            index.RemoveAll(i => string.Equals(i.Id, meta.Id, StringComparison.OrdinalIgnoreCase));
            index.Add(ToIndexItem(meta));
            index = index
                .OrderByDescending(i => i.CreatedAt ?? "")
                .ThenByDescending(i => i.IncidentDate ?? "")
                .ToList();
            SaveLocalIndex(index);
            return folder;
        }

        public static async Task<DriverDisciplineSaveResult> SaveAndSyncAsync(
            DriverDisciplineRecord record,
            byte[] docxBytes,
            HiatmeAiSettings settings,
            CancellationToken cancellationToken = default)
        {
            var result = new DriverDisciplineSaveResult();
            var meta = ToMeta(record);
            result.Meta = meta;

            try
            {
                result.LocalFolder = SaveLocal(meta, docxBytes);
                result.LocalOk = true;
            }
            catch (Exception ex)
            {
                result.LocalOk = false;
                result.Error = "Local save failed: " + ex.Message;
                return result;
            }

            if (settings == null)
            {
                result.ServerOk = false;
                result.Error = "Saved locally; AI panel settings missing.";
                return result;
            }

            var server = await HiatmeAiClient.SaveDriverDisciplineAsync(
                settings, meta, docxBytes, meta.UpdatedBy, cancellationToken).ConfigureAwait(false);
            result.ServerOk = server != null && server.Ok;
            if (!result.ServerOk)
                result.Error = server?.Error ?? "Panel save failed.";
            else if (server.Path != null)
                result.ServerPath = server.Path;

            return result;
        }

        public static async Task<List<DriverDisciplineIndexItem>> ListMergedAsync(
            HiatmeAiSettings settings,
            string driverFilter = null,
            CancellationToken cancellationToken = default)
        {
            var local = LoadLocalIndex();
            List<DriverDisciplineIndexItem> server = null;
            try
            {
                var doc = await HiatmeAiClient.ListDriverDisciplineAsync(
                    settings, driverFilter, null, 500, cancellationToken).ConfigureAwait(false);
                if (doc != null && doc.Ok && doc.Items != null)
                    server = doc.Items;
            }
            catch { /* offline */ }

            if (server == null)
            {
                if (!string.IsNullOrWhiteSpace(driverFilter))
                {
                    string slug = DriverSlug(driverFilter);
                    return local.Where(i =>
                            string.Equals(i.DriverSlug, slug, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(i.DriverName, driverFilter, StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(i => i.CreatedAt ?? "")
                        .ToList();
                }
                return local.OrderByDescending(i => i.CreatedAt ?? "").ToList();
            }

            // Prefer server index; fill gaps from local
            var map = new Dictionary<string, DriverDisciplineIndexItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var it in server)
            {
                if (it != null && !string.IsNullOrEmpty(it.Id))
                    map[it.Id] = it;
            }
            foreach (var it in local)
            {
                if (it != null && !string.IsNullOrEmpty(it.Id) && !map.ContainsKey(it.Id))
                    map[it.Id] = it;
            }

            IEnumerable<DriverDisciplineIndexItem> q = map.Values;
            if (!string.IsNullOrWhiteSpace(driverFilter))
            {
                string slug = DriverSlug(driverFilter);
                q = q.Where(i =>
                    string.Equals(i.DriverSlug, slug, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(i.DriverName, driverFilter, StringComparison.OrdinalIgnoreCase));
            }
            return q.OrderByDescending(i => i.CreatedAt ?? "").ToList();
        }
    }

    internal sealed class DriverDisciplineSaveResult
    {
        public bool LocalOk { get; set; }
        public bool ServerOk { get; set; }
        public string LocalFolder { get; set; }
        public string ServerPath { get; set; }
        public string Error { get; set; }
        public DriverDisciplineMeta Meta { get; set; }
    }

    internal sealed class DriverDisciplineIndexItem
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("case_number")]
        public string CaseNumber { get; set; }

        [JsonProperty("driver_name")]
        public string DriverName { get; set; }

        [JsonProperty("driver_slug")]
        public string DriverSlug { get; set; }

        [JsonProperty("employee_id")]
        public string EmployeeId { get; set; }

        [JsonProperty("incident_date")]
        public string IncidentDate { get; set; }

        [JsonProperty("action_level")]
        public string ActionLevel { get; set; }

        [JsonProperty("violations")]
        public List<string> Violations { get; set; }

        [JsonProperty("created_at")]
        public string CreatedAt { get; set; }

        [JsonProperty("prepared_by")]
        public string PreparedBy { get; set; }
    }

    internal sealed class DriverDisciplineMeta
    {
        [JsonProperty("version")]
        public int Version { get; set; } = 1;

        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("case_number")]
        public string CaseNumber { get; set; }

        [JsonProperty("driver_name")]
        public string DriverName { get; set; }

        [JsonProperty("driver_slug")]
        public string DriverSlug { get; set; }

        [JsonProperty("employee_id")]
        public string EmployeeId { get; set; }

        [JsonProperty("wellryde_sec_id")]
        public string WellRydeSecId { get; set; }

        [JsonProperty("vehicle")]
        public string Vehicle { get; set; }

        [JsonProperty("supervisor_name")]
        public string SupervisorName { get; set; }

        [JsonProperty("department")]
        public string Department { get; set; }

        [JsonProperty("notice_date")]
        public string NoticeDate { get; set; }

        [JsonProperty("incident_date")]
        public string IncidentDate { get; set; }

        [JsonProperty("incident_time")]
        public string IncidentTime { get; set; }

        [JsonProperty("trip_or_client_ref")]
        public string TripOrClientRef { get; set; }

        [JsonProperty("location")]
        public string Location { get; set; }

        [JsonProperty("violations")]
        public List<string> Violations { get; set; }

        [JsonProperty("action_level")]
        public string ActionLevel { get; set; }

        [JsonProperty("footage_summary")]
        public string FootageSummary { get; set; }

        [JsonProperty("narrative")]
        public string Narrative { get; set; }

        [JsonProperty("policy_cited")]
        public string PolicyCited { get; set; }

        [JsonProperty("prior_history")]
        public string PriorHistory { get; set; }

        [JsonProperty("corrective_action")]
        public string CorrectiveAction { get; set; }

        [JsonProperty("follow_up_date")]
        public string FollowUpDate { get; set; }

        [JsonProperty("driver_statement")]
        public string DriverStatement { get; set; }

        [JsonProperty("footage_folder")]
        public string FootageFolder { get; set; }

        [JsonProperty("clip_paths")]
        public List<string> ClipPaths { get; set; }

        [JsonProperty("prepared_by")]
        public string PreparedBy { get; set; }

        [JsonProperty("docx_filename")]
        public string DocxFilename { get; set; }

        [JsonProperty("created_at")]
        public string CreatedAt { get; set; }

        [JsonProperty("updated_at")]
        public string UpdatedAt { get; set; }

        [JsonProperty("updated_by")]
        public string UpdatedBy { get; set; }
    }

    internal sealed class DriverDisciplineListResult
    {
        public bool Ok { get; set; }
        public int Count { get; set; }
        public List<DriverDisciplineIndexItem> Items { get; set; }
        public string Error { get; set; }
    }

    internal sealed class DriverDisciplinePriorsResult
    {
        public bool Ok { get; set; }

        [JsonProperty("driver_name")]
        public string DriverName { get; set; }

        public int Count { get; set; }
        public List<DriverDisciplineIndexItem> Items { get; set; }
        public string Summary { get; set; }

        [JsonProperty("prior_history_text")]
        public string PriorHistoryText { get; set; }

        public string Error { get; set; }
    }

    internal sealed class DriverDisciplineServerSaveResult
    {
        public bool Ok { get; set; }
        public string Id { get; set; }
        public string Path { get; set; }
        public string Error { get; set; }
    }
}
