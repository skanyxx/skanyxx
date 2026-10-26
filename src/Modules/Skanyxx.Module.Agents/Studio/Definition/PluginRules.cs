using System.Text.RegularExpressions;
using Skanyxx.Core.Platform.Plugins;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Plugins;

namespace Skanyxx.Module.Agents.Studio.Definition;

/// <summary>
/// What a plugin may contain (P1–P8), checked on the install (untrusted, whatever translated it) and again on the files
/// of a plugin proposal and of main. Returns every problem, in words for the person.
/// </summary>
internal static partial class PluginRules
{
    public const int MaxAgents = 20;
    public const int MaxServers = 10;
    public const int MaxHeaders = 10;
    public const int MaxNotes = 200;
    public const int MaxHeaderValue = 4_096;

    public static readonly string[] SourceKinds = ["git", "zip", "catalog"];

    public static readonly string[] NoteStatuses = ["changed", "unsupported", "refused"];

    public static readonly string[] Protocols = ["SSE", "STREAMABLE_HTTP"];

    public static IReadOnlyList<string> CheckPlugin(InstalledPlugin plugin)
    {
        var problems = new List<string>();
        if (!StudioNames.IsValidPluginName(plugin.Name))
            problems.Add($"Plugin name: lowercase letters, digits and '-', at most {StudioNames.MaxServerNameLength} characters, starting with a letter.");
        if (!IsLine(plugin.Description, DraftRules.MaxDescription))
            problems.Add($"Plugin description: one line, at most {DraftRules.MaxDescription} characters.");
        if (plugin.Version is { } version && (version.Length == 0 || !IsLine(version, 64)))
            problems.Add("Plugin version: one line, at most 64 characters.");

        var source = plugin.Source;
        if (!SourceKinds.Contains(source.Kind))
            problems.Add($"Source kind: one of {string.Join(", ", SourceKinds)}.");
        if (source.Location.Length == 0 || !IsLine(source.Location, PluginServerUrl.MaxUrl) || source.Location.Any(char.IsWhiteSpace))
            problems.Add("Source location: required, one line without spaces.");
        if (source.Kind == "git" ? source.Commit is null || !Commit().IsMatch(source.Commit) : source.Commit is not null)
            problems.Add("Source commit: a full 40-hex commit for a git source, none otherwise.");
        if (!IsLine(source.Path, 512) || source.Path.Split('/').Contains(".."))
            problems.Add("Source path: one line, at most 512 characters, no '..'.");
        if (!Digest().IsMatch(source.Digest))
            problems.Add("Source digest: a SHA-256 (64 lowercase hex, optionally 'sha256:' first).");

        if (plugin.Agents.Count > MaxAgents || plugin.Agents.Distinct().Count() != plugin.Agents.Count || plugin.Agents.Any(a => !StudioNames.IsValidName(a)))
            problems.Add($"Agents: at most {MaxAgents} distinct valid agent names.");
        if (plugin.Servers.Count > MaxServers || plugin.Servers.Distinct().Count() != plugin.Servers.Count || plugin.Servers.Any(s => !StudioNames.IsValidServerName(s)))
            problems.Add($"Servers: at most {MaxServers} distinct server names (Kubernetes names of at most {StudioNames.MaxServerNameLength}, not 'skanyxx-' or 'preview-').");
        foreach (var header in plugin.Headers)
        {
            if (!plugin.Servers.Contains(header.Server))
                problems.Add($"Header '{header.Name}' names the server '{header.Server}', which the plugin does not list.");
            if (!IsHeaderName(header.Name))
                problems.Add($"Header '{header.Name}': {HeaderNameRule}");
            if (header.Template is { } template && !IsTemplate(template))
                problems.Add($"Header '{header.Name}' of '{header.Server}': a template is only variables (${{NAME}}), optionally after one word such as 'Bearer'.");
        }
        if (plugin.Headers.GroupBy(h => (h.Server, h.Name.ToLowerInvariant())).Any(g => g.Count() > 1)
            || plugin.Headers.GroupBy(h => h.Server).Any(g => g.Count() > MaxHeaders))
            problems.Add($"Headers: each once per server, at most {MaxHeaders} per server.");
        if (plugin.Notes.Count > MaxNotes)
            problems.Add($"Notes: at most {MaxNotes}.");
        if (plugin.Notes.Any(n => !IsLine(n.Item, 200) || !NoteStatuses.Contains(n.Status) || !IsLine(n.Reason, DraftRules.MaxDescription)))
            problems.Add($"Each note: an item (at most 200 characters), a status ({string.Join(", ", NoteStatuses)}) and a reason, one line each.");
        return problems;
    }

    /// <summary>A server's shape and values; its headers by name (values are judged by <see cref="CheckHeaderValues"/>).</summary>
    public static IReadOnlyList<string> CheckServer(PluginServerDraft server, StudioOptions options, PluginsOptions plugins)
    {
        var problems = new List<string>();
        var name = server.Name;
        if (!StudioNames.IsValidServerName(name))
            problems.Add($"Server '{name}': a Kubernetes name of at most {StudioNames.MaxServerNameLength} characters, not starting 'skanyxx-' or 'preview-'.");
        else if (options.McpServers.Contains(name))
            problems.Add($"Server '{name}' is an allow-listed server of the owner's (Studio:McpServers); a plugin cannot bring one by that name.");
        if (!IsLine(server.Description, DraftRules.MaxDescription))
            problems.Add($"Server '{name}' description: one line, at most {DraftRules.MaxDescription} characters.");
        if (!Protocols.Contains(server.Protocol))
            problems.Add($"Server '{name}' protocol: {string.Join(" or ", Protocols)}.");
        if (PluginServerUrl.Problem(server.Url, plugins.AllowHttp, plugins.AllowPrivateServers) is { } url)
            problems.Add($"Server '{name}' url: {url}");
        if (server.Headers.Count > MaxHeaders || server.Headers.Any(h => !IsHeaderName(h.Name))
            || server.Headers.Select(h => h.Name.ToLowerInvariant()).Distinct().Count() != server.Headers.Count)
            problems.Add($"Server '{name}' headers: at most {MaxHeaders}, each once; each name {HeaderNameRule}");
        return problems;
    }

    /// <summary>
    /// What an install brings for each header: exactly one of a template (goes to git) and a literal value (goes to the
    /// Secret only), the value at most <see cref="MaxHeaderValue"/> without control characters.
    /// </summary>
    public static IReadOnlyList<string> CheckHeaderValues(PluginServerDraft server)
    {
        var problems = new List<string>();
        foreach (var header in server.Headers)
        {
            if ((header.Template is null) == (header.Value is null))
                problems.Add($"Header '{header.Name}' of '{server.Name}': a template or a value, not both and not neither.");
            if (header.Template is { } template && !IsTemplate(template))
                problems.Add($"Header '{header.Name}' of '{server.Name}': a template is only variables (${{NAME}}), optionally after one word such as 'Bearer'.");
            if (header.Value is { } value && !IsHeaderValue(value))
                problems.Add($"Header '{header.Name}' of '{server.Name}': a value of at most {MaxHeaderValue} characters, without control characters.");
        }
        return problems;
    }

    /// <summary>A plugin's agent is a recipe (D005): no memory grants, no kagent TTL memory, no skills, only its plugin's servers.</summary>
    public static IReadOnlyList<string> CheckRecipe(AgentDraft agent, IReadOnlyList<string> pluginServers)
    {
        var problems = new List<string>();
        foreach (var other in agent.McpTools.Select(t => t.Server).Where(s => !pluginServers.Contains(s)))
            problems.Add($"Agent '{agent.Name}': a plugin's agent uses only its plugin's servers, not '{other}'.");
        if (agent.Grants.Count > 0 || agent.MemoryTtlDays is not null)
            problems.Add($"Agent '{agent.Name}': a plugin's agent never has memory grants or kagent TTL memory (recipes only).");
        if (agent.Skills.Count > 0)
            problems.Add($"Agent '{agent.Name}': a plugin's agent has no skills.");
        return problems;
    }

    public const string HeaderNameRule =
        "letters, digits and '-', starting with a letter, and not Host, Cookie, a Content-*, Proxy-* or X-Forwarded-* header, a client-IP or URL-override "
        + "header (X-Real-IP, True-Client-IP, X-Original-URL, X-HTTP-Method-Override, …) or another connection-level one.";

    /// <summary>
    /// A header a plugin may have kagent send. Names that steer the connection, a proxy or what the far end believes about
    /// the caller (Host, Cookie, Content-*, Transfer-Encoding, X-Forwarded-*, …) are not a plugin's to set.
    /// </summary>
    public static bool IsHeaderName(string name) => HeaderName().IsMatch(name) && !SensitiveHeader().IsMatch(name);

    public static bool IsHeaderValue(string value) => value.Length is > 0 and <= MaxHeaderValue && !value.Any(char.IsControl);

    public static bool IsTemplate(string template) => template.Length <= 256 && Template().IsMatch(template);

    private static bool IsLine(string text, int max) => text.Length <= max && !DraftRules.HasControl(text, allowNewlines: false);

    [GeneratedRegex("^[a-f0-9]{40}\\z")]
    private static partial Regex Commit();

    [GeneratedRegex("^(sha256:)?[a-f0-9]{64}\\z")]
    private static partial Regex Digest();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9-]{0,63}\\z")]
    private static partial Regex HeaderName();

    [GeneratedRegex("^(host|cookie|set-cookie|content-.*|transfer-encoding|connection|keep-alive|proxy-.*|x-forwarded-.*|forwarded|te|trailer|upgrade|via"
        + "|x-real-ip|true-client-ip|x-client-ip|cf-connecting-ip|x-original-forwarded-for|x-original-url|x-rewrite-url|x-host|x-original-host|x-envoy-.*|x-http-method-override)\\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveHeader();

    // One optional word (Bearer, token, Basic), then only ${VAR}s (user_config.KEY included): a template never carries
    // a literal secret.
    [GeneratedRegex(@"^(?:[A-Za-z]{1,20} )?(?:\$\{[A-Za-z_][A-Za-z0-9_.]{0,63}\})+\z")]
    private static partial Regex Template();
}
