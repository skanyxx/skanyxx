using Skanyxx.Module.Analytics.Reports;

namespace Skanyxx.Host.Tests.Analytics;

/// <summary>Every report is bounded: presets end today, a custom range is at most a year, never in the future.</summary>
public sealed class DateRangeTests
{
    private static readonly DateOnly Today = new(2026, 10, 28);

    [Theory]
    [InlineData("7d", 7)]
    [InlineData("30d", 30)]
    [InlineData("90d", 90)]
    [InlineData(null, 30)]
    public void Presets_EndToday(string? preset, int days)
    {
        var (range, error) = DateRange.Parse(preset, null, null, Today);

        Assert.Null(error);
        Assert.Equal(Today, range!.To);
        Assert.Equal(days, range.Days);
    }

    [Fact]
    public void Previous_IsTheSameLength_RightBefore()
    {
        var range = DateRange.Last(7, Today);

        Assert.Equal(new DateOnly(2026, 10, 15), range.Previous.From);
        Assert.Equal(new DateOnly(2026, 10, 21), range.Previous.To);
        Assert.Equal(7, range.Previous.Days);
    }

    [Fact]
    public void Custom_AtMostAYear()
    {
        Assert.NotNull(DateRange.Parse("custom", Today.AddDays(1 - DateRange.MaxDays), Today, Today).Range);
        var (range, error) = DateRange.Parse("custom", Today.AddDays(-DateRange.MaxDays), Today, Today);
        Assert.Null(range);
        Assert.Contains("366", error);
    }

    [Theory]
    [InlineData("2026-10-29", "2026-10-29")] // the future
    [InlineData("2026-10-20", "2026-10-10")] // backwards
    [InlineData("2021-10-27", "2021-10-28")] // more than five years back
    public void Custom_Refused(string from, string to) =>
        Assert.Null(DateRange.Parse("custom", DateOnly.Parse(from), DateOnly.Parse(to), Today).Range);

    [Theory]
    [InlineData("custom")]
    [InlineData("1y")]
    [InlineData("")]
    public void Custom_WithoutDates_OrAnUnknownPreset_IsRefused(string preset) =>
        Assert.NotNull(DateRange.Parse(preset, null, null, Today).Error);
}
