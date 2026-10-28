namespace Skanyxx.Module.ToolServers.Definition;

/// <summary>A tool server kind and whether Skanyxx creates it (the Add form shows both).</summary>
public sealed record SupportedKind(string Kind, string Label, bool Supported, string Note)
{
    public static readonly IReadOnlyList<SupportedKind> All =
    [
        new(ToolServerKinds.Remote, "Remote MCP server (Streamable HTTP or SSE)", true, "kagent connects to the server's URL."),
        new(ToolServerKinds.Stdio, "Stdio MCP server (MCPServer)", false, "kagent runs a command in a pod. Listed here, managed with kubectl or kmcp."),
    ];
}
