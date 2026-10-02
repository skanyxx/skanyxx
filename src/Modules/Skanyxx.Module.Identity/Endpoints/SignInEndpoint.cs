using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Endpoints;

internal sealed class SignInEndpoint(IMediator mediator) : Endpoint<SignInRequest>
{
    public override void Configure()
    {
        Post("sign-in");
        Group<IdentityGroup>();
        AllowAnonymous();
    }

    public override async Task HandleAsync(SignInRequest req, CancellationToken ct)
    {
        var outcome = await mediator.Send(new SignInCommand(req.Email?.Trim() ?? "", req.Password ?? "", req.UseCookie,
            HttpContext.Request.Headers[BootstrapEndpoint.TokenHeader].FirstOrDefault()), ct);
        // Busy account: the other sign-in finishes within one password hash.
        if (outcome.Status == OutcomeStatus.RateLimited)
            HttpContext.Response.Headers.RetryAfter = "1";
        await Send.ResultAsync(outcome.ToHttp(s => s));
    }
}
