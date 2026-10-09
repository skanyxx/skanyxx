using System.Text.Json.Nodes;
using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Studio.Definition;

/// <summary>
/// The two files of one agent (D1): <c>agent.yaml</c>, a kagent <c>v1alpha2</c> Agent exactly as the reconciler applies it
/// (labels aside), and <c>grants.yaml</c>, its Skanyxx memory grants (D033). Both are rendered from the studio form.
/// Reading them back is strict: the files must be exactly what the form they describe renders to — any other key,
/// kind, tool type, MCP server or field (a BYO image, a deployment, a service account, env) makes them invalid, so a
/// merge can only ever apply an Agent of our shape.
/// </summary>
internal static class AgentManifest
{
    private const string Header = "# Skanyxx studio. Applied to kagent by Skanyxx's reconciler when a supervisor merges (D045).\n";

    private const string GrantsHeader = "# Skanyxx memory grants (D033), applied by Skanyxx, not kagent. Team and department scopes need the owner's merge.\n";

    public static (string Agent, string Grants) Render(AgentDraft draft, string ns) =>
        (Header + YamlTree.Write(Agent(draft, ns)), GrantsHeader + YamlTree.Write(Grants(draft)));

    /// <summary>The kagent Agent for <paramref name="draft"/>, without labels (the reconciler adds them).</summary>
    public static JsonObject Agent(AgentDraft draft, string ns)
    {
        var declarative = new JsonObject
        {
            ["modelConfig"] = draft.ModelConfig,
            ["systemMessage"] = draft.Instructions
        };
        var tools = new JsonArray();
        if (MemoryTools(draft.Grants) is { Count: > 0 } memoryTools)
            tools.Add(McpTool(StudioNames.MemoryServer(draft.Name), memoryTools));
        foreach (var choice in draft.McpTools)
            tools.Add(McpTool(choice.Server, choice.Tools));
        if (tools.Count > 0)
            declarative["tools"] = tools;
        if (draft.MemoryTtlDays is { } ttl)
            declarative["memory"] = new JsonObject { ["modelConfig"] = draft.ModelConfig, ["ttlDays"] = ttl };

        var spec = new JsonObject
        {
            ["type"] = "Declarative",
            ["description"] = draft.Description,
            ["declarative"] = declarative
        };
        if (draft.Skills.Count > 0)
            spec["skills"] = new JsonObject { ["refs"] = new JsonArray([.. draft.Skills.Select(s => (JsonNode)s)]) };

        return new JsonObject
        {
            ["apiVersion"] = "kagent.dev/v1alpha2",
            ["kind"] = "Agent",
            ["metadata"] = new JsonObject { ["name"] = draft.Name, ["namespace"] = ns },
            ["spec"] = spec
        };
    }

    public static JsonObject Grants(AgentDraft draft) => new()
    {
        ["agent"] = draft.Name,
        ["grants"] = new JsonArray([.. draft.Grants.Select(g => (JsonNode)new JsonObject
        {
            ["scope"] = g.Scope, ["search"] = g.Search, ["upsert"] = g.Upsert
        })])
    };

    /// <summary><c>memory_search</c> with any search grant, <c>memory_upsert</c> with any upsert grant (D033).</summary>
    public static IReadOnlyList<string> MemoryTools(IReadOnlyList<StudioGrant> grants) =>
        [.. grants.Any(g => g.Search) ? [StudioNames.MemorySearch] : Array.Empty<string>(),
            .. grants.Any(g => g.Upsert) ? [StudioNames.MemoryUpsert] : Array.Empty<string>()];

    private static JsonObject McpTool(string server, IEnumerable<string> tools) => new()
    {
        ["type"] = "McpServer",
        ["mcpServer"] = new JsonObject
        {
            ["name"] = server,
            ["kind"] = "RemoteMCPServer",
            ["apiGroup"] = "kagent.dev",
            ["toolNames"] = new JsonArray([.. tools.Select(t => (JsonNode)t)])
        }
    };

    /// <summary>
    /// The form <paramref name="agentYaml"/> and <paramref name="grantsYaml"/> describe, or the reason they are not ours.
    /// Values are not judged here (<see cref="DraftRules"/> does that); the shape is.
    /// </summary>
    public static (AgentDraft? Draft, string? Problem) Read(string folderName, string agentYaml, string grantsYaml, string ns)
    {
        JsonNode? agent, grants;
        try
        {
            agent = YamlTree.Read(agentYaml);
            grants = YamlTree.Read(grantsYaml);
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            return (null, $"Not valid YAML: {ex.Message.Split('\n')[0]}");
        }
        if (agent is not JsonObject a || grants is not JsonObject g)
            return (null, "agent.yaml and grants.yaml must each be a YAML mapping.");

        var spec = At(a, "spec");
        var declarative = At(spec, "declarative");
        var memoryServer = StudioNames.MemoryServer(folderName);
        var tools = Items(At(declarative, "tools")).Select(t => At(t, "mcpServer")).ToList();
        int? ttl = null;
        if (Text(At(At(declarative, "memory"), "ttlDays")) is { } ttlText)
        {
            if (!int.TryParse(ttlText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var days))
                return (null, "spec.declarative.memory.ttlDays must be a whole number of days.");
            ttl = days;
        }
        var grantList = new List<StudioGrant>();
        foreach (var entry in Items(At(g, "grants")))
        {
            if (Bool(At(entry, "search")) is not { } search || Bool(At(entry, "upsert")) is not { } upsert)
                return (null, "Each grant needs search and upsert as true or false.");
            grantList.Add(new StudioGrant(Text(At(entry, "scope")) ?? "", search, upsert));
        }

        var draft = new AgentDraft(
            Text(At(At(a, "metadata"), "name")) ?? "",
            Text(At(spec, "description")) ?? "",
            Text(At(declarative, "modelConfig")) ?? "",
            Text(At(declarative, "systemMessage")) ?? "",
            [.. Items(At(At(spec, "skills"), "refs")).Select(s => Text(s) ?? "")],
            [.. tools.Where(t => Text(At(t, "name")) != memoryServer)
                .Select(t => new McpToolChoice(Text(At(t, "name")) ?? "", [.. Items(At(t, "toolNames")).Select(n => Text(n) ?? "")]))],
            grantList,
            ttl);

        if (draft.Name != folderName || Text(At(g, "agent")) != folderName)
            return (null, $"The files in {StudioNames.Folder(folderName)} must name the agent '{folderName}'.");
        // Exactly what the form renders to: nothing added, nothing missing, nothing of another kind.
        if (!JsonNode.DeepEquals(YamlTree.AsRead(Agent(draft, ns)), agent))
            return (null, "agent.yaml is not a studio agent: only a Declarative kagent Agent with a description, model, instructions, skills, "
                + "allow-listed MCP tools, the agent's own memory server and optional TTL memory, in this namespace, may be merged.");
        if (!JsonNode.DeepEquals(YamlTree.AsRead(Grants(draft)), grants))
            return (null, "grants.yaml must hold only the agent's name and its grants (scope, search, upsert).");
        return (draft, null);
    }

    /// <summary>A key of a mapping; null for anything that is not one (the exact comparison then refuses it).</summary>
    private static JsonNode? At(JsonNode? node, string key) => node is JsonObject o ? o[key] : null;

    private static IEnumerable<JsonNode?> Items(JsonNode? node) => node as JsonArray ?? [];

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static bool? Bool(JsonNode? node) => Text(node) switch { "true" => true, "false" => false, _ => null };
}
