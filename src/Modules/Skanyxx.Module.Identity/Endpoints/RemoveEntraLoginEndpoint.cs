using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Endpoints;

internal sealed class RemoveEntraLoginEndpoint(IMediator mediator) : Endpoint<IdRequest>
{
    public override void Configure()
    {
        Delete("people/{id}/entra-login");
        Group<IdentityGroup>();
        Roles(SkanyxxRoles.Owner);
    }

    public override async Task HandleAsync(IdRequest req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new RemoveEntraLoginCommand(Caller.UserId(User)!, req.Id), ct)).ToHttp(p => p));
}
