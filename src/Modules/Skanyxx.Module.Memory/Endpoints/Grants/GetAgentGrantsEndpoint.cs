using Skanyxx.Core.Platform;
using FastEndpoints;
using MediatR;
using Skanyxx.Module.Memory.Features.Grants;

namespace Skanyxx.Module.Memory.Endpoints.Grants;

internal sealed class GetAgentGrantsEndpoint(IMediator mediator) : Endpoint<AgentGrantsRequest>
{
    public override void Configure()
    {
        Get("grants/{agentId}");
        // D392: supervisors' and the owner's; the handler still decides what only the owner may do.
        Roles(SkanyxxRoles.Owner, SkanyxxRoles.Supervisor);
        Group<MemoryGroup>();
    }

    public override async Task HandleAsync(AgentGrantsRequest req, CancellationToken ct)
    {
        var outcome = await mediator.Send(new GetAgentGrantsQuery(HumanCaller.From(User), req.AgentId), ct);
        await Send.ResultAsync(outcome.ToHttp(g => g));
    }
}
