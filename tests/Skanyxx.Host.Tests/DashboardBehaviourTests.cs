using System.Text.RegularExpressions;

namespace Skanyxx.Host.Tests;

/// <summary>
/// The Dashboard's states in a real browser (D558): the status says "all healthy" or names what is down and degraded,
/// problems come first and parts that are off come last, every state is an icon and words, a tile shows only its fixed
/// texts, a failed read offers "Try again" and recovers, empty data says so, trends compare the last 7 days with the 7
/// before, and a supervisor sees no per-person list. Ui/dashboard-behaviour.html runs the real scripts with the API and
/// Chart.js stubbed (<see cref="HeadlessChrome"/>); skipped when this machine has no Chrome or Chromium.
/// </summary>
public sealed partial class DashboardBehaviourTests
{
    private static readonly string[] Checks =
    [
        "healthySaysSo", "offTilesComeLast", "chartsHaveTheirNumbers", "chatsTrend", "failedTurnsTrendIsBad", "checkedAtShown",
        "allHealthy", "emptyChartsSaySo", "problemsComeFirst", "statusNamesWhatIsWrong", "statesAreIconAndWords",
        "downTileHasItsFix", "onlyFixedTexts", "healthErrorRetries", "usageErrorRetries", "supervisorSeesNoPeople"
    ];

    private static string Script(string name) => new Uri(Repo.Path("src", "Skanyxx.Host", "wwwroot", "js", name)).AbsoluteUri;

    [Fact]
    public async Task TheDashboard_PutsWhatIsWrongFirst_AndEveryStateIsReadable()
    {
        var chrome = HeadlessChrome.Find();
        if (chrome is null)
            Assert.Skip("No Chrome or Chromium on this machine (set CHROME_PATH to run the Dashboard behaviour checks).");

        var html = (await File.ReadAllTextAsync(Repo.Path("tests", "Skanyxx.Host.Tests", "Ui", "dashboard-behaviour.html"), TestContext.Current.CancellationToken))
            .Replace("{{UI_JS}}", Script("ui.js"), StringComparison.Ordinal)
            .Replace("{{SETUP_GUIDE_JS}}", Script("setup-guide.js"), StringComparison.Ordinal)
            .Replace("{{INSIGHTS_JS}}", Script("insights.js"), StringComparison.Ordinal)
            .Replace("{{DASHBOARD_JS}}", Script("dashboard.js"), StringComparison.Ordinal);

        var result = await HeadlessChrome.RunAsync(chrome!, html, "dashboardBehaviour");

        Assert.False(result.TryGetProperty("error", out var error), error.ToString());
        Assert.All(Checks, check => Assert.True(result.GetProperty(check).GetBoolean(), check));
    }

    /// <summary>The behaviour page stands in for the real one, so the real one must have every id the script reads.</summary>
    [Fact]
    public void ThePage_HasEveryElementTheScriptReads()
    {
        var script = File.ReadAllText(Repo.Path("src", "Skanyxx.Host", "wwwroot", "js", "dashboard.js"));
        var page = File.ReadAllText(Repo.Path("src", "Skanyxx.Host", "Pages", "Dashboard.cshtml"));
        var ids = LookedUp().Matches(script).Select(m => m.Groups[1].Value)
            .Concat(new[] { "tokens", "chats", "errors", "cards" }.SelectMany(id => new[] { $"chart-{id}", $"sum-{id}" }))
            .Distinct()
            .ToList();

        Assert.True(ids.Count >= 15, $"expected the script's lookups, found {ids.Count}");
        Assert.All(ids, id => Assert.Contains($"id=\"{id}\"", page));
    }

    [GeneratedRegex(@"\$\('([a-z-]+)'\)")]
    private static partial Regex LookedUp();
}
