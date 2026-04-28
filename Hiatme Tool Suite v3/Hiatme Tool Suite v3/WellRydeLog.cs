using System;
using System.Text;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Thread-safe buffer for WellRyde diagnostic lines shown on the WellRyde log tab (and mirrored to the console).
    /// </summary>
    internal static class WellRydeLog
    {
        private static readonly object _lock = new object();
        private static readonly StringBuilder _sb = new StringBuilder();

        /// <summary>Keep memory bounded when something logs in a tight loop.</summary>
        private const int MaxChars = 512 * 1024;

        public static event EventHandler Changed;

        public static void WriteLine(string line)
        {
            Write((line ?? string.Empty) + Environment.NewLine);
        }

        public static void Write(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            lock (_lock)
            {
                _sb.Append(text);
                TrimIfNeeded();
            }

            try { Console.Write(text); }
            catch { /* ignore */ }

            Changed?.Invoke(null, EventArgs.Empty);
        }

        private static void TrimIfNeeded()
        {
            if (_sb.Length <= MaxChars)
                return;
            var remove = _sb.Length - MaxChars;
            _sb.Remove(0, remove);
        }

        public static string GetText()
        {
            lock (_lock)
            {
                return _sb.ToString();
            }
        }

        public static void Clear()
        {
            lock (_lock)
            {
                _sb.Clear();
            }

            Changed?.Invoke(null, EventArgs.Empty);
        }
    }
}
