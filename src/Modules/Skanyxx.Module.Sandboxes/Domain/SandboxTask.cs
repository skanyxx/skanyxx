namespace Skanyxx.Module.Sandboxes.Domain;

/// <summary>
/// An AX task as Skanyxx shows it (distinct from kagent's A2A tasks and from ticket runs). Env values are never
/// returned — only names — because every caller can read every task until the identity slice.
/// </summary>
public sealed record SandboxTask(
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
