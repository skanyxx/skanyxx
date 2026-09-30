using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Endpoints;

internal sealed class CreateInviteEndpoint(IMediator mediator) : Endpoint<CreateInviteRequest>
{
    public override void Configure()
    {
        Post("invites");
        Group<IdentityGroup>();
        Roles(SkanyxxRoles.Owner);
    }

    public override async Task HandleAsync(CreateInviteRequest req, CancellationToken ct)
    {
        var outcome = await mediator.Send(new CreateInviteCommand(Caller.UserId(User)!, req.Email?.Trim() ?? "", req.Roles ?? []), ct);
        HttpContext.Response.Headers.CacheControl = "no-store";
        await Send.ResultAsync(outcome.ToHttp(i => new InviteCreatedResponse(i.InviteId, i.Link, i.ExpiresAt)));
    }
}
