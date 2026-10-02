namespace Skanyxx.Core.Platform.Identity;

/// <summary>
/// <paramref name="EntraManaged"/>: the account has a Microsoft Entra login, so its roles and teams are rewritten from
/// the group mapping at each Microsoft sign-in. <paramref name="HasPassword"/>: it can also sign in with a password; an
/// account Microsoft sign-in created has none, so its Microsoft login cannot be removed (D16).
/// </summary>
public sealed record PersonDto(string Id, string Email, string? DisplayName, IReadOnlyList<string> Roles, bool Disabled, bool EntraManaged, bool HasPassword);
