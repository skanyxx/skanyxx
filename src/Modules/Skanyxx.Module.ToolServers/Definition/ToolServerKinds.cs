namespace Skanyxx.Module.ToolServers.Definition;

/// <summary>kagent's tool server kinds (<c>groupKind</c> in its list). Skanyxx creates and edits remote servers only.</summary>
public static class ToolServerKinds
{
    public const string Remote = "RemoteMCPServer.kagent.dev";

    /// <summary>A stdio server kagent runs in a pod (kmcp's MCPServer): listed, never created or changed here.</summary>
    public const string Stdio = "MCPServer.kagent.dev";

    public const string Service = "Service";

    public static string Label(string groupKind) => groupKind switch
    {
        Remote => "Remote MCP server",
        Stdio => "Stdio MCP server",
        Service => "Kubernetes Service",
        _ => groupKind.Length == 0 ? "Unknown kind" : groupKind
    };
}
