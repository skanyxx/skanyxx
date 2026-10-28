using Skanyxx.Core.Platform.Analytics;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Skanyxx.Module.Analytics.Health;

/// <summary>The Dashboard's health tiles from what the probe found: a fixed status and a short reason each, and how to fix it.</summary>
public static class HealthBoard
{
    public const long SlowKAgentMs = 1_000;

    private static readonly string[] KAgentSteps =
    [
        "Check that KAgent:BaseUrl and KAgent:Port point at the kagent controller (in the cluster: kagent-controller.kagent:8083).",
        "From where Skanyxx runs, open http://<controller>:8083/health; it must answer 200.",
        "Restart Skanyxx after changing the settings, then reload the Dashboard."
    ];

    public static IReadOnlyList<HealthTile> Tiles(HealthFacts facts) =>
    [
        KAgent(facts), Models(facts), ToolServers(facts), Agents(facts), Postgres(facts),
        AnswerCache(facts), Git(facts), Sandboxes(facts), Jobs(facts)
    ];

    private static HealthTile KAgent(HealthFacts f) => f.KAgentLatencyMs switch
    {
        null => new("kagent", "kagent controller", TileStatus.Down, "Not reachable", "Skanyxx could not reach the kagent controller.", "/Debug", KAgentSteps),
        > SlowKAgentMs and var ms => new("kagent", "kagent controller", TileStatus.Warn, $"Slow: {ms} ms", "The controller answers, but slowly.", "/Debug",
            ["Check the kagent controller pod's CPU and memory, and the network between Skanyxx and the controller."]),
        var ms => new("kagent", "kagent controller", TileStatus.Ok, $"Reachable in {ms} ms", null, null, [])
    };

    private static HealthTile Models(HealthFacts f) => (f.ModelConfigs, f.ModelConfigured) switch
    {
        (null, _) => Unreachable("models", "Model providers", "/Model"),
        (0, _) or (_, false or null) => new("models", "Model providers", TileStatus.Down, $"{f.ModelConfigs} ModelConfig(s); default not ready",
            "The default model is missing, not accepted by kagent, or has no API key.", "/Model",
            ["Open Model, choose the provider and model, and paste the API key.", "Save, then wait for kagent to accept it (the page shows the status)."]),
        var (count, _) => new("models", "Model providers", TileStatus.Ok, $"{count} ModelConfig(s); default ready", null, null, [])
    };

    private static HealthTile ToolServers(HealthFacts f) => f.ToolServers switch
    {
        null => Unreachable("toolservers", "Tool servers", "/ToolServers"),
        (_, 0) => new("toolservers", "Tool servers", TileStatus.Off, "None registered", "No MCP tool server is registered yet.", "/ToolServers",
            ["Open Tool Servers and add an MCP server your agents should use."]),
        var (up, total) when up < total => new("toolservers", "Tool servers", TileStatus.Warn, $"{up} / {total} up",
            $"{total - up} tool server(s) have no tools discovered: kagent cannot reach them.", "/ToolServers",
            ["Open Tool Servers and check the servers without tools: their URL, and that they are running."]),
        var (up, total) => new("toolservers", "Tool servers", TileStatus.Ok, $"{up} / {total} up", null, null, [])
    };

    private static HealthTile Agents(HealthFacts f) => f.Agents switch
    {
        null => Unreachable("agents", "Agents", "/Agents"),
        (_, 0) => new("agents", "Agents", TileStatus.Warn, "No agents", "kagent runs no agent: nobody can chat.", "/Agents",
            ["Apply the seed agent (deploy/kagent/memory/) or create an agent on the Agents page."]),
        var (ready, total) when ready < total => new("agents", "Agents", TileStatus.Warn, $"{ready} / {total} ready",
            $"{total - ready} agent(s) are not ready.", "/Agents",
            ["Open Agents and check the agents that are not ready: their model, tools and pod status in kagent."]),
        var (ready, total) => new("agents", "Agents", TileStatus.Ok, $"{ready} / {total} ready", null, null, [])
    };

    private static HealthTile Postgres(HealthFacts f)
    {
        var stores = f.Checks.Where(c => c.Key.EndsWith(DashboardChecks.PostgresSuffix, StringComparison.Ordinal)).ToList();
        var down = stores.Count(s => s.Value.Status != HealthStatus.Healthy);
        return (stores.Count, down) switch
        {
            (0, _) => new("postgres", "Postgres", TileStatus.Down, "No database check", "No module registered its database.", "/Debug",
                ["Set ConnectionStrings:Identity, :Memory and :Tickets and restart Skanyxx."]),
            (var total, 0) => new("postgres", "Postgres", TileStatus.Ok, $"{total} / {total} stores answer", null, null, []),
            (var total, var failing) => new("postgres", "Postgres", TileStatus.Down, $"{total - failing} / {total} stores answer",
                $"{failing} database(s) do not answer.", "/Debug",
                ["Open Debug to see which store fails.", "Check that Postgres is running and the connection strings point at it."])
        };
    }

    private static HealthTile AnswerCache(HealthFacts f) => Optional(f, DashboardChecks.AnswerCache, "dragonfly", "Dragonfly (answer cache)", "/Debug",
        "Off on this install",
        ["Run Dragonfly (or Redis) where Skanyxx can reach it.",
         "Set ChatContext:AnswerCache:Enabled=true and ChatContext:AnswerCache:Redis=<host>:6379, then restart Skanyxx."],
        "Answering", "Dragonfly does not answer: repeated questions go to the model.",
        ["Check that Dragonfly is running and that ChatContext:AnswerCache:Redis points at it."]);

    private static HealthTile Git(HealthFacts f) => Optional(f, DashboardChecks.StudioGit, "git", "Studio git (Gitea)", "/Studio",
        "Not configured",
        ["Run the bundled Gitea (or give Skanyxx a git server).",
         "Set Studio:Git:BaseUrl and Studio:Git:Token (a token of the skanyxx-bot account), then restart Skanyxx."],
        "Answering", "The studio's git server does not answer: proposals and merges fail.",
        ["Check that Gitea is running at Studio:Git:BaseUrl and that Studio:Git:Token is still valid."]);

    private static HealthTile Sandboxes(HealthFacts f) => Optional(f, DashboardChecks.Sandboxes, "sandboxes", "AX sandboxes", "/Sandboxes",
        "Off on this install",
        ["Deploy ax-server and the sandbox network policies (deploy/sandboxes/).",
         "Set Sandboxes:Enabled=true and Sandboxes:Address=http://<ax-server>:<port>, then restart Skanyxx."],
        "Answering", "ax-server does not answer: sandboxes cannot start or be listed.",
        ["Check that ax-server is running at Sandboxes:Address."]);

    private static HealthTile Jobs(HealthFacts f)
    {
        if (!f.Checks.TryGetValue(DashboardChecks.StudioReconciler, out var check))
            return new("jobs", "Background jobs", TileStatus.Off, "No reconciler (studio off)", "The studio reconciler runs only with studio git configured.", "/Studio",
                ["Configure Studio git (see the Studio git tile); the reconciler starts with it."]);
        var last = check.Data.TryGetValue(DashboardChecks.LastPassKey, out var at) ? $"last pass {at}" : "no pass yet";
        var failures = check.Data.TryGetValue(DashboardChecks.FailuresKey, out var n) ? n : 0;
        return check.Status switch
        {
            HealthStatus.Healthy => new("jobs", "Background jobs", TileStatus.Ok, $"Reconciler: {last}", null, null, []),
            HealthStatus.Degraded => new("jobs", "Background jobs", TileStatus.Warn, $"Reconciler: {last}",
                $"The last pass left {failures} agent(s) not applied.", "/Studio",
                ["Open Studio and check the agents that did not apply; the next pass retries them."]),
            _ => new("jobs", "Background jobs", TileStatus.Down, $"Reconciler: {last}", "The reconciler's last pass failed or it has not run recently.", "/Debug",
                ["Check that kagent and git answer (their tiles).", "Look for 'Studio reconcile' in the Skanyxx log for the reason."])
        };
    }

    private static HealthTile Optional(HealthFacts f, string check, string id, string title, string fixUrl, string offSummary,
        IReadOnlyList<string> enableSteps, string okSummary, string downReason, IReadOnlyList<string> downSteps) =>
        !f.Checks.TryGetValue(check, out var fact)
            ? new(id, title, TileStatus.Off, offSummary, null, fixUrl, enableSteps)
            : fact.Status == HealthStatus.Healthy
                ? new(id, title, TileStatus.Ok, okSummary, null, null, [])
                : new(id, title, TileStatus.Down, "Not answering", downReason, fixUrl, downSteps);

    private static HealthTile Unreachable(string id, string title, string fixUrl) =>
        new(id, title, TileStatus.Down, "Unknown", "kagent is not reachable, so this cannot be read.", fixUrl, KAgentSteps);
}
