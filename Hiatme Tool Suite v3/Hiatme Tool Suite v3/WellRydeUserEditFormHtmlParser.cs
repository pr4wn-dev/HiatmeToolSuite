using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Portal <c>GET /portal/users/{sec}?form</c> returns HTML (Fiddler May 2026), not JSON.
    /// Extracts <c>&lt;form id="user"&gt;</c> fields for <see cref="WellRydeNuUpdateFormBuilder"/>.
    /// </summary>
    internal static class WellRydeUserEditFormHtmlParser
    {
        private static readonly Regex InputTagRegex = new Regex(
            @"<input\b([^>]*)/?>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex SelectTagRegex = new Regex(
            @"<select\b([^>]*)>([\s\S]*?)</select>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex SelectedOptionRegex = new Regex(
            @"<option\b([^>]*selected[^>]*)>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static bool IsUserEditFormHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return false;
            return html.IndexOf("id=\"user\"", StringComparison.OrdinalIgnoreCase) >= 0
                && html.IndexOf("nuUpdateUser", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Build a flat JSON object of form field names → values (for <c>JObject.Parse</c>).</summary>
        public static string ToFormJson(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return "{}";

            string section = ExtractUserFormSection(html);
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (Match m in InputTagRegex.Matches(section))
            {
                string attrs = m.Groups[1].Value;
                string name = GetAttr(attrs, "name");
                if (string.IsNullOrEmpty(name))
                    continue;

                string type = (GetAttr(attrs, "type") ?? "text").Trim();
                if (string.Equals(type, "file", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(type, "button", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(type, "submit", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (string.Equals(type, "checkbox", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(type, "radio", StringComparison.OrdinalIgnoreCase))
                {
                    if (attrs.IndexOf("checked", StringComparison.OrdinalIgnoreCase) >= 0)
                        fields[name] = string.Equals(type, "checkbox", StringComparison.OrdinalIgnoreCase) ? "on" : (GetAttr(attrs, "value") ?? "");
                    continue;
                }

                fields[name] = GetAttr(attrs, "value") ?? "";
            }

            foreach (Match m in SelectTagRegex.Matches(section))
            {
                string name = GetAttr(m.Groups[1].Value, "name");
                if (string.IsNullOrEmpty(name))
                    continue;
                foreach (Match opt in SelectedOptionRegex.Matches(m.Groups[2].Value))
                {
                    string val = GetAttr(opt.Groups[1].Value, "value");
                    if (val != null)
                    {
                        fields[name] = val;
                        break;
                    }
                }
            }

            return new JObject(
                fields.Select(kv => new JProperty(kv.Key, kv.Value))).ToString(Newtonsoft.Json.Formatting.None);
        }

        private static string ExtractUserFormSection(string html)
        {
            int userForm = html.IndexOf("id=\"user\"", StringComparison.OrdinalIgnoreCase);
            if (userForm < 0)
                return html;

            int formStart = html.LastIndexOf("<form", userForm, StringComparison.OrdinalIgnoreCase);
            if (formStart < 0)
                formStart = userForm;

            int formEnd = html.IndexOf("</form>", userForm, StringComparison.OrdinalIgnoreCase);
            if (formEnd > formStart)
                return html.Substring(formStart, formEnd - formStart + "</form>".Length);

            return html.Substring(formStart);
        }

        private static string GetAttr(string attrs, string attrName)
        {
            if (string.IsNullOrEmpty(attrs) || string.IsNullOrEmpty(attrName))
                return null;

            Match m = Regex.Match(attrs,
                @"\b" + Regex.Escape(attrName) + @"\s*=\s*""([^""]*)""",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (m.Success)
                return WebUtility.HtmlDecode(m.Groups[1].Value);

            m = Regex.Match(attrs,
                @"\b" + Regex.Escape(attrName) + @"\s*=\s*'([^']*)'",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (m.Success)
                return WebUtility.HtmlDecode(m.Groups[1].Value);

            return null;
        }
    }
}
