using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Driver Habits tile: "Free for Xm" when the driver has at least 15 minutes
    /// before they must leave for the next pickup. Drive time to that pickup
    /// comes out of the gap.
    /// </summary>
    partial class Form1
    {
        private const int LateDriversFreeMinMinutes = 15;

        // Temporary test: first, middle, and last driver in the strip are treated as free.
        private const bool LateDriversFreeEdgeTest = true;
        private bool _ldFreeEdgeTestShifted;

        private sealed class LateDriversFreeWindow
        {
            public DateTime LeaveByLocal;
            public int DriveMinutes;
            public DateTime NextPuLocal;
            public string NextCity;
            /// <summary>When the driver is back in Lewiston/Auburn. MinValue means the dropoff was already in town.</summary>
            public DateTime BackInTownLocal;
        }

        private sealed class LateDriversFreeNeed
        {
            public string Driver;
            public DateTime NextPu;
            public DateTime DropoffEnd;
            public string FromAddress;
            public string ToAddress;
            public string NextCity;
            public bool OutOfTown;
        }

        private static readonly GeoPoint LateDriversLewistonHub = new GeoPoint(44.1004, -70.2148);
        private static readonly GeoPoint LateDriversAuburnHub = new GeoPoint(44.0978, -70.2312);

        private readonly Dictionary<string, LateDriversFreeWindow> _ldFreeByDriver =
            new Dictionary<string, LateDriversFreeWindow>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _ldDoneForDay =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _ldDriveMinuteCache =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, GeoPoint> _ldFreeGeoCache =
            new Dictionary<string, GeoPoint>(StringComparer.OrdinalIgnoreCase);
        private System.Windows.Forms.Timer _ldFreeTick;
        private System.Windows.Forms.Timer _ldStarDance;
        private volatile int _ldFreeGen;

        private void EnsureLateDriversFreeTick()
        {
            if (_ldFreeTick != null)
                return;
            _ldFreeTick = new System.Windows.Forms.Timer { Interval = 15_000 };
            _ldFreeTick.Tick += (_, __) => PaintLateDriversFreeLabels();
            _ldFreeTick.Start();
        }

        private void QueueLateDriversFreeUpdate()
        {
            if (IsDisposed || !_ldBuilt)
                return;
            EnsureLateDriversFreeTick();
            int gen = ++_ldFreeGen;
            string today = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (!string.Equals(LateDriversSelectedServiceDateIso(), today, StringComparison.Ordinal))
            {
                _ldFreeByDriver.Clear();
                _ldDoneForDay.Clear();
                PaintLateDriversFreeLabels();
                return;
            }

            var trips = _ldWrTripsByTripNo.Values
                .Where(t => t != null && !string.IsNullOrWhiteSpace(t.Driver))
                .ToList();
            var drivers = (_ldDriverRows ?? new List<HiatmeAiClient.LateDriversDriverSummary>())
                .Select(d => d?.Driver)
                .Where(name => !string.IsNullOrWhiteSpace(name)
                    && !string.Equals(name, LateDriversReservedKey, StringComparison.Ordinal))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var pending = new List<LateDriversFreeNeed>();
            var ready = new Dictionary<string, LateDriversFreeWindow>(StringComparer.OrdinalIgnoreCase);
            var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            DateTime now = DateTime.Now;
            foreach (string driver in drivers)
            {
                if (LateDriversDriverFinishedDay(driver, trips, now))
                    done.Add(driver);
            }
            _ldDoneForDay.Clear();
            foreach (string driver in done)
                _ldDoneForDay.Add(driver);
            foreach (string driver in drivers)
            {
                if (done.Contains(driver))
                    continue;
                if (!TryLateDriversFreeGap(driver, trips, now, out var need))
                    continue;
                if ((need.NextPu - now).TotalMinutes < LateDriversFreeMinMinutes)
                    continue;
                if (!need.OutOfTown && string.Equals(need.FromAddress, need.ToAddress, StringComparison.OrdinalIgnoreCase))
                {
                    ready[driver] = new LateDriversFreeWindow
                    {
                        LeaveByLocal = need.NextPu,
                        DriveMinutes = 0,
                        NextPuLocal = need.NextPu,
                        NextCity = need.NextCity,
                    };
                    continue;
                }
                pending.Add(need);
            }

            PaintLateDriversFreeLabels();
            var settings = LateDriversAiSettings();
            _ = Task.Run(async () =>
            {
                try
                {
                    foreach (var item in pending)
                    {
                        if (gen != _ldFreeGen)
                            return;
                        LateDriversFreeWindow window = item.OutOfTown
                            ? await LateDriversFreeAfterReturnAsync(settings, item).ConfigureAwait(false)
                            : await LateDriversFreeInTownAsync(settings, item).ConfigureAwait(false);
                        if (gen != _ldFreeGen || window == null)
                            continue;
                        ready[item.Driver] = window;
                    }
                }
                catch
                {
                    return;
                }

                if (gen != _ldFreeGen || IsDisposed)
                    return;
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        if (gen != _ldFreeGen || IsDisposed)
                            return;
                        _ldFreeByDriver.Clear();
                        foreach (var kv in ready)
                            _ldFreeByDriver[kv.Key] = kv.Value;
                        PaintLateDriversFreeLabels();
                    }));
                }
                catch { }
            });
        }

        /// <summary>
        /// Every trip still on this driver is finished. Cancelled trips do not count.
        /// An open will-call or a later pickup means they are still working.
        /// </summary>
        private bool LateDriversDriverFinishedDay(
            string driver,
            List<HiatmeAiClient.TripScoutServerTripRow> trips,
            DateTime now)
        {
            bool any = false;
            foreach (var trip in trips)
            {
                if (trip == null || !LateDriversDriverNamesMatch(trip.Driver, driver))
                    continue;
                if (LateDriversTripIsCancelled(trip))
                    continue;
                any = true;
                if (!LateDriversTripIsFinished(trip, now))
                    return false;
            }
            return any;
        }

        /// <summary>
        /// Next timed pickup after the driver is clear of the current trip.
        /// from/to are "street, city" for the last dropoff and the next pickup.
        /// </summary>
        private bool TryLateDriversFreeGap(
            string driver,
            List<HiatmeAiClient.TripScoutServerTripRow> trips,
            DateTime now,
            out LateDriversFreeNeed need)
        {
            need = null;
            var mine = new List<HiatmeAiClient.TripScoutServerTripRow>();
            foreach (var trip in trips)
            {
                if (trip == null || !LateDriversDriverNamesMatch(trip.Driver, driver))
                    continue;
                if (LateDriversTripIsCancelled(trip))
                    continue;
                if (LateDriversSchedPuIsWillCall("", null, trip))
                    continue;
                if (!TryParseLateDriversIso(trip.SchedPuIso, out _))
                    continue;
                mine.Add(trip);
            }
            if (mine.Count == 0)
                return false;

            foreach (var trip in mine)
            {
                if (LateDriversTripOccupiesNow(trip, now))
                    return false;
            }

            HiatmeAiClient.TripScoutServerTripRow next = null;
            DateTime nextAt = DateTime.MaxValue;
            foreach (var trip in mine)
            {
                if (!TryParseLateDriversIso(trip.SchedPuIso, out var pu))
                    continue;
                pu = LateDriversAsLocal(pu);
                if (pu <= now || pu >= nextAt)
                    continue;
                next = trip;
                nextAt = pu;
            }
            if (next == null)
                return false;

            HiatmeAiClient.TripScoutServerTripRow last = null;
            DateTime lastEnd = DateTime.MinValue;
            foreach (var trip in mine)
            {
                if (!LateDriversTripIsFinished(trip, now))
                    continue;
                DateTime end = DateTime.MinValue;
                if (TryParseLateDriversIso(trip.ActualDoIso, out var actualDo))
                    end = LateDriversAsLocal(actualDo);
                else if (TryParseLateDriversIso(trip.SchedDoIso, out var schedDo))
                    end = LateDriversAsLocal(schedDo);
                if (end == DateTime.MinValue)
                    continue;
                if (end > now)
                    end = now;
                if (end < lastEnd)
                    continue;
                last = trip;
                lastEnd = end;
            }
            if (last == null)
                return false;

            string fromAddress = LateDriversFreeAddress(last.DoStreet, last.DoCity);
            string toAddress = LateDriversFreeAddress(next.PuStreet, next.PuCity);
            if (fromAddress.Length == 0 || toAddress.Length == 0)
                return false;
            need = new LateDriversFreeNeed
            {
                Driver = driver,
                NextPu = nextAt,
                DropoffEnd = lastEnd,
                FromAddress = fromAddress,
                ToAddress = toAddress,
                NextCity = (next.PuCity ?? "").Trim(),
                OutOfTown = !LateDriversCityIsLewistonAuburn(last.DoCity),
            };
            return true;
        }

        private static bool LateDriversCityIsLewistonAuburn(string city)
        {
            string name = (city ?? "").Trim().ToLowerInvariant();
            if (name.EndsWith(", me"))
                name = name.Substring(0, name.Length - 4).Trim();
            else if (name.EndsWith(" me"))
                name = name.Substring(0, name.Length - 3).Trim();
            int comma = name.IndexOf(',');
            if (comma >= 0)
                name = name.Substring(0, comma).Trim();
            return name == "lewiston" || name == "auburn";
        }

        private async Task<LateDriversFreeWindow> LateDriversFreeInTownAsync(
            HiatmeAiSettings settings, LateDriversFreeNeed item)
        {
            int? drive = await LateDriversDriveMinutesAsync(settings, item.FromAddress, item.ToAddress)
                .ConfigureAwait(false);
            if (!drive.HasValue || drive.Value < 0)
                return null;
            DateTime leaveBy = item.NextPu.AddMinutes(-drive.Value);
            if ((leaveBy - DateTime.Now).TotalMinutes < LateDriversFreeMinMinutes)
                return null;
            return new LateDriversFreeWindow
            {
                LeaveByLocal = leaveBy,
                DriveMinutes = drive.Value,
                NextPuLocal = item.NextPu,
                NextCity = item.NextCity,
            };
        }

        /// <summary>
        /// Dropoff is outside Lewiston/Auburn. The driver is not free until the shorter
        /// drive back to one of those hubs is done. Then the drive from that hub to the
        /// next pickup still comes out of the gap.
        /// </summary>
        private async Task<LateDriversFreeWindow> LateDriversFreeAfterReturnAsync(
            HiatmeAiSettings settings, LateDriversFreeNeed item)
        {
            GeoPoint? dropoff;
            GeoPoint? nextPickup;
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
            {
                dropoff = await LateDriversFreePointAsync(settings, item.FromAddress, cts.Token)
                    .ConfigureAwait(false);
                nextPickup = await LateDriversFreePointAsync(settings, item.ToAddress, cts.Token)
                    .ConfigureAwait(false);
            }
            if (!dropoff.HasValue || !nextPickup.HasValue)
                return null;

            int? toLewiston = await LateDriversDriveMinutesBetweenAsync(
                settings, dropoff.Value, LateDriversLewistonHub, item.FromAddress + " → Lewiston")
                .ConfigureAwait(false);
            int? toAuburn = await LateDriversDriveMinutesBetweenAsync(
                settings, dropoff.Value, LateDriversAuburnHub, item.FromAddress + " → Auburn")
                .ConfigureAwait(false);
            if (!toLewiston.HasValue && !toAuburn.HasValue)
                return null;

            bool lewiston;
            int returnMinutes;
            GeoPoint hub;
            if (!toAuburn.HasValue || (toLewiston.HasValue && toLewiston.Value <= toAuburn.Value))
            {
                lewiston = true;
                returnMinutes = toLewiston.Value;
                hub = LateDriversLewistonHub;
            }
            else
            {
                lewiston = false;
                returnMinutes = toAuburn.Value;
                hub = LateDriversAuburnHub;
            }

            DateTime backAt = item.DropoffEnd.AddMinutes(returnMinutes);
            int? toNext = await LateDriversDriveMinutesBetweenAsync(
                settings, hub, nextPickup.Value,
                (lewiston ? "Lewiston" : "Auburn") + " → " + item.ToAddress)
                .ConfigureAwait(false);
            if (!toNext.HasValue)
                return null;

            DateTime leaveBy = item.NextPu.AddMinutes(-toNext.Value);
            if ((leaveBy - backAt).TotalMinutes < LateDriversFreeMinMinutes)
                return null;
            return new LateDriversFreeWindow
            {
                LeaveByLocal = leaveBy,
                DriveMinutes = toNext.Value,
                NextPuLocal = item.NextPu,
                NextCity = item.NextCity,
                BackInTownLocal = backAt,
            };
        }

        private static bool LateDriversTripIsCancelled(HiatmeAiClient.TripScoutServerTripRow trip)
        {
            string status = (trip?.Status ?? "").Trim().ToLowerInvariant();
            return status.Contains("cancel");
        }

        private static bool LateDriversTripIsFinished(HiatmeAiClient.TripScoutServerTripRow trip, DateTime now)
        {
            if (trip == null || LateDriversTripIsCancelled(trip))
                return false;
            string status = (trip.Status ?? "").Trim().ToLowerInvariant();
            if (status.Contains("complet") || status.Contains("billed")
                || status.Contains("no show") || status.Contains("noshow")
                || status.Contains("drop"))
                return true;
            if (!TryParseLateDriversIso(trip.ActualDoIso, out var actualDo))
                return false;
            return LateDriversAsLocal(actualDo) <= now;
        }

        private static bool LateDriversTripOccupiesNow(HiatmeAiClient.TripScoutServerTripRow trip, DateTime now)
        {
            if (trip == null || LateDriversTripIsCancelled(trip) || LateDriversTripIsFinished(trip, now))
                return false;
            if (LateDriversSchedPuIsWillCall("", null, trip))
                return false;
            if (!TryParseLateDriversIso(trip.SchedPuIso, out var pu))
                return false;
            return LateDriversAsLocal(pu) <= now;
        }

        private static DateTime LateDriversAsLocal(DateTime dt)
        {
            if (dt.Kind == DateTimeKind.Utc)
                return dt.ToLocalTime();
            return dt;
        }

        private static string LateDriversFreeAddress(string street, string city)
        {
            street = (street ?? "").Trim();
            city = (city ?? "").Trim();
            if (street.Length == 0 || city.Length == 0)
                return "";
            return street + ", " + city;
        }

        private async Task<int?> LateDriversDriveMinutesAsync(HiatmeAiSettings settings, string from, string to)
        {
            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
                return null;
            string key = from.Trim() + " → " + to.Trim();
            lock (_ldDriveMinuteCache)
            {
                if (_ldDriveMinuteCache.TryGetValue(key, out int cached))
                    return cached;
            }
            if (settings == null || string.IsNullOrWhiteSpace(settings.BaseUrl))
                return null;
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
            {
                GeoPoint? a = await LateDriversFreePointAsync(settings, from, cts.Token).ConfigureAwait(false);
                GeoPoint? b = await LateDriversFreePointAsync(settings, to, cts.Token).ConfigureAwait(false);
                if (!a.HasValue || !b.HasValue)
                    return null;
                var route = await HiatmeGeoClient.GetCumulativeDurationsAsync(
                        settings, new List<GeoPoint> { a.Value, b.Value }, cts.Token)
                    .ConfigureAwait(false);
                if (route == null || !route.Ok || route.Durations == null || route.Durations.Count == 0)
                    return null;
                int minutes = (int)Math.Ceiling(route.Durations[route.Durations.Count - 1] / 60.0);
                if (minutes < 0)
                    return null;
                lock (_ldDriveMinuteCache)
                    _ldDriveMinuteCache[key] = minutes;
                return minutes;
            }
        }

        private async Task<int?> LateDriversDriveMinutesBetweenAsync(
            HiatmeAiSettings settings, GeoPoint from, GeoPoint to, string cacheKey)
        {
            lock (_ldDriveMinuteCache)
            {
                if (_ldDriveMinuteCache.TryGetValue(cacheKey, out int cached))
                    return cached;
            }
            if (settings == null || string.IsNullOrWhiteSpace(settings.BaseUrl))
                return null;
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
            {
                var route = await HiatmeGeoClient.GetCumulativeDurationsAsync(
                        settings, new List<GeoPoint> { from, to }, cts.Token)
                    .ConfigureAwait(false);
                if (route == null || !route.Ok || route.Durations == null || route.Durations.Count == 0)
                    return null;
                int minutes = (int)Math.Ceiling(route.Durations[route.Durations.Count - 1] / 60.0);
                if (minutes < 0)
                    return null;
                lock (_ldDriveMinuteCache)
                    _ldDriveMinuteCache[cacheKey] = minutes;
                return minutes;
            }
        }

        private async Task<GeoPoint?> LateDriversFreePointAsync(
            HiatmeAiSettings settings, string address, CancellationToken cancellationToken)
        {
            lock (_ldFreeGeoCache)
            {
                if (_ldFreeGeoCache.TryGetValue(address, out var hit))
                    return hit;
            }
            int comma = address.LastIndexOf(',');
            if (comma <= 0)
                return null;
            string street = address.Substring(0, comma).Trim();
            string city = address.Substring(comma + 1).Trim();
            var point = await HiatmeGeoClient.ResolveAsync(
                    settings, street, city, "ME", "", "us", cancellationToken)
                .ConfigureAwait(false);
            if (!point.HasValue)
                return null;
            lock (_ldFreeGeoCache)
                _ldFreeGeoCache[address] = point.Value;
            return point;
        }

        private void PaintLateDriversFreeLabels()
        {
            if (IsDisposed || _ldDriverTiles == null)
                return;
            LateDriversApplyFreeEdgeTest();
            DateTime now = DateTime.Now;
            foreach (var tile in _ldDriverTiles)
            {
                if (tile == null || tile.IsDisposed)
                    continue;
                var summary = tile.Tag as HiatmeAiClient.LateDriversDriverSummary;
                var mins = tile.Controls["ldTileMins"] as Label;
                if (mins == null || mins.IsDisposed)
                    continue;
                if (summary == null
                    || string.IsNullOrWhiteSpace(summary.Driver)
                    || string.Equals(summary.Driver, LateDriversReservedKey, StringComparison.Ordinal))
                    continue;

                LateDriversFreeWindow window = null;
                if (!_ldFreeByDriver.TryGetValue(summary.Driver, out window))
                {
                    foreach (var kv in _ldFreeByDriver)
                    {
                        if (LateDriversDriverNamesMatch(kv.Key, summary.Driver))
                        {
                            window = kv.Value;
                            break;
                        }
                    }
                }

                var star = LateDriversStarFor(tile);
                var nameRow = tile.Controls["ldTileNameRow"] as Panel;
                var nameLbl = nameRow?.Controls["ldTileName"] as Label;
                var statsLbl = tile.Controls["ldTileStats"] as Label;
                bool done = LateDriversIsDoneForDay(summary.Driver);
                LateDriversSetBulletHoles(tile, done, summary.Driver);
                if (done)
                {
                    LateDriversLayoutDriverTile(nameRow, nameLbl, statsLbl, mins, star, free: false);
                    mins.Font = SupeyTheme.BodyFont;
                    mins.Text = "Done for the day";
                    mins.ForeColor = SupeyTheme.TextSecondary;
                    LateDriversSetTileTip(mins, "All trips completed");
                    continue;
                }

                if (window != null
                    && window.BackInTownLocal != DateTime.MinValue
                    && now < window.BackInTownLocal)
                    window = null;
                int freeMinutes = window == null
                    ? 0
                    : (int)Math.Floor((window.LeaveByLocal - now).TotalMinutes);
                bool showFree = window != null && freeMinutes >= LateDriversFreeMinMinutes;
                if (!showFree)
                {
                    LateDriversLayoutDriverTile(nameRow, nameLbl, statsLbl, mins, star, free: false);
                    mins.Font = SupeyTheme.BodyFont;
                    mins.Text = summary.TotalMinutes.ToString("0", CultureInfo.InvariantCulture) + "m late";
                    mins.ForeColor = SupeyTheme.TextPrimary;
                    LateDriversSetTileTip(mins, "");
                    continue;
                }

                LateDriversLayoutDriverTile(nameRow, nameLbl, statsLbl, mins, star, free: true);

                mins.Font = LateDriversFreeCityFont();
                mins.Text = LateDriversFreeLabel(freeMinutes) + LateDriversNextPickupLine(window.NextCity);
                mins.ForeColor = SupeyTheme.SuccessText;
                bool returning = window.BackInTownLocal != DateTime.MinValue;
                string back = returning
                    ? "Back in town "
                        + window.BackInTownLocal.ToString("h:mm tt", CultureInfo.CurrentCulture)
                        + " · "
                    : "";
                string tip = back
                    + "Next pickup "
                    + window.NextPuLocal.ToString("h:mm tt", CultureInfo.CurrentCulture)
                    + " · " + window.DriveMinutes.ToString(CultureInfo.InvariantCulture)
                    + (returning ? " min from town · leave by " : " min away · leave by ")
                    + window.LeaveByLocal.ToString("h:mm tt", CultureInfo.CurrentCulture);
                LateDriversSetTileTip(mins, tip);
            }
            UpdateLateDriversFreeEdges();
        }

        private readonly Dictionary<string, LateDriversFreeEdge> _ldFreeEdges =
            new Dictionary<string, LateDriversFreeEdge>(StringComparer.OrdinalIgnoreCase);
        private bool _ldFreeEdgeUpdating;
        private bool _ldFreeEdgeHooked;
        private bool _ldFreeEdgeRestackQueued;

        private sealed class LateDriversOffscreenFree
        {
            public string Driver;
            public bool Left;
        }

        private void LateDriversApplyFreeEdgeTest()
        {
            if (!LateDriversFreeEdgeTest)
                return;
            var rows = _ldStripDrivers;
            if (rows == null || rows.Count == 0)
                return;
            DateTime leave = DateTime.Now.AddMinutes(40);
            int mid = rows.Count / 2;
            int[] picks = rows.Count == 1
                ? new[] { 0 }
                : rows.Count == 2
                    ? new[] { 0, 1 }
                    : new[] { 0, mid, rows.Count - 1 };
            foreach (int i in picks)
            {
                var row = rows[i];
                if (row == null || string.IsNullOrWhiteSpace(row.Driver))
                    continue;
                _ldDoneForDay.Remove(row.Driver);
                _ldFreeByDriver[row.Driver] = new LateDriversFreeWindow
                {
                    LeaveByLocal = leave,
                    NextPuLocal = leave.AddMinutes(10),
                    NextCity = i == rows.Count - 1 ? "Auburn" : "Lewiston",
                    DriveMinutes = 8,
                    BackInTownLocal = DateTime.MinValue,
                };
            }
        }

        private bool LateDriversDriverShowsFree(HiatmeAiClient.LateDriversDriverSummary summary, DateTime now)
        {
            if (summary == null || string.IsNullOrWhiteSpace(summary.Driver))
                return false;
            if (string.Equals(summary.Driver, LateDriversReservedKey, StringComparison.Ordinal))
                return false;
            if (LateDriversIsDoneForDay(summary.Driver))
                return false;
            LateDriversFreeWindow window = null;
            if (!_ldFreeByDriver.TryGetValue(summary.Driver, out window))
            {
                foreach (var kv in _ldFreeByDriver)
                {
                    if (LateDriversDriverNamesMatch(kv.Key, summary.Driver))
                    {
                        window = kv.Value;
                        break;
                    }
                }
            }
            if (window == null)
                return false;
            if (window.BackInTownLocal != DateTime.MinValue && now < window.BackInTownLocal)
                return false;
            int freeMinutes = (int)Math.Floor((window.LeaveByLocal - now).TotalMinutes);
            return freeMinutes >= LateDriversFreeMinMinutes;
        }

        private List<LateDriversOffscreenFree> LateDriversOffscreenFreeDrivers()
        {
            var found = new List<LateDriversOffscreenFree>();
            var rows = _ldStripDrivers;
            if (rows == null || rows.Count == 0)
                return found;
            int page = LateDriversDriverStripPageSize();
            int start = _ldDriverScrollOffset;
            int end = Math.Min(rows.Count, start + page);
            DateTime now = DateTime.Now;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i] == null || !LateDriversDriverShowsFree(rows[i], now))
                    continue;
                found.Add(new LateDriversOffscreenFree
                {
                    Driver = rows[i].Driver.Trim(),
                    Left = i < start,
                });
            }
            return found;
        }

        private SupeyCard LateDriversTileForDriver(string driver)
        {
            if (_ldDriverTiles == null || string.IsNullOrWhiteSpace(driver))
                return null;
            foreach (SupeyCard tile in _ldDriverTiles)
            {
                if (tile == null || tile.IsDisposed || !tile.Visible)
                    continue;
                var summary = tile.Tag as HiatmeAiClient.LateDriversDriverSummary;
                if (summary == null || string.IsNullOrWhiteSpace(summary.Driver))
                    continue;
                if (LateDriversDriverNamesMatch(summary.Driver, driver))
                    return tile;
            }
            return null;
        }

        private static Point LateDriversControlOnForm(Control control, Control form)
        {
            Point origin = Point.Empty;
            for (Control n = control; n != null && n != form; n = n.Parent)
                origin.Offset(n.Left, n.Top);
            return origin;
        }

        private void UpdateLateDriversFreeEdges()
        {
            if (IsDisposed || tabPageLateDrivers == null || tabPageLateDrivers.IsDisposed)
                return;
            if (ldDriverPrevBtn == null || ldDriverNextBtn == null)
                return;
            if (_ldFreeEdgeUpdating)
                return;
            if (ldDriverCaptionLbl != null && !ldDriverCaptionLbl.IsDisposed)
                ldDriverCaptionLbl.Padding = Padding.Empty;
            EnsureLateDriversFreeEdgeHook();
            _ldFreeEdgeUpdating = true;
            try
            {
            UpdateLateDriversFreeEdgesCore();
            }
            finally
            {
                _ldFreeEdgeUpdating = false;
            }
        }

        private void UpdateLateDriversFreeEdgesCore()
        {
            if (ldFreeEdgeLane != null && !ldFreeEdgeLane.IsDisposed && ldFreeEdgeLane.Visible)
                ldFreeEdgeLane.Visible = false;
            if (!tabPageLateDrivers.IsHandleCreated || tabPageLateDrivers.ClientSize.Width < 80)
                return;
            var wanted = LateDriversOffscreenFreeDrivers();
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int leftSlot = 0;
            int rightSlot = 0;
            foreach (LateDriversOffscreenFree item in wanted)
            {
                if (string.IsNullOrWhiteSpace(item.Driver) || !keep.Add(item.Driver))
                    continue;
                LateDriversFreeEdge edge;
                bool created = false;
                if (!_ldFreeEdges.TryGetValue(item.Driver, out edge) || edge == null || edge.IsDisposed)
                {
                    edge = new LateDriversFreeEdge();
                    string driver = item.Driver;
                    edge.Click += (_, __) => LateDriversRevealOffscreenDriver(driver);
                    _ldFreeEdges[item.Driver] = edge;
                    created = true;
                }
                edge.Tag = item.Driver;
                int slot = item.Left ? leftSlot++ : rightSlot++;
                PlaceLateDriversFreeEdge(edge, item.Left, slot, created);
            }

            var remove = new List<string>();
            foreach (var kv in _ldFreeEdges)
            {
                if (!keep.Contains(kv.Key))
                    remove.Add(kv.Key);
            }
            foreach (string key in remove)
            {
                LateDriversFreeEdge edge;
                if (!_ldFreeEdges.TryGetValue(key, out edge))
                    continue;
                _ldFreeEdges.Remove(key);
                if (edge == null || edge.IsDisposed)
                    continue;
                if (edge.Parent != null)
                    edge.Parent.Controls.Remove(edge);
                edge.Dispose();
            }
            LateDriversQueueFreeEdgeRestack();
            SyncLateDriversHabitMarkers();
        }

        private bool LateDriversHabitsToolIsOpen()
        {
            return hiatmeTabControl != null
                && !hiatmeTabControl.IsDisposed
                && hiatmeTabControl.SelectedTab == tabPageLateDrivers;
        }

        private void SyncLateDriversHabitMarkers()
        {
            foreach (LateDriversFreeEdge edge in _ldFreeEdges.Values)
            {
                if (edge != null && !edge.IsDisposed)
                    edge.ApplyToolGate();
            }
            if (ldDriverStripHost == null || ldDriverStripHost.IsDisposed)
                return;
            bool anyStar = false;
            foreach (Control c in ldDriverStripHost.Controls)
            {
                var star = c as LateDriversHappyStar;
                if (star == null || star.IsDisposed)
                    continue;
                star.ApplyToolGate();
                if (star.IsShowing)
                    anyStar = true;
            }
            if (anyStar)
                EnsureLateDriversStarDance();
        }

        private void EnsureLateDriversFreeEdgeHook()
        {
            if (_ldFreeEdgeHooked || tabPageLateDrivers == null || tabPageLateDrivers.IsDisposed)
                return;
            _ldFreeEdgeHooked = true;
            tabPageLateDrivers.Resize += (_, __) =>
            {
                UpdateLateDriversFreeEdges();
                LateDriversQueueFreeEdgeRestack();
            };
        }

        private void LateDriversQueueFreeEdgeRestack()
        {
            if (_ldFreeEdgeRestackQueued || IsDisposed || !IsHandleCreated)
                return;
            _ldFreeEdgeRestackQueued = true;
            try
            {
                BeginInvoke(new Action(LateDriversRestackFreeEdges));
            }
            catch
            {
                _ldFreeEdgeRestackQueued = false;
            }
        }

        private void LateDriversRestackFreeEdges()
        {
            _ldFreeEdgeRestackQueued = false;
            if (IsDisposed)
                return;
            foreach (LateDriversFreeEdge edge in _ldFreeEdges.Values)
            {
                if (edge != null && !edge.IsDisposed && edge.SpriteVisible)
                    edge.RestackSprite();
            }
        }

        private void PlaceLateDriversFreeEdge(LateDriversFreeEdge edge, bool left, int slot, bool created)
        {
            if (edge == null || edge.IsDisposed || !IsHandleCreated
                || ldDriverStripHost == null || ldDriverStripHost.IsDisposed
                || !ldDriverStripHost.IsHandleCreated || ldDriverStripHost.Height < 40)
                return;
            Point origin = Point.Empty;
            for (Control n = ldDriverStripHost; n != null && n != this; n = n.Parent)
                origin.Offset(n.Left, n.Top);
            int stripW = ldDriverStripHost.ClientSize.Width;
            int stripH = ldDriverStripHost.ClientSize.Height;
            if (stripW < edge.Width + 16)
                return;
            edge.HostOn(this);
            int tileTop = origin.Y + 28;
            int trackY = tileTop + Math.Max(24, (stripH - 28) / 2);
            SupeyCard tile = LateDriversTileForDriver(edge.Tag as string);
            Point track;
            int x;
            int y;
            if (tile != null)
            {
                Point tileAt = LateDriversControlOnForm(tile, this);
                x = tileAt.X + (tile.Width - edge.Width) / 2;
                y = tileAt.Y - edge.Height + 28;
                track = new Point(tileAt.X + tile.Width / 2, tileAt.Y + Math.Min(28, tile.Height / 2));
            }
            else
            {
                track = left
                    ? new Point(origin.X - 36, trackY)
                    : new Point(origin.X + stripW + 36, trackY);
                x = left
                    ? origin.X + 8 + slot * 28
                    : origin.X + stripW - edge.Width - 8 - slot * 28;
                y = tileTop - edge.Height + 20 - slot * 18;
            }
            int leftLimit = Math.Max(8, hiatmeTabControl != null ? hiatmeTabControl.Left + 8 : 8);
            int rightLimit = ClientSize.Width - edge.Width - 8;
            if (hiatmeTabControl != null)
                rightLimit = Math.Min(rightLimit, hiatmeTabControl.Right - edge.Width - 8);
            if (x < leftLimit)
                x = leftLimit;
            if (x > rightLimit)
                x = Math.Max(leftLimit, rightLimit);
            int topLimit = hiatmeTabControl != null ? hiatmeTabControl.Top + 4 : 4;
            int bottomLimit = ClientSize.Height - edge.Height - 8;
            if (y < topLimit)
                y = topLimit;
            if (y > bottomLimit)
                y = Math.Max(topLimit, bottomLimit);
            edge.Anchor = AnchorStyles.None;
            edge.FlyTo(new Point(x, y), track, left, slot * 6);
        }

        private void LateDriversRevealOffscreenDriver(string driver)
        {
            var rows = _ldStripDrivers;
            if (rows == null || string.IsNullOrWhiteSpace(driver))
                return;
            int idx = -1;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i] != null && LateDriversDriverNamesMatch(rows[i].Driver, driver))
                {
                    idx = i;
                    break;
                }
            }
            if (idx < 0)
                return;
            int page = Math.Max(1, LateDriversDriverStripPageSize());
            int next = idx < _ldDriverScrollOffset ? idx : idx - page + 1;
            int maxOff = Math.Max(0, rows.Count - page);
            _ldDriverScrollOffset = Math.Max(0, Math.Min(maxOff, next));
            RenderLateDriversDriverStripPage();
        }

        private static void TryPlayLateDriversFreeStarSound()
        {
            try
            {
                string path = FindLateDriversFreeStarMp3();
                if (string.IsNullOrEmpty(path))
                    return;
                string fullPath = Path.GetFullPath(path);
                var playThread = new Thread(() =>
                {
                    try
                    {
                        using (var reader = new MediaFoundationReader(fullPath))
                        using (var output = new WasapiOut(AudioClientShareMode.Shared, 200))
                        using (var done = new ManualResetEvent(false))
                        {
                            output.PlaybackStopped += (_, __) =>
                            {
                                try { done.Set(); } catch (Exception) { }
                            };
                            output.Init(reader);
                            output.Play();
                            int waitMs = (int)Math.Ceiling(reader.TotalTime.TotalMilliseconds) + 500;
                            if (waitMs < 800)
                                waitMs = 800;
                            if (waitMs > 20000)
                                waitMs = 20000;
                            done.WaitOne(waitMs);
                        }
                    }
                    catch (Exception)
                    {
                    }
                });
                playThread.IsBackground = true;
                playThread.Name = "LateDriversFreeStar";
                playThread.SetApartmentState(ApartmentState.STA);
                playThread.Start();
            }
            catch (Exception)
            {
            }
        }

        private static string FindLateDriversFreeStarMp3()
        {
            var dirs = new List<string>();
            string baseDir = AppDomain.CurrentDomain.BaseDirectory ?? "";
            if (!string.IsNullOrEmpty(baseDir))
            {
                dirs.Add(Path.Combine(baseDir, "Resources", "free-star"));
                DirectoryInfo bin = Directory.GetParent(baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                DirectoryInfo project = bin == null ? null : bin.Parent;
                if (project != null)
                    dirs.Add(Path.Combine(project.FullName, "Resources", "free-star"));
            }
            foreach (string dir in dirs)
            {
                if (!Directory.Exists(dir))
                    continue;
                string[] files = Directory.GetFiles(dir, "*.mp3");
                if (files.Length == 0)
                    continue;
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                return files[0];
            }
            return null;
        }

        private sealed class LateDriversFreeEdge : Control
        {
            private const float Cx = 50f;
            private const float Cy = 50f;
            private const float RingR = 23f;
            private const float ArrowGap = 5f;
            private const float ArrowLen = 16f;
            private const float ArrowHalf = 11f;

            private readonly System.Windows.Forms.Timer _timer;
            private readonly LateDriversLayeredSprite _sprite = new LateDriversLayeredSprite();
            private Form _host;
            private Point _pos;
            private float _angle = -90f;
            private float _target = 180f;
            private float _phase = 2f;
            private float _clock;
            private float _drawScale = 1f;
            private int _delay;
            private Point _spot;
            private PointF _anchor;
            private Point _track;
            private bool _launched;
            private bool _wanted;
            // Driver Habits markers hide when that tab is closed. A suite tracker stays up
            // once this marker exists, which is only after its target is known.
            private bool _suiteWide;
            private float _spin;
            private float _beat;

            public LateDriversFreeEdge()
            {
                Size = new Size(100, 100);
                TabStop = false;
                _sprite.Click += (_, e) => OnClick(e);
                _sprite.Cursor = Cursors.Hand;
                _timer = new System.Windows.Forms.Timer { Interval = 32 };
                _timer.Tick += (_, __) => Step();
            }

            public bool SpriteVisible
            {
                get { return _sprite != null && !_sprite.IsDisposed && _sprite.Visible; }
            }

            public void HostOn(Form form)
            {
                _host = form;
                if (form == null || form.IsDisposed || !form.IsHandleCreated || _sprite.IsDisposed)
                    return;
                if (_sprite.Owner != form)
                    _sprite.Show(form);
            }

            public void RestackSprite()
            {
                if (_sprite != null && !_sprite.IsDisposed && _sprite.Visible)
                    _sprite.BringToFront();
            }

            public void UseAcrossSuite()
            {
                _suiteWide = true;
                ApplyToolGate();
            }

            public void ApplyToolGate()
            {
                if (_sprite == null || _sprite.IsDisposed)
                    return;
                bool was = _sprite.Visible;
                _sprite.Visible = _wanted && _host != null && MarkerAllowed();
                if (_sprite.Visible && !was)
                    PresentFrame();
            }

            protected override void SetVisibleCore(bool value)
            {
                base.SetVisibleCore(false);
                _wanted = value;
                ApplyToolGate();
            }

            private bool MarkerAllowed()
            {
                if (_suiteWide)
                    return true;
                var form = _host as Form1;
                return form != null && form.LateDriversHabitsToolIsOpen();
            }

            public void PointLeft(bool left)
            {
                _target = left ? 180f : 0f;
                if (!_timer.Enabled)
                    _timer.Start();
            }

            public void FlyTo(Point spot, Point track, bool left, int staggerFrames)
            {
                _spot = spot;
                _track = track;
                PointLeft(left);
                if (!_launched)
                {
                    _launched = true;
                    _delay = staggerFrames;
                    _phase = 0f;
                    _drawScale = 0.45f;
                    _anchor = new PointF(spot.X, -Height - 24);
                    _pos = new Point(spot.X, -Height - 24);
                    Visible = true;
                    if (MarkerAllowed())
                        TryPlayLateDriversFreeStarSound();
                }
                if (!_timer.Enabled)
                    _timer.Start();
                RestackSprite();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _timer.Stop();
                    _timer.Dispose();
                    if (_sprite != null && !_sprite.IsDisposed)
                        _sprite.Dispose();
                }
                base.Dispose(disposing);
            }

            private void Step()
            {
                AdvanceFlight();
                AimAtDriver();
                _spin = (_spin + 5f) % 360f;
                _beat += 0.28f;
                PresentFrame();
            }

            private void AimAtDriver()
            {
                float dx = _track.X - (_pos.X + Width / 2f);
                float dy = _track.Y - (_pos.Y + Height / 2f);
                if (dx * dx + dy * dy < 4f)
                    return;
                _angle = (float)(Math.Atan2(dy, dx) * 180.0 / Math.PI);
            }

            private void AdvanceFlight()
            {
                if (_delay > 0)
                {
                    _delay--;
                    return;
                }
                _clock += 1f;
                if (_phase < 1f)
                {
                    _phase += 0.035f;
                    float t = 1f - (float)Math.Pow(1f - Math.Min(1f, _phase), 3);
                    float wobble = (float)Math.Sin(_clock * 0.65) * 10f * (1f - t);
                    int y = (int)(-Height - 24 + (_spot.Y + Height + 24) * t);
                    int x = _spot.X + (int)wobble;
                    _anchor = new PointF(x, y);
                    _pos = new Point(x, y);
                    _drawScale = 0.45f + 0.55f * t;
                    if (_phase >= 1f)
                        _phase = 2f;
                    return;
                }
                DriftAnchorToSpot();
                int floatX = (int)(Math.Sin(_clock * 0.09) * 9);
                int floatY = (int)(Math.Sin(_clock * 0.13 + 0.8) * 14);
                _pos = new Point((int)Math.Round(_anchor.X) + floatX, (int)Math.Round(_anchor.Y) + floatY);
                _drawScale = 1f + (float)Math.Sin(_clock * 0.18) * 0.05f;
            }

            private void DriftAnchorToSpot()
            {
                float dx = _spot.X - _anchor.X;
                float dy = _spot.Y - _anchor.Y;
                if (dx * dx + dy * dy < 2.5f)
                {
                    _anchor = new PointF(_spot.X, _spot.Y);
                    return;
                }
                _anchor.X += dx * 0.16f;
                _anchor.Y += dy * 0.16f;
            }

            private void PresentFrame()
            {
                if (_host == null || _host.IsDisposed || !_host.IsHandleCreated || _sprite.IsDisposed || !_sprite.Visible)
                    return;
                using (var bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    PaintGlyph(g);
                    _sprite.Present(bmp, _host.PointToScreen(_pos));
                }
            }

            private GraphicsPath GlyphPath()
            {
                var path = new GraphicsPath { FillMode = FillMode.Alternate };
                path.AddEllipse(Cx - RingR, Cy - RingR, RingR * 2f, RingR * 2f);
                float inner = RingR - 7f;
                path.AddEllipse(Cx - inner, Cy - inner, inner * 2f, inner * 2f);
                double rad = _angle * Math.PI / 180.0;
                float c = (float)Math.Cos(rad);
                float s = (float)Math.Sin(rad);
                float px = -s;
                float py = c;
                float baseD = RingR + ArrowGap;
                float tipD = baseD + ArrowLen;
                path.AddPolygon(new[]
                {
                    new PointF(Cx + c * baseD + px * ArrowHalf, Cy + s * baseD + py * ArrowHalf),
                    new PointF(Cx + c * tipD, Cy + s * tipD),
                    new PointF(Cx + c * baseD - px * ArrowHalf, Cy + s * baseD - py * ArrowHalf),
                });
                return path;
            }

            private GraphicsPath StarGlyph(float radius)
            {
                float bob = (float)Math.Sin(_beat) * 1.6f;
                float pulse = 0.82f + 0.18f * (float)(0.5 + 0.5 * Math.Sin(_beat * 2));
                float rot = _spin + (float)Math.Sin(_beat) * 18f;
                var path = new GraphicsPath();
                var pts = new PointF[10];
                for (int i = 0; i < 10; i++)
                {
                    double ang = -Math.PI / 2 + i * Math.PI / 5;
                    float r = (i % 2 == 0) ? radius : radius * 0.42f;
                    pts[i] = new PointF((float)Math.Cos(ang) * r, (float)Math.Sin(ang) * r);
                }
                path.AddPolygon(pts);
                using (var m = new Matrix())
                {
                    m.Translate(Cx, Cy + bob);
                    m.Rotate(rot);
                    m.Scale(pulse, pulse);
                    path.Transform(m);
                }
                return path;
            }

            private void PaintGlyph(Graphics g)
            {
                var state = g.Save();
                if (_drawScale < 0.995f)
                {
                    g.TranslateTransform(Cx, Cy);
                    g.ScaleTransform(_drawScale, _drawScale);
                    g.TranslateTransform(-Cx, -Cy);
                }
                using (GraphicsPath path = GlyphPath())
                using (var brush = new SolidBrush(Color.FromArgb(255, 196, 48)))
                    g.FillPath(brush, path);
                using (GraphicsPath star = StarGlyph(10f))
                using (var glow = new SolidBrush(Color.FromArgb(255, 196, 48)))
                    g.FillPath(glow, star);
                using (GraphicsPath core = StarGlyph(4.5f))
                using (var brush = new SolidBrush(Color.FromArgb(255, 244, 180)))
                    g.FillPath(brush, core);
                g.Restore(state);
            }
        }

        private bool LateDriversIsDoneForDay(string driver)
        {
            if (_ldDoneForDay.Contains(driver))
                return true;
            foreach (string name in _ldDoneForDay)
            {
                if (LateDriversDriverNamesMatch(name, driver))
                    return true;
            }
            return false;
        }

        private static void LateDriversSetTileTip(Label mins, string text)
        {
            try
            {
                var existing = mins.Tag as ToolTip;
                if (existing == null)
                {
                    existing = new ToolTip();
                    mins.Tag = existing;
                    mins.Disposed += (_, __) => { try { existing.Dispose(); } catch { } };
                }
                existing.SetToolTip(mins, text ?? "");
            }
            catch { }
        }

        private void EnsureLateDriversStarDance()
        {
            if (_ldStarDance == null)
            {
                _ldStarDance = new System.Windows.Forms.Timer { Interval = 40 };
                _ldStarDance.Tick += (_, __) =>
                {
                    bool any = false;
                    if (ldDriverStripHost != null && !ldDriverStripHost.IsDisposed)
                    {
                        foreach (Control c in ldDriverStripHost.Controls)
                        {
                            var dancing = c as LateDriversHappyStar;
                            if (dancing == null || dancing.IsDisposed || !dancing.IsShowing)
                                continue;
                            any = true;
                            dancing.Step();
                        }
                    }
                    if (!any)
                        _ldStarDance.Stop();
                };
            }
            if (!_ldStarDance.Enabled)
                _ldStarDance.Start();
        }

        private void LateDriversSetBulletHoles(SupeyCard tile, bool on, string driver)
        {
            if (tile == null || tile.IsDisposed)
                return;
            var holes = tile.Controls["ldBulletHoles"] as LateDriversBulletHoles;
            if (!on)
            {
                if (holes == null)
                    return;
                tile.Controls.Remove(holes);
                holes.Dispose();
                return;
            }

            int seed = 17;
            if (!string.IsNullOrEmpty(driver))
            {
                foreach (char c in driver)
                    seed = unchecked(seed * 31 + c);
            }
            if (holes == null)
            {
                holes = new LateDriversBulletHoles
                {
                    Name = "ldBulletHoles",
                    Seed = seed,
                };
                holes.Click += (_, __) =>
                {
                    var card = holes.Parent as SupeyCard;
                    var chosen = card?.Tag as HiatmeAiClient.LateDriversDriverSummary;
                    SelectLateDriversDriver(chosen?.Driver, focusTripNo: null);
                };
                tile.Controls.Add(holes);
            }
            else if (holes.Seed != seed)
            {
                holes.Seed = seed;
            }
            holes.Bounds = tile.ClientRectangle;
            holes.BringToFront();
        }

        private sealed class LateDriversBulletHoles : Control
        {
            private sealed class HoleMark
            {
                public float X, Y, R, Spin, DripLen, DripLean;
                public float[] Wobble;
            }

            private sealed class BloodSpot
            {
                public float X, Y, R, Spray;
                public float[] Wobble;
                public float[] Spike;
            }

            private static readonly Color Blood = Color.FromArgb(186, 22, 30);
            private static readonly Color BloodDark = Color.FromArgb(110, 8, 14);
            private static readonly Color BloodWet = Color.FromArgb(186, 42, 48);

            private static readonly float[,] Slots =
            {
                { 0.22f, 0.38f },
                { 0.78f, 0.28f },
                { 0.46f, 0.78f },
                { 0.84f, 0.72f },
            };

            private int _seed;
            private HoleMark[] _marks = new HoleMark[0];
            private BloodSpot[] _spots = new BloodSpot[0];

            public LateDriversBulletHoles()
            {
                SetStyle(
                    ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.UserPaint
                    | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.Opaque,
                    true);
                BackColor = Color.FromArgb(6, 6, 7);
                TabStop = false;
            }

            public int Seed
            {
                get => _seed;
                set
                {
                    _seed = value;
                    ApplyShape();
                    Invalidate();
                }
            }

            protected override void OnSizeChanged(EventArgs e)
            {
                base.OnSizeChanged(e);
                ApplyShape();
            }

            private void ApplyShape()
            {
                if (Width < 20 || Height < 20)
                    return;
                _marks = new HoleMark[Slots.GetLength(0)];
                using (var path = new GraphicsPath())
                {
                    for (int i = 0; i < _marks.Length; i++)
                    {
                        var rng = new Random(_seed + 19 * (i + 1));
                        float r = 7.2f + (float)rng.NextDouble() * 1.8f;
                        float jx = ((float)rng.NextDouble() - 0.5f) * 10f;
                        float jy = ((float)rng.NextDouble() - 0.5f) * 8f;
                        var mark = new HoleMark
                        {
                            R = r,
                            X = Math.Max(r * 1.5f, Math.Min(Width - r * 1.5f, Slots[i, 0] * Width + jx)),
                            Y = Math.Max(r * 1.5f, Math.Min(Height - r * 1.5f, Slots[i, 1] * Height + jy)),
                            Spin = (float)(rng.NextDouble() * Math.PI * 2),
                            Wobble = Wobble(rng),
                        };
                        _marks[i] = mark;
                        using (GraphicsPath local = HoleGeometry(mark))
                            path.AddPath(local, false);
                    }
                    int drips = 0;
                    for (int i = 0; i < _marks.Length; i++)
                    {
                        HoleMark mark = _marks[i];
                        float startY = mark.Y + mark.R * 0.55f;
                        float room = Height - startY - 8f;
                        if (room < 16f || drips >= 2)
                            continue;
                        var rng = new Random(_seed + 41 * (i + 1));
                        mark.DripLen = Math.Min(room, 20f + (float)rng.NextDouble() * 16f);
                        mark.DripLean = ((float)rng.NextDouble() - 0.5f) * 12f;
                        drips++;
                        using (GraphicsPath drip = DripGeometry(mark))
                            path.AddPath(drip, false);
                    }
                    _spots = BuildSpots();
                    foreach (BloodSpot spot in _spots)
                    {
                        using (GraphicsPath spotPath = SpotGeometry(spot))
                            path.AddPath(spotPath, false);
                    }
                    Region previous = Region;
                    Region = new Region(path);
                    previous?.Dispose();
                }
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                foreach (HoleMark mark in _marks)
                    PaintHole(g, mark);
                PaintBlood(g);
            }

            private static float[] Wobble(Random rng)
            {
                var wobble = new float[12];
                for (int i = 0; i < wobble.Length; i++)
                    wobble[i] = 0.9f + (float)rng.NextDouble() * 0.18f;
                wobble[2] = 1.08f;
                wobble[7] = 0.86f;
                return wobble;
            }

            private static GraphicsPath HoleGeometry(HoleMark mark)
            {
                var local = new GraphicsPath();
                float r = mark.R;
                local.AddClosedCurve(Ring(r * 1.08f, mark.Wobble), 0.5f);
                local.AddPolygon(Burr(r, -1.05, 1.22f));
                local.AddPolygon(Burr(r, 2.2, 1.16f));
                local.AddPolygon(CrackPoly(r));
                using (var m = new Matrix())
                {
                    m.Translate(mark.X, mark.Y);
                    m.Rotate(mark.Spin * 180f / (float)Math.PI);
                    local.Transform(m);
                }
                return local;
            }

            private static void PaintHole(Graphics g, HoleMark mark)
            {
                var state = g.Save();
                g.TranslateTransform(mark.X, mark.Y);
                g.RotateTransform(mark.Spin * 180f / (float)Math.PI);
                float r = mark.R;
                PointF[] crater = Ring(r * 1.08f, mark.Wobble);
                PointF[] mouth = Ring(r * 0.9f, mark.Wobble);
                using (var path = new GraphicsPath())
                {
                    path.AddClosedCurve(crater, 0.5f);
                    using (var dent = new SolidBrush(Color.FromArgb(28, 28, 30)))
                        g.FillPath(dent, path);
                }
                using (var path = new GraphicsPath())
                {
                    path.AddClosedCurve(mouth, 0.5f);
                    using (var hole = new SolidBrush(Color.FromArgb(2, 2, 3)))
                        g.FillPath(hole, path);
                }
                using (var path = new GraphicsPath())
                {
                    path.AddClosedCurve(Shift(mouth, r * 0.07f, r * 0.08f, 0.92f), 0.5f);
                    using (var wall = new SolidBrush(Color.FromArgb(36, 36, 38)))
                        g.FillPath(wall, path);
                }
                using (var path = new GraphicsPath())
                {
                    path.AddClosedCurve(Shift(mouth, -r * 0.02f, -r * 0.02f, 0.78f), 0.5f);
                    using (var hole = new SolidBrush(Color.FromArgb(1, 1, 2)))
                        g.FillPath(hole, path);
                }
                using (var pen = new Pen(Color.FromArgb(170, 174, 180), 1.15f))
                    g.DrawArc(pen, -r * 0.92f, -r * 0.92f, r * 1.84f, r * 1.84f, 200f, 70f);
                FillBurr(g, r, -1.05, 1.22f, true);
                FillBurr(g, r, 2.2, 1.16f, false);
                using (var pen = new Pen(Color.FromArgb(8, 8, 10), 1f))
                    g.DrawLine(pen, Polar(0.55, r * 0.95f), Polar(0.55, r * 1.35f));
                g.Restore(state);
            }

            private static PointF[] Shift(PointF[] pts, float dx, float dy, float scale)
            {
                var next = new PointF[pts.Length];
                for (int i = 0; i < pts.Length; i++)
                    next[i] = new PointF(pts[i].X * scale + dx, pts[i].Y * scale + dy);
                return next;
            }

            private static PointF[] Ring(float radius, float[] wobble)
            {
                var pts = new PointF[wobble.Length];
                for (int i = 0; i < wobble.Length; i++)
                {
                    double ang = i * Math.PI * 2 / wobble.Length;
                    pts[i] = new PointF((float)Math.Cos(ang) * radius * wobble[i], (float)Math.Sin(ang) * radius * wobble[i]);
                }
                return pts;
            }

            private static PointF[] Burr(float r, double ang, float reach)
            {
                return new[]
                {
                    Polar(ang - 0.16, r * 0.9f),
                    Polar(ang - 0.02, r * reach),
                    Polar(ang + 0.14, r * (reach - 0.08f)),
                    Polar(ang + 0.16, r * 0.88f),
                };
            }

            private static PointF[] CrackPoly(float r)
            {
                PointF a = Polar(0.55, r * 0.86f);
                PointF b = Polar(0.55, r * 1.35f);
                float px = -(float)Math.Sin(0.55) * 0.6f;
                float py = (float)Math.Cos(0.55) * 0.6f;
                return new[]
                {
                    new PointF(a.X + px, a.Y + py),
                    new PointF(b.X + px * 0.3f, b.Y + py * 0.3f),
                    new PointF(b.X - px * 0.3f, b.Y - py * 0.3f),
                    new PointF(a.X - px, a.Y - py),
                };
            }

            private static void FillBurr(Graphics g, float r, double ang, float reach, bool lit)
            {
                using (var brush = new SolidBrush(lit ? Color.FromArgb(132, 136, 142) : Color.FromArgb(58, 60, 64)))
                    g.FillPolygon(brush, Burr(r, ang, reach));
            }

            private static PointF Polar(double ang, float radius)
            {
                return new PointF((float)Math.Cos(ang) * radius, (float)Math.Sin(ang) * radius);
            }

            private static readonly float[,] SplatterSlots =
            {
                { 0.28f, 0.36f },
                { 0.76f, 0.3f },
                { 0.62f, 0.74f },
            };

            private BloodSpot[] BuildSpots()
            {
                var spots = new BloodSpot[SplatterSlots.GetLength(0)];
                for (int i = 0; i < spots.Length; i++)
                {
                    var rng = new Random(_seed + 91 * (i + 1));
                    float r = 6.4f + (float)rng.NextDouble() * 2.4f;
                    float jx = ((float)rng.NextDouble() - 0.5f) * 16f;
                    float jy = ((float)rng.NextDouble() - 0.5f) * 12f;
                    var wobble = new float[11];
                    for (int w = 0; w < wobble.Length; w++)
                        wobble[w] = 0.75f + (float)rng.NextDouble() * 0.5f;
                    var spike = new float[8];
                    for (int s = 0; s < spike.Length; s++)
                        spike[s] = s < 5
                            ? 1.55f + (float)rng.NextDouble() * 1.15f
                            : 1.05f + (float)rng.NextDouble() * 0.35f;
                    spots[i] = new BloodSpot
                    {
                        R = r,
                        X = Math.Max(28f, Math.Min(Width - 28f, SplatterSlots[i, 0] * Width + jx)),
                        Y = Math.Max(22f, Math.Min(Height - 22f, SplatterSlots[i, 1] * Height + jy)),
                        Spray = (float)(rng.NextDouble() * 360),
                        Wobble = wobble,
                        Spike = spike,
                    };
                }
                return spots;
            }

            private static GraphicsPath DripGeometry(HoleMark mark)
            {
                float x = mark.X;
                float y = mark.Y + mark.R * 0.55f;
                float x2 = x + mark.DripLean;
                float y2 = y + mark.DripLen;
                var path = new GraphicsPath();
                path.AddPolygon(DripRibbon(x, y, mark.DripLen, mark.DripLean));
                path.AddEllipse(x - 3f, y - 2f, 6f, 4f);
                path.AddEllipse(x2 - 3.4f, y2 - 4.6f, 6.6f, 7.6f);
                return path;
            }

            private static PointF[] DripRibbon(float x, float y, float len, float lean)
            {
                const int n = 7;
                var poly = new PointF[n * 2];
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)(n - 1);
                    float cx = x + lean * t + (float)Math.Sin(t * Math.PI) * lean * 0.35f;
                    float cy = y + len * t;
                    float w = 2.15f * (1f - t) + 0.45f;
                    poly[i] = new PointF(cx - w, cy);
                    poly[n * 2 - 1 - i] = new PointF(cx + w * 0.75f, cy);
                }
                return poly;
            }

            private static GraphicsPath SpotGeometry(BloodSpot spot)
            {
                GraphicsPath local = SplatterLocal(spot);
                using (var m = new Matrix())
                {
                    m.Translate(spot.X, spot.Y);
                    m.Rotate(spot.Spray);
                    local.Transform(m);
                }
                return local;
            }

            private static GraphicsPath SplatterLocal(BloodSpot spot)
            {
                float r = spot.R;
                var path = new GraphicsPath();
                path.AddClosedCurve(SplatterRing(r, spot.Wobble), 0.35f);
                for (int i = 0; i < spot.Spike.Length; i++)
                {
                    double ang = i < 5
                        ? (i - 2) * 0.42
                        : Math.PI + (i - 6) * 0.9;
                    float len = r * spot.Spike[i];
                    path.AddPolygon(new[]
                    {
                        Polar(ang - 0.16, r * 0.78f),
                        Polar(ang, len),
                        Polar(ang + 0.16, r * 0.78f),
                    });
                }
                for (int i = 0; i < 6; i++)
                {
                    float dist = r * (1.85f + i * 0.48f);
                    float side = (i % 2 == 0 ? -1f : 1f) * r * (0.12f + 0.1f * (i % 3));
                    float rad = Math.Max(0.9f, r * (0.32f - i * 0.035f));
                    path.AddEllipse(dist - rad, side - rad * 0.65f, rad * 2f, rad * 1.3f);
                }
                return path;
            }

            private static PointF[] SplatterRing(float radius, float[] wobble)
            {
                var pts = new PointF[wobble.Length];
                for (int i = 0; i < wobble.Length; i++)
                {
                    double ang = i * Math.PI * 2 / wobble.Length;
                    pts[i] = new PointF(
                        (float)Math.Cos(ang) * radius * wobble[i],
                        (float)Math.Sin(ang) * radius * wobble[i]);
                }
                return pts;
            }

            private void PaintBlood(Graphics g)
            {
                foreach (BloodSpot spot in _spots)
                {
                    using (GraphicsPath path = SpotGeometry(spot))
                    using (var brush = new SolidBrush(Blood))
                        g.FillPath(brush, path);
                    var state = g.Save();
                    g.TranslateTransform(spot.X, spot.Y);
                    g.RotateTransform(spot.Spray);
                    using (var path = new GraphicsPath())
                    {
                        path.AddClosedCurve(SplatterRing(spot.R * 0.62f, spot.Wobble), 0.4f);
                        using (var brush = new SolidBrush(BloodDark))
                            g.FillPath(brush, path);
                    }
                    g.Restore(state);
                }
                foreach (HoleMark mark in _marks)
                {
                    if (mark.DripLen <= 0f)
                        continue;
                    float x = mark.X;
                    float y = mark.Y + mark.R * 0.55f;
                    float x2 = x + mark.DripLean;
                    float y2 = y + mark.DripLen;
                    using (var brush = new SolidBrush(Blood))
                    {
                        g.FillPolygon(brush, DripRibbon(x, y, mark.DripLen, mark.DripLean));
                        g.FillEllipse(brush, x - 3f, y - 2f, 6f, 4f);
                        g.FillEllipse(brush, x2 - 2.4f, y2 - 3.8f, 4.6f, 5.6f);
                    }
                    using (var brush = new SolidBrush(BloodDark))
                        g.FillEllipse(brush, x2 - 3.4f, y2 - 4.6f, 6.6f, 7.6f);
                    using (var brush = new SolidBrush(Blood))
                        g.FillEllipse(brush, x2 - 2.4f, y2 - 3.8f, 4.6f, 5.6f);
                    using (var brush = new SolidBrush(BloodWet))
                        g.FillEllipse(brush, x2 - 1.1f, y2 - 2.4f, 1.8f, 2.2f);
                }
            }
        }

        private void DisposeLateDriversStars()
        {
            if (ldDriverStripHost == null || ldDriverStripHost.IsDisposed)
                return;
            var stars = new List<Control>();
            foreach (Control c in ldDriverStripHost.Controls)
            {
                if (c is LateDriversHappyStar)
                    stars.Add(c);
            }
            foreach (Control star in stars)
            {
                ldDriverStripHost.Controls.Remove(star);
                star.Dispose();
            }
        }

        private LateDriversHappyStar LateDriversStarFor(SupeyCard tile)
        {
            if (tile == null || tile.IsDisposed)
                return null;
            var onTile = tile.Controls["ldTileStar"] as LateDriversHappyStar;
            if (onTile != null && !onTile.IsDisposed)
                return onTile;
            if (ldDriverStripHost == null || ldDriverStripHost.IsDisposed)
                return null;
            foreach (Control c in ldDriverStripHost.Controls)
            {
                var star = c as LateDriversHappyStar;
                if (star != null && !star.IsDisposed && ReferenceEquals(star.Tag, tile))
                    return star;
            }
            return null;
        }

        private void PlaceLateDriversStar(SupeyCard tile, LateDriversHappyStar star)
        {
            if (tile == null || tile.IsDisposed || star == null || star.IsDisposed)
                return;
            if (ldDriverStripHost == null || ldDriverStripHost.IsDisposed)
                return;
            star.Tag = tile;
            if (!ReferenceEquals(star.Parent, ldDriverStripHost))
            {
                star.Parent?.Controls.Remove(star);
                ldDriverStripHost.Controls.Add(star);
            }
            if (!tile.IsHandleCreated || !ldDriverStripHost.IsHandleCreated)
                return;
            Point corner = ldDriverStripHost.PointToClient(tile.PointToScreen(Point.Empty));
            // HUD corner is a 14px cut. Sit the star on that angled line.
            const int overCut = 8;
            star.Location = new Point(
                corner.X - star.Width / 2 + overCut,
                corner.Y - star.Height / 2 + overCut);
            star.SetShowing(true);
        }

        private void RepositionLateDriversStars()
        {
            if (_ldDriverTiles == null)
                return;
            foreach (SupeyCard tile in _ldDriverTiles)
            {
                LateDriversHappyStar star = LateDriversStarFor(tile);
                if (star == null || star.IsDisposed || !star.IsShowing)
                    continue;
                PlaceLateDriversStar(tile, star);
            }
        }

        private sealed class LateDriversHappyStar : Control
        {
            private float _spin;
            private float _beat;

            private readonly LateDriversLayeredSprite _sprite = new LateDriversLayeredSprite();

            public LateDriversHappyStar()
            {
                Size = new Size(40, 40);
                TabStop = false;
                _sprite.Click += (_, e) => OnClick(e);
                _sprite.Cursor = Cursors.Hand;
                Visible = false;
            }

            private bool _wanted;

            public bool IsShowing
            {
                get { return _sprite != null && !_sprite.IsDisposed && _sprite.Visible; }
            }

            public void SetShowing(bool on)
            {
                _wanted = on;
                EnsureSpriteHost();
                ApplyToolGate();
            }

            public void ApplyToolGate()
            {
                EnsureSpriteHost();
                if (_sprite == null || _sprite.IsDisposed)
                    return;
                var form = FindForm() as Form1;
                bool habitsOpen = form != null && form.LateDriversHabitsToolIsOpen();
                bool was = _sprite.Visible;
                _sprite.Visible = _wanted && Parent != null && habitsOpen;
                if (_sprite.Visible && !was)
                    PresentFrame();
            }

            public void Step()
            {
                _spin = (_spin + 5f) % 360f;
                _beat += 0.28f;
                PresentFrame();
            }

            protected override void SetVisibleCore(bool value)
            {
                base.SetVisibleCore(false);
                SetShowing(value);
            }

            protected override void OnLocationChanged(EventArgs e)
            {
                base.OnLocationChanged(e);
                PresentFrame();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing && _sprite != null && !_sprite.IsDisposed)
                    _sprite.Dispose();
                base.Dispose(disposing);
            }

            private void EnsureSpriteHost()
            {
                Form form = FindForm();
                if (form == null || form.IsDisposed || !form.IsHandleCreated || _sprite.IsDisposed)
                    return;
                if (_sprite.Owner != form)
                    _sprite.Show(form);
            }

            private void PresentFrame()
            {
                if (!IsShowing || Parent == null || Parent.IsDisposed || !Parent.IsHandleCreated)
                    return;
                using (var bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    using (GraphicsPath star = StarPath(13f))
                    using (var glow = new SolidBrush(Color.FromArgb(255, 196, 48)))
                        g.FillPath(glow, star);
                    using (GraphicsPath core = StarPath(13f * 0.45f))
                    using (var brush = new SolidBrush(Color.FromArgb(255, 244, 180)))
                        g.FillPath(brush, core);
                    int floatX = (int)(Math.Sin(_beat * 0.45) * 5);
                    int floatY = (int)(Math.Cos(_beat * 0.62) * 8);
                    Point at = Parent.PointToScreen(Location);
                    _sprite.Present(bmp, new Point(at.X + floatX, at.Y + floatY));
                }
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
            }

            protected override void OnPaint(PaintEventArgs e)
            {
            }

            private GraphicsPath StarPath(float radius)
            {
                float bob = (float)Math.Sin(_beat) * 1.2f;
                float pulse = 0.82f + 0.18f * (float)(0.5 + 0.5 * Math.Sin(_beat * 2));
                float rot = _spin + (float)Math.Sin(_beat) * 18f;
                var path = new GraphicsPath();
                path.AddPolygon(HappyStarPoints(radius));
                using (var m = new Matrix())
                {
                    m.Translate(Width / 2f, Height / 2f + bob);
                    m.Rotate(rot);
                    m.Scale(pulse, pulse);
                    path.Transform(m);
                }
                return path;
            }

            private static PointF[] HappyStarPoints(float radius)
            {
                var pts = new PointF[10];
                for (int i = 0; i < 10; i++)
                {
                    double ang = -Math.PI / 2 + i * Math.PI / 5;
                    float r = (i % 2 == 0) ? radius : radius * 0.42f;
                    pts[i] = new PointF((float)Math.Cos(ang) * r, (float)Math.Sin(ang) * r);
                }
                return pts;
            }
        }

        private Font _ldFreeCityFont;

        private Font LateDriversFreeCityFont()
        {
            if (_ldFreeCityFont == null)
                _ldFreeCityFont = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold);
            return _ldFreeCityFont;
        }

        private static void LateDriversLayoutDriverTile(
            Panel nameRow,
            Label nameLbl,
            Label statsLbl,
            Label mins,
            LateDriversHappyStar star,
            bool free)
        {
            if (nameRow != null)
                nameRow.Height = 22;
            if (nameLbl != null)
                nameLbl.Padding = Padding.Empty;
            if (statsLbl != null)
                statsLbl.Padding = Padding.Empty;
            if (mins != null)
                mins.TextAlign = free ? ContentAlignment.TopLeft : ContentAlignment.MiddleLeft;
            if (star != null)
                star.SetShowing(false);
        }

        private static string LateDriversNextPickupLine(string city)
        {
            city = (city ?? "").Trim();
            if (city.Length == 0)
                return "";
            city = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(city.ToLowerInvariant());
            return "\nNext pickup in " + city;
        }

        private static string LateDriversFreeLabel(int minutes)
        {
            if (minutes < 60)
                return "Free for " + minutes.ToString(CultureInfo.InvariantCulture) + "m";
            int hours = minutes / 60;
            int rest = minutes % 60;
            if (rest == 0)
                return "Free for " + hours.ToString(CultureInfo.InvariantCulture) + "h";
            return "Free for " + hours.ToString(CultureInfo.InvariantCulture) + "h "
                + rest.ToString(CultureInfo.InvariantCulture) + "m";
        }

        private sealed class LateDriversLayeredSprite : Form
        {
            public LateDriversLayeredSprite()
            {
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                ShowIcon = false;
                Visible = false;
            }

            protected override bool ShowWithoutActivation
            {
                get { return true; }
            }

            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams cp = base.CreateParams;
                    cp.ExStyle |= 0x00080000 | 0x00000080 | 0x08000000;
                    return cp;
                }
            }

            public void Present(Bitmap bitmap, Point screen)
            {
                if (bitmap == null || IsDisposed || !Visible)
                    return;
                if (!IsHandleCreated)
                    CreateHandle();
                IntPtr screenDc = GetDC(IntPtr.Zero);
                IntPtr memDc = CreateCompatibleDC(screenDc);
                IntPtr dib = IntPtr.Zero;
                IntPtr old = IntPtr.Zero;
                try
                {
                    var info = new BITMAPINFO();
                    info.biSize = Marshal.SizeOf(typeof(BITMAPINFO));
                    info.biWidth = bitmap.Width;
                    info.biHeight = -bitmap.Height;
                    info.biPlanes = 1;
                    info.biBitCount = 32;
                    IntPtr bits;
                    dib = CreateDIBSection(screenDc, ref info, 0, out bits, IntPtr.Zero, 0);
                    if (dib == IntPtr.Zero || bits == IntPtr.Zero)
                        return;
                    var data = bitmap.LockBits(
                        new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                        ImageLockMode.ReadOnly,
                        PixelFormat.Format32bppArgb);
                    try
                    {
                        int widthBytes = bitmap.Width * 4;
                        var row = new byte[widthBytes];
                        for (int y = 0; y < bitmap.Height; y++)
                        {
                            Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, widthBytes);
                            for (int x = 0; x < widthBytes; x += 4)
                            {
                                byte a = row[x + 3];
                                row[x] = (byte)(row[x] * a / 255);
                                row[x + 1] = (byte)(row[x + 1] * a / 255);
                                row[x + 2] = (byte)(row[x + 2] * a / 255);
                            }
                            Marshal.Copy(row, 0, bits + y * widthBytes, widthBytes);
                        }
                    }
                    finally
                    {
                        bitmap.UnlockBits(data);
                    }
                    old = SelectObject(memDc, dib);
                    var size = new SIZE { cx = bitmap.Width, cy = bitmap.Height };
                    var dst = new POINT { x = screen.X, y = screen.Y };
                    var src = new POINT();
                    var blend = new BLENDFUNCTION
                    {
                        BlendOp = 0,
                        SourceConstantAlpha = 255,
                        AlphaFormat = 1
                    };
                    UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, 2);
                }
                finally
                {
                    if (old != IntPtr.Zero)
                        SelectObject(memDc, old);
                    if (dib != IntPtr.Zero)
                        DeleteObject(dib);
                    DeleteDC(memDc);
                    ReleaseDC(IntPtr.Zero, screenDc);
                }
            }

            [DllImport("user32.dll", SetLastError = true)]
            private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

            [DllImport("user32.dll", SetLastError = true)]
            private static extern IntPtr GetDC(IntPtr hWnd);

            [DllImport("user32.dll")]
            private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

            [DllImport("gdi32.dll", SetLastError = true)]
            private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

            [DllImport("gdi32.dll")]
            private static extern bool DeleteDC(IntPtr hdc);

            [DllImport("gdi32.dll")]
            private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

            [DllImport("gdi32.dll")]
            private static extern bool DeleteObject(IntPtr hObject);

            [DllImport("gdi32.dll", SetLastError = true)]
            private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint usage, out IntPtr bits, IntPtr section, uint offset);

            [StructLayout(LayoutKind.Sequential)]
            private struct POINT { public int x, y; }

            [StructLayout(LayoutKind.Sequential)]
            private struct SIZE { public int cx, cy; }

            [StructLayout(LayoutKind.Sequential)]
            private struct BLENDFUNCTION
            {
                public byte BlendOp;
                public byte BlendFlags;
                public byte SourceConstantAlpha;
                public byte AlphaFormat;
            }

            [StructLayout(LayoutKind.Sequential, Pack = 1)]
            private struct BITMAPINFO
            {
                public int biSize;
                public int biWidth;
                public int biHeight;
                public short biPlanes;
                public short biBitCount;
                public int biCompression;
                public int biSizeImage;
                public int biXPelsPerMeter;
                public int biYPelsPerMeter;
                public int biClrUsed;
                public int biClrImportant;
            }
        }
    }
}
