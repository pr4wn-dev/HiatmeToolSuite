using System;
using System.Collections.Generic;
using System.Text;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Timestamped BUILD transcript on the desk (toolbar + server poll).</summary>
    internal sealed class SupeyBuildSessionLog
    {
        private readonly List<string> _lines = new List<string>();
        private readonly object _lock = new object();
        private const int MaxLines = 500;
        private int _serverLogLinesSeen;

        public void Clear()
        {
            lock (_lock)
            {
                _lines.Clear();
                _serverLogLinesSeen = 0;
            }
        }

        public void Add(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            string entry = DateTime.Now.ToString("HH:mm:ss") + "  " + line.Trim();
            lock (_lock)
            {
                _lines.Add(entry);
                if (_lines.Count > MaxLines)
                    _lines.RemoveRange(0, _lines.Count - MaxLines);
            }
        }

        public void AddServerLines(IEnumerable<string> serverLines)
        {
            AddServerLinesFromIndex(serverLines, 0);
        }

        /// <summary>Append only new tail lines (build-progress poll returns last N events each time).</summary>
        public void AddServerLinesIncremental(IReadOnlyList<string> serverLines)
        {
            if (serverLines == null || serverLines.Count == 0) return;
            lock (_lock)
            {
                AddServerLinesFromIndex(serverLines, _serverLogLinesSeen);
                if (serverLines.Count > _serverLogLinesSeen)
                    _serverLogLinesSeen = serverLines.Count;
            }
        }

        private void AddServerLinesFromIndex(IEnumerable<string> serverLines, int startIndex)
        {
            if (serverLines == null) return;
            int i = 0;
            foreach (var ln in serverLines)
            {
                if (i++ < startIndex) continue;
                if (!string.IsNullOrWhiteSpace(ln))
                    Add("[server] " + ln.Trim());
            }
        }

        public string ToText()
        {
            lock (_lock)
                return string.Join(Environment.NewLine, _lines);
        }
    }
}
