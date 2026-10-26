using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Studio.Git;
using Skanyxx.Module.Agents.Studio.Plugins;
using Skanyxx.Module.Agents.Studio.Reconcile;

namespace Skanyxx.Module.Agents.Studio.Features;

/// <summary>
/// Closing takes nothing live with it: an agent proposal's preview goes, and a plugin proposal's install-time header
/// values for servers main does not have are deleted (D220), so a later install never inherits them.
/// </summary>
internal sealed class CloseProposalHandler(
    IAgentRepo repo, StudioReconciler reconciler, IOptions<StudioOptions> options, ILogger<CloseProposalHandler> logger)
    : IRequestHandler<CloseProposalCommand, Outcome<StudioResult>>
{
    public async Task<Outcome<StudioResult>> Handle(CloseProposalCommand command, CancellationToken ct)
    {
        var user = command.User;
        if (!user.CanEnter)
            return Outcome<StudioResult>.Forbidden(StudioFailure.NotStudio);
        if (!options.Value.Configured)
            return StudioFailure.Conflict<StudioResult>(StudioFailure.NotConfigured);
        try
        {
            if (await repo.PullAsync(command.Number, ct) is not { Open: true } pull)
                return Outcome<StudioResult>.NotFound(StudioFailure.NoProposal);
            if (!user.CanMerge && ProposalTrailer.ProposedBy(pull.Body) != user.UserId)
                return Outcome<StudioResult>.Forbidden("Only a supervisor, or the builder who proposed it, closes a proposal.");
            var plugin = ProposalTrailer.PluginOf(pull.Body);
            var servers = plugin is null ? [] : PluginRepoReader.ServerNames((await repo.PullFilesAsync(pull.Number, ct)).Select(f => f.Path));
            // A reinstall may store values the moment the close lands; the purge keeps anything written after this.
            var closedAt = DateTime.UtcNow;
            await repo.CloseAsync(pull, ct);
            logger.LogWarning("Studio proposal #{Number} closed by {ActorUserId}", pull.Number, user.UserId);
            var agent = ProposalTrailer.AgentOf(pull.Body);
            if (agent is not null && Definition.StudioNames.IsValidName(agent))
                await reconciler.RemovePreviewAsync(pull.Number, agent, ct);
            if (plugin is not null && servers.Count > 0 && !await PurgeAsync(pull.Number, plugin, servers, closedAt, ct))
                return Outcome<StudioResult>.Ok(new StudioResult(
                    $"Closed #{pull.Number}; nothing was applied. Its stored header values could not be deleted yet; the reconciler deletes them on a later pass.",
                    pull.Number, agent));
            return Outcome<StudioResult>.Ok(new StudioResult($"Closed #{pull.Number}; nothing was applied.", pull.Number, agent));
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or StudioApplyException)
        {
            logger.LogWarning(ex, "Studio close of #{Number} failed", command.Number);
            return Outcome<StudioResult>.Unavailable(StudioFailure.Unavailable);
        }
    }

    /// <summary>The proposal is closed already: a failure here is logged, and the pass purges the values later (D220).</summary>
    private async Task<bool> PurgeAsync(int number, string plugin, IReadOnlySet<string> servers, DateTime closedAt, CancellationToken ct)
    {
        try
        {
            await reconciler.PurgeClosedAsync(plugin, servers, closedAt, ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Studio closed #{Number} but could not delete its stored header values; the reconcile pass deletes them", number);
            return false;
        }
    }
}
