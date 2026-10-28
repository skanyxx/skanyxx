namespace Skanyxx.Module.Analytics.Health;

/// <summary>
/// One dependency on the Dashboard. <paramref name="Summary"/> and <paramref name="Reason"/> are fixed texts written here,
/// never an exception message, a response body or a connection string. <paramref name="FixUrl"/> is the page that fixes
/// it; <paramref name="FixSteps"/> say how, in plain words (also what "Copy instructions for agent" copies).
/// </summary>
public sealed record HealthTile(
    string Id, string Title, string Status, string Summary, string? Reason, string? FixUrl, IReadOnlyList<string> FixSteps);
