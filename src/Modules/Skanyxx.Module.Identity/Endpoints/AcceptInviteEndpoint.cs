using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Endpoints;

/// <summary>Anonymous; counted in the sign-in rate-limit window. 201 with the sign-in body.</summary>
internal sealed class AcceptInviteEndpoint(IMediator mediator) : Endpoint<AcceptInviteRequest>
{
    public override void Configure()
    {
        Post("invites/accept");
        Group<IdentityGroup>();
        AllowAnonymous();
    }

    public override async Task HandleAsync(AcceptInviteRequest req, CancellationToken ct)
    {
        var outcome = await mediator.Send(new AcceptInviteCommand(req.Token ?? "", req.Password ?? "", req.DisplayName, req.UseCookie), ct);
        HttpContext.Response.Headers.CacheControl = "no-store";
        await Send.ResultAsync(outcome.ToHttp(s => s));
    }
}
