using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace Update
{
    internal static class Program
    {
        // Marker arg that signals this process is already running from a temp copy / zip-extracted worker.
        private const string FromTempArg = "--from-temp";
        private const string ApplyLatestArg = "--apply-latest";

        internal static readonly string LogPath = Path.Combine(Path.GetTempPath(), "HiatmeUpdaterLog.txt");

        internal static void Log(string message)
        {
            try
            {
                File.AppendAllText(LogPath,
                    "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "] " + message + Environment.NewLine);
            }
            catch { }
        }

        [STAThread]
        static void Main(string[] args)
        {
            args = args ?? new string[0];
            var opts = UpdateArgs.Parse(args);
            bool applyLatest = args.Any(a => string.Equals(a, ApplyLatestArg, StringComparison.OrdinalIgnoreCase))
                || IsApplyUpdateBinaryName();
            bool fromTemp = args.Any(a => string.Equals(a, FromTempArg, StringComparison.OrdinalIgnoreCase));
            bool isLegacyDoubleClick = !opts.HasAnyUpdateAction && !applyLatest;

            Log("Update.exe started. cwd=" + Environment.CurrentDirectory +
                " exe=" + Assembly.GetExecutingAssembly().Location +
                " args=[" + string.Join(" | ", args) + "]" +
                " fromTemp=" + fromTemp +
                " applyLatest=" + applyLatest);

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                Log("UNHANDLED: " + (e.ExceptionObject == null ? "(null)" : e.ExceptionObject.ToString()));
            };
            Application.ThreadException += (s, e) =>
            {
                Log("UI THREAD: " + (e.Exception == null ? "(null)" : e.Exception.ToString()));
            };

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (applyLatest)
            {
                Log("Apply-latest mode.");
                ApplyLatest.RunInteractive();
                return;
            }

            if (isLegacyDoubleClick)
            {
                Log("Legacy double-click mode (no update args). Showing repair form.");
                Application.Run(new Form1());
                return;
            }

            // Worker already in temp / extracted from zip — run pipeline directly.
            if (fromTemp)
            {
                Log("Worker mode. Showing update form.");
                Application.Run(new Form1(opts));
                Log("Update.exe exiting normally.");
                return;
            }

            // Launched from install dir without --from-temp: relocate then hand off.
            try
            {
                string tempCopy = CopySelfAndDepsToTemp();
                Log("Relocated to: " + tempCopy);
                var psi = new ProcessStartInfo
                {
                    FileName = tempCopy,
                    Arguments = BuildArgsForRelaunch(args),
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(tempCopy) ?? Path.GetTempPath(),
                };
                var p = Process.Start(psi);
                Log("Relaunched temp copy as pid " + (p == null ? "(null)" : p.Id.ToString()));
            }
            catch (Exception ex)
            {
                Log("Self-relocate failed: " + ex);
                Log("Falling back to in-place updater run from " + Assembly.GetExecutingAssembly().Location);
                Application.Run(new Form1(opts));
            }
        }

        private static bool IsApplyUpdateBinaryName()
        {
            try
            {
                string name = Path.GetFileNameWithoutExtension(Assembly.GetExecutingAssembly().Location) ?? "";
                return name.IndexOf("ApplyUpdate", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("HiatmeApply", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("RepairUpdate", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        private static string CopySelfAndDepsToTemp()
        {
            string self = Assembly.GetExecutingAssembly().Location;
            string srcDir = Path.GetDirectoryName(self) ?? "";
            string runDir = Path.Combine(Path.GetTempPath(), "HiatmeToolSuiteUpdaterRun", "run_" + DateTime.UtcNow.Ticks);
            Directory.CreateDirectory(runDir);

            string destExe = Path.Combine(runDir, Path.GetFileName(self));
            File.Copy(self, destExe, overwrite: true);

            string cfgName = Path.GetFileName(self) + ".config";
            string cfgSrc = Path.Combine(srcDir, cfgName);
            if (File.Exists(cfgSrc))
            {
                try { File.Copy(cfgSrc, Path.Combine(runDir, cfgName), overwrite: true); }
                catch (Exception ex) { Log("Could not copy " + cfgName + ": " + ex.Message); }
            }

            // Prefer Update.exe.config name when this binary was renamed HiatmeApplyUpdate.exe
            string altCfg = Path.Combine(srcDir, "Update.exe.config");
            if (!File.Exists(cfgSrc) && File.Exists(altCfg))
            {
                try { File.Copy(altCfg, Path.Combine(runDir, "Update.exe.config"), overwrite: true); }
                catch { }
            }

            if (!File.Exists(destExe))
                throw new IOException("Failed to copy Update.exe to " + destExe);

            return destExe;
        }

        private static string BuildArgsForRelaunch(string[] args)
        {
            var parts = new System.Collections.Generic.List<string>();
            if (args != null)
            {
                foreach (var a in args)
                {
                    if (a.Contains(' ') && !(a.StartsWith("\"") && a.EndsWith("\"")))
                        parts.Add("\"" + a + "\"");
                    else
                        parts.Add(a);
                }
            }
            parts.Add(FromTempArg);
            return string.Join(" ", parts);
        }
    }

    public sealed class UpdateArgs
    {
        public int? WaitForPid { get; private set; }
        public string ZipPath { get; private set; }
        public string TargetDir { get; private set; }
        public string RestartExe { get; private set; }

        public bool HasAnyUpdateAction => !string.IsNullOrEmpty(ZipPath) && !string.IsNullOrEmpty(TargetDir);

        public static UpdateArgs Parse(string[] args)
        {
            var r = new UpdateArgs();
            if (args == null) return r;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i] ?? "";
                if (string.Equals(a, "--pid", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    if (int.TryParse(args[++i], out int pid)) r.WaitForPid = pid;
                }
                else if (string.Equals(a, "--zip", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    r.ZipPath = args[++i];
                }
                else if (string.Equals(a, "--target", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    r.TargetDir = args[++i];
                }
                else if (string.Equals(a, "--restart", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    r.RestartExe = args[++i];
                }
            }
            return r;
        }
    }
}
