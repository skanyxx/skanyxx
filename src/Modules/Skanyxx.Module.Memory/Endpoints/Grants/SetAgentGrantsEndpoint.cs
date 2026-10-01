using Skanyxx.Core.Platform;
using FastEndpoints;
using MediatR;
using Microsoft.Extensions.Logging;
using Skanyxx.Module.Memory.Features.Grants;

namespace Skanyxx.Module.Memory.Endpoints.Grants;

internal sealed class SetAgentGrantsEndpoint(IMediator mediator, ILogger<SetAgentGrantsEndpoint> logger) : Endpoint<AgentGrantsRequest>
{
    public override void Configure()
    {
        Put("grants/{agentId}");
        Group<MemoryGroup>();
    }

    public override async Task HandleAsync(AgentGrantsRequest req, CancellationToken ct)
    {
        var caller = HumanCaller.From(User);
        var outcome = await mediator.Send(new SetAgentGrantsCommand(caller, req.AgentId, req.Grants), ct);
        // Audit (D091), in the style of the secret lines (D084/D085): grants are what an agent may read and write, so
        // every change is the agent's access history, and a refusal is where an escalation attempt shows.
        if (outcome.Status == OutcomeStatus.Ok)
            logger.LogWarning("Agent memory grants set for {AgentId} by {ActorUserId} from {RemoteIp}: {Grants}",
                req.AgentId, caller.UserId, HttpContext.Connection.RemoteIpAddress, Describe(req.Grants));
        else if (outcome.Status is OutcomeStatus.Forbidden or OutcomeStatus.Conflict)
            logger.LogWarning("Agent memory grants refused for {AgentId} by {ActorUserId} from {RemoteIp}: {Grants}; reason: {Reason}",
                req.AgentId, caller.UserId, HttpContext.Connection.RemoteIpAddress, Describe(req.Grants), outcome.Message);
        await Send.ResultAsync(outcome.ToHttp(g => g));
    }

    private static string Describe(IEnumerable<GrantEntry> grants) =>
        string.Join(", ", grants.Select(g => $"{g.Scope} (search: {g.CanSearch}, upsert: {g.CanUpsert})"));
}
