namespace Skanyxx.Module.Identity.Endpoints;

/// <summary>Plus <c>sort=asc|desc</c> (by time, default newest first), <c>limit=</c> (1–200, 50) and the next page's <c>afterAt=&amp;afterId=</c>.</summary>
public sealed class SearchAuditRequest : AuditQueryRequest
{
    public string? Sort { get; set; }
    public int? Limit { get; set; }
    public DateTimeOffset? AfterAt { get; set; }
    public long? AfterId { get; set; }
}
