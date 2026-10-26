using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Skanyxx.Core.Platform.Plugins;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Studio.Definition;
using YamlDotNet.Core;

namespace Skanyxx.Module.Agents.Plugins.Format;

/// <summary>
/// A plugin's subagents (<c>agents/**/*.md</c>, or the files <c>plugin.json</c> <c>agents</c> lists instead) as studio
/// drafts (P5): YAML frontmatter for name, description and tools, the markdown body as the instructions. Recipes only:
/// no grants, no skills, no TTL memory, and the owner's model rather than the plugin's.
/// </summary>
internal static partial class AgentReader
{
    public const int MaxAgents = 20;

    private const string DefaultFolder = "agents/";

    public static List<AgentDraft> Read(
        PluginTree root, JsonObject? manifest, string plugin, OrderedDictionary<string, PluginServerDraft> servers,
        IReadOnlySet<string> declared, PluginReaderOptions options, List<PluginNote> notes)
    {
        var agents = new List<AgentDraft>();
        foreach (var path in Files(root, manifest, notes))
        {
            if (ReadAgent(root, path, plugin, servers, declared, options, notes) is not { } agent)
                continue;
            if (agents.Any(a => a.Name == agent.Name))
                notes.Add(PluginNotes.Refused(path, $"Another agent of this plugin is already named '{agent.Name}'."));
            else if (agents.Count == MaxAgents)
                notes.Add(PluginNotes.Refused(path, $"A plugin installs at most {MaxAgents} agents."));
            else
                agents.Add(agent);
        }
        return agents;
    }

    /// <summary>The declared <c>agents</c> paths replace the default folder, as in Claude Code.</summary>
    private static List<string> Files(PluginTree root, JsonObject? manifest, List<PluginNote> notes)
    {
        if (manifest?["agents"] is not { } declaredNode)
            return [.. root.Paths.Where(p => p.StartsWith(DefaultFolder, StringComparison.Ordinal) && p.EndsWith(".md", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal)];

        var files = new List<string>();
        JsonNode?[] entries = declaredNode is JsonArray list ? [.. list] : [declaredNode];
        foreach (var entry in entries)
        {
            var raw = PluginFiles.Text(entry);
            var path = raw is null ? null : PluginFiles.ComponentPath(raw);
            if (path is null || !path.EndsWith(".md", StringComparison.Ordinal))
                notes.Add(PluginNotes.Refused(raw ?? "plugin.json agents", "An agent path starts with ./, stays inside the plugin and names a .md file (folders are not accepted)."));
            else if (root.Read(path) is null)
                notes.Add(PluginNotes.Refused(raw!, "The file does not exist."));
            else if (!files.Contains(path))
                files.Add(path);
        }
        return files;
    }

    private static AgentDraft? ReadAgent(
        PluginTree root, string path, string plugin, OrderedDictionary<string, PluginServerDraft> servers,
        IReadOnlySet<string> declared, PluginReaderOptions options, List<PluginNote> notes)
    {
        var (text, problem) = PluginFiles.ReadText(root, path);
        if (text is null)
            return Refused(problem!);
        var (front, body, unclosed) = Split(text.Replace("\r\n", "\n").Replace('\r', '\n'));
        var fields = new JsonObject();
        if (unclosed)
            notes.Add(PluginNotes.Changed(path, "Frontmatter ignored, named after the file: no closing ---."));
        else if (front is null)
            notes.Add(PluginNotes.Changed(path, "No YAML frontmatter: named after the file."));
        else
        {
            try
            {
                fields = YamlTree.Read(front) switch
                {
                    JsonObject map => map,
                    null => fields,
                    _ => throw new YamlException("The frontmatter is not a mapping.")
                };
            }
            catch (YamlException ex)
            {
                // Claude Code still loads such an agent, named after the file, with every field ignored.
                notes.Add(PluginNotes.Changed(path, $"Frontmatter ignored, named after the file: {ex.Message.Split('\n')[0]}"));
            }
        }

        var stem = (path.StartsWith(DefaultFolder, StringComparison.Ordinal) ? path[DefaultFolder.Length..] : path[(path.LastIndexOf('/') + 1)..])[..^3];
        var rawName = PluginFiles.Text(fields["name"]) is { } given && given.Trim().Length > 0 ? given.Trim() : stem;
        var name = PluginNames.AgentName(rawName);
        if (!StudioNames.IsValidName(name))
            return Refused($"'{rawName}' does not make an agent name: lowercase letters, digits and '-', starting with a letter.");
        if (StudioNames.IsReserved(name))
            return Refused($"'{name}' is reserved (the seed and the studio's own agents).");
        if (name != rawName)
            notes.Add(PluginNotes.Changed(path, $"Named '{name}' (was '{rawName}'): lowercase letters, digits and '-', at most {StudioNames.MaxNameLength} characters."));

        var instructions = body.Trim();
        if (instructions.Length == 0)
            return Refused("No instructions: the markdown after the frontmatter is the agent's system prompt.");
        if (instructions.Length > DraftRules.MaxInstructions)
            return Refused($"The instructions are {instructions.Length} characters; at most {DraftRules.MaxInstructions} (never cut).");
        if (instructions.Any(c => (char.IsControl(c) && c is not '\n' and not '\t') || char.GetUnicodeCategory(c) == UnicodeCategory.Format))
            return Refused("The instructions contain control or invisible formatting characters.");

        var description = Description(PluginFiles.Text(fields["description"]), plugin, path, notes);

        if (PluginFiles.Text(fields["model"]) is { } model && model != "inherit")
            notes.Add(PluginNotes.Changed(path, $"model '{model}' ignored: plugin agents run on {options.ModelConfig}."));
        if (fields["skills"] is not null)
            notes.Add(PluginNotes.Changed(path, "skills ignored: plugin agents get no skills."));
        if (fields["disallowedTools"] is not null)
            notes.Add(PluginNotes.Changed(path, "disallowedTools ignored: single tools cannot be excluded; check the MCP tools this agent gets."));

        var toolsNode = fields["tools"];
        if (fields["allowed-tools"] is { } allowed)
        {
            if (toolsNode is null)
            {
                toolsNode = allowed;
                notes.Add(PluginNotes.Changed(path, "allowed-tools (not a Claude Code agent field) read as tools."));
            }
            else
                notes.Add(PluginNotes.Changed(path, "allowed-tools ignored: tools is set."));
        }
        IReadOnlyList<McpToolChoice> tools = toolsNode is null
            ? [.. servers.Values.Select(s => new McpToolChoice(s.Name, []))]
            : Tools(Entries(toolsNode), path, plugin, servers, declared, notes);

        return new AgentDraft(name, description, options.ModelConfig, instructions, [], tools, [], null);

        AgentDraft? Refused(string reason)
        {
            notes.Add(PluginNotes.Refused(path, reason));
            return null;
        }
    }

    /// <summary>The frontmatter (null when the file has none) and the body.</summary>
    private static (string? Front, string Body, bool Unclosed) Split(string text)
    {
        var lines = text.Split('\n');
        if (lines[0].TrimEnd() != "---")
            return (null, text, false);
        var end = Array.FindIndex(lines, 1, l => l.TrimEnd() == "---");
        return end < 0
            ? (null, string.Join('\n', lines[1..]), true)
            : (string.Join('\n', lines[1..end]), string.Join('\n', lines[(end + 1)..]), false);
    }

    private static string Description(string? raw, string plugin, string path, List<PluginNote> notes)
    {
        var line = PluginFiles.OneLine(raw);
        if (line.Length == 0)
        {
            notes.Add(PluginNotes.Changed(path, "No description."));
            return $"Agent from the {plugin} plugin";
        }
        if (line.Length <= DraftRules.MaxDescription)
            return line;
        var cut = char.IsHighSurrogate(line[DraftRules.MaxDescription - 1]) ? DraftRules.MaxDescription - 1 : DraftRules.MaxDescription;
        notes.Add(PluginNotes.Changed(path, $"Description truncated from {line.Length} to {DraftRules.MaxDescription} characters."));
        return line[..cut].TrimEnd();
    }

    /// <summary><c>tools</c> as Claude Code writes it: a comma-separated string or a YAML list.</summary>
    private static List<string> Entries(JsonNode node)
    {
        IEnumerable<string> items = node is JsonArray list ? list.Select(PluginFiles.Text).OfType<string>() : [PluginFiles.Text(node) ?? ""];
        return [.. items.SelectMany(i => i.Split(',')).Select(i => i.Trim()).Where(i => i.Length > 0).Distinct()];
    }

    /// <summary>
    /// <c>mcp__&lt;server&gt;__&lt;tool&gt;</c> or <c>mcp__plugin_&lt;plugin&gt;_&lt;server&gt;__&lt;tool&gt;</c> to that
    /// server's tool names (<c>*</c>, or no tool part, for all of them); anything else is a Claude Code built-in.
    /// </summary>
    private static List<McpToolChoice> Tools(
        List<string> entries, string path, string plugin, OrderedDictionary<string, PluginServerDraft> servers,
        IReadOnlySet<string> declared, List<PluginNote> notes)
    {
        var chosen = new Dictionary<string, HashSet<string>?>(StringComparer.Ordinal);
        var builtIns = new List<string>();
        foreach (var entry in entries)
        {
            if (!entry.StartsWith("mcp__", StringComparison.Ordinal))
            {
                builtIns.Add(entry);
                continue;
            }
            var rest = entry["mcp__".Length..];
            var split = rest.IndexOf("__", StringComparison.Ordinal);
            var (serverPart, tool) = split < 0 ? (rest, "*") : (rest[..split], rest[(split + 2)..]);
            var key = ServerKey(serverPart, plugin, declared);
            if (key is null)
                notes.Add(PluginNotes.Unsupported(path, $"{entry}: '{serverPart}' is not an MCP server of this plugin."));
            else if (!servers.ContainsKey(key))
                notes.Add(PluginNotes.Unsupported(path, $"{entry}: the server '{key}' is not installed (see its note)."));
            else if (tool == "*")
                chosen[key] = null;
            else if (!ToolName().IsMatch(tool))
                notes.Add(PluginNotes.Unsupported(path, $"{entry}: name one tool, or * for all of them."));
            else if (!chosen.TryGetValue(key, out var set))
                chosen[key] = [tool];
            else
                set?.Add(tool);
        }
        if (builtIns.Count > 0)
            notes.Add(PluginNotes.Unsupported(path, $"Claude Code's built-in tools do not exist in kagent: {string.Join(", ", builtIns)}."));
        return [.. servers.Where(s => chosen.ContainsKey(s.Key))
            .Select(s => new McpToolChoice(s.Value.Name, chosen[s.Key] is { } set ? [.. set] : []))];
    }

    /// <summary>The plugin's server key a tool name points at. Claude Code writes names with '_' for any other character.</summary>
    private static string? ServerKey(string part, string plugin, IReadOnlySet<string> declared)
    {
        var prefix = $"plugin_{ToolId(plugin)}_";
        var unprefixed = part.StartsWith(prefix, StringComparison.Ordinal) ? part[prefix.Length..] : part;
        return declared.FirstOrDefault(k => ToolId(k) == part) ?? declared.FirstOrDefault(k => ToolId(k) == unprefixed);
    }

    private static string ToolId(string name) => NotToolId().Replace(name, "_");

    [GeneratedRegex("^[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}\\z")]
    private static partial Regex ToolName();

    [GeneratedRegex("[^A-Za-z0-9_-]")]
    private static partial Regex NotToolId();
}
