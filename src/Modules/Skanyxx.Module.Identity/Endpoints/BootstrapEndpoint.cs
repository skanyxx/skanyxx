using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.Http;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Endpoints;

internal sealed class BootstrapEndpoint(IMediator mediator) : Endpoint<BootstrapRequest>
{
    public const string TokenHeader = "X-Bootstrap-Token";

    public override void Configure()
    {
        Post("bootstrap");
        Group<IdentityGroup>();
        AllowAnonymous();
    }

    public override async Task HandleAsync(BootstrapRequest req, CancellationToken ct)
    {
        var command = new BootstrapOwnerCommand(req.Email?.Trim() ?? "", req.Password ?? "", req.DisplayName,
            HttpContext.Request.Headers[TokenHeader].FirstOrDefault(), DirectLoopback.Is(HttpContext));
        var outcome = await mediator.Send(command, ct);
        // There is no current state worth returning to a second bootstrap, so a plain problem rather than ConflictResponse.
        await Send.ResultAsync(outcome.Status == OutcomeStatus.Conflict
            ? Results.Problem(outcome.Message, statusCode: StatusCodes.Status409Conflict)
            : outcome.ToHttp(a => a));
    }
}
