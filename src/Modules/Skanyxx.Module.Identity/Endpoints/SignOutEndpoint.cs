using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Endpoints;

internal sealed class SignOutEndpoint(IMediator mediator) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("sign-out");
        Group<IdentityGroup>();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        await mediator.Send(new SignOutCommand(Caller.UserId(User)!), ct);
        await Send.NoContentAsync(ct);
    }
}
