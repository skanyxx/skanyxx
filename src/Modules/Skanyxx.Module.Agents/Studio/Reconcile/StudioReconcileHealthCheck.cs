using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform.Analytics;

namespace Skanyxx.Module.Agents.Studio.Reconcile;

/// <summary>
/// Healthy after a clean pass (or before the first one), Degraded when the last pass left agents unapplied, Unhealthy when
/// it failed or no pass ran for three intervals (the loop is stuck). Data: the last pass time and its failures.
/// </summary>
internal sealed class StudioReconcileHealthCheck(ReconcileStatus status, IOptions<StudioOptions> options, TimeProvider clock) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (status.Last is not { } last)
            return Task.FromResult(HealthCheckResult.Healthy("No pass yet."));
        var data = new Dictionary<string, object>
        {
            [DashboardChecks.LastPassKey] = last.At.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'"),
            [DashboardChecks.FailuresKey] = last.Failures
        };
        var stale = clock.GetUtcNow() - last.At > TimeSpan.FromSeconds(3 * Math.Max(1, options.Value.ReconcileSeconds) + 60);
        return Task.FromResult(last.Failed || stale
            ? new HealthCheckResult(context.Registration.FailureStatus, "The reconciler failed or stopped.", data: data)
            : last.Failures > 0
                ? HealthCheckResult.Degraded("Agents were left unapplied.", data: data)
                : HealthCheckResult.Healthy("Last pass clean.", data));
    }
}
