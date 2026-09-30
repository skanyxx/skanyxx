using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Endpoints;

internal sealed class ListInvitesEndpoint(IMediator mediator) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("invites");
        Group<IdentityGroup>();
        Roles(SkanyxxRoles.Owner);
    }

    public override async Task HandleAsync(CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new ListInvitesQuery(), ct)).ToHttp(i => i));
}
