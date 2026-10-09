using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Studio.Reconcile;

namespace Skanyxx.Module.Agents.Studio.Features;

internal sealed class ConfirmRepoHandler(
    StudioRepoGuard guard, StudioReconciler reconciler, IOptions<StudioOptions> options, ILogger<ConfirmRepoHandler> logger)
    : IRequestHandler<ConfirmRepoCommand, Outcome<StudioResult>>
{
    public async Task<Outcome<StudioResult>> Handle(ConfirmRepoCommand command, CancellationToken ct)
    {
        if (!command.User.IsOwner)
        {
            logger.LogWarning("Studio repo confirmation refused for {ActorUserId}: not the owner", command.User.UserId);
            return Outcome<StudioResult>.Forbidden(StudioFailure.NotOwner);
        }
        if (!options.Value.Configured)
            return StudioFailure.Conflict<StudioResult>(StudioFailure.NotConfigured);
        try
        {
            var repo = await guard.ConfirmAsync(command.User.UserId!, ct);
            var failures = await reconciler.ConfirmedPassAsync(ct);
            logger.LogWarning("Studio pass confirmed by {ActorUserId} ran without the removal brake: {Failures} agent(s) not applied", command.User.UserId, failures);
            return Outcome<StudioResult>.Ok(new StudioResult(
                $"The studio trusts {repo.FullName} (git id {repo.Id}) and reconciled it without the removal brake; {failures} agent(s) not applied.", null, null));
        }
        catch (StudioApplyException ex)
        {
            return StudioFailure.Conflict<StudioResult>(ex.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "Studio repo confirmation failed");
            return Outcome<StudioResult>.Unavailable(StudioFailure.Unavailable);
        }
    }
}
