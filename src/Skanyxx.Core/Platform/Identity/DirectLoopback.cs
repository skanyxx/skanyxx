using System.Net;
using Microsoft.AspNetCore.Http;

namespace Skanyxx.Core.Platform.Identity;

/// <summary>The transport half of the bootstrap guard, shared by the API endpoint and the Host's setup page.</summary>
public static class DirectLoopback
{
    /// <summary>
    /// A loopback peer that carries forwarding headers is a local reverse proxy relaying someone else, so it does
    /// not count. Nor does one addressed by any name but a loopback one: that is a DNS-rebound page running in the
    /// developer's own browser (SEC2-N3), which host filtering stops only when <c>AllowedHosts</c> is not <c>*</c>.
    /// </summary>
    public static bool Is(HttpContext context) =>
        context.Connection.RemoteIpAddress is { } ip && IPAddress.IsLoopback(ip)
        && IsLoopbackHost(context.Request.Host.Host)
        && !context.Request.Headers.ContainsKey("X-Forwarded-For")
        && !context.Request.Headers.ContainsKey("Forwarded");

    private static bool IsLoopbackHost(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
        || (IPAddress.TryParse(host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address));
}
