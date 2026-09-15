using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GMap.NET;
using GMap.NET.CacheProviders;
using GMap.NET.MapProviders;
using GMap.NET.WindowsForms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// One-time global setup for GMap.NET. OpenStreetMap's tile usage policy requires apps to send
    /// a unique <c>User-Agent</c> and a <c>Referer</c>; without them OSM returns 403 "Access blocked"
    /// PNG tiles (the yellow/black warning images you see when the policy is violated). We set both,
    /// and point the tile cache at a Hiatme-specific folder so any 403 PNGs that may have been
    /// cached on the shared %LOCALAPPDATA%\GMap.NET\TileDBv5 path don't keep getting served from disk.
    ///
    /// GMap.NET's paint path downloads missing tiles with a blocking HttpWebRequest on the UI
    /// thread (5s timeout, then a retry = 10s freeze). The server desk never feels it because
    /// its tile cache is already warm. Paint is CacheOnly so the window never waits on OSM;
    /// <see cref="GMapTilePrefetch"/> downloads the visible tiles on a worker with a full
    /// timeout and refreshes the map when they land.
    /// </summary>
    internal static class GMapInitializer
    {
        private static readonly object _gate = new object();
        private static bool _initialized;

        /// <summary>Idempotent — safe to call from <c>Program.Main</c> and from each map form's constructor.</summary>
        public static void EnsureInitialized()
        {
            if (_initialized) return;
            lock (_gate)
            {
                if (_initialized) return;
                _initialized = true;

                try { WebRequest.DefaultWebProxy = null; }
                catch { /* process-wide; ignore if locked down */ }

                try
                {
                    GMapProvider.UserAgent = "HiatmeToolSuite/3.0 (+https://hiatme.app; ops@hiatme.app)";
                }
                catch { }

                try
                {
                    GMapProviders.OpenStreetMap.RefererUrl = "https://www.openstreetmap.org/";
                }
                catch { }

                try
                {
                    GMapProvider.WebProxy = null;
                    GMapProvider.TimeoutMs = 15000;
                    GMapProvider.WebRequestFactory = CreateTileRequest;
                }
                catch { }

                try
                {
                    string root = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Hiatme Tool Suite",
                        "GMapCache");
                    Directory.CreateDirectory(root);
                    var cache = new SQLitePureImageCache { CacheLocation = root };
                    GMaps.Instance.PrimaryCache = cache;
                    // Paint never hits the network. Prefetch fills this cache off-thread.
                    GMaps.Instance.Mode = AccessMode.CacheOnly;
                }
                catch { }
            }
        }

        private static WebRequest CreateTileRequest(GMapProvider provider, string url)
        {
            var req = (HttpWebRequest)WebRequest.Create(url ?? "");
            req.Proxy = null;
            req.KeepAlive = true;
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            req.Timeout = 15000;
            req.ReadWriteTimeout = 15000;
            if (!string.IsNullOrEmpty(GMapProvider.UserAgent))
                req.UserAgent = GMapProvider.UserAgent;
            if (provider != null && !string.IsNullOrEmpty(provider.RefererUrl))
                req.Referer = provider.RefererUrl;
            return req;
        }
    }

    /// <summary>
    /// Downloads the tiles currently on screen on worker threads, writes them into GMap's
    /// disk cache, then asks maps to redraw. The UI thread only reads cache.
    /// OSM's tile policy allows two connections; we use two workers and debounce pan/zoom
    /// so a drag does not enqueue hundreds of overlapping viewports.
    /// </summary>
    internal static class GMapTilePrefetch
    {
        private const int MaxWorkers = 2;
        private const int ViewDebounceMs = 120;
        private const int RefreshGapMs = 150;

        private static readonly ConcurrentQueue<PrefetchItem> Queue = new ConcurrentQueue<PrefetchItem>();
        private static readonly ConcurrentDictionary<string, byte> InFlight =
            new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        private static readonly List<WeakReference> Maps = new List<WeakReference>();
        private static int _workers;
        private static int _viewGen;
        private static int _refreshPending;
        private static int _lastRefreshTick;

        public static void Attach(GMapControl map)
        {
            if (map == null) return;
            GMapInitializer.EnsureInitialized();
            map.RetryLoadTile = 0;
            try { map.EmptyTileColor = Color.FromArgb(40, 40, 40); } catch { }
            try { map.LevelsKeepInMemory = 5; } catch { }
            lock (Maps)
                Maps.Add(new WeakReference(map));

            Action kick = () => ScheduleView(map);
            map.OnMapZoomChanged += () => kick();
            map.OnPositionChanged += _ => kick();
            map.SizeChanged += (_, __) => kick();
            map.VisibleChanged += (_, __) => kick();
            map.HandleCreated += (_, __) => kick();
            if (map.IsHandleCreated)
                kick();
        }

        private static void ScheduleView(GMapControl map)
        {
            int gen = Interlocked.Increment(ref _viewGen);
            _ = DebouncedEnqueueAsync(map, gen);
        }

        private static async Task DebouncedEnqueueAsync(GMapControl map, int gen)
        {
            try
            {
                await Task.Delay(ViewDebounceMs).ConfigureAwait(false);
                if (gen != Volatile.Read(ref _viewGen)) return;
                if (map == null || map.IsDisposed || !map.IsHandleCreated) return;
                map.BeginInvoke((Action)(() => EnqueueView(map)));
            }
            catch { }
        }

        public static void EnqueueView(GMapControl map)
        {
            if (map == null || map.IsDisposed) return;
            var provider = map.MapProvider;
            if (provider?.Projection == null) return;
            int zoom = (int)Math.Max(0, Math.Round(map.Zoom));
            RectLatLng view = map.ViewArea;
            if (view.WidthLng == 0 && view.HeightLat == 0)
                return;

            List<GPoint> tiles;
            try { tiles = provider.Projection.GetAreaTileList(view, zoom, 1); }
            catch { return; }
            if (tiles == null) return;
            foreach (var pos in tiles)
                Enqueue(provider, pos, zoom);
        }

        public static void Enqueue(GMapProvider provider, GPoint pos, int zoom)
        {
            if (provider == null) return;
            string key = provider.DbId + ":" + zoom + ":" + pos.X + ":" + pos.Y;
            if (!InFlight.TryAdd(key, 0)) return;
            Queue.Enqueue(new PrefetchItem
            {
                Key = key,
                Provider = provider,
                Pos = pos,
                Zoom = zoom,
            });
            KickWorker();
        }

        private static void KickWorker()
        {
            while (!Queue.IsEmpty)
            {
                int n = Volatile.Read(ref _workers);
                if (n >= MaxWorkers) return;
                if (Interlocked.CompareExchange(ref _workers, n + 1, n) != n) continue;
                _ = Task.Run((Action)Pump);
                return;
            }
        }

        private static void Pump()
        {
            try
            {
                PrefetchItem item;
                while (Queue.TryDequeue(out item))
                {
                    try
                    {
                        if (TryFillCache(item))
                            RequestRefresh();
                    }
                    catch { }
                    finally
                    {
                        byte gone;
                        InFlight.TryRemove(item.Key, out gone);
                    }
                }
            }
            finally
            {
                Interlocked.Decrement(ref _workers);
                KickWorker();
                FlushRefresh();
            }
        }

        private static bool TryFillCache(PrefetchItem item)
        {
            var cache = GMaps.Instance.PrimaryCache;
            if (cache != null)
            {
                try
                {
                    var hit = cache.GetImageFromCache(item.Provider.DbId, item.Pos, item.Zoom);
                    if (hit != null)
                    {
                        hit.Dispose();
                        return false;
                    }
                }
                catch { }
            }

            PureImage img = null;
            try
            {
                img = item.Provider.GetTileImage(item.Pos, item.Zoom);
                if (img?.Data == null)
                    return false;
                byte[] bytes = img.Data.ToArray();
                if (bytes == null || bytes.Length == 0)
                    return false;
                if (cache != null)
                    cache.PutImageToCache(bytes, item.Provider.DbId, item.Pos, item.Zoom);
                return true;
            }
            finally
            {
                try { img?.Dispose(); } catch { }
            }
        }

        private static void RequestRefresh()
        {
            Interlocked.Exchange(ref _refreshPending, 1);
            int now = Environment.TickCount;
            int last = Volatile.Read(ref _lastRefreshTick);
            if (unchecked(now - last) < RefreshGapMs) return;
            FlushRefresh();
        }

        private static void FlushRefresh()
        {
            if (Interlocked.Exchange(ref _refreshPending, 0) == 0) return;
            Interlocked.Exchange(ref _lastRefreshTick, Environment.TickCount);
            RefreshMaps();
        }

        private static void RefreshMaps()
        {
            List<GMapControl> live = new List<GMapControl>();
            lock (Maps)
            {
                for (int i = Maps.Count - 1; i >= 0; i--)
                {
                    var map = Maps[i].Target as GMapControl;
                    if (map == null || map.IsDisposed)
                    {
                        Maps.RemoveAt(i);
                        continue;
                    }
                    live.Add(map);
                }
            }
            foreach (var map in live)
            {
                try
                {
                    if (!map.IsHandleCreated) continue;
                    map.BeginInvoke((Action)(() =>
                    {
                        try { if (!map.IsDisposed) map.Refresh(); }
                        catch { }
                    }));
                }
                catch { }
            }
        }

        private struct PrefetchItem
        {
            public string Key;
            public GMapProvider Provider;
            public GPoint Pos;
            public int Zoom;
        }
    }
}
