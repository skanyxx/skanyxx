using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Features.Passwords;

namespace Skanyxx.Module.Identity.Endpoints;

/// <summary>Anonymous; counted in the sign-in rate-limit window. <c>202</c> with the same body for every email (D156).</summary>
internal sealed class ForgotPasswordEndpoint(IMediator mediator) : Endpoint<ForgotPasswordRequest>
{
    public override void Configure()
    {
        Post("password/forgot");
        Group<IdentityGroup>();
        AllowAnonymous();
    }

    public override async Task HandleAsync(ForgotPasswordRequest req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new RequestPasswordResetCommand(req.Email?.Trim() ?? ""), ct))
            .ToHttp(_ => new { message = RequestPasswordResetHandler.Answer }));
}
