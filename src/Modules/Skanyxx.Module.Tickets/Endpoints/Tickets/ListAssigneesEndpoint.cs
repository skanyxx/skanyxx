using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Tickets.Features.Tickets;

namespace Skanyxx.Module.Tickets.Endpoints.Tickets;

internal sealed class ListAssigneesEndpoint(IMediator mediator) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("issues/assignees");
        Group<TicketsGroup>();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var outcome = await mediator.Send(new ListAssigneesQuery(TicketsHeaders.User(HttpContext)), ct);
        await Send.ResultAsync(outcome.ToHttp(a => a));
    }
}
