using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Studio.Endpoints;

/// <summary>Owner only (D121): <c>200</c> suspended, <c>202</c> suspended in memory but still in kagent for a moment.</summary>
internal sealed class SuspendAgentEndpoint(IMediator mediator) : Endpoint<AgentRoute>
{
    public override void Configure() => Post("api/studio/agents/{name}/suspend");

    public override async Task HandleAsync(AgentRoute req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new SuspendAgentCommand(StudioUser.From(User), req.Name, true), ct)).ToHttp());
}
