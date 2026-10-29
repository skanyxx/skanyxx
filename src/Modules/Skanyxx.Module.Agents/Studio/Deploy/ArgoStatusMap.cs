using System.Text.Json.Nodes;
using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Studio.Deploy;

/// <summary>
/// One agent's deploy status from an Argo CD Application (D363): its <c>Agent</c> resource in <c>status.resources</c>
/// (sync and health). Not the Application's last sync operation: Argo never syncs studio agents (D546), so a failed
/// operation is only a refused manual Sync, which says nothing about any agent (D547). Argo's own status words are
/// shown, never its messages.
/// </summary>
internal static class ArgoStatusMap
{
    public const string Source = "argo";

    public static DeployStatus For(JsonNode? application, string agent, string ns)
    {
        var status = application?["status"];
        if (status is null)
            return new DeployStatus(DeployState.Unknown, Source, "Argo CD answered without a status.");
        var resource = (status["resources"] as JsonArray ?? [])
            .FirstOrDefault(r => Text(r?["kind"]) == "Agent" && Text(r?["name"]) == agent && (Text(r?["namespace"]) ?? ns) == ns);
        if (resource is null)
            return new DeployStatus(DeployState.Waiting, Source, "Argo CD does not see this agent in main yet.");
        var sync = Text(resource["status"]) ?? "Unknown";
        var health = Text(resource["health"]?["status"]) ?? "Unknown";
        return (sync, health) switch
        {
            (_, "Degraded") => new DeployStatus(DeployState.Failed, Source, $"Argo CD: {sync}, Degraded."),
            (_, "Missing") => new DeployStatus(DeployState.Waiting, Source, "Argo CD: not in the cluster yet."),
            ("Synced", "Healthy") => new DeployStatus(DeployState.Healthy, Source, "Argo CD: Synced, Healthy."),
            ("Synced", "Suspended") => new DeployStatus(DeployState.Suspended, Source, "Argo CD: Synced, Suspended."),
            _ => new DeployStatus(DeployState.Syncing, Source, $"Argo CD: {sync}, {health}.")
        };
    }

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
