using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Tickets.Features.Pipelines;

namespace Skanyxx.Module.Tickets.Endpoints.Pipelines;

internal sealed class ListPipelinesEndpoint(IMediator mediator) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("pipelines");
        Group<TicketsGroup>();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var outcome = await mediator.Send(new ListPipelinesQuery(Caller.UserId(User)), ct);
        await Send.ResultAsync(outcome.ToHttp(p => p));
    }
}
