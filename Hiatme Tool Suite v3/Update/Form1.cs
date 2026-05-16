using MaterialSkin;
using MaterialSkin.Controls;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
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
    /// User data preservation: we ONLY overwrite files contained in the zip. Anything else in the install
    /// directory (Monday/, Tuesday/, …, Template Temps/, user-saved files, etc.) is left untouched.
    /// Login info lives in %LOCALAPPDATA% and never sees this code path at all.
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
                "Your saved login and templates will be kept.";
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
        }

        private Label _statusLabel;

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
                    bool exited = await Task.Run(() => WaitForProcessExit(_opts.WaitForPid.Value, TimeSpan.FromSeconds(30)));
                    Program.Log("WaitForProcessExit(" + _opts.WaitForPid.Value + ") returned " + exited);
                    if (!exited)
                    {
                        Status("Main app didn't exit in time. Trying to terminate...");
                        try
                        {
                            using (var p = Process.GetProcessById(_opts.WaitForPid.Value))
                            {
                                p.Kill();
                                p.WaitForExit(5000);
                                Program.Log("Killed pid " + _opts.WaitForPid.Value);
                            }
                        }
                        catch (Exception ex)
                        {
                            Program.Log("Could not kill pid " + _opts.WaitForPid.Value + ": " + ex.Message);
                        }
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
                    Status("Done. Restarting Hiatme Tool Suite...");
                    try
                    {
                        var rp = Process.Start(new ProcessStartInfo
                        {
                            FileName = _opts.RestartExe,
                            UseShellExecute = false,
                            WorkingDirectory = Path.GetDirectoryName(_opts.RestartExe) ?? "",
                        });
                        Program.Log("Relaunched main app as pid " + (rp == null ? "(null)" : rp.Id.ToString()));
                    }
                    catch (Exception ex)
                    {
                        Program.Log("Restart failed: " + ex);
                        Fail("Update installed, but the app could not be relaunched.\n\n" + ex.Message);
                        return;
                    }
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
        /// Waits up to <paramref name="timeout"/> for the given pid to exit. Returns true if the process is gone
        /// (either because it exited cleanly or because the pid was already invalid by the time we asked).
        /// </summary>
        private static bool WaitForProcessExit(int pid, TimeSpan timeout)
        {
            try
            {
                using (var p = Process.GetProcessById(pid))
                {
                    return p.WaitForExit((int)timeout.TotalMilliseconds);
                }
            }
            catch (ArgumentException)
            {
                // Process not found = already exited.
                return true;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
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
                string target = Path.Combine(dstDir, rel);
                Directory.CreateDirectory(target);
            }
            foreach (string file in Directory.GetFiles(srcDir, "*", SearchOption.AllDirectories))
            {
                string rel = file.Substring(srcDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
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
