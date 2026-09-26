namespace Skanyxx.Module.Tickets.Tests.Infrastructure;

/// <summary>Builds pipeline stage JSON the way a client would send it (snake_case enum values).</summary>
public static class Stages
{
    public static Dictionary<string, object?> Stage(string id, string kind, string agent, params (string Key, object? Value)[] extra)
    {
        var stage = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["kind"] = kind,
            ["skills"] = new[] { "prior_stage_digest" },
            ["agents"] = new[] { new { id = agent, agent = $"kagent/{agent}" } }
        };
        foreach (var (key, value) in extra)
            stage[key] = value;
        return stage;
    }

    public static object Pipeline(params object[] stages) => new { name = "Test pipeline", description = "", stages };
}
