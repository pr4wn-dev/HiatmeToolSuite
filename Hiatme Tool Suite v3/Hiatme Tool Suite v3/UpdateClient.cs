using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Fetches the update manifest from hiatme.com, downloads the verified zip, and hands off to <c>Update.exe</c>
    /// so the main app can exit while file replacement happens. Public — anonymous — endpoint by design;
    /// integrity is guaranteed by the SHA256 in the manifest.
    /// </summary>
    internal static class UpdateClient
    {
        // Single source of truth for the publish layout. Bump together with the PHP endpoint path.
        public const string ManifestUrl = "https://hiatme.com/downloads/hiatme-tool-suite/latest.php";

        private static int _tlsConfigured;

        // Force TLS 1.2/1.3 once per process. .NET Framework 4.8's default protocol selection still
        // surprises us occasionally on older Windows builds — without this, HTTPS to a modern server can
        // fail with a generic HttpRequestException and no useful inner message.
        private static void EnsureModernTls()
        {
            if (Interlocked.Exchange(ref _tlsConfigured, 1) != 0) return;
            try
            {
                var current = ServicePointManager.SecurityProtocol;
                var tls12 = (SecurityProtocolType)0x00000C00; // Tls12
                var tls13 = (SecurityProtocolType)0x00003000; // Tls13
                ServicePointManager.SecurityProtocol = current | tls12;
                try { ServicePointManager.SecurityProtocol |= tls13; } catch { /* not supported on this OS */ }
            }
            catch
            {
                // Never let TLS setup crash the app — worst case we fall back to the runtime default.
            }
        }

        /// <summary>App's currently installed assembly version (e.g. 1.0.0.0). Falls back to "0.0.0.0".</summary>
        public static Version CurrentVersion
        {
            get
            {
                try
                {
                    var v = Assembly.GetExecutingAssembly().GetName().Version;
                    return v ?? new Version(0, 0, 0, 0);
                }
                catch
                {
                    return new Version(0, 0, 0, 0);
                }
            }
        }

        /// <summary>"vMAJOR.MINOR.BUILD.REV" for use in title bars / about dialogs.</summary>
        public static string CurrentVersionDisplay => "v" + CurrentVersion;

        /// <summary>
        /// GET the manifest JSON. Returns the parsed result or throws on network/parse/HTTP failure.
        /// Caller decides whether to compare versions and prompt the user.
        /// </summary>
        public static async Task<UpdateManifest> FetchManifestAsync(CancellationToken cancellationToken = default)
        {
            EnsureModernTls();

            using (var handler = new HttpClientHandler())
            using (var client = new HttpClient(handler))
            {
                client.Timeout = TimeSpan.FromSeconds(15);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("HiatmeToolSuite/" + CurrentVersion + " (Updater)");
                client.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };

                // bust both proxy and PHP-opcache style caches; the endpoint also sends no-cache headers
                string url = ManifestUrl + (ManifestUrl.Contains("?") ? "&" : "?") + "t=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                HttpResponseMessage res;
                try
                {
                    res = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
                }
                catch (HttpRequestException ex)
                {
                    // Network-level failure (DNS, TLS, connection refused). Surface a friendly message rather
                    // than the opaque "An error occurred while sending the request" the framework throws.
                    throw new UpdateCheckException(
                        "Couldn't reach " + ManifestUrl + ".\n\nNetwork / TLS error: " + (ex.InnerException?.Message ?? ex.Message), ex);
                }

                using (res)
                {
                    if (!res.IsSuccessStatusCode)
                    {
                        // 404 is the by-far-most-common case before latest.php is deployed; call it out explicitly.
                        if (res.StatusCode == HttpStatusCode.NotFound)
                        {
                            throw new UpdateCheckException(
                                "Update endpoint not found (HTTP 404).\n\n" +
                                "It looks like latest.php hasn't been uploaded to\n" +
                                "hiatme.com/downloads/hiatme-tool-suite/ yet.\n\n" +
                                "URL: " + ManifestUrl);
                        }
                        throw new UpdateCheckException(
                            "Update server returned HTTP " + (int)res.StatusCode + " " + res.StatusCode + ".\n\nURL: " + ManifestUrl);
                    }

                    string body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                    try
                    {
                        return UpdateManifest.ParseJson(body);
                    }
                    catch (Exception ex)
                    {
                        // Body wasn't valid JSON (e.g. PHP error page, HTML 200 from a misconfigured host). Include
                        // a short snippet so it's diagnosable without server access.
                        string snippet = body == null ? "" : (body.Length > 200 ? body.Substring(0, 200) + "…" : body);
                        throw new UpdateCheckException(
                            "Update endpoint returned an unexpected response.\n\n" + ex.Message +
                            "\n\nFirst bytes:\n" + snippet, ex);
                    }
                }
            }
        }

        /// <summary>
        /// Returns true when <paramref name="manifest"/> advertises a strictly higher version than the running build.
        /// </summary>
        public static bool IsUpdateAvailable(UpdateManifest manifest)
        {
            if (manifest == null || string.IsNullOrEmpty(manifest.Version))
                return false;
            if (!Version.TryParse(manifest.Version, out var remote))
                return false;
            return remote > CurrentVersion;
        }

        /// <summary>
        /// Download the zip to a fresh temp path, verify SHA256, and return the local path. Reports progress 0..1.
        /// Throws on size mismatch, hash mismatch, or any HTTP failure so the caller can show a clean error.
        /// </summary>
        public static async Task<string> DownloadVerifiedAsync(UpdateManifest manifest,
            IProgress<double> progress = null, CancellationToken cancellationToken = default)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            if (string.IsNullOrEmpty(manifest.DownloadUrl)) throw new InvalidOperationException("Manifest has no downloadUrl.");
            if (string.IsNullOrEmpty(manifest.Sha256)) throw new InvalidOperationException("Manifest has no sha256.");

            EnsureModernTls();

            string tempDir = Path.Combine(Path.GetTempPath(), "HiatmeToolSuiteUpdate");
            Directory.CreateDirectory(tempDir);
            string zipPath = Path.Combine(tempDir, "HiatmeToolSuite-" + manifest.Version + ".zip");
            // Clean any half-finished prior attempt so we don't ever accept a stale partial file.
            if (File.Exists(zipPath))
            {
                try { File.Delete(zipPath); } catch { /* will overwrite below */ }
            }

            using (var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.None })
            using (var client = new HttpClient(handler))
            {
                client.Timeout = Timeout.InfiniteTimeSpan; // download timeout governed by the cancellation token / per-read stream
                client.DefaultRequestHeaders.UserAgent.ParseAdd("HiatmeToolSuite/" + CurrentVersion + " (Updater)");

                using (var res = await client.GetAsync(manifest.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                {
                    res.EnsureSuccessStatusCode();
                    long? total = res.Content.Headers.ContentLength ?? (manifest.SizeBytes > 0 ? (long?)manifest.SizeBytes : null);
                    using (var src = await res.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var dst = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                    {
                        var buf = new byte[81920];
                        long copied = 0;
                        int n;
                        while ((n = await src.ReadAsync(buf, 0, buf.Length, cancellationToken).ConfigureAwait(false)) > 0)
                        {
                            await dst.WriteAsync(buf, 0, n, cancellationToken).ConfigureAwait(false);
                            copied += n;
                            if (total.HasValue && total.Value > 0)
                                progress?.Report(Math.Min(1.0, (double)copied / total.Value));
                        }
                    }
                }
            }

            if (manifest.SizeBytes > 0)
            {
                long actual = new FileInfo(zipPath).Length;
                if (actual != manifest.SizeBytes)
                {
                    try { File.Delete(zipPath); } catch { }
                    throw new InvalidDataException(
                        "Download size mismatch — expected " + manifest.SizeBytes + " bytes, got " + actual + ".");
                }
            }

            string actualHash = ComputeSha256Hex(zipPath);
            if (!string.Equals(actualHash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(zipPath); } catch { }
                throw new InvalidDataException(
                    "Downloaded file failed integrity check.\nExpected SHA256: " + manifest.Sha256 + "\nActual:   " + actualHash);
            }

            return zipPath;
        }

        /// <summary>
        /// Launch the bundled <c>Update.exe</c> with arguments to wait for the current process, extract the zip
        /// over the install dir, and restart this exe. Returns true if the updater was launched successfully —
        /// caller should then exit the main app.
        /// </summary>
        public static bool LaunchUpdaterAndExit(string zipPath)
        {
            if (string.IsNullOrEmpty(zipPath) || !File.Exists(zipPath))
                return false;

            string installDir = AppDomain.CurrentDomain.BaseDirectory;
            string mainExe = Assembly.GetExecutingAssembly().Location;
            string updaterExe = Path.Combine(installDir, "Update.exe");
            if (!File.Exists(updaterExe))
            {
                // Older installs shipped before Update.exe was bundled next to the main exe.
                if (!TryBootstrapUpdaterFromZip(zipPath, installDir))
                    return false;
            }

            var pid = System.Diagnostics.Process.GetCurrentProcess().Id;
            string args =
                "--pid " + pid +
                " --zip \"" + zipPath + "\"" +
                " --target \"" + installDir.TrimEnd(Path.DirectorySeparatorChar) + "\"" +
                " --restart \"" + mainExe + "\"";

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = updaterExe,
                    Arguments = args,
                    UseShellExecute = false,
                    WorkingDirectory = installDir,
                });
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Pulls <c>Update.exe</c> (and its .config) out of the verified download so legacy installs can update once.
        /// </summary>
        private static bool TryBootstrapUpdaterFromZip(string zipPath, string installDir)
        {
            if (string.IsNullOrEmpty(zipPath) || !File.Exists(zipPath) || string.IsNullOrEmpty(installDir))
                return false;

            try
            {
                using (ZipArchive archive = ZipFile.OpenRead(zipPath))
                {
                    bool gotExe = false;
                    foreach (string name in new[] { "Update.exe", "Update.exe.config" })
                    {
                        ZipArchiveEntry entry = archive.GetEntry(name);
                        if (entry == null)
                            continue;
                        string dest = Path.Combine(installDir, name);
                        entry.ExtractToFile(dest, overwrite: true);
                        if (name.Equals("Update.exe", StringComparison.OrdinalIgnoreCase))
                            gotExe = true;
                    }
                    return gotExe && File.Exists(Path.Combine(installDir, "Update.exe"));
                }
            }
            catch
            {
                return false;
            }
        }

        private static string ComputeSha256Hex(string path)
        {
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(path))
            {
                byte[] hash = sha.ComputeHash(fs);
                var sb = new System.Text.StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                    sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }
    }

    /// <summary>
    /// Thrown by <see cref="UpdateClient.FetchManifestAsync"/> when the update server is unreachable, returns a
    /// non-2xx response, or returns content that isn't a valid manifest. Carries a message safe to show in a dialog.
    /// </summary>
    internal sealed class UpdateCheckException : Exception
    {
        public UpdateCheckException(string message) : base(message) { }
        public UpdateCheckException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>Minimal JSON shape returned by <c>latest.php</c>.</summary>
    internal sealed class UpdateManifest
    {
        public string Version { get; set; }
        public string DownloadUrl { get; set; }
        public string Sha256 { get; set; }
        public long SizeBytes { get; set; }
        public string PublishedAt { get; set; }
        public string ReleaseNotes { get; set; }

        /// <summary>
        /// Hand-rolled JSON reader so we don't take a new dependency on Newtonsoft just for this. Tolerates field
        /// reordering and missing optional fields; rejects manifests without <c>version</c>, <c>downloadUrl</c>, or <c>sha256</c>.
        /// </summary>
        public static UpdateManifest ParseJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new InvalidDataException("Empty update manifest.");

            var m = new UpdateManifest
            {
                Version = ReadString(json, "version"),
                DownloadUrl = ReadString(json, "downloadUrl"),
                Sha256 = ReadString(json, "sha256"),
                PublishedAt = ReadString(json, "publishedAt"),
                ReleaseNotes = ReadString(json, "releaseNotes"),
            };
            string sizeStr = ReadRaw(json, "sizeBytes");
            if (!string.IsNullOrEmpty(sizeStr) && long.TryParse(sizeStr, out long sz))
                m.SizeBytes = sz;

            if (string.IsNullOrEmpty(m.Version) || string.IsNullOrEmpty(m.DownloadUrl) || string.IsNullOrEmpty(m.Sha256))
                throw new InvalidDataException("Update manifest is missing required fields (version/downloadUrl/sha256).");

            return m;
        }

        // Match "key": "value" with backslash-escapes; tolerant of whitespace and field order.
        private static string ReadString(string json, string key)
        {
            var rx = new Regex("\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"",
                RegexOptions.Singleline | RegexOptions.CultureInvariant);
            var m = rx.Match(json);
            if (!m.Success) return null;
            return UnescapeJsonString(m.Groups[1].Value);
        }

        private static string ReadRaw(string json, string key)
        {
            var rx = new Regex("\"" + Regex.Escape(key) + "\"\\s*:\\s*([^,}\\]\\s]+)",
                RegexOptions.Singleline | RegexOptions.CultureInvariant);
            var m = rx.Match(json);
            return m.Success ? m.Groups[1].Value : null;
        }

        private static string UnescapeJsonString(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != '\\') { sb.Append(c); continue; }
                if (i + 1 >= s.Length) { sb.Append(c); continue; }
                char n = s[++i];
                switch (n)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 < s.Length)
                        {
                            string hex = s.Substring(i + 1, 4);
                            if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber,
                                System.Globalization.CultureInfo.InvariantCulture, out int code))
                            {
                                sb.Append((char)code);
                                i += 4;
                                break;
                            }
                        }
                        sb.Append(n);
                        break;
                    default: sb.Append(n); break;
                }
            }
            return sb.ToString();
        }
    }
}
