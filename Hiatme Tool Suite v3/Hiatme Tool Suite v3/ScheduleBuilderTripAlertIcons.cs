using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Loads and caches PNG alert icons from Resources/trip-alerts.</summary>
    internal static class ScheduleBuilderTripAlertIcons
    {
        private static readonly object Sync = new object();
        private static Dictionary<ScheduleBuilderTripAlertKind, Image> _sources;
        private static bool _loaded;

        /// <summary>Pre-rendered tinted icons at exact pixel size — resampling once (not per paint) keeps edges crisp.</summary>
        private static readonly Dictionary<long, Bitmap> _rendered = new Dictionary<long, Bitmap>();

        private static readonly Dictionary<ScheduleBuilderTripAlertKind, string> FileNames =
            new Dictionary<ScheduleBuilderTripAlertKind, string>
            {
                { ScheduleBuilderTripAlertKind.Date, "date.png" },
                { ScheduleBuilderTripAlertKind.Hidden, "hidden.png" },
                { ScheduleBuilderTripAlertKind.Cancelled, "cancelled.png" },
                { ScheduleBuilderTripAlertKind.Dupe, "dupe.png" },
                { ScheduleBuilderTripAlertKind.Time, "time.png" },
                { ScheduleBuilderTripAlertKind.Address, "address.png" },
                { ScheduleBuilderTripAlertKind.WcNotInReserves, "wc-not-in-reserves.png" },
                { ScheduleBuilderTripAlertKind.Mwc, "mwc.png" },
                { ScheduleBuilderTripAlertKind.Child, "child.png" },
                { ScheduleBuilderTripAlertKind.Escort, "escort.png" },
                { ScheduleBuilderTripAlertKind.Lbs, "lbs.png" },
                { ScheduleBuilderTripAlertKind.ServiceDog, "service-dog.png" },
                { ScheduleBuilderTripAlertKind.Scooter, "scooter.png" },
                { ScheduleBuilderTripAlertKind.MassTransit, "mass-transit.png" },
                { ScheduleBuilderTripAlertKind.Rerouted, "rerouted.png" },
            };

        public static void DrawBold(Graphics g, ScheduleBuilderTripAlertKind kind, Rectangle bounds, Color tint)
        {
            if (g == null || bounds.Width <= 0 || bounds.Height <= 0)
                return;

            int size = Math.Min(bounds.Width, bounds.Height);
            Bitmap bmp = GetRenderedIcon(kind, size, tint);
            if (bmp == null)
            {
                DrawFallbackGlyph(g, kind, bounds, tint);
                return;
            }

            // 1:1 pixel blit — no half-pixel resample that softens the glyph.
            InterpolationMode prevInterp = g.InterpolationMode;
            PixelOffsetMode prevPixel = g.PixelOffsetMode;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(
                bmp,
                new Rectangle(bounds.X, bounds.Y, bmp.Width, bmp.Height),
                0,
                0,
                bmp.Width,
                bmp.Height,
                GraphicsUnit.Pixel);
            g.InterpolationMode = prevInterp;
            g.PixelOffsetMode = prevPixel;
        }

        private static long RenderCacheKey(ScheduleBuilderTripAlertKind kind, int sizePx, Color tint)
        {
            return ((long)(int)kind << 40) | ((long)(sizePx & 0xFF) << 32) | (uint)tint.ToArgb();
        }

        private static Bitmap GetRenderedIcon(ScheduleBuilderTripAlertKind kind, int sizePx, Color tint)
        {
            if (sizePx <= 0)
                return null;

            EnsureLoaded();
            if (!_sources.TryGetValue(kind, out Image source) || source == null)
                return null;

            long key = RenderCacheKey(kind, sizePx, tint);
            lock (Sync)
            {
                if (_rendered.TryGetValue(key, out Bitmap cached))
                    return cached;
            }

            var bmp = new Bitmap(sizePx, sizePx, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            using (var attributes = new ImageAttributes())
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;

                // White silhouette → tint; alpha ×1.3 firms up anti-aliased edges so thin glyphs stay visible.
                var matrix = new ColorMatrix(new[]
                {
                    new[] { 0f, 0f, 0f, 0f, 0f },
                    new[] { 0f, 0f, 0f, 0f, 0f },
                    new[] { 0f, 0f, 0f, 0f, 0f },
                    new[] { 0f, 0f, 0f, 1.3f, 0f },
                    new[] { tint.R / 255f, tint.G / 255f, tint.B / 255f, 0f, 1f },
                });
                attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                g.DrawImage(
                    source,
                    new Rectangle(0, 0, sizePx, sizePx),
                    0,
                    0,
                    source.Width,
                    source.Height,
                    GraphicsUnit.Pixel,
                    attributes);
            }

            lock (Sync)
            {
                if (_rendered.TryGetValue(key, out Bitmap raced))
                {
                    bmp.Dispose();
                    return raced;
                }
                _rendered[key] = bmp;
            }
            return bmp;
        }

        public static bool DrawTinted(Graphics g, ScheduleBuilderTripAlertKind kind, Rectangle bounds, Color tint)
        {
            if (g == null || bounds.Width <= 0 || bounds.Height <= 0)
                return false;

            EnsureLoaded();
            if (!_sources.TryGetValue(kind, out Image source) || source == null)
                return false;

            var matrix = new ColorMatrix(new[]
            {
                new[] { 0f, 0f, 0f, 0f, 0f },
                new[] { 0f, 0f, 0f, 0f, 0f },
                new[] { 0f, 0f, 0f, 0f, 0f },
                new[] { 0f, 0f, 0f, 1f, 0f },
                new[] { tint.R / 255f, tint.G / 255f, tint.B / 255f, 0f, 1f },
            });

            using (var attributes = new ImageAttributes())
            {
                attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                g.DrawImage(
                    source,
                    bounds,
                    0,
                    0,
                    source.Width,
                    source.Height,
                    GraphicsUnit.Pixel,
                    attributes);
            }

            return true;
        }

        private static void DrawFallbackGlyph(Graphics g, ScheduleBuilderTripAlertKind kind, Rectangle bounds, Color tint)
        {
            if (kind != ScheduleBuilderTripAlertKind.Rerouted)
                return;

            float x = bounds.X;
            float y = bounds.Y;
            float w = bounds.Width;
            float h = bounds.Height;
            float stroke = Math.Max(1.6f, w * 0.14f);

            using (var pen = new Pen(tint, stroke))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;

                float left = x + w * 0.18f;
                float top = y + h * 0.22f;
                float right = x + w * 0.82f;
                float bottom = y + h * 0.78f;
                float midY = y + h * 0.52f;

                g.DrawLine(pen, right, top, right, bottom);
                g.DrawArc(pen, left, midY - (bottom - midY), (right - left) * 2f, (bottom - midY) * 2f, 90f, 90f);
                g.DrawLine(pen, left, midY, left, top + stroke);
                g.DrawLine(pen, left - stroke * 1.4f, top + stroke * 1.6f, left + stroke * 1.4f, top + stroke * 1.6f);
            }
        }

        private static void EnsureLoaded()
        {
            if (_loaded)
                return;

            lock (Sync)
            {
                if (_loaded)
                    return;

                _sources = new Dictionary<ScheduleBuilderTripAlertKind, Image>();
                foreach (KeyValuePair<ScheduleBuilderTripAlertKind, string> entry in FileNames)
                {
                    Image image = TryLoadIcon(entry.Value);
                    if (image != null)
                        _sources[entry.Key] = image;
                }

                _loaded = true;
            }
        }

        private static Image TryLoadIcon(string fileName)
        {
            string folder = ResolveIconFolder();
            if (folder != null)
            {
                string path = Path.Combine(folder, fileName);
                if (File.Exists(path))
                {
                    try
                    {
                        using (var stream = File.OpenRead(path))
                            return Image.FromStream(stream);
                    }
                    catch
                    {
                        // Fall through to embedded copy.
                    }
                }
            }

            return TryLoadEmbeddedIcon(fileName);
        }

        private static Image TryLoadEmbeddedIcon(string fileName)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            string suffix = ".Resources.trip-alerts." + fileName;
            string resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(name => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
            if (resourceName == null)
                return null;

            try
            {
                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                        return null;
                    return Image.FromStream(stream);
                }
            }
            catch
            {
                return null;
            }
        }

        private static string ResolveIconFolder()
        {
            string[] candidates =
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "trip-alerts"),
                Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "", "Resources", "trip-alerts"),
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                if (Directory.Exists(candidates[i]))
                    return candidates[i];
            }

            return null;
        }
    }
}
