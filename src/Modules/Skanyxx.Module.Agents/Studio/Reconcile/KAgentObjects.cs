using System.Text.Json.Nodes;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Studio.Definition;

namespace Skanyxx.Module.Agents.Studio.Reconcile;

/// <summary>The kagent objects the reconciler sends (research/kagent-agent-api.md).</summary>
internal static class KAgentObjects
{
    public static JsonObject WithLabels(JsonObject agent, params (string Key, string Value)[] labels)
    {
        var labelled = (JsonObject)agent.DeepClone();
        labelled["metadata"]!["labels"] = new JsonObject(labels.Select(l => KeyValuePair.Create(l.Key, (JsonNode?)l.Value)));
        return labelled;
    }

    /// <summary>
    /// The agent's own memory server (D083: one RemoteMCPServer per agent): kagent sends the Authorization header from
    /// <paramref name="secretName"/>, a Secret kagent creates with the server and deletes with it.
    /// </summary>
    public static JsonObject MemoryServer(string agentId, string ns, string url, string secretName) => new()
    {
        ["apiVersion"] = "kagent.dev/v1alpha2",
        ["kind"] = "RemoteMCPServer",
        ["metadata"] = new JsonObject
        {
            ["name"] = StudioNames.MemoryServer(agentId),
            ["namespace"] = ns,
            ["labels"] = new JsonObject { [StudioNames.ManagedByLabel] = StudioNames.ManagedBy }
        },
        ["spec"] = new JsonObject
        {
            ["description"] = $"Skanyxx company memory, as the agent {agentId}",
            ["protocol"] = "STREAMABLE_HTTP",
            ["url"] = url,
            ["headersFrom"] = new JsonArray(new JsonObject
            {
                ["name"] = "Authorization",
                ["valueFrom"] = new JsonObject { ["type"] = "Secret", ["name"] = secretName, ["key"] = "token" }
            })
        }
    };

    /// <summary>
    /// A proposal as its preview runs it (D030, D032): its own name, never merged, memory search on <c>company</c> only
    /// (a team or department scope is the owner's to open, and a preview is a builder's), never upsert, no kagent TTL
    /// memory, and no skills (D122: a skill runs code nobody has reviewed yet). Allow-listed MCP tools stay (D113).
    /// </summary>
    public static AgentDraft Preview(AgentDraft draft, int number) => draft with
    {
        Name = StudioNames.PreviewAgent(number, draft.Name),
        Skills = [],
        Grants = draft.Grants.Any(g => g.Scope == "company" && g.Search) ? [new StudioGrant("company", true, false)] : [],
        MemoryTtlDays = null
    };

    /// <summary>
    /// Whether kagent's spec already is <paramref name="desired"/>'s, comparing only the fields the studio sets (kagent
    /// may fill in defaults elsewhere), so an unchanged agent is never written again.
    /// </summary>
    public static bool SpecMatches(JsonObject existing, JsonObject desired)
    {
        if (existing["spec"] is not JsonObject spec || desired["spec"] is not JsonObject wanted)
            return false;
        var declarative = spec["declarative"] as JsonObject;
        var projected = new JsonObject();
        Copy(spec, projected, "type", "description", "skills");
        if (declarative is not null)
        {
            var d = new JsonObject();
            Copy(declarative, d, "modelConfig", "systemMessage", "tools", "memory");
            projected["declarative"] = d;
        }
        return JsonNode.DeepEquals(Strip(projected), Strip(wanted));
    }

    public static string? Label(JsonObject agent, string key) =>
        agent["metadata"]?["labels"]?[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static void Copy(JsonObject from, JsonObject to, params string[] keys)
    {
        foreach (var key in keys)
            if (from[key] is { } value)
                to[key] = value.DeepClone();
    }

    // Empty strings, arrays and objects are what omitempty leaves out on kagent's side.
    private static JsonNode? Strip(JsonNode? node) => node switch
    {
        JsonObject o => new JsonObject(o.Select(p => KeyValuePair.Create(p.Key, Strip(p.Value)))
            .Where(p => p.Value is not null && !(p.Value is JsonObject { Count: 0 } or JsonArray { Count: 0 })
                && !(p.Value is JsonValue v && v.TryGetValue<string>(out var s) && s.Length == 0))),
        JsonArray a => new JsonArray([.. a.Select(Strip)]),
        _ => node?.DeepClone()
    };
}
