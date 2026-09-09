using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Update
{
    /// <summary>
    /// Rewrites desk .lnk files so they keep pointing at the folder that was just updated.
    /// A loose exe on the Desktop is never treated as the install — extracting the flat
    /// release zip there dumps DLLs next to every other shortcut.
    /// </summary>
    internal static class DesktopShortcut
    {
        public const string MainExeName = "Hiatme Tool Suite v3.exe";
        public const string ShortcutFileName = "Hiatme Tool Suite.lnk";

        public static bool IsLooseUserRoot(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir))
                return false;
            string full;
            try { full = Path.GetFullPath(dir).TrimEnd('\\'); }
            catch { return false; }

            foreach (string root in UserRoots())
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

        public static void RepairAfterInstall(string installDir)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(installDir) || !Directory.Exists(installDir))
                    return;
                if (IsLooseUserRoot(installDir))
                {
                    Program.Log("Skipping shortcut repair — install dir is the Desktop/Documents root: " + installDir);
                    return;
                }

                string exe = Path.Combine(installDir, MainExeName);
                if (!File.Exists(exe))
                    return;
                exe = Path.GetFullPath(exe);
                string workDir = Path.GetDirectoryName(exe);

                foreach (string desktop in DesktopDirs())
                {
                    if (string.IsNullOrEmpty(desktop) || !Directory.Exists(desktop))
                        continue;
                    WriteShortcut(Path.Combine(desktop, ShortcutFileName), exe, workDir);
                    RetargetExistingHiatmeShortcuts(desktop, exe, workDir);
                }
            }
            catch (Exception ex)
            {
                Program.Log("Shortcut repair failed: " + ex.Message);
            }
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
                if (string.IsNullOrWhiteSpace(target))
                {
                    WriteShortcut(lnk, exe, workDir);
                    continue;
                }

                bool missing = !File.Exists(target);
                bool different = !string.Equals(Path.GetFullPath(target), exe, StringComparison.OrdinalIgnoreCase);
                bool looksLikeSuite = Path.GetFileName(target)
                    .Equals(MainExeName, StringComparison.OrdinalIgnoreCase);
                if (missing || (looksLikeSuite && different))
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
                Program.Log("Wrote shortcut " + lnkPath + " -> " + exe);
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

        private static string[] UserRoots()
        {
            return new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            };
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
