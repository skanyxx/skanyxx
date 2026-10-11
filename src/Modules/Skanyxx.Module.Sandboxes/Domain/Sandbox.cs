namespace Skanyxx.Module.Sandboxes.Domain;

/// <summary>
/// A sandbox as Skanyxx shows it — one AX <c>Task</c> (not a kagent A2A conversation, not a ticket run). Env values
/// are never returned — only names — because every caller can read every sandbox until the identity slice.
/// </summary>
public sealed record Sandbox(
    string Name,
    string? Owner,
    string? AgentId,
    string Image,
    IReadOnlyList<string> Command,
    IReadOnlyList<string> EnvNames,
    IReadOnlyList<WorkspaceMount> Workspaces,
    SandboxResources? Resources,
    bool Suspended,
    string Phase,
    IReadOnlyList<SandboxCondition> Conditions,
    DateTime? CreatedAt);
