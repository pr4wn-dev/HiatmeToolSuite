using System;
using System.Net;
using System.Net.Http;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// HttpClient for desk → office panel (and other local HTTP).
    ///
    /// Default WinHTTP proxy autodetection can stall the calling thread for ~8–10s on
    /// desks; the server PC often has “no proxy” and never feels it. Panel probes already
    /// set <c>Proxy = null</c>. Every other client must do the same, and must not start
    /// <see cref="HttpClient.SendAsync"/> on the WinForms UI thread (.NET Framework can
    /// block there for the whole connect wait).
    /// </summary>
    internal static class HiatmePanelHttp
    {
        public static HttpClient Create(TimeSpan timeout)
        {
            var handler = new HttpClientHandler
            {
                UseProxy = false,
                Proxy = null,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            };
            return new HttpClient(handler) { Timeout = timeout };
        }
    }
}
