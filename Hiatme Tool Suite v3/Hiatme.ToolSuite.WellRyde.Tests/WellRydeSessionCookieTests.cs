using System;
using System.Net;
using System.Text.RegularExpressions;
using Hiatme_Tool_Suite_v3;
using Xunit;

namespace Hiatme.ToolSuite.WellRyde.Tests
{
    public class WellRydeSessionCookieTests
    {
        [Fact]
        public void TryDecodeSpringSessionCookie_ToUuidHex_MatchesKnownLogSample()
        {
            var b64 = "ODg3MDFiYjQtYjA4Yy00NjRiLWExOTQtZmIzOTRhODRjNzBi";
            Assert.True(WellRydeCookieHelper.TryDecodeSpringSessionCookieToUuidHex(b64, out var hex));
            Assert.Equal("88701BB4B08C464BA194FB394A84C70B", hex);
        }

        [Fact]
        public void JsessionIdLooksSynthesized_True_WhenJsessionMatchesSessionUuidHex()
        {
            var jar = new CookieContainer();
            var uri = new Uri("https://portal.app.wellryde.com/portal/filterdata");
            jar.Add(new Cookie("SESSION", "ODg3MDFiYjQtYjA4Yy00NjRiLWExOTQtZmIzOTRhODRjNzBi", "/portal/", "portal.app.wellryde.com")
            {
                Secure = true,
            });
            jar.Add(new Cookie("JSESSIONID", "88701BB4B08C464BA194FB394A84C70B", "/portal/", "portal.app.wellryde.com")
            {
                Secure = true,
            });
            Assert.True(WellRydeCookieHelper.JsessionIdLooksSynthesizedFromSpringSession(jar, uri));
        }

        [Fact]
        public void JsessionIdLooksSynthesized_False_WhenJsessionDiffersFromSessionUuidHex()
        {
            var jar = new CookieContainer();
            var uri = new Uri("https://portal.app.wellryde.com/portal/filterdata");
            jar.Add(new Cookie("SESSION", "ODg3MDFiYjQtYjA4Yy00NjRiLWExOTQtZmIzOTRhODRjNzBi", "/portal/", "portal.app.wellryde.com")
            {
                Secure = true,
            });
            jar.Add(new Cookie("JSESSIONID", "ABCD1234EF567890ABCD1234EF567890", "/portal/", "portal.app.wellryde.com")
            {
                Secure = true,
            });
            Assert.False(WellRydeCookieHelper.JsessionIdLooksSynthesizedFromSpringSession(jar, uri));
        }

        [Fact]
        public void BuildChromeLikeFilterDataCookieHeader_OmitsPoisonJsessionId()
        {
            var jar = new CookieContainer();
            jar.Add(new Cookie("SESSION", "ODg3MDFiYjQtYjA4Yy00NjRiLWExOTQtZmIzOTRhODRjNzBi", "/portal/", "portal.app.wellryde.com") { Secure = true });
            jar.Add(new Cookie("JSESSIONID", "88701BB4B08C464BA194FB394A84C70B", "/portal/", "portal.app.wellryde.com") { Secure = true });
            jar.Add(new Cookie("XSRF-TOKEN", "f0d4585f-4a2a-476c-8f92-f2962c29585e", "/portal/", "portal.app.wellryde.com") { Secure = true });
            jar.Add(new Cookie("AWSALB", "lb", "/", "portal.app.wellryde.com") { Secure = true });
            jar.Add(new Cookie("AWSALBCORS", "lbc", "/", "portal.app.wellryde.com") { Secure = true });
            var line = WellRydeCookieHelper.BuildChromeLikeFilterDataCookieHeader(jar);
            Assert.DoesNotContain("JSESSIONID=", line, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("SESSION=", line, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void TryPromoteJsessionId_CopiesPathRoot_ToPortalPathForFilterDataHeader()
        {
            var jar = new CookieContainer();
            const string host = "portal.app.wellryde.com";
            jar.Add(new Cookie("SESSION", "x", "/portal/", host) { Secure = true });
            jar.Add(new Cookie("JSESSIONID", "TOMCATSESSION99", "/", host) { Secure = true });
            WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(jar);
            var line = WellRydeCookieHelper.BuildChromeLikeFilterDataCookieHeader(jar);
            Assert.Contains("JSESSIONID=TOMCATSESSION99", line, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void CollapseDuplicatePortalCookies_JsessionId_portalAndPortalSlash_YieldsSingleWireName()
        {
            var jar = new CookieContainer();
            const string host = "portal.app.wellryde.com";
            const string id = "8BDF942DA21818BA731432B3F1703914";
            jar.Add(new Cookie("JSESSIONID", id, "/portal", host) { Secure = true });
            jar.Add(new Cookie("JSESSIONID", id, "/portal/", host) { Secure = true });
            WellRydeCookieHelper.CollapseDuplicatePortalCookies(jar);
            var probe = new Uri("https://portal.app.wellryde.com/portal/filterdata");
            var header = jar.GetCookieHeader(probe);
            var n = Regex.Matches(header, "JSESSIONID=", RegexOptions.IgnoreCase).Count;
            Assert.Equal(1, n);
        }

        [Fact]
        public void TryPromoteJsessionId_AfterDuplicatePaths_DoesNotReintroduceTwoJsessionIds()
        {
            var jar = new CookieContainer();
            const string host = "portal.app.wellryde.com";
            const string id = "8BDF942DA21818BA731432B3F1703914";
            jar.Add(new Cookie("SESSION", "x", "/portal/", host) { Secure = true });
            jar.Add(new Cookie("JSESSIONID", id, "/portal", host) { Secure = true });
            jar.Add(new Cookie("JSESSIONID", id, "/portal/", host) { Secure = true });
            WellRydeCookieHelper.TryPromoteJsessionIdToPortalPathForFilterData(jar);
            var probe = new Uri("https://portal.app.wellryde.com/portal/filterdata");
            var header = WellRydeCookieHelper.GetCookieHeader(jar, probe);
            var n = Regex.Matches(header, "JSESSIONID=", RegexOptions.IgnoreCase).Count;
            Assert.Equal(1, n);
        }

        [Fact]
        public void GetCookieHeaderExtension_DedupesTwoJsessionIds_ForTripFilterListUri()
        {
            var jar = new CookieContainer();
            const string host = "portal.app.wellryde.com";
            const string id = "9AC2C18D1A2B204CB2DEF0E20B3C7664";
            jar.Add(new Cookie("SESSION", "YWY0ZTM5MmUtMGY4OS00OGFkLWI4NmEtMmQ1YzBjNmY5NWI2", "/portal/", host) { Secure = true });
            jar.Add(new Cookie("JSESSIONID", id, "/portal", host) { Secure = true });
            jar.Add(new Cookie("JSESSIONID", id, "/portal/", host) { Secure = true });
            jar.Add(new Cookie("AWSALB", "lb", "/", host) { Secure = true });
            jar.Add(new Cookie("AWSALBCORS", "lbc", "/", host) { Secure = true });
            var tripList = new Uri("https://portal.app.wellryde.com/portal/trip/filterlist?list_name=VTripBilling");
            var deduped = WellRydeCookieHelper.GetCookieHeader(jar, tripList);
            Assert.Equal(1, Regex.Matches(deduped, "JSESSIONID=", RegexOptions.IgnoreCase).Count);
            Assert.Contains("JSESSIONID=" + id, deduped, StringComparison.OrdinalIgnoreCase);
        }
    }
}
