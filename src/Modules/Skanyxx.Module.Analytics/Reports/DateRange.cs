namespace Skanyxx.Module.Analytics.Reports;

/// <summary>
/// UTC days <see cref="From"/> to <see cref="To"/>, inclusive. Built only by <see cref="Parse"/>, which keeps every query
/// bounded: at most <see cref="MaxDays"/> days, never past today nor more than <see cref="MaxYearsBack"/> years back, and its compare period (<see cref="Previous"/>) is the
/// same length right before it.
/// </summary>
public sealed record DateRange
{
    public const int MaxDays = 366;

    /// <summary>How far back a custom range may start (its compare period goes one range further).</summary>
    public const int MaxYearsBack = 5;

    public static readonly IReadOnlyList<string> Presets = ["7d", "30d", "90d"];

    private DateRange(DateOnly from, DateOnly to) => (From, To) = (from, to);

    public DateOnly From { get; }

    public DateOnly To { get; }

    public int Days => To.DayNumber - From.DayNumber + 1;

    public DateRange Previous => new(From.AddDays(-Days), From.AddDays(-1));

    public IEnumerable<DateOnly> Each() => Enumerable.Range(0, Days).Select(From.AddDays);

    /// <summary>The last <paramref name="days"/> days up to today.</summary>
    public static DateRange Last(int days, DateOnly today) => new(today.AddDays(1 - days), today);

    /// <summary>
    /// A preset (<c>7d</c>, <c>30d</c>, <c>90d</c>; the default 30d) or <c>custom</c> with both dates. Null with a reason
    /// for anything else, a range of more than <see cref="MaxDays"/> days, an end after today or a start after the end.
    /// </summary>
    public static (DateRange? Range, string? Error) Parse(string? preset, DateOnly? from, DateOnly? to, DateOnly today)
    {
        switch (preset ?? "30d")
        {
            case "7d": return (Last(7, today), null);
            case "30d": return (Last(30, today), null);
            case "90d": return (Last(90, today), null);
            case "custom":
                if (from is not { } start || to is not { } end)
                    return (null, "A custom range needs both 'from' and 'to' (yyyy-MM-dd).");
                if (end > today)
                    return (null, "'to' cannot be after today (UTC).");
                if (start > end)
                    return (null, "'from' must be on or before 'to'.");
                if (start < today.AddYears(-MaxYearsBack))
                    return (null, $"'from' is at most {MaxYearsBack} years back.");
                if (end.DayNumber - start.DayNumber + 1 > MaxDays)
                    return (null, $"A range is at most {MaxDays} days.");
                return (new DateRange(start, end), null);
            default:
                return (null, "'range' is 7d, 30d, 90d or custom.");
        }
    }
}
