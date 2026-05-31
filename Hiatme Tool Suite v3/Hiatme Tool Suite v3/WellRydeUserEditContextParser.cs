using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Parses <c>/portal/users/roles/selected</c> and <c>companies/selected</c> JSON for nuUpdateUser.</summary>
    internal static class WellRydeUserEditContextParser
    {
        public static void MergeIntoFormRoot(JObject formRoot, string rolesSelectedJson, string companiesSelectedJson)
        {
            if (formRoot == null) return;
            var root = Unwrap(formRoot);

            string roles = ParseSelectedIdsCsv(rolesSelectedJson);
            if (roles.Length > 0)
                root["selectedRolesUpdate"] = roles;

            string companies = ParseSelectedCompanyIdsCsv(companiesSelectedJson);
            if (companies.Length > 0)
                root["selectedCompaniesUpdate"] = companies;
        }

        private static JObject Unwrap(JObject root)
        {
            foreach (var path in new[] { "user", "data", "userForm", "form", "model" })
            {
                if (root[path] is JObject child)
                    return child;
            }
            return root;
        }

        private static string ParseSelectedIdsCsv(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return "";
            var ids = new List<string>();
            try
            {
                CollectIds(JToken.Parse(json), ids);
            }
            catch
            {
                return "";
            }
            if (ids.Count == 0) return "";
            return string.Join(",", ids);
        }

        private static string ParseSelectedCompanyIdsCsv(string json)
        {
            var ids = new List<string>();
            if (string.IsNullOrWhiteSpace(json)) return "";
            try
            {
                var tok = JToken.Parse(json);
                CollectSecIds(tok, ids);
            }
            catch
            {
                return "";
            }
            if (ids.Count == 0) return "";
            return string.Join(",", ids);
        }

        private static void CollectIds(JToken tok, List<string> ids)
        {
            if (tok == null || tok.Type == JTokenType.Null) return;
            if (tok.Type == JTokenType.Integer || tok.Type == JTokenType.Float)
            {
                ids.Add(tok.ToString());
                return;
            }
            if (tok.Type == JTokenType.String)
            {
                string s = (tok.ToString() ?? "").Trim();
                if (s.Length > 0 && char.IsDigit(s[0]))
                    ids.Add(s);
                return;
            }
            if (tok is JObject o)
            {
                foreach (var key in new[] { "id", "roleId", "value", "selectedId" })
                {
                    var v = o[key];
                    if (v != null && (v.Type == JTokenType.Integer || v.Type == JTokenType.String))
                    {
                        string s = v.ToString().Trim();
                        if (s.Length > 0 && char.IsDigit(s[0]))
                            ids.Add(s);
                    }
                }
                foreach (var p in o.Properties())
                    CollectIds(p.Value, ids);
                return;
            }
            if (tok is JArray arr)
            {
                foreach (var item in arr)
                    CollectIds(item, ids);
            }
        }

        private static void CollectSecIds(JToken tok, List<string> ids)
        {
            if (tok == null || tok.Type == JTokenType.Null) return;
            if (tok.Type == JTokenType.String)
            {
                string s = (tok.ToString() ?? "").Trim();
                if (s.StartsWith("SEC-", StringComparison.OrdinalIgnoreCase))
                    ids.Add(s);
                return;
            }
            if (tok is JObject o)
            {
                foreach (var key in new[] { "id", "companyId", "secId", "value", "key" })
                {
                    var v = o[key]?.ToString()?.Trim() ?? "";
                    if (v.StartsWith("SEC-", StringComparison.OrdinalIgnoreCase))
                        ids.Add(v);
                }
                foreach (var p in o.Properties())
                    CollectSecIds(p.Value, ids);
                return;
            }
            if (tok is JArray arr)
            {
                foreach (var item in arr)
                    CollectSecIds(item, ids);
            }
        }
    }
}
