namespace Skanyxx.Core.Platform.Identity;

public sealed record InviteDetails(string Email, IReadOnlyList<string> Roles, DateTimeOffset ExpiresAt);
