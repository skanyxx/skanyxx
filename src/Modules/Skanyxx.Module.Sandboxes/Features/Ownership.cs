namespace Skanyxx.Module.Sandboxes.Features;

/// <summary>
/// The whole sandboxes API is supervisors' and the owner's (D392), so every one of them may stop, suspend and resume any
/// sandbox. Replace is the creator's only — a replace re-runs code under the sandbox's memory identity, so a supervisor
/// may not do it to someone else's; a sandbox with no recorded owner (made outside Skanyxx) may be replaced, and is then
/// the replacer's.
/// </summary>
internal static class Ownership
{
    public static bool CanReplace(string user, string? owner) => owner is null || owner == user;
}
