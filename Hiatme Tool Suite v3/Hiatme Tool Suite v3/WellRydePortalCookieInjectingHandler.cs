using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Manual <c>Cookie</c> header with inner <c>UseCookies=false</c> so ELB/portal <c>Set-Cookie</c> is merged only via
    /// <see cref="WellRydeCookieHelper.IngestSetCookieHeaders"/> — avoids doubling cookies when the same response is also
    /// processed by <see cref="CookieContainer"/> on a shared <see cref="HttpClientHandler"/>.
    /// </summary>
    internal sealed class WellRydePortalCookieInjectingHandler : DelegatingHandler
    {
        private readonly CookieContainer _jar;

        public WellRydePortalCookieInjectingHandler(CookieContainer jar, HttpMessageHandler innerHandler)
            : base(innerHandler)
        {
            _jar = jar ?? throw new ArgumentNullException(nameof(jar));
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri;
            if (uri != null)
            {
                var path = uri.AbsolutePath ?? "";
                string line = null;
                if (request.Method == HttpMethod.Post
                    && path.StartsWith("/portal/filterdata", StringComparison.OrdinalIgnoreCase))
                {
                    line = WellRydeCookieHelper.BuildChromeLikeFilterDataCookieHeader(_jar);
                }
                if (string.IsNullOrEmpty(line))
                    line = WellRydeCookieHelper.GetCookieHeader(_jar, uri);
                if (!string.IsNullOrEmpty(line))
                    request.Headers.TryAddWithoutValidation("Cookie", line);
            }

            return base.SendAsync(request, cancellationToken);
        }
    }
}
