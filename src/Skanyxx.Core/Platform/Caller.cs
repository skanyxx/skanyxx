using System.Security.Claims;

namespace Skanyxx.Core.Platform;

/// <summary>
/// Who is calling, from the authenticated principal only — never from a header or a bound DTO. The user id is the
/// identity store's stable id (lowercase, <see cref="Identifier"/>-shaped). An owner also counts as a supervisor until
/// roles are assigned by invite (D024: in small orgs the owner wears the supervisor hat).
/// </summary>
public static class Caller
{
    public static string? UserId(ClaimsPrincipal principal) =>
        principal.Identity?.IsAuthenticated == true ? principal.FindFirstValue(ClaimTypes.NameIdentifier) : null;

    public static bool IsOwner(ClaimsPrincipal principal) => principal.IsInRole(SkanyxxRoles.Owner);

    public static bool IsSupervisor(ClaimsPrincipal principal) =>
        principal.IsInRole(SkanyxxRoles.Owner) || principal.IsInRole(SkanyxxRoles.Supervisor);
}
