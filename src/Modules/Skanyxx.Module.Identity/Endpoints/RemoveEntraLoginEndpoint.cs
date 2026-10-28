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

    // Not behind the Entra switch (D272): removing a Microsoft login is how an owner cleans up after turning it off.
    public override async Task HandleAsync(IdRequest req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new RemoveEntraLoginCommand(Caller.UserId(User)!, req.Id), ct)).ToHttp(p => p));
}
