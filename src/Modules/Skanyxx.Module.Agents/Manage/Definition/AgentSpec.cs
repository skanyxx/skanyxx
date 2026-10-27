using System.Text.Json.Nodes;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Studio.Definition;

namespace Skanyxx.Module.Agents.Manage.Definition;

/// <summary>
/// The form to and from kagent's Agent CR (kagent.dev/v1alpha2). kagent's update replaces the whole spec, so an edit
/// starts from the agent's current spec and changes only the form's fields: anything else (deployment, memory, context,
/// header settings on a kept MCP server) stays as it was.
/// </summary>
internal static class AgentSpec
{
    /// <summary>A new agent: Declarative, labelled as the Agents page's and listed in Chat (D253).</summary>
    public static JsonObject NewAgent(AgentForm form) => new()
    {
        ["apiVersion"] = "kagent.dev/v1alpha2",
        ["kind"] = "Agent",
        ["metadata"] = new JsonObject
        {
            ["name"] = form.Name,
            ["namespace"] = form.Namespace,
            ["labels"] = new JsonObject
            {
                [StudioNames.ManagedByLabel] = AgentProtection.ManagedBy,
                [StudioNames.MergedLabel] = "true"
            }
        },
        ["spec"] = Apply(new JsonObject(), form)
    };

    /// <summary>The CR kagent's PUT takes: its name and namespace and the changed spec (kagent ignores the rest of metadata).</summary>
    public static JsonObject Updated(JsonObject agent, AgentForm form) => new()
    {
        ["apiVersion"] = "kagent.dev/v1alpha2",
        ["kind"] = "Agent",
        ["metadata"] = new JsonObject { ["name"] = form.Name, ["namespace"] = form.Namespace },
        ["spec"] = Apply(agent["spec"] as JsonObject ?? new JsonObject(), form)
    };

    public static bool IsDeclarative(JsonObject agent) => Text(agent["spec"]?["type"]) is null or "Declarative";

    /// <summary>A prompt kagent reads from a ConfigMap or Secret (<c>systemMessageFrom</c>) is not the form's to overwrite.</summary>
    public static bool HasPromptSource(JsonObject agent) => agent["spec"]?["declarative"]?["systemMessageFrom"] is not null;

    public static ExistingSpec Existing(JsonObject agent)
    {
        var declarative = agent["spec"]?["declarative"];
        var mcp = new Dictionary<string, HashSet<string>>();
        foreach (var tool in declarative?["tools"] as JsonArray ?? [])
            if (Text(tool?["mcpServer"]?["name"]) is { } server)
                mcp[server] = [.. Strings(tool!["mcpServer"]!["toolNames"])];
        return new ExistingSpec(mcp, Strings(agent["spec"]?["skills"]?["refs"]).ToHashSet());
    }

    private static JsonObject Apply(JsonObject current, AgentForm form)
    {
        var spec = (JsonObject)current.DeepClone();
        spec["type"] = "Declarative";
        Set(spec, "description", form.Description.Length > 0 ? form.Description : null);
        var declarative = spec["declarative"] as JsonObject ?? new JsonObject();
        spec["declarative"] = declarative;
        declarative["systemMessage"] = form.SystemMessage;
        declarative["modelConfig"] = form.ModelConfig;
        Set(declarative, "runtime", string.IsNullOrEmpty(form.Runtime) ? null : form.Runtime);
        Set(declarative, "stream", form.Stream ? true : null);
        Set(declarative, "tools", Tools(declarative["tools"] as JsonArray ?? [], form));
        Set(declarative, "a2aConfig", form.A2aSkills.Count == 0 ? null : new JsonObject { ["skills"] = new JsonArray([.. form.A2aSkills.Select(A2aSkill)]) });

        var skills = spec["skills"] as JsonObject ?? new JsonObject();
        Set(skills, "refs", form.Skills.Count == 0 ? null : new JsonArray([.. form.Skills.Select(s => (JsonNode)s)]));
        Set(spec, "skills", skills.Any(p => p.Key is "refs" or "gitRefs" or "s3Refs") ? skills : null);
        return spec;
    }

    /// <summary>The form's MCP servers and agents; a server the agent already had keeps its other settings.</summary>
    private static JsonArray? Tools(JsonArray current, AgentForm form)
    {
        var tools = new JsonArray();
        foreach (var choice in form.McpTools)
        {
            var server = current.Select(t => t?["mcpServer"] as JsonObject).FirstOrDefault(s => Text(s?["name"]) == choice.Server)?.DeepClone() as JsonObject
                ?? new JsonObject { ["kind"] = "RemoteMCPServer", ["apiGroup"] = "kagent.dev", ["name"] = choice.Server };
            Set(server, "toolNames", choice.Tools.Count == 0 ? null : new JsonArray([.. choice.Tools.Select(t => (JsonNode)t)]));
            if (server["requireApproval"] is JsonArray approval)
                Set(server, "requireApproval", Strings(approval).Where(choice.Tools.Contains).ToList() is { Count: > 0 } kept
                    ? new JsonArray([.. kept.Select(t => (JsonNode)t)]) : null);
            tools.Add(new JsonObject { ["type"] = "McpServer", ["mcpServer"] = server });
        }
        foreach (var agent in form.AgentTools)
            tools.Add(new JsonObject
            {
                ["type"] = "Agent",
                ["agent"] = new JsonObject { ["kind"] = "Agent", ["apiGroup"] = "kagent.dev", ["name"] = agent, ["namespace"] = form.Namespace }
            });
        return tools.Count == 0 ? null : tools;
    }

    private static JsonNode A2aSkill(A2aSkillForm skill)
    {
        var node = new JsonObject { ["id"] = skill.Id, ["name"] = skill.Name };
        Set(node, "description", string.IsNullOrEmpty(skill.Description) ? null : skill.Description);
        Set(node, "tags", skill.Tags is { Count: > 0 } tags ? new JsonArray([.. tags.Select(t => (JsonNode)t)]) : null);
        Set(node, "examples", skill.Examples is { Count: > 0 } examples ? new JsonArray([.. examples.Select(e => (JsonNode)e)]) : null);
        return node;
    }

    /// <summary>The agent as the API shows it: never annotations, env, headers or secret references.</summary>
    public static AgentDetail Detail(JsonObject agent, bool full, bool canWrite)
    {
        var name = Text(agent["metadata"]?["name"]) ?? "";
        var labels = Labels(agent);
        var spec = agent["spec"];
        var declarative = spec?["declarative"];
        var source = AgentProtection.SourceOf(name, labels);
        var refusal = AgentProtection.Refusal(name, labels);
        var declarativeType = IsDeclarative(agent);
        var tools = declarative?["tools"] as JsonArray ?? [];
        return new AgentDetail(
            Text(agent["metadata"]?["namespace"]) ?? "",
            name,
            Text(spec?["description"]) ?? "",
            Text(spec?["type"]) ?? "Declarative",
            Condition(agent, "Ready"),
            Condition(agent, "Accepted"),
            source,
            labels.GetValueOrDefault(StudioNames.MergedLabel) == "true",
            canWrite && refusal is null && declarativeType && !HasPromptSource(agent),
            canWrite && refusal is null,
            refusal,
            source == AgentSource.Studio ? StudioNames.AgentPath(name) : null,
            Text(declarative?["modelConfig"]),
            full ? Text(declarative?["systemMessage"]) : null,
            Text(declarative?["runtime"]),
            declarative?["stream"] is JsonValue stream && stream.TryGetValue<bool>(out var streaming) && streaming,
            [.. tools.Select(t => t?["mcpServer"]).OfType<JsonObject>()
                .Select(s => new McpToolChoice(Text(s["name"]) ?? "", [.. Strings(s["toolNames"])]))],
            [.. tools.Select(t => Text(t?["agent"]?["name"])).OfType<string>()],
            [.. Strings(spec?["skills"]?["refs"])],
            [.. (declarative?["a2aConfig"]?["skills"] as JsonArray ?? []).OfType<JsonObject>().Select(s => new A2aSkillForm(
                Text(s["id"]) ?? "", Text(s["name"]) ?? "", Text(s["description"]), [.. Strings(s["tags"])], [.. Strings(s["examples"])]))],
            full ? [.. (agent["status"]?["conditions"] as JsonArray ?? []).OfType<JsonObject>().Select(c => new AgentCondition(
                Text(c["type"]) ?? "", Text(c["status"]) ?? "", Text(c["reason"]), Text(c["message"]) is { } m && m.Length > 300 ? m[..300] : Text(c["message"])))] : []);
    }

    public static Dictionary<string, string> Labels(JsonObject agent) =>
        (agent["metadata"]?["labels"] as JsonObject ?? [])
        .Where(l => Text(l.Value) is not null).ToDictionary(l => l.Key, l => Text(l.Value)!);

    private static bool Condition(JsonObject agent, string type) =>
        (agent["status"]?["conditions"] as JsonArray ?? []).Any(c => Text(c?["type"]) == type && Text(c?["status"]) == "True");

    private static void Set(JsonObject target, string key, JsonNode? value)
    {
        if (value is null)
            target.Remove(key);
        else
            target[key] = value;
    }

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static IEnumerable<string> Strings(JsonNode? node) => (node as JsonArray ?? []).Select(Text).OfType<string>();
}
