namespace Skanyxx.Module.Agents.Manage.Definition;

/// <summary>One card of the Agents list (fields as on <see cref="AgentDetail"/>).</summary>
/// <param name="ToolCount">MCP tools named on its servers (a server with no names counts once) plus agents called as tools.</param>
/// <param name="Accepted">kagent's Accepted condition; null while kagent has not reported it yet (starting, not refused, D559).</param>
internal sealed record AgentSummary(
    string Namespace,
    string Name,
    string Description,
    string Type,
    bool Ready,
    bool? Accepted,
    string Source,
    bool InChat,
    bool Suspended,
    bool CanEdit,
    bool CanDelete,
    string? Protection,
    IReadOnlyList<string> McpServers,
    string ModelConfig,
    int ToolCount)
{
    /// <summary>Null while its tool servers look fine or kagent did not list them; else by name for builders and up, a fixed text for anyone else (D554).</summary>
    public string? ToolProblem { get; init; }
}
