using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Services;
using Skanyxx.Module.Agents.Manage.Definition;
using Skanyxx.Module.Agents.Studio;

namespace Skanyxx.Module.Agents.Manage.Features;

/// <summary>
/// D257: suspend = the agent's Deployment scaled to zero (<c>deployment.replicas: 0</c>, which kagent passes to the
/// Deployment), and Chat stops offering it; resume = the override removed (kagent's default, one replica). The same
/// gates as an edit: role, namespace, never a studio agent (the owner suspends those in Studio, D121) or a system agent.
/// Audited like every write (C-3). Asking for the state it is already in changes nothing.
/// </summary>
internal sealed class SetAgentSuspendedHandler(
    KAgentApiClient kagent, IMediator mediator, IOptions<StudioOptions> options, ILogger<SetAgentSuspendedHandler> logger)
    : IRequestHandler<SetAgentSuspendedCommand, Outcome<AgentDetail>>
{
    public async Task<Outcome<AgentDetail>> Handle(SetAgentSuspendedCommand command, CancellationToken ct)
    {
        if (!AgentAccess.CanWrite(command.User))
            return Outcome<AgentDetail>.Forbidden(AgentAccess.NotWriter);
        if (!AgentQueries.IsRef(command.Namespace, command.Name))
            return Outcome<AgentDetail>.NotFound(AgentFailure.NotFound);
        if (command.Namespace != options.Value.Namespace)
            return Outcome<AgentDetail>.Conflict(null, AgentFailure.OtherNamespace(options.Value.Namespace), "namespace");
        var verb = command.Suspended ? "suspend" : "resume";
        var intended = false;
        try
        {
            var existing = await kagent.GetAgentObjectAsync(command.Namespace, command.Name, ct);
            if (existing is null)
                return Outcome<AgentDetail>.NotFound(AgentFailure.NotFound);
            var labels = AgentSpec.Labels(existing);
            if (AgentProtection.Refusal(command.Name, labels) is { } refusal)
            {
                await AgentAudit.WriteAsync(mediator, AgentAudit.Refused, command.User, command.Namespace, command.Name,
                    reason: $"{verb}: {AgentProtection.SourceOf(command.Name, labels)}");
                return Outcome<AgentDetail>.Conflict(null, refusal, AgentProtection.SourceOf(command.Name, labels));
            }
            if (AgentSpec.IsSuspended(existing) == command.Suspended)
                return Outcome<AgentDetail>.Ok(AgentSpec.Detail(existing, full: true, canWrite: true));

            var changed = AgentSpec.WithSuspended(existing, command.Suspended);
            await AgentAudit.WriteAsync(mediator, command.Suspended ? AgentAudit.SuspendRequested : AgentAudit.ResumeRequested,
                command.User, command.Namespace, command.Name);
            intended = true;
            await kagent.UpdateAgentAsync(changed, ct);
            await AgentAudit.OutcomeAsync(mediator, logger, command.Suspended ? AgentAudit.Suspended : AgentAudit.Resumed,
                command.User, command.Namespace, command.Name);
            logger.LogWarning("Agent {Namespace}/{Name} {Verb}ed by {ActorUserId}", command.Namespace, command.Name, verb, command.User.UserId);
            return Outcome<AgentDetail>.Ok(AgentSpec.Detail(changed, full: true, canWrite: true, read: false));
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "Agent {Namespace}/{Name}: kagent did not {Verb} it", command.Namespace, command.Name, verb);
            if (intended)
                await AgentAudit.OutcomeAsync(mediator, logger, AgentAudit.Failed, command.User, command.Namespace, command.Name, reason: verb);
            return Outcome<AgentDetail>.Unavailable(AgentFailure.Unavailable);
        }
    }
}
