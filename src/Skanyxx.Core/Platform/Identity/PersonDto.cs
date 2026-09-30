namespace Skanyxx.Core.Platform.Identity;

public sealed record PersonDto(string Id, string Email, string? DisplayName, IReadOnlyList<string> Roles, bool Disabled);
