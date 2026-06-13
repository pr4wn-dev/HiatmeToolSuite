using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Builds multipart fields for POST <c>/portal/users/nuUpdateUser</c> from the portal
    /// <c>GET /portal/users/{secId}?form</c> JSON plus Supey home-address and email overrides.
    /// </summary>
    internal static class WellRydeNuUpdateFormBuilder
    {
        /// <summary>Field names observed in browser capture (May 2026).</summary>
        private static readonly string[] KnownFormKeys =
        {
            "userId", "defaultCompanyId", "firstName", "orgFirstName", "middleName", "orgMiddleName",
            "lastName", "orgLastName", "username", "email", "orgEmail", "phoneNumber", "orgPhoneNumber",
            "facilityId", "orgFacilityId", "ssn", "orgSSN", "dobStr", "orgdobStr", "gender", "orgGender",
            "address1", "orgAddress1", "address2", "orgAddress2", "city", "orgCity", "state", "orgState",
            "zip", "orgZip", "country", "orgCountry", "driverBase", "orgDriverBase", "vin", "orgVin",
            "cdlNbr", "orgCdlNbr", "licenseAuthority", "orgLicenseAuthority", "licenseState",
            "orgLicenseState", "licenseExpStr", "orgLicenseExpStr", "orgPwdAcctLocked",
            "orgUnableToUseApp", "orgForcePwdChg", "enabled", "orgEnabled", "lastKnownVehicleId",
            "orgLastKnownVehicleId", "secLvl", "orgSecLvl", "createdDttm", "selectedCompaniesUpdate",
            "selectedRolesUpdate", "updatedFields", "selectedGroupsUpdate",
        };

        /// <summary>Fills gaps when <c>?form</c> JSON omits fields the multipart POST still expects.</summary>
        public static void MergePortalDetail(JObject formRoot, WellRydeUserDetail detail)
        {
            if (formRoot == null || detail == null) return;
            var root = UnwrapFormRoot(formRoot);

            void SetIfMissing(string key, string value)
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                if (FindStringValue(root, key) != null) return;
                root[key] = value;
            }

            SetIfMissing("userId", detail.SecId);
            SetIfMissing("username", detail.Username);
            SetIfMissing("email", detail.Email);
            SetIfMissing("phoneNumber", detail.Phone);
            SetIfMissing("address1", detail.Address1);
            SetIfMissing("address2", detail.Address2);
            SetIfMissing("city", detail.City);
            SetIfMissing("state", detail.State);
            SetIfMissing("zip", detail.Zip);
            SetIfMissing("country", string.IsNullOrWhiteSpace(detail.Country) ? "US" : detail.Country);
            SetIfMissing("vin", detail.VIN);
            SetIfMissing("cdlNbr", detail.CdlNumber);
            SetIfMissing("licenseExpStr", detail.LicenseExpiration);
            SetIfMissing("orgUnableToUseApp", "false");
            SetIfMissing("orgForcePwdChg", "false");
            SetIfMissing("orgEnabled", detail.AccountEnabled ? "true" : "false");

            if (FindStringValue(root, "firstName") == null && !string.IsNullOrWhiteSpace(detail.FullName))
            {
                SplitName(detail.FullName, out var first, out var last);
                SetIfMissing("firstName", first);
                SetIfMissing("lastName", last);
                SetIfMissing("orgFirstName", first);
                SetIfMissing("orgLastName", last);
            }
        }

        private static void SplitName(string fullName, out string first, out string last)
        {
            var parts = (fullName ?? "").Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                first = "";
                last = "";
                return;
            }
            if (parts.Length == 1)
            {
                first = parts[0];
                last = parts[0];
                return;
            }
            last = parts[parts.Length - 1];
            first = string.Join(" ", parts, 0, parts.Length - 1);
        }

        public static Dictionary<string, string> Build(
            JObject formRoot,
            SupeyDriverProfile profile,
            string csrfToken)
        {
            if (formRoot == null) throw new ArgumentNullException(nameof(formRoot));
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            var root = UnwrapFormRoot(formRoot);
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var key in KnownFormKeys)
            {
                string val = FindStringValue(root, key);
                if (val != null)
                    fields[key] = val;
            }

            if (!fields.ContainsKey("userId") || string.IsNullOrWhiteSpace(fields["userId"]))
                fields["userId"] = (profile.WellRydeSecId ?? "").Trim();

            fields["_csrf"] = (csrfToken ?? "").Trim();

            string updatedFields = fields.TryGetValue("updatedFields", out var prevUpdated)
                ? prevUpdated
                : "";

            bool hasHome = !string.IsNullOrWhiteSpace(profile.HomeStreet)
                || !string.IsNullOrWhiteSpace(profile.HomeCity);
            if (hasHome)
            {
                string street = (profile.HomeStreet ?? "").Trim();
                string city = (profile.HomeCity ?? "").Trim();
                string state = NormalizeState(profile.HomeState);
                string zip = (profile.HomeZip ?? "").Trim();
                fields["address1"] = street;
                fields["city"] = city;
                fields["state"] = state;
                fields["zip"] = zip;
                fields["orgAddress1"] = street;
                fields["orgCity"] = city;
                fields["orgState"] = state;
                fields["orgZip"] = zip;
                if (!fields.ContainsKey("country") || string.IsNullOrWhiteSpace(fields["country"]))
                    fields["country"] = "US";
                if (fields.TryGetValue("country", out var country) && !string.IsNullOrWhiteSpace(country))
                    fields["orgCountry"] = country;
                updatedFields = AppendUpdatedFields(updatedFields, "Address1", "City", "State", "Zip");
            }

            string email = (profile.Email ?? "").Trim();
            if (email.Length > 0)
            {
                fields["email"] = email;
                fields["orgEmail"] = email;
                updatedFields = AppendUpdatedFields(updatedFields, "Email");
            }

            if (!string.IsNullOrWhiteSpace(updatedFields))
                fields["updatedFields"] = updatedFields;

            if (fields.TryGetValue("enabled", out var en))
            {
                if (string.Equals(en, "true", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(en, "yes", StringComparison.OrdinalIgnoreCase))
                    fields["enabled"] = "on";
            }

            return fields;
        }

        private static JObject UnwrapFormRoot(JObject root)
        {
            foreach (var path in new[] { "user", "data", "userForm", "form", "model" })
            {
                if (root[path] is JObject child)
                    return child;
            }
            return root;
        }

        private static string FindStringValue(JToken root, string key)
        {
            if (root == null || string.IsNullOrEmpty(key)) return null;
            if (root is JObject o)
            {
                foreach (var p in o.Properties())
                {
                    if (string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase))
                        return TokenToString(p.Value);
                    if (p.Value is JObject || p.Value is JArray)
                    {
                        string nested = FindStringValue(p.Value, key);
                        if (nested != null) return nested;
                    }
                }
            }
            else if (root is JArray arr)
            {
                foreach (var item in arr)
                {
                    string nested = FindStringValue(item, key);
                    if (nested != null) return nested;
                }
            }
            return null;
        }

        private static string TokenToString(JToken tok)
        {
            if (tok == null || tok.Type == JTokenType.Null) return "";
            if (tok.Type == JTokenType.Boolean)
                return tok.Value<bool>() ? "true" : "false";
            return (tok.ToString() ?? "").Trim();
        }

        private static string NormalizeState(string state)
        {
            string s = (state ?? "").Trim();
            if (s.Length == 0) return "maine";
            if (string.Equals(s, "ME", StringComparison.OrdinalIgnoreCase)) return "maine";
            return s;
        }

        private static string AppendUpdatedFields(string existing, params string[] names)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in (existing ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = part.Trim();
                if (t.Length > 0) set.Add(t);
            }
            foreach (var n in names)
                set.Add(n);
            return "," + string.Join(",", set);
        }
    }
}
