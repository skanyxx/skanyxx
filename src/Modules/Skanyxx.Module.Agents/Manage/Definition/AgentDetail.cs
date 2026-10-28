using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Manage.Definition;

/// <param name="Source">An <see cref="AgentSource"/>.</param>
/// <param name="InChat">Labelled merged: Chat lists it, and employees can read it (D031, D254).</param>
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
    bool Accepted,
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
    string? Version);
