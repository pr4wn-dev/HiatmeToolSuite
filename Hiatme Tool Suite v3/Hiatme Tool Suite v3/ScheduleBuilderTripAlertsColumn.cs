using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Alert kinds matching strings set by <see cref="Analyzer"/> via
    /// <c>CheckIfTripIsAlreadyLogged</c> and <see cref="MCDownloadedTrip.GetColor"/>.
    /// </summary>
    internal enum ScheduleBuilderTripAlertKind
    {
        Date,
        Hidden,
        Cancelled,
        Dupe,
        Time,
        Address,
        WcNotInReserves,
        Mwc,
        Child,
        Escort,
        Lbs,
        ServiceDog,
        Scooter,
        MassTransit,
        /// <summary>Schedule Builder — confirmed rerouted on Modivcare only.</summary>
        Rerouted,
    }

    /// <summary>
    /// Owner-drawn Alerts column (between Grp and Trip #) on the Schedule Builder trips ListView.
    /// </summary>
    internal static class ScheduleBuilderTripAlertsColumn
    {
        public const int ColumnIndex = 1;
        public const int DefaultWidthPx = 130;

        /// <summary>When true, every trip row shows all alert icons (visual QA only).</summary>
        public static bool ShowAllSampleIconsForVisualTest = false;

        public const int GapNoteColumnIndex = 4;
        public const int SectionLabelColumnIndex = 3;
        public const int PuTimeColumnIndex = 5;
        public const int DoTimeColumnIndex = 8;

        public const int IconSizePx = 20;
        public const int IconGlyphInsetPx = 1;
        public const int IconGapPx = 4;
        public const int CellPaddingLeftPx = 6;
        public const int CellPaddingRightPx = 6;
        public const int CellVerticalInsetPx = 2;
        public const int IconHitSlopPx = 2;

        private static readonly Dictionary<ScheduleBuilderTripAlertKind, string> DisplayNames =
            new Dictionary<ScheduleBuilderTripAlertKind, string>
            {
                { ScheduleBuilderTripAlertKind.Date, "Date" },
                { ScheduleBuilderTripAlertKind.Hidden, "Hidden" },
                { ScheduleBuilderTripAlertKind.Cancelled, "Cancelled" },
                { ScheduleBuilderTripAlertKind.Dupe, "Dupe" },
                { ScheduleBuilderTripAlertKind.Time, "Time" },
                { ScheduleBuilderTripAlertKind.Address, "Address" },
                { ScheduleBuilderTripAlertKind.WcNotInReserves, "WC Not in reserves!" },
                { ScheduleBuilderTripAlertKind.Mwc, "MWC" },
                { ScheduleBuilderTripAlertKind.Child, "Child" },
                { ScheduleBuilderTripAlertKind.Escort, "Escort" },
                { ScheduleBuilderTripAlertKind.Lbs, "LBS" },
                { ScheduleBuilderTripAlertKind.ServiceDog, "Service Dog" },
                { ScheduleBuilderTripAlertKind.Scooter, "Scooter" },
                { ScheduleBuilderTripAlertKind.MassTransit, "Mass Transit" },
                { ScheduleBuilderTripAlertKind.Rerouted, "Reroute" },
            };

        private static readonly ScheduleBuilderTripAlertKind[] DisplayOrder =
        {
            ScheduleBuilderTripAlertKind.Rerouted,
            ScheduleBuilderTripAlertKind.Date,
            ScheduleBuilderTripAlertKind.Dupe,
            ScheduleBuilderTripAlertKind.Cancelled,
            ScheduleBuilderTripAlertKind.WcNotInReserves,
            ScheduleBuilderTripAlertKind.Time,
            ScheduleBuilderTripAlertKind.Address,
            ScheduleBuilderTripAlertKind.Hidden,
            ScheduleBuilderTripAlertKind.Mwc,
            ScheduleBuilderTripAlertKind.Child,
            ScheduleBuilderTripAlertKind.Escort,
            ScheduleBuilderTripAlertKind.Lbs,
            ScheduleBuilderTripAlertKind.ServiceDog,
            ScheduleBuilderTripAlertKind.Scooter,
            ScheduleBuilderTripAlertKind.MassTransit,
        };

        private static readonly Dictionary<string, ScheduleBuilderTripAlertKind> ExactAlertMap =
            new Dictionary<string, ScheduleBuilderTripAlertKind>(StringComparer.OrdinalIgnoreCase)
            {
                { "Date", ScheduleBuilderTripAlertKind.Date },
                { "Hidden", ScheduleBuilderTripAlertKind.Hidden },
                { "Cancelled", ScheduleBuilderTripAlertKind.Cancelled },
                { "Dupe", ScheduleBuilderTripAlertKind.Dupe },
                { "Time", ScheduleBuilderTripAlertKind.Time },
                { "Address", ScheduleBuilderTripAlertKind.Address },
                { "WC Not in reserves!", ScheduleBuilderTripAlertKind.WcNotInReserves },
                { "MWC", ScheduleBuilderTripAlertKind.Mwc },
                { "Child", ScheduleBuilderTripAlertKind.Child },
                { "Escort", ScheduleBuilderTripAlertKind.Escort },
                { "LBS", ScheduleBuilderTripAlertKind.Lbs },
                { "Service Dog", ScheduleBuilderTripAlertKind.ServiceDog },
                { "Scooter", ScheduleBuilderTripAlertKind.Scooter },
                { "Mass Transit", ScheduleBuilderTripAlertKind.MassTransit },
            };

        /// <summary>Keeps the "Alerts" header readable when no row has more than one icon.</summary>
        public const int MinColumnWidthPx = 48;

        /// <summary>Row height that fits a 20px badge, its 1px shadow, and breathing room top/bottom.</summary>
        public const int MinRowHeightPx = IconSizePx + CellVerticalInsetPx * 2 + 1;

        /// <summary>
        /// Details-view rows size to the font (~19px for Segoe UI 9.5), which clips 20px alert badges.
        /// Assign a 1px-wide image list to raise the row height; invisible under full owner-draw.
        /// </summary>
        public static void EnsureRowHeightFitsIcons(ListView lv)
        {
            if (lv == null || lv.SmallImageList != null)
                return;

            int h = Math.Max(MinRowHeightPx, lv.Font.Height + 2);
            lv.SmallImageList = new ImageList
            {
                ImageSize = new Size(1, Math.Min(h, 256)),
                ColorDepth = ColorDepth.Depth32Bit,
            };
        }

        public static string GetDisplayName(ScheduleBuilderTripAlertKind kind)
        {
            return DisplayNames.TryGetValue(kind, out string name) ? name : kind.ToString();
        }

        /// <summary>Column pixel width that fits <paramref name="count"/> icons side by side.</summary>
        public static int WidthForIconCount(int count)
        {
            int n = Math.Max(1, count);
            int w = CellPaddingLeftPx + n * IconSizePx + (n - 1) * IconGapPx + CellPaddingRightPx;
            return Math.Max(MinColumnWidthPx, w);
        }

        /// <summary>Alert count for a preview line (same rules as <see cref="ResolveAlerts"/>, no tag required).</summary>
        public static int CountAlerts(MCDownloadedTrip trip, bool cancelledOnWellRyde, bool rerouted)
        {
            if (trip == null)
                return 0;

            if (ShowAllSampleIconsForVisualTest)
                return DisplayOrder.Length;

            var found = new HashSet<ScheduleBuilderTripAlertKind>();
            if (cancelledOnWellRyde)
                found.Add(ScheduleBuilderTripAlertKind.Cancelled);
            if (rerouted)
                found.Add(ScheduleBuilderTripAlertKind.Rerouted);
            CollectFromAlertsList(trip, found);
            return found.Count;
        }

        public static IReadOnlyList<ScheduleBuilderTripAlertKind> ResolveAlerts(FsPreviewTripTag tripTag)
        {
            if (tripTag?.Trip == null)
                return Array.Empty<ScheduleBuilderTripAlertKind>();

            if (ShowAllSampleIconsForVisualTest)
                return DisplayOrder;

            var found = new HashSet<ScheduleBuilderTripAlertKind>();
            CollectFromTripTag(tripTag, found);
            CollectFromAlertsList(tripTag.Trip, found);
            return SortForDisplay(found);
        }

        public static void PaintIcons(
            Graphics g,
            Rectangle cellBounds,
            IReadOnlyList<ScheduleBuilderTripAlertKind> alerts,
            Color cellBackground)
        {
            if (g == null || alerts == null || alerts.Count == 0)
                return;

            int y = IconStripOriginY(cellBounds);
            int x = cellBounds.Left + CellPaddingLeftPx;
            int maxX = cellBounds.Right - CellPaddingRightPx;

            for (int i = 0; i < alerts.Count; i++)
            {
                if (x + IconSizePx > maxX)
                    break;

                PaintIcon(g, x, y, alerts[i], cellBackground);
                x += IconSizePx + IconGapPx;
            }
        }

        /// <summary>
        /// Alerts-column cell bounds for a row (matches owner-draw layout; use drag bump when the row is shifted).
        /// </summary>
        public static bool TryGetAlertsCellBounds(
            ListView lv,
            ListViewItem item,
            int dragBumpPx,
            out Rectangle cellBounds)
        {
            cellBounds = Rectangle.Empty;
            if (lv == null || item == null || ColumnIndex >= lv.Columns.Count)
                return false;

            Rectangle row;
            try
            {
                row = lv.GetItemRect(item.Index, ItemBoundsPortion.Entire);
            }
            catch (ArgumentException)
            {
                return false;
            }

            if (dragBumpPx > 0)
                row = new Rectangle(row.X, row.Y + dragBumpPx, row.Width, row.Height);

            int x = row.X;
            for (int c = 0; c < ColumnIndex; c++)
                x += lv.Columns[c].Width;

            cellBounds = new Rectangle(x, row.Y, lv.Columns[ColumnIndex].Width, row.Height);
            return cellBounds.Width > 0 && cellBounds.Height > 0;
        }

        /// <summary>Hit-test a client point against alert icons in the Alerts column cell.</summary>
        public static bool TryHitTest(
            Rectangle cellBounds,
            IReadOnlyList<ScheduleBuilderTripAlertKind> alerts,
            Point clientPoint,
            out ScheduleBuilderTripAlertKind kind)
            => TryGetIconAtPoint(cellBounds, alerts, clientPoint, out kind, out _);

        /// <summary>Hit-test and return the matched icon bounds (for stable tooltip anchoring).</summary>
        public static bool TryGetIconAtPoint(
            Rectangle cellBounds,
            IReadOnlyList<ScheduleBuilderTripAlertKind> alerts,
            Point clientPoint,
            out ScheduleBuilderTripAlertKind kind,
            out Rectangle iconBounds)
        {
            kind = default;
            iconBounds = Rectangle.Empty;
            if (alerts == null || alerts.Count == 0)
                return false;

            int x = cellBounds.Left + CellPaddingLeftPx;
            int y = IconStripOriginY(cellBounds);
            int maxX = cellBounds.Right - CellPaddingRightPx;

            for (int i = 0; i < alerts.Count; i++)
            {
                if (x + IconSizePx > maxX)
                    break;

                iconBounds = new Rectangle(x, y, IconSizePx, IconSizePx);
                var hit = iconBounds;
                hit.Inflate(IconHitSlopPx, IconHitSlopPx);
                if (hit.Contains(clientPoint))
                {
                    kind = alerts[i];
                    return true;
                }

                x += IconSizePx + IconGapPx;
            }

            iconBounds = Rectangle.Empty;
            return false;
        }

        /// <summary>Client position for a tooltip anchored above the icon center.</summary>
        public static Point GetToolTipAnchor(Rectangle iconBounds, Size tipSize)
        {
            int x = iconBounds.X + iconBounds.Width / 2 - tipSize.Width / 2;
            int y = iconBounds.Top - tipSize.Height - 6;
            if (y < 0)
                y = iconBounds.Bottom + 6;
            return new Point(Math.Max(0, x), Math.Max(0, y));
        }

        private static int IconStripOriginY(Rectangle cellBounds)
        {
            int inset = CellVerticalInsetPx;
            if (cellBounds.Height <= IconSizePx + inset * 2)
                return cellBounds.Top + inset;
            return cellBounds.Top + (cellBounds.Height - IconSizePx) / 2;
        }

        private static void CollectFromTripTag(FsPreviewTripTag tripTag, HashSet<ScheduleBuilderTripAlertKind> into)
        {
            if (tripTag == null)
                return;

            // WellRyde Cancelled/Suspended — highlighted on the row after cancel sync. Analyzer
            // CheckForCancels only flags trips missing from MC, so this covers WR cancels still in download.
            if (tripTag.CancelledOnWellRyde)
                into.Add(ScheduleBuilderTripAlertKind.Cancelled);

            if (tripTag.ReroutedOnModivcare)
                into.Add(ScheduleBuilderTripAlertKind.Rerouted);
        }

        private static void CollectFromAlertsList(MCDownloadedTrip trip, HashSet<ScheduleBuilderTripAlertKind> into)
        {
            if (trip.Alerts == null)
                return;

            foreach (string alert in trip.Alerts)
            {
                if (TryMapAlertText(alert, out ScheduleBuilderTripAlertKind kind))
                    into.Add(kind);
            }
        }

        private static bool TryMapAlertText(string alert, out ScheduleBuilderTripAlertKind kind)
        {
            kind = default;
            if (string.IsNullOrWhiteSpace(alert))
                return false;

            if (ExactAlertMap.TryGetValue(alert.Trim(), out kind))
                return true;

            if (alert.Contains("Date")) { kind = ScheduleBuilderTripAlertKind.Date; return true; }
            if (alert.Contains("Hidden")) { kind = ScheduleBuilderTripAlertKind.Hidden; return true; }
            if (alert.Contains("Cancelled")) { kind = ScheduleBuilderTripAlertKind.Cancelled; return true; }
            if (alert.Contains("Dupe")) { kind = ScheduleBuilderTripAlertKind.Dupe; return true; }
            if (alert.Contains("WC Not in reserves!")) { kind = ScheduleBuilderTripAlertKind.WcNotInReserves; return true; }
            if (alert.Contains("Time")) { kind = ScheduleBuilderTripAlertKind.Time; return true; }
            if (alert.Contains("Address")) { kind = ScheduleBuilderTripAlertKind.Address; return true; }
            if (alert.Contains("MWC")) { kind = ScheduleBuilderTripAlertKind.Mwc; return true; }
            if (alert.Contains("Child")) { kind = ScheduleBuilderTripAlertKind.Child; return true; }
            if (alert.Contains("Escort")) { kind = ScheduleBuilderTripAlertKind.Escort; return true; }
            if (alert.Contains("LBS")) { kind = ScheduleBuilderTripAlertKind.Lbs; return true; }
            if (alert.Contains("Service Dog")) { kind = ScheduleBuilderTripAlertKind.ServiceDog; return true; }
            if (alert.Contains("Scooter")) { kind = ScheduleBuilderTripAlertKind.Scooter; return true; }
            if (alert.Contains("Mass Transit")) { kind = ScheduleBuilderTripAlertKind.MassTransit; return true; }

            return false;
        }

        private static ScheduleBuilderTripAlertKind[] SortForDisplay(HashSet<ScheduleBuilderTripAlertKind> found)
        {
            if (found == null || found.Count == 0)
                return Array.Empty<ScheduleBuilderTripAlertKind>();

            var ordered = new List<ScheduleBuilderTripAlertKind>(found.Count);
            for (int i = 0; i < DisplayOrder.Length; i++)
            {
                if (found.Contains(DisplayOrder[i]))
                    ordered.Add(DisplayOrder[i]);
            }

            return ordered.ToArray();
        }

        /// <summary>Theme semantic ink for each alert family.</summary>
        private static Color SemanticColor(ScheduleBuilderTripAlertKind kind)
        {
            switch (kind)
            {
                case ScheduleBuilderTripAlertKind.Hidden:
                    return SupeyTheme.AccentPrimary;
                case ScheduleBuilderTripAlertKind.Cancelled:
                    return SupeyTheme.TextMuted;
                case ScheduleBuilderTripAlertKind.Rerouted:
                    return ScheduleBuilderReserveBuckets.RerouteBand;
                case ScheduleBuilderTripAlertKind.Mwc:
                case ScheduleBuilderTripAlertKind.Child:
                case ScheduleBuilderTripAlertKind.Escort:
                case ScheduleBuilderTripAlertKind.Lbs:
                case ScheduleBuilderTripAlertKind.ServiceDog:
                case ScheduleBuilderTripAlertKind.Scooter:
                case ScheduleBuilderTripAlertKind.MassTransit:
                    return SupeyTheme.WarnText;
                default:
                    return SupeyTheme.ErrorText;
            }
        }

        private static void PaintIcon(Graphics g, int x, int y, ScheduleBuilderTripAlertKind kind, Color cellBackground)
        {
            bool darkRow = RelativeLuminance(cellBackground) < 0.45;
            Color iconColor = IconInkColor(kind, SemanticColor(kind), darkRow);
            var box = new Rectangle(x, y, IconSizePx, IconSizePx);
            int inset = IconGlyphInsetPx;
            var glyph = new Rectangle(x + inset, y + inset, IconSizePx - inset * 2, IconSizePx - inset * 2);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;

            var shadowBox = new Rectangle(box.X, box.Y + 1, box.Width, box.Height);
            using (GraphicsPath shadowPath = CreateRoundedRect(shadowBox, 5))
            using (var shadowBrush = new SolidBrush(Color.FromArgb(darkRow ? 140 : 70, 0, 0, 0)))
                g.FillPath(shadowBrush, shadowPath);

            Color plateFill = Color.FromArgb(255, 255, 255, 255);
            Color plateBorder = Color.FromArgb(darkRow ? 210 : 150, Darken(iconColor, 0.25));

            using (GraphicsPath plate = CreateRoundedRect(box, 5))
            {
                using (var plateBrush = new SolidBrush(plateFill))
                    g.FillPath(plateBrush, plate);
                using (var border = new Pen(plateBorder, 1.35f))
                    g.DrawPath(border, plate);
            }

            ScheduleBuilderTripAlertIcons.DrawBold(g, kind, glyph, iconColor);
        }

        /// <summary>Saturated icon ink that stays readable on the white badge (especially cancel/reroute rows).</summary>
        private static Color IconInkColor(ScheduleBuilderTripAlertKind kind, Color semantic, bool darkRow)
        {
            switch (kind)
            {
                case ScheduleBuilderTripAlertKind.Cancelled:
                    return Color.FromArgb(190, 48, 78);
                case ScheduleBuilderTripAlertKind.Rerouted:
                    return Color.FromArgb(205, 108, 18);
                case ScheduleBuilderTripAlertKind.Hidden:
                    return Color.FromArgb(88, 108, 210);
                case ScheduleBuilderTripAlertKind.WcNotInReserves:
                case ScheduleBuilderTripAlertKind.Date:
                case ScheduleBuilderTripAlertKind.Time:
                case ScheduleBuilderTripAlertKind.Address:
                case ScheduleBuilderTripAlertKind.Dupe:
                    return Color.FromArgb(200, 52, 58);
                default:
                    return darkRow
                        ? (RelativeLuminance(semantic) < 0.42f ? Lighten(semantic, 0.12f) : semantic)
                        : Darken(semantic, 0.08);
            }
        }

        private static GraphicsPath CreateRoundedRect(Rectangle bounds, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static double ContrastDelta(Color a, Color b)
            => Math.Abs(RelativeLuminance(a) - RelativeLuminance(b));

        private static double RelativeLuminance(Color c)
            => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;

        private static Color Lighten(Color c, double amount)
        {
            amount = Clamp(amount, 0, 1);
            return Color.FromArgb(
                c.A,
                (int)(c.R + (255 - c.R) * amount),
                (int)(c.G + (255 - c.G) * amount),
                (int)(c.B + (255 - c.B) * amount));
        }

        private static Color Darken(Color c, double amount)
        {
            amount = Clamp(amount, 0, 1);
            return Color.FromArgb(
                c.A,
                (int)(c.R * (1 - amount)),
                (int)(c.G * (1 - amount)),
                (int)(c.B * (1 - amount)));
        }

        private static double Clamp(double v, double min, double max)
            => v < min ? min : v > max ? max : v;
    }
}
