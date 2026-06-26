using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Submit trip reroute requests to Modivcare (transportationco.logisticare.com).</summary>
    internal static class MCTripRerouter
    {
        private const string BaseUrl = "https://transportationco.logisticare.com";
        private const string TripReroutesUrl = BaseUrl + "/TripReroutes.aspx";
        private const string ReroutesUrl = BaseUrl + "/Reroutes.aspx";
        private const string RerouteFinishUrl = BaseUrl + "/RerouteFinish.aspx";

        /// <summary>Modivcare server id on Trip Reroutes (TP portal).</summary>
        private const string DefaultServerId = "01";

        /// <summary>Default reroute reason: 1 = Not in Service Area.</summary>
        public const string DefaultRerouteReasonCode = "1";

        public sealed class Result
        {
            public bool Success { get; set; }
            public string Message { get; set; }
        }

        public enum RerouteProbeOutcome
        {
            StillOnCompany,
            AlreadyRerouted,
            LookupFailed,
        }

        public sealed class ProbeResult
        {
            public RerouteProbeOutcome Outcome { get; set; }
            public string Message { get; set; }
        }

        /// <summary>Modivcare shows this on TripReroutes.aspx when the trip was already rerouted away.</summary>
        internal const string NotAssignedToCompanyFragment = "not assigned to your company";

        /// <summary>
        /// TripReroutes.aspx → Reroutes.aspx → RerouteFinish.aspx (same flow as the website).
        /// </summary>
        public static async Task<Result> SubmitRerouteAsync(
            MCLoginHandler login,
            MCDownloadedTrip trip,
            string rerouteReasonCode = DefaultRerouteReasonCode,
            DateTime? serviceDateFallback = null)
        {
            if (login == null || !login.Connected)
            {
                return Fail("Modivcare is not signed in.");
            }

            if (trip == null || string.IsNullOrWhiteSpace(trip.TripNumber))
            {
                return Fail("Trip number is missing.");
            }

            if (!TryParseTripFields(trip, serviceDateFallback, out string tripNumber, out string tripLeg, out string tripDate, out string parseError))
            {
                return Fail(parseError);
            }

            rerouteReasonCode = (rerouteReasonCode ?? DefaultRerouteReasonCode).Trim();
            if (rerouteReasonCode.Length == 0)
                rerouteReasonCode = DefaultRerouteReasonCode;

            try
            {
                string tripReroutesHtml = await GetPageHtmlAsync(login, TripReroutesUrl, TripReroutesUrl)
                    .ConfigureAwait(false);
                login.GrabTokens(tripReroutesHtml);

                string reroutesHtml;
                var tripSelectResult = await PostFormAsync(
                    login,
                    TripReroutesUrl,
                    TripReroutesUrl,
                    () => BuildTripReroutesSelectForm(login, tripNumber, tripLeg, tripDate))
                    .ConfigureAwait(false);
                reroutesHtml = tripSelectResult.Body;
                if (tripSelectResult.AuthRedirect)
                    throw new ModivcareSessionExpiredException();

                if (UriIsTripReroutesPage(tripSelectResult.FinalUri))
                {
                    string err = TryExtractPageError(reroutesHtml);
                    return Fail(string.IsNullOrEmpty(err)
                        ? "Modivcare rejected the trip lookup for " + (trip.TripNumber ?? "trip") + "."
                        : err);
                }

                if (!UriIsReroutesLandingPage(tripSelectResult.FinalUri)
                    && reroutesHtml.IndexOf("Reroute Trip", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    string err = TryExtractPageError(reroutesHtml);
                    return Fail(string.IsNullOrEmpty(err)
                        ? "Modivcare did not open the reroute form for trip " + trip.TripNumber + "."
                        : err);
                }

                login.GrabTokens(reroutesHtml);

                var rerouteResult = await PostFormAsync(
                    login,
                    ReroutesUrl,
                    ReroutesUrl,
                    () => BuildReroutesSubmitForm(login, rerouteReasonCode))
                    .ConfigureAwait(false);
                string finishHtml = rerouteResult.Body;
                if (rerouteResult.AuthRedirect)
                    throw new ModivcareSessionExpiredException();

                if (!LooksLikeRerouteSuccess(rerouteResult.FinalUri, finishHtml))
                {
                    string err = TryExtractPageError(finishHtml);
                    return Fail(string.IsNullOrEmpty(err)
                        ? "Modivcare did not confirm the reroute for trip " + trip.TripNumber + "."
                        : err);
                }

                return new Result
                {
                    Success = true,
                    Message = "Trip " + trip.TripNumber + " rerouted on Modivcare.",
                };
            }
            catch (ModivcareSessionExpiredException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Fail(ModivcareRequestErrors.DescribeOrDefault(ex, "Reroute request failed."));
            }
        }

        /// <summary>
        /// Trip lookup only (TripReroutes.aspx step 1) — never submits the reroute form.
        /// </summary>
        public static async Task<ProbeResult> ProbeRerouteStatusAsync(
            MCLoginHandler login,
            MCDownloadedTrip trip,
            DateTime? serviceDateFallback = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (login == null)
                throw new ArgumentNullException(nameof(login));

            if (!login.Connected)
                throw new ModivcareSessionExpiredException();

            if (trip == null || string.IsNullOrWhiteSpace(trip.TripNumber))
            {
                return new ProbeResult
                {
                    Outcome = RerouteProbeOutcome.LookupFailed,
                    Message = "Trip number is missing.",
                };
            }

            if (!TryParseTripFields(trip, serviceDateFallback, out string tripNumber, out string tripLeg, out string tripDate, out string parseError))
            {
                return new ProbeResult
                {
                    Outcome = RerouteProbeOutcome.LookupFailed,
                    Message = parseError,
                };
            }

            try
            {
                string tripReroutesHtml = await GetPageHtmlAsync(login, TripReroutesUrl, TripReroutesUrl)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                login.GrabTokens(tripReroutesHtml);

                var tripSelectResult = await PostFormAsync(
                    login,
                    TripReroutesUrl,
                    TripReroutesUrl,
                    () => BuildTripReroutesSelectForm(login, tripNumber, tripLeg, tripDate))
                    .ConfigureAwait(false);
                if (tripSelectResult.AuthRedirect)
                    throw new ModivcareSessionExpiredException();

                string html = tripSelectResult.Body;
                string finalUri = tripSelectResult.FinalUri ?? TripReroutesUrl;

                if (LooksLikeAlreadyRerouted(finalUri, html))
                {
                    return new ProbeResult
                    {
                        Outcome = RerouteProbeOutcome.AlreadyRerouted,
                        Message = TryExtractPageError(html),
                    };
                }

                if (LooksLikeStillOnCompany(finalUri, html))
                {
                    return new ProbeResult
                    {
                        Outcome = RerouteProbeOutcome.StillOnCompany,
                    };
                }

                return new ProbeResult
                {
                    Outcome = RerouteProbeOutcome.LookupFailed,
                    Message = TryExtractPageError(html)
                        ?? "Modivcare did not return a clear reroute status for trip " + trip.TripNumber + ".",
                };
            }
            catch (ModivcareSessionExpiredException)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new ProbeResult
                {
                    Outcome = RerouteProbeOutcome.LookupFailed,
                    Message = ModivcareRequestErrors.DescribeOrDefault(ex, "Reroute status check failed."),
                };
            }
        }

        /// <summary>Trip lookup page — not the same as <see cref="UriIsReroutesLandingPage"/> (TripReroutes contains "Reroutes.aspx").</summary>
        internal static bool UriIsTripReroutesPage(string uri) =>
            !string.IsNullOrEmpty(uri)
            && uri.IndexOf("TripReroutes.aspx", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Reroute form landing page after a successful trip lookup.</summary>
        internal static bool UriIsReroutesLandingPage(string uri)
        {
            if (string.IsNullOrEmpty(uri))
                return false;
            return uri.IndexOf("Reroutes.aspx", StringComparison.OrdinalIgnoreCase) >= 0
                && uri.IndexOf("TripReroutes.aspx", StringComparison.OrdinalIgnoreCase) < 0;
        }

        internal static bool LooksLikeAlreadyRerouted(string finalUri, string html)
        {
            if (string.IsNullOrEmpty(html) || !UriIsTripReroutesPage(finalUri))
                return false;

            string err = TryExtractPageError(html);
            if (!string.IsNullOrEmpty(err)
                && err.IndexOf(NotAssignedToCompanyFragment, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return html.IndexOf(NotAssignedToCompanyFragment, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static bool LooksLikeStillOnCompany(string finalUri, string html)
        {
            if (UriIsReroutesLandingPage(finalUri))
                return true;

            return !string.IsNullOrEmpty(html)
                && html.IndexOf("Reroute Trip", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool TryParseTripFields(
            MCDownloadedTrip trip,
            DateTime? serviceDateFallback,
            out string tripNumber,
            out string tripLeg,
            out string tripDate,
            out string error)
        {
            tripNumber = null;
            tripLeg = null;
            tripDate = null;
            error = null;

            if (!TryFormatRerouteTripNumberAndLeg(trip?.TripNumber, out tripNumber, out tripLeg))
            {
                error = "Trip number is missing.";
                return false;
            }

            if (!TryFormatModivcareDate(trip, serviceDateFallback, out tripDate))
            {
                error = "Could not read the trip date for Modivcare (need a valid service date on the trip row).";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Trip Reroutes form: bare trip id in trip number (e.g. <c>42012</c>), leg separate.
        /// Server id goes in txtServerID — not in the trip number field.
        /// </summary>
        internal static bool TryFormatRerouteTripNumberAndLeg(
            string tripNumberRaw,
            out string mcTripNumber,
            out string mcTripLeg)
        {
            mcTripNumber = null;
            mcTripLeg = null;
            string raw = (tripNumberRaw ?? "").Trim().Replace(" ", "");
            if (raw.Length == 0)
                return false;

            mcTripLeg = char.ToLowerInvariant(SupeyScheduleAlgorithm.DetectLegPublic(raw))
                .ToString(CultureInfo.InvariantCulture);

            mcTripNumber = SupeyScheduleAlgorithm.TripPartnerBase(raw);
            if (string.IsNullOrWhiteSpace(mcTripNumber))
                mcTripNumber = raw;

            mcTripNumber = mcTripNumber.Trim();
            // Batch links use display ids like 1-42012-A; the reroute form wants only 42012.
            if (mcTripNumber.StartsWith("1-", StringComparison.Ordinal))
                mcTripNumber = mcTripNumber.Substring(2);

            return mcTripNumber.Length > 0;
        }

        internal static bool TryFormatModivcareDate(MCDownloadedTrip trip, DateTime? serviceDateFallback, out string mcDate)
        {
            mcDate = null;
            if (trip == null)
                return false;

            string raw = (trip.Date ?? "").Trim();
            DateTime parsed;
            if (raw.Length > 0
                && (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)
                    || DateTime.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.None, out parsed)))
            {
                mcDate = parsed.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture);
                return true;
            }

            if (serviceDateFallback.HasValue)
            {
                mcDate = serviceDateFallback.Value.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture);
                return true;
            }

            return false;
        }

        private static MyFormUrlEncodedContent BuildTripReroutesSelectForm(
            MCLoginHandler login,
            string tripNumber,
            string tripLeg,
            string tripDate)
        {
            return new MyFormUrlEncodedContent(new[]
            {
                Pair("__EVENTTARGET", ""),
                Pair("__EVENTARGUMENT", ""),
                Pair("__VIEWSTATE", login.ViewStateToken),
                Pair("__VIEWSTATEGENERATOR", login.ViewStateGeneratorToken),
                Pair("__SCROLLPOSITIONX", "0"),
                Pair("__SCROLLPOSITIONY", "0"),
                Pair("__EVENTVALIDATION", login.EventValidationToken),
                Pair("ctl00$cphMainContent$tripSelect$txtServerID", DefaultServerId),
                Pair("ctl00$cphMainContent$tripSelect$txtTripDate", tripDate),
                Pair("ctl00$cphMainContent$tripSelect$txtTripNumber", tripNumber),
                Pair("ctl00$cphMainContent$txtTripLeg", tripLeg),
                Pair("ctl00$cphMainContent$btnSubmit", "Submit"),
            });
        }

        private static MyFormUrlEncodedContent BuildReroutesSubmitForm(MCLoginHandler login, string reasonCode)
        {
            return new MyFormUrlEncodedContent(new[]
            {
                Pair("__LASTFOCUS", ""),
                Pair("__EVENTTARGET", ""),
                Pair("__EVENTARGUMENT", ""),
                Pair("__VIEWSTATE", login.ViewStateToken),
                Pair("__VIEWSTATEGENERATOR", login.ViewStateGeneratorToken),
                Pair("__SCROLLPOSITIONX", "0"),
                Pair("__SCROLLPOSITIONY", "0"),
                Pair("__EVENTVALIDATION", login.EventValidationToken),
                Pair("ctl00$cphMainContent$ddlRerouteReasons", reasonCode),
                Pair("ctl00$cphMainContent$btnRerouteTrip", "Reroute Trip"),
            });
        }

        private static KeyValuePair<string, string> Pair(string key, string value) =>
            new KeyValuePair<string, string>(key, value ?? "");

        private sealed class PostFormResult
        {
            public string Body { get; set; }
            public string FinalUri { get; set; }
            public bool AuthRedirect { get; set; }
        }

        private static async Task<string> GetPageHtmlAsync(MCLoginHandler login, string url, string referer)
        {
            login.UpdateTripActualsHeaders(referer);
            using (var res = await login.GetWithAuthRetryAsync(url).ConfigureAwait(false))
            {
                if (MCLoginHandler.IsAuthRedirect(res))
                    throw new ModivcareSessionExpiredException();
                return await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
        }

        private static async Task<PostFormResult> PostFormAsync(
            MCLoginHandler login,
            string url,
            string referer,
            Func<MyFormUrlEncodedContent> contentFactory)
        {
            login.UpdateTripActualsHeaders(referer);
            using (var res = await login.PostWithAuthRetryAsync(url, () => contentFactory()).ConfigureAwait(false))
            {
                bool authRedirect = MCLoginHandler.IsAuthRedirect(res);
                string body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                string finalUri = res.RequestMessage?.RequestUri?.ToString() ?? url;
                return new PostFormResult
                {
                    Body = body,
                    FinalUri = finalUri,
                    AuthRedirect = authRedirect,
                };
            }
        }

        private static string TryExtractPageError(string html)
        {
            if (string.IsNullOrEmpty(html))
                return null;

            var lbl = Regex.Match(
                html,
                @"id=""ctl00_lblErrorMessage""[^>]*>(?<t>[^<]*)",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (lbl.Success)
            {
                string t = System.Web.HttpUtility.HtmlDecode(lbl.Groups["t"].Value ?? "").Trim();
                if (t.Length > 0)
                    return t;
            }

            var summary = Regex.Match(
                html,
                @"id=""ctl00_ValidationSummary1""[^>]*>(?<t>.*?)</div>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (summary.Success)
            {
                string t = Regex.Replace(summary.Groups["t"].Value ?? "", "<[^>]+>", " ").Trim();
                t = System.Web.HttpUtility.HtmlDecode(t);
                t = Regex.Replace(t, @"\s+", " ").Trim();
                if (t.Length > 0)
                    return t;
            }

            if (html.IndexOf("valid Trip number", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Modivcare says the trip number is not valid. Check the date, number, and leg.";

            return null;
        }

        private static bool LooksLikeRerouteSuccess(string finalUri, string html)
        {
            if (!string.IsNullOrEmpty(finalUri)
                && finalUri.IndexOf("RerouteFinish.aspx", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (string.IsNullOrEmpty(html))
                return false;

            if (html.IndexOf("Reroute Completed Successfully", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (html.IndexOf("Reroute Completed", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return false;
        }

        private static Result Fail(string message) => new Result { Success = false, Message = message };
    }
}
