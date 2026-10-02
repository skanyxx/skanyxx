using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Endpoints;

internal sealed class SetRolesEndpoint(IMediator mediator) : Endpoint<SetRolesRequest>
{
    public override void Configure()
    {
        Put("people/{id}/roles");
        Group<IdentityGroup>();
        Roles(SkanyxxRoles.Owner);
    }

    public override async Task HandleAsync(SetRolesRequest req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new SetRolesCommand(Caller.UserId(User)!, req.Id, req.Roles ?? []), ct)).ToHttp(p => p));
}
