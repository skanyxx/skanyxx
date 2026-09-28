using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Memory.Features.AgentSecrets;

namespace Skanyxx.Module.Memory.Endpoints.AgentSecrets;

internal sealed class GetAgentSecretEndpoint(IMediator mediator) : Endpoint<AgentSecretRequest>
{
    public override void Configure()
    {
        Get("agents/{agentId}/secret");
        Group<MemoryGroup>();
    }

    public override async Task HandleAsync(AgentSecretRequest req, CancellationToken ct)
    {
        var outcome = await mediator.Send(new GetAgentSecretQuery(HumanCaller.From(User), req.AgentId), ct);
        await Send.ResultAsync(outcome.ToHttp(s => s));
    }
}
