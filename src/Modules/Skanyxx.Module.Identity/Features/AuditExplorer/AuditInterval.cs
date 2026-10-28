namespace Skanyxx.Module.Identity.Features.AuditExplorer;

/// <summary>
/// The histogram's bucket size: the smallest round interval that cuts the range into at most <see cref="MaxBuckets"/>.
/// Buckets are aligned to <see cref="Origin"/> (a Monday, midnight UTC), so a week starts on Monday and an hour on the hour.
/// </summary>
internal static class AuditInterval
{
    public const int MaxBuckets = 100;

    public static readonly DateTimeOffset Origin = new(2001, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan[] Steps =
    [
        TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(30), TimeSpan.FromHours(1), TimeSpan.FromHours(3), TimeSpan.FromHours(6), TimeSpan.FromHours(12),
        TimeSpan.FromDays(1), TimeSpan.FromDays(7)
    ];

    public static TimeSpan For(DateTimeOffset from, DateTimeOffset to) =>
        Steps.FirstOrDefault(step => (to - from) / step <= MaxBuckets, Steps[^1]);

    /// <summary>Every bucket start from the one holding <paramref name="from"/> up to <paramref name="to"/>, so empty buckets show as zero.</summary>
    public static IEnumerable<DateTimeOffset> Starts(DateTimeOffset from, DateTimeOffset to, TimeSpan interval)
    {
        var start = Origin + (from.ToUniversalTime() - Origin).Ticks / interval.Ticks * interval;
        for (var at = start; at < to; at += interval)
            yield return at;
    }
}
