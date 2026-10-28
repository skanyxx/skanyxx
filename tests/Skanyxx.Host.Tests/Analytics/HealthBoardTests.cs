using Microsoft.Extensions.Diagnostics.HealthChecks;
using Skanyxx.Core.Platform.Analytics;
using Skanyxx.Module.Analytics.Health;

namespace Skanyxx.Host.Tests.Analytics;

/// <summary>Each dependency up and down gives its tile the right colour, a fixed reason, and the page that fixes it.</summary>
public sealed class HealthBoardTests
{
    private static readonly IReadOnlyDictionary<string, object> NoData = new Dictionary<string, object>();

    private static CheckFact Fact(HealthStatus status, IReadOnlyDictionary<string, object>? data = null) => new(status, data ?? NoData);

    private static readonly Dictionary<string, CheckFact> AllUp = new()
    {
        ["identity-postgres"] = Fact(HealthStatus.Healthy),
        ["memory-postgres"] = Fact(HealthStatus.Healthy),
        [DashboardChecks.AnswerCache] = Fact(HealthStatus.Healthy),
        [DashboardChecks.StudioGit] = Fact(HealthStatus.Healthy),
        [DashboardChecks.Sandboxes] = Fact(HealthStatus.Healthy),
        [DashboardChecks.StudioReconciler] = Fact(HealthStatus.Healthy,
            new Dictionary<string, object> { [DashboardChecks.LastPassKey] = "2026-10-28 10:00 UTC", [DashboardChecks.FailuresKey] = 0 })
    };

    private static readonly HealthFacts Up = new(42, (3, 3), (2, 2), 1, true, AllUp);

    private static HealthTile Tile(HealthFacts facts, string id) => HealthBoard.Tiles(facts).Single(t => t.Id == id);

    [Fact]
    public void EverythingUp_IsGreen_WithNothingToFix()
    {
        var tiles = HealthBoard.Tiles(Up);

        Assert.Equal(["kagent", "models", "toolservers", "agents", "postgres", "dragonfly", "git", "sandboxes", "jobs"], tiles.Select(t => t.Id));
        Assert.All(tiles, t => Assert.Equal(TileStatus.Ok, t.Status));
        Assert.All(tiles, t => Assert.Empty(t.FixSteps));
        Assert.Equal("Reachable in 42 ms", Tile(Up, "kagent").Summary);
        Assert.Equal("Reconciler: last pass 2026-10-28 10:00 UTC", Tile(Up, "jobs").Summary);
    }

    [Fact]
    public void KAgentDown_TurnsItAndWhatItKnowsRed()
    {
        var down = new HealthFacts(null, null, null, null, null, AllUp);

        foreach (var id in new[] { "kagent", "models", "toolservers", "agents" })
        {
            var tile = Tile(down, id);
            Assert.Equal(TileStatus.Down, tile.Status);
            Assert.NotEmpty(tile.FixSteps);
            Assert.NotNull(tile.FixUrl);
        }
        Assert.Equal(TileStatus.Ok, Tile(down, "postgres").Status);
        Assert.Equal(TileStatus.Warn, Tile(Up with { KAgentLatencyMs = HealthBoard.SlowKAgentMs + 1 }, "kagent").Status);
    }

    [Fact]
    public void Models_RedWithoutAReadyDefault()
    {
        Assert.Equal(TileStatus.Down, Tile(Up with { ModelConfigured = false }, "models").Status);
        Assert.Equal(TileStatus.Down, Tile(Up with { ModelConfigs = 0 }, "models").Status);
        Assert.Equal("/Model", Tile(Up with { ModelConfigured = false }, "models").FixUrl);
    }

    [Fact]
    public void ToolServersAndAgents_AmberWhenSomeAreNotUp()
    {
        Assert.Equal(TileStatus.Warn, Tile(Up with { ToolServers = (1, 2) }, "toolservers").Status);
        Assert.Equal(TileStatus.Off, Tile(Up with { ToolServers = (0, 0) }, "toolservers").Status);
        Assert.Equal("1 / 2 up", Tile(Up with { ToolServers = (1, 2) }, "toolservers").Summary);
        Assert.Equal(TileStatus.Warn, Tile(Up with { Agents = (2, 3) }, "agents").Status);
        Assert.Equal(TileStatus.Warn, Tile(Up with { Agents = (0, 0) }, "agents").Status);
    }

    [Fact]
    public void APostgresStoreDown_IsRed()
    {
        var checks = new Dictionary<string, CheckFact>(AllUp) { ["memory-postgres"] = Fact(HealthStatus.Unhealthy) };

        var tile = Tile(Up with { Checks = checks }, "postgres");

        Assert.Equal(TileStatus.Down, tile.Status);
        Assert.Equal("1 / 2 stores answer", tile.Summary);
        Assert.Equal("/Debug", tile.FixUrl);
    }

    [Theory]
    [InlineData(DashboardChecks.AnswerCache, "dragonfly", "/Debug")]
    [InlineData(DashboardChecks.StudioGit, "git", "/Studio")]
    [InlineData(DashboardChecks.Sandboxes, "sandboxes", "/Sandboxes")]
    public void OptionalDependencies_AreOffWhenNotConfigured_RedWhenDown(string check, string id, string fixUrl)
    {
        var without = new Dictionary<string, CheckFact>(AllUp);
        without.Remove(check);
        var off = Tile(Up with { Checks = without }, id);
        Assert.Equal(TileStatus.Off, off.Status);
        Assert.NotEmpty(off.FixSteps); // how to turn it on

        var failing = new Dictionary<string, CheckFact>(AllUp) { [check] = Fact(HealthStatus.Unhealthy) };
        var down = Tile(Up with { Checks = failing }, id);
        Assert.Equal(TileStatus.Down, down.Status);
        Assert.Equal(fixUrl, down.FixUrl);
    }

    [Fact]
    public void TheReconciler_AmberWithUnappliedAgents_RedWhenItFailed_OffWithoutTheStudio()
    {
        var data = new Dictionary<string, object> { [DashboardChecks.LastPassKey] = "2026-10-28 10:00 UTC", [DashboardChecks.FailuresKey] = 2 };
        Assert.Equal(TileStatus.Warn, Tile(Up with { Checks = new Dictionary<string, CheckFact>(AllUp) { [DashboardChecks.StudioReconciler] = Fact(HealthStatus.Degraded, data) } }, "jobs").Status);
        Assert.Equal(TileStatus.Down, Tile(Up with { Checks = new Dictionary<string, CheckFact>(AllUp) { [DashboardChecks.StudioReconciler] = Fact(HealthStatus.Unhealthy, data) } }, "jobs").Status);
        var without = new Dictionary<string, CheckFact>(AllUp);
        without.Remove(DashboardChecks.StudioReconciler);
        Assert.Equal(TileStatus.Off, Tile(Up with { Checks = without }, "jobs").Status);
    }
}
