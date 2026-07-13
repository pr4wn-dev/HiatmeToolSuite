using System;
using System.Collections.Generic;
using System.Linq;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Preserves workbook / UI tab order for Schedule Builder load, preview, and save.</summary>
    internal static class ScheduleBuilderTabOrder
    {
        /// <summary>
        /// Reserves first, then drivers in the given sequence (not A–Z).
        /// Pass template / UI / workbook order here — do not alphabetize first.
        /// </summary>
        public static List<string> DefaultBuildTabOrder(IEnumerable<string> driverNames)
        {
            var drivers = DedupePreserveOrder(driverNames)
                .Where(n => !n.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var tabs = new List<string>(drivers.Count + 1) { "Reserves" };
            tabs.AddRange(drivers);
            return tabs;
        }

        /// <summary>Driver tabs only — never includes Reserves.</summary>
        public static List<string> OrderDriverNames(
            IEnumerable<string> driverNames,
            IReadOnlyList<string> preferredOrder)
        {
            if (driverNames == null)
                return new List<string>();

            var available = DedupePreserveOrder(driverNames)
                .Where(n => !n.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
                .ToList();

            return NormalizeFullTabOrder(preferredOrder, available)
                .Where(n => !n.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        /// <summary>
        /// Merge preferred order with available keys.
        /// Unknown preferred names are skipped; leftover keys keep their available order (not A–Z).
        /// </summary>
        public static List<string> NormalizeFullTabOrder(
            IReadOnlyList<string> preferredOrder,
            IEnumerable<string> availableKeys)
        {
            var available = DedupePreserveOrder(availableKeys);
            if (available.Count == 0)
                return new List<string>();

            var canonical = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in available)
                canonical[key] = key;

            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (preferredOrder != null)
            {
                foreach (var name in preferredOrder)
                {
                    if (string.IsNullOrWhiteSpace(name) || !seen.Add(name))
                        continue;
                    if (!canonical.TryGetValue(name, out string actual))
                        continue;
                    result.Add(actual);
                }
            }

            foreach (var key in available)
            {
                if (seen.Add(key))
                    result.Add(key);
            }

            return result;
        }

        public static IEnumerable<string> OrderedKeys<T>(
            IReadOnlyDictionary<string, T> dict,
            IReadOnlyList<string> tabOrder)
        {
            if (dict == null || dict.Count == 0)
                yield break;

            foreach (var key in NormalizeFullTabOrder(tabOrder, dict.Keys))
                yield return key;
        }

        public static int CompareByTabOrder(IReadOnlyList<string> order, string a, string b)
        {
            a = a ?? "";
            b = b ?? "";
            int ia = IndexOfTab(order, a);
            int ib = IndexOfTab(order, b);
            if (ia >= 0 && ib >= 0)
                return ia.CompareTo(ib);
            if (ia >= 0)
                return -1;
            if (ib >= 0)
                return 1;
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static List<string> DedupePreserveOrder(IEnumerable<string> names)
        {
            var result = new List<string>();
            if (names == null)
                return result;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in names)
            {
                if (string.IsNullOrWhiteSpace(name) || !seen.Add(name))
                    continue;
                result.Add(name);
            }

            return result;
        }

        private static int IndexOfTab(IReadOnlyList<string> order, string name)
        {
            if (order == null || string.IsNullOrWhiteSpace(name))
                return -1;

            for (int i = 0; i < order.Count; i++)
            {
                if (order[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return -1;
        }
    }
}
