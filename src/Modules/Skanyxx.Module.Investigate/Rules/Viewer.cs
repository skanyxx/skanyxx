using System.Security.Claims;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Investigate.Rules;

/// <summary>
/// Who asks, from the authenticated principal only. <see cref="IsOwner"/> reaches every workflow and run;
/// <see cref="IsSupervisor"/> (owner or supervisor) reaches the work of the people in their own teams (D371); a builder
/// has their own and the workflows shared to their teams. <see cref="SeesAgentInternals"/> reads kagent's own failure
/// reasons (<see cref="Caller.SeesAgentInternals"/>, D556); every role the panel has does today, and the mapping checks it anyway.
/// </summary>
public sealed record Viewer(string UserId, bool IsSupervisor, bool IsOwner, bool SeesAgentInternals = false)
{
    public static Viewer From(ClaimsPrincipal user) =>
        new(Caller.UserId(user) ?? "", Caller.IsSupervisor(user), Caller.IsOwner(user), Caller.SeesAgentInternals(user));
}
