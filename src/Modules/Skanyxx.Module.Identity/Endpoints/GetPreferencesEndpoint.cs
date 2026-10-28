using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Preferences;

namespace Skanyxx.Module.Identity.Endpoints;

internal sealed class GetPreferencesEndpoint(IMediator mediator) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("preferences");
        Group<MeGroup>();
    }

    public override async Task HandleAsync(CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new GetPreferencesQuery(Caller.UserId(User)!), ct)).ToHttp(v => v));
}
