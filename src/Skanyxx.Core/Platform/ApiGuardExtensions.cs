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
