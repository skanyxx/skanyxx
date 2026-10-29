using System.Security.Claims;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Quotas.Features;

/// <summary>Who is acting, from the authenticated principal only.</summary>
public sealed record QuotaViewer(string UserId, bool IsOwner, bool IsSupervisor)
{
    public static QuotaViewer From(ClaimsPrincipal principal) =>
        new(Caller.UserId(principal) ?? throw new InvalidOperationException("Quotas need a signed-in person."),
            Caller.IsOwner(principal), Caller.IsSupervisor(principal));

    /// <summary>The people this viewer may see and act for: everyone for the owner, their teams' members for a supervisor.</summary>
    public IReadOnlySet<string> People(OrgSnapshot org) =>
        IsOwner ? org.People.Select(p => p.Id).ToHashSet() : IsSupervisor ? org.SupervisedBy(UserId) : new HashSet<string>();

    /// <summary>
    /// A supervisor acts only on their own teams' members, and never on themselves, the owner or another supervisor:
    /// those quotas and requests are the owner's (D333, D338).
    /// </summary>
    public bool MayActFor(string userId, OrgSnapshot org) =>
        IsOwner || (IsSupervisor && userId != UserId && org.SupervisedBy(UserId).Contains(userId)
            && org.Person(userId) is { } person && !person.Roles.Contains(SkanyxxRoles.Owner) && !person.Roles.Contains(SkanyxxRoles.Supervisor));
}
