using System.Text.Json.Nodes;

namespace Skanyxx.Module.Chat.Features.Agents;

/// <summary>Reads kagent's agent list entries (<c>{agent: {metadata, spec}, model, modelProvider, modelConfigRef}</c>).</summary>
internal static class AgentEntries
{
    public static JsonObject? Find(JsonArray entries, string ns, string name) =>
        entries.OfType<JsonObject>().FirstOrDefault(e =>
            Text(e["agent"]?["metadata"]?["namespace"]) == ns && Text(e["agent"]?["metadata"]?["name"]) == name);

    public static string? Model(JsonObject? entry) => Text(entry?["model"]) ?? Text(entry?["modelConfigRef"]);

    public static string? Text(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0 ? s : null;

    public static IReadOnlyList<string> Strings(JsonNode? node) => [.. (node as JsonArray ?? []).Select(Text).OfType<string>()];
}
