using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Skanyxx.Module.Agents.Studio.Git;

/// <summary>Whether the studio's git server answers (a read of the agent repo; absent is still an answer). Never git's text.</summary>
internal sealed class StudioGitHealthCheck(IAgentRepo repo) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await repo.InfoAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "Git did not answer.");
        }
    }
}
