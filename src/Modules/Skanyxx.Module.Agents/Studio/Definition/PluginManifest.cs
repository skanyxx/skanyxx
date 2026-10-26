using System.Text.Json.Nodes;
using Skanyxx.Core.Platform.Plugins;

namespace Skanyxx.Module.Agents.Studio.Definition;

/// <summary>
/// <c>plugins/&lt;p&gt;/plugin.yaml</c>: provenance (source, commit, digest), the agents and servers the plugin owns, each
/// header's name and template (never a value) and the install report. Read back strictly, like <see cref="AgentManifest"/>.
/// </summary>
internal static class PluginManifest
{
    private const string Header = "# Skanyxx plugin. Written by Skanyxx's plugin install; it owns the agents and servers listed here.\n";

    public static string Render(InstalledPlugin plugin) => Header + YamlTree.Write(Tree(plugin));

    private static JsonObject Tree(InstalledPlugin plugin)
    {
        var tree = new JsonObject { ["name"] = plugin.Name };
        if (plugin.Version is not null)
            tree["version"] = plugin.Version;
        tree["description"] = plugin.Description;
        var source = new JsonObject { ["kind"] = plugin.Source.Kind, ["location"] = plugin.Source.Location };
        if (plugin.Source.Commit is not null)
            source["commit"] = plugin.Source.Commit;
        source["path"] = plugin.Source.Path;
        source["digest"] = plugin.Source.Digest;
        tree["source"] = source;
        tree["agents"] = new JsonArray([.. plugin.Agents.Select(a => (JsonNode)a)]);
        tree["servers"] = new JsonArray([.. plugin.Servers.Select(s => (JsonNode)s)]);
        tree["headers"] = new JsonArray([.. plugin.Headers.Select(h =>
        {
            var header = new JsonObject { ["server"] = h.Server, ["name"] = h.Name };
            if (h.Template is not null)
                header["template"] = h.Template;
            return (JsonNode)header;
        })]);
        tree["notes"] = new JsonArray([.. plugin.Notes.Select(n => (JsonNode)new JsonObject
        {
            ["item"] = n.Item, ["status"] = n.Status, ["reason"] = n.Reason
        })]);
        return tree;
    }

    /// <summary>The plugin <paramref name="yaml"/> describes, or the reason it is not one of ours. Values are judged by <see cref="PluginRules"/>.</summary>
    public static (InstalledPlugin? Plugin, string? Problem) Read(string folderName, string yaml)
    {
        JsonNode? tree;
        try
        {
            tree = YamlTree.Read(yaml);
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            return (null, $"Not valid YAML: {ex.Message.Split('\n')[0]}");
        }
        if (tree is not JsonObject root)
            return (null, "plugin.yaml must be a YAML mapping.");

        var source = At(root, "source");
        var plugin = new InstalledPlugin(
            Text(At(root, "name")) ?? "",
            Text(At(root, "version")),
            Text(At(root, "description")) ?? "",
            new PluginSource(Text(At(source, "kind")) ?? "", Text(At(source, "location")) ?? "", Text(At(source, "commit")),
                Text(At(source, "path")) ?? "", Text(At(source, "digest")) ?? ""),
            [.. Items(At(root, "agents")).Select(a => Text(a) ?? "")],
            [.. Items(At(root, "servers")).Select(s => Text(s) ?? "")],
            [.. Items(At(root, "headers")).Select(h => new PluginHeaderTemplate(Text(At(h, "server")) ?? "", Text(At(h, "name")) ?? "", Text(At(h, "template"))))],
            [.. Items(At(root, "notes")).Select(n => new PluginNote(Text(At(n, "item")) ?? "", Text(At(n, "status")) ?? "", Text(At(n, "reason")) ?? ""))]);
        if (plugin.Name != folderName)
            return (null, $"{StudioNames.PluginManifestPath(folderName)} must name the plugin '{folderName}'.");
        if (!JsonNode.DeepEquals(YamlTree.AsRead(Tree(plugin)), root))
            return (null, "plugin.yaml must hold only the plugin's name, version, description, source, agents, servers, header names and templates, and notes.");
        return (plugin, null);
    }

    private static JsonNode? At(JsonNode? node, string key) => node is JsonObject o ? o[key] : null;

    private static IEnumerable<JsonNode?> Items(JsonNode? node) => node as JsonArray ?? [];

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
