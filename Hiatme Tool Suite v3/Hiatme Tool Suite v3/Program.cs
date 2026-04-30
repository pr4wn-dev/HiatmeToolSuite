using System;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    internal static class Program
    {
        /// <summary>The main entry point for the application.</summary>
        [STAThread]
        private static void Main()
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) =>
            {
                ShowExceptionChain("UI thread error", e.Exception);
                Application.Exit();
            };
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                    ShowExceptionChain("Fatal error", ex);
            };

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                Application.Run(new Form1());
            }
            catch (Exception ex)
            {
                ShowExceptionChain("Startup error", ex);
            }
        }

        /// <summary>Walks <see cref="Exception.InnerException"/> (and one level of <see cref="ReflectionTypeLoadException"/>) so reflection wrappers are readable.</summary>
        private static void ShowExceptionChain(string title, Exception ex)
        {
            var sb = new StringBuilder(4096);
            sb.AppendLine(title);
            sb.AppendLine();
            for (var e = ex; e != null; e = e.InnerException)
            {
                sb.AppendLine("=== " + e.GetType().FullName + " ===");
                sb.AppendLine(e.Message);
                if (e is ReflectionTypeLoadException rtl && rtl.LoaderExceptions != null)
                {
                    foreach (var le in rtl.LoaderExceptions)
                    {
                        if (le != null)
                            sb.AppendLine("Loader: " + le.Message);
                    }
                }

                sb.AppendLine(e.StackTrace ?? "(no stack)");
                sb.AppendLine();
            }

            try
            {
                MessageBox.Show(sb.ToString(), "Hiatme Tool Suite — error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch
            {
                /* last resort */
                Console.Error.WriteLine(sb);
            }
        }
    }
}
