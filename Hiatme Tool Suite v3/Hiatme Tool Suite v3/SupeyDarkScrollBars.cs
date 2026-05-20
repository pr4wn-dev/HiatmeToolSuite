using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Replaces the bright-gray default scrollbars with the dark "DarkMode_Explorer"
    /// theme — i.e. the same flat, square scrollbars Windows File Explorer uses
    /// when in dark mode. Track is a near-black, thumb is a muted gray that
    /// brightens on hover. Square corners are preserved.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The undocumented uxtheme.dll exports we lean on (<c>SetPreferredAppMode</c>,
    /// <c>AllowDarkModeForWindow</c>, <c>FlushMenuThemes</c>) are present on
    /// Windows 10 build 18362 (1903) and newer. On older builds the P/Invokes
    /// throw <see cref="EntryPointNotFoundException"/>; we swallow it so the app
    /// silently keeps the system-default scrollbars.
    /// </para>
    /// <para>
    /// Usage (do both):
    /// <list type="number">
    /// <item><see cref="EnableForProcess"/> — once at app startup, before <c>Application.Run</c>.</item>
    /// <item><see cref="Apply"/> — once per top-level Form, after Controls are built. The walk
    /// hooks <see cref="Control.ControlAdded"/> so dynamically added controls also pick up
    /// the dark scrollbars.</item>
    /// </list>
    /// </para>
    /// </remarks>
    internal static class SupeyDarkScrollBars
    {
        // Public uxtheme API since XP. "DarkMode_Explorer" is the magic theme
        // string that flips scrollbars / list headers to their dark equivalents
        // when the app is in dark mode.
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int SetWindowTheme(IntPtr hwnd, string pszSubAppName, string pszSubIdList);

        // Undocumented exports; called by ordinal because no name is exported.
        // Signatures + ordinals come from the leaked Win10 1903 PDBs and have
        // been stable through Win11 24H2.
        [DllImport("uxtheme.dll", EntryPoint = "#135", SetLastError = true)]
        private static extern int SetPreferredAppMode(int preferredAppMode);

        [DllImport("uxtheme.dll", EntryPoint = "#136", SetLastError = true)]
        private static extern void FlushMenuThemes();

        [DllImport("uxtheme.dll", EntryPoint = "#133", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AllowDarkModeForWindow(IntPtr hwnd, bool allow);

        // Used to force a scrollbar repaint after we change the theme — without it
        // some controls hold onto the old theme until the user resizes the window.
        private const int WM_THEMECHANGED = 0x031A;
        private const uint RDW_INVALIDATE = 0x0001;
        private const uint RDW_FRAME = 0x0400;
        private const uint RDW_ALLCHILDREN = 0x0080;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);

        // PreferredAppMode values: 0=Default 1=AllowDark 2=ForceDark 3=ForceLight
        // ForceDark is the right pick — we WANT dark everywhere, the rest of
        // the app is also a dark theme.
        private const int APPMODE_FORCE_DARK = 2;

        private static bool s_processEnabled;

        /// <summary>
        /// One-shot opt-in for the entire process. Safe to call multiple times;
        /// silently no-ops on Windows builds without the dark-mode exports.
        /// </summary>
        public static void EnableForProcess()
        {
            if (s_processEnabled) return;
            try
            {
                SetPreferredAppMode(APPMODE_FORCE_DARK);
                FlushMenuThemes();
                s_processEnabled = true;
            }
            catch
            {
                // EntryPointNotFoundException on Win8.1 / older Win10 — leave
                // scrollbars at the system default rather than crashing.
            }
        }

        /// <summary>
        /// Walks <paramref name="root"/>'s control tree and themes every
        /// scrollbar-bearing child. Hooks <see cref="Control.ControlAdded"/> on
        /// every descendant we visit so controls added later (the Supey tab is
        /// built programmatically AFTER this call, lazy-init dialogs, dynamically
        /// rebuilt panels) inherit the dark scrollbars without their author
        /// opting in.
        /// </summary>
        /// <remarks>
        /// Idempotent: calling Apply twice on the same control is harmless. Safe
        /// to call again after a major UI rebuild (e.g. at the end of
        /// InitializeSupeyTab) to forcibly re-walk a sub-tree.
        /// </remarks>
        public static void Apply(Control root)
        {
            if (root == null) return;
            EnableForProcess();
            ApplyRecursive(root);
        }

        private static void ApplyRecursive(Control c)
        {
            if (c == null) return;

            // Tell the theme service this window allows dark mode (per-window
            // opt-in, on top of the process-level SetPreferredAppMode). Has to
            // happen before SetWindowTheme to take effect.
            AllowDarkOnHandle(c);
            ApplyTo(c);

            // Hook ControlAdded on every descendant (idempotent: -= guards against
            // duplicate handlers if Apply is called multiple times). Earlier
            // version hooked only the root, which missed every control added
            // inside InitializeSupeyTab — TabPage/Panel/etc. are direct parents
            // of those controls, not Form1, so the event never fired on root.
            c.ControlAdded -= OnControlAdded;
            c.ControlAdded += OnControlAdded;

            foreach (Control child in c.Controls)
                ApplyRecursive(child);
        }

        private static void AllowDarkOnHandle(Control c)
        {
            if (c.IsHandleCreated)
            {
                try { AllowDarkModeForWindow(c.Handle, true); } catch { }
            }
            else
            {
                c.HandleCreated += (s, e) =>
                {
                    try { AllowDarkModeForWindow(c.Handle, true); } catch { }
                };
            }
        }

        private static void ApplyTo(Control c)
        {
            // SetWindowTheme has to run after the HWND exists — themes apply per-window.
            if (c.IsHandleCreated)
            {
                ApplyThemeNow(c);
            }
            else
            {
                c.HandleCreated += (s, e) => ApplyThemeNow(c);
            }
        }

        private static void ApplyThemeNow(Control c)
        {
            try
            {
                SetWindowTheme(c.Handle, "DarkMode_Explorer", null);
            }
            catch
            {
                // Older Windows — fall back to plain "Explorer" which at least
                // suppresses the chunky XP-era visual style on themed listviews.
                try { SetWindowTheme(c.Handle, "Explorer", null); } catch { }
            }

            // Some controls cache the old theme handle until they receive
            // WM_THEMECHANGED — without this they keep the bright-gray scrollbars
            // until the next size change. Followed by a frame redraw so the
            // scrollbar non-client area actually repaints with the new theme.
            try
            {
                SendMessage(c.Handle, WM_THEMECHANGED, IntPtr.Zero, IntPtr.Zero);
                RedrawWindow(c.Handle, IntPtr.Zero, IntPtr.Zero,
                    RDW_INVALIDATE | RDW_FRAME | RDW_ALLCHILDREN);
            }
            catch { }
        }

        private static void OnControlAdded(object sender, ControlEventArgs e)
        {
            if (e?.Control == null) return;
            // ApplyRecursive themes the new subtree AND wires ControlAdded on
            // every descendant, so further nested adds keep propagating.
            ApplyRecursive(e.Control);
        }
    }
}
