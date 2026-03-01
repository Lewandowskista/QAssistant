// Copyright (C) 2026 Lewandowskista
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU Affero General Public License for more details.
//
// You should have received a copy of the GNU Affero General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.

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
                    // 172.16-31.x.x
                    if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return false;
                    // 192.168.x.x
                    if (bytes[0] == 192 && bytes[1] == 168) return false;
                    // 169.254.x.x (link-local)
                    if (bytes[0] == 169 && bytes[1] == 254) return false;
                }
                else if (bytes.Length == 16)
                {
                    // fc00::/7 — unique local (covers fc00:: through fdff::)
                    if (bytes[0] == 0xFC || bytes[0] == 0xFD) return false;
                    // fe80::/10 — link-local (covers fe80:: through febf::)
                    if (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80) return false;
                    // ::ffff:0:0/96 — IPv4-mapped: apply the same IPv4 private-range rules
                    if (ip.IsIPv4MappedToIPv6)
                    {
                        var v4 = ip.MapToIPv4().GetAddressBytes();
                        if (v4[0] == 10) return false;
                        if (v4[0] == 172 && v4[1] >= 16 && v4[1] <= 31) return false;
                        if (v4[0] == 192 && v4[1] == 168) return false;
                        if (v4[0] == 169 && v4[1] == 254) return false;
                    }
                }
            }

            return true;
        }
    }
}
