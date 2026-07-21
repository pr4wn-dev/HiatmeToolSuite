using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Small JSON-on-disk store for the Dashcam Videos tab, kept under
    /// <c>{exe}\hiatme_config\dashcam_videos.json</c> (same convention as the other tool config).
    /// Holds only the handful of facts that can't be derived from the files themselves: the
    /// library root, per-driver SD-card capacity overrides, folders to ignore, and gaps/days the
    /// user has marked "explained". Writes go through a temp file + replace so a crash can't
    /// corrupt it.
    /// </summary>
    internal static class DashcamVideoStore
    {
        public sealed class Data
        {
            [JsonProperty("root")]
            public string Root { get; set; } = DashcamVideoLibrary.DefaultRoot;

            /// <summary>Driver name → SD chip capacity in GB (256 / 512 / custom).</summary>
            [JsonProperty("capacityGb")]
            public Dictionary<string, int> CapacityGb { get; set; } =
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            /// <summary>Top-level folders to skip (archives, "MIXED", "Criminal Records", etc.).</summary>
            [JsonProperty("ignoredFolders")]
            public List<string> IgnoredFolders { get; set; } = new List<string>();

            /// <summary>Driver name → set of sequence-gap keys the user marked explained.</summary>
            [JsonProperty("acknowledgedGaps")]
            public Dictionary<string, List<string>> AcknowledgedGaps { get; set; } =
                new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            /// <summary>Driver name → set of yyyy-MM-dd lost days the user marked explained.</summary>
            [JsonProperty("acknowledgedLostDays")]
            public Dictionary<string, List<string>> AcknowledgedLostDays { get; set; } =
                new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            [JsonIgnore]
            private HashSet<string> _ignoredSet;

            public bool IsIgnored(string folder)
            {
                if (string.IsNullOrEmpty(folder)) return false;
                if (_ignoredSet == null)
                    _ignoredSet = new HashSet<string>(IgnoredFolders ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
                return _ignoredSet.Contains(folder);
            }

            public void SetIgnored(string folder, bool ignored)
            {
                if (string.IsNullOrEmpty(folder)) return;
                IgnoredFolders = IgnoredFolders ?? new List<string>();
                IgnoredFolders.RemoveAll(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase));
                if (ignored) IgnoredFolders.Add(folder);
                _ignoredSet = null;
            }

            public bool TryGetCapacity(string driver, out int gb)
            {
                gb = 0;
                return CapacityGb != null && driver != null && CapacityGb.TryGetValue(driver, out gb);
            }

            public void SetCapacity(string driver, int gb)
            {
                if (string.IsNullOrEmpty(driver)) return;
                CapacityGb = CapacityGb ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                if (gb <= 0) CapacityGb.Remove(driver);
                else CapacityGb[driver] = gb;
            }

            public bool IsGapAcknowledged(string driver, string gapKey)
            {
                return AcknowledgedGaps != null && driver != null && gapKey != null
                    && AcknowledgedGaps.TryGetValue(driver, out var list) && list != null
                    && list.Contains(gapKey);
            }

            public void AcknowledgeGap(string driver, string gapKey)
            {
                if (string.IsNullOrEmpty(driver) || string.IsNullOrEmpty(gapKey)) return;
                AcknowledgedGaps = AcknowledgedGaps ?? new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                if (!AcknowledgedGaps.TryGetValue(driver, out var list)) { list = new List<string>(); AcknowledgedGaps[driver] = list; }
                if (!list.Contains(gapKey)) list.Add(gapKey);
            }

            public bool IsLostDayAcknowledged(string driver, DateTime day)
            {
                string k = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                return AcknowledgedLostDays != null && driver != null
                    && AcknowledgedLostDays.TryGetValue(driver, out var list) && list != null
                    && list.Contains(k);
            }

            public void AcknowledgeLostDay(string driver, DateTime day)
            {
                if (string.IsNullOrEmpty(driver)) return;
                AcknowledgedLostDays = AcknowledgedLostDays ?? new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                if (!AcknowledgedLostDays.TryGetValue(driver, out var list)) { list = new List<string>(); AcknowledgedLostDays[driver] = list; }
                string k = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (!list.Contains(k)) list.Add(k);
            }
        }

        private const string FileName = "dashcam_videos.json";

        public static string GetPath() =>
            Path.Combine(AppContext.BaseDirectory ?? "", "hiatme_config", FileName);

        private static string GetBackupPath() => GetPath() + ".bak";

        public static Data Load()
        {
            string path = GetPath();
            try
            {
                if (!File.Exists(path)) return new Data();
                string raw = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(raw)) return new Data();
                return JsonConvert.DeserializeObject<Data>(raw) ?? new Data();
            }
            catch
            {
                try
                {
                    string broken = path + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                    if (File.Exists(path)) File.Move(path, broken);
                    if (File.Exists(GetBackupPath())) File.Copy(GetBackupPath(), path, overwrite: true);
                }
                catch { }
                return new Data();
            }
        }

        public static bool Save(Data data)
        {
            try
            {
                string path = GetPath();
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string json = JsonConvert.SerializeObject(data ?? new Data(), Formatting.Indented);
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(path)) File.Replace(tmp, path, GetBackupPath(), ignoreMetadataErrors: true);
                else File.Move(tmp, path);
                return true;
            }
            catch { return false; }
        }
    }
}
