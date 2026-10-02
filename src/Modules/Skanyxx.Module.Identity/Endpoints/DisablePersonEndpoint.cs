using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Endpoints;

internal sealed class DisablePersonEndpoint(IMediator mediator) : Endpoint<IdRequest>
{
    public override void Configure()
    {
        Post("people/{id}/disable");
        Group<IdentityGroup>();
        Roles(SkanyxxRoles.Owner);
    }

    public override async Task HandleAsync(IdRequest req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new SetDisabledCommand(Caller.UserId(User)!, req.Id, Disabled: true), ct)).ToHttp(p => p));
}
