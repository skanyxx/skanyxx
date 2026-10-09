namespace Skanyxx.Core.Platform.Identity;

/// <summary><paramref name="PasswordResetByEmail"/>: SMTP is configured, so "Forgot your password?" sends a reset link (D156).</summary>
public sealed record IdentityStatus(bool Bootstrapped, bool PasswordResetByEmail);
