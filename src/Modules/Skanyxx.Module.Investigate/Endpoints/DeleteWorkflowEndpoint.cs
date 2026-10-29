using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Investigate.Features.Workflows;
using Skanyxx.Module.Investigate.Rules;

namespace Skanyxx.Module.Investigate.Endpoints;

internal sealed class DeleteWorkflowEndpoint(IMediator mediator) : Endpoint<IdRequest>
{
    public override void Configure()
    {
        Delete("workflows/{id}");
        Group<InvestigateGroup>();
        Roles(InvestigateGroup.Roles);
    }

    public override async Task HandleAsync(IdRequest req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new DeleteWorkflowCommand(Viewer.From(User), req.Id), ct)).ToHttp(id => new { id }));
}
