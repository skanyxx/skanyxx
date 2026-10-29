namespace Skanyxx.Module.Quotas.Features;

/// <summary>Quotas run per UTC calendar month; a month is its first day.</summary>
public static class QuotaMonth
{
    public static DateOnly Of(DateTimeOffset at) => new(at.UtcDateTime.Year, at.UtcDateTime.Month, 1);

    public static DateOnly ResetsOn(DateOnly month) => month.AddMonths(1);
}
