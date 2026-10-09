using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Studio.Git;
using Skanyxx.Module.Agents.Studio.Reconcile;

namespace Skanyxx.Module.Agents.Studio.Features;

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
            await repo.CloseAsync(pull, ct);
            logger.LogWarning("Studio proposal #{Number} closed by {ActorUserId}", pull.Number, user.UserId);
            var agent = pull.Body.Split('\n')[0] is var first && first.StartsWith("Agent: ", StringComparison.Ordinal) ? first["Agent: ".Length..].Trim() : null;
            if (agent is not null && Definition.StudioNames.IsValidName(agent))
                await reconciler.RemovePreviewAsync(pull.Number, agent, ct);
            return Outcome<StudioResult>.Ok(new StudioResult($"Closed #{pull.Number}; nothing was applied.", pull.Number, agent));
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or StudioApplyException)
        {
            logger.LogWarning(ex, "Studio close of #{Number} failed", command.Number);
            return Outcome<StudioResult>.Unavailable(StudioFailure.Unavailable);
        }
    }
}
