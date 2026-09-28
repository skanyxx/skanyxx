namespace Skanyxx.Module.Sandboxes.Features;

/// <summary>
/// Stop, suspend and resume: creator or supervisor. Replace: the creator only — a replace re-runs code under the
/// task's memory identity, so a supervisor may not do it to someone else's task; a task with no recorded owner (made
/// outside Skanyxx) may be replaced by a supervisor, who then owns it. Supervisor is the signed-in user's role.
/// </summary>
internal static class Ownership
{
    public static bool CanManage(bool isSupervisor, string user, string? owner) =>
        (owner is not null && owner == user) || isSupervisor;

    public static bool CanReplace(bool isSupervisor, string user, string? owner) =>
        owner is null ? isSupervisor : owner == user;
}
