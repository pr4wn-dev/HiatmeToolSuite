using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Cherie and Remie edit some days in Excel on Desktop and others in Builder.
    /// Builder never opens the OneDrive path. When Excel actually writes that
    /// Desktop (or cache) workbook, publish those bytes so the other desk sees them.
    /// OneDrive sync without Excel holding the file is ignored.
    /// </summary>
    internal sealed class ScheduleExcelPublishWatch : IDisposable
    {
        private readonly ConcurrentDictionary<string, DateTime> _lockSeen =
            new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, string> _shaWhenLocked =
            new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, System.Threading.Timer> _debounce =
            new ConcurrentDictionary<string, System.Threading.Timer>(StringComparer.OrdinalIgnoreCase);
        private FileSystemWatcher[] _watchers = Array.Empty<FileSystemWatcher>();
        private bool _started;

        public void Start()
        {
            if (_started)
                return;
            _started = true;
            int year = DateTime.Now.Year;
            var list = new System.Collections.Generic.List<FileSystemWatcher>();
            foreach (int y in new[] { year, year + 1 })
                TryWatch(list, ScheduleExportPaths.ResolveDesktopYearFolder(y, createIfMissing: false));
            _watchers = list.ToArray();
        }

        private void TryWatch(System.Collections.Generic.List<FileSystemWatcher> list, string dir)
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                return;
            try
            {
                var w = new FileSystemWatcher(dir, "*.xlsx")
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = true,
                };
                w.Changed += OnFileTouched;
                w.Created += OnFileTouched;
                w.Renamed += (_, e) => OnFileTouched(w, e);
                list.Add(w);
            }
            catch
            {
                /* folder not watchable */
            }
        }

        private void OnFileTouched(object sender, FileSystemEventArgs e)
        {
            if (e == null || string.IsNullOrWhiteSpace(e.FullPath))
                return;
            if (e.FullPath.IndexOf(".tmp", StringComparison.OrdinalIgnoreCase) >= 0
                || e.FullPath.EndsWith(".pulling.xlsx", StringComparison.OrdinalIgnoreCase)
                || e.FullPath.EndsWith(".bak.xlsx", StringComparison.OrdinalIgnoreCase))
                return;

            NoteLockIfHeld(e.FullPath);

            _debounce.AddOrUpdate(
                e.FullPath,
                _ => new System.Threading.Timer(OnDebounce, e.FullPath, 2500, Timeout.Infinite),
                (_, existing) =>
                {
                    existing.Change(2500, Timeout.Infinite);
                    return existing;
                });
        }

        private void NoteLockIfHeld(string path)
        {
            if (FileIsLocked(path))
                _lockSeen[path] = DateTime.UtcNow;
        }

        private void OnDebounce(object state)
        {
            string path = state as string;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return;
            if (!ScheduleExportPaths.TryParseWorkbookServiceDate(path, out DateTime day))
                return;

            NoteLockIfHeld(path);

            string iso = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string sha = ScheduleWorkbookResolver.FileSha256Hex(path);
            if (string.IsNullOrWhiteSpace(sha))
                return;
            bool locked = ScheduleWorkbookResolver.FileIsLocked(path);
            if (locked)
            {
                _shaWhenLocked.TryAdd(path, sha);
                return;
            }
            string cacheSha = ScheduleWorkbookResolver.FileSha256Hex(
                ScheduleWorkbookResolver.LocalCachePath(day));
            string shaWhenLocked;
            _shaWhenLocked.TryGetValue(path, out shaWhenLocked);
            string decision = ScheduleWorkbookResolver.ExcelDesktopPublishDecision(
                sha,
                cacheSha,
                false,
                shaWhenLocked,
                ScheduleWorkbookResolver.IsKnownPublishedSha(iso, sha));
            string drop;
            _shaWhenLocked.TryRemove(path, out drop);
            if (string.Equals(decision, "restore", StringComparison.OrdinalIgnoreCase))
            {
                ScheduleWorkbookResolver.RestoreDesktopIfReverted(day);
                return;
            }
            if (!string.Equals(decision, "publish", StringComparison.OrdinalIgnoreCase))
                return;
            if (!ExcelLikelyWrote(path))
                return;

            var settings = HiatmeAiSettings.Load();
            if (settings == null || string.IsNullOrWhiteSpace(settings.BaseUrl))
                return;

            Task.Run(async () =>
            {
                try
                {
                    var meta = await HiatmeAiClient.GetScheduleWorkbookMetaAsync(settings, iso)
                        .ConfigureAwait(false);
                    if (meta != null && meta.Ok && !string.IsNullOrWhiteSpace(meta.Sha256)
                        && string.Equals(meta.Sha256.Trim(), sha, StringComparison.OrdinalIgnoreCase))
                    {
                        ScheduleWorkbookResolver.NotePublishedSha(iso, sha);
                        return;
                    }

                    int localRev = ScheduleWorkbookResolver.ReadLocalRevision(path);
                    if (localRev <= 0 && meta != null && meta.Revision > 0)
                        ScheduleWorkbookResolver.WriteLocalRevision(path, meta.Revision);

                    var result = await HiatmeAiClient.UploadScheduleWorkbookAsync(
                        settings, iso, path, "excel_save").ConfigureAwait(false);
                    if (result != null && result.Ok)
                    {
                        ScheduleWorkbookResolver.NotePublishedSha(iso, sha);
                        if (result.Revision > 0)
                        {
                            ScheduleWorkbookResolver.WriteLocalRevision(path, result.Revision);
                            string cache = ScheduleWorkbookResolver.LocalCachePath(day);
                            if (!string.Equals(cache, path, StringComparison.OrdinalIgnoreCase)
                                && File.Exists(path))
                            {
                                try
                                {
                                    string dir = Path.GetDirectoryName(cache);
                                    if (!string.IsNullOrEmpty(dir))
                                        Directory.CreateDirectory(dir);
                                    File.Copy(path, cache, overwrite: true);
                                    ScheduleWorkbookResolver.WriteLocalRevision(cache, result.Revision);
                                }
                                catch { }
                            }
                        }
                        HiatmeAiSettings.LogProbe(
                            "excel workbook published " + iso + " rev=" + (result.Revision));
                    }
                }
                catch (Exception ex)
                {
                    HiatmeAiSettings.LogProbe("excel workbook publish fail " + iso + " " + ex.Message);
                }
            });
        }

        private bool ExcelLikelyWrote(string path)
        {
            if (FileIsLocked(path) && ExcelProcessRunning())
                return true;
            if (_lockSeen.TryGetValue(path, out DateTime seen)
                && (DateTime.UtcNow - seen).TotalSeconds < 60
                && ExcelProcessRunning())
                return true;
            return false;
        }

        private static bool IsUnderCache(string path)
        {
            try
            {
                string root = Path.GetFullPath(ScheduleWorkbookResolver.LocalCacheRoot());
                string full = Path.GetFullPath(path);
                return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool ExcelProcessRunning()
        {
            try
            {
                return Process.GetProcessesByName("EXCEL").Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool FileIsLocked(string path)
        {
            try
            {
                using (var fs = new FileStream(
                    path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    return false;
                }
            }
            catch (IOException)
            {
                return File.Exists(path);
            }
            catch (UnauthorizedAccessException)
            {
                return File.Exists(path);
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            foreach (var w in _watchers)
            {
                try { w.EnableRaisingEvents = false; w.Dispose(); }
                catch { }
            }
            _watchers = Array.Empty<FileSystemWatcher>();
            foreach (var t in _debounce.Values)
            {
                try { t.Dispose(); }
                catch { }
            }
            _debounce.Clear();
        }
    }
}
