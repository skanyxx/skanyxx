using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Studio.Deploy;
using Skanyxx.Module.Agents.Studio.Git;
using Skanyxx.Module.Agents.Studio.Reconcile;

namespace Skanyxx.Module.Agents.Studio.Features;

/// <summary>
/// Every change people follow (D365): open proposals and the latest closed ones, each with its link in git, its CI checks
/// (D362) and — for the newest merged change of an agent — where that agent is in kagent (D363).
/// </summary>
internal sealed class StudioChangesHandler(
    IAgentRepo repo, StudioRepoGuard guard, DeployStatusReader deploys, IOptions<StudioOptions> options, ILogger<StudioChangesHandler> logger)
    : IRequestHandler<StudioChangesQuery, Outcome<IReadOnlyList<StudioChange>>>
{
    public const int MaxOpen = 30;
    public const int MaxClosed = 10;

    public async Task<Outcome<IReadOnlyList<StudioChange>>> Handle(StudioChangesQuery query, CancellationToken ct)
    {
        if (!query.User.CanEnter)
            return Outcome<IReadOnlyList<StudioChange>>.Forbidden(StudioFailure.NotStudio);
        if (!options.Value.Configured)
            return StudioFailure.Conflict<IReadOnlyList<StudioChange>>(StudioFailure.NotConfigured);
        try
        {
            await guard.RequireAsync(setup: false, ct);
            var pulls = (await repo.OpenPullsAsync(ct)).OrderByDescending(p => p.Number).Take(MaxOpen)
                .Concat(await repo.RecentClosedPullsAsync(MaxClosed, ct))
                .Where(p => p.BaseRef == options.Value.Git.Branch)
                .ToList();
            var newestMerged = new Dictionary<string, int>();
            foreach (var pull in pulls.Where(p => p.Merged).OrderByDescending(p => p.Number))
                if (ProposalTrailer.AgentOf(pull.Body) is { } agent)
                    newestMerged.TryAdd(agent, pull.Number);
            var deployed = await deploys.ReadAsync(newestMerged.Keys, ct);

            var changes = new List<StudioChange>();
            foreach (var pull in pulls)
            {
                var agent = ProposalTrailer.AgentOf(pull.Body);
                var plugin = ProposalTrailer.PluginOf(pull.Body);
                var status = pull.Open ? ChangeStatus.Open : pull.Merged ? ChangeStatus.Merged : ChangeStatus.Closed;
                var checks = await repo.ChecksAsync(pull.HeadSha, ct);
                var deploy = agent is not null && newestMerged.GetValueOrDefault(agent) == pull.Number && status == ChangeStatus.Merged
                    ? deployed.GetValueOrDefault(agent) : null;
                var note = status != ChangeStatus.Merged || deploy is not null ? null
                    : plugin is not null ? $"Applied with the plugin {plugin} (see Plugins)."
                    : pull.Body.StartsWith(InstallCiHandler.Trailer + "\n", StringComparison.Ordinal) ? "The CI workflow: nothing to deploy; it runs on every later change."
                    : agent is not null && newestMerged.TryGetValue(agent, out var later) ? $"Replaced by a later change (#{later})."
                    : null;
                changes.Add(new StudioChange(pull.Number, pull.Title, agent ?? (plugin is null ? null : $"plugin {plugin}"), pull.Url,
                    ProposalTrailer.ProposedBy(pull.Body), status, checks, deploy,
                    ChangeTimeline.Build(status, checks, deploy, options.Value.RequireChecks, note)));
            }
            return Outcome<IReadOnlyList<StudioChange>>.Ok(changes);
        }
        catch (StudioRepoException ex)
        {
            return StudioFailure.Conflict<IReadOnlyList<StudioChange>>(ex.Message, StudioFailure.RepoReason);
        }
        catch (StudioApplyException ex)
        {
            return StudioFailure.Conflict<IReadOnlyList<StudioChange>>(ex.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "Studio changes could not be read");
            return Outcome<IReadOnlyList<StudioChange>>.Unavailable(StudioFailure.Unavailable);
        }
    }
}
