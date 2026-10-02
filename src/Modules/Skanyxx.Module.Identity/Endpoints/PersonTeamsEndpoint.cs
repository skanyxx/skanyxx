using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Endpoints;

internal sealed class PersonTeamsEndpoint(IMediator mediator) : Endpoint<IdRequest>
{
    public override void Configure()
    {
        Get("people/{id}/teams");
        Group<IdentityGroup>();
        Roles(SkanyxxRoles.Owner);
    }

    public override async Task HandleAsync(IdRequest req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new PersonTeamsQuery(req.Id), ct)).ToHttp(t => t));
}
