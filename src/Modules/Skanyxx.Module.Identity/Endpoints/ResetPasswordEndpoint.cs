using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Endpoints;

/// <summary>Anonymous; counted in the one-time-link rate-limit window. <c>204</c>: the password is set and every session ended.</summary>
internal sealed class ResetPasswordEndpoint(IMediator mediator) : Endpoint<ResetPasswordRequest>
{
    public override void Configure()
    {
        Post("password/reset");
        Group<IdentityGroup>();
        AllowAnonymous();
    }

    public override async Task HandleAsync(ResetPasswordRequest req, CancellationToken ct)
    {
        var outcome = await mediator.Send(new CompletePasswordResetCommand(req.Token ?? "", req.Password ?? ""), ct);
        HttpContext.Response.Headers.CacheControl = "no-store";
        if (outcome.Status == OutcomeStatus.Ok)
            await Send.NoContentAsync(ct);
        else
            await Send.ResultAsync(outcome.ToHttp(r => r));
    }
}
