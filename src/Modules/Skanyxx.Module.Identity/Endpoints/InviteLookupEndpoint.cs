using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Endpoints;

/// <summary>What the accept page shows. Anonymous; counted in the sign-in rate-limit window.</summary>
internal sealed class InviteLookupEndpoint(IMediator mediator) : Endpoint<InviteTokenRequest>
{
    public override void Configure()
    {
        Post("invites/lookup");
        Group<IdentityGroup>();
        AllowAnonymous();
    }

    public override async Task HandleAsync(InviteTokenRequest req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new InviteStatusQuery(req.Token ?? ""), ct)).ToHttp(i => i));
}
