namespace Skanyxx.Module.Identity.Entra;

/// <summary>What applying a mapping changed on one account.</summary>
internal sealed record EntraRoleChanges(
    IReadOnlyList<string> AddedRoles, IReadOnlyList<string> RemovedRoles, IReadOnlyList<string> AddedTeams, IReadOnlyList<string> RemovedTeams)
{
    public bool RolesChanged => AddedRoles.Count > 0 || RemovedRoles.Count > 0;

    public bool Any => RolesChanged || AddedTeams.Count > 0 || RemovedTeams.Count > 0;
}
