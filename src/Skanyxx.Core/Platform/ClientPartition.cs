using System.Net;
using System.Net.Sockets;

namespace Skanyxx.Core.Platform;

/// <summary>
/// The rate-limit key for a client address. IPv6 is keyed by its /64: one subscriber usually holds a whole /64, and
/// keying by full address would hand them 2^64 fresh windows. IPv4 (and IPv4-mapped IPv6) is keyed by the address.
/// </summary>
public static class ClientPartition
{
    public static string Key(IPAddress? address)
    {
        if (address is null)
            return IPAddress.None.ToString();
        if (address.IsIPv4MappedToIPv6)
            return address.MapToIPv4().ToString();
        if (address.AddressFamily != AddressFamily.InterNetworkV6)
            return address.ToString();

        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new IPAddress(bytes) + "/64";
    }
}
