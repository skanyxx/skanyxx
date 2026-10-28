using FastEndpoints;
using Skanyxx.Core.Platform;
using Skanyxx.Module.ToolServers.Definition;
using Skanyxx.Module.ToolServers.Features;

namespace Skanyxx.Module.ToolServers.Endpoints;

/// <summary>Adds a RemoteMCPServer in kagent (D116: what changes kagent is the owner's).</summary>
internal sealed class CreateToolServerEndpoint(ToolServerCatalog catalog) : Endpoint<ToolServerForm>
{
    public override void Configure()
    {
        Post("/" + ToolServersGroup.Prefix);
        ToolServersGroup.Apply(Definition);
        Roles(SkanyxxRoles.Owner);
    }

    public override async Task HandleAsync(ToolServerForm req, CancellationToken ct) =>
        await Send.ResultAsync((await catalog.CreateAsync(User, req, ct)).ToHttp());
}
