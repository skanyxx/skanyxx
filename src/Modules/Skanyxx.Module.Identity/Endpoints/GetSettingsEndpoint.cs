using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Settings;

namespace Skanyxx.Module.Identity.Endpoints;

/// <summary>Every installation setting's effective value (D350). Owner only.</summary>
internal sealed class GetSettingsEndpoint(IMediator mediator) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("settings");
        Group<IdentityGroup>();
        Roles(SkanyxxRoles.Owner);
    }

    public override async Task HandleAsync(CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new GetInstallationSettingsQuery(), ct)).ToHttp());
}
