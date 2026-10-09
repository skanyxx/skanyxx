using System.Security.Claims;

namespace Skanyxx.Core.Platform.Memory;

/// <summary>
/// The signed-in person asking memory. Built only by <see cref="From"/>, from the authenticated principal: memory trusts
/// <see cref="UserId"/> and the roles without checking them, so no caller may make one up.
/// </summary>
public sealed record LibraryUser
{
    private LibraryUser(string? userId, bool isSupervisor, bool isOwner)
    {
        UserId = userId;
        IsSupervisor = isSupervisor;
        IsOwner = isOwner;
    }

    public string? UserId { get; }

    public bool IsSupervisor { get; }

    public bool IsOwner { get; }

    /// <summary>
    /// The person behind <paramref name="principal"/>; an anonymous one has no <see cref="UserId"/> and is refused by
    /// validation. An agent principal (memory's agent-secret scheme) throws: library requests are human actions, and
    /// handing one an agent would make its id a personal scope.
    /// </summary>
    public static LibraryUser From(ClaimsPrincipal principal)
    {
        if (principal.Identities.Any(i => i.AuthenticationType == AgentPrincipal.SchemeName)
            || principal.HasClaim(c => c.Type == AgentPrincipal.ActsForUsersClaim))
            throw new InvalidOperationException("An agent is not a person: library requests need a signed-in user.");
        return new(Caller.UserId(principal), Caller.IsSupervisor(principal), Caller.IsOwner(principal));
    }
}
