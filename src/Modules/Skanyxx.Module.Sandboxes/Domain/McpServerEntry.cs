namespace Skanyxx.Module.Sandboxes.Domain;

/// <summary>An MCP server declared on a workspace. AX v0.3.1 only records it; the task's own image must read and use it.</summary>
public sealed record McpServerEntry(string Name, string Endpoint);
