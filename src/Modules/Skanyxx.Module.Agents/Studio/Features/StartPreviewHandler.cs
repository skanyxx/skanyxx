using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Studio.Git;
using Skanyxx.Module.Agents.Studio.Reconcile;

namespace Skanyxx.Module.Agents.Studio.Features;

/// <summary>D030/D032: builders and supervisors try a proposal before it is merged, without it ever writing to memory.</summary>
internal sealed class StartPreviewHandler(
    IAgentRepo repo, StudioRepoGuard guard, ProposalInspector inspector, StudioReconciler reconciler, IOptions<StudioOptions> options, ILogger<StartPreviewHandler> logger)
    : IRequestHandler<StartPreviewCommand, Outcome<PreviewAgent>>
{
    public async Task<Outcome<PreviewAgent>> Handle(StartPreviewCommand command, CancellationToken ct)
    {
        if (!command.User.CanEnter)
            return Outcome<PreviewAgent>.Forbidden(StudioFailure.NotStudio);
        if (!options.Value.Configured)
            return StudioFailure.Conflict<PreviewAgent>(StudioFailure.NotConfigured);
        try
        {
            if (await repo.PullAsync(command.Number, ct) is not { Open: true } pull)
                return Outcome<PreviewAgent>.NotFound(StudioFailure.NoProposal);
            await guard.RequireAsync(setup: false, ct);
            var inspected = await inspector.InspectAsync(pull, ct);
            if (inspected.Draft is not { } draft)
                return StudioFailure.Conflict<PreviewAgent>($"This proposal cannot run: {string.Join(" ", inspected.Problems)}", "invalid");
            var preview = await reconciler.PreviewAsync(draft, pull.Number, command.User.UserId!, ct);
            logger.LogWarning("Studio preview of #{Number} ({Agent}) deployed for {ActorUserId}", pull.Number, preview.Name, command.User.UserId);
            return Outcome<PreviewAgent>.Ok(preview);
        }
        catch (StudioRepoException ex)
        {
            return StudioFailure.Conflict<PreviewAgent>(ex.Message, StudioFailure.RepoReason);
        }
        catch (StudioApplyException ex)
        {
            return StudioFailure.Conflict<PreviewAgent>(ex.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "Studio preview of #{Number} failed", command.Number);
            return Outcome<PreviewAgent>.Unavailable(StudioFailure.Unavailable);
        }
    }
}
