using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Hiatme_Tool_Suite_v3
{
    internal static class Program
    {
        private const string StartupAudioFileName = "must-have-precious.mp3";

        /// <summary>Plays <c>Resources\must-have-precious.mp3</c> in full once (NAudio + Media Foundation).</summary>
        internal static void TryPlayStartupMyPreciousOnce()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                if (string.IsNullOrEmpty(baseDir))
                    return;

                string path = Path.Combine(baseDir, "Resources", StartupAudioFileName);
                if (!File.Exists(path))
                    path = Path.Combine(baseDir, StartupAudioFileName);
                if (!File.Exists(path))
                    return;

                string fullPath = Path.GetFullPath(path);
                var playThread = new Thread(() => PlayStartupMp3FullNaudio(fullPath))
                {
                    IsBackground = true,
                    Name = "StartupMyPreciousAudio"
                };
                playThread.SetApartmentState(ApartmentState.STA);
                playThread.Start();
            }
            catch
            {
                /* optional audio */
            }
        }

        private static void PlayStartupMp3FullNaudio(string path)
        {
            try
            {
                using (var reader = new MediaFoundationReader(path))
                using (var output = new WasapiOut(AudioClientShareMode.Shared, 150))
                {
                    output.Init(reader);
                    output.Play();
                    while (output.PlaybackState == PlaybackState.Playing)
                        Thread.Sleep(25);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Startup sound: " + ex);
            }
        }

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

            // Force TLS 1.2 (and 1.3 where available) for ALL outbound HTTPS in this process.
            // .NET Framework 4.8's default `SecurityProtocol` is the OS default, which on some
            // Windows configurations still negotiates TLS 1.0 — modern servers like
            // router.project-osrm.org reject that with SEC_E_ILLEGAL_MESSAGE, which then makes
            // every Supey schedule fall back to straight-line routes. Setting it explicitly here
            // fixes OSRM, Nominatim, and any other HTTPS calls in one shot.
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                // Tls13 enum value (12288) was added in 4.8 but is rejected at runtime on older
                // Windows builds — wrap so a missing flag doesn't take the whole app down.
                try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)12288; } catch { }
            }
            catch { /* fail silent — leaves whatever the OS default was */ }

            // Set GMap.NET's User-Agent / Referer / cache path before any tile fetches so OSM
            // doesn't 403 us with the "Access blocked" warning tiles.
            GMapInitializer.EnsureInitialized();
            UserSettingsMigration.ApplyAfterVersionChange();
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
