using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Investigate.Features.Runs;
using Skanyxx.Module.Investigate.Rules;

namespace Skanyxx.Module.Investigate.Endpoints;

/// <summary>202 with the queued run; the background worker does the work.</summary>
internal sealed class StartRunEndpoint(IMediator mediator) : Endpoint<StartRunRequest>
{
    public override void Configure()
    {
        Post("runs");
        Group<InvestigateGroup>();
        Roles(InvestigateGroup.Roles);
    }

    public override async Task HandleAsync(StartRunRequest req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new StartRunCommand(Viewer.From(User), req.WorkflowId, req.Input), ct)).ToHttp());
}
