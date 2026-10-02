using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Skanyxx.Core.Platform;

/// <summary>
/// Answers within <paramref name="timeout"/> even when the inner check ignores cancellation (Npgsql against a
/// stalled server keeps going until its own connect and command timeouts, ~45 s). The inner call is cancelled
/// and abandoned; it finishes in the background under the database client's own limits.
/// </summary>
public sealed class BoundedHealthCheck(IHealthCheck inner, TimeSpan timeout) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            return await inner.CheckHealthAsync(context, deadline.Token).WaitAsync(deadline.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, $"Timed out after {timeout.TotalSeconds:0}s.");
        }
    }
}
