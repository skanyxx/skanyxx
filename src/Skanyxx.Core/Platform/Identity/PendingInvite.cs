namespace Skanyxx.Core.Platform.Identity;

public sealed record PendingInvite(string Id, string Email, IReadOnlyList<string> Roles, string CreatedBy, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);
