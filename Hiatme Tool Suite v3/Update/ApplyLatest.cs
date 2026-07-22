using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace Update
{
    /// <summary>
    /// Standalone repair path for desks whose in-app updater is broken.
    /// Does not require a working on-disk Update.exe handoff from the main app —
    /// users can download this same binary as HiatmeApplyUpdate.exe from the website
    /// and run it directly.
    /// </summary>
    internal static class ApplyLatest
    {
        public const string DefaultManifestUrl = "https://hiatme.com/downloads/hiatme-tool-suite/latest.php";
        private const string MainExeName = "Hiatme Tool Suite v3.exe";

        public static void RunInteractive()
        {
            EnsureTls();
            string installDir = FindInstallDirectory();
            if (string.IsNullOrEmpty(installDir))
            {
                MessageBox.Show(
                    "Could not find a Hiatme Tool Suite install folder.\n\n" +
                    "Browse to the folder that contains \"" + MainExeName + "\".",
                    "Hiatme Apply Update", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                using (var dlg = new FolderBrowserDialog
                {
                    Description = "Select the Hiatme Tool Suite install folder",
                    ShowNewFolderButton = false,
                })
                {
                    if (dlg.ShowDialog() != DialogResult.OK)
                        return;
                    installDir = dlg.SelectedPath;
                }
            }

            if (!File.Exists(Path.Combine(installDir, MainExeName)))
            {
                MessageBox.Show(
                    "That folder does not contain \"" + MainExeName + "\".\n\n" + installDir,
                    "Hiatme Apply Update", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var confirm = MessageBox.Show(
                "Install the latest Hiatme Tool Suite into:\n\n" + installDir + "\n\n" +
                "The app will close if it is open, then reopen after install.\n" +
                "Templates and saved logins are kept.",
                "Hiatme Apply Update", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (confirm != DialogResult.OK)
                return;

            try
            {
                ApplyTo(installDir);
            }
            catch (Exception ex)
            {
                Program.Log("ApplyLatest failed: " + ex);
                MessageBox.Show(
                    "Update failed.\n\n" + ex.Message + "\n\nLog: " + Program.LogPath,
                    "Hiatme Apply Update", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public static void ApplyTo(string installDir)
        {
            EnsureTls();
            Program.Log("ApplyLatest start. installDir=" + installDir);

            var manifest = FetchManifest(DefaultManifestUrl);
            Program.Log("Manifest version=" + manifest.Version + " url=" + manifest.DownloadUrl);

            string zipDir = Path.Combine(Path.GetTempPath(), "HiatmeToolSuiteUpdate");
            Directory.CreateDirectory(zipDir);
            string zipPath = Path.Combine(zipDir, "HiatmeToolSuite-" + manifest.Version + ".zip");

            DownloadFile(manifest.DownloadUrl, zipPath);
            string actual = Sha256Hex(zipPath);
            if (!string.Equals(actual, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(zipPath); } catch { }
                throw new InvalidDataException(
                    "Download failed integrity check.\nExpected: " + manifest.Sha256 + "\nActual: " + actual);
            }

            string mainExe = Path.Combine(installDir, MainExeName);
            CloseRunningApp(mainExe, TimeSpan.FromSeconds(45));
            if (!WaitForFileUnlocked(mainExe, TimeSpan.FromSeconds(45)))
                throw new IOException("App files are still locked. Close Hiatme Tool Suite and try again.");

            string staging = Path.Combine(zipDir, "staged_" + DateTime.UtcNow.Ticks);
            Directory.CreateDirectory(staging);
            ZipFile.ExtractToDirectory(zipPath, staging);
            CopyDirectoryOverwrite(staging, installDir);
            try { Directory.Delete(staging, recursive: true); } catch { }
            try { File.Delete(zipPath); } catch { }

            Program.Log("ApplyLatest installed " + manifest.Version + " to " + installDir);

            if (File.Exists(mainExe))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = mainExe,
                    WorkingDirectory = installDir,
                    UseShellExecute = true,
                });
            }

            MessageBox.Show(
                "Installed v" + manifest.Version + ".\n\nHiatme Tool Suite should open now.",
                "Hiatme Apply Update", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static string FindInstallDirectory()
        {
            // 1) Running main app
            try
            {
                foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(MainExeName)))
                {
                    try
                    {
                        string path = p.MainModule?.FileName;
                        if (!string.IsNullOrEmpty(path) && File.Exists(path))
                            return Path.GetDirectoryName(path);
                    }
                    catch { }
                    finally { try { p.Dispose(); } catch { } }
                }
            }
            catch { }

            // 2) Same folder as this ApplyUpdate / Update.exe (if dropped into install dir)
            try
            {
                string selfDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                if (!string.IsNullOrEmpty(selfDir) && File.Exists(Path.Combine(selfDir, MainExeName)))
                    return selfDir;
            }
            catch { }

            // 3) Common install locations + shallow desktop/documents search
            string[] roots =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hiatme"),
                @"C:\Hiatme",
                @"C:\Hiatme Tool Suite",
            };
            foreach (string root in roots)
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
                string direct = Path.Combine(root, MainExeName);
                if (File.Exists(direct)) return root;
                try
                {
                    foreach (string dir in Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly))
                    {
                        if (File.Exists(Path.Combine(dir, MainExeName)))
                            return dir;
                    }
                }
                catch { }
            }

            return null;
        }

        private static void CloseRunningApp(string mainExe, TimeSpan timeout)
        {
            if (string.IsNullOrEmpty(mainExe)) return;
            string full;
            try { full = Path.GetFullPath(mainExe); }
            catch { full = mainExe; }

            string name = Path.GetFileNameWithoutExtension(full);
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < timeout)
            {
                bool any = false;
                foreach (var p in Process.GetProcessesByName(name))
                {
                    try
                    {
                        string path = p.MainModule?.FileName;
                        if (!string.IsNullOrEmpty(path)
                            && string.Equals(Path.GetFullPath(path), full, StringComparison.OrdinalIgnoreCase))
                        {
                            any = true;
                            try { p.CloseMainWindow(); } catch { }
                            if (!p.WaitForExit(2000))
                            {
                                try { p.Kill(); } catch { }
                                try { p.WaitForExit(3000); } catch { }
                            }
                        }
                    }
                    catch { }
                    finally { try { p.Dispose(); } catch { } }
                }
                if (!any) return;
                Thread.Sleep(250);
            }
        }

        private static bool WaitForFileUnlocked(string path, TimeSpan timeout)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return true;
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < timeout)
            {
                try
                {
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                        return true;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                Thread.Sleep(300);
            }
            return false;
        }

        private sealed class Manifest
        {
            public string Version;
            public string DownloadUrl;
            public string Sha256;
        }

        private static Manifest FetchManifest(string url)
        {
            string json;
            using (var wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.CacheControl] = "no-cache";
                json = wc.DownloadString(url + (url.Contains("?") ? "&" : "?") + "t=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            }

            string version = JsonString(json, "version");
            string downloadUrl = JsonString(json, "downloadUrl");
            string sha = JsonString(json, "sha256");
            if (string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(downloadUrl) || string.IsNullOrWhiteSpace(sha))
                throw new InvalidDataException("Update manifest missing version/downloadUrl/sha256.");
            return new Manifest { Version = version.Trim(), DownloadUrl = downloadUrl.Trim(), Sha256 = sha.Trim() };
        }

        private static string JsonString(string json, string key)
        {
            var m = Regex.Match(json ?? "", "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"([^\"]*)\"");
            return m.Success ? Uri.UnescapeDataString(m.Groups[1].Value.Replace("\\/", "/")) : null;
        }

        private static void DownloadFile(string url, string dest)
        {
            if (File.Exists(dest))
            {
                try { File.Delete(dest); } catch { }
            }
            using (var wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.CacheControl] = "no-cache";
                wc.DownloadFile(url, dest);
            }
        }

        private static string Sha256Hex(string path)
        {
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(path))
            {
                byte[] hash = sha.ComputeHash(fs);
                var sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                    sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }

        private static readonly System.Collections.Generic.HashSet<string> Preserved =
            new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday", "Template Temps",
            };

        private static void CopyDirectoryOverwrite(string srcDir, string dstDir)
        {
            Directory.CreateDirectory(dstDir);
            foreach (string srcFile in Directory.GetFiles(srcDir))
            {
                string destFile = Path.Combine(dstDir, Path.GetFileName(srcFile));
                CopyFileWithRetry(srcFile, destFile);
            }
            foreach (string srcSub in Directory.GetDirectories(srcDir))
            {
                string name = Path.GetFileName(srcSub);
                if (Preserved.Contains(name)) continue;
                CopyDirectoryOverwrite(srcSub, Path.Combine(dstDir, name));
            }
        }

        private static void CopyFileWithRetry(string src, string dst)
        {
            for (int i = 1; i <= 10; i++)
            {
                try
                {
                    File.Copy(src, dst, overwrite: true);
                    return;
                }
                catch (IOException) when (i < 10) { Thread.Sleep(200 * i); }
                catch (UnauthorizedAccessException) when (i < 10) { Thread.Sleep(200 * i); }
            }
            File.Copy(src, dst, overwrite: true);
        }

        private static void EnsureTls()
        {
            try
            {
                ServicePointManager.SecurityProtocol |=
                    SecurityProtocolType.Tls12 | (SecurityProtocolType)3072;
            }
            catch { }
        }
    }
}
