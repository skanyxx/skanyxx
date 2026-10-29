using System.Text.Json.Nodes;
using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Studio.Git;

/// <summary>
/// A commit's combined status as git answers it (D362): Gitea says each run's <c>status</c>, GitHub its <c>state</c>, both
/// with <c>context</c>, <c>description</c> and <c>target_url</c>. Any failure fails the change, otherwise any pending run
/// keeps it pending; Gitea's <c>warning</c> does not fail it. Only the latest run per context counts (git lists newest first).
/// </summary>
internal static class CheckStatusMap
{
    private const int MaxRuns = 20;

    public static ChangeChecks From(JsonNode? combined)
    {
        if (combined?["statuses"] is not JsonArray statuses || statuses.Count == 0)
            return ChangeChecks.None;
        var runs = statuses
            .Select(s => new CheckRun(
                Text(s?["context"]) is { Length: > 0 } name ? name : "check",
                State(Text(s?["status"]) ?? Text(s?["state"])),
                Text(s?["description"]) ?? "",
                Url(Text(s?["target_url"]))))
            .DistinctBy(r => r.Name)
            .Take(MaxRuns)
            .ToList();
        return new ChangeChecks(Combine(runs.Select(r => r.State)), runs);
    }

    public static CheckState State(string? status) => status?.ToLowerInvariant() switch
    {
        "success" or "warning" => CheckState.Passed,
        "failure" or "error" => CheckState.Failed,
        "pending" => CheckState.Pending,
        _ => CheckState.Pending
    };

    public static CheckState Combine(IEnumerable<CheckState> states)
    {
        var list = states.ToList();
        if (list.Count == 0)
            return CheckState.None;
        if (list.Contains(CheckState.Failed))
            return CheckState.Failed;
        return list.Contains(CheckState.Pending) ? CheckState.Pending : CheckState.Passed;
    }

    /// <summary>Only an http(s) link is offered to people: a check's URL is whatever CI wrote.</summary>
    private static string? Url(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri.ToString() : null;

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
