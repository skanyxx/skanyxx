namespace Skanyxx.Module.ToolServers.Definition;

/// <summary>The page: kagent's state, every server, and what the caller may do (writes are the owner's).</summary>
public sealed record ToolServersOverview(
    KAgentStatus KAgent,
    IReadOnlyList<ToolServerView> Servers,
    bool CanWrite,
    IReadOnlyList<string> Protocols,
    IReadOnlyList<SupportedKind> Kinds);
