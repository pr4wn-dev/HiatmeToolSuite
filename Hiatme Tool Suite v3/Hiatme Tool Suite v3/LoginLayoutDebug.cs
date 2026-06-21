using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>NDJSON debug logger for login layout investigation (session 87e3dd).</summary>
    internal static class LoginLayoutDebug
    {
        private static readonly string[] LogPaths = BuildLogPaths();

        private static string[] BuildLogPaths()
        {
            var list = new List<string>
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug-87e3dd.log"),
                @"C:\Users\megap\HiatmeToolSuite\debug-87e3dd.log",
                @"C:\Users\megap\HiatmeToolSuite\Hiatme Tool Suite v3\debug-87e3dd.log",
                @"C:\Users\megap\.cursor\projects\c-Users-megap-HiatmeToolSuite\debug-87e3dd.log",
            };
            return list.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        public static void Log(string location, string message, string hypothesisId, object data, string runId = "pre-fix")
        {
            // #region agent log
            try
            {
                var payload = new Dictionary<string, object>
                {
                    ["sessionId"] = "87e3dd",
                    ["runId"] = runId,
                    ["hypothesisId"] = hypothesisId,
                    ["location"] = location,
                    ["message"] = message,
                    ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    ["data"] = data,
                };
                string line = JsonConvert.SerializeObject(payload) + Environment.NewLine;
                WriteLine(line);
            }
            catch (Exception ex)
            {
                try { WriteLine("{\"sessionId\":\"87e3dd\",\"message\":\"LOG_FAIL\",\"error\":\"" + ex.Message.Replace("\"", "'") + "\"}" + Environment.NewLine); } catch { }
            }
            // #endregion
        }

        public static void LogSimple(string location, string hypothesisId, string message)
        {
            // #region agent log
            try
            {
                WriteLine(JsonConvert.SerializeObject(new Dictionary<string, object>
                {
                    ["sessionId"] = "87e3dd",
                    ["hypothesisId"] = hypothesisId,
                    ["location"] = location,
                    ["message"] = message,
                    ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                }) + Environment.NewLine);
            }
            catch { }
            // #endregion
        }

        private static void WriteLine(string line)
        {
            foreach (string path in LogPaths)
            {
                try
                {
                    string dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir))
                        Directory.CreateDirectory(dir);
                    File.AppendAllText(path, line, Encoding.UTF8);
                }
                catch { /* try next path */ }
            }
        }

        public static object ControlSnapshot(Control c)
        {
            if (c == null || c.IsDisposed) return null;
            Rectangle screen = Rectangle.Empty;
            try { screen = c.RectangleToScreen(new Rectangle(0, 0, c.Width, c.Height)); } catch { }
            return new Dictionary<string, object>
            {
                ["name"] = c.Name,
                ["type"] = c.GetType().FullName,
                ["visible"] = c.Visible,
                ["left"] = c.Left,
                ["top"] = c.Top,
                ["width"] = c.Width,
                ["height"] = c.Height,
                ["screenX"] = screen.X,
                ["screenY"] = screen.Y,
                ["parent"] = c.Parent?.Name,
                ["parentType"] = c.Parent?.GetType().Name,
            };
        }

        public static object OverlapPair(Control a, Control b)
        {
            if (a == null || b == null || a.IsDisposed || b.IsDisposed) return null;
            try
            {
                var ra = a.RectangleToScreen(new Rectangle(0, 0, a.Width, a.Height));
                var rb = b.RectangleToScreen(new Rectangle(0, 0, b.Width, b.Height));
                var inter = Rectangle.Intersect(ra, rb);
                bool overlaps = inter.Width > 0 && inter.Height > 0;
                return new Dictionary<string, object>
                {
                    ["a"] = a.Name,
                    ["b"] = b.Name,
                    ["overlaps"] = overlaps,
                    ["intersectionW"] = overlaps ? inter.Width : 0,
                    ["intersectionH"] = overlaps ? inter.Height : 0,
                };
            }
            catch { return null; }
        }

        public static List<object> TreeSnapshot(Control root, int maxDepth = 4)
        {
            if (root == null) return null;
            return Walk(root, 0, maxDepth).ToList();
        }

        /// <summary>Controls on tabPage1 whose screen rect intersects the login footer (bleed scan).</summary>
        public static List<object> FooterBleedScan(Control footer, Control tabPage)
        {
            var hits = new List<object>();
            if (footer == null || tabPage == null || footer.IsDisposed || tabPage.IsDisposed) return hits;
            Rectangle footerScreen;
            try { footerScreen = footer.RectangleToScreen(new Rectangle(0, 0, footer.Width, footer.Height)); }
            catch { return hits; }

            void Scan(Control c)
            {
                if (c == null || c.IsDisposed || !c.Visible) return;
                if (ReferenceEquals(c, footer)) return;
                try
                {
                    var r = c.RectangleToScreen(new Rectangle(0, 0, c.Width, c.Height));
                    var inter = Rectangle.Intersect(footerScreen, r);
                    if (inter.Width > 2 && inter.Height > 2)
                    {
                        hits.Add(new Dictionary<string, object>
                        {
                            ["control"] = ControlSnapshot(c),
                            ["intersectionW"] = inter.Width,
                            ["intersectionH"] = inter.Height,
                        });
                    }
                }
                catch { /* skip */ }
                foreach (Control child in c.Controls)
                    Scan(child);
            }

            Scan(tabPage);
            return hits;
        }

        private static IEnumerable<object> Walk(Control c, int depth, int maxDepth)
        {
            yield return new Dictionary<string, object>
            {
                ["depth"] = depth,
                ["control"] = ControlSnapshot(c),
            };
            if (depth >= maxDepth) yield break;
            foreach (Control child in c.Controls)
            {
                foreach (object item in Walk(child, depth + 1, maxDepth))
                    yield return item;
            }
        }
    }
}
