namespace Skanyxx.Module.Memory.Mcp;

/// <summary>Marks an endpoint that only matches requests accepted on one of these local ports (<see cref="McpPortMatcherPolicy"/>).</summary>
internal sealed record McpPortMetadata(IReadOnlyList<int> Ports);
