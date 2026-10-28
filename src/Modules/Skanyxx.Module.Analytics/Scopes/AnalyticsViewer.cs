using System.Security.Claims;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Analytics.Scopes;

/// <summary>Who is looking, from the authenticated principal only. A supervisor is the brief's "maintainer" (requirements.md O8).</summary>
public sealed record AnalyticsViewer(string UserId, bool IsOwner, bool IsSupervisor)
{
    public static AnalyticsViewer From(ClaimsPrincipal principal) =>
        new(Caller.UserId(principal) ?? throw new InvalidOperationException("Analytics needs a signed-in person."),
            Caller.IsOwner(principal), Caller.IsSupervisor(principal));
}
