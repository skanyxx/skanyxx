using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Identity.Features.Me;

namespace Skanyxx.Module.Identity.Endpoints;

internal sealed class MeEndpoint(IMediator mediator) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("me");
        Group<IdentityGroup>();
    }

    public override async Task HandleAsync(CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new GetMeQuery(Caller.UserId(User)!), ct)).ToHttp(a => a));
}
