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
        // D392: Jira is read with Skanyxx's own token, so it is not everyone's to browse.
        Roles(SkanyxxRoles.Owner, SkanyxxRoles.Supervisor);
        Group<TicketsGroup>();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var outcome = await mediator.Send(new ListAssigneesQuery(Caller.UserId(User)), ct);
        await Send.ResultAsync(outcome.ToHttp(a => a));
    }
}
