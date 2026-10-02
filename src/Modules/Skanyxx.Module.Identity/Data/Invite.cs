namespace Skanyxx.Module.Identity.Data;

/// <summary>
/// One invitation (D026). Only the SHA-256 of its token is stored. Pending = neither accepted nor revoked, and before
/// <see cref="ExpiresUtc"/>; at most one invite per email is open (neither accepted nor revoked) at a time.
/// </summary>
public sealed class Invite
{
    public string Id { get; set; } = "";
    public string Email { get; set; } = "";
    public string NormalizedEmail { get; set; } = "";
    public string[] Roles { get; set; } = [];
    public byte[] TokenHash { get; set; } = [];
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset ExpiresUtc { get; set; }
    public DateTimeOffset? AcceptedUtc { get; set; }
    public string? AcceptedUserId { get; set; }
    public DateTimeOffset? RevokedUtc { get; set; }
}
