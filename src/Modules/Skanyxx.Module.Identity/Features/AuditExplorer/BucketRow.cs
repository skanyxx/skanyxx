namespace Skanyxx.Module.Identity.Features.AuditExplorer;

/// <summary>A row of a histogram query: how many rows of one outcome fell in the bucket starting at <see cref="At"/>.</summary>
internal sealed class BucketRow
{
    public DateTimeOffset At { get; set; }
    public string Outcome { get; set; } = "";
    public long Count { get; set; }
}
