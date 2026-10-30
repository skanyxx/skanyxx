using System.Buffers.Binary;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Skanyxx.Core.Platform;

/// <summary>Origin allow-list (guarded routes, and unsafe methods everywhere) and per-client rate limits on the guarded routes, credential posts and invite links.</summary>
public static class ApiGuardExtensions
{
    /// <summary>
    /// The broadest Skanyxx:TrustedProxies range startup accepts (D397, narrowed in D399 and D525; /120 kept over /112, D552): a /16 is a whole k3s
    /// pod network. The Helm schema holds the same line.
    /// </summary>
    public const int MinIPv4Prefix = 24;

    /// <inheritdoc cref="MinIPv4Prefix"/>
    public const int MinIPv6Prefix = 120;

    private const int MappedPrefix = 96;

    private static readonly UInt128 MappedBits = (UInt128)0xFFFF << 32;

    /// <summary>
    /// Kestrel binds dual-mode, so IPv4 peers arrive as <c>::ffff:a.b.c.d</c> and are matched against IPv6 ranges too: a
    /// range inside <c>::ffff:0:0/96</c> is judged as the IPv4 range it is, and any other range reaching into it is
    /// refused. The overlap is worked out on the bits: <see cref="System.Net.IPNetwork.Contains"/> special-cases
    /// IPv4-mapped addresses (it says <c>fd00:7::/120</c> holds <c>::ffff:0:0</c>), so it cannot answer this.
    /// </summary>
    public static bool TooBroad(System.Net.IPNetwork network)
    {
        if (network.BaseAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            return network.PrefixLength < MinIPv4Prefix;
        var shared = Math.Min(network.PrefixLength, MappedPrefix);
        var bits = BinaryPrimitives.ReadUInt128BigEndian(network.BaseAddress.GetAddressBytes());
        if (shared > 0 && (bits ^ MappedBits) >> (128 - shared) != 0)
            return network.PrefixLength < MinIPv6Prefix;
        return network.PrefixLength < MappedPrefix
            || TooBroad(new System.Net.IPNetwork(network.BaseAddress.MapToIPv4(), network.PrefixLength - MappedPrefix));
    }

    public static IServiceCollection AddSkanyxxApiGuards(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SkanyxxOptions>()
            .Bind(configuration.GetSection(SkanyxxOptions.Section))
            .ValidateDataAnnotations()
            .Validate(o => o.RateLimit is { PermitLimit: > 0, WindowSeconds: > 0 },
                "Skanyxx:RateLimit:PermitLimit and WindowSeconds must be positive.")
            .Validate(o => o.SignInRateLimit is { PermitLimit: > 0, WindowSeconds: > 0 },
                "Skanyxx:SignInRateLimit:PermitLimit and WindowSeconds must be positive.")
            .Validate(o => o.InviteRateLimit is { PermitLimit: > 0, WindowSeconds: > 0 },
                "Skanyxx:InviteRateLimit:PermitLimit and WindowSeconds must be positive.")
            .Validate(o => o.TrustedProxies.All(p => System.Net.IPNetwork.TryParse(p, out _)),
                "Skanyxx:TrustedProxies must be CIDRs: the ingress controller's own pod IPs, e.g. 10.42.3.17/32, never the whole pod network.")
            // D397: a range that wide is a pod network, not the ingress pods; any pod in it could name itself any client.
            .Validate(o => o.TrustedProxies.All(p => System.Net.IPNetwork.TryParse(p, out var n) && !TooBroad(n)),
                $"Skanyxx:TrustedProxies may not be broader than /{MinIPv4Prefix} (IPv4) or /{MinIPv6Prefix} (IPv6), and an IPv6 range may reach into ::ffff:0:0/96 only as an IPv4-mapped range held to the IPv4 floor: list the ingress controller pods, not the pod network.")
            .ValidateOnStart();
        // D390: behind a trusted proxy the client is the last X-Forwarded-For hop it added.
        services.AddOptions<ForwardedHeadersOptions>().Configure<IOptions<SkanyxxOptions>>((forwarded, skanyxx) =>
        {
            forwarded.KnownIPNetworks.Clear();
            forwarded.KnownProxies.Clear();
            foreach (var proxy in skanyxx.Value.TrustedProxies)
                forwarded.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(proxy));
            forwarded.ForwardedHeaders = skanyxx.Value.TrustedProxies.Length > 0 ? ForwardedHeaders.XForwardedFor : ForwardedHeaders.None;
            forwarded.ForwardLimit = 1;
        });

        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var options = context.RequestServices.GetRequiredService<IOptions<SkanyxxOptions>>().Value;
                var ip = ClientPartition.Key(context.Connection.RemoteIpAddress);
                if (GuardedPaths.IsInviteRequest(context.Request))
                    return FixedWindow("invite:" + ip, options.InviteRateLimit);
                if (GuardedPaths.IsCredentialPost(context.Request))
                    return FixedWindow("sign-in:" + ip, options.SignInRateLimit);
                if (GuardedPaths.Contains(context.Request.Path) || GuardedPaths.IsGuardedPagePost(context.Request))
                    return FixedWindow("api:" + ip, options.RateLimit);
                return RateLimitPartition.GetNoLimiter("");
            });
            o.OnRejected = (rejected, _) =>
            {
                if (rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    rejected.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
                return new ValueTask(Results.Problem("Too many requests.", statusCode: StatusCodes.Status429TooManyRequests)
                    .ExecuteAsync(rejected.HttpContext));
            };
        });
        return services;
    }

    private static RateLimitPartition<string> FixedWindow(string key, ApiRateLimitOptions limit) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = limit.PermitLimit,
            Window = TimeSpan.FromSeconds(limit.WindowSeconds),
            QueueLimit = 0
        });

    public static IApplicationBuilder UseSkanyxxApiGuards(this IApplicationBuilder app) =>
        app.UseForwardedHeaders().UseMiddleware<OriginGuardMiddleware>().UseRateLimiter();
}
