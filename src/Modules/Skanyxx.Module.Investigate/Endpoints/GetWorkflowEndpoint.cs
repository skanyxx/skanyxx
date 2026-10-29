using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Investigate.Features.Workflows;
using Skanyxx.Module.Investigate.Rules;

namespace Skanyxx.Module.Investigate.Endpoints;

internal sealed class GetWorkflowEndpoint(IMediator mediator) : Endpoint<IdRequest>
{
    public override void Configure()
    {
        Get("workflows/{id}");
        Group<InvestigateGroup>();
        Roles(InvestigateGroup.Roles);
    }

    public override async Task HandleAsync(IdRequest req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new GetWorkflowQuery(Viewer.From(User), req.Id), ct)).ToHttp());
}
