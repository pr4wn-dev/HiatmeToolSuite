using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Named scopes for everything that can block the UI thread. Ints, not strings, so
    /// the high-frequency ones (list cell draws, toast paints) cost a stopwatch and an
    /// array add — no allocation per call.
    /// </summary>
    internal enum UiScope
    {
        None = 0,
        ToastPush,
        ToastRelayout,
        ToastStep,
        ToastLayoutText,
        ToastPaint,
        PresenceSetOthers,
        PresenceRelayout,
        RevisionRead,
        ActivityEvents,
        ActivityHeartbeat,
        ActivityPoll,
        ChatPoll,
        ChatViewAdd,
        ChatViewPaint,
        TripsDrawItem,
        TripsDrawSubItem,
        DriversDraw,
        /// <summary>Panel URL resolve. Probes candidates over the network, so it must never
        /// land here from the UI thread — if this scope ever shows time, that is the bug.</summary>
        SettingsResolve,
        /// <summary>GMap.NET tile HTTP. Must never stay on the UI thread.</summary>
        MapTile,
        Count,
    }

    /// <summary>
    /// Watchdog that proves where UI-thread time goes instead of guessing.
    ///
    /// A background thread watches a heartbeat that only the UI thread can advance, so any
    /// gap is a real freeze the user felt. Every stall over <see cref="StallMs"/> is logged
    /// with whichever <see cref="UiScope"/> was on the stack when it happened. Separately,
    /// each scope self-times so we get a cost table (calls / total / max) even when nothing
    /// stalls. Report lands in %LOCALAPPDATA%\HiatmeToolSuite\ui-stalls.log and is posted to
    /// the panel, so desks that actually reproduce the lag report their own numbers.
    /// </summary>
    internal static class UiStallWatch
    {
        public const int StallMs = 120;
        private const int BeatMs = 20;
        private const int WatchMs = 25;
        private const int MaxStallRecords = 400;

        private static readonly object Gate = new object();
        private static readonly long[] Calls = new long[(int)UiScope.Count];
        private static readonly double[] TotalMs = new double[(int)UiScope.Count];
        private static readonly double[] MaxMs = new double[(int)UiScope.Count];

        // Same scopes measured off the UI thread. Kept apart so background work never
        // inflates the UI numbers, and so a slow panel probe still shows its real cost.
        private static readonly long[] BgCalls = new long[(int)UiScope.Count];
        private static readonly double[] BgTotalMs = new double[(int)UiScope.Count];
        private static readonly double[] BgMaxMs = new double[(int)UiScope.Count];

        private static readonly List<string> Stalls = new List<string>();

        private static System.Diagnostics.Stopwatch _clock;
        private static System.Windows.Forms.Timer _beatTimer;
        private static Thread _watcher;
        private static volatile bool _running;
        private static long _lastBeatMs;            // Interlocked: written by UI, read by watcher
        private static volatile int _activeScope;   // UiScope the UI thread is inside
        private static volatile int _scopeDepth;
        private static int _uiThreadId;             // only this thread may set _activeScope
        private static long _scopeEnteredMs;        // Interlocked
        private static int _stallCount;
        private static double _worstStallMs;
        private static string _lastUiStack = "";
        private static int _beatN;

        private static long NowMs => _clock?.ElapsedMilliseconds ?? 0;

        public static bool Enabled { get; private set; }
        public static int StallCount => _stallCount;
        public static double WorstStallMs => _worstStallMs;

        private static string LogPath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "HiatmeToolSuite");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "ui-stalls.log");
            }
        }

        /// <summary>Start the watchdog. Safe to call twice.</summary>
        public static void Start()
        {
            if (Enabled) return;
            Enabled = true;
            _running = true;
            _uiThreadId = Thread.CurrentThread.ManagedThreadId;
            _clock = System.Diagnostics.Stopwatch.StartNew();
            Interlocked.Exchange(ref _lastBeatMs, _clock.ElapsedMilliseconds);

            // Only the UI thread can advance this, so a gap == a real freeze.
            _beatTimer = new System.Windows.Forms.Timer { Interval = BeatMs };
            _beatTimer.Tick += (_, __) =>
            {
                Interlocked.Exchange(ref _lastBeatMs, NowMs);
                // Snapshot the UI stack every ~160ms so a freeze can name the
                // caller instead of "(outside any watched scope)".
                if ((++_beatN & 7) == 0)
                {
                    try { _lastUiStack = Environment.StackTrace; }
                    catch { }
                }
            };
            _beatTimer.Start();

            _watcher = new Thread(WatchLoop)
            {
                IsBackground = true,
                Name = "UiStallWatch",
                Priority = ThreadPriority.AboveNormal,
            };
            _watcher.Start();

            Append("=== session start " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " v"
                + Application.ProductVersion + " ===");
        }

        public static void Stop()
        {
            if (!Enabled) return;
            _running = false;
            try { _beatTimer?.Stop(); _beatTimer?.Dispose(); } catch { }
            _beatTimer = null;
            Enabled = false;
        }

        private static void WatchLoop()
        {
            long lastReported = -1;
            while (_running)
            {
                Thread.Sleep(WatchMs);
                if (_clock == null) continue;
                long now = NowMs;
                long startedAt = Interlocked.Read(ref _lastBeatMs);
                if (now - startedAt < StallMs) continue;
                if (startedAt <= lastReported) continue;
                int scope = _activeScope;
                long inScopeMs = scope != 0 ? now - Interlocked.Read(ref _scopeEnteredMs) : 0;
                // Wait for the UI thread to come back so we can report the full freeze.
                while (_running && NowMs - Interlocked.Read(ref _lastBeatMs) >= StallMs)
                    Thread.Sleep(WatchMs);
                lastReported = startedAt;
                string stack;
                lock (Gate) stack = _lastUiStack;
                RecordStall(Interlocked.Read(ref _lastBeatMs) - startedAt, (UiScope)scope, inScopeMs, stack);
            }
        }

        private static void RecordStall(double ms, UiScope scope, double inScopeMs, string stack)
        {
            lock (Gate)
            {
                _stallCount++;
                if (ms > _worstStallMs) _worstStallMs = ms;
                string line = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}  STALL {1,6:N0}ms  scope={2}{3}",
                    DateTime.Now.ToString("HH:mm:ss.fff"),
                    ms,
                    scope == UiScope.None ? "(outside any watched scope)" : scope.ToString(),
                    scope == UiScope.None ? "" : string.Format(
                        CultureInfo.InvariantCulture, " (in scope {0:N0}ms, depth {1})", inScopeMs, _scopeDepth));
                Stalls.Add(line);
                if (Stalls.Count > MaxStallRecords) Stalls.RemoveAt(0);
                Append(line);
                if (ms >= 1000 && !string.IsNullOrEmpty(stack))
                    Append(TrimStack(stack));
            }
        }

        private static string TrimStack(string stack)
        {
            if (string.IsNullOrEmpty(stack)) return "";
            var lines = stack.Replace("\r\n", "\n").Split('\n');
            var sb = new StringBuilder();
            int kept = 0;
            for (int i = 0; i < lines.Length && kept < 18; i++)
            {
                string L = lines[i].Trim();
                if (L.Length == 0) continue;
                if (L.IndexOf("UiStallWatch", StringComparison.Ordinal) >= 0) continue;
                if (L.IndexOf("System.Environment.GetStackTrace", StringComparison.Ordinal) >= 0) continue;
                if (L.IndexOf("System.Environment.get_StackTrace", StringComparison.Ordinal) >= 0) continue;
                sb.Append("    ").Append(L).AppendLine();
                kept++;
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ scopes

        /// <summary>
        /// Time one suspect block: <c>using (UiStallWatch.Measure(UiScope.ToastPush)) { ... }</c>.
        /// Nesting keeps the outermost scope as the blame target.
        /// </summary>
        public static Measured Measure(UiScope scope) => new Measured(scope);

        internal struct Measured : IDisposable
        {
            private readonly int _scope;
            private readonly double _startMs;
            private readonly bool _outermost;
            private readonly bool _onUi;

            public Measured(UiScope scope)
            {
                if (!Enabled || _clock == null)
                {
                    _scope = 0; _startMs = 0; _outermost = false; _onUi = false;
                    return;
                }
                _scope = (int)scope;
                _startMs = _clock.Elapsed.TotalMilliseconds;
                _onUi = Thread.CurrentThread.ManagedThreadId == _uiThreadId;
                // Blame attribution belongs to the UI thread alone; a pool thread entering a
                // scope must not make the watcher label the next freeze with that scope.
                _outermost = _onUi && _scopeDepth == 0;
                if (_onUi) _scopeDepth++;
                if (_outermost)
                {
                    _activeScope = _scope;
                    Interlocked.Exchange(ref _scopeEnteredMs, (long)_startMs);
                }
            }

            public void Dispose()
            {
                if (_scope == 0 || _clock == null) return;
                double ms = _clock.Elapsed.TotalMilliseconds - _startMs;
                lock (Gate)
                {
                    if (_onUi)
                    {
                        Calls[_scope]++;
                        TotalMs[_scope] += ms;
                        if (ms > MaxMs[_scope]) MaxMs[_scope] = ms;
                    }
                    else
                    {
                        BgCalls[_scope]++;
                        BgTotalMs[_scope] += ms;
                        if (ms > BgMaxMs[_scope]) BgMaxMs[_scope] = ms;
                    }
                }
                if (_onUi) _scopeDepth = Math.Max(0, _scopeDepth - 1);
                if (_outermost)
                {
                    _activeScope = 0;
                    Interlocked.Exchange(ref _scopeEnteredMs, 0);
                }
            }
        }

        // ------------------------------------------------------------------ report

        /// <summary>Cost table + stall list, newest first. Written to the log and postable to the panel.</summary>
        public static string Report()
        {
            var sb = new StringBuilder();
            lock (Gate)
            {
                sb.AppendLine("UI thread report  (stall threshold " + StallMs + "ms)");
                sb.AppendLine("stalls=" + _stallCount + "  worst=" + _worstStallMs.ToString("N0", CultureInfo.InvariantCulture) + "ms");
                sb.AppendLine();
                sb.AppendLine("scope                     calls      total ms       max ms     avg ms");
                for (int i = 1; i < (int)UiScope.Count; i++)
                {
                    if (Calls[i] == 0) continue;
                    sb.AppendLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0,-22} {1,9:N0} {2,13:N1} {3,12:N1} {4,10:N2}",
                        (UiScope)i, Calls[i], TotalMs[i], MaxMs[i], TotalMs[i] / Calls[i]));
                }
                bool anyBg = false;
                for (int i = 1; i < (int)UiScope.Count && !anyBg; i++) anyBg = BgCalls[i] != 0;
                if (anyBg)
                {
                    sb.AppendLine();
                    sb.AppendLine("off the UI thread (does not freeze the window):");
                    for (int i = 1; i < (int)UiScope.Count; i++)
                    {
                        if (BgCalls[i] == 0) continue;
                        sb.AppendLine(string.Format(
                            CultureInfo.InvariantCulture,
                            "{0,-22} {1,9:N0} {2,13:N1} {3,12:N1} {4,10:N2}",
                            (UiScope)i, BgCalls[i], BgTotalMs[i], BgMaxMs[i], BgTotalMs[i] / BgCalls[i]));
                    }
                }
                if (Stalls.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("stalls:");
                    for (int i = Stalls.Count - 1; i >= 0 && i >= Stalls.Count - 40; i--)
                        sb.AppendLine("  " + Stalls[i]);
                }
            }
            return sb.ToString();
        }

        /// <summary>Reset counters so a measurement run starts clean.</summary>
        public static void ResetCounters()
        {
            lock (Gate)
            {
                Array.Clear(Calls, 0, Calls.Length);
                Array.Clear(TotalMs, 0, TotalMs.Length);
                Array.Clear(MaxMs, 0, MaxMs.Length);
                Array.Clear(BgCalls, 0, BgCalls.Length);
                Array.Clear(BgTotalMs, 0, BgTotalMs.Length);
                Array.Clear(BgMaxMs, 0, BgMaxMs.Length);
                Stalls.Clear();
                _stallCount = 0;
                _worstStallMs = 0;
                Append("--- counters reset " + DateTime.Now.ToString("HH:mm:ss") + " ---");
            }
        }

        /// <summary>Append the current report to the log (called on a timer and at shutdown).</summary>
        public static void FlushReport(string tag = "")
        {
            if (!Enabled) return;
            Append("--- report " + (string.IsNullOrEmpty(tag) ? "" : tag + " ")
                + DateTime.Now.ToString("HH:mm:ss") + " ---");
            Append(Report());
        }

        private static void Append(string text)
        {
            try { File.AppendAllText(LogPath, (text ?? "") + Environment.NewLine); }
            catch { }
        }

        // ---------------------------------------------------------------- upload

        private static System.Threading.Timer _uploadTimer;
        private static int _lastUploadedStalls = -1;

        /// <summary>
        /// Send this desk's report to the panel whenever new stalls appear, so the desks that
        /// actually reproduce the lag report their own numbers instead of us guessing.
        /// Runs on a pool thread — never touches the UI thread.
        /// </summary>
        public static void StartReporting(Func<HiatmeAiSettings> settingsProvider, int everyMs = 60_000)
        {
            if (_uploadTimer != null || settingsProvider == null) return;
            _uploadTimer = new System.Threading.Timer(
                _ => { try { UploadIfChanged(settingsProvider); } catch { } },
                null, everyMs, everyMs);
        }

        private static void UploadIfChanged(Func<HiatmeAiSettings> settingsProvider)
        {
            int stalls = _stallCount;
            if (stalls == _lastUploadedStalls) return;
            _lastUploadedStalls = stalls;
            var settings = settingsProvider();
            string baseUrl = (settings?.BaseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl)) return;

            var body = new Newtonsoft.Json.Linq.JObject
            {
                ["dispatcher"] = ScheduleActivityIdentity.DispatcherName(),
                ["client_id"] = ScheduleActivityIdentity.ClientId(settings),
                ["machine"] = ScheduleActivityIdentity.Machine(),
                ["version"] = Application.ProductVersion,
                ["stalls"] = stalls,
                ["worst_ms"] = _worstStallMs,
                ["report"] = Report(),
            };
            try
            {
                using (var http = HiatmePanelHttp.Create(TimeSpan.FromSeconds(8)))
                using (var req = new System.Net.Http.HttpRequestMessage(
                    System.Net.Http.HttpMethod.Post, baseUrl + "/api/hiatme/uistall"))
                {
                    if (!string.IsNullOrWhiteSpace(settings.ApiToken))
                        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                            "Bearer", settings.ApiToken.Trim());
                    ScheduleActivityIdentity.AddHeaders(req, settings);
                    req.Content = new System.Net.Http.StringContent(
                        body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
                    http.SendAsync(req).Wait(TimeSpan.FromSeconds(8));
                }
            }
            catch { }
        }
    }
}
