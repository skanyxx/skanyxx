namespace Skanyxx.Module.Identity.Endpoints;

/// <summary>Bound from the query string: <c>?before=&amp;limit=&amp;action=&amp;userId=</c>.</summary>
public sealed class ListAuditRequest
{
    public long? Before { get; set; }
    public int? Limit { get; set; }
    public string? Action { get; set; }
    public string? UserId { get; set; }
}
