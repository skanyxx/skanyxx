using Skanyxx.Core.Platform;
using FastEndpoints;
using MediatR;
using Skanyxx.Module.Memory.Features.Grants;

namespace Skanyxx.Module.Memory.Endpoints.Grants;

internal sealed class SetAgentGrantsEndpoint(IMediator mediator) : Endpoint<AgentGrantsRequest>
{
    public override void Configure()
    {
        Put("grants/{agentId}");
        Group<MemoryGroup>();
    }

    public override async Task HandleAsync(AgentGrantsRequest req, CancellationToken ct)
    {
        var outcome = await mediator.Send(
            new SetAgentGrantsCommand(MemoryHeaders.Human(HttpContext), req.AgentId, req.Grants), ct);
        await Send.ResultAsync(outcome.ToHttp(g => g));
    }
}
