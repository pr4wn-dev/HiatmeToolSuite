using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Update
{
    /// <summary>
    /// Updater UI. Behavior depends on the constructor used:
    ///   * <see cref="Form1()"/> — informational placeholder for the legacy double-click case.
    ///   * <see cref="Form1(UpdateArgs)"/> — drives the wait → extract → restart pipeline.
    ///
    /// Plain WinForms only (no MaterialSkin) so a missing theming DLL can never kill the install.
    /// User data preservation: we ONLY overwrite files contained in the zip, and we never copy
    /// weekday template folders or Template Temps from the zip even if a bad package includes them.
    /// </summary>
    public partial class Form1 : Form
    {
        private readonly UpdateArgs _opts;

        public Form1()
        {
            InitializeComponent();
            ApplyUpdaterChrome();
        }

        public Form1(UpdateArgs opts)
        {
            InitializeComponent();
            ApplyUpdaterChrome();
            _opts = opts ?? throw new ArgumentNullException(nameof(opts));

            SetupWorkerUi();
            Shown += async (_, __) => await RunUpdatePipelineAsync();
        }

        private void ApplyUpdaterChrome()
        {
            BackColor = Color.FromArgb(32, 32, 36);
            ForeColor = Color.Gainsboro;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
        }

        private Label _statusLabel;
        private Button _launchButton;

        private void SetupWorkerUi()
        {
            Text = "Hiatme Tool Suite — installing update";
            _startupHintLabel.TextAlign = ContentAlignment.TopLeft;
            _startupHintLabel.Padding = new Padding(28, 28, 28, 28);
            _startupHintLabel.Text =
                "Installing update — please don't close this window.\r\n\r\n" +
                "Your saved login and templates will be kept.\r\n\r\n" +
                "The app will reopen when installation finishes.";
            _statusLabel = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 80,
                Padding = new Padding(28, 8, 28, 16),
                Font = new Font("Segoe UI", 9F),
                ForeColor = Color.Gainsboro,
                BackColor = Color.FromArgb(32, 32, 36),
                Text = "Preparing…",
            };
            Controls.Add(_statusLabel);
            _statusLabel.BringToFront();

            _launchButton = new Button
            {
                Text = "LAUNCH HIATME TOOL SUITE",
                AutoSize = false,
                Size = new Size(320, 42),
                Anchor = AnchorStyles.Bottom,
                Visible = false,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(76, 175, 80),
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 10F),
                Cursor = Cursors.Hand,
            };
            _launchButton.FlatAppearance.BorderSize = 0;
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
            _launchButton.Location = new Point(x, y);
        }

        private void OnLaunchMainAppClicked()
        {
            _launchButton.Enabled = false;
            if (TryRelaunchMainApp(showFailureDialog: true))
                BeginInvoke((MethodInvoker)Close);
            else
                _launchButton.Enabled = true;
        }

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
                    Status("Update installed. Relaunching…");
                    await Task.Delay(500);
                    if (TryRelaunchMainApp(showFailureDialog: false))
                    {
                        Program.Log("Auto-relaunch succeeded; closing updater.");
                        await Task.Delay(350);
                        BeginInvoke((MethodInvoker)Close);
                        return;
                    }

                    Program.Log("Auto-relaunch failed; showing Launch button.");
                    Status("Update installed. Click Launch to reopen the app.");
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

                Program.Log("No restart requested or restart exe missing: " + _opts.RestartExe);
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
            Program.Log("FAIL: " + message);
            try
            {
                MessageBox.Show(this, message + "\n\nLog: " + Program.LogPath,
                    "Hiatme Tool Suite — Update", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { }
            try { BeginInvoke((MethodInvoker)Close); } catch { Close(); }
        }

        private static bool WaitForMainAppExit(int pid)
        {
            try
            {
                using (var p = Process.GetProcessById(pid))
                {
                    return p.WaitForExit(120000);
                }
            }
            catch (ArgumentException)
            {
                return true; // already gone
            }
        }

        private static bool WaitForAllInstancesExit(string exePath, TimeSpan timeout)
        {
            if (string.IsNullOrEmpty(exePath)) return true;
            string full;
            try { full = Path.GetFullPath(exePath); }
            catch { full = exePath; }

            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < timeout)
            {
                if (!AnyProcessRunningFrom(full))
                    return true;
                Thread.Sleep(300);
            }
            return !AnyProcessRunningFrom(full);
        }

        private static bool AnyProcessRunningFrom(string fullExePath)
        {
            string name = Path.GetFileNameWithoutExtension(fullExePath);
            bool result = false;
            foreach (var p in Process.GetProcessesByName(name))
            {
                try
                {
                    string path = p.MainModule?.FileName;
                    if (!string.IsNullOrEmpty(path)
                        && string.Equals(Path.GetFullPath(path), fullExePath, StringComparison.OrdinalIgnoreCase))
                    {
                        result = true;
                    }
                }
                catch { }
                finally
                {
                    try { p.Dispose(); } catch { }
                }
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

            if (!WaitForAllInstancesExit(_opts.RestartExe, TimeSpan.FromSeconds(20)))
                Program.Log("Relaunch: prior instances still present after wait; proceeding cautiously.");

            for (int attempt = 1; attempt <= 8; attempt++)
            {
                try
                {
                    if (!WaitForFileUnlocked(_opts.RestartExe, TimeSpan.FromSeconds(5)))
                        throw new IOException("Application files are still locked.");

                    // Shell-execute is the most reliable way to start a WinForms GUI app from another process.
                    var rp = Process.Start(new ProcessStartInfo
                    {
                        FileName = _opts.RestartExe,
                        WorkingDirectory = workDir,
                        UseShellExecute = true,
                        ErrorDialog = false,
                    });
                    Program.Log("Relaunched main app as pid " + (rp == null ? "(null)" : rp.Id.ToString()));

                    // Confirm it actually stayed up (AV / lock races sometimes spawn-and-die).
                    Thread.Sleep(600);
                    if (rp != null && !rp.HasExited)
                        return true;
                    if (AnyProcessRunningFrom(Path.GetFullPath(_opts.RestartExe)))
                        return true;

                    // Fallback: cmd start (handles some quoting / working-dir oddities).
                    var rp2 = Process.Start(new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = "/c start \"\" \"" + _opts.RestartExe + "\"",
                        WorkingDirectory = workDir,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    });
                    Thread.Sleep(800);
                    if (AnyProcessRunningFrom(Path.GetFullPath(_opts.RestartExe)))
                    {
                        Program.Log("Relaunch succeeded via cmd start.");
                        return true;
                    }
                    Program.Log("Relaunch process started but main app not detected running.");
                    return false;
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

        /// <summary>
        /// Recursive copy of <paramref name="srcDir"/> over <paramref name="dstDir"/>:
        ///   * Files in src overwrite the dst version (with a brief retry loop in case the OS is slow to release a lock).
        ///   * Files in dst that aren't in src are left in place — that's how we preserve user templates,
        ///     %install%/Monday/ etc., and any other side-loaded content.
        /// </summary>
        private static void CopyDirectoryOverwrite(string srcDir, string dstDir)
        {
            Directory.CreateDirectory(dstDir);
            foreach (string srcFile in Directory.GetFiles(srcDir))
            {
                string name = Path.GetFileName(srcFile);
                string destFile = Path.Combine(dstDir, name);
                CopyFileWithRetry(srcFile, destFile);
            }
            foreach (string srcSub in Directory.GetDirectories(srcDir))
            {
                string relName = Path.GetFileName(srcSub);
                if (PreservedInstallSubdirs.Contains(relName))
                {
                    Program.Log("Skipping preserved user folder from zip: " + relName);
                    continue;
                }
                CopyDirectoryOverwrite(srcSub, Path.Combine(dstDir, relName));
            }
        }

        private static void CopyFileWithRetry(string src, string dst)
        {
            const int attempts = 10;
            for (int i = 1; i <= attempts; i++)
            {
                try
                {
                    File.Copy(src, dst, overwrite: true);
                    return;
                }
                catch (IOException) when (i < attempts)
                {
                    Thread.Sleep(200 * i);
                }
                catch (UnauthorizedAccessException) when (i < attempts)
                {
                    Thread.Sleep(200 * i);
                }
            }
            File.Copy(src, dst, overwrite: true);
        }
    }
}
