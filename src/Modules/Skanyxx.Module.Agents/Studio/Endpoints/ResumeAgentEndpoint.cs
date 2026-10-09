using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Studio.Endpoints;

/// <summary>Owner only (D121): lifts the suspension; the reconciler puts the agent back from main.</summary>
internal sealed class ResumeAgentEndpoint(IMediator mediator) : Endpoint<AgentRoute>
{
    public override void Configure() => Post("api/studio/agents/{name}/resume");

    public override async Task HandleAsync(AgentRoute req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new SuspendAgentCommand(StudioUser.From(User), req.Name, false), ct)).ToHttp());
}
