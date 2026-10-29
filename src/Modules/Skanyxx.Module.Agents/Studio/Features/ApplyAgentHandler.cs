using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Studio.Features;

/// <summary>
/// Direct by role, PR for users (D366): a supervisor or the owner applies a form now — one pull request opened and merged
/// by them through the studio's own gates (form rules, licence, team grants need the owner, the merge pinned to its
/// head). With <see cref="StudioOptions.RequireChecks"/> it stops at the open pull request and merges once CI passed.
/// Builders and employees get 403: they propose.
/// </summary>
internal sealed class ApplyAgentHandler(IMediator mediator, IOptions<StudioOptions> options, ILogger<ApplyAgentHandler> logger)
    : IRequestHandler<ApplyAgentCommand, Outcome<StudioResult>>
{
    public const string NotDirect = "Only a supervisor or the owner applies directly; propose the change instead, and a supervisor merges it.";

    public async Task<Outcome<StudioResult>> Handle(ApplyAgentCommand command, CancellationToken ct)
    {
        var (user, draft) = (command.User, command.Draft);
        if (!user.CanMerge)
        {
            await StudioAudit.OutcomeAsync(mediator, logger, StudioAudit.ApplyRefused, user, draft, reason: "role");
            return Outcome<StudioResult>.Forbidden(NotDirect);
        }
        await StudioAudit.WriteAsync(mediator, StudioAudit.ApplyRequested, user, draft);

        Outcome<StudioResult> proposed;
        try
        {
            proposed = await mediator.Send(new ProposeAgentCommand(user, draft, Direct: true), ct);
        }
        catch (FluentValidation.ValidationException)
        {
            await StudioAudit.OutcomeAsync(mediator, logger, StudioAudit.ApplyRefused, user, draft, reason: "invalid");
            throw;
        }
        if (proposed is not { Status: OutcomeStatus.Created, Value: { Number: { } number } })
        {
            await StudioAudit.OutcomeAsync(mediator, logger, StudioAudit.ApplyRefused, user, draft, reason: proposed.Reason ?? proposed.Status.ToString());
            return proposed;
        }
        if (options.Value.RequireChecks)
        {
            await StudioAudit.OutcomeAsync(mediator, logger, StudioAudit.Applied, user, draft, number, "waiting-for-checks");
            return Outcome<StudioResult>.Created(new StudioResult(
                $"Opened #{number}. It merges once its checks pass (Studio:RequireChecks): merge it from the proposal then.", number, draft.Name));
        }

        var merged = await mediator.Send(new MergeProposalCommand(user, number), ct);
        await StudioAudit.OutcomeAsync(mediator, logger, merged.Status is OutcomeStatus.Ok or OutcomeStatus.Accepted ? StudioAudit.Applied : StudioAudit.ApplyRefused,
            user, draft, number, merged.Reason ?? merged.Status.ToString());
        return merged.Value is { } result
            ? merged with { Value = result with { Message = $"Applied directly. {result.Message}" } }
            : merged.Status == OutcomeStatus.Forbidden
                ? StudioFailure.Conflict<StudioResult>($"Opened #{number}, but you may not merge it: {merged.Message} It waits for the owner.", "needs-owner")
                : StudioFailure.Conflict<StudioResult>($"Opened #{number}, but it did not merge: {merged.Message}", merged.Reason);
    }
}
