using System.Net;
using System.Net.Sockets;

namespace Skanyxx.Module.Agents.Plugins.Sources;

/// <summary>
/// Which addresses a plugin download may connect to (P9): the public internet only. Loopback, private, link-local,
/// unique-local, CGNAT, multicast and reserved ranges are the cluster's own network or nobody's, and a plugin URL must
/// not become a way to reach them (SSRF). IPv6 forms that embed an IPv4 address are judged by that address.
/// </summary>
internal static class NetworkGuard
{
    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        var b = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
            return !(b[0] is 0 or 10 or 127 or >= 224                 // this network, private, loopback, multicast, reserved
                || (b[0] == 100 && (b[1] & 0xC0) == 64)               // 100.64.0.0/10 CGNAT
                || (b[0] == 169 && b[1] == 254)                       // link-local (cloud metadata)
                || (b[0] == 172 && (b[1] & 0xF0) == 16)               // 172.16.0.0/12
                || (b[0] == 192 && b[1] == 168)                       // 192.168.0.0/16
                || (b[0] == 192 && b[1] == 0 && b[2] == 0)            // 192.0.0.0/24 protocol assignments
                || (b[0] == 192 && b[1] == 88 && b[2] == 99)          // 192.88.99.0/24 deprecated 6to4 relay
                || (b[0] == 198 && (b[1] & 0xFE) == 18));             // 198.18.0.0/15 benchmarking
        if (address.AddressFamily != AddressFamily.InterNetworkV6)
            return false;
        if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xff && b[3] == 0x9b && b[4] == 0x00 && b[5] == 0x01)
            return false;                                             // 64:ff9b:1::/48 local-use NAT64
        if (b.AsSpan(0, 8).IndexOfAnyExcept((byte)0) < 0 && b[8] == 0xff && b[9] == 0xff && b[10] == 0x00 && b[11] == 0x00)
            return false;                                             // ::ffff:0:0:0/96 SIIT IPv4-translated
        if (b[0] == 0x01 && b.AsSpan(1, 7).IndexOfAnyExcept((byte)0) < 0)
            return false;                                             // 100::/64 discard
        if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xff && b[3] == 0x9b && b.AsSpan(4, 8).IndexOfAnyExcept((byte)0) < 0)
            return IsPublic(new IPAddress(b.AsSpan(12, 4)));          // 64:ff9b::/96 NAT64
        if (b[0] == 0x20 && b[1] == 0x02)
            return IsPublic(new IPAddress(b.AsSpan(2, 4)));           // 2002::/16 6to4
        return !(b.AsSpan(0, 12).IndexOfAnyExcept((byte)0) < 0        // ::, ::1, IPv4-compatible
            || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal || address.IsIPv6Multicast
            || (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x00 && b[3] == 0x00)   // 2001::/32 Teredo
            || (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x00 && b[3] == 0x02 && b[4] == 0x00 && b[5] == 0x00) // 2001:2::/48 benchmarking
            || (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0d && b[3] == 0xb8)); // 2001:db8::/32 documentation
    }
}
