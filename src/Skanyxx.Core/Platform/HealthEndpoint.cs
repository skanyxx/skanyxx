using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Skanyxx.Core.Platform;

/// <summary>
/// <c>/health</c> is unauthenticated, so it reports a status per check and nothing else (no exception text, no
/// descriptions), is not readable cross-origin, and every check is bounded by a timeout so a stalled database
/// cannot hold request threads.
/// </summary>
public static class HealthEndpoint
{
    public static IServiceCollection AddSkanyxxHealthTimeouts(this IServiceCollection services)
    {
        services.AddOptions<HealthCheckServiceOptions>().PostConfigure<IOptions<SkanyxxOptions>>((health, skanyxx) =>
        {
            var timeout = TimeSpan.FromSeconds(skanyxx.Value.HealthCheckTimeoutSeconds);
            foreach (var registration in health.Registrations)
            {
                var factory = registration.Factory;
                registration.Factory = sp => new BoundedHealthCheck(factory(sp), timeout);
            }
        });
        return services;
    }

    public static IEndpointConventionBuilder MapSkanyxxHealth(this IEndpointRouteBuilder endpoints, string pattern) =>
        endpoints.MapHealthChecks(pattern, new HealthCheckOptions { ResponseWriter = WriteAsync })
            .WithMetadata(new DisableCorsAttribute())
            .AllowAnonymous();

    private static Task WriteAsync(HttpContext context, HealthReport report)
    {
        var failed = report.Entries.Where(e => e.Value.Status != HealthStatus.Healthy).ToList();
        if (failed.Count > 0)
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(HealthEndpoint))
                .LogWarning("Health {Status}: {Failures}", report.Status,
                    string.Join("; ", failed.Select(e => $"{e.Key}={e.Value.Status} ({e.Value.Exception?.Message ?? e.Value.Description})")));

        context.Response.ContentType = "application/json";
        return JsonSerializer.SerializeAsync(context.Response.Body, new
        {
            status = report.Status.ToString(),
            entries = report.Entries.ToDictionary(e => e.Key, e => e.Value.Status.ToString())
        }, cancellationToken: context.RequestAborted);
    }
}
