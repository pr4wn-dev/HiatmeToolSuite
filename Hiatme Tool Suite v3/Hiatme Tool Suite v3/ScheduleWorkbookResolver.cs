using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Resolve a day workbook for read: pick the newest copy (local Desktop vs AI server),
    /// sync down when the server wins, and queue upload when local is newer.
    /// </summary>
    internal sealed class ScheduleWorkbookResolveResult
    {
        public string FullPath;
        public string FileName;
        /// <summary>"desktop", "server_cache", or "desktop_synced".</summary>
        public string Source;
        public string Etag;
        public int Revision;
        public string ServiceDateIso;
        public string Error;
    }

    internal sealed class ScheduleWorkbookPendingPublish
    {
        public string ServiceDateIso;
        public string WorkbookPath;
        public string Source;
    }

    internal static class ScheduleWorkbookResolver
    {
        private const double MtimeSkewSeconds = 0.5;

        public static string LocalCacheRoot()
        {
            string root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HiatmeToolSuite",
                "schedule_cache");
            return root;
        }

        public static string LocalCacheYearFolder(int year)
        {
            string dir = Path.Combine(LocalCacheRoot(), ScheduleExportPaths.YearFolderName(year));
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static string LocalCachePath(DateTime serviceDate)
        {
            ScheduleExportPaths.GetDefaultWorkbookSaveLocation(
                serviceDate, out _, out string fileName, out _);
            return Path.Combine(LocalCacheYearFolder(serviceDate.Year), fileName);
        }

        public static string EtagSidecarPath(string workbookPath) =>
            (workbookPath ?? "") + ".etag";

        public static string ReadCachedEtag(string workbookPath)
        {
            string side = EtagSidecarPath(workbookPath);
            if (!File.Exists(side)) return null;
            try { return (File.ReadAllText(side) ?? "").Trim(); }
            catch { return null; }
        }

        public static void WriteCachedEtag(string workbookPath, string etag)
        {
            if (string.IsNullOrWhiteSpace(workbookPath) || string.IsNullOrWhiteSpace(etag))
                return;
            try { File.WriteAllText(EtagSidecarPath(workbookPath), etag.Trim()); }
            catch { }
        }

        public static string RevisionSidecarPath(string workbookPath) =>
            (workbookPath ?? "") + ".rev";

        /// <summary>
        /// Per-machine record of which published revision this desk's copy is based on.
        ///
        /// This deliberately does not live next to the workbook. The workbook sits in
        /// Desktop\SCHEDULES FOR {year}\, which is OneDrive-synced on every desk, so a sidecar
        /// there is not this desk's state at all — it syncs in from whoever wrote it last,
        /// arrives after the .xlsx it describes, or never arrives while the .xlsx does. A desk
        /// that picked up a workbook through OneDrive rather than through our own pull ends up
        /// reading no revision, sends a base of 0, and the server correctly refuses the save.
        ///
        /// It cannot recover on its own either: the revision is only written after a save
        /// succeeds, so a desk in that state is refused every time it tries, which is exactly
        /// what happened to one desk four times in three minutes against server rev 18.
        /// Keeping the answer on the machine that owns it takes the sync service out of the
        /// loop entirely.
        /// </summary>
        private static string RevisionStorePath(string workbookPath)
        {
            string key = (workbookPath ?? "").Trim().ToLowerInvariant();
            string hash;
            using (var sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(key));
                hash = BitConverter.ToString(h, 0, 10).Replace("-", "").ToLowerInvariant();
            }
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HiatmeToolSuite",
                "schedule_revisions");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, hash + ".rev");
        }

        private static int ReadRevisionFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return 0;
                int rev;
                return int.TryParse((File.ReadAllText(path) ?? "").Trim(), out rev) ? rev : 0;
            }
            catch { return 0; }
        }

        public static int ReadLocalRevision(string workbookPath)
        {
            if (string.IsNullOrWhiteSpace(workbookPath)) return 0;

            string store;
            try { store = RevisionStorePath(workbookPath); }
            catch { return ReadRevisionFile(RevisionSidecarPath(workbookPath)); }

            int rev = ReadRevisionFile(store);
            if (rev > 0) return rev;

            // Desks upgrading into this carry their history in the old synced sidecar. Adopt it
            // once so nobody starts from zero — and therefore locked out of saving — on upgrade.
            int legacy = ReadRevisionFile(RevisionSidecarPath(workbookPath));
            if (legacy > 0)
            {
                try { File.WriteAllText(store, legacy.ToString(CultureInfo.InvariantCulture)); }
                catch { }
            }
            return legacy;
        }

        public static void WriteLocalRevision(string workbookPath, int revision)
        {
            if (string.IsNullOrWhiteSpace(workbookPath) || revision <= 0)
                return;
            string text = revision.ToString(CultureInfo.InvariantCulture);
            try { File.WriteAllText(RevisionStorePath(workbookPath), text); }
            catch { }
            // Still written so rolling a desk back to an older build does not strand it at zero.
            // Older builds read only this copy; newer ones prefer the per-machine store above.
            try { File.WriteAllText(RevisionSidecarPath(workbookPath), text); }
            catch { }
        }

        public static string BackupFolder(string serviceDateIso)
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HiatmeToolSuite",
                "schedule_backups",
                (serviceDateIso ?? "unknown").Trim());
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static string PendingPublishFolder()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HiatmeToolSuite",
                "schedule_pending");
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static string PendingPublishPath(string serviceDateIso) =>
            Path.Combine(PendingPublishFolder(), (serviceDateIso ?? "unknown").Trim() + ".json");

        public static void QueuePendingPublish(string serviceDateIso, string workbookPath, string source)
        {
            if (string.IsNullOrWhiteSpace(serviceDateIso) || string.IsNullOrWhiteSpace(workbookPath))
                return;
            try
            {
                string json = "{\"ServiceDateIso\":\"" + EscapeJson(serviceDateIso.Trim())
                    + "\",\"WorkbookPath\":\"" + EscapeJson(workbookPath)
                    + "\",\"Source\":\"" + EscapeJson(source ?? "schedule_builder_save") + "\"}";
                File.WriteAllText(PendingPublishPath(serviceDateIso), json);
            }
            catch { }
        }

        public static void ClearPendingPublish(string serviceDateIso)
        {
            try
            {
                string path = PendingPublishPath(serviceDateIso);
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch { }
        }

        public static List<ScheduleWorkbookPendingPublish> ListPendingPublishes()
        {
            var list = new List<ScheduleWorkbookPendingPublish>();
            try
            {
                string dir = PendingPublishFolder();
                foreach (string file in Directory.GetFiles(dir, "*.json"))
                {
                    try
                    {
                        string raw = File.ReadAllText(file);
                        var item = new ScheduleWorkbookPendingPublish
                        {
                            ServiceDateIso = JsonStringField(raw, "ServiceDateIso"),
                            WorkbookPath = JsonStringField(raw, "WorkbookPath"),
                            Source = JsonStringField(raw, "Source"),
                        };
                        if (!string.IsNullOrWhiteSpace(item.ServiceDateIso)
                            && !string.IsNullOrWhiteSpace(item.WorkbookPath)
                            && File.Exists(item.WorkbookPath))
                            list.Add(item);
                    }
                    catch { }
                }
            }
            catch { }
            return list;
        }

        private static string EscapeJson(string value)
        {
            if (value == null) return "";
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string JsonStringField(string json, string name)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(name))
                return null;
            string needle = "\"" + name + "\"";
            int i = json.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;
            int colon = json.IndexOf(':', i + needle.Length);
            if (colon < 0) return null;
            int q1 = json.IndexOf('"', colon + 1);
            if (q1 < 0) return null;
            var sb = new System.Text.StringBuilder();
            for (int p = q1 + 1; p < json.Length; p++)
            {
                char c = json[p];
                if (c == '\\' && p + 1 < json.Length)
                {
                    sb.Append(json[p + 1]);
                    p++;
                    continue;
                }
                if (c == '"')
                    break;
                sb.Append(c);
            }
            return sb.ToString();
        }

        public static string BackupLocalWorkbook(string workbookPath, string serviceDateIso)
        {
            if (string.IsNullOrWhiteSpace(workbookPath) || !File.Exists(workbookPath))
                return null;
            try
            {
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                string dest = Path.Combine(
                    BackupFolder(serviceDateIso),
                    stamp + "-" + Path.GetFileName(workbookPath));
                File.Copy(workbookPath, dest, overwrite: false);
                return dest;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Sync resolve via GetResult. Do not call from the WinForms UI thread when the
        /// Desktop workbook is missing — that path hits HTTP and can freeze the app.
        /// Prefer <see cref="ResolveForReadAsync"/> from UI code.
        /// </summary>
        public static ScheduleWorkbookResolveResult ResolveForRead(
            DateTime serviceDate,
            HiatmeAiSettings settings = null)
        {
            try
            {
                return ResolveForReadAsync(serviceDate, settings, CancellationToken.None)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception ex)
            {
                return new ScheduleWorkbookResolveResult
                {
                    ServiceDateIso = serviceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    Error = ex.Message,
                };
            }
        }

        public static async Task<ScheduleWorkbookResolveResult> ResolveForReadAsync(
            DateTime serviceDate,
            HiatmeAiSettings settings = null,
            CancellationToken cancellationToken = default)
        {
            string iso = serviceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            ScheduleExportPaths.GetDefaultWorkbookSaveLocation(
                serviceDate, out _, out string fileName, out string desktopPath);

            bool desktopExists = !string.IsNullOrWhiteSpace(desktopPath) && File.Exists(desktopPath);
            double? desktopMtime = desktopExists ? FileUtcUnixSeconds(desktopPath) : null;

            settings = settings ?? HiatmeAiSettings.Load();
            if (settings != null && !string.IsNullOrWhiteSpace(settings.BaseUrl))
                HiatmeAiClient.RetryPendingWorkbookUploads(settings);

            HiatmeScheduleWorkbookMeta meta = null;
            if (settings != null && !string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                try
                {
                    meta = await HiatmeAiClient.GetScheduleWorkbookMetaAsync(
                        settings, iso, cancellationToken).ConfigureAwait(false);
                }
                catch { }
            }

            bool serverExists = meta != null && meta.Ok && meta.Exists;
            int serverRev = serverExists ? meta.Revision : 0;
            int localRev = desktopExists ? ReadLocalRevision(desktopPath) : 0;

            // A copy that is byte-identical to what is published IS based on that revision,
            // whether or not this desk was ever told so. Recording it here is what stops a desk
            // that received the workbook through OneDrive rather than through our own pull from
            // sitting at base 0 — a state it cannot leave, because the base is only written
            // after a save succeeds and the server refuses every save made from base 0.
            // Safe to adopt precisely because it is gated on the bytes matching.
            if (desktopExists && serverExists && serverRev > localRev
                && !string.IsNullOrWhiteSpace(meta.Sha256)
                && string.Equals(
                    FileSha256Hex(desktopPath),
                    meta.Sha256.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                WriteLocalRevision(desktopPath, serverRev);
                localRev = serverRev;
            }

            // Pull when the published revision is newer, or when the .rev sidecar
            // already matches (OneDrive synced the tiny sidecar) but the xlsx
            // bytes are still the old cached copy — that is Cherie opening
            // Remie's book and seeing yesterday until someone opens it in Excel.
            if (desktopExists && serverExists
                && ShouldPullServerWorkbook(desktopPath, serverRev, localRev, meta.Sha256))
            {
                var synced = await PullServerWorkbookToDesktopAsync(
                    serviceDate, iso, desktopPath, settings, meta, cancellationToken)
                    .ConfigureAwait(false);
                if (synced != null)
                    return synced;
            }

            // Local Desktop exists and is at least as new as the server mirror.
            if (desktopExists)
            {
                // Never publish from LOAD — AutoSave + "local newer" was stomping
                // other desks' explicit SAVE. Only SAVE SCHEDULE / BUILD upload.

                return new ScheduleWorkbookResolveResult
                {
                    FullPath = desktopPath,
                    FileName = fileName,
                    Source = "desktop",
                    Revision = localRev,
                    ServiceDateIso = iso,
                };
            }

            // No local Desktop file — use server cache (download when stale/missing).
            if (settings == null || string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                return new ScheduleWorkbookResolveResult
                {
                    FileName = fileName,
                    ServiceDateIso = iso,
                    Error = "no AI server — " + fileName + " not on Desktop",
                };
            }

            return await ResolveFromServerCacheAsync(
                serviceDate, iso, fileName, settings, meta, cancellationToken)
                .ConfigureAwait(false);
        }

        private static async Task<ScheduleWorkbookResolveResult> PullServerWorkbookToDesktopAsync(
            DateTime serviceDate,
            string iso,
            string desktopPath,
            HiatmeAiSettings settings,
            HiatmeScheduleWorkbookMeta meta,
            CancellationToken cancellationToken)
        {
            string cachePath = LocalCachePath(serviceDate);
            var download = await HiatmeAiClient.DownloadScheduleWorkbookAsync(
                settings, iso, cachePath, cancellationToken).ConfigureAwait(false);
            if (download == null || !download.Ok || !File.Exists(cachePath))
            {
                return new ScheduleWorkbookResolveResult
                {
                    FullPath = desktopPath,
                    FileName = Path.GetFileName(desktopPath),
                    Source = "desktop",
                    Revision = ReadLocalRevision(desktopPath),
                    ServiceDateIso = iso,
                    Error = download?.Error ?? "server download failed; using local copy",
                };
            }

            BackupLocalWorkbook(desktopPath, iso);

            try
            {
                string dir = Path.GetDirectoryName(desktopPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                ReplaceExistingWorkbook(cachePath, desktopPath);
                ApplyServerMtimeToFile(desktopPath, download.Mtime ?? meta?.Mtime);
                int rev = download.Revision > 0 ? download.Revision : (meta != null ? meta.Revision : 0);
                if (rev > 0)
                    WriteLocalRevision(desktopPath, rev);
            }
            catch (Exception ex)
            {
                return new ScheduleWorkbookResolveResult
                {
                    FullPath = cachePath,
                    FileName = download.Filename ?? Path.GetFileName(cachePath),
                    Source = "server_cache",
                    Etag = download.Etag ?? meta?.Etag,
                    ServiceDateIso = iso,
                    Error = "could not update Desktop: " + ex.Message,
                };
            }

            string etag = download.Etag ?? meta?.Etag;
            if (!string.IsNullOrWhiteSpace(etag))
            {
                WriteCachedEtag(cachePath, etag);
                WriteCachedEtag(desktopPath, etag);
            }

            return new ScheduleWorkbookResolveResult
            {
                FullPath = desktopPath,
                FileName = download.Filename ?? Path.GetFileName(desktopPath),
                Source = "desktop_synced",
                Etag = etag,
                Revision = download.Revision > 0 ? download.Revision : (meta != null ? meta.Revision : 0),
                ServiceDateIso = iso,
            };
        }

        private static async Task<ScheduleWorkbookResolveResult> ResolveFromServerCacheAsync(
            DateTime serviceDate,
            string iso,
            string fileName,
            HiatmeAiSettings settings,
            HiatmeScheduleWorkbookMeta meta,
            CancellationToken cancellationToken)
        {
            string cachePath = LocalCachePath(serviceDate);
            string cachedEtag = File.Exists(cachePath) ? ReadCachedEtag(cachePath) : null;

            if (meta == null)
            {
                try
                {
                    meta = await HiatmeAiClient.GetScheduleWorkbookMetaAsync(
                        settings, iso, cancellationToken).ConfigureAwait(false);
                }
                catch { }
            }

            if (meta != null && meta.Ok && meta.Exists
                && !string.IsNullOrWhiteSpace(meta.Etag)
                && File.Exists(cachePath)
                && string.Equals(
                    (cachedEtag ?? "").Trim(),
                    meta.Etag.Trim(),
                    StringComparison.Ordinal))
            {
                return new ScheduleWorkbookResolveResult
                {
                    FullPath = cachePath,
                    FileName = meta.Filename ?? fileName,
                    Source = "server_cache",
                    Etag = meta.Etag,
                    ServiceDateIso = iso,
                };
            }

            if (meta != null && meta.Ok == false && meta.Exists == false
                && File.Exists(cachePath))
            {
                if (meta.Error != null
                    && meta.Error.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // Truly missing on server; don't use stale unless desktop also gone.
                }
                else if (!string.IsNullOrWhiteSpace(meta.Error))
                {
                    return new ScheduleWorkbookResolveResult
                    {
                        FullPath = cachePath,
                        FileName = fileName,
                        Source = "server_cache",
                        Etag = cachedEtag,
                        ServiceDateIso = iso,
                        Error = "server unreachable; using cached " + fileName,
                    };
                }
            }

            var download = await HiatmeAiClient.DownloadScheduleWorkbookAsync(
                settings, iso, cachePath, cancellationToken).ConfigureAwait(false);
            if (download != null && download.Ok && File.Exists(cachePath))
            {
                if (!string.IsNullOrWhiteSpace(download.Etag))
                    WriteCachedEtag(cachePath, download.Etag);
                else if (meta != null && !string.IsNullOrWhiteSpace(meta.Etag))
                    WriteCachedEtag(cachePath, meta.Etag);

                // Seed Desktop so the next resolve is local-fast and matches other desks.
                ScheduleExportPaths.GetDefaultWorkbookSaveLocation(
                    serviceDate, out _, out _, out string desktopPath);
                if (!string.IsNullOrWhiteSpace(desktopPath))
                {
                    try
                    {
                        string dir = Path.GetDirectoryName(desktopPath);
                        if (!string.IsNullOrEmpty(dir))
                            Directory.CreateDirectory(dir);
                        ReplaceExistingWorkbook(cachePath, desktopPath);
                        ApplyServerMtimeToFile(desktopPath, download.Mtime ?? meta?.Mtime);
                        if (!string.IsNullOrWhiteSpace(download.Etag ?? meta?.Etag))
                            WriteCachedEtag(desktopPath, download.Etag ?? meta.Etag);
                        int seedRev = download.Revision > 0
                            ? download.Revision
                            : (meta != null ? meta.Revision : 0);
                        if (seedRev > 0)
                            WriteLocalRevision(desktopPath, seedRev);

                        return new ScheduleWorkbookResolveResult
                        {
                            FullPath = desktopPath,
                            FileName = download.Filename ?? fileName,
                            Source = "desktop_synced",
                            Etag = download.Etag ?? meta?.Etag,
                            Revision = seedRev,
                            ServiceDateIso = iso,
                        };
                    }
                    catch
                    {
                        // fall through to cache-only
                    }
                }

                return new ScheduleWorkbookResolveResult
                {
                    FullPath = cachePath,
                    FileName = download.Filename ?? fileName,
                    Source = "server_cache",
                    Etag = download.Etag ?? meta?.Etag,
                    ServiceDateIso = iso,
                };
            }

            if (File.Exists(cachePath))
            {
                return new ScheduleWorkbookResolveResult
                {
                    FullPath = cachePath,
                    FileName = fileName,
                    Source = "server_cache",
                    Etag = cachedEtag,
                    ServiceDateIso = iso,
                    Error = download?.Error ?? meta?.Error ?? "using stale cache",
                };
            }

            return new ScheduleWorkbookResolveResult
            {
                FileName = fileName,
                ServiceDateIso = iso,
                Error = download?.Error
                    ?? meta?.Error
                    ?? (fileName + " missing on Desktop and server"),
            };
        }

        internal static bool ShouldPullServerWorkbook(
            string desktopPath,
            int serverRev,
            int localRev,
            string serverSha256)
        {
            if (serverRev > localRev)
                return true;
            if (serverRev <= 0 || string.IsNullOrWhiteSpace(serverSha256))
                return false;
            string localSha = FileSha256Hex(desktopPath);
            if (string.IsNullOrWhiteSpace(localSha))
                return false;
            return !string.Equals(
                localSha,
                serverSha256.Trim(),
                StringComparison.OrdinalIgnoreCase);
        }

        internal static string FileSha256Hex(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return null;
            try
            {
                using (var fs = new FileStream(
                    path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sha = SHA256.Create())
                {
                    byte[] hash = sha.ComputeHash(fs);
                    var sb = new System.Text.StringBuilder(hash.Length * 2);
                    for (int i = 0; i < hash.Length; i++)
                        sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                    return sb.ToString();
                }
            }
            catch
            {
                return null;
            }
        }

        private static void ReplaceExistingWorkbook(string sourcePath, string destPath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(destPath))
                throw new ArgumentException("workbook replace paths required");
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException(sourcePath);

            string destDir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destDir))
                Directory.CreateDirectory(destDir);

            if (!File.Exists(destPath))
            {
                File.Copy(sourcePath, destPath, overwrite: false);
                return;
            }

            string tmp = destPath + ".pulling.xlsx";
            string bak = destPath + ".bak.xlsx";
            if (File.Exists(tmp))
                File.Delete(tmp);
            File.Copy(sourcePath, tmp, overwrite: true);
            File.Replace(tmp, destPath, bak, ignoreMetadataErrors: true);
            try
            {
                if (File.Exists(bak))
                    File.Delete(bak);
            }
            catch
            {
                /* leftover bak is harmless */
            }
        }

        private static bool ServerIsNewer(double? serverMtime, double? localMtime)
        {
            if (!serverMtime.HasValue || !localMtime.HasValue)
                return false;
            return serverMtime.Value > localMtime.Value + MtimeSkewSeconds;
        }

        private static bool LocalIsNewer(double? localMtime, double? serverMtime)
        {
            if (!localMtime.HasValue)
                return false;
            if (!serverMtime.HasValue)
                return true;
            return localMtime.Value > serverMtime.Value + MtimeSkewSeconds;
        }

        private static double? FileUtcUnixSeconds(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return null;
            try
            {
                var utc = File.GetLastWriteTimeUtc(path);
                return (utc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            }
            catch
            {
                return null;
            }
        }

        private static void ApplyServerMtimeToFile(string path, double? serverMtime)
        {
            if (string.IsNullOrWhiteSpace(path) || !serverMtime.HasValue)
                return;
            try
            {
                long seconds = (long)Math.Floor(serverMtime.Value);
                var utc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(seconds);
                File.SetLastWriteTimeUtc(path, utc);
            }
            catch { }
        }
    }
}
