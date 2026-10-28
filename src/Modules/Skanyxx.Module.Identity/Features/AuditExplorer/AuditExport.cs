namespace Skanyxx.Module.Identity.Features.AuditExplorer;

/// <summary>
/// The rows of an export, at most <see cref="ExportAuditCommand.MaxRows"/>; <paramref name="Total"/> is how many matched,
/// so a cut export says so. <paramref name="Emails"/> names the people the rows' ids belong to.
/// </summary>
internal sealed record AuditExport(IReadOnlyList<AuditEvent> Rows, long Total, IReadOnlyDictionary<string, string> Emails)
{
    public bool Truncated => Total > Rows.Count;

    public string? Email(string? id) => id is not null && Emails.TryGetValue(id, out var email) ? email : null;
}
