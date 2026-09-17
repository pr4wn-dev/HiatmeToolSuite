using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// JSON-on-disk persistence for the Supey driver roster. Lives next to the .exe at
    /// <c>{AppContext.BaseDirectory}\SupeyDrivers.json</c>. Writes go through a temp file +
    /// <see cref="File.Replace(string, string, string)"/> so a crash mid-save can't leave
    /// half-written JSON; the previous good copy survives as <c>SupeyDrivers.json.bak</c>.
    /// </summary>
    internal static class SupeyDriverRosterStore
    {
        private const string FileName = "SupeyDrivers.json";

        /// <summary>Result of a save attempt — IsoDateTime stamp the UI surfaces in the roster footer.</summary>
        public sealed class SaveResult
        {
            public bool Ok { get; }
            public string ErrorMessage { get; }
            public DateTime SavedAtLocal { get; }

            private SaveResult(bool ok, string err, DateTime savedAt)
            {
                Ok = ok;
                ErrorMessage = err;
                SavedAtLocal = savedAt;
            }

            public static SaveResult Success(DateTime at) => new SaveResult(true, null, at);
            public static SaveResult Fail(string msg) => new SaveResult(false, msg, DateTime.MinValue);
        }

        public static string GetPath() =>
            Path.Combine(AppContext.BaseDirectory ?? "", FileName);

        public static string GetBackupPath() => GetPath() + ".bak";

        /// <summary>
        /// Loads the roster, returning an empty list when the file is missing. On parse failure,
        /// the bad file is shoved aside as <c>.broken-{stamp}</c> and the .bak is restored if it
        /// exists; the caller still gets a non-null list (possibly empty) so the UI can render.
        /// </summary>
        public static List<SupeyDriverProfile> Load(out string warning)
        {
            warning = null;
            string path = GetPath();
            if (!File.Exists(path))
                return new List<SupeyDriverProfile>();

            try
            {
                string raw = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(raw))
                    return new List<SupeyDriverProfile>();

                // Top-level shape is { "drivers": [ ... ] } so we can add fields later without
                // rewriting all existing files.
                var jo = JObject.Parse(raw);
                var arr = jo["drivers"] as JArray;
                if (arr == null)
                    return new List<SupeyDriverProfile>();

                // Fully qualified — there's a project-local 'JsonSerializer' class that would
                // otherwise shadow Newtonsoft's static factory.
                var serializer = Newtonsoft.Json.JsonSerializer.Create(new JsonSerializerSettings
                {
                    MissingMemberHandling = MissingMemberHandling.Ignore,
                });
                var list = new List<SupeyDriverProfile>(arr.Count);
                foreach (var token in arr)
                {
                    if (token == null || token.Type != JTokenType.Object) continue;
                    try
                    {
                        var prof = token.ToObject<SupeyDriverProfile>(serializer);
                        if (prof != null && !string.IsNullOrWhiteSpace(prof.Name))
                            list.Add(prof);
                    }
                    catch
                    {
                        // Per-row corruption shouldn't sink the whole roster — skip the bad entry.
                    }
                }
                return list;
            }
            catch (Exception ex)
            {
                warning = "Could not read driver roster (" + ex.Message + "). The bad file was renamed and the previous backup restored if available.";
                try
                {
                    string broken = path + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                    File.Move(path, broken);
                    string bak = GetBackupPath();
                    if (File.Exists(bak))
                        File.Copy(bak, path, overwrite: true);
                }
                catch
                {
                    // Best-effort recovery — fall through.
                }
                return new List<SupeyDriverProfile>();
            }
        }

        /// <summary>
        /// Fold the shared roster from the panel into what this PC has on disk.
        /// </summary>
        /// <remarks>
        /// Capacity and shift are the panel's to decide — that is the whole point of moving
        /// them off individual machines. Everything else stays local: the name and home come
        /// from WellRyde, and a driver this PC knows about who is not on the shared roster yet
        /// is kept rather than dropped, because "the panel has not heard of them" is not the
        /// same as "they do not exist". A null <paramref name="shared"/> means the panel could
        /// not be reached and the local roster is returned untouched.
        /// </remarks>
        public static List<SupeyDriverProfile> MergeShared(
            IList<SupeyDriverProfile> local, IList<SupeyDriverProfile> shared)
        {
            var merged = new List<SupeyDriverProfile>(local ?? new List<SupeyDriverProfile>());
            if (shared == null || shared.Count == 0)
                return merged;

            foreach (var remote in shared)
            {
                if (remote == null || string.IsNullOrWhiteSpace(remote.Name)) continue;

                var match = merged.Find(d =>
                    d != null && string.Equals(
                        (d.Name ?? "").Trim(), remote.Name.Trim(), StringComparison.OrdinalIgnoreCase));

                if (match == null)
                {
                    merged.Add(remote);
                    continue;
                }
                if (remote.CapacityPassengers > 0)
                    match.CapacityPassengers = remote.CapacityPassengers;
                if (!string.IsNullOrWhiteSpace(remote.ShiftStart))
                    match.ShiftStart = remote.ShiftStart;
                if (!string.IsNullOrWhiteSpace(remote.ShiftEnd))
                    match.ShiftEnd = remote.ShiftEnd;
                if (!string.IsNullOrWhiteSpace(remote.VehicleLabel))
                    match.VehicleLabel = remote.VehicleLabel;
                if (!string.IsNullOrWhiteSpace(remote.ScheduleTabKey))
                    match.ScheduleTabKey = remote.ScheduleTabKey;
            }
            return merged;
        }

        /// <summary>
        /// Atomically writes the roster: serialize to a temp file in the same directory, then
        /// <see cref="File.Replace(string,string,string)"/> over the live file with a .bak so a
        /// crash mid-write can't truncate the saved roster.
        /// </summary>
        public static SaveResult Save(IList<SupeyDriverProfile> drivers)
        {
            try
            {
                string path = GetPath();
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var jo = new JObject
                {
                    ["drivers"] = JArray.FromObject(drivers ?? new List<SupeyDriverProfile>()),
                    ["savedAtUtc"] = DateTime.UtcNow.ToString("o"),
                };
                string json = jo.ToString(Formatting.Indented);

                string tmp = path + ".tmp";
                File.WriteAllText(tmp, json);

                if (File.Exists(path))
                {
                    // File.Replace creates/overwrites the .bak with the previous good content.
                    File.Replace(tmp, path, GetBackupPath(), ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(tmp, path);
                }

                return SaveResult.Success(DateTime.Now);
            }
            catch (Exception ex)
            {
                return SaveResult.Fail("Save failed: " + ex.Message);
            }
        }
    }
}
