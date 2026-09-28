using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Identity.Features.Refresh;

namespace Skanyxx.Module.Identity.Endpoints;

internal sealed class RefreshEndpoint(IMediator mediator) : Endpoint<RefreshRequest>
{
    public override void Configure()
    {
        Post("refresh");
        Group<IdentityGroup>();
        AllowAnonymous();
    }

    public override async Task HandleAsync(RefreshRequest req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new RefreshCommand(req.RefreshToken), ct)).ToHttp(t => t));
}
