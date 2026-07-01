using System.Threading;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Loads a full <see cref="WellRydeUserDetail"/> from the portal, combining list summary,
    /// detail HTML, and edit-form JSON. The user list email is the baseline — detail/form overlay it.
    /// </summary>
    internal static class WellRydeUserDetailLoader
    {
        public static async Task<WellRydeUserDetail> LoadAsync(
            WellRydePortalSession session,
            string secId,
            WellRydeUserSummary summaryFallback = null,
            CancellationToken cancellationToken = default)
        {
            secId = WellRydePortalSession.NormalizeUserSecId(secId);
            var detail = BuildFromSummary(summaryFallback, secId);

            if (session == null || secId.Length == 0)
                return detail;

            try
            {
                var htmlRes = await session.GetUserDetailHtmlAsync(secId, cancellationToken)
                    .ConfigureAwait(false);
                if (htmlRes.IsSuccess && !string.IsNullOrWhiteSpace(htmlRes.HtmlBody))
                    OverlayDetail(detail, WellRydeUserParser.ParseUserDetail(secId, htmlRes.HtmlBody));
            }
            catch
            {
                // Detail HTML is best-effort — list summary + form still apply.
            }

            if (string.IsNullOrWhiteSpace(detail.Email))
            {
                try
                {
                    var formRes = await session.GetUserEditFormJsonAsync(secId, cancellationToken)
                        .ConfigureAwait(false);
                    if (formRes.IsSuccess)
                    {
                        string formEmail = WellRydeUserParser.TryNormalizeEmail(
                            WellRydeUserParser.ParseEmailFromEditFormJson(formRes.JsonBody));
                        if (formEmail.Length > 0)
                            detail.Email = formEmail;
                    }
                }
                catch
                {
                    // Form JSON often needs admin session — list email is the reliable fallback.
                }
            }

            FinalizeDetail(detail, summaryFallback, secId);
            return detail;
        }

        /// <summary>Minimum detail from the filterdata list row — always includes list email when present.</summary>
        public static WellRydeUserDetail BuildFromSummary(WellRydeUserSummary summary, string secId = null)
        {
            secId = WellRydePortalSession.NormalizeUserSecId(secId ?? summary?.SecId ?? "");
            var detail = new WellRydeUserDetail { SecId = secId };
            ApplySummaryFallback(detail, summary);
            detail.Email = WellRydeUserParser.TryNormalizeEmail(detail.Email);
            return detail;
        }

        internal static void ApplySummaryFallback(WellRydeUserDetail detail, WellRydeUserSummary summary)
        {
            if (detail == null || summary == null)
                return;

            if (string.IsNullOrWhiteSpace(detail.SecId))
                detail.SecId = WellRydePortalSession.NormalizeUserSecId(summary.SecId);
            if (string.IsNullOrWhiteSpace(detail.Username))
                detail.Username = summary.Username;
            if (string.IsNullOrWhiteSpace(detail.FullName))
                detail.FullName = summary.FullName;
            if (string.IsNullOrWhiteSpace(detail.Email))
                detail.Email = WellRydeUserParser.TryNormalizeEmail(summary.Email);
            detail.AccountEnabled = summary.Enabled;
            detail.AccountLocked = summary.Locked;
        }

        private static void OverlayDetail(WellRydeUserDetail target, WellRydeUserDetail parsed)
        {
            if (target == null || parsed == null)
                return;

            if (!string.IsNullOrWhiteSpace(parsed.Username)) target.Username = parsed.Username.Trim();
            if (!string.IsNullOrWhiteSpace(parsed.FullName)) target.FullName = parsed.FullName.Trim();
            string email = WellRydeUserParser.TryNormalizeEmail(parsed.Email);
            if (email.Length > 0) target.Email = email;
            if (!string.IsNullOrWhiteSpace(parsed.Phone)) target.Phone = parsed.Phone.Trim();
            if (!string.IsNullOrWhiteSpace(parsed.Address1)) target.Address1 = parsed.Address1;
            if (!string.IsNullOrWhiteSpace(parsed.Address2)) target.Address2 = parsed.Address2;
            if (!string.IsNullOrWhiteSpace(parsed.City)) target.City = parsed.City.Trim();
            if (!string.IsNullOrWhiteSpace(parsed.State)) target.State = parsed.State.Trim();
            if (!string.IsNullOrWhiteSpace(parsed.Zip)) target.Zip = parsed.Zip.Trim();
            if (!string.IsNullOrWhiteSpace(parsed.Country)) target.Country = parsed.Country.Trim();
            if (!string.IsNullOrWhiteSpace(parsed.VehicleLabel)) target.VehicleLabel = parsed.VehicleLabel.Trim();
            if (!string.IsNullOrWhiteSpace(parsed.VIN)) target.VIN = parsed.VIN.Trim();
            if (parsed.Roles != null && parsed.Roles.Count > 0)
                target.Roles = parsed.Roles;
            target.AccountEnabled = parsed.AccountEnabled;
            target.AccountLocked = parsed.AccountLocked;
        }

        private static void FinalizeDetail(WellRydeUserDetail detail, WellRydeUserSummary summary, string secId)
        {
            if (detail == null)
                return;

            detail.SecId = WellRydePortalSession.NormalizeUserSecId(
                string.IsNullOrWhiteSpace(detail.SecId) ? secId : detail.SecId);
            ApplySummaryFallback(detail, summary);
            detail.Email = WellRydeUserParser.TryNormalizeEmail(detail.Email);
        }
    }
}
