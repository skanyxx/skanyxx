using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Identity.Features.Unlock;

namespace Skanyxx.Module.Identity.Endpoints;

internal sealed class UnlockEndpoint(IMediator mediator) : Endpoint<UnlockRequest>
{
    public override void Configure()
    {
        Post("unlock");
        Group<IdentityGroup>();
        AllowAnonymous();
    }

    public override async Task HandleAsync(UnlockRequest req, CancellationToken ct)
    {
        var outcome = await mediator.Send(new UnlockOwnerCommand(req.Email?.Trim() ?? "",
            HttpContext.Request.Headers[BootstrapEndpoint.TokenHeader].FirstOrDefault()), ct);
        if (outcome.Status == OutcomeStatus.Ok)
            await Send.NoContentAsync(ct);
        else
            await Send.ResultAsync(outcome.ToHttp(r => r));
    }
}
