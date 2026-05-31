using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Resolve WellRyde <c>SEC-...</c> ids from the portal user list when the local roster row
    /// only has a name (manual add or roster saved before SEC ids were stored).
    /// </summary>
    internal static class WellRydeUserSecLookup
    {
        public static async Task<WellRydeUserSummary> FindDriverAsync(
            WellRydePortalSession session,
            SupeyDriverProfile profile,
            CancellationToken cancellationToken = default)
        {
            if (session == null || profile == null) return null;

            string secId = WellRydePortalSession.NormalizeUserSecId(profile.WellRydeSecId);
            if (secId.Length > 0)
            {
                return new WellRydeUserSummary
                {
                    SecId = secId,
                    Username = profile.WellRydeUsername ?? "",
                };
            }

            var summaries = await LoadAllUserSummariesAsync(session, cancellationToken)
                .ConfigureAwait(false);
            return MatchProfile(profile, summaries);
        }

        public static async Task<List<WellRydeUserSummary>> LoadAllUserSummariesAsync(
            WellRydePortalSession session,
            CancellationToken cancellationToken = default)
        {
            var summaries = new List<WellRydeUserSummary>();
            if (session == null) return summaries;

            int page = 1;
            int totalRecords = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var listResult = await session.PostUsersFilterDataAsync(
                    page: page,
                    maxResults: WellRydePortalSession.DefaultUsersFilterMaxResult,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                if (!listResult.IsSuccess)
                    break;

                int pageTotal;
                var pageSummaries = WellRydeUserParser.ParseUsersList(listResult.JsonBody, out pageTotal);
                if (page == 1) totalRecords = pageTotal;
                summaries.AddRange(pageSummaries);
                if (pageSummaries.Count == 0 || summaries.Count >= totalRecords)
                    break;
                page++;
                if (page > 50) break;
            }

            return summaries;
        }

        public static WellRydeUserSummary MatchProfile(
            SupeyDriverProfile profile,
            IList<WellRydeUserSummary> summaries)
        {
            if (profile == null || summaries == null || summaries.Count == 0) return null;

            string user = NormalizeKey(profile.WellRydeUsername);
            if (user.Length > 0)
            {
                foreach (var s in summaries)
                {
                    if (s == null) continue;
                    if (NormalizeKey(s.Username) == user)
                        return s;
                }
            }

            string nameKey = NormalizeKey(profile.Name);
            if (nameKey.Length == 0) return null;

            WellRydeUserSummary best = null;
            foreach (var s in summaries)
            {
                if (s == null) continue;
                if (NormalizeKey(s.FullName) == nameKey)
                    return s;
            }

            return best;
        }

        private static string NormalizeKey(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            return string.Join(" ", raw.Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
                .ToLowerInvariant();
        }
    }
}
