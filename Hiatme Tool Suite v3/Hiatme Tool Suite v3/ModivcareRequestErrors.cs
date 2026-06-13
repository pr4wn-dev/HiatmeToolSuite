using System;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>User-facing messages when Modivcare HTTP calls fail (network / site down).</summary>
    internal static class ModivcareRequestErrors
    {
        public const string UnreachableMessage =
            "Modivcare could not be reached.\n\n"
            + "The site may be down or your network connection was interrupted. "
            + "Check your internet connection and try again in a few minutes.";

        public static bool IsUnreachable(Exception ex)
        {
            for (var cur = ex; cur != null; cur = cur.InnerException)
            {
                if (cur is HttpRequestException)
                    return true;
                if (cur is SocketException)
                    return true;
                if (cur is TaskCanceledException && cur.InnerException is TimeoutException)
                    return true;
            }

            return false;
        }

        public static string DescribeOrDefault(Exception ex, string fallback = null)
        {
            if (IsUnreachable(ex))
                return UnreachableMessage;
            return string.IsNullOrWhiteSpace(ex?.Message) ? (fallback ?? "Request failed.") : ex.Message;
        }
    }
}
