using System;
using System.IO;
using GMap.NET;
using GMap.NET.CacheProviders;
using GMap.NET.MapProviders;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// One-time global setup for GMap.NET. OpenStreetMap's tile usage policy requires apps to send
    /// a unique <c>User-Agent</c> and a <c>Referer</c>; without them OSM returns 403 "Access blocked"
    /// PNG tiles (the yellow/black warning images you see when the policy is violated). We set both,
    /// and point the tile cache at a Hiatme-specific folder so any 403 PNGs that may have been
    /// cached on the shared %LOCALAPPDATA%\GMap.NET\TileDBv5 path don't keep getting served from disk.
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

                try
                {
                    // Static on the base provider — applies to every provider request.
                    GMapProvider.UserAgent = "HiatmeToolSuite/3.0 (+https://hiatme.app; ops@hiatme.app)";
                }
                catch { /* setting the UA must never break startup */ }

                try
                {
                    // Per-provider; OSM is the only tile source we use today.
                    GMapProviders.OpenStreetMap.RefererUrl = "https://www.openstreetmap.org/";
                }
                catch { /* per-provider headers are best-effort */ }

                try
                {
                    // Isolate from any other GMap-using app on the same machine: poisoned tiles in
                    // the shared cache won't be served to us, and a future "wipe my map cache" only
                    // affects Hiatme.
                    string root = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Hiatme Tool Suite",
                        "GMapCache");
                    Directory.CreateDirectory(root);
                    var cache = new SQLitePureImageCache { CacheLocation = root };
                    GMaps.Instance.PrimaryCache = cache;
                    GMaps.Instance.Mode = AccessMode.ServerAndCache;
                }
                catch { /* if the custom cache fails, GMap will fall back to its default location */ }
            }
        }
    }
}
