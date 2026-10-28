namespace Skanyxx.Module.ToolServers.Definition;

/// <summary>The transports of a RemoteMCPServer (kagent's <c>spec.protocol</c> enum).</summary>
public static class ToolServerProtocols
{
    public const string StreamableHttp = "STREAMABLE_HTTP";
    public const string Sse = "SSE";

    public static readonly IReadOnlyList<string> All = [StreamableHttp, Sse];
}
