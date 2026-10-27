using MediatR;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Services;
using Microsoft.Extensions.Options;
using Skanyxx.Module.Agents.Manage.Definition;
using Skanyxx.Module.Agents.Studio;

namespace Skanyxx.Module.Agents.Manage.Features;

/// <summary>D252: studio and system agents (the seed, the factory) never leave kagent from here; audited (D255).</summary>
internal sealed class DeleteAgentHandler(KAgentApiClient kagent, IMediator mediator, IOptions<StudioOptions> options, ILogger<DeleteAgentHandler> logger)
    : IRequestHandler<DeleteAgentCommand, Outcome<AgentDeleted>>
{
    public async Task<Outcome<AgentDeleted>> Handle(DeleteAgentCommand command, CancellationToken ct)
    {
        if (!AgentAccess.CanWrite(command.User))
            return Outcome<AgentDeleted>.Forbidden(AgentAccess.NotWriter);
        if (!AgentQueries.IsRef(command.Namespace, command.Name))
            return Outcome<AgentDeleted>.NotFound(AgentFailure.NotFound);
        if (command.Namespace != options.Value.Namespace)
            return Outcome<AgentDeleted>.Conflict(null, AgentFailure.OtherNamespace(options.Value.Namespace), "namespace");
        try
        {
            var existing = await kagent.GetAgentObjectAsync(command.Namespace, command.Name, ct);
            if (existing is null)
                return Outcome<AgentDeleted>.NotFound(AgentFailure.NotFound);
            var labels = AgentSpec.Labels(existing);
            if (AgentProtection.Refusal(command.Name, labels) is { } refusal)
            {
                await AgentAudit.WriteAsync(mediator, AgentAudit.Refused, command.User, command.Namespace, command.Name,
                    reason: "delete: " + AgentProtection.SourceOf(command.Name, labels));
                return Outcome<AgentDeleted>.Conflict(null, refusal, AgentProtection.SourceOf(command.Name, labels));
            }
            if (!await kagent.DeleteAgentAsync(command.Namespace, command.Name, ct))
                return Outcome<AgentDeleted>.NotFound(AgentFailure.NotFound);
            await AgentAudit.WriteAsync(mediator, AgentAudit.Deleted, command.User, command.Namespace, command.Name);
            logger.LogWarning("Agent {Namespace}/{Name} deleted by {ActorUserId}", command.Namespace, command.Name, command.User.UserId);
            return Outcome<AgentDeleted>.Ok(new AgentDeleted(command.Namespace, command.Name));
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "Agent {Namespace}/{Name}: kagent did not delete it", command.Namespace, command.Name);
            return Outcome<AgentDeleted>.Unavailable(AgentFailure.Unavailable);
        }
    }
}
