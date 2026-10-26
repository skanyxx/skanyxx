using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Plugins.Features;

namespace Skanyxx.Module.Agents.Plugins.Endpoints;

/// <summary>The built-in catalog's listing: each plugin with the request that inspects it.</summary>
internal sealed class PluginCatalogEndpoint(IMediator mediator) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("catalog");
        Group<PluginsGroup>();
        Roles(SkanyxxRoles.Owner, SkanyxxRoles.Supervisor, SkanyxxRoles.Builder);
    }

    public override async Task HandleAsync(CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(
            new InspectPluginQuery(StudioUser.From(User), new PluginSourceRequest { Source = PluginSourceKinds.Catalog }, null), ct)).ToHttp());
}
