using System.Collections.Generic;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Lightweight user record produced by parsing the WellRyde portal users list
    /// (<c>POST /portal/filterdata</c> with <c>listDefId=SEC-...</c>). Just enough to filter the
    /// roster (Enabled / Locked) and chain into a per-user detail fetch.
    /// </summary>
    /// <remarks>
    /// The portal list endpoint returns names, email, and an opaque <c>SEC-</c> id but no address.
    /// Use <see cref="WellRydePortalSession.GetUserDetailHtmlAsync(string, System.Threading.CancellationToken)"/>
    /// for each <see cref="SecId"/> to get home address, vehicle, license, and roles.
    /// </remarks>
    internal sealed class WellRydeUserSummary
    {
        public string SecId { get; set; } = "";
        public string Username { get; set; } = "";
        public string FirstName { get; set; } = "";
        public string MiddleName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string Email { get; set; } = "";

        /// <summary>UserEnabled bit on the portal list ("yes" if true).</summary>
        public bool Enabled { get; set; }

        /// <summary>UserPwdAcctLocked bit on the portal list ("yes" if true). Inactive accounts.</summary>
        public bool Locked { get; set; }

        /// <summary>
        /// Per-user eligibility rule the dispatcher gave us: enabled AND not locked. Matches the
        /// portal's "can drive" check used by Trip Scout and the Supey schedule builder.
        /// </summary>
        public bool IsEligibleForSchedule => Enabled && !Locked;

        public string FullName
        {
            get
            {
                string mid = string.IsNullOrWhiteSpace(MiddleName) ? "" : (" " + MiddleName.Trim());
                return ((FirstName ?? "") + mid + " " + (LastName ?? "")).Trim();
            }
        }
    }
}
