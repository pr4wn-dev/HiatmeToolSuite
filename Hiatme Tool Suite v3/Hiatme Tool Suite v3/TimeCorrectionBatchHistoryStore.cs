using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace Hiatme_Tool_Suite_v3
{
    internal sealed class TimeCorrectionBatchHistoryEntry
    {
        public string BatchId { get; set; }
        public TimeCorrectionLoadMode LoadMode { get; set; }
        public int FixableTrips { get; set; }
        public int PassedTrips { get; set; }
        public int FailedTrips { get; set; }
        public int TotalTrips { get; set; }
        public string ServiceDateLabel { get; set; }
        public DateTime LastLoadedUtc { get; set; }
        public DateTime? LastExecutedUtc { get; set; }
    }

    /// <summary>
    /// Remembers how each batch was last processed in this tool (load mode and fix counts).
    /// Modivcare does not expose lenient/standard on the batch list, so this is local history.
    /// </summary>
    internal static class TimeCorrectionBatchHistoryStore
    {
        private static readonly object FileLock = new object();
        private static Dictionary<string, TimeCorrectionBatchHistoryEntry> _cache;

        private static string HistoryFilePath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "HiatmeToolSuite",
                "time-correction-batch-history.json");

        public static string FormatListSummary(string batchId)
        {
            TimeCorrectionBatchHistoryEntry entry = Get(batchId);
            if (entry == null)
                return "—";

            string mode = ModeShortLabel(entry.LoadMode);
            if (entry.FixableTrips == 0 && entry.FailedTrips == 0)
                return mode + " · complete";

            if (entry.LastExecutedUtc.HasValue)
                return mode + " · " + entry.FixableTrips + " fix · " + entry.FailedTrips + " fail";

            return mode + " · " + entry.FixableTrips + " to fix";
        }

        public static void RecordLoad(string batchId, TimeCorrectionLoadMode mode,
            int fixable, int passed, int failed, int total, string serviceDateLabel)
        {
            if (string.IsNullOrWhiteSpace(batchId))
                return;

            var entry = Get(batchId) ?? new TimeCorrectionBatchHistoryEntry { BatchId = batchId.Trim() };
            entry.LoadMode = mode;
            entry.FixableTrips = fixable;
            entry.PassedTrips = passed;
            entry.FailedTrips = failed;
            entry.TotalTrips = total;
            entry.ServiceDateLabel = serviceDateLabel ?? "";
            entry.LastLoadedUtc = DateTime.UtcNow;
            Upsert(entry);
        }

        public static void RecordExecute(string batchId, TimeCorrectionLoadMode mode,
            int fixable, int passed, int failed, int total, string serviceDateLabel)
        {
            if (string.IsNullOrWhiteSpace(batchId))
                return;

            var entry = Get(batchId) ?? new TimeCorrectionBatchHistoryEntry { BatchId = batchId.Trim() };
            entry.LoadMode = mode;
            entry.FixableTrips = fixable;
            entry.PassedTrips = passed;
            entry.FailedTrips = failed;
            entry.TotalTrips = total;
            entry.ServiceDateLabel = serviceDateLabel ?? "";
            entry.LastExecutedUtc = DateTime.UtcNow;
            if (entry.LastLoadedUtc == default)
                entry.LastLoadedUtc = entry.LastExecutedUtc.Value;
            Upsert(entry);
        }

        private static string ModeShortLabel(TimeCorrectionLoadMode mode)
        {
            switch (mode)
            {
                case TimeCorrectionLoadMode.Lenient:
                    return "Lenient";
                case TimeCorrectionLoadMode.DataOnly:
                    return "Data only";
                case TimeCorrectionLoadMode.ModivcareRedOnly:
                    return "Portal red";
                default:
                    return "Standard";
            }
        }

        private static TimeCorrectionBatchHistoryEntry Get(string batchId)
        {
            if (string.IsNullOrWhiteSpace(batchId))
                return null;
            EnsureLoaded();
            _cache.TryGetValue(batchId.Trim(), out TimeCorrectionBatchHistoryEntry entry);
            return entry;
        }

        private static void Upsert(TimeCorrectionBatchHistoryEntry entry)
        {
            EnsureLoaded();
            _cache[entry.BatchId.Trim()] = entry;
            Save();
        }

        private static void EnsureLoaded()
        {
            if (_cache != null)
                return;

            lock (FileLock)
            {
                if (_cache != null)
                    return;

                _cache = new Dictionary<string, TimeCorrectionBatchHistoryEntry>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    if (!File.Exists(HistoryFilePath))
                        return;

                    string json = File.ReadAllText(HistoryFilePath);
                    var list = JsonConvert.DeserializeObject<List<TimeCorrectionBatchHistoryEntry>>(json);
                    if (list == null)
                        return;

                    foreach (TimeCorrectionBatchHistoryEntry entry in list)
                    {
                        if (entry == null || string.IsNullOrWhiteSpace(entry.BatchId))
                            continue;
                        _cache[entry.BatchId.Trim()] = entry;
                    }
                }
                catch
                {
                    _cache = new Dictionary<string, TimeCorrectionBatchHistoryEntry>(StringComparer.OrdinalIgnoreCase);
                }
            }
        }

        private static void Save()
        {
            lock (FileLock)
            {
                try
                {
                    string dir = Path.GetDirectoryName(HistoryFilePath);
                    if (!string.IsNullOrEmpty(dir))
                        Directory.CreateDirectory(dir);

                    var list = new List<TimeCorrectionBatchHistoryEntry>(_cache.Values);
                    string json = JsonConvert.SerializeObject(list, Formatting.Indented);
                    File.WriteAllText(HistoryFilePath, json);
                }
                catch
                {
                    // History is optional UI hinting only.
                }
            }
        }
    }
}
