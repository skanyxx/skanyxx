namespace Skanyxx.Core.Platform.Identity;

/// <summary>
/// One Entra group (its object id, a GUID) and what membership of it gives: roles among
/// <see cref="SkanyxxRoles.Grantable"/> and team slugs. <paramref name="Label"/> is the owner's note (e.g. the group's
/// display name); nothing matches on it.
/// </summary>
public sealed record EntraGroupMapDto(string GroupId, string? Label, IReadOnlyList<string> Roles, IReadOnlyList<string> Teams);
