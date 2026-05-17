using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Compact owner-drawn card that summarizes one <see cref="WRDownloadedTrip"/> in the trips
    /// panel of <see cref="DriverLocationMapForm"/>. Layout matches the marker tooltip styling
    /// (left accent stripe + bold title row + muted detail rows) so the popup feels cohesive.
    /// </summary>
    internal sealed class TripCardControl : Panel
    {
        public const int CardWidth = 296;
        public const int CardHeight = 96;
        /// <summary>Taller variant used for cards that can show an ETA row (Scheduled / OnBoard / AtPickup).</summary>
        public const int CardHeightWithEta = 116;

        private const int AccentWidth = 4;
        private const int TextLeftPad = 14;
        private const int TextRightPad = 12;
        private const int TopPad = 9;

        private static readonly Color CardBackground = Color.FromArgb(50, 50, 50);
        private static readonly Color CardBackgroundHover = Color.FromArgb(58, 58, 58);
        private static readonly Color BorderColor = Color.FromArgb(80, 80, 80);
        private static readonly Color TitleColor = Color.White;
        private static readonly Color ClientColor = Color.Gainsboro;
        private static readonly Color DetailColor = Color.Silver;
        private static readonly Color EtaLabelColor = Color.FromArgb(160, 160, 160);
        private static readonly Color EtaTimeColor = Color.White;
        private static readonly Color EtaRelColor = Color.FromArgb(140, 200, 245); // soft sky blue
        private static readonly Color EtaPendingColor = Color.FromArgb(150, 150, 150);
        private static readonly Color EtaWarnColor = Color.FromArgb(255, 183, 77);  // amber 300

        private readonly Font _titleFont;
        private readonly Font _clientFont;
        private readonly Font _detailFont;
        private readonly Font _pillFont;
        private bool _hover;

        // ETA state — populated by SetEta from the form once "Estimate ETAs" runs.
        public enum EtaState { Hidden, Pending, Computing, Estimated, Failed }
        private EtaState _etaState;
        private DateTime _etaTime;
        private TimeSpan _etaFromNow;
        private string _etaLabel = "";
        private string _etaErrorText = "";

        public WRDownloadedTrip Trip { get; }
        public TripPhase Phase { get; }
        public string PhaseLabel { get; }
        public Color PhaseColor { get; }
        /// <summary>Whether this card's phase qualifies for an ETA row (Scheduled / OnBoard / AtPickup).</summary>
        public bool ReservesEtaRow { get; }

        public TripCardControl(WRDownloadedTrip trip, TripPhase phase, string phaseLabel, Color phaseColor,
            Font titleFont, Font clientFont, Font detailFont, Font pillFont, bool reservesEtaRow)
        {
            Trip = trip ?? throw new ArgumentNullException(nameof(trip));
            Phase = phase;
            PhaseLabel = phaseLabel ?? "";
            PhaseColor = phaseColor;
            ReservesEtaRow = reservesEtaRow;
            _titleFont = titleFont;
            _clientFont = clientFont;
            _detailFont = detailFont;
            _pillFont = pillFont;

            DoubleBuffered = true;
            BackColor = CardBackground;
            Width = CardWidth;
            // Reserve extra vertical space only on cards that can host an ETA row, so non-eligible
            // phases (Completed, Cancelled, AtDropoff) stay compact and the panel doesn't waste space.
            Height = reservesEtaRow ? CardHeightWithEta : CardHeight;
            Margin = new Padding(0, 0, 0, 8);
            Cursor = Cursors.Hand;
            _etaState = reservesEtaRow ? EtaState.Pending : EtaState.Hidden;
        }

        /// <summary>Reset the ETA row to the pending placeholder (used when the user reruns the estimator).</summary>
        public void ResetEta()
        {
            if (!ReservesEtaRow) { _etaState = EtaState.Hidden; }
            else _etaState = EtaState.Pending;
            _etaErrorText = "";
            Invalidate();
        }

        /// <summary>Mark this card as currently being computed (during the Nominatim/OSRM round-trip).</summary>
        public void SetEtaComputing()
        {
            if (!ReservesEtaRow) return;
            _etaState = EtaState.Computing;
            _etaErrorText = "";
            Invalidate();
        }

        /// <summary>Stamp a successful ETA on the card.</summary>
        public void SetEta(DateTime localTime, TimeSpan fromNow, string label)
        {
            if (!ReservesEtaRow) return;
            _etaState = EtaState.Estimated;
            _etaTime = localTime;
            _etaFromNow = fromNow;
            _etaLabel = string.IsNullOrEmpty(label) ? "ETA" : label;
            Invalidate();
        }

        /// <summary>Mark this card as ineligible for ETA computation, with a short explanation.</summary>
        public void SetEtaFailed(string reason)
        {
            if (!ReservesEtaRow) return;
            _etaState = EtaState.Failed;
            _etaErrorText = reason ?? "ETA unavailable";
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            using (var bg = new SolidBrush(_hover ? CardBackgroundHover : CardBackground))
                g.FillRectangle(bg, ClientRectangle);

            using (var border = new Pen(BorderColor, 1f))
                g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);

            using (var accent = new SolidBrush(PhaseColor))
                g.FillRectangle(accent, 0, 0, AccentWidth, Height);

            int textX = AccentWidth + TextLeftPad;
            int textRight = Width - TextRightPad;
            int y = TopPad;

            string tripNumStr = "#" + (string.IsNullOrWhiteSpace(Trip.TripNumber) ? "—" : Trip.TripNumber.Trim());
            var tripNumSize = Size.Ceiling(g.MeasureString(tripNumStr, _titleFont));

            DrawStatusPill(g, textRight, y, _pillFont);

            using (var titleBrush = new SolidBrush(TitleColor))
                g.DrawString(tripNumStr, _titleFont, titleBrush, textX, y);
            y += tripNumSize.Height + 3;

            string client = Trip.ClientName ?? "";
            if (!string.IsNullOrWhiteSpace(client))
            {
                using (var clientBrush = new SolidBrush(ClientColor))
                    g.DrawString(client, _clientFont, clientBrush, textX, y);
                y += Size.Ceiling(g.MeasureString(client, _clientFont)).Height + 1;
            }

            int availWidth = textRight - textX;
            string timeLine = BuildTimeLine();
            using (var timeBrush = new SolidBrush(DetailColor))
                g.DrawString(TruncateToFit(g, timeLine, _detailFont, availWidth),
                    _detailFont, timeBrush, textX, y);
            y += Size.Ceiling(g.MeasureString(timeLine, _detailFont)).Height + 1;

            string route = BuildRouteLine();
            using (var routeBrush = new SolidBrush(DetailColor))
                g.DrawString(TruncateToFit(g, route, _detailFont, availWidth),
                    _detailFont, routeBrush, textX, y);
            y += Size.Ceiling(g.MeasureString(route, _detailFont)).Height + 1;

            if (ReservesEtaRow && _etaState != EtaState.Hidden)
            {
                // Small horizontal divider so the ETA row reads as a separate "fact" and not a
                // run-on of the route line above.
                int dividerY = y + 3;
                using (var div = new Pen(Color.FromArgb(70, 70, 70), 1f))
                    g.DrawLine(div, textX, dividerY, textRight, dividerY);
                y = dividerY + 4;
                DrawEtaRow(g, textX, y, availWidth);
            }
        }

        private void DrawEtaRow(Graphics g, int textX, int y, int availWidth)
        {
            switch (_etaState)
            {
                case EtaState.Pending:
                    using (var b = new SolidBrush(EtaPendingColor))
                        g.DrawString("Click \"Estimate ETAs\" for arrival time",
                            _detailFont, b, textX, y);
                    break;

                case EtaState.Computing:
                    using (var b = new SolidBrush(EtaPendingColor))
                        g.DrawString("Estimating arrival...", _detailFont, b, textX, y);
                    break;

                case EtaState.Estimated:
                    {
                        // Three-segment line so each piece can have its own color: muted label,
                        // bright wall-clock time, soft "≈ N min from now" relative.
                        string labelStr = _etaLabel + "  ";
                        string timeStr = _etaTime.ToString("h:mm tt", CultureInfo.InvariantCulture);
                        string relStr = "  •  " + FormatRelative(_etaFromNow);

                        var labelSize = g.MeasureString(labelStr, _detailFont);
                        var timeSize = g.MeasureString(timeStr, _detailFont);

                        using (var lb = new SolidBrush(EtaLabelColor))
                            g.DrawString(labelStr, _detailFont, lb, textX, y);
                        using (var tb = new SolidBrush(EtaTimeColor))
                            g.DrawString(timeStr, _detailFont, tb, textX + labelSize.Width, y);
                        using (var rb = new SolidBrush(EtaRelColor))
                            g.DrawString(relStr, _detailFont, rb,
                                textX + labelSize.Width + timeSize.Width, y);
                        break;
                    }

                case EtaState.Failed:
                    using (var b = new SolidBrush(EtaWarnColor))
                        g.DrawString(TruncateToFit(g, _etaErrorText, _detailFont, availWidth),
                            _detailFont, b, textX, y);
                    break;
            }
        }

        private static string FormatRelative(TimeSpan ts)
        {
            int totalMin = (int)Math.Round(ts.TotalMinutes);
            if (totalMin <= 0) return "now";
            if (totalMin < 60) return "≈ " + totalMin + " min";
            int hr = totalMin / 60;
            int rem = totalMin % 60;
            if (rem == 0) return "≈ " + hr + "h";
            return "≈ " + hr + "h " + rem + "m";
        }

        private void DrawStatusPill(Graphics g, int rightEdge, int top, Font font)
        {
            if (string.IsNullOrEmpty(PhaseLabel)) return;
            var size = Size.Ceiling(g.MeasureString(PhaseLabel, font));
            var rect = new Rectangle(rightEdge - size.Width - 12, top, size.Width + 12, size.Height + 4);
            using (var fill = new SolidBrush(Color.FromArgb(60, PhaseColor.R, PhaseColor.G, PhaseColor.B)))
            using (var path = RoundedRectPath(rect, 4))
                g.FillPath(fill, path);
            using (var border = new Pen(PhaseColor, 1f))
            using (var path = RoundedRectPath(rect, 4))
                g.DrawPath(border, path);
            using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (var brush = new SolidBrush(PhaseColor))
                g.DrawString(PhaseLabel, font, brush, rect, fmt);
        }

        private string BuildTimeLine()
        {
            string sched = FormatTimeOnly(Trip.PUTime);
            string actual = FormatTimeOnly(Trip.ActualPUTime);
            string actualDo = FormatTimeOnly(Trip.ActualDOTime);

            if (!string.IsNullOrWhiteSpace(actualDo))
                return "Sched " + (string.IsNullOrEmpty(sched) ? "—" : sched) + "  •  Dropped " + actualDo;
            if (!string.IsNullOrWhiteSpace(actual))
                return "Sched " + (string.IsNullOrEmpty(sched) ? "—" : sched) + "  •  Picked up " + actual;
            return "Sched " + (string.IsNullOrEmpty(sched) ? "—" : sched);
        }

        private string BuildRouteLine()
        {
            string from = JoinCommaSafe(Trip.PUStreet, Trip.PUCity);
            string to = JoinCommaSafe(Trip.DOStreet, Trip.DOCITY);
            if (string.IsNullOrEmpty(from) && string.IsNullOrEmpty(to)) return "";
            if (string.IsNullOrEmpty(from)) from = "—";
            if (string.IsNullOrEmpty(to)) to = "—";
            return from + "  →  " + to;
        }

        private static string JoinCommaSafe(string street, string city)
        {
            bool hasS = !string.IsNullOrWhiteSpace(street);
            bool hasC = !string.IsNullOrWhiteSpace(city);
            if (hasS && hasC) return street.Trim() + ", " + city.Trim();
            if (hasS) return street.Trim();
            if (hasC) return city.Trim();
            return "";
        }

        private static string FormatTimeOnly(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            if (DateTime.TryParse(raw, CultureInfo.CurrentCulture, System.Globalization.DateTimeStyles.None, out var dt))
                return dt.ToString("h:mm tt", CultureInfo.InvariantCulture);
            return raw.Trim();
        }

        private static string TruncateToFit(Graphics g, string text, Font font, int maxWidth)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (Size.Ceiling(g.MeasureString(text, font)).Width <= maxWidth) return text;
            const string ellipsis = "…";
            int lo = 0, hi = text.Length;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                string candidate = text.Substring(0, mid) + ellipsis;
                if (Size.Ceiling(g.MeasureString(candidate, font)).Width <= maxWidth) lo = mid;
                else hi = mid - 1;
            }
            return text.Substring(0, lo) + ellipsis;
        }

        private static GraphicsPath RoundedRectPath(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal enum TripPhase
    {
        Unknown,
        Cancelled,
        Scheduled,
        AtPickup,
        OnBoard,
        AtDropoff,
        Completed,
    }

    /// <summary>
    /// Maps the WellRyde portal status string (Reserved / Assigned / Pickup Arrived / Pickup
    /// Departed / Dropoff Arrived / Dropoff Completed / Completed / Billed / In Progress /
    /// Cancelled / Suspended) into the colored <see cref="TripPhase"/> enum used by the trip
    /// card and the trip-list ordering rules.
    /// </summary>
    internal static class TripPhaseClassifier
    {
        public static readonly Color CompletedColor = Color.FromArgb(110, 110, 110);
        public static readonly Color CancelledColor = Color.FromArgb(229, 57, 53);   // red 600
        public static readonly Color ScheduledColor = Color.FromArgb(120, 144, 156); // blueGrey 400
        public static readonly Color AtPickupColor = Color.FromArgb(255, 152, 0);    // orange 500
        public static readonly Color OnBoardColor = Color.FromArgb(76, 175, 80);     // green 500
        public static readonly Color AtDropoffColor = Color.FromArgb(33, 150, 243);  // blue 500
        public static readonly Color UnknownColor = Color.FromArgb(110, 110, 110);

        public static (TripPhase phase, string label, Color color) Classify(WRDownloadedTrip trip)
        {
            if (trip == null) return (TripPhase.Unknown, "—", UnknownColor);

            string s = (trip.Status ?? "").Trim();
            if (string.Equals(s, "Cancelled", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "Suspended", StringComparison.OrdinalIgnoreCase))
                return (TripPhase.Cancelled, "CANCELLED", CancelledColor);

            if (string.Equals(s, "Completed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "Billed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "Dropoff Completed", StringComparison.OrdinalIgnoreCase))
                return (TripPhase.Completed, "DONE", CompletedColor);

            if (string.Equals(s, "Pickup Arrived", StringComparison.OrdinalIgnoreCase))
                return (TripPhase.AtPickup, "AT PICKUP", AtPickupColor);

            if (string.Equals(s, "Pickup Departed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "In Progress", StringComparison.OrdinalIgnoreCase))
                return (TripPhase.OnBoard, "ON BOARD", OnBoardColor);

            if (string.Equals(s, "Dropoff Arrived", StringComparison.OrdinalIgnoreCase))
                return (TripPhase.AtDropoff, "AT DROPOFF", AtDropoffColor);

            if (string.Equals(s, "Reserved", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "Assigned", StringComparison.OrdinalIgnoreCase))
                return (TripPhase.Scheduled, "SCHEDULED", ScheduledColor);

            // Fallback: ActualPUTime/ActualDOTime tell us almost as much as the raw status string
            // when the portal returns something unexpected (newer status verbiage, locale skew).
            bool hasPickup = !string.IsNullOrWhiteSpace(trip.ActualPUTime);
            bool hasDropoff = !string.IsNullOrWhiteSpace(trip.ActualDOTime);
            if (hasDropoff) return (TripPhase.Completed, "DONE", CompletedColor);
            if (hasPickup) return (TripPhase.OnBoard, "ON BOARD", OnBoardColor);

            return (TripPhase.Unknown, string.IsNullOrEmpty(s) ? "—" : s.ToUpperInvariant(), UnknownColor);
        }

        /// <summary>
        /// Sort key for the trip list: in-progress / at-pickup trips first (those are the ones
        /// dispatch is actively trying to figure out), then upcoming scheduled, then completed,
        /// then cancelled. Trips within the same phase are ordered by parsed PUTime.
        /// </summary>
        public static int PriorityOrder(TripPhase phase)
        {
            switch (phase)
            {
                case TripPhase.OnBoard: return 0;
                case TripPhase.AtPickup: return 1;
                case TripPhase.AtDropoff: return 2;
                case TripPhase.Scheduled: return 3;
                case TripPhase.Unknown: return 4;
                case TripPhase.Completed: return 5;
                case TripPhase.Cancelled: return 6;
                default: return 7;
            }
        }

        public static IEnumerable<WRDownloadedTrip> Sort(IEnumerable<WRDownloadedTrip> trips)
        {
            if (trips == null) yield break;
            var list = new List<(WRDownloadedTrip trip, TripPhase phase, DateTime puKey)>();
            foreach (var t in trips)
            {
                if (t == null) continue;
                var (phase, _, _) = Classify(t);
                if (!DateTime.TryParse(t.PUTime, CultureInfo.CurrentCulture,
                        System.Globalization.DateTimeStyles.None, out var dt))
                    dt = DateTime.MaxValue;
                list.Add((t, phase, dt));
            }
            list.Sort((a, b) =>
            {
                int p = PriorityOrder(a.phase).CompareTo(PriorityOrder(b.phase));
                if (p != 0) return p;
                return a.puKey.CompareTo(b.puKey);
            });
            foreach (var x in list) yield return x.trip;
        }
    }
}
