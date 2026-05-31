using System;
using System.Collections.Generic;

namespace Hiatme_Tool_Suite_v3
{
    internal static class SupeyTemplateDriverNames
    {
        public static string MapTabToRoster(string templateTab, IList<SupeyDriverProfile> roster)
        {
            if (string.IsNullOrWhiteSpace(templateTab) || roster == null)
                return null;

            foreach (var d in roster)
            {
                if (d == null || string.IsNullOrWhiteSpace(d.Name)) continue;
                if (string.Equals(d.Name.Trim(), templateTab.Trim(), StringComparison.OrdinalIgnoreCase))
                    return d.Name;
            }

            var tParts = SplitName(templateTab);
            if (tParts.Length < 2)
                return null;

            string tFirst = tParts[0];
            string tLastToken = tParts[tParts.Length - 1];
            if (tLastToken.Length == 0)
                return null;

            foreach (var d in roster)
            {
                if (d == null || string.IsNullOrWhiteSpace(d.Name)) continue;
                var rParts = SplitName(d.Name);
                if (rParts.Length < 2) continue;
                if (!string.Equals(rParts[0], tFirst, StringComparison.OrdinalIgnoreCase))
                    continue;
                string rLast = rParts[rParts.Length - 1];
                if (rLast.Length > 0 && rLast[0] == tLastToken[0])
                    return d.Name;
            }

            return null;
        }

        private static string[] SplitName(string name)
        {
            return (name ?? "").Trim()
                .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
