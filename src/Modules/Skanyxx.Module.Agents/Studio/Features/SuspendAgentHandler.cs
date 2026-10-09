using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Studio.Reconcile;

namespace Skanyxx.Module.Agents.Studio.Features;

/// <summary>
/// D121: memory first (secret and grants revoked in one transaction, so the agent is cut off even if kagent is down),
/// then kagent. Resuming wakes the reconciler, which puts the agent back from main.
/// </summary>
internal sealed class SuspendAgentHandler(
    IMediator mediator, StudioReconciler reconciler, StudioSignal signal, IOptions<StudioOptions> options, ILogger<SuspendAgentHandler> logger)
    : IRequestHandler<SuspendAgentCommand, Outcome<StudioResult>>
{
    public async Task<Outcome<StudioResult>> Handle(SuspendAgentCommand command, CancellationToken ct)
    {
        if (!command.User.IsOwner)
        {
            logger.LogWarning("Studio suspend of {Agent} refused for {ActorUserId}: not the owner", command.Agent, command.User.UserId);
            return Outcome<StudioResult>.Forbidden(StudioFailure.NotOwner);
        }
        if (!options.Value.Configured)
            return StudioFailure.Conflict<StudioResult>(StudioFailure.NotConfigured);

        var memory = await mediator.Send(new SuspendStudioAgentCommand(command.User.UserId!, command.Agent, command.Suspended), ct);
        if (memory.Status == OutcomeStatus.NotFound)
            return Outcome<StudioResult>.NotFound(memory.Message!);
        if (memory.Status != OutcomeStatus.Ok)
            return StudioFailure.Conflict<StudioResult>(memory.Message ?? "Memory refused.");

        if (!command.Suspended)
        {
            signal.Poke();
            return Outcome<StudioResult>.Ok(new StudioResult($"{command.Agent} is resumed; the reconciler puts it back from main now.", null, command.Agent));
        }
        try
        {
            await reconciler.TakeDownAsync(command.Agent, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Suspended {Agent} in memory, but kagent did not take it down yet; the reconciler retries", command.Agent);
            return Outcome<StudioResult>.Accepted(new StudioResult(
                $"{command.Agent} is suspended: its memory access is revoked. kagent did not take it down yet; the reconciler does.", null, command.Agent));
        }
        return Outcome<StudioResult>.Ok(new StudioResult($"{command.Agent} is suspended: memory access revoked and out of kagent until you resume it.", null, command.Agent));
    }
}
