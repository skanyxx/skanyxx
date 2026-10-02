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
        Group<MemoryGroup>();
    }

    public override async Task HandleAsync(AgentGrantsRequest req, CancellationToken ct)
    {
        var outcome = await mediator.Send(new GetAgentGrantsQuery(HumanCaller.From(User), req.AgentId), ct);
        await Send.ResultAsync(outcome.ToHttp(g => g));
    }
}
