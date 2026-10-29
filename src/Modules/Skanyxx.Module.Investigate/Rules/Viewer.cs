using System.Security.Claims;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Investigate.Rules;

/// <summary>
/// Who asks, from the authenticated principal only. <see cref="IsSupervisor"/>: the owner or a supervisor, who see and
/// edit every workflow and run; a builder has their own and those shared to their teams.
/// </summary>
public sealed record Viewer(string UserId, bool IsSupervisor)
{
    public static Viewer From(ClaimsPrincipal user) => new(Caller.UserId(user) ?? "", Caller.IsSupervisor(user));
}
