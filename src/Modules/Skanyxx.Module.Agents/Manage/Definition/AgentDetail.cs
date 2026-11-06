using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Manage.Definition;

/// <param name="Accepted">kagent's Accepted condition; null while kagent has not reported it yet (starting, not refused, D559).</param>
/// <param name="Source">An <see cref="AgentSource"/>.</param>
/// <param name="InChat">Chat offers it unless suspended (<see cref="Skanyxx.Core.Platform.Chat.ChatOffer"/>, D557), and employees can read it then (D254). On a write's response it judges the agent alone, not the agents it calls.</param>
/// <param name="Suspended">Scaled to zero on the Agents page and out of Chat (D257).</param>
/// <param name="CanEdit">The caller may change it here: a supervisor, and an agent neither the studio's nor Skanyxx's.</param>
/// <param name="Protection">Why it cannot be changed here, when it cannot.</param>
/// <param name="StudioPath">For a studio agent, its file in the agent repo.</param>
/// <param name="SystemMessage">Only for builders, supervisors and the owner.</param>
/// <param name="Version">
/// The spec kagent holds, as a hash: an edit sends it back (C-2). Null on a write's answer, since kagent fills in
/// defaults; read the agent again to edit it.
/// </param>
internal sealed record AgentDetail(
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
    string? StudioPath,
    string? ModelConfig,
    string? SystemMessage,
    string? Runtime,
    bool Stream,
    IReadOnlyList<McpToolChoice> McpTools,
    IReadOnlyList<string> AgentTools,
    IReadOnlyList<string> Skills,
    IReadOnlyList<A2aSkillForm> A2aSkills,
    IReadOnlyList<AgentCondition> Conditions,
    string? Version)
{
    public AgentMemoryForm? Memory { get; init; }

    /// <summary><c>deployment.replicas</c> when set (0 = suspended); null = kagent's default of one.</summary>
    public int? Replicas { get; init; }

    /// <summary>The MCP tools grouped by server with kagent's descriptions (the details view).</summary>
    public IReadOnlyList<ServerTools> ToolsByServer { get; init; } = [];

    /// <summary>Agents in its namespace that call it as a tool: deleting it breaks them. Only for builders and up.</summary>
    public IReadOnlyList<string> UsedBy { get; init; } = [];

    /// <inheritdoc cref="AgentSummary.ToolProblem"/>
    public string? ToolProblem { get; init; }
}
