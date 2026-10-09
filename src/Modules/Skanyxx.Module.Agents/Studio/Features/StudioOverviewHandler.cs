using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Core.Services;
using Skanyxx.Module.Agents.Studio.Definition;
using Skanyxx.Module.Agents.Studio.Git;
using Skanyxx.Module.Agents.Studio.Reconcile;

namespace Skanyxx.Module.Agents.Studio.Features;

internal sealed class StudioOverviewHandler(
    IAgentRepo repo, StudioRepoGuard guard, StudioReconciler reconciler, KAgentApiClient kagent, IOptions<StudioOptions> options, ILogger<StudioOverviewHandler> logger)
    : IRequestHandler<StudioOverviewQuery, Outcome<StudioOverview>>
{
    public async Task<Outcome<StudioOverview>> Handle(StudioOverviewQuery query, CancellationToken ct)
    {
        if (!query.User.CanEnter)
            return Outcome<StudioOverview>.Forbidden(StudioFailure.NotStudio);
        if (!options.Value.Configured)
            return Outcome<StudioOverview>.Ok(new StudioOverview(null, StudioFailure.NotConfigured, [], []));
        try
        {
            await guard.RequireAsync(setup: false, ct);
            var open = (await repo.OpenPullsAsync(ct))
                .Select(p => new ProposalSummary(p.Number, p.Title, AgentOf(p.Body), ProposalTrailer.ProposedBy(p.Body), p.HeadSha, p.Mergeable))
                .ToList();
            var main = await reconciler.ReadMainAsync(await reconciler.MainFoldersAsync(ct), ct);
            var live = (await kagent.GetAgentsAsync(ct))
                .Where(a => a.Namespace == options.Value.Namespace && a.Labels.GetValueOrDefault(StudioNames.MergedLabel) == "true"
                    && a.Labels.GetValueOrDefault(StudioNames.ManagedByLabel) == StudioNames.ManagedBy)
                .Select(a => a.Name).ToHashSet();
            return Outcome<StudioOverview>.Ok(new StudioOverview(repo.Name, null, open,
                [.. main.Values.OrderBy(d => d.Name).Select(d => new StudioAgentSummary(d.Name, d.Description, live.Contains(d.Name)))]));
        }
        catch (StudioApplyException ex)
        {
            return Outcome<StudioOverview>.Ok(new StudioOverview(repo.Name, ex.Message, [], []));
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "Studio overview failed");
            return Outcome<StudioOverview>.Ok(new StudioOverview(repo.Name, StudioFailure.Unavailable, [], []));
        }
    }

    private static string? AgentOf(string body) =>
        body.Split('\n').FirstOrDefault(l => l.StartsWith("Agent: ", StringComparison.Ordinal))?["Agent: ".Length..].Trim();
}
