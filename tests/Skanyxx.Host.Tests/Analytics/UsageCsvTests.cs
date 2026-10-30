using Skanyxx.Core.Platform.Analytics;
using Skanyxx.Module.Analytics;
using Skanyxx.Module.Analytics.Data;
using Skanyxx.Module.Analytics.Reports;

namespace Skanyxx.Host.Tests.Analytics;

/// <summary>The export: the disclaimer first, fixed columns of counts and names, nothing a spreadsheet would run.</summary>
public sealed class UsageCsvTests
{
    [Fact]
    public void DisclaimerFirst_ThenFixedColumns()
    {
        var csv = UsageCsv.Write([new UsageDay { Day = new(2026, 10, 28), UserId = "ann", Agent = "kagent/seed", Model = "m", Requests = 2, TokensIn = 5, ClientTokens = 3 }],
            OrgFixture.Snapshot, new CostCalculator(new AnalyticsOptions()));
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal($"# {CostDisclaimer.Text}", lines[0]);
        Assert.Equal(string.Join(',', UsageCsv.Columns), lines[1]);
        Assert.Equal("2026-10-28,ann,ann@example.com,kagent/seed,m,2,0,5,0,3,0,0,0,0,", lines[2]);
    }

    [Theory]
    [InlineData("=HYPERLINK(\"x\")", "\"'=HYPERLINK(\"\"x\"\")\"")]
    [InlineData("+1", "'+1")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("plain", "plain")]
    public void Cells_AreNeverFormulas(string value, string cell) => Assert.Equal(cell, UsageCsv.Cell(value));
}
