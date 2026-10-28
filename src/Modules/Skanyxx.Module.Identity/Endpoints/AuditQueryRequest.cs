using Skanyxx.Module.Identity.Features.AuditExplorer;

namespace Skanyxx.Module.Identity.Endpoints;

/// <summary>
/// The explorer's query string: <c>?from=&amp;to=</c> (ISO 8601; default the last 24 hours), <c>q=</c> free text,
/// repeated <c>f=field:value</c> chips and <c>nf=field:value</c> exclusions (<c>(none)</c> for an empty field).
/// </summary>
public class AuditQueryRequest
{
    public static readonly TimeSpan DefaultRange = TimeSpan.FromHours(24);

    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
    public string? Q { get; set; }
    public string[]? F { get; set; }
    public string[]? Nf { get; set; }

    internal AuditFilter Filter(DateTimeOffset now)
    {
        var to = To ?? now;
        return new AuditFilter(From ?? to - DefaultRange, to, Q,
            [.. (F ?? []).Select(c => AuditFieldFilter.Parse(c, exclude: false)), .. (Nf ?? []).Select(c => AuditFieldFilter.Parse(c, exclude: true))]);
    }
}
