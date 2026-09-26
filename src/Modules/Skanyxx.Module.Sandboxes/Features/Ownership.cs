namespace Skanyxx.Module.Sandboxes.Features;

/// <summary>
/// Stop, suspend and resume: creator or supervisor. Replace: the creator only — a replace re-runs code under the
/// task's memory identity, so a supervisor may not do it to someone else's task; a task with no recorded owner (made
/// outside Skanyxx) may be replaced by a supervisor, who then owns it.
/// </summary>
internal static class Ownership
{
    public static bool CanManage(SandboxesOptions options, string user, string? owner) =>
        (owner is not null && owner == user) || options.Supervisors.Contains(user);

    public static bool CanReplace(SandboxesOptions options, string user, string? owner) =>
        owner is null ? options.Supervisors.Contains(user) : owner == user;
}
