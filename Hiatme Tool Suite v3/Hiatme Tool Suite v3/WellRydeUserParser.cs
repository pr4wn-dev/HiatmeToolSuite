using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Parses WellRyde portal user payloads into the typed DTOs we feed the schedule builder.
    /// Two surfaces:
    /// <list type="bullet">
    /// <item><description><see cref="ParseUsersList"/> — the JSON returned by
    /// <c>POST /portal/filterdata</c> with the user <c>listDefId</c>. Each row is a dictionary of
    /// named columns (UserUsername / UserFirstName / etc.) — much easier than the array-row trip
    /// list shape, but we still have to dig into the embedded link blob for the SEC- id.</description></item>
    /// <item><description><see cref="ParseUserDetail"/> — the HTML returned by
    /// <c>GET /portal/users/{secId}</c>. The detail page uses a stable
    /// <c>&lt;div class="display-group"&gt;&lt;label class="display-label"&gt;X&lt;/label&gt;&lt;span class="display-value"&gt;Y&lt;/span&gt;&lt;/div&gt;</c>
    /// pattern, so we regex out label→value pairs into a dictionary, then fish out the fields we
    /// care about by label name.</description></item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// We deliberately avoid HtmlAgilityPack so we don't drag a new package dependency into the
    /// solution. The detail HTML is mechanically generated and tightly structured; regex over a
    /// well-known template is robust enough here.
    /// </remarks>
    internal static class WellRydeUserParser
    {
        // <div class="display-group">
        //   <label class="display-label">Address1</label>
        //   <span class="display-value" title="...">112 Newbury St</span>
        // </div>
        //
        // The label is plain text; the span sometimes carries a title attribute and sometimes
        // doesn't. We allow either, and we match across newlines because the portal pretty-prints
        // its HTML with one element per line.
        private static readonly Regex DisplayGroupRegex = new Regex(
            @"<label\s+class=""display-label"">\s*([^<]+?)\s*</label>\s*<span\s+class=""display-value""(?:\s+[^>]*)?>\s*(.*?)\s*</span>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        // <div>
        //   <h3>User Roles</h3>
        //   <span>MemberAdmin</span>
        //   , <span>User</span>
        //   ...
        // </div>
        // We find the User Roles header and then capture every <span>...</span> until the next h3.
        private static readonly Regex UserRolesBlockRegex = new Regex(
            @"<h3>\s*User\s+Roles\s*</h3>(?<body>.*?)(?:<h3>|</div>)",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        private static readonly Regex SpanInnerRegex = new Regex(
            @"<span[^>]*>\s*([^<]+?)\s*</span>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        // <div class="user-profile"> ... <h3>Jeffrey J Brown</h3> ... </div> — that h3 is the
        // canonical "real name" rendering used on the detail page. Distinct from the smaller h3s
        // for "Signature Picture" / "User Roles" / etc.
        private static readonly Regex UserProfileFullNameRegex = new Regex(
            @"<div\s+class=""user-profile"">.*?<h3>\s*([^<]+?)\s*</h3>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        // ---------------- LIST ----------------

        /// <summary>
        /// Parses the <c>filterData</c> array from <c>/portal/filterdata</c> for the user list.
        /// Each row is an object keyed by column name (<c>UserFirstName</c> etc.) — see the
        /// <c>defaultViewColumnDescriptorJsonObj</c> on <c>listFilterDefsJson</c> for the full
        /// column set. The portal serializes booleans as either <c>true</c>/<c>false</c> or the
        /// empty string for "unset", so we coerce defensively.
        /// </summary>
        /// <returns>Empty list on parse failure — never null.</returns>
        public static List<WellRydeUserSummary> ParseUsersList(string filterDataJson, out int totalRecords)
        {
            totalRecords = 0;
            var list = new List<WellRydeUserSummary>();
            if (string.IsNullOrWhiteSpace(filterDataJson))
                return list;

            JObject root;
            try
            {
                root = JObject.Parse(filterDataJson);
            }
            catch
            {
                return list;
            }

            totalRecords = root["totalRecords"]?.Value<int?>() ?? 0;
            var values = root["filterData"] as JArray;
            if (values == null)
                return list;

            foreach (var rowToken in values)
            {
                var row = rowToken as JObject;
                if (row == null)
                    continue;

                var summary = new WellRydeUserSummary
                {
                    SecId = ExtractSecIdFromUsernameCell(GetCellValue(row, "UserUsername")),
                    Username = ExtractUsernameFromUsernameCell(GetCellValue(row, "UserUsername"), GetCellValue(row, "SelectColumn")),
                    FirstName = GetCellValue(row, "UserFirstName"),
                    MiddleName = GetCellValue(row, "UserMiddleName"),
                    LastName = GetCellValue(row, "UserLastName"),
                    Email = GetCellValue(row, "UserEmail"),
                    Enabled = ParseCellBool(row, "UserEnabled"),
                    Locked = ParseCellBool(row, "UserPwdAcctLocked"),
                };

                // KeyColumn is the canonical SEC- id; fall back to it if Username didn't carry the link blob.
                if (string.IsNullOrEmpty(summary.SecId))
                    summary.SecId = GetCellValue(row, "KeyColumn");

                if (!string.IsNullOrEmpty(summary.SecId))
                    list.Add(summary);
            }

            return list;
        }

        private static string GetCellValue(JObject row, string columnKey)
        {
            var cell = row[columnKey];
            if (cell == null)
                return "";

            if (cell.Type == JTokenType.Object)
            {
                var v = cell["value"];
                if (v == null) return "";
                if (v.Type == JTokenType.Boolean) return v.Value<bool>() ? "true" : "false";
                if (v.Type == JTokenType.Null) return "";
                return (v.ToString() ?? "").Trim();
            }
            return (cell.ToString() ?? "").Trim();
        }

        private static bool ParseCellBool(JObject row, string columnKey)
        {
            // The portal sometimes emits booleans (true/false) and sometimes the empty string for
            // "false" — treat the empty string as false.
            var cell = row[columnKey];
            if (cell == null || cell.Type != JTokenType.Object) return false;
            var v = cell["value"];
            if (v == null) return false;
            if (v.Type == JTokenType.Boolean) return v.Value<bool>();
            string s = v.ToString();
            if (string.IsNullOrEmpty(s)) return false;
            bool parsed;
            return bool.TryParse(s, out parsed) && parsed;
        }

        private static string ExtractSecIdFromUsernameCell(string raw)
        {
            // Username cell value is itself a JSON blob like
            // {"colmnLinkId":"SEC-W-lHn9ad26_xTsgT5HZhEA","columnValue":"jbrown","columnLink":"users/{itemId}"}
            if (string.IsNullOrWhiteSpace(raw)) return "";
            try
            {
                var o = JObject.Parse(raw);
                return (o["colmnLinkId"]?.ToString() ?? "").Trim();
            }
            catch
            {
                return "";
            }
        }

        private static string ExtractUsernameFromUsernameCell(string raw, string fallback)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return (fallback ?? "").Trim();
            try
            {
                var o = JObject.Parse(raw);
                string v = (o["columnValue"]?.ToString() ?? "").Trim();
                return v.Length > 0 ? v : (fallback ?? "").Trim();
            }
            catch
            {
                return (fallback ?? "").Trim();
            }
        }

        // ---------------- DETAIL ----------------

        /// <summary>
        /// Walks the detail-page HTML, building a label→value dictionary off the
        /// <c>display-group</c> blocks, then maps the labels we care about into a typed
        /// <see cref="WellRydeUserDetail"/>. Roles come from the separate User Roles section
        /// because they're rendered as a comma-separated span list rather than a label/value pair.
        /// </summary>
        /// <returns>
        /// A populated detail with <see cref="WellRydeUserDetail.SecId"/> echoed back. Returns a
        /// best-effort detail even if the page is missing fields; never throws or returns null.
        /// </returns>
        public static WellRydeUserDetail ParseUserDetail(string secId, string html)
        {
            var d = new WellRydeUserDetail { SecId = secId ?? "" };
            if (string.IsNullOrWhiteSpace(html))
                return d;

            // Build the label→value dictionary first. Last-write-wins doesn't matter — labels are
            // unique inside the detail-page form.
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in DisplayGroupRegex.Matches(html))
            {
                string label = WebUtility.HtmlDecode((m.Groups[1].Value ?? "").Trim());
                string value = WebUtility.HtmlDecode((m.Groups[2].Value ?? "").Trim());
                // Strip stray whitespace runs introduced by the portal's pretty-printing.
                value = Regex.Replace(value, @"\s+", " ").Trim();
                if (label.Length > 0)
                    fields[label] = value;
            }

            d.Username = GetField(fields, "Username");
            d.Email = GetField(fields, "Email");
            d.Phone = GetField(fields, "User Phone Number");

            d.Address1 = GetField(fields, "Address1");
            d.Address2 = GetField(fields, "Address2");
            d.City = GetField(fields, "City");
            d.State = GetField(fields, "User State");
            d.Zip = GetField(fields, "ZIP Code");
            d.Country = GetField(fields, "User Country");

            d.VIN = GetField(fields, "Vehicle identification number (VIN)");
            d.CdlNumber = GetField(fields, "CDL Number");
            d.LicenseExpiration = GetField(fields, "License Expiration Date");

            d.AccountEnabled = string.Equals(GetField(fields, "Account Enabled?"), "yes", StringComparison.OrdinalIgnoreCase);
            d.AccountLocked = string.Equals(GetField(fields, "Account Locked?"), "yes", StringComparison.OrdinalIgnoreCase);

            // Vehicle label preference order:
            //   1. "Last known vehicle"  — the human plate-tail dispatch crews actually call out
            //   2. last 4 of VIN          — fallback so we always have *something* to show
            string lastKnown = GetField(fields, "Last known vehicle");
            if (!string.IsNullOrWhiteSpace(lastKnown))
            {
                d.VehicleLabel = lastKnown;
            }
            else if (!string.IsNullOrWhiteSpace(d.VIN) && d.VIN.Length >= 4)
            {
                d.VehicleLabel = d.VIN.Substring(d.VIN.Length - 4);
            }

            // Full name: prefer the user-profile h3 (canonical), fall back to assembling from parts.
            var nameMatch = UserProfileFullNameRegex.Match(html);
            if (nameMatch.Success)
            {
                d.FullName = WebUtility.HtmlDecode((nameMatch.Groups[1].Value ?? "").Trim());
            }
            if (string.IsNullOrWhiteSpace(d.FullName))
            {
                // Composite from list-style fields if we got them.
                d.FullName = ((GetField(fields, "First Name") + " " + GetField(fields, "Last Name")).Trim());
            }

            // Roles.
            var rolesMatch = UserRolesBlockRegex.Match(html);
            if (rolesMatch.Success)
            {
                string body = rolesMatch.Groups["body"].Value ?? "";
                foreach (Match sm in SpanInnerRegex.Matches(body))
                {
                    string role = WebUtility.HtmlDecode((sm.Groups[1].Value ?? "").Trim());
                    // Strip stray punctuation (the portal renders "<span>A</span>, <span>B</span>")
                    role = role.Trim(',', ' ').Trim();
                    if (role.Length > 0 && !d.Roles.Contains(role))
                        d.Roles.Add(role);
                }
            }

            return d;
        }

        private static string GetField(Dictionary<string, string> fields, string label)
        {
            string v;
            return fields.TryGetValue(label, out v) ? (v ?? "") : "";
        }
    }
}
