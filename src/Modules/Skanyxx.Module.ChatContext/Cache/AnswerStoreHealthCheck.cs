using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Skanyxx.Module.ChatContext.Cache;

/// <summary>Whether Dragonfly answers (one counter read). Registered only with the answer cache on; never the store's error text.</summary>
internal sealed class AnswerStoreHealthCheck(IAnswerStore store) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await store.CounterAsync("skanyxx:chat:epoch").WaitAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (AnswerCache.IsStoreFailure(ex))
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "Dragonfly did not answer.");
        }
    }
}
