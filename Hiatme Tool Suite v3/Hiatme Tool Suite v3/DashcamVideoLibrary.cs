using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Reads a dashcam-video library organised as <c>{root}\{DRIVER}\...\{clip}.MP4</c> and
    /// works out, purely from the file names + sizes, what footage is missing and when each
    /// driver's SD card is at risk of loop-overwriting itself.
    ///
    /// Clip name format (confirmed against the live library):
    /// <c>YYYYMMDD_HHMMSS_NNNNN_T_C.MP4</c> — date, time, 5-digit sequence counter, type
    /// letter (N = normal), channel (A = front, B = rear). A and B share the same sequence
    /// number, so the counter is the ground truth for "did we lose a clip".
    ///
    /// Nothing here reads file contents — only names, sizes and timestamps — so a full scan of
    /// a 100k+ clip / multi-TB library is fast and safe to run on a background thread.
    /// </summary>
    internal static class DashcamVideoLibrary
    {
        /// <summary>Default library root. Overridable via <see cref="DashcamVideoStore"/>.</summary>
        public const string DefaultRoot = @"F:\VIDEOS FOR 2026";

        // A driver whose newest clip is older than this looks parked/archived, not "at risk".
        private const int ActiveWindowDays = 45;

        // A sequence gap is only counted as lost footage when the elapsed wall-clock time roughly
        // matches the number of missing clips (camera kept recording; we're missing the middle).
        // A counter that jumps without matching time = a different card / van, not lost footage —
        // this is what stops multi-card drivers from showing tens of thousands of false "missing".
        private const double GapTimeMatchLo = 0.6;
        private const double GapTimeMatchHi = 1.6;
        private const int MaxMissingPerGap = 500;

        // Internal date gaps up to this many missing days are flagged as "lost days"; longer
        // gaps read as time-off / vacation and are surfaced separately, not as lost footage.
        private const int MaxLostDayRun = 3;

        private const double DefaultClipMinutes = 3.0;   // fallback only; real value is derived
        private const double DefaultFillGbPerHour = 11.0; // fallback only; real value is derived

        public sealed class Clip
        {
            public DateTime Timestamp;
            public int Seq;
            public string Type;
            public char Channel;
            public long SizeBytes;
            public string FileName;
        }

        /// <summary>A break in the sequence counter within one continuous recording run.</summary>
        public sealed class SeqGap
        {
            public int LastSeqBefore;
            public int NextSeqAfter;
            public DateTime TimeBefore;
            public DateTime TimeAfter;
            public int MissingCount => Math.Max(0, NextSeqAfter - LastSeqBefore - 1);

            /// <summary>Stable id so the user can mark a specific gap "explained / ignore".</summary>
            public string Key => LastSeqBefore.ToString(CultureInfo.InvariantCulture) + "-" +
                                 NextSeqAfter.ToString(CultureInfo.InvariantCulture) + "@" +
                                 TimeBefore.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        }

        public enum BackupStatus { Archived, Ok, Soon, Overdue, Unknown }

        public sealed class DriverReport
        {
            public string Driver;
            public string FolderPath;

            public int ClipInstants;        // unique (seq,time) capture moments
            public int FileCount;           // raw .MP4 files (both channels)
            public long TotalBytes;
            public DateTime FirstClip;
            public DateTime LastClip;

            public double ClipMinutes;      // derived median clip length
            public double RecordedHours;
            public int ActiveDays;
            public double AvgDailyHours;
            public double FillGbPerHour;    // derived
            public double MaxOffloadGb;     // biggest single sub-folder dump (chip-size hint)

            public List<SeqGap> SeqGaps = new List<SeqGap>();
            public int MissingClipCount;
            public List<DateTime> LostDays = new List<DateTime>();
            public List<Tuple<DateTime, DateTime>> LongGaps = new List<Tuple<DateTime, DateTime>>();
            public int ChannelMismatchCount;    // clips missing a partner angle (front-only + rear-only)
            public int FrontOnlyCount;          // has front (A), no rear (B)
            public int RearOnlyCount;           // has rear (B), no front (A)
            public int LargeJumpCount;          // counter jumps that don't match time = card/van changes

            public bool IsActive;

            // Backup / overwrite risk
            public int CapacityGb;
            public bool CapacityIsGuessed;
            public double CapacityHours;
            public double HoursSinceOffload;
            public double PercentUsed;
            public double DaysUntilOverwrite;   // negative = already overwriting
            public int DaysSinceOffload;
            public BackupStatus Status = BackupStatus.Unknown;

            public double TotalGb => TotalBytes / (1024.0 * 1024.0 * 1024.0);

            // "Problems" = actionable footage loss / backup risk. Front-only clips are usually a
            // van hardware reality, so they're shown as info but don't trip the problems filter.
            public bool HasProblems => MissingClipCount > 0 || LostDays.Count > 0 ||
                                       Status == BackupStatus.Overdue || Status == BackupStatus.Soon;
        }

        public sealed class ScanResult
        {
            public string Root;
            public DateTime ScannedAtLocal;
            public List<DriverReport> Drivers = new List<DriverReport>();
            public string Warning;
        }

        /// <summary>
        /// Tries to parse a dashcam clip file name. Returns false for anything that isn't a
        /// well-formed clip (other extensions, ad-hoc names, etc.) so junk never skews the math.
        /// </summary>
        public static bool TryParse(string fileName, out DateTime ts, out int seq, out string type, out char channel)
        {
            ts = default(DateTime); seq = 0; type = null; channel = '\0';
            if (string.IsNullOrEmpty(fileName)) return false;

            string name = fileName;
            int dot = name.LastIndexOf('.');
            if (dot >= 0)
            {
                string ext = name.Substring(dot + 1);
                if (!ext.Equals("MP4", StringComparison.OrdinalIgnoreCase)) return false;
                name = name.Substring(0, dot);
            }

            // YYYYMMDD_HHMMSS_NNNNN_T_C
            string[] p = name.Split('_');
            if (p.Length < 5) return false;
            if (p[0].Length != 8 || p[1].Length != 6) return false;

            if (!DateTime.TryParseExact(p[0] + p[1], "yyyyMMddHHmmss",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out ts))
                return false;

            if (!int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out seq))
                return false;

            type = p[3];
            string ch = p[p.Length - 1];
            if (ch.Length != 1) return false;
            channel = char.ToUpperInvariant(ch[0]);
            if (channel != 'A' && channel != 'B') return false;
            return true;
        }

        /// <summary>
        /// Enumerates <paramref name="root"/> one driver folder at a time and produces a report
        /// per driver. <paramref name="progress"/> is called with (driverName, index, total).
        /// </summary>
        public static ScanResult Scan(
            string root,
            DashcamVideoStore.Data settings,
            Action<string, int, int> progress = null,
            CancellationToken cancel = default(CancellationToken))
        {
            var result = new ScanResult
            {
                Root = root,
                ScannedAtLocal = DateTime.Now,
            };

            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                result.Warning = "Video folder not found: " + root;
                return result;
            }

            string[] driverDirs;
            try { driverDirs = Directory.GetDirectories(root); }
            catch (Exception ex) { result.Warning = "Could not list drivers: " + ex.Message; return result; }

            Array.Sort(driverDirs, StringComparer.OrdinalIgnoreCase);
            int total = driverDirs.Length;
            for (int i = 0; i < driverDirs.Length; i++)
            {
                cancel.ThrowIfCancellationRequested();
                string dir = driverDirs[i];
                string driver = Path.GetFileName(dir);
                progress?.Invoke(driver, i, total);

                if (settings != null && settings.IsIgnored(driver)) continue;

                try
                {
                    var report = AnalyzeDriver(driver, dir, settings, cancel);
                    if (report != null) result.Drivers.Add(report);
                }
                catch (OperationCanceledException) { throw; }
                catch { /* one bad folder shouldn't sink the whole scan */ }
            }

            progress?.Invoke(null, total, total);
            return result;
        }

        private static DriverReport AnalyzeDriver(
            string driver, string dir, DashcamVideoStore.Data settings, CancellationToken cancel)
        {
            var clips = new List<Clip>();
            long totalBytes = 0;
            int fileCount = 0;

            // Biggest single immediate sub-folder = one card dump → hints at physical chip size.
            var subFolderBytes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

            foreach (string path in Directory.EnumerateFiles(dir, "*.MP4", SearchOption.AllDirectories))
            {
                cancel.ThrowIfCancellationRequested();
                string fn = Path.GetFileName(path);
                long size;
                try { size = new FileInfo(path).Length; } catch { size = 0; }

                if (!TryParse(fn, out DateTime ts, out int seq, out string type, out char ch))
                    continue;

                fileCount++;
                totalBytes += size;
                clips.Add(new Clip { Timestamp = ts, Seq = seq, Type = type, Channel = ch, SizeBytes = size, FileName = fn });

                string rel = GetImmediateSubFolder(dir, path);
                subFolderBytes.TryGetValue(rel, out long acc);
                subFolderBytes[rel] = acc + size;
            }

            if (clips.Count == 0) return null;

            var report = new DriverReport
            {
                Driver = driver,
                FolderPath = dir,
                FileCount = fileCount,
                TotalBytes = totalBytes,
            };
            report.MaxOffloadGb = subFolderBytes.Count == 0 ? 0
                : subFolderBytes.Values.Max() / (1024.0 * 1024.0 * 1024.0);

            // One entry per capture moment (collapse the A/B pair), ordered by time.
            var byMoment = clips
                .GroupBy(c => new { c.Timestamp, c.Seq })
                .Select(g => new
                {
                    g.Key.Timestamp,
                    g.Key.Seq,
                    HasA = g.Any(x => x.Channel == 'A'),
                    HasB = g.Any(x => x.Channel == 'B'),
                })
                .OrderBy(x => x.Timestamp).ThenBy(x => x.Seq)
                .ToList();

            report.ClipInstants = byMoment.Count;
            report.FirstClip = byMoment[0].Timestamp;
            report.LastClip = byMoment[byMoment.Count - 1].Timestamp;
            report.FrontOnlyCount = byMoment.Count(x => x.HasA && !x.HasB);
            report.RearOnlyCount = byMoment.Count(x => x.HasB && !x.HasA);
            report.ChannelMismatchCount = report.FrontOnlyCount + report.RearOnlyCount;

            // Pass 1: derive this driver's clip length from contiguous (delta==1) neighbours.
            var clipLenSamples = new List<double>();
            for (int i = 1; i < byMoment.Count; i++)
            {
                if (byMoment[i].Seq - byMoment[i - 1].Seq == 1)
                {
                    double mins = (byMoment[i].Timestamp - byMoment[i - 1].Timestamp).TotalMinutes;
                    if (mins > 0.1 && mins < 15) clipLenSamples.Add(mins);
                }
            }
            report.ClipMinutes = Median(clipLenSamples, DefaultClipMinutes);

            // Pass 2: a forward counter gap is lost footage ONLY when the elapsed time matches it
            // (continuous recording with a hole). delta<=0 is a card reset/swap; a jump whose time
            // doesn't match is a different card, not lost footage.
            for (int i = 1; i < byMoment.Count; i++)
            {
                var prev = byMoment[i - 1];
                var cur = byMoment[i];
                int delta = cur.Seq - prev.Seq;
                if (delta <= 1) continue;

                double expected = delta * report.ClipMinutes;
                double actual = (cur.Timestamp - prev.Timestamp).TotalMinutes;
                double ratio = expected > 0.01 ? actual / expected : double.MaxValue;

                if (ratio >= GapTimeMatchLo && ratio <= GapTimeMatchHi && (delta - 1) <= MaxMissingPerGap)
                {
                    report.SeqGaps.Add(new SeqGap
                    {
                        LastSeqBefore = prev.Seq,
                        NextSeqAfter = cur.Seq,
                        TimeBefore = prev.Timestamp,
                        TimeAfter = cur.Timestamp,
                    });
                }
                else
                {
                    report.LargeJumpCount++; // different card / van boundary
                }
            }

            // Drop gaps the user has explicitly marked "explained".
            if (settings != null)
                report.SeqGaps = report.SeqGaps.Where(g => !settings.IsGapAcknowledged(driver, g.Key)).ToList();
            report.MissingClipCount = report.SeqGaps.Sum(g => g.MissingCount);
            report.RecordedHours = report.ClipInstants * report.ClipMinutes / 60.0;
            report.FillGbPerHour = report.RecordedHours > 0.01
                ? report.TotalGb / report.RecordedHours
                : DefaultFillGbPerHour;

            // Active days + which weekdays this driver actually works (self-calibrating).
            var dates = byMoment.Select(x => x.Timestamp.Date).Distinct().OrderBy(d => d).ToList();
            report.ActiveDays = dates.Count;
            report.AvgDailyHours = report.ActiveDays > 0 ? report.RecordedHours / report.ActiveDays : 0;

            // Internal missing days: short island gaps = lost days; long gaps = time off.
            for (int i = 1; i < dates.Count; i++)
            {
                int gap = (int)(dates[i] - dates[i - 1]).TotalDays;
                if (gap <= 1) continue;
                int missing = gap - 1;
                if (missing <= MaxLostDayRun)
                {
                    for (int d = 1; d <= missing; d++)
                    {
                        var day = dates[i - 1].AddDays(d);
                        if (settings == null || !settings.IsLostDayAcknowledged(driver, day))
                            report.LostDays.Add(day);
                    }
                }
                else
                {
                    report.LongGaps.Add(Tuple.Create(dates[i - 1], dates[i]));
                }
            }

            var workingWeekdays = InferWorkingWeekdays(byMoment.Select(x => x.Timestamp.Date));

            // Backup / overwrite math.
            report.IsActive = (DateTime.Today - report.LastClip.Date).TotalDays <= ActiveWindowDays;
            report.CapacityGb = ResolveCapacity(driver, report.MaxOffloadGb, settings, out bool guessed);
            report.CapacityIsGuessed = guessed;
            report.CapacityHours = report.FillGbPerHour > 0.01
                ? report.CapacityGb / report.FillGbPerHour
                : 0;
            report.DaysSinceOffload = (int)(DateTime.Today - report.LastClip.Date).TotalDays;

            int workDaysSince = CountWorkdays(report.LastClip.Date.AddDays(1), DateTime.Today, workingWeekdays);
            report.HoursSinceOffload = workDaysSince * report.AvgDailyHours;
            report.PercentUsed = report.CapacityHours > 0.01
                ? report.HoursSinceOffload / report.CapacityHours
                : 0;
            report.DaysUntilOverwrite = report.AvgDailyHours > 0.01
                ? (report.CapacityHours - report.HoursSinceOffload) / report.AvgDailyHours
                : double.NaN;

            report.Status = ClassifyBackup(report);
            return report;
        }

        /// <summary>
        /// Re-runs only the capacity-dependent backup math for one already-scanned driver, so the
        /// 256/512 toggle updates instantly without re-reading the folder.
        /// </summary>
        public static void ApplyCapacity(DriverReport r, int capacityGb)
        {
            if (r == null || capacityGb <= 0) return;
            r.CapacityGb = capacityGb;
            r.CapacityIsGuessed = false;
            r.CapacityHours = r.FillGbPerHour > 0.01 ? r.CapacityGb / r.FillGbPerHour : 0;
            r.PercentUsed = r.CapacityHours > 0.01 ? r.HoursSinceOffload / r.CapacityHours : 0;
            r.DaysUntilOverwrite = r.AvgDailyHours > 0.01
                ? (r.CapacityHours - r.HoursSinceOffload) / r.AvgDailyHours
                : double.NaN;
            r.Status = ClassifyBackup(r);
        }

        private static BackupStatus ClassifyBackup(DriverReport r)
        {
            if (!r.IsActive) return BackupStatus.Archived;
            if (r.CapacityHours <= 0.01 || r.AvgDailyHours <= 0.01) return BackupStatus.Unknown;
            if (r.PercentUsed >= 1.0) return BackupStatus.Overdue;
            if (r.PercentUsed >= 0.70) return BackupStatus.Soon;
            return BackupStatus.Ok;
        }

        private static int ResolveCapacity(string driver, double maxOffloadGb, DashcamVideoStore.Data settings, out bool guessed)
        {
            guessed = true;
            if (settings != null && settings.TryGetCapacity(driver, out int cap) && cap > 0)
            {
                guessed = false;
                return cap;
            }
            // A single dump that already exceeds what a 256 GB card could hold ⇒ it's a 512.
            if (maxOffloadGb > 240) return 512;
            return 256;
        }

        private static HashSet<DayOfWeek> InferWorkingWeekdays(IEnumerable<DateTime> dates)
        {
            var byDow = new Dictionary<DayOfWeek, int>();
            var weeksSeen = new Dictionary<DayOfWeek, HashSet<int>>();
            var cal = CultureInfo.InvariantCulture.Calendar;
            foreach (var d in dates)
            {
                byDow.TryGetValue(d.DayOfWeek, out int c);
                byDow[d.DayOfWeek] = c + 1;
                if (!weeksSeen.TryGetValue(d.DayOfWeek, out var set)) { set = new HashSet<int>(); weeksSeen[d.DayOfWeek] = set; }
                set.Add(cal.GetWeekOfYear(d, CalendarWeekRule.FirstDay, DayOfWeek.Sunday) + d.Year * 100);
            }
            int totalWeeks = weeksSeen.Values.SelectMany(s => s).Distinct().Count();
            if (totalWeeks == 0) return new HashSet<DayOfWeek>();

            var result = new HashSet<DayOfWeek>();
            foreach (var kv in weeksSeen)
                if ((double)kv.Value.Count / totalWeeks >= 0.4) result.Add(kv.Key);

            // Never let inference produce an empty set for an active driver.
            if (result.Count == 0)
                foreach (var kv in byDow.OrderByDescending(x => x.Value).Take(5)) result.Add(kv.Key);
            return result;
        }

        private static int CountWorkdays(DateTime fromInclusive, DateTime toInclusive, HashSet<DayOfWeek> working)
        {
            if (working == null || working.Count == 0 || toInclusive < fromInclusive) return 0;
            int n = 0;
            for (var d = fromInclusive.Date; d <= toInclusive.Date; d = d.AddDays(1))
                if (working.Contains(d.DayOfWeek)) n++;
            return n;
        }

        private static string GetImmediateSubFolder(string driverDir, string filePath)
        {
            try
            {
                string rel = filePath.Substring(driverDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                int slash = rel.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
                return slash >= 0 ? rel.Substring(0, slash) : "(root)";
            }
            catch { return "(root)"; }
        }

        private static double Median(List<double> values, double fallback)
        {
            if (values == null || values.Count == 0) return fallback;
            values.Sort();
            int mid = values.Count / 2;
            return values.Count % 2 == 1 ? values[mid] : (values[mid - 1] + values[mid]) / 2.0;
        }
    }
}
