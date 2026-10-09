namespace Skanyxx.Module.Identity.Data;

/// <summary>
/// One password-reset link (D156). Only the SHA-256 of its token is stored. Pending = neither used nor revoked, and
/// before <see cref="ExpiresUtc"/>; at most one per account is open at a time (a newer link revokes the older one).
/// <see cref="CreatedBy"/>: the owner who issued it, or null when the person asked by email.
/// </summary>
public sealed class PasswordReset
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public byte[] TokenHash { get; set; } = [];
    public string? CreatedBy { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset ExpiresUtc { get; set; }
    public DateTimeOffset? UsedUtc { get; set; }
    public DateTimeOffset? RevokedUtc { get; set; }
}
