using System;
using System.Net;
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
    }
}
