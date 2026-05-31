using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Persistent WellRyde portal HTTP log (human-readable). Path: <see cref="LogFilePath"/>.
    /// Always on — no screenshots needed for session/cookie failures.
    /// </summary>
    internal static class WellRydePortalLog
    {
        private static readonly object Gate = new object();
        private static string _activeLogPath;

        static WellRydePortalLog()
        {
            _activeLogPath = ResolveLogFilePath();
        }

        /// <summary>Full path to the log file currently being written (created on first write).</summary>
        public static string LogFilePath
        {
            get
            {
                lock (Gate)
                {
                    return _activeLogPath ?? ResolveLogFilePath();
                }
            }
        }

        public static void Info(string category, string message) => WriteLine(category, message, null);

        public static void Error(string category, string message, Exception ex = null)
        {
            string detail = ex == null ? message : message + " | " + ex.GetType().Name + ": " + ex.Message;
            WriteLine(category, detail, null);
        }

        /// <summary>Legacy NDJSON hook — writes same human-readable line.</summary>
        public static void Write(string hypothesisId, string location, string message, object data = null, string runId = null)
        {
            string extra = data == null ? null : " " + data;
            if (!string.IsNullOrEmpty(runId))
                extra = (extra ?? "") + " runId=" + runId;
            WriteLine(hypothesisId + "/" + location, message + (extra ?? ""), null);
        }

        public static void HttpRequest(string method, string url, string cookieHeader, IEnumerable<string> storeKeys)
        {
            WriteLine("HTTP-REQ",
                method + " " + url
                + " | Cookie: " + RedactCookieHeader(cookieHeader)
                + " | store=[" + JoinKeys(storeKeys) + "]",
                null);
        }

        public static void HttpResponse(string url, int status, IEnumerable<string> setCookieNames,
            IEnumerable<string> storeKeys, string note = null)
        {
            WriteLine("HTTP-RSP",
                status + " " + url
                + " | Set-Cookie=[" + JoinKeys(setCookieNames) + "]"
                + " | store=[" + JoinKeys(storeKeys) + "]"
                + (string.IsNullOrEmpty(note) ? "" : " | " + note),
                null);
        }

        public static void CookieState(string step, IDictionary<string, string> store, string note = null)
        {
            string pairs = store == null || store.Count == 0
                ? "(empty)"
                : string.Join(", ", store.Select(kv => kv.Key + "=" + RedactValue(kv.Value)));
            WriteLine("COOKIES", step + " | " + pairs + (string.IsNullOrEmpty(note) ? "" : " | " + note), null);
        }

        public static string UserHintSuffix() =>
            "\r\n\r\n(Error report copied to clipboard.)";

        /// <summary>Full error + recent log lines for paste into chat/email.</summary>
        public static void CopyErrorReport(string summary, Exception ex = null, string context = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Hiatme Tool Suite — WellRyde error");
            sb.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(summary))
                sb.AppendLine(summary.Trim());
            if (!string.IsNullOrWhiteSpace(context))
            {
                sb.AppendLine();
                sb.AppendLine(context.Trim());
            }
            if (ex != null)
            {
                sb.AppendLine();
                sb.AppendLine(ex.GetType().FullName + ": " + ex.Message);
                if (!string.IsNullOrEmpty(ex.StackTrace))
                    sb.AppendLine(ex.StackTrace);
            }
            string tail = TryReadLogTail(80);
            if (!string.IsNullOrEmpty(tail))
            {
                sb.AppendLine();
                sb.AppendLine("--- wellryde-portal.log (recent) ---");
                sb.AppendLine(tail);
            }
            sb.AppendLine();
            sb.AppendLine("Log file: " + LogFilePath);
            TrySetClipboardText(sb.ToString());
        }

        /// <summary>Shows a dialog and copies the same report (with log tail) to the clipboard.</summary>
        public static DialogResult ShowError(IWin32Window owner, string title, string message,
            MessageBoxIcon icon = MessageBoxIcon.Warning)
        {
            CopyErrorReport(message);
            string box = (message ?? "").Trim()
                + "\r\n\r\n(Copied to clipboard — paste it here or into email.)";
            return MessageBox.Show(owner, box, title ?? "WellRyde", MessageBoxButtons.OK, icon);
        }

        private static void TrySetClipboardText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;
            try
            {
                Clipboard.SetText(text, TextDataFormat.UnicodeText);
                return;
            }
            catch
            {
                // retry (clipboard busy)
            }
            try
            {
                Clipboard.SetDataObject(text, true, 5, 200);
            }
            catch
            {
                // ignore
            }
        }

        private static string TryReadLogTail(int maxLines)
        {
            if (maxLines <= 0)
                return null;
            try
            {
                string path = LogFilePath;
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    return "(no log file yet)";
                string[] lines = File.ReadAllLines(path);
                if (lines.Length == 0)
                    return "(log empty)";
                if (lines.Length <= maxLines)
                    return string.Join(Environment.NewLine, lines);
                return string.Join(Environment.NewLine, lines.Skip(lines.Length - maxLines));
            }
            catch (Exception ex)
            {
                return "(could not read log: " + ex.Message + ")";
            }
        }

        private static void WriteLine(string category, string message, string unused)
        {
            try
            {
                string path = LogFilePath;
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                string line = string.Format(
                    "{0:yyyy-MM-dd HH:mm:ss.fff} [{1}] {2}{3}",
                    DateTime.Now,
                    category ?? "LOG",
                    message ?? "",
                    Environment.NewLine);

                lock (Gate)
                {
                    File.AppendAllText(path, line, Encoding.UTF8);
                }
            }
            catch
            {
                // last resort — do not break portal calls
            }
        }

        private static string ResolveLogFilePath()
        {
            var candidates = new[]
            {
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "HiatmeToolSuite", "Logs", "wellryde-portal.log"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "wellryde-portal.log"),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "HiatmeToolSuite", "wellryde-portal.log"),
            };

            foreach (string path in candidates)
            {
                try
                {
                    string dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir))
                        Directory.CreateDirectory(dir);
                    return path;
                }
                catch
                {
                    // try next
                }
            }

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wellryde-portal.log");
        }

        private static string JoinKeys(IEnumerable<string> keys) =>
            keys == null ? "" : string.Join(",", keys.Where(k => !string.IsNullOrWhiteSpace(k)));

        private static string RedactCookieHeader(string header)
        {
            if (string.IsNullOrWhiteSpace(header))
                return "(none)";
            var parts = new List<string>();
            foreach (string part in header.Split(';'))
            {
                string piece = part.Trim();
                int eq = piece.IndexOf('=');
                if (eq <= 0)
                {
                    parts.Add(piece);
                    continue;
                }
                string name = piece.Substring(0, eq).Trim();
                string val = piece.Substring(eq + 1).Trim();
                parts.Add(name + "=" + RedactValue(val));
            }
            return string.Join("; ", parts);
        }

        private static string RedactValue(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";
            if (value.Length <= 12)
                return value;
            return value.Substring(0, 8) + "…(" + value.Length + "ch)";
        }
    }
}
