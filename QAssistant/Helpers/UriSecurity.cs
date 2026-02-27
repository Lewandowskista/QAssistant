using System;
using System.Net;

namespace QAssistant.Helpers
{
    internal static class UriSecurity
    {
        /// <summary>
        /// Returns <c>true</c> when the URL is an absolute HTTP or HTTPS URL.
        /// Rejects all other schemes (file, javascript, ftp, etc.).
        /// </summary>
        internal static bool IsHttpUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            return Uri.TryCreate(url, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }

        /// <summary>
        /// Returns <c>true</c> when the URL is a safe HTTP/HTTPS URL that does not
        /// target loopback or private/link-local IP ranges (SSRF mitigation).
        /// </summary>
        internal static bool IsSafeHttpUrl(string? url)
        {
            if (!IsHttpUrl(url))
                return false;

            var uri = new Uri(url!);

            if (uri.IsLoopback)
                return false;

            if (IPAddress.TryParse(uri.Host, out var ip))
            {
                var bytes = ip.GetAddressBytes();
                if (bytes.Length == 4)
                {
                    // 10.x.x.x
                    if (bytes[0] == 10) return false;
                    // 172.16–31.x.x
                    if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return false;
                    // 192.168.x.x
                    if (bytes[0] == 192 && bytes[1] == 168) return false;
                    // 169.254.x.x (link-local)
                    if (bytes[0] == 169 && bytes[1] == 254) return false;
                }
            }

            return true;
        }
    }
}
