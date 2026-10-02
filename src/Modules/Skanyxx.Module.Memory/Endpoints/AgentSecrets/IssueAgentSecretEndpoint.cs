using FastEndpoints;
using MediatR;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Memory.Features.AgentSecrets;

namespace Skanyxx.Module.Memory.Endpoints.AgentSecrets;

/// <summary>D080: issue or rotate. The plaintext secret is in this response and nowhere else, ever.</summary>
internal sealed class IssueAgentSecretEndpoint(IMediator mediator, ILogger<IssueAgentSecretEndpoint> logger)
    : Endpoint<IssueAgentSecretRequest>
{
    public override void Configure()
    {
        Post("agents/{agentId}/secret");
        Group<MemoryGroup>();
    }

    public override async Task HandleAsync(IssueAgentSecretRequest req, CancellationToken ct)
    {
        var caller = HumanCaller.From(User);
        var outcome = await mediator.Send(new IssueAgentSecretCommand(caller, req.AgentId, req.ActsForUsers), ct);
        // Audit (D084): every issue is a rotation of whatever came before, so these lines are the secret's history, and a
        // refusal is where an escalation attempt shows. Warning, not Information: the Host's Serilog floor is Information
        // today, but an operator who raises it (Serilog:MinimumLevel in configuration) must not lose the audit with it.
        if (outcome.Status == OutcomeStatus.Ok)
            logger.LogWarning("Agent memory secret issued for {AgentId} by {ActorUserId} from {RemoteIp}; acts for users: {ActsForUsers}",
                req.AgentId, caller.UserId, HttpContext.Connection.RemoteIpAddress, req.ActsForUsers);
        else if (outcome.Status == OutcomeStatus.Forbidden)
            logger.LogWarning("Agent memory secret issue refused for {AgentId} by {ActorUserId} from {RemoteIp}; acts for users requested: {ActsForUsers}; reason: {Reason}",
                req.AgentId, caller.UserId, HttpContext.Connection.RemoteIpAddress, req.ActsForUsers, outcome.Message);
        HttpContext.Response.Headers.CacheControl = "no-store";
        await Send.ResultAsync(outcome.ToHttp(s => s));
    }
}
