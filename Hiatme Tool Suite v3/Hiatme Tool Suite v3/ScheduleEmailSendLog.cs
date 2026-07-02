using System;
using System.IO;
using System.Text;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Append-only log of schedule email sends (hiatme_config\email_send_log.txt) so
    /// "driver didn't get it" reports can be checked against what actually went out.
    /// </summary>
    internal static class ScheduleEmailSendLog
    {
        public static string LogPath =>
            Path.Combine(AppContext.BaseDirectory ?? "", "hiatme_config", "email_send_log.txt");

        public static void Append(
            DateTime serviceDate,
            string driverDisplayName,
            string email,
            bool ok,
            string detail)
        {
            try
            {
                string path = LogPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));

                var sb = new StringBuilder();
                sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.Append("  ").Append(ok ? "OK  " : "FAIL");
                sb.Append("  schedule ").Append(serviceDate.ToString("yyyy-MM-dd"));
                sb.Append("  ").Append((driverDisplayName ?? "").Trim());
                sb.Append(" <").Append((email ?? "").Trim()).Append(">");
                if (!ok && !string.IsNullOrWhiteSpace(detail))
                    sb.Append("  — ").Append(detail.Replace("\r", " ").Replace("\n", " ").Trim());

                File.AppendAllText(path, sb.ToString() + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // Logging is best-effort — never block or fail a send over it.
            }
        }
    }
}
