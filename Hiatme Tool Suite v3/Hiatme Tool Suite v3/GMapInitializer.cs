using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Windows.Forms;
using GMap.NET;
using GMap.NET.CacheProviders;
using GMap.NET.MapProviders;
using GMap.NET.WindowsForms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// One-time global setup for GMap.NET. OpenStreetMap requires a unique User-Agent and Referer
    /// or it returns 403 tiles. Cache lives under a Hiatme folder so old blocked PNGs from the
    /// shared GMap.NET cache are not reused.
    ///
    /// Tiles load on GMap's background pool (ServerAndCache). The 10s desk freeze was panel
    /// probing on the UI thread, not OSM. Do not ReloadMap from a helper — that clears the
    /// tile matrix and leaves a blank map.
    /// </summary>
    internal static class GMapInitializer
    {
        private static readonly object _gate = new object();
        private static bool _initialized;

        internal static string CacheRoot
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Hiatme Tool Suite",
                    "GMapCache");
            }
        }

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

                try { GMapImageProxy.Enable(); }
                catch { }

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
                    string root = CacheRoot;
                    Directory.CreateDirectory(root);
                    var cache = new SQLitePureImageCache { CacheLocation = root };
                    GMaps.Instance.PrimaryCache = cache;
                    GMaps.Instance.Mode = AccessMode.ServerAndCache;
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
    /// Map attach + shutdown. GMap's own tile pool paints ServerAndCache.
    /// </summary>
    internal static class GMapTilePrefetch
    {
        private static readonly List<WeakReference> Maps = new List<WeakReference>();
        private static volatile bool _stopped;

        /// <summary>
        /// Stop GMap's foreground cache-writer thread so close can finish.
        /// </summary>
        public static void Shutdown()
        {
            _stopped = true;
            Log("shutdown");
            try { GMaps.Instance.CancelTileCaching(); } catch { }
        }

        public static void Attach(GMapControl map)
        {
            if (map == null) return;
            GMapInitializer.EnsureInitialized();
            try { map.CacheLocation = GMapInitializer.CacheRoot; } catch { }
            try { map.EmptyTileColor = Color.FromArgb(40, 40, 40); } catch { }
            try { map.LevelsKeepInMemory = 5; } catch { }
            lock (Maps)
                Maps.Add(new WeakReference(map));
        }

        public static void EnqueueView(GMapControl map)
        {
            // Call sites remain. Display is ServerAndCache — GMap loads tiles itself.
        }

        public static void Enqueue(GMapProvider provider, GPoint pos, int zoom)
        {
        }

        private static void Log(string line)
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "HiatmeToolSuite");
                Directory.CreateDirectory(dir);
                File.AppendAllText(
                    Path.Combine(dir, "tile-prefetch.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + "  " + (line ?? "") + Environment.NewLine);
            }
            catch { }
        }
    }
}
