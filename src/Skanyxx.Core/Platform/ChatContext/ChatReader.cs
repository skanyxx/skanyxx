using System.Security.Claims;
using Skanyxx.Core.Platform.Memory;

namespace Skanyxx.Core.Platform.ChatContext;

/// <summary>
/// The signed-in person whose chats are read or written (D237). Built only by <see cref="From"/>, from the authenticated
/// principal, like memory's <see cref="LibraryUser"/> — but it opens no memory door: library commands still take a
/// LibraryUser, which only the Host and memory hold.
/// </summary>
public sealed record ChatReader
{
    private ChatReader(string userId, bool isSupervisor, bool isOwner) =>
        (UserId, IsSupervisor, IsOwner) = (userId, isSupervisor, isOwner);

    public string UserId { get; }

    public bool IsSupervisor { get; }

    public bool IsOwner { get; }

    /// <summary>An agent principal or an anonymous one throws: chats are people's.</summary>
    public static ChatReader From(ClaimsPrincipal principal)
    {
        if (AgentPrincipal.IsAgent(principal))
            throw new InvalidOperationException("An agent is not a person: chat requests need a signed-in user.");
        return new(Caller.UserId(principal) ?? throw new InvalidOperationException("Chat requests need a signed-in user."),
            Caller.IsSupervisor(principal), Caller.IsOwner(principal));
    }
}
