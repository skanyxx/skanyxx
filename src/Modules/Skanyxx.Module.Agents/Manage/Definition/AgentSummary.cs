namespace Skanyxx.Module.Agents.Manage.Definition;

/// <summary>One row of the Agents list (fields as on <see cref="AgentDetail"/>).</summary>
internal sealed record AgentSummary(
    string Namespace,
    string Name,
    string Description,
    string Type,
    bool Ready,
    bool Accepted,
    string Source,
    bool InChat,
    bool CanEdit,
    bool CanDelete,
    string? Protection,
    IReadOnlyList<string> McpServers);
