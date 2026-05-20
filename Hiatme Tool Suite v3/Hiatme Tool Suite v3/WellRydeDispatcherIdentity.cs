using System;
using Newtonsoft.Json.Linq;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// WellRyde login identity for the company AI (username is usually the dispatcher's name).
    /// </summary>
    internal static class WellRydeDispatcherIdentity
    {
        public static void ApplyToContext(JObject ctx, string username, string companyCode)
        {
            if (ctx == null) return;
            var user = (username ?? "").Trim();
            var company = (companyCode ?? "").Trim();
            if (!string.IsNullOrEmpty(user))
            {
                ctx["dispatcher_username"] = user;
                ctx["dispatcher_display_name"] = user;
            }
            if (!string.IsNullOrEmpty(company))
                ctx["dispatcher_company_code"] = company;
            if (!string.IsNullOrEmpty(user))
                ctx["dispatcher_source"] = "wellryde_tool_suite";
        }

        public static void ApplyFromSettings(JObject ctx)
        {
            ApplyToContext(
                ctx,
                Properties.Settings.Default.wrUserName,
                Properties.Settings.Default.wrCompanyCode);
        }
    }
}
