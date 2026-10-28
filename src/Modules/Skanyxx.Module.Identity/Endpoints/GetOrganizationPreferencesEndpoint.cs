using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Preferences;

namespace Skanyxx.Module.Identity.Endpoints;

internal sealed class GetOrganizationPreferencesEndpoint(IMediator mediator) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("org/preferences");
        Group<IdentityGroup>();
        Roles(SkanyxxRoles.Owner);
    }

    public override async Task HandleAsync(CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new GetOrganizationPreferencesQuery(), ct)).ToHttp(s => s));
}
