using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Studio.Endpoints;

/// <summary>An agent in main as a form, to propose a change (D2).</summary>
internal sealed class EditDraftEndpoint(IMediator mediator) : Endpoint<AgentRoute>
{
    public override void Configure() => Get("api/studio/agents/{name}/draft");

    public override async Task HandleAsync(AgentRoute req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new EditDraftQuery(StudioUser.From(User), req.Name), ct)).ToHttp());
}
