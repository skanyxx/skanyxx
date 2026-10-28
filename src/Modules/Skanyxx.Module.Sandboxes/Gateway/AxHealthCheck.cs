using Grpc.Core;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Skanyxx.Module.Sandboxes.Gateway;

/// <summary>Whether ax-server answers (a one-item list). Registered only with sandboxes enabled; never AX's own error text.</summary>
internal sealed class AxHealthCheck(AxGateway ax) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await ax.ListSandboxesAsync(1, 0, cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (RpcException) when (!cancellationToken.IsCancellationRequested)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, AxErrors.Unreachable);
        }
    }
}
