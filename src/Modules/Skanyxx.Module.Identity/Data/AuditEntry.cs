namespace Skanyxx.Module.Identity.Data;

/// <summary>
/// One identity audit row (D152): who did what to whom, from where, when. Append-only (a trigger refuses UPDATE); only
/// the retention job deletes. <see cref="Details"/> is JSON and never holds a token, password or secret.
/// </summary>
public sealed class AuditEntry
{
    public long Id { get; set; }
    public DateTimeOffset AtUtc { get; set; }
    public string Action { get; set; } = "";

    /// <summary>The signed-in person who acted; null for an anonymous caller or a background job.</summary>
    public string? ActorId { get; set; }

    /// <summary>A user id, an invite id, a team or department slug, … — what <see cref="Action"/> names.</summary>
    public string? TargetId { get; set; }

    public string? RemoteIp { get; set; }
    public string? Details { get; set; }
}
