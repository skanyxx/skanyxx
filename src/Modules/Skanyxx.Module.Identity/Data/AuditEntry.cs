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

    /// <summary>The action's area, <c>person</c> of <c>person.disabled</c>; computed by Postgres from <see cref="Action"/>.</summary>
    public string Module { get; private set; } = "";

    /// <summary><c>refused</c>, <c>failed</c> or <c>success</c>, read off the action's last word; computed by Postgres.</summary>
    public string Outcome { get; private set; } = "";
}
