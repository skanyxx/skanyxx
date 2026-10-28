namespace Skanyxx.Module.ToolServers.Definition;

/// <param name="UsedBy">The agents that named the server: they lose its tools.</param>
public sealed record ToolServerDeleted(string Ref, IReadOnlyList<string> UsedBy);
