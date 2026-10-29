using FastEndpoints;
using MediatR;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Endpoints;

internal sealed class GetEntraSettingsEndpoint(IMediator mediator, IOptionsMonitor<IdentityModuleOptions> options) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("entra/settings");
        Group<IdentityGroup>();
        Roles(SkanyxxRoles.Owner);
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!options.CurrentValue.EntraAvailable)
            await Send.NotFoundAsync(ct);
        else
            await Send.ResultAsync((await mediator.Send(new GetEntraSettingsQuery(), ct)).ToHttp(s => s));
    }
}
