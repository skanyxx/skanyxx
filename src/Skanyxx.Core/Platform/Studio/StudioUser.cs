using System.Security.Claims;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Core.Platform.Memory;

namespace Skanyxx.Core.Platform.Studio;

/// <summary>
/// The signed-in person in the studio (D028–D031): builders propose, supervisors merge (D024), both preview (D030);
/// the owner wears every hat. Built only by <see cref="From"/>, from the authenticated principal; an agent is refused.
/// </summary>
public sealed record StudioUser
{
    private StudioUser(string? userId, string name, string email, bool isOwner, bool isSupervisor, bool isBuilder)
    {
        UserId = userId;
        Name = name;
        Email = email;
        IsOwner = isOwner;
        IsSupervisor = isSupervisor;
        IsBuilder = isBuilder;
    }

    public string? UserId { get; }

    /// <summary>Display name for the commit author; the email when there is none.</summary>
    public string Name { get; }

    public string Email { get; }

    public bool IsOwner { get; }

    /// <summary>Supervisor or owner (<see cref="Caller.IsSupervisor"/>).</summary>
    public bool IsSupervisor { get; }

    public bool IsBuilder { get; }

    /// <summary>Opens a pull request (D020, D028): builders, and the owner.</summary>
    public bool CanPropose => IsBuilder || IsOwner;

    /// <summary>Makes a proposal live (D024).</summary>
    public bool CanMerge => IsSupervisor;

    /// <summary>Studio, preview chat and factory (D022, D030): never an employee-only account.</summary>
    public bool CanEnter => IsBuilder || IsSupervisor;

    public static StudioUser From(ClaimsPrincipal principal)
    {
        if (principal.Identities.Any(i => i.AuthenticationType == AgentPrincipal.SchemeName)
            || principal.HasClaim(c => c.Type == AgentPrincipal.ActsForUsersClaim))
            throw new InvalidOperationException("An agent is not a person: studio requests need a signed-in user.");
        var email = principal.FindFirstValue(ClaimTypes.Email) ?? principal.Identity?.Name ?? "";
        var name = principal.FindFirstValue(SkanyxxClaims.DisplayName) is { Length: > 0 } display ? display : email;
        return new(Caller.UserId(principal), name, email, Caller.IsOwner(principal), Caller.IsSupervisor(principal),
            principal.IsInRole(SkanyxxRoles.Builder));
    }
}
