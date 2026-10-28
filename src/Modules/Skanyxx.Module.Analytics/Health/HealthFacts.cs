namespace Skanyxx.Module.Analytics.Health;

/// <summary>
/// What the probe found. Null = could not be read (kagent down). <see cref="Checks"/> holds the health checks other
/// modules register (by name); a missing name is a dependency this install does not configure.
/// </summary>
public sealed record HealthFacts(
    long? KAgentLatencyMs,
    (int Ready, int Total)? Agents,
    (int Up, int Total)? ToolServers,
    int? ModelConfigs,
    bool? ModelConfigured,
    IReadOnlyDictionary<string, CheckFact> Checks);
