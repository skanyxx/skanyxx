using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Studio.Definition;

namespace Skanyxx.Module.Agents.Manage.Definition;

/// <summary>
/// The form to and from kagent's Agent CR (kagent.dev/v1alpha2). kagent's update replaces the whole spec, so an edit
/// starts from the agent's current spec and changes only the form's fields: anything else (deployment, memory, context,
/// header settings on a kept MCP server) stays as it was.
/// </summary>
internal static partial class AgentSpec
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

    /// <summary>
    /// The agent with the form applied: kagent's PUT takes its name, namespace and spec and ignores the rest of metadata
    /// (labels stay as they are).
    /// </summary>
    public static JsonObject Updated(JsonObject agent, AgentForm form)
    {
        var updated = (JsonObject)agent.DeepClone();
        updated["spec"] = Apply(agent["spec"] as JsonObject ?? new JsonObject(), form);
        return updated;
    }

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
        return new ExistingSpec(mcp, Strings(agent["spec"]?["skills"]?["refs"]).ToHashSet(),
            (declarative?["tools"] as JsonArray ?? []).Select(t => Text(t?["agent"]?["name"])).OfType<string>().ToHashSet());
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
        var currentSkills = (declarative["a2aConfig"]?["skills"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        Set(declarative, "a2aConfig", form.A2aSkills.Count == 0 ? null : new JsonObject
        {
            ["skills"] = new JsonArray([.. form.A2aSkills.Select(s => A2aSkill(s, currentSkills.FirstOrDefault(c => Text(c["id"]) == s.Id)))])
        });

        var skills = spec["skills"] as JsonObject ?? new JsonObject();
        Set(skills, "refs", form.Skills.Count == 0 ? null : new JsonArray([.. form.Skills.Select(s => (JsonNode)s)]));
        Set(spec, "skills", skills.Any(p => p.Key is "refs" or "gitRefs" or "s3Refs") ? skills : null);
        return spec;
    }

    /// <summary>
    /// The form's MCP servers and agents. A tool the agent already had keeps its whole entry (<c>headersFrom</c> and
    /// <c>isolateSessions</c> sit on the entry, <c>allowedHeaders</c> on the server); only its tool names change.
    /// </summary>
    private static JsonArray? Tools(JsonArray current, AgentForm form)
    {
        var tools = new JsonArray();
        foreach (var choice in form.McpTools)
        {
            var entry = current.FirstOrDefault(t => Text(t?["mcpServer"]?["name"]) == choice.Server)?.DeepClone() as JsonObject
                ?? new JsonObject
                {
                    ["type"] = "McpServer",
                    ["mcpServer"] = new JsonObject { ["kind"] = "RemoteMCPServer", ["apiGroup"] = "kagent.dev", ["name"] = choice.Server }
                };
            var server = (JsonObject)entry["mcpServer"]!;
            Set(server, "toolNames", choice.Tools.Count == 0 ? null : new JsonArray([.. choice.Tools.Select(t => (JsonNode)t)]));
            if (server["requireApproval"] is JsonArray approval)
                Set(server, "requireApproval", Strings(approval).Where(choice.Tools.Contains).ToList() is { Count: > 0 } kept
                    ? new JsonArray([.. kept.Select(t => (JsonNode)t)]) : null);
            tools.Add(entry);
        }
        foreach (var agent in form.AgentTools)
            tools.Add(current.FirstOrDefault(t => Text(t?["agent"]?["name"]) == agent)?.DeepClone() ?? new JsonObject
            {
                ["type"] = "Agent",
                ["agent"] = new JsonObject { ["kind"] = "Agent", ["apiGroup"] = "kagent.dev", ["name"] = agent, ["namespace"] = form.Namespace }
            });
        return tools.Count == 0 ? null : tools;
    }

    /// <summary>
    /// Tools the form cannot show faithfully (D256, C-1): an MCP server or agent in another namespace, a server of another
    /// kind, or one server or agent named by several entries (each entry may carry its own headers, so merging them would
    /// move a credential to other tools). The form names each tool once, by name, in the agent's namespace.
    /// </summary>
    public static bool HasUneditableTools(JsonObject agent)
    {
        var ns = Text(agent["metadata"]?["namespace"]);
        var tools = agent["spec"]?["declarative"]?["tools"] as JsonArray ?? [];
        var servers = tools.Select(t => Text(t?["mcpServer"]?["name"])).OfType<string>().ToList();
        var agents = tools.Select(t => Text(t?["agent"]?["name"])).OfType<string>().ToList();
        return servers.Count != servers.Distinct().Count() || agents.Count != agents.Distinct().Count()
            || tools.Any(t =>
                (t?["mcpServer"] is JsonObject server && (Text(server["namespace"]) is { } sns && sns != ns
                    || Text(server["kind"]) is { } kind && kind != "RemoteMCPServer"))
                || (t?["agent"] is JsonObject target && Text(target["namespace"]) is { } ans && ans != ns));
    }

    /// <summary>
    /// The form's skill over the agent's skill of the same id, so what the form does not show (<c>inputModes</c>,
    /// <c>outputModes</c>) stays (C-5).
    /// </summary>
    private static JsonNode A2aSkill(A2aSkillForm skill, JsonObject? current)
    {
        var node = current?.DeepClone() as JsonObject ?? new JsonObject();
        node["id"] = skill.Id;
        node["name"] = skill.Name;
        Set(node, "description", string.IsNullOrEmpty(skill.Description) ? null : skill.Description);
        Set(node, "tags", skill.Tags is { Count: > 0 } tags ? new JsonArray([.. tags.Select(t => (JsonNode)t)]) : null);
        Set(node, "examples", skill.Examples is { Count: > 0 } examples ? new JsonArray([.. examples.Select(e => (JsonNode)e)]) : null);
        return node;
    }

    /// <summary>The agent as the API shows it: never annotations, env, headers or secret references.</summary>
    /// <summary>The edit version of an agent: a hash of its spec as kagent returns it (C-2).</summary>
    public static string Version(JsonObject agent) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(agent["spec"]?.ToJsonString() ?? "")))[..32];

    /// <param name="read">Read from kagent (it carries a version); false for what a write just sent.</param>
    public static AgentDetail Detail(JsonObject agent, bool full, bool canWrite, bool read = true)
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
            canWrite && refusal is null && declarativeType && !HasPromptSource(agent) && !HasUneditableTools(agent),
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
            full ? [.. (agent["status"]?["conditions"] as JsonArray ?? []).OfType<JsonObject>()
                .Where(c => Token(Text(c["type"])) is not null)
                .Select(c => new AgentCondition(Text(c["type"])!, Text(c["status"]) is "True" or "False" ? Text(c["status"])! : "Unknown", Token(Text(c["reason"]))))] : [],
            read ? Version(agent) : null);
    }

    public static Dictionary<string, string> Labels(JsonObject agent) =>
        (agent["metadata"]?["labels"] as JsonObject ?? [])
        .Where(l => Text(l.Value) is not null).ToDictionary(l => l.Key, l => Text(l.Value)!);

    private static bool Condition(JsonObject agent, string type) =>
        (agent["status"]?["conditions"] as JsonArray ?? []).Any(c => Text(c?["type"]) == type && Text(c?["status"]) == "True");

    /// <summary>A CamelCase word as kagent's condition types and reasons are (Ready, DeploymentReady); anything else is dropped (C-4).</summary>
    private static string? Token(string? text) => text is not null && ConditionToken().IsMatch(text) ? text : null;

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9]{0,63}\\z")]
    private static partial Regex ConditionToken();

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
