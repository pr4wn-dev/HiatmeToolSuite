using System;
using System.Net.Http;
using Xunit;

namespace Hiatme.ToolSuite.WellRyde.Tests
{
    /// <summary>
    /// Regression: Tomcat URL rewriting uses <c>;jsessionid=…</c> on <c>POST /portal/filterdata</c>.
    /// If <see cref="HttpRequestMessage.RequestUri"/> or <see cref="Uri"/> canonicalization ever drops it, servlet calls return HTML instead of JSON.
    /// </summary>
    public class WellRydeFilterDataUriTests
    {
        private const string WithMatrix =
            "https://portal.app.wellryde.com/portal/filterdata;jsessionid=05C475F3357D12CAA7069E3E51AAAF09";

        [Fact]
        public void HttpRequestMessage_post_keeps_jsessionid_on_RequestUri_net48()
        {
            var uri = new Uri(WithMatrix, UriKind.Absolute);
            using (var req = new HttpRequestMessage(HttpMethod.Post, uri))
            {
                Assert.Contains(";jsessionid=05C475F3357D12CAA7069E3E51AAAF09",
                    req.RequestUri.AbsoluteUri,
                    StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact]
        public void Legacy_Uri_dontEscape_ctor_keeps_jsessionid()
        {
#pragma warning disable CS0618
            var u = new Uri(WithMatrix, true);
#pragma warning restore CS0618
            var wire = u.AbsoluteUri ?? u.OriginalString;
            Assert.Contains(";jsessionid=05C475F3357D12CAA7069E3E51AAAF09", wire, StringComparison.OrdinalIgnoreCase);
        }
    }
}
