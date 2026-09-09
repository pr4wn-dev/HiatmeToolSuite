using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Keeps the desk desktop shortcut aimed at the running install after an update.
    /// Skips Visual Studio / bin folders so the build PC does not get a Debug shortcut.
    /// </summary>
    internal static class DesktopShortcut
    {
        private const string MainExeName = "Hiatme Tool Suite v3.exe";
        private const string ShortcutFileName = "Hiatme Tool Suite.lnk";

        public static void RepairForRunningInstall()
        {
            try
            {
                string exe = Assembly.GetExecutingAssembly().Location;
                if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
                    return;
                exe = Path.GetFullPath(exe);
                if (IsDevBuildPath(exe))
                    return;

                string workDir = Path.GetDirectoryName(exe);
                if (string.IsNullOrEmpty(workDir) || IsLooseUserRoot(workDir))
                    return;

                foreach (string desktop in DesktopDirs())
                {
                    if (string.IsNullOrEmpty(desktop) || !Directory.Exists(desktop))
                        continue;
                    WriteShortcut(Path.Combine(desktop, ShortcutFileName), exe, workDir);
                    RetargetExistingHiatmeShortcuts(desktop, exe, workDir);
                }
            }
            catch
            {
                /* never block startup */
            }
        }

        private static bool IsDevBuildPath(string exe)
        {
            string p = exe.Replace('/', '\\');
            return p.IndexOf(@"\bin\Debug\", StringComparison.OrdinalIgnoreCase) >= 0
                || p.IndexOf(@"\bin\Release\", StringComparison.OrdinalIgnoreCase) >= 0
                || p.IndexOf(@"\source\repos\", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsLooseUserRoot(string dir)
        {
            string full;
            try { full = Path.GetFullPath(dir).TrimEnd('\\'); }
            catch { return false; }

            foreach (string root in new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            })
            {
                if (string.IsNullOrEmpty(root))
                    continue;
                try
                {
                    if (string.Equals(Path.GetFullPath(root).TrimEnd('\\'), full, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch { }
            }
            return false;
        }

        private static void RetargetExistingHiatmeShortcuts(string desktop, string exe, string workDir)
        {
            string[] links;
            try { links = Directory.GetFiles(desktop, "*.lnk"); }
            catch { return; }

            foreach (string lnk in links)
            {
                string name = Path.GetFileNameWithoutExtension(lnk) ?? "";
                if (name.IndexOf("Hiatme", StringComparison.OrdinalIgnoreCase) < 0
                    && name.IndexOf("Tool Suite", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                string target = ReadShortcutTarget(lnk);
                if (string.IsNullOrWhiteSpace(target) || !File.Exists(target))
                {
                    WriteShortcut(lnk, exe, workDir);
                    continue;
                }

                bool different = !string.Equals(Path.GetFullPath(target), exe, StringComparison.OrdinalIgnoreCase);
                bool looksLikeSuite = Path.GetFileName(target)
                    .Equals(MainExeName, StringComparison.OrdinalIgnoreCase);
                if (looksLikeSuite && different)
                    WriteShortcut(lnk, exe, workDir);
            }
        }

        private static void WriteShortcut(string lnkPath, string exe, string workDir)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
                return;
            object shell = Activator.CreateInstance(shellType);
            try
            {
                object shortcut = shellType.InvokeMember(
                    "CreateShortcut",
                    BindingFlags.InvokeMethod,
                    null,
                    shell,
                    new object[] { lnkPath });
                if (shortcut == null)
                    return;
                Type scType = shortcut.GetType();
                scType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { exe });
                scType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { workDir ?? "" });
                scType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { exe + ",0" });
                scType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { "Hiatme Tool Suite" });
                scType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            finally
            {
                try { Marshal.FinalReleaseComObject(shell); } catch { }
            }
        }

        private static string ReadShortcutTarget(string lnkPath)
        {
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null)
                    return null;
                object shell = Activator.CreateInstance(shellType);
                try
                {
                    object shortcut = shellType.InvokeMember(
                        "CreateShortcut",
                        BindingFlags.InvokeMethod,
                        null,
                        shell,
                        new object[] { lnkPath });
                    if (shortcut == null)
                        return null;
                    object target = shortcut.GetType().InvokeMember(
                        "TargetPath", BindingFlags.GetProperty, null, shortcut, null);
                    return target as string;
                }
                finally
                {
                    try { Marshal.FinalReleaseComObject(shell); } catch { }
                }
            }
            catch
            {
                return null;
            }
        }

        private static string[] DesktopDirs()
        {
            return new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            };
        }
    }
}
