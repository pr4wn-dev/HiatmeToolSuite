using System;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Merges WellRyde portal user detail into <see cref="SupeyDriverProfile"/>.
    /// Portal fields overwrite local JSON on pull/BUILD refresh; capacity and shift stay on this PC.
    /// </summary>
    internal static class SupeyWellRydeRosterMerge
    {
        /// <summary>
        /// Apply WellRyde as source of truth for identity, name, home, and vehicle.
        /// Preserves <see cref="SupeyDriverProfile.CapacityPassengers"/>,
        /// <see cref="SupeyDriverProfile.ShiftStart"/>, and <see cref="SupeyDriverProfile.ShiftEnd"/>.
        /// </summary>
        public static void ApplyPortalDetail(WellRydeUserDetail detail, SupeyDriverProfile profile, bool isNewDriver = false)
        {
            if (detail == null || profile == null) return;

            int capacity = profile.CapacityPassengers > 0 ? profile.CapacityPassengers : 4;
            string shiftStart = profile.ShiftStart ?? "";
            string shiftEnd = profile.ShiftEnd ?? "";

            profile.WellRydeSecId = detail.SecId ?? "";
            profile.WellRydeUsername = (detail.Username ?? "").Trim();
            profile.WellRydeSyncedAtUtc = DateTime.UtcNow;

            string name = (detail.FullName ?? "").Trim();
            if (name.Length == 0)
                name = (detail.Username ?? "").Trim();
            profile.Name = name;

            profile.HomeStreet = detail.FullStreet ?? "";
            profile.HomeCity = (detail.City ?? "").Trim();
            profile.HomeState = (detail.State ?? "").Trim();
            profile.HomeZip = (detail.Zip ?? "").Trim();
            profile.VehicleLabel = (detail.VehicleLabel ?? "").Trim();

            string portalEmail = (detail.Email ?? "").Trim();
            if (portalEmail.Length > 0)
            {
                string localEmail = (profile.Email ?? "").Trim();
                if (isNewDriver || localEmail.Length == 0)
                    profile.Email = portalEmail;
            }

            profile.CapacityPassengers = capacity;
            profile.ShiftStart = shiftStart;
            profile.ShiftEnd = shiftEnd;
            if (isNewDriver)
            {
                if (profile.CapacityPassengers <= 0) profile.CapacityPassengers = 4;
                if (string.IsNullOrWhiteSpace(profile.ShiftStart)) profile.ShiftStart = "06:00";
                if (string.IsNullOrWhiteSpace(profile.ShiftEnd)) profile.ShiftEnd = "18:00";
            }
        }
    }
}
