using FastEndpoints;
using Skanyxx.Core.Platform;
using Skanyxx.Module.ToolServers.Features;

namespace Skanyxx.Module.ToolServers.Endpoints;

/// <summary>Every server with its status and fixes, and kagent's own state. Owner and supervisor only.</summary>
internal sealed class ListToolServersEndpoint(ToolServerCatalog catalog) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/" + ToolServersGroup.Prefix);
        ToolServersGroup.Apply(Definition);
        Roles(SkanyxxRoles.Owner, SkanyxxRoles.Supervisor);
    }

    public override async Task HandleAsync(CancellationToken ct) => await Send.OkAsync(await catalog.OverviewAsync(User, ct), ct);
}
