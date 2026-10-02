namespace Skanyxx.Module.Sandboxes.Domain;

public sealed record SandboxWorkspace(string Name, IReadOnlyList<GitSource> Git, IReadOnlyList<McpServerEntry> McpServers, bool MemoryAttached);
