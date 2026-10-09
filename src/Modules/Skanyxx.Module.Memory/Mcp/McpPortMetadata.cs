namespace Skanyxx.Module.Memory.Mcp;

/// <summary>Marks an endpoint that only matches requests accepted on this local port (<see cref="McpPortMatcherPolicy"/>).</summary>
internal sealed record McpPortMetadata(int Port);
