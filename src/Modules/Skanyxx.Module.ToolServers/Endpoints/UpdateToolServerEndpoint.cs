using FastEndpoints;
using Skanyxx.Core.Platform;
using Skanyxx.Module.ToolServers.Definition;
using Skanyxx.Module.ToolServers.Features;

namespace Skanyxx.Module.ToolServers.Endpoints;

/// <summary>Replaces a server added on this page (kagent has no update: delete, then create). Owner only.</summary>
internal sealed class UpdateToolServerEndpoint(ToolServerCatalog catalog) : Endpoint<ToolServerForm>
{
    public override void Configure()
    {
        Put("{namespace}/{name}");
        Group<ToolServersGroup>();
        Roles(SkanyxxRoles.Owner);
    }

    public override async Task HandleAsync(ToolServerForm req, CancellationToken ct)
    {
        req.Namespace = Route<string>("namespace");
        req.Name = Route<string>("name");
        await Send.ResultAsync((await catalog.UpdateAsync(User, req, ct)).ToHttp());
    }
}
