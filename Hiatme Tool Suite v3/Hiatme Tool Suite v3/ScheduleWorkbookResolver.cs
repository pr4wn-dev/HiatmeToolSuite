using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Resolve a day workbook for read: local Desktop first, else AI server cache.
    /// </summary>
    internal sealed class ScheduleWorkbookResolveResult
    {
        public string FullPath;
        public string FileName;
        /// <summary>"desktop" or "server_cache".</summary>
        public string Source;
        public string Etag;
        public string ServiceDateIso;
        public string Error;
    }

    internal static class ScheduleWorkbookResolver
    {
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

            // 1) Local Desktop (server PC / anyone with the folder).
            if (!string.IsNullOrWhiteSpace(desktopPath) && File.Exists(desktopPath))
            {
                return new ScheduleWorkbookResolveResult
                {
                    FullPath = desktopPath,
                    FileName = fileName,
                    Source = "desktop",
                    ServiceDateIso = iso,
                };
            }

            // 2) AI server → local app cache (refresh when etag/mtime/size changes).
            settings = settings ?? HiatmeAiSettings.Load();
            if (settings == null || string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                return new ScheduleWorkbookResolveResult
                {
                    FileName = fileName,
                    ServiceDateIso = iso,
                    Error = "no AI server — " + fileName + " not on Desktop",
                };
            }

            string cachePath = LocalCachePath(serviceDate);
            string cachedEtag = File.Exists(cachePath) ? ReadCachedEtag(cachePath) : null;

            HiatmeScheduleWorkbookMeta meta = null;
            try
            {
                meta = await HiatmeAiClient.GetScheduleWorkbookMetaAsync(
                    settings, iso, cancellationToken).ConfigureAwait(false);
            }
            catch { }

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
                // Server unavailable or missing — keep stale cache if we have one.
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
    }
}
