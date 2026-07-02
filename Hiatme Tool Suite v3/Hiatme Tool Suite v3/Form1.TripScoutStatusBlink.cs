using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    partial class Form1
    {
        private const int TripScoutStatusBlinkDurationMs = 30_000;
        private const int TripScoutStatusBlinkIntervalMs = 450;
        private static readonly Color TripScoutStatusBlinkAccent = Color.FromArgb(210, 145, 45);

        private readonly Dictionary<string, DateTime> _tripScoutStatusBlinkUntil =
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        private System.Windows.Forms.Timer _tripScoutStatusBlinkTimer;
        private bool _tripScoutStatusBlinkPhase;

        internal void TripScoutRegisterStatusBlink(string tripKey)
        {
            if (string.IsNullOrWhiteSpace(tripKey) || tslv == null || tslv.IsDisposed)
                return;

            _tripScoutStatusBlinkUntil[tripKey.Trim()] =
                DateTime.UtcNow.AddMilliseconds(TripScoutStatusBlinkDurationMs);
            EnsureTripScoutStatusBlinkTimer();
            tslv.Invalidate(true);
        }

        internal void TripScoutClearStatusBlinks()
        {
            _tripScoutStatusBlinkUntil.Clear();
            _tripScoutStatusBlinkTimer?.Stop();
            _tripScoutStatusBlinkPhase = false;
        }

        private void EnsureTripScoutStatusBlinkTimer()
        {
            if (_tripScoutStatusBlinkTimer == null)
            {
                _tripScoutStatusBlinkTimer = new System.Windows.Forms.Timer
                {
                    Interval = TripScoutStatusBlinkIntervalMs,
                };
                _tripScoutStatusBlinkTimer.Tick += (_, __) => TripScoutStatusBlinkTimer_Tick();
            }

            if (!_tripScoutStatusBlinkTimer.Enabled)
                _tripScoutStatusBlinkTimer.Start();
        }

        private void TripScoutStatusBlinkTimer_Tick()
        {
            if (tslv == null || tslv.IsDisposed)
            {
                _tripScoutStatusBlinkTimer?.Stop();
                return;
            }

            TripScoutPruneExpiredStatusBlinks();
            if (_tripScoutStatusBlinkUntil.Count == 0)
            {
                _tripScoutStatusBlinkTimer.Stop();
                _tripScoutStatusBlinkPhase = false;
                return;
            }

            _tripScoutStatusBlinkPhase = !_tripScoutStatusBlinkPhase;
            tslv.Invalidate(true);
        }

        private void TripScoutPruneExpiredStatusBlinks()
        {
            if (_tripScoutStatusBlinkUntil.Count == 0)
                return;

            DateTime now = DateTime.UtcNow;
            foreach (string key in _tripScoutStatusBlinkUntil.Keys.ToList())
            {
                if (_tripScoutStatusBlinkUntil[key] <= now)
                    _tripScoutStatusBlinkUntil.Remove(key);
            }
        }

        private bool TripScoutIsStatusBlinkHighlight(string tripKey)
        {
            if (string.IsNullOrWhiteSpace(tripKey))
                return false;

            string key = tripKey.Trim();
            if (!_tripScoutStatusBlinkUntil.TryGetValue(key, out DateTime until))
                return false;

            if (until <= DateTime.UtcNow)
            {
                _tripScoutStatusBlinkUntil.Remove(key);
                return false;
            }

            return _tripScoutStatusBlinkPhase;
        }

        private static Color TripScoutStatusBlinkCellBackground(Color baseBg)
        {
            if (baseBg == Color.Empty || baseBg == Color.Transparent)
                baseBg = SupeyTheme.ListBody;
            return TripScoutBlendColors(baseBg, TripScoutStatusBlinkAccent, 0.58f);
        }

        private static Color TripScoutBlendColors(Color a, Color b, float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        private void TripScoutPaintStatusCellIfBlinking(
            DrawListViewSubItemEventArgs e,
            Color baseBg,
            out Color cellBg)
        {
            cellBg = baseBg;
            if (e == null || e.ColumnIndex != 0 || !ReferenceEquals(e.Item?.ListView, tslv))
                return;

            var trip = TripScoutListRow.TryGetTrip(e.Item.Tag);
            if (trip == null)
                return;

            if (TripScoutIsStatusBlinkHighlight(TripScoutTripKey(trip)))
                cellBg = TripScoutStatusBlinkCellBackground(baseBg);
        }
    }
}
