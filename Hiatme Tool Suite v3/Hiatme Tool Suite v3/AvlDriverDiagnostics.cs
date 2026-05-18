using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Human-readable AVL / portal hints for why a driver's live position may be missing or stale.
    /// Best-effort only — WellRyde does not expose phone permission state to the desktop portal.
    /// </summary>
    internal sealed class AvlDriverDiagnostics
    {
        public string Headline { get; set; } = "";
        public List<string> Bullets { get; set; } = new List<string>();
        public Color AccentColor { get; set; } = Color.Silver;

        public string FormatBody()
        {
            if (Bullets == null || Bullets.Count == 0)
                return "";
            var lines = new List<string>(Bullets.Count);
            foreach (string b in Bullets)
            {
                if (string.IsNullOrWhiteSpace(b)) continue;
                lines.Add("• " + b.Trim());
            }
            return string.Join(Environment.NewLine, lines);
        }
    }

    internal static class AvlDriverDiagnosticsBuilder
    {
        private static readonly TimeSpan LiveGpsWindow = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan RecentGpsWindow = TimeSpan.FromMinutes(60);
        private static readonly TimeSpan ConnectedFreshWindow = TimeSpan.FromMinutes(15);

        public static AvlDriverDiagnostics Build(
            WRDriverPosition avl,
            WellRydeUserDetail profile,
            bool foundOnAvlFeed)
        {
            var d = new AvlDriverDiagnostics();
            AppendProfileAccountWarnings(d, profile);

            if (!foundOnAvlFeed || avl == null)
            {
                d.Headline = "Not on live map";
                d.AccentColor = Color.FromArgb(110, 110, 110);
                d.Bullets.Add("WellRyde AVL does not list this driver right now.");
                d.Bullets.Add("Common causes: Driver app closed, not logged in, location permission off, or phone offline.");
                d.Bullets.Add("Ask the driver to open the WellRyde Driver app, log in, enable location, and select their vehicle.");
                return d;
            }

            DateTime? reported = avl.GetReportedLocalTime();
            DateTime? connected = avl.GetLastConnectedLocalTime();
            TimeSpan? gpsAge = reported.HasValue ? DateTime.Now - reported.Value : (TimeSpan?)null;
            TimeSpan? connAge = connected.HasValue ? DateTime.Now - connected.Value : (TimeSpan?)null;

            if (!avl.HasValidLocation)
            {
                d.Headline = "On live map — no GPS fix";
                d.AccentColor = StatusColorForAge(gpsAge);
                d.Bullets.Add("Portal sees the driver but has no coordinates.");
                if (string.IsNullOrWhiteSpace(avl.VehicleName))
                    d.Bullets.Add("No vehicle on the live feed — often means no vehicle is selected in the Driver app.");
                else
                    d.Bullets.Add("Live vehicle on feed: " + avl.VehicleName.Trim() + ".");
                d.Bullets.Add("Have them open the Driver app, confirm the correct vehicle is selected, and keep location enabled.");
                AppendTimestampBullets(d, reported, connected, gpsAge, connAge);
                AppendVehicleMismatchBullet(d, avl, profile);
                return d;
            }

            bool gpsLive = gpsAge.HasValue && gpsAge.Value <= LiveGpsWindow;
            bool gpsRecent = gpsAge.HasValue && gpsAge.Value <= RecentGpsWindow;

            if (gpsLive)
            {
                d.Headline = "Live GPS";
                d.AccentColor = Color.FromArgb(76, 175, 80);
            }
            else if (gpsRecent)
            {
                d.Headline = "GPS somewhat stale";
                d.AccentColor = Color.FromArgb(255, 193, 7);
            }
            else
            {
                d.Headline = "GPS stale";
                d.AccentColor = Color.FromArgb(229, 57, 53);
            }

            if (gpsAge.HasValue)
                d.Bullets.Add("Last GPS ping: " + FormatAge(gpsAge.Value) + ".");
            else
                d.Bullets.Add("Last GPS ping: unknown.");

            if (connAge.HasValue)
                d.Bullets.Add("App last connected: " + FormatAge(connAge.Value) + ".");
            else
                d.Bullets.Add("App last connected: unknown.");

            if (!gpsLive && connAge.HasValue && connAge.Value <= ConnectedFreshWindow &&
                (!gpsAge.HasValue || gpsAge.Value > LiveGpsWindow))
            {
                d.Bullets.Add("App connected recently but GPS is not updating — check location permission or background restrictions on the phone.");
            }
            else if (!gpsRecent && connAge.HasValue && connAge.Value > RecentGpsWindow)
            {
                d.Bullets.Add("App has not connected recently — Driver app may be closed or logged out.");
            }
            else if (!gpsRecent)
            {
                d.Bullets.Add("GPS has not updated recently — driver may need to reopen the app or select their vehicle.");
            }

            if (!string.IsNullOrWhiteSpace(avl.VehicleName))
                d.Bullets.Add("Live feed vehicle: " + avl.VehicleName.Trim() +
                    (string.IsNullOrWhiteSpace(avl.VehicleType) ? "" : " (" + avl.VehicleType.Trim() + ")") + ".");

            if (avl.IsExternalLoad && !string.IsNullOrWhiteSpace(avl.TransportProviderName))
                d.Bullets.Add("External transport provider: " + avl.TransportProviderName.Trim() + ".");

            AppendVehicleMismatchBullet(d, avl, profile);
            return d;
        }

        private static void AppendProfileAccountWarnings(AvlDriverDiagnostics d, WellRydeUserDetail profile)
        {
            if (profile == null) return;
            if (profile.AccountLocked)
                d.Bullets.Add("Portal account is locked — driver may not be able to sign into the app.");
            else if (!profile.AccountEnabled)
                d.Bullets.Add("Portal account is disabled.");
            if (!profile.HasDriverRole)
                d.Bullets.Add("Portal profile does not include the Driver role.");
        }

        private static void AppendTimestampBullets(AvlDriverDiagnostics d,
            DateTime? reported, DateTime? connected, TimeSpan? gpsAge, TimeSpan? connAge)
        {
            if (gpsAge.HasValue)
                d.Bullets.Add("Last GPS ping: " + FormatAge(gpsAge.Value) + ".");
            if (connAge.HasValue)
                d.Bullets.Add("App last connected: " + FormatAge(connAge.Value) + ".");
            if (!reported.HasValue && !connected.HasValue)
                d.Bullets.Add("Portal did not return GPS or connection timestamps.");
        }

        private static void AppendVehicleMismatchBullet(AvlDriverDiagnostics d, WRDriverPosition avl, WellRydeUserDetail profile)
        {
            if (profile == null) return;
            string profileVehicle = (profile.VehicleLabel ?? "").Trim();
            string liveVehicle = (avl.VehicleName ?? "").Trim();
            if (profileVehicle.Length == 0 || liveVehicle.Length == 0)
            {
                if (profileVehicle.Length > 0 && liveVehicle.Length == 0)
                    d.Bullets.Add("Portal profile last known vehicle: " + profileVehicle + " (not on live feed).");
                return;
            }
            if (!VehiclesLikelyMatch(profileVehicle, liveVehicle))
            {
                d.Bullets.Add("Vehicle mismatch: portal profile shows \"" + profileVehicle +
                    "\" but live feed shows \"" + liveVehicle + "\" — driver may need to select the correct vehicle in the app.");
            }
        }

        private static Color StatusColorForAge(TimeSpan? gpsAge)
        {
            if (!gpsAge.HasValue) return Color.FromArgb(110, 110, 110);
            double mins = Math.Max(0, gpsAge.Value.TotalMinutes);
            if (mins <= 5) return Color.FromArgb(76, 175, 80);
            if (mins <= 60) return Color.FromArgb(255, 193, 7);
            return Color.FromArgb(229, 57, 53);
        }

        private static bool VehiclesLikelyMatch(string a, string b)
        {
            string na = NormalizeVehicleToken(a);
            string nb = NormalizeVehicleToken(b);
            if (na.Length == 0 || nb.Length == 0) return true;
            if (string.Equals(na, nb, StringComparison.OrdinalIgnoreCase)) return true;
            if (na.Length >= 3 && nb.IndexOf(na, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (nb.Length >= 3 && na.IndexOf(nb, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static string NormalizeVehicleToken(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            return Regex.Replace(raw.ToUpperInvariant(), "[^A-Z0-9]", "");
        }

        private static string FormatAge(TimeSpan age)
        {
            if (age.TotalSeconds < 0) return "just now";
            if (age.TotalMinutes < 1) return ((int)age.TotalSeconds) + " sec ago";
            if (age.TotalHours < 1) return ((int)age.TotalMinutes) + " min ago";
            if (age.TotalDays < 1) return ((int)age.TotalHours) + " hr ago";
            return ((int)age.TotalDays) + " day(s) ago";
        }
    }
}
