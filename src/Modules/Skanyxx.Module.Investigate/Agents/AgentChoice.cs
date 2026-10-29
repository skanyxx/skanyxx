namespace Skanyxx.Module.Investigate.Agents;

/// <summary>
/// An agent a step may call, as the editor's dropdown lists it. <paramref name="Ref"/> is <c>namespace/name</c>;
/// <paramref name="ToolServers"/> the MCP servers its spec names and <paramref name="CallsAgents"/> whether it calls other
/// agents as tools, for the chained-writes rule (D372).
/// </summary>
public sealed record AgentChoice(string Ref, string Namespace, string Name, string Description, IReadOnlyList<string>? ToolServers = null, bool CallsAgents = false);
