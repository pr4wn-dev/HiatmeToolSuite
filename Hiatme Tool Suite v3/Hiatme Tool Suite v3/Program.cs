using System;
using System.Diagnostics;
using System.IO;
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
        // File-time window: exactly 0:01 through 0:07 (six seconds of audio).
        private const double MyPreciousStartSeconds = 1.0;
        private const double MyPreciousEndSeconds = 7.0;
        private static readonly TimeSpan MyPreciousStart = TimeSpan.FromSeconds(MyPreciousStartSeconds);
        private static readonly TimeSpan MyPreciousEnd = TimeSpan.FromSeconds(MyPreciousEndSeconds);
        private static readonly TimeSpan MyPreciousSlice = TimeSpan.FromSeconds(MyPreciousEndSeconds - MyPreciousStartSeconds);

        /// <summary>Plays <c>Resources\my-precious.mp3</c> from 0:01 to 0:07 once (NAudio + Media Foundation).</summary>
        internal static void TryPlayStartupMyPreciousOnce()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                if (string.IsNullOrEmpty(baseDir))
                    return;

                string path = Path.Combine(baseDir, "Resources", "my-precious.mp3");
                if (!File.Exists(path))
                    path = Path.Combine(baseDir, "my-precious.mp3");
                if (!File.Exists(path))
                    return;

                string fullPath = Path.GetFullPath(path);
                var playThread = new Thread(() => PlayMyPreciousClipNaudio(fullPath))
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

        private static void PlayMyPreciousClipNaudio(string path)
        {
            try
            {
                using (var reader = new MediaFoundationReader(path))
                using (var output = new WasapiOut(AudioClientShareMode.Shared, 150))
                {
                    output.Init(reader);
                    reader.CurrentTime = MyPreciousStart;
                    output.Play();
                    var sw = Stopwatch.StartNew();
                    // Stop when decoder position reaches 7s (correct for 0:01–0:07). Stopwatch is only a guard against a stuck clock.
                    var safetyCap = MyPreciousSlice + TimeSpan.FromMilliseconds(600);
                    while (output.PlaybackState == PlaybackState.Playing)
                    {
                        if (reader.CurrentTime >= MyPreciousEnd)
                        {
                            output.Stop();
                            break;
                        }

                        if (sw.Elapsed >= safetyCap)
                        {
                            output.Stop();
                            break;
                        }

                        Thread.Sleep(15);
                    }
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
