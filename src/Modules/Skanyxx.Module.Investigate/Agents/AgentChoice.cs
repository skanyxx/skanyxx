namespace Skanyxx.Module.Investigate.Agents;

/// <summary>
/// An agent a step may call, as the editor's dropdown lists it. <paramref name="Ref"/> is <c>namespace/name</c>. For the
/// chained-writes rule (D372, D386): <paramref name="Declarative"/> whether kagent knows its tools from its spec at all
/// (a BYO agent's are its own business), <paramref name="ToolServers"/> the MCP servers that spec names (null: unknown),
/// and <paramref name="CallsAgents"/> whether it calls other agents as tools. <paramref name="ModelConfig"/>
/// (in its own namespace, null for BYO) prices its steps against the quota.
/// </summary>
public sealed record AgentChoice(
    string Ref, string Namespace, string Name, string Description, bool Declarative, IReadOnlyList<string>? ToolServers, bool CallsAgents, string? ModelConfig);
