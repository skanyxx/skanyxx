using MediatR;

namespace Skanyxx.Core.Platform.Identity;

/// <summary>
/// Replaces the person's roles with <paramref name="Roles"/> (any of <see cref="SkanyxxRoles.Grantable"/>); the owner
/// keeps <see cref="SkanyxxRoles.Owner"/> whatever is sent. A change ends the person's sessions, so no cookie or token
/// goes on carrying the old roles.
/// </summary>
public sealed record SetRolesCommand(string ActorId, string UserId, IReadOnlyList<string> Roles) : IRequest<Outcome<PersonDto>>;
