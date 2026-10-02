using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Memory.Features.AgentSecrets;

namespace Skanyxx.Module.Memory.Endpoints.AgentSecrets;

internal sealed class RevokeAgentSecretEndpoint(IMediator mediator, ILogger<RevokeAgentSecretEndpoint> logger)
    : Endpoint<AgentSecretRequest>
{
    public override void Configure()
    {
        Delete("agents/{agentId}/secret");
        Group<MemoryGroup>();
    }

    public override async Task HandleAsync(AgentSecretRequest req, CancellationToken ct)
    {
        var caller = HumanCaller.From(User);
        var outcome = await mediator.Send(new RevokeAgentSecretCommand(caller, req.AgentId), ct);
        // Audit at Warning for the same reason as IssueAgentSecretEndpoint.
        if (outcome.Status == OutcomeStatus.Forbidden)
            logger.LogWarning("Agent memory secret revoke refused for {AgentId} by {ActorUserId} from {RemoteIp}; reason: {Reason}",
                req.AgentId, caller.UserId, HttpContext.Connection.RemoteIpAddress, outcome.Message);
        if (outcome.Status != OutcomeStatus.Ok)
        {
            await Send.ResultAsync(outcome.ToHttp(a => a));
            return;
        }

        logger.LogWarning("Agent memory secret revoked for {AgentId} by {ActorUserId} from {RemoteIp}",
            req.AgentId, caller.UserId, HttpContext.Connection.RemoteIpAddress);
        await Send.ResultAsync(Results.NoContent());
    }
}
