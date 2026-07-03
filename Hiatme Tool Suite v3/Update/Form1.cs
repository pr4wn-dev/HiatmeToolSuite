using MaterialSkin;
using MaterialSkin.Controls;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Update
{
    /// <summary>
    /// The actual updater UI. Behavior depends on the constructor used:
    ///   * <see cref="Form1()"/> — informational placeholder for the legacy double-click case.
    ///   * <see cref="Form1(UpdateArgs)"/> — drives the wait → extract → restart pipeline.
    ///
    /// User data preservation: we ONLY overwrite files contained in the zip, and we never copy
    /// weekday template folders or Template Temps from the zip even if a bad package includes them.
    /// Saved login creds live in a versioned user.config under %LOCALAPPDATA%; the main app migrates
    /// those on first launch after a version bump (see UserSettingsMigration).
    /// </summary>
    public partial class Form1 : MaterialForm
    {
        readonly MaterialSkinManager materialSkinManager;
        private readonly UpdateArgs _opts;

        public Form1()
        {
            InitializeComponent();
            TryApplyTheme(out materialSkinManager);
        }

        public Form1(UpdateArgs opts)
        {
            InitializeComponent();
            TryApplyTheme(out materialSkinManager);
            _opts = opts ?? throw new ArgumentNullException(nameof(opts));

            SetupWorkerUi();
            Shown += async (_, __) => await RunUpdatePipelineAsync();
        }

        /// <summary>
        /// Theming is *cosmetic*. If MaterialSkin.dll didn't make it into the relocated temp folder (or is
        /// version-skewed against our reference), we must still complete the update pipeline. So we swallow
        /// any TypeInitialization / FileNotFound failure and proceed with the default WinForms look.
        /// </summary>
        private void TryApplyTheme(out MaterialSkinManager mgr)
        {
            mgr = null;
            try
            {
                mgr = MaterialSkinManager.Instance;
                mgr.EnforceBackcolorOnAllComponents = false;
                mgr.AddFormToManage(this);
                mgr.Theme = MaterialSkinManager.Themes.DARK;
                mgr.ColorScheme = new ColorScheme(Primary.Grey900, Primary.Grey800, Primary.BlueGrey500, Accent.Lime700, TextShade.WHITE);
            }
            catch (Exception ex)
            {
                Program.Log("Theme init failed (continuing without MaterialSkin theming): " + ex.Message);
            }
        }

        private void SetupWorkerUi()
        {
            Text = "Hiatme Tool Suite — installing update";
            _startupHintLabel.TextAlign = System.Drawing.ContentAlignment.TopLeft;
            _startupHintLabel.Padding = new Padding(28, 28, 28, 28);
            _startupHintLabel.Text =
                "Installing update — please don't close this window.\r\n\r\n" +
                "Your saved login and templates will be kept.\r\n\r\n" +
                "When installation finishes, use the launch button to reopen the app.";
            // Replace the static hint with a simple status line at the bottom.
            _statusLabel = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 80,
                Padding = new Padding(28, 8, 28, 16),
                Font = new System.Drawing.Font("Segoe UI", 9F),
                ForeColor = System.Drawing.Color.Gainsboro,
                Text = "Preparing…",
            };
            Controls.Add(_statusLabel);
            _statusLabel.BringToFront();

            _launchButton = new MaterialButton
            {
                Text = "LAUNCH HIATME TOOL SUITE",
                Type = MaterialButton.MaterialButtonType.Contained,
                UseAccentColor = true,
                AutoSize = false,
                Size = new System.Drawing.Size(320, 42),
                Anchor = AnchorStyles.Bottom,
                Visible = false,
            };
            _launchButton.Click += (_, __) => OnLaunchMainAppClicked();
            Controls.Add(_launchButton);
            _launchButton.BringToFront();
            PositionLaunchButton();
            Resize += (_, __) => PositionLaunchButton();
        }

        private void PositionLaunchButton()
        {
            if (_launchButton == null)
                return;
            int x = Math.Max(28, (ClientSize.Width - _launchButton.Width) / 2);
            int y = ClientSize.Height - _launchButton.Height - 100;
            _launchButton.Location = new System.Drawing.Point(x, y);
        }

        private void OnLaunchMainAppClicked()
        {
            _launchButton.Enabled = false;
            if (TryRelaunchMainApp(showFailureDialog: true))
                BeginInvoke((MethodInvoker)Close);
            else
                _launchButton.Enabled = true;
        }

        private Label _statusLabel;
        private MaterialButton _launchButton;

        private void Status(string text)
        {
            if (_statusLabel == null || _statusLabel.IsDisposed) return;
            if (_statusLabel.InvokeRequired)
                _statusLabel.BeginInvoke((MethodInvoker)(() => _statusLabel.Text = text));
            else
                _statusLabel.Text = text;
        }

        private async Task RunUpdatePipelineAsync()
        {
            try
            {
                Program.Log("Pipeline start. zip=" + _opts.ZipPath +
                    " target=" + _opts.TargetDir +
                    " restart=" + _opts.RestartExe +
                    " pid=" + (_opts.WaitForPid?.ToString() ?? "(none)"));

                if (_opts.WaitForPid.HasValue)
                {
                    Status("Waiting for Hiatme Tool Suite to exit...");
                    bool exited = await Task.Run(() => WaitForMainAppExit(_opts.WaitForPid.Value));
                    Program.Log("WaitForMainAppExit(" + _opts.WaitForPid.Value + ") returned " + exited);
                    if (!exited)
                    {
                        Fail("Hiatme Tool Suite did not exit in time.\n\nClose the app manually, then run the update again.");
                        return;
                    }
                }

                // Any OTHER instances (e.g. a second window the user opened) still lock the exe/DLLs.
                // Wait for every process running from the install folder to exit before we touch files.
                if (!string.IsNullOrEmpty(_opts.RestartExe) && File.Exists(_opts.RestartExe))
                {
                    Status("Waiting for all app windows to close...");
                    bool allClosed = await Task.Run(() =>
                        WaitForAllInstancesExit(_opts.RestartExe, TimeSpan.FromSeconds(60)));
                    Program.Log("WaitForAllInstancesExit returned " + allClosed);
                    if (!allClosed)
                    {
                        Fail("Another Hiatme Tool Suite window is still open.\n\n" +
                             "Close every Hiatme Tool Suite window, then run the update again.");
                        return;
                    }

                    Status("Waiting for app files to unlock...");
                    bool unlocked = await Task.Run(() =>
                        WaitForFileUnlocked(_opts.RestartExe, TimeSpan.FromSeconds(45)));
                    Program.Log("WaitForFileUnlocked(" + _opts.RestartExe + ") returned " + unlocked);
                    if (!unlocked)
                    {
                        Fail("App files are still in use.\n\nClose Hiatme Tool Suite completely and try again.");
                        return;
                    }
                }

                if (string.IsNullOrEmpty(_opts.ZipPath) || !File.Exists(_opts.ZipPath))
                {
                    Fail("Update archive not found:\n" + _opts.ZipPath);
                    return;
                }
                if (string.IsNullOrEmpty(_opts.TargetDir) || !Directory.Exists(_opts.TargetDir))
                {
                    Fail("Install directory not found:\n" + _opts.TargetDir);
                    return;
                }

                Status("Extracting...");
                string staging = Path.Combine(Path.GetTempPath(), "HiatmeToolSuiteUpdate", "staged_" + DateTime.UtcNow.Ticks);
                Directory.CreateDirectory(staging);
                await Task.Run(() => ZipFile.ExtractToDirectory(_opts.ZipPath, staging));
                int fileCount = Directory.GetFiles(staging, "*", SearchOption.AllDirectories).Length;
                Program.Log("Extracted " + fileCount + " files to " + staging);

                Status("Installing files...");
                await Task.Run(() => CopyDirectoryOverwrite(staging, _opts.TargetDir));
                Program.Log("Copied staged files to " + _opts.TargetDir);

                try { Directory.Delete(staging, recursive: true); } catch (Exception ex) { Program.Log("Could not delete staging: " + ex.Message); }
                try { File.Delete(_opts.ZipPath); } catch (Exception ex) { Program.Log("Could not delete downloaded zip: " + ex.Message); }

                if (!string.IsNullOrEmpty(_opts.RestartExe) && File.Exists(_opts.RestartExe))
                {
                    Status("Update installed. Launch when you're ready.");
                    void ShowLaunch()
                    {
                        _launchButton.Visible = true;
                        _launchButton.Enabled = true;
                        _launchButton.BringToFront();
                        PositionLaunchButton();
                    }
                    if (_launchButton.InvokeRequired)
                        _launchButton.BeginInvoke((MethodInvoker)ShowLaunch);
                    else
                        ShowLaunch();
                    return;
                }
                else
                {
                    Program.Log("No restart requested or restart exe missing: " + _opts.RestartExe);
                }

                await Task.Delay(750);
                BeginInvoke((MethodInvoker)Close);
            }
            catch (Exception ex)
            {
                Program.Log("Pipeline EXCEPTION: " + ex);
                Fail("Update failed.\n\n" + ex.Message);
            }
        }

        private void Fail(string message)
        {
            Status("Failed.");
            MessageBox.Show(message, "Hiatme Updater", MessageBoxButtons.OK, MessageBoxIcon.Error);
            BeginInvoke((MethodInvoker)Close);
        }

        /// <summary>
        /// Waits for the main app pid to exit. Uses a graceful wait, then CloseMainWindow, then Kill as last resort.
        /// </summary>
        private static bool WaitForMainAppExit(int pid)
        {
            Process proc = null;
            try
            {
                proc = Process.GetProcessById(pid);
            }
            catch (ArgumentException)
            {
                return true;
            }
            catch (InvalidOperationException)
            {
                return true;
            }

            using (proc)
            {
                if (proc.WaitForExit(20000))
                {
                    Thread.Sleep(1500);
                    return true;
                }

                try
                {
                    if (proc.MainWindowHandle != IntPtr.Zero)
                        proc.CloseMainWindow();
                }
                catch { }

                if (proc.WaitForExit(8000))
                {
                    Thread.Sleep(1500);
                    return true;
                }

                try
                {
                    proc.Kill();
                    proc.WaitForExit(8000);
                    Program.Log("Killed pid " + pid);
                }
                catch (Exception ex)
                {
                    Program.Log("Could not kill pid " + pid + ": " + ex.Message);
                    return false;
                }

                Thread.Sleep(2000);
                return !IsProcessRunning(pid);
            }
        }

        private static bool IsProcessRunning(int pid)
        {
            try
            {
                using (var p = Process.GetProcessById(pid))
                    return !p.HasExited;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Blocks until no process is running from <paramref name="exePath"/> (any instance the user opened),
        /// escalating to CloseMainWindow then Kill as the timeout nears. Returns true only when all are gone.
        /// </summary>
        private static bool WaitForAllInstancesExit(string exePath, TimeSpan timeout)
        {
            if (string.IsNullOrEmpty(exePath))
                return true;

            string processName = Path.GetFileNameWithoutExtension(exePath);
            string fullPath;
            try { fullPath = Path.GetFullPath(exePath); }
            catch { fullPath = exePath; }

            var sw = Stopwatch.StartNew();
            bool triedClose = false;
            bool triedKill = false;

            while (sw.Elapsed < timeout)
            {
                var matches = GetProcessesAtPath(processName, fullPath);
                if (matches.Count == 0)
                {
                    Thread.Sleep(1000); // let the OS release file handles
                    return true;
                }

                // Halfway to the deadline: politely ask windows to close.
                if (!triedClose && sw.Elapsed > TimeSpan.FromTicks(timeout.Ticks / 2))
                {
                    triedClose = true;
                    foreach (var p in matches)
                    {
                        try { if (p.MainWindowHandle != IntPtr.Zero) p.CloseMainWindow(); }
                        catch { }
                    }
                }

                // Near the deadline: force any stragglers.
                if (!triedKill && sw.Elapsed > TimeSpan.FromTicks((long)(timeout.Ticks * 0.8)))
                {
                    triedKill = true;
                    foreach (var p in matches)
                    {
                        try { p.Kill(); Program.Log("Killed stray instance pid " + p.Id); }
                        catch (Exception ex) { Program.Log("Could not kill pid " + p.Id + ": " + ex.Message); }
                    }
                }

                foreach (var p in matches)
                {
                    try { p.Dispose(); } catch { }
                }
                Thread.Sleep(400);
            }

            return GetProcessesAtPath(processName, fullPath).Count == 0;
        }

        private static List<Process> GetProcessesAtPath(string processName, string fullPath)
        {
            var result = new List<Process>();
            Process[] byName;
            try { byName = Process.GetProcessesByName(processName); }
            catch { return result; }

            foreach (var p in byName)
            {
                bool keep = false;
                try
                {
                    // MainModule access can throw for protected/exited processes; match on path when we can,
                    // otherwise fall back to the process-name match (better to over-wait than copy over a lock).
                    string modPath = p.MainModule?.FileName;
                    keep = string.IsNullOrEmpty(modPath)
                        || string.Equals(modPath, fullPath, StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    keep = true;
                }

                if (keep)
                    result.Add(p);
                else
                    try { p.Dispose(); } catch { }
            }
            return result;
        }

        private static bool WaitForFileUnlocked(string path, TimeSpan timeout)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return true;

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

        private bool TryRelaunchMainApp(bool showFailureDialog)
        {
            if (string.IsNullOrEmpty(_opts.RestartExe) || !File.Exists(_opts.RestartExe))
                return false;

            string workDir = Path.GetDirectoryName(_opts.RestartExe) ?? "";

            // Don't launch a fresh instance while an old one is still shutting down — that's exactly
            // the "file in use" race. Make sure every prior instance is gone first.
            if (!WaitForAllInstancesExit(_opts.RestartExe, TimeSpan.FromSeconds(20)))
                Program.Log("Relaunch: prior instances still present after wait; proceeding cautiously.");

            for (int attempt = 1; attempt <= 8; attempt++)
            {
                try
                {
                    if (!WaitForFileUnlocked(_opts.RestartExe, TimeSpan.FromSeconds(5)))
                        throw new IOException("Application files are still locked.");

                    var rp = Process.Start(new ProcessStartInfo
                    {
                        FileName = _opts.RestartExe,
                        UseShellExecute = true,
                        WorkingDirectory = workDir,
                    });
                    Program.Log("Relaunched main app as pid " + (rp == null ? "(null)" : rp.Id.ToString()));
                    return rp != null;
                }
                catch (Exception ex)
                {
                    Program.Log("Restart attempt " + attempt + " failed: " + ex.Message);
                    Thread.Sleep(400 * attempt);
                }
            }

            if (showFailureDialog)
            {
                Fail("Update installed, but the app could not be relaunched.\n\n" +
                     "Open Hiatme Tool Suite from the desktop or install folder.");
            }
            return false;
        }

        private static readonly HashSet<string> PreservedInstallSubdirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday", "Template Temps",
        };

        private static bool IsPreservedUserDataPath(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
                return false;
            string top = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
            return PreservedInstallSubdirs.Contains(top);
        }

        /// <summary>
        /// Recursive copy of <paramref name="srcDir"/> over <paramref name="dstDir"/>:
        ///   * Files in src overwrite the dst version (with a brief retry loop in case the OS is slow to release a lock).
        ///   * Files in dst that aren't in src are left in place — that's how we preserve user templates,
        ///     %install%/Monday/ etc., and any other side-loaded content.
        /// </summary>
        private static void CopyDirectoryOverwrite(string srcDir, string dstDir)
        {
            foreach (string sub in Directory.GetDirectories(srcDir, "*", SearchOption.AllDirectories))
            {
                string rel = sub.Substring(srcDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (IsPreservedUserDataPath(rel))
                    continue;
                string target = Path.Combine(dstDir, rel);
                Directory.CreateDirectory(target);
            }
            foreach (string file in Directory.GetFiles(srcDir, "*", SearchOption.AllDirectories))
            {
                string rel = file.Substring(srcDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (IsPreservedUserDataPath(rel))
                    continue;
                string target = Path.Combine(dstDir, rel);
                string targetDir = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(targetDir))
                    Directory.CreateDirectory(targetDir);

                CopyWithRetry(file, target);
            }
        }

        private static void CopyWithRetry(string src, string dst)
        {
            const int maxAttempts = 8;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    if (File.Exists(dst))
                    {
                        try { File.SetAttributes(dst, FileAttributes.Normal); } catch { }
                    }
                    File.Copy(src, dst, overwrite: true);
                    return;
                }
                catch (IOException) when (attempt < maxAttempts)
                {
                    Thread.Sleep(250 * attempt);
                }
                catch (UnauthorizedAccessException) when (attempt < maxAttempts)
                {
                    Thread.Sleep(250 * attempt);
                }
            }
            // Last-ditch attempt: move the locked file aside and copy. If even that fails, let the exception bubble.
            try
            {
                string aside = dst + ".old-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                if (File.Exists(dst)) File.Move(dst, aside);
            }
            catch { }
            File.Copy(src, dst, overwrite: true);
        }
    }
}
