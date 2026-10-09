namespace Skanyxx.Core.Platform.Identity;

public sealed record PasswordResetDetails(string Email, DateTimeOffset ExpiresAt);
