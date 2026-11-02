using System.Text.Json.Nodes;
using Skanyxx.Core.Services;

namespace Skanyxx.Host.Tests;

/// <summary>
/// D554: an agent kagent calls Ready is still unhealthy when a tool server it names gives no tools, read from kagent's
/// tool server list. Builders and up read which server and why; anyone else a fixed text without a name.
/// </summary>
public sealed class ToolServerHealthTests
{
    private static JsonArray Listed() =>
    [
        new JsonObject { ["ref"] = "kagent/docs-mcp", ["discoveredTools"] = new JsonArray(new JsonObject { ["name"] = "search_docs" }) },
        new JsonObject { ["ref"] = "kagent/skanyxx-memory-seed", ["discoveredTools"] = new JsonArray() },
        // A repeated ref: the first entry wins, as on the Tool servers page.
        new JsonObject { ["ref"] = "kagent/skanyxx-memory-seed", ["discoveredTools"] = new JsonArray(new JsonObject { ["name"] = "memory_search" }) },
        new JsonObject { ["groupKind"] = "RemoteMCPServer.kagent.dev" }
    ];

    [Fact]
    public void ServersWithTools_HaveNoProblem()
    {
        Assert.Empty(ToolServerHealth.Problems(["kagent/docs-mcp"], Listed()));
        Assert.Null(ToolServerHealth.Summary([], internals: true));
    }

    [Fact]
    public void AServerWithNoTools_OrNotListed_IsAProblem()
    {
        var problems = ToolServerHealth.Problems(["kagent/docs-mcp", "kagent/skanyxx-memory-seed", "team-a/gone"], Listed());

        Assert.Equal(
            [new ToolServerProblem("kagent/skanyxx-memory-seed", ToolServerHealth.NoTools), new ToolServerProblem("team-a/gone", ToolServerHealth.NotListed)],
            problems);
    }

    [Fact]
    public void BuildersAndUp_ReadWhichServer_AnyoneElseAFixedText()
    {
        var problems = ToolServerHealth.Problems(["kagent/skanyxx-memory-seed"], Listed());

        Assert.Equal($"kagent/skanyxx-memory-seed: {ToolServerHealth.NoTools}", ToolServerHealth.Summary(problems, internals: true));
        var employee = ToolServerHealth.Summary(problems, internals: false);
        Assert.Equal(ToolServerHealth.ForEmployees, employee);
        Assert.DoesNotContain("memory", employee);
    }
}
