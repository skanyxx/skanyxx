using System.Text.Json.Nodes;
using Skanyxx.Core.Models;
using Skanyxx.Core.Platform.Memory;

namespace Skanyxx.Module.Chat.Features.Agents;

/// <summary>Builds <see cref="ChatAgentDetails"/> from kagent's list entry (spec, model) and its tool servers (tool descriptions).</summary>
internal static class AgentDetailsReader
{
    public static ChatAgentDetails Read(Agent agent, JsonObject? entry, JsonArray toolServers, IReadOnlyList<AgentMemoryGrant>? memory)
    {
        var spec = entry?["agent"]?["spec"];
        var declarative = spec?["declarative"];
        var tools = declarative?["tools"] as JsonArray ?? [];
        return new ChatAgentDetails(
            agent.Namespace, agent.Name, agent.Description, agent.Ready,
            AgentEntries.Text(entry?["model"]), AgentEntries.Text(entry?["modelProvider"]),
            AgentEntries.Text(entry?["modelConfigRef"]) ?? AgentEntries.Text(declarative?["modelConfig"]),
            MergedAgents.AcceptsImages(agent),
            [.. tools.Select(t => t?["mcpServer"]).OfType<JsonObject>().Select(s => Server(s, agent.Namespace, toolServers))],
            [.. tools.Select(t => t?["agent"]).OfType<JsonObject>()
                .Select(a => AgentEntries.Text(a["name"]) is { } name ? $"{AgentEntries.Text(a["namespace"]) ?? agent.Namespace}/{name}" : null)
                .OfType<string>()],
            AgentEntries.Strings(spec?["skills"]?["refs"]),
            [.. (declarative?["a2aConfig"]?["skills"] as JsonArray ?? []).OfType<JsonObject>()
                .Select(s => new ChatAgentSkill(AgentEntries.Text(s["id"]) ?? "", AgentEntries.Text(s["name"]) ?? "", AgentEntries.Text(s["description"]) ?? ""))],
            memory,
            $"/Agents?agent={Uri.EscapeDataString($"{agent.Namespace}/{agent.Name}")}");
    }

    // A spec's toolNames pick tools; none means every tool the server lists. Descriptions are what kagent discovered.
    private static ChatToolServer Server(JsonObject server, string agentNamespace, JsonArray toolServers)
    {
        var reference = $"{AgentEntries.Text(server["namespace"]) ?? agentNamespace}/{AgentEntries.Text(server["name"])}";
        var discovered = (toolServers.OfType<JsonObject>().FirstOrDefault(s => AgentEntries.Text(s["ref"]) == reference)?["discoveredTools"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Select(t => new ChatTool(AgentEntries.Text(t["name"]) ?? "", AgentEntries.Text(t["description"]) ?? ""))
            .Where(t => t.Name.Length > 0)
            .ToList();
        var named = AgentEntries.Strings(server["toolNames"]);
        return named.Count == 0
            ? new ChatToolServer(reference, true, discovered)
            : new ChatToolServer(reference, false, [.. named.Select(n => discovered.FirstOrDefault(t => t.Name == n) ?? new ChatTool(n, ""))]);
    }
}
