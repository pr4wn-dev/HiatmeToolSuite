using System;
using System.Globalization;
using System.IO;
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
        public string ServiceDateIso;
        public string Error;
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
            double? serverMtime = meta?.Mtime;

            // Server copy is newer — pull it down to Desktop + cache so every desk converges.
            if (desktopExists && serverExists
                && ServerIsNewer(serverMtime, desktopMtime))
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
                if (serverExists && LocalIsNewer(desktopMtime, serverMtime))
                {
                    HiatmeAiClient.UploadScheduleWorkbookFireAndForget(
                        settings, iso, desktopPath, "resolver_local_newer");
                }
                else if (!serverExists)
                {
                    HiatmeAiClient.UploadScheduleWorkbookFireAndForget(
                        settings, iso, desktopPath, "resolver_local_only");
                }

                return new ScheduleWorkbookResolveResult
                {
                    FullPath = desktopPath,
                    FileName = fileName,
                    Source = "desktop",
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
                    ServiceDateIso = iso,
                    Error = download?.Error ?? "server download failed; using local copy",
                };
            }

            try
            {
                string dir = Path.GetDirectoryName(desktopPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                File.Copy(cachePath, desktopPath, overwrite: true);
                ApplyServerMtimeToFile(desktopPath, download.Mtime ?? meta?.Mtime);
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
                        File.Copy(cachePath, desktopPath, overwrite: true);
                        ApplyServerMtimeToFile(desktopPath, download.Mtime ?? meta?.Mtime);
                        if (!string.IsNullOrWhiteSpace(download.Etag ?? meta?.Etag))
                            WriteCachedEtag(desktopPath, download.Etag ?? meta.Etag);

                        return new ScheduleWorkbookResolveResult
                        {
                            FullPath = desktopPath,
                            FileName = download.Filename ?? fileName,
                            Source = "desktop_synced",
                            Etag = download.Etag ?? meta?.Etag,
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
