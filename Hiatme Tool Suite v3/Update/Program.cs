using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace Update
{
    internal static class Program
    {
        // Marker arg that signals this process is already running from the temp self-relocated copy.
        // Without it, Update.exe ALWAYS copies itself to %TEMP% and re-execs so it can replace its own file on disk.
        private const string FromTempArg = "--from-temp";

        // Single per-process log file. Without this, any failure inside the self-relocated copy is invisible
        // because the parent main app has already exited by then.
        internal static readonly string LogPath = Path.Combine(Path.GetTempPath(), "HiatmeUpdaterLog.txt");

        internal static void Log(string message)
        {
            try
            {
                File.AppendAllText(LogPath,
                    "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "] " + message + Environment.NewLine);
            }
            catch
            {
                // Logging is best-effort; never crash the updater because a temp file can't be written.
            }
        }

        [STAThread]
        static void Main(string[] args)
        {
            // Parse the args we accept. Unknown args are ignored so users can run Update.exe by hand
            // (e.g. by double-clicking) and still see a friendly window.
            var opts = UpdateArgs.Parse(args);
            bool isLegacyDoubleClick = !opts.HasAnyUpdateAction;
            bool fromTemp = args != null && args.Any(a => string.Equals(a, FromTempArg, StringComparison.OrdinalIgnoreCase));

            Log("Update.exe started. cwd=" + Environment.CurrentDirectory +
                " exe=" + Assembly.GetExecutingAssembly().Location +
                " args=[" + string.Join(" | ", args ?? new string[0]) + "]" +
                " fromTemp=" + fromTemp);

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

            if (isLegacyDoubleClick)
            {
                Log("Legacy double-click mode (no update args). Showing hint form.");
                Application.Run(new Form1());
                return;
            }

            if (!fromTemp)
            {
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
                    // Fall through to in-place mode below. The downside is we can't overwrite our own
                    // Update.exe on disk, but most releases don't need to and an in-place install is
                    // infinitely better than the silent-failure mode we had before this fix.
                    Log("Falling back to in-place updater run from " + Assembly.GetExecutingAssembly().Location);
                    Application.Run(new Form1(opts));
                }
                return; // original exe exits; the temp copy (or in-place fallback) takes over
            }

            Log("Worker mode (running from temp). Showing update form.");
            Application.Run(new Form1(opts));
            Log("Update.exe exiting normally.");
        }

        /// <summary>
        /// Copies <c>Update.exe</c> AND every file alongside it (DLLs, .config, etc.) into a fresh per-run
        /// folder under %TEMP%. Crucial: we need MaterialSkin.dll plus anything else .NET probes for in the
        /// exe's directory — without this the relaunched process dies on first form construction with a
        /// FileNotFoundException that the user never sees.
        /// </summary>
        private static string CopySelfAndDepsToTemp()
        {
            string self = Assembly.GetExecutingAssembly().Location;
            string srcDir = Path.GetDirectoryName(self) ?? "";
            string runDir = Path.Combine(Path.GetTempPath(), "HiatmeToolSuiteUpdaterRun", "run_" + DateTime.UtcNow.Ticks);
            Directory.CreateDirectory(runDir);

            string destExe = Path.Combine(runDir, Path.GetFileName(self));

            // Copy the exe and its sibling assemblies / native deps. We deliberately skip large bundled
            // payloads (.zip, .mp3, .otf) that aren't needed at runtime by the updater itself.
            string[] skipExt = { ".zip", ".mp3", ".otf", ".pdb" };
            foreach (var file in Directory.GetFiles(srcDir))
            {
                if (skipExt.Contains(Path.GetExtension(file).ToLowerInvariant())) continue;
                string fname = Path.GetFileName(file);
                string dest = Path.Combine(runDir, fname);
                try { File.Copy(file, dest, overwrite: true); }
                catch (Exception ex) { Log("Could not copy " + fname + ": " + ex.Message); }
            }

            // Also copy any architecture-specific native subfolders the runtime might probe (e.g. x86/, x64/
            // for SQLite.Interop.dll). Cheap insurance.
            foreach (var subName in new[] { "x86", "x64" })
            {
                string sub = Path.Combine(srcDir, subName);
                if (Directory.Exists(sub))
                {
                    string destSub = Path.Combine(runDir, subName);
                    Directory.CreateDirectory(destSub);
                    foreach (var file in Directory.GetFiles(sub))
                    {
                        try { File.Copy(file, Path.Combine(destSub, Path.GetFileName(file)), overwrite: true); }
                        catch { }
                    }
                }
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
                    // Quote anything containing spaces (unless it's already a fully-quoted token).
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

    /// <summary>
    /// Parsed command-line args.
    ///   --pid &lt;int&gt;        process id of the main app to wait for (optional)
    ///   --zip &lt;path&gt;       path to the verified zip to extract (required)
    ///   --target &lt;dir&gt;     install directory to extract over (required)
    ///   --restart &lt;exe&gt;    path to the app exe to launch after extraction (optional)
    /// </summary>
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
