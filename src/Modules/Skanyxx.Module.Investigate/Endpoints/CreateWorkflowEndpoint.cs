using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Investigate.Features.Workflows;
using Skanyxx.Module.Investigate.Rules;

namespace Skanyxx.Module.Investigate.Endpoints;

internal sealed class CreateWorkflowEndpoint(IMediator mediator) : Endpoint<SaveWorkflowRequest>
{
    public override void Configure()
    {
        Post("workflows");
        Group<InvestigateGroup>();
        Roles(InvestigateGroup.Roles);
    }

    public override async Task HandleAsync(SaveWorkflowRequest req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(
            new SaveWorkflowCommand(Viewer.From(User), null, req.Name, req.Description, req.SharedTeam, req.Steps), ct)).ToHttp());
}
