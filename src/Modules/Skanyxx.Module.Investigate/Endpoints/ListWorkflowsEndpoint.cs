using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Investigate.Features.Workflows;
using Skanyxx.Module.Investigate.Rules;

namespace Skanyxx.Module.Investigate.Endpoints;

internal sealed class ListWorkflowsEndpoint(IMediator mediator) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("workflows");
        Group<InvestigateGroup>();
        Roles(InvestigateGroup.Roles);
    }

    public override async Task HandleAsync(CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new ListWorkflowsQuery(Viewer.From(User)), ct)).ToHttp());
}
