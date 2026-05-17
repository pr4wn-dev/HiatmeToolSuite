using System.Collections.Generic;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Rich user record produced by scraping the WellRyde portal user detail page
    /// (<c>GET /portal/users/SEC-...</c>). Aggregates the <see cref="WellRydeUserSummary"/>
    /// list-level fields with everything pulled from the detail page's <c>display-group</c>
    /// label/value blocks, plus the role list and the optional last-known-vehicle plate.
    /// </summary>
    /// <remarks>
    /// All fields are best-effort — the portal HTML is forgiving and many drivers leave fields
    /// blank. Code consuming this DTO should treat missing fields as empty strings, not nulls.
    /// </remarks>
    internal sealed class WellRydeUserDetail
    {
        public string SecId { get; set; } = "";
        public string Username { get; set; } = "";
        public string FullName { get; set; } = "";
        public string Email { get; set; } = "";
        public string Phone { get; set; } = "";

        public string Address1 { get; set; } = "";
        public string Address2 { get; set; } = "";
        public string City { get; set; } = "";
        public string State { get; set; } = "";
        public string Zip { get; set; } = "";
        public string Country { get; set; } = "";

        /// <summary>
        /// Cosmetic vehicle identifier. The portal exposes both the full VIN and a "Last known
        /// vehicle" plate suffix; the parser prefers the latter and falls back to the last 4 of
        /// the VIN. Used to populate <see cref="SupeyDriverProfile.VehicleLabel"/>.
        /// </summary>
        public string VehicleLabel { get; set; } = "";

        public string VIN { get; set; } = "";
        public string CdlNumber { get; set; } = "";
        public string LicenseExpiration { get; set; } = "";

        public bool AccountEnabled { get; set; }
        public bool AccountLocked { get; set; }

        /// <summary>Set of WellRyde portal roles: MemberAdmin, DI_Driver, DI_Dispatcher, etc.</summary>
        public IList<string> Roles { get; set; } = new List<string>();

        /// <summary>Convenience: portal role <c>DI_Driver</c> is present.</summary>
        public bool HasDriverRole
        {
            get
            {
                if (Roles == null) return false;
                foreach (var r in Roles)
                    if (string.Equals(r, "DI_Driver", System.StringComparison.OrdinalIgnoreCase))
                        return true;
                return false;
            }
        }

        /// <summary>"112 Newbury St" plus optional Address2 trimmed.</summary>
        public string FullStreet
        {
            get
            {
                string a1 = (Address1 ?? "").Trim();
                string a2 = (Address2 ?? "").Trim();
                if (a1.Length == 0) return a2;
                if (a2.Length == 0) return a1;
                return a1 + " " + a2;
            }
        }

        /// <summary>"112 Newbury St, Auburn, ME 04210" — what we hand to Nominatim / the user.</summary>
        public string FormatOneLine()
        {
            string street = FullStreet;
            string city = (City ?? "").Trim();
            string state = (State ?? "").Trim();
            string zip = (Zip ?? "").Trim();
            string locality;
            if (state.Length > 0 && zip.Length > 0) locality = (city + ", " + state + " " + zip).Trim();
            else if (state.Length > 0) locality = (city + ", " + state).Trim();
            else locality = (city + " " + zip).Trim();
            locality = locality.Trim().TrimStart(',').Trim();
            if (street.Length > 0 && locality.Length > 0) return street + ", " + locality;
            if (street.Length > 0) return street;
            return locality;
        }
    }
}
