namespace Skanyxx.Module.Identity.Endpoints;

/// <summary>Plus <c>format=csv|json</c> and <c>sort=asc|desc</c>.</summary>
public sealed class ExportAuditRequest : AuditQueryRequest
{
    public string? Format { get; set; }
    public string? Sort { get; set; }
}
