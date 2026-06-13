using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Pushes Supey driver home address and email to WellRyde (<c>POST /portal/users/nuUpdateUser</c>).</summary>
    internal static class WellRydeUserProfileSync
    {
        public sealed class PushResult
        {
            public bool Ok { get; set; }
            public string Message { get; set; }
        }

        public static async Task<PushResult> PushHomeAddressAsync(
            WellRydePortalSession session,
            SupeyDriverProfile profile,
            CancellationToken cancellationToken = default)
        {
            if (session == null)
                return Fail("WellRyde session is not available.");
            if (profile == null)
                return Fail("Driver profile is missing.");
            string secId = WellRydePortalSession.NormalizeUserSecId(profile.WellRydeSecId);
            if (secId.Length == 0)
            {
                var hit = await WellRydeUserSecLookup.FindDriverAsync(session, profile, cancellationToken)
                    .ConfigureAwait(false);
                secId = WellRydePortalSession.NormalizeUserSecId(hit?.SecId);
                if (secId.Length == 0)
                {
                    return Fail(
                        "Could not find \"" + (profile.Name ?? "driver")
                        + "\" on the WellRyde user list — use Pull from WellRyde, or check the name matches the portal.");
                }
                profile.WellRydeSecId = secId;
                if (string.IsNullOrWhiteSpace(profile.WellRydeUsername) && !string.IsNullOrWhiteSpace(hit.Username))
                    profile.WellRydeUsername = hit.Username.Trim();
            }
            else if (!string.Equals(profile.WellRydeSecId, secId, StringComparison.Ordinal))
            {
                profile.WellRydeSecId = secId;
            }

            if (string.IsNullOrWhiteSpace(profile.HomeStreet) && string.IsNullOrWhiteSpace(profile.HomeCity)
                && string.IsNullOrWhiteSpace(profile.Email))
                return Fail("Enter a home address or email before saving to WellRyde.");

            if (string.IsNullOrEmpty(session.GetAjaxCsrfToken()))
            {
                var nu = await session.GetPortalNuAsync(cancellationToken).ConfigureAwait(false);
                if (!nu.IsSuccess)
                    return Fail("Could not load WellRyde portal — sign in from the Login bar.");
            }

            if (!session.HasPortalSessionCookie())
                return Fail("WellRyde sign-in required — use Login → WellRyde.");

            var ctx = await session.LoadUserEditContextAsync(secId, cancellationToken).ConfigureAwait(false);
            if (!ctx.IsSuccess || string.IsNullOrWhiteSpace(ctx.FormJson))
            {
                return Fail(
                    ctx.ErrorMessage
                    ?? "Could not load user edit form from WellRyde (GET ...?form). Sign in and try again.");
            }

            JObject root;
            try
            {
                root = JObject.Parse(ctx.FormJson);
            }
            catch (Exception ex)
            {
                return Fail("WellRyde edit form was not valid JSON: " + ex.Message);
            }

            WellRydeUserEditContextParser.MergeIntoFormRoot(root, ctx.RolesSelectedJson, ctx.CompaniesSelectedJson);

            var detailHtml = await session.GetUserDetailHtmlAsync(secId, cancellationToken)
                .ConfigureAwait(false);
            if (detailHtml.IsSuccess)
            {
                var detail = WellRydeUserParser.ParseUserDetail(secId, detailHtml.HtmlBody);
                WellRydeNuUpdateFormBuilder.MergePortalDetail(root, detail);
            }

            string csrf = session.GetAjaxCsrfToken();
            if (string.IsNullOrEmpty(csrf))
                return Fail("Could not read CSRF token — open WellRyde in the tool (billing/sign-in) and retry.");

            var fields = WellRydeNuUpdateFormBuilder.Build(root, profile, csrf);
            var post = await session.PostNuUpdateUserAsync(fields, cancellationToken)
                .ConfigureAwait(false);
            if (!post.IsSuccess)
            {
                string detail = post.ResponseBody;
                if (!string.IsNullOrWhiteSpace(detail) && detail.Length > 200)
                    detail = detail.Substring(0, 200) + "…";
                return Fail(
                    (post.ErrorMessage ?? "WellRyde rejected the update.")
                    + (string.IsNullOrWhiteSpace(detail) ? "" : " " + detail));
            }

            string wantedEmail = (profile.Email ?? "").Trim();
            if (wantedEmail.Length > 0)
            {
                var verifyHtml = await session.GetUserDetailHtmlAsync(secId, cancellationToken)
                    .ConfigureAwait(false);
                if (verifyHtml.IsSuccess)
                {
                    var verified = WellRydeUserParser.ParseUserDetail(secId, verifyHtml.HtmlBody);
                    string onPortal = (verified.Email ?? "").Trim();
                    if (!string.Equals(onPortal, wantedEmail, StringComparison.OrdinalIgnoreCase))
                    {
                        return Fail(
                            "WellRyde did not apply the email change (portal still shows "
                            + (string.IsNullOrEmpty(onPortal) ? "empty" : onPortal) + ").");
                    }
                }
            }

            profile.WellRydeSyncedAtUtc = DateTime.UtcNow;
            bool hasHome = !string.IsNullOrWhiteSpace(profile.HomeStreet)
                || !string.IsNullOrWhiteSpace(profile.HomeCity);
            bool hasEmail = !string.IsNullOrWhiteSpace(profile.Email);
            string saved = hasHome && hasEmail
                ? "Home address and email saved to WellRyde for "
                : hasEmail
                    ? "Email saved to WellRyde for "
                    : "Home address saved to WellRyde for ";
            return new PushResult
            {
                Ok = true,
                Message = saved + (profile.Name ?? "driver") + ".",
            };
        }

        private static PushResult Fail(string msg) =>
            new PushResult { Ok = false, Message = msg ?? "WellRyde update failed." };
    }
}
