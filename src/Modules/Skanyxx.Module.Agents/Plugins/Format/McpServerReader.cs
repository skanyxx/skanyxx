using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Skanyxx.Core.Platform.Plugins;
using Skanyxx.Module.Agents.Studio.Definition;

namespace Skanyxx.Module.Agents.Plugins.Format;

/// <summary>
/// A plugin's MCP servers (P3, P4): <c>.mcp.json</c> first, then each <c>mcpServers</c> entry of <c>plugin.json</c> in
/// order (a later name replaces an earlier one, as in Claude Code), translated into kagent RemoteMCPServers. Only remote
/// servers install; stdio and the rest are reported, never run.
/// </summary>
internal static partial class McpServerReader
{
    public const int MaxServers = 10;

    public const int MaxHeaders = 20;

    private const string DefaultFile = ".mcp.json";

    /// <summary>
    /// <c>Installed</c>: the plugin's key of each server that installs and its draft, in declaration order.
    /// <c>Declared</c>: every key the plugin declares, installed or not.
    /// </summary>
    public static (OrderedDictionary<string, PluginServerDraft> Installed, IReadOnlySet<string> Declared) Read(
        PluginTree root, JsonObject? manifest, string plugin, PluginReaderOptions options, List<PluginNote> notes)
    {
        var configs = new OrderedDictionary<string, JsonNode?>(StringComparer.Ordinal);
        if (root.Read(DefaultFile) is not null)
            AddFile(root, DefaultFile, configs, notes);
        JsonNode?[] entries = manifest?["mcpServers"] switch { JsonArray list => [.. list], null => [], var one => [one] };
        foreach (var entry in entries)
        {
            if (entry is JsonObject inline)
                AddMap(inline, "plugin.json mcpServers", configs, notes);
            else if (FileEntry(entry, notes) is { } path)
                AddFile(root, path, configs, notes);
        }

        var installed = new OrderedDictionary<string, PluginServerDraft>(StringComparer.Ordinal);
        foreach (var (key, config) in configs)
        {
            if (Translate(key, config, plugin, options, notes) is not { } draft)
                continue;
            if (installed.Values.Any(s => s.Name == draft.Name))
                notes.Add(PluginNotes.Refused(key, $"Another server of this plugin is already named '{draft.Name}'."));
            else if (installed.Count == MaxServers)
                notes.Add(PluginNotes.Refused(key, $"A plugin installs at most {MaxServers} MCP servers."));
            else
                installed.Add(key, draft);
        }
        return (installed, configs.Keys.ToHashSet(StringComparer.Ordinal));
    }

    private static string? FileEntry(JsonNode? entry, List<PluginNote> notes)
    {
        var raw = PluginFiles.Text(entry);
        if (raw is null)
        {
            notes.Add(PluginNotes.Refused("plugin.json mcpServers", "Each entry is a ./path to a .json file or an object of servers."));
            return null;
        }
        if (raw.EndsWith(".mcpb", StringComparison.OrdinalIgnoreCase) || raw.EndsWith(".dxt", StringComparison.OrdinalIgnoreCase))
        {
            notes.Add(PluginNotes.Unsupported(raw, "MCP bundles (.mcpb, .dxt) are not installed."));
            return null;
        }
        if (PluginFiles.ComponentPath(raw) is not { } path || !path.EndsWith(".json", StringComparison.Ordinal))
        {
            notes.Add(PluginNotes.Refused(raw, "An MCP config path starts with ./, stays inside the plugin and names a .json file."));
            return null;
        }
        return path;
    }

    private static void AddFile(PluginTree root, string path, OrderedDictionary<string, JsonNode?> configs, List<PluginNote> notes)
    {
        var (json, problem) = PluginFiles.ReadObject(root, path);
        if (json is null)
        {
            notes.Add(PluginNotes.Refused(path, problem!));
            return;
        }
        // Both shapes are real: { "mcpServers": { … } } and the servers at the top level (github, honeycomb).
        switch (json["mcpServers"])
        {
            case JsonObject wrapped:
                AddMap(wrapped, path, configs, notes);
                break;
            case null:
                AddMap(json, path, configs, notes);
                break;
            default:
                notes.Add(PluginNotes.Refused(path, "mcpServers must be an object of servers."));
                break;
        }
    }

    private static void AddMap(JsonObject map, string from, OrderedDictionary<string, JsonNode?> configs, List<PluginNote> notes)
    {
        foreach (var (key, config) in map)
        {
            if (key == "$schema")
                continue;
            if (key.Length == 0)
                notes.Add(PluginNotes.Refused(from, "A server has an empty name."));
            else
                configs[key] = config;
        }
    }

    private static PluginServerDraft? Translate(string key, JsonNode? config, string plugin, PluginReaderOptions options, List<PluginNote> notes)
    {
        if (config is not JsonObject server)
        {
            notes.Add(PluginNotes.Refused(key, "A server's config must be an object."));
            return null;
        }
        var url = PluginFiles.Text(server["url"]);
        var protocol = PluginFiles.Text(server["type"]) switch
        {
            "http" or "streamable-http" => "STREAMABLE_HTTP",
            "sse" => "SSE",
            null when url is not null => Unsupported("It has a url but no type; Claude Code skips such an entry too (add \"type\": \"http\")."),
            null or "stdio" => Unsupported("A stdio server runs a local command; Skanyxx only connects to remote MCP servers."),
            "ws" => Unsupported("WebSocket MCP servers are not supported by kagent."),
            "sdk" => Unsupported("An sdk server lives inside Claude Code's own process."),
            var other => Unsupported($"Unknown server type '{other}'.")
        };
        if (protocol is null)
            return null;

        if (url is null)
            return Refused("A remote server needs a url.");
        if (PluginServerUrl.Problem(url, options.AllowHttp, options.AllowPrivateServers) is { } urlProblem)
            return Refused($"The url: {urlProblem}");

        var headers = new List<PluginHeader>();
        if (server["headers"] is { } headerNode)
        {
            if (headerNode is not JsonObject headerMap || headerMap.Count > MaxHeaders)
                return Refused($"headers must be an object of at most {MaxHeaders} text values.");
            foreach (var (header, value) in headerMap)
            {
                if (!PluginRules.IsHeaderName(header))
                    return Refused($"'{header}' is not a header name Skanyxx sends: letters, digits and '-', starting with a letter, and not "
                        + "Host, Cookie, a Content-*, Proxy-* or X-Forwarded-* header or another connection-level one.");
                if (PluginFiles.Text(value) is not { } text || text.Any(char.IsControl))
                    return Refused($"Header '{header}' must be one line of text.");
                // A value naming variables is a template the owner fills in; anything else is a literal, possibly a
                // credential, so it is only ever a secret value (P3). A default would put a literal in git: dropped.
                if (!text.Contains("${", StringComparison.Ordinal))
                {
                    headers.Add(new PluginHeader(header, null, text));
                    continue;
                }
                var template = VariableDefault().Replace(text, "}");
                if (!PluginRules.IsTemplate(template))
                    return Refused($"Header '{header}': a template is only variables (${{NAME}}), optionally after one word such as 'Bearer'.");
                if (template != text)
                    notes.Add(PluginNotes.Changed($"{key}.{header}", "Variable defaults are left out; the owner enters the value."));
                headers.Add(new PluginHeader(header, template, null));
            }
        }
        if (server["headersHelper"] is not null)
            notes.Add(PluginNotes.Changed(key, "headersHelper ignored: it runs a local command. Set the header values as secrets instead."));
        if (server["oauth"] is not null)
            notes.Add(PluginNotes.Changed(key, "oauth ignored: kagent does not sign in to MCP servers. Set an Authorization header secret instead."));

        var name = PluginNames.ServerName(plugin, key);
        if (name.Length == 0)
            return Refused("The name has no letters or digits.");
        if (PluginNames.IsReservedServer(name))
            return Refused($"'{name}' starts with skanyxx- or preview-, which are Skanyxx's own servers.");
        if (name != $"{plugin}-{key}")
            notes.Add(PluginNotes.Changed(key, $"Named '{name}': a server name is '<plugin>-<server>' in lowercase letters, digits and '-', at most {PluginNames.MaxServerName} characters."));
        return new PluginServerDraft(name, $"{key} from the {plugin} plugin", protocol, url, headers);

        string? Unsupported(string reason)
        {
            notes.Add(PluginNotes.Unsupported(key, reason));
            return null;
        }

        PluginServerDraft? Refused(string reason)
        {
            notes.Add(PluginNotes.Refused(key, reason));
            return null;
        }
    }

    // ${VAR:-default} → ${VAR}.
    [GeneratedRegex(@":-[^}]*\}")]
    private static partial Regex VariableDefault();
}
