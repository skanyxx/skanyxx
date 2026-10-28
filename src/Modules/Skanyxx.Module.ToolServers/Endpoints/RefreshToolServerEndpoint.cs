using FastEndpoints;
using Skanyxx.Core.Platform;
using Skanyxx.Module.ToolServers.Features;

namespace Skanyxx.Module.ToolServers.Endpoints;

/// <summary>Reads what kagent discovered on one server now (kagent rediscovers each minute). Owner and supervisor.</summary>
internal sealed class RefreshToolServerEndpoint(ToolServerCatalog catalog) : Endpoint<ToolServerRoute>
{
    public override void Configure()
    {
        Post("{namespace}/{name}/refresh");
        Group<ToolServersGroup>();
        Roles(SkanyxxRoles.Owner, SkanyxxRoles.Supervisor);
    }

    public override async Task HandleAsync(ToolServerRoute req, CancellationToken ct) =>
        await Send.ResultAsync((await catalog.RefreshAsync(User, req.Namespace, req.Name, ct)).ToHttp());
}
