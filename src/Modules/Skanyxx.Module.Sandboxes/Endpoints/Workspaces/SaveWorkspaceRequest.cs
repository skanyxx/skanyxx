using FastEndpoints;
using Skanyxx.Module.Sandboxes.Domain;

namespace Skanyxx.Module.Sandboxes.Endpoints.Workspaces;

public sealed class SaveWorkspaceRequest : WorkspaceRouteRequest
{
    [DontBind(Source.QueryParam)]
    public List<GitSource>? Git { get; set; }

    [DontBind(Source.QueryParam)]
    public List<McpServerEntry>? McpServers { get; set; }

    /// <summary>Adds Skanyxx's memory MCP server (<c>Sandboxes:MemoryMcpUrl</c>) as <c>skanyxx-memory</c>.</summary>
    [DontBind(Source.QueryParam)]
    public bool AttachMemory { get; set; }
}
