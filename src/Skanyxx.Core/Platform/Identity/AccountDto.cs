namespace Skanyxx.Core.Platform.Identity;

public sealed record AccountDto(string Id, string Email, string? DisplayName, IReadOnlyList<string> Roles);
