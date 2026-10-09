using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Endpoints;

/// <summary>What the reset page shows. Anonymous; counted in the one-time-link rate-limit window.</summary>
internal sealed class ResetLookupEndpoint(IMediator mediator) : Endpoint<InviteTokenRequest>
{
    public override void Configure()
    {
        Post("password/lookup");
        Group<IdentityGroup>();
        AllowAnonymous();
    }

    public override async Task HandleAsync(InviteTokenRequest req, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "no-store";
        await Send.ResultAsync((await mediator.Send(new PasswordResetStatusQuery(req.Token ?? ""), ct)).ToHttp(d => d));
    }
}
