using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Investigate.Features.Runs;
using Skanyxx.Module.Investigate.Rules;

namespace Skanyxx.Module.Investigate.Endpoints;

internal sealed class ListRunsEndpoint(IMediator mediator) : Endpoint<ListRunsRequest>
{
    public override void Configure()
    {
        Get("runs");
        Group<InvestigateGroup>();
        Roles(InvestigateGroup.Roles);
    }

    public override async Task HandleAsync(ListRunsRequest req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(
            new ListRunsQuery(Viewer.From(User), req.WorkflowId, req.State, req.Mine, req.Q, req.Limit), ct)).ToHttp());
}
