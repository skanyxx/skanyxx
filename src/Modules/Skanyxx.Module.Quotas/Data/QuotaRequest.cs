namespace Skanyxx.Module.Quotas.Data;

/// <summary>A person asking for more tokens. An approved one adds <see cref="Amount"/> to their limit for <see cref="Month"/> (the month it was approved).</summary>
public sealed class QuotaRequest
{
    public long Id { get; set; }
    public string UserId { get; set; } = "";
    public long Amount { get; set; }
    public string Reason { get; set; } = "";
    public string Status { get; set; } = RequestStatuses.Pending;
    public DateTimeOffset CreatedAt { get; set; }
    public string? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public DateOnly? Month { get; set; }
}
