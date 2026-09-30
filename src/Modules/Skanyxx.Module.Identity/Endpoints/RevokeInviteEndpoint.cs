using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Endpoints;

internal sealed class RevokeInviteEndpoint(IMediator mediator) : Endpoint<IdRequest>
{
    public override void Configure()
    {
        Delete("invites/{id}");
        Group<IdentityGroup>();
        Roles(SkanyxxRoles.Owner);
    }

    public override async Task HandleAsync(IdRequest req, CancellationToken ct)
    {
        var outcome = await mediator.Send(new RevokeInviteCommand(Caller.UserId(User)!, req.Id), ct);
        if (outcome.Status == OutcomeStatus.Ok)
            await Send.NoContentAsync(ct);
        else
            await Send.ResultAsync(outcome.ToHttp(r => r));
    }
}
