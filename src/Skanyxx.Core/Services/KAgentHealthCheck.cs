using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Skanyxx.Core.Services;

/// <summary>Whether kagent's controller answers. Tagged for diagnostics only: kagent down must not take the Host out of rotation.</summary>
public sealed class KAgentHealthCheck(KAgentApiClient kagent) : IHealthCheck
{
    public const string Name = "kagent";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await kagent.PingAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            // A status, a timeout or a connection error: never a body kagent sent.
            return new HealthCheckResult(context.Registration.FailureStatus, ex.Message);
        }
    }
}
