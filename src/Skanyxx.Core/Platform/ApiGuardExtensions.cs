using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Skanyxx.Core.Platform;

/// <summary>Origin allow-list and a per-IP rate limit on the guarded routes; everything else is untouched.</summary>
public static class ApiGuardExtensions
{
    public static IServiceCollection AddSkanyxxApiGuards(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SkanyxxOptions>()
            .Bind(configuration.GetSection(SkanyxxOptions.Section))
            .ValidateDataAnnotations()
            .Validate(o => o.RateLimit is { PermitLimit: > 0, WindowSeconds: > 0 },
                "Skanyxx:RateLimit:PermitLimit and WindowSeconds must be positive.")
            .ValidateOnStart();

        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                if (!GuardedPaths.Contains(context.Request.Path))
                    return RateLimitPartition.GetNoLimiter("");

                var limit = context.RequestServices.GetRequiredService<IOptions<SkanyxxOptions>>().Value.RateLimit;
                return RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? IPAddress.None.ToString(),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = limit.PermitLimit,
                        Window = TimeSpan.FromSeconds(limit.WindowSeconds),
                        QueueLimit = 0
                    });
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

    public static IApplicationBuilder UseSkanyxxApiGuards(this IApplicationBuilder app) =>
        app.UseMiddleware<OriginGuardMiddleware>().UseRateLimiter();
}
