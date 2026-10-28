using FastEndpoints;
using Skanyxx.Core.Platform;
using Skanyxx.Module.ToolServers.Features;

namespace Skanyxx.Module.ToolServers.Endpoints;

/// <summary>Deletes a remote server; 409 <c>in-use</c> lists the agents that use it until the owner confirms.</summary>
internal sealed class DeleteToolServerEndpoint(ToolServerCatalog catalog) : Endpoint<DeleteToolServerRequest>
{
    public override void Configure()
    {
        Delete("{namespace}/{name}");
        Group<ToolServersGroup>();
        Roles(SkanyxxRoles.Owner);
    }

    public override async Task HandleAsync(DeleteToolServerRequest req, CancellationToken ct) =>
        await Send.ResultAsync((await catalog.DeleteAsync(User, req.Namespace, req.Name, req.ConfirmInUse, ct)).ToHttp());
}
