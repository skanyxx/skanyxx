using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Skanyxx.Core.Platform.Plugins;
using static Skanyxx.Module.Agents.Plugins.Format.PluginFiles;

namespace Skanyxx.Module.Agents.Plugins.Format;

/// <summary>
/// A <c>.claude-plugin/marketplace.json</c> as a listing (P10). An entry installs from a folder of the same source, or
/// from another git repository pinned to a full commit SHA; a branch or tag alone is refused (it can move after the
/// supervisor reviewed it), and npm, archive and command sources are not supported. Each entry is judged on its own.
/// </summary>
internal static partial class MarketplaceReader
{
    public const string File = ".claude-plugin/marketplace.json";

    public const int MaxEntries = 500;

    /// <param name="source">The whole source.</param>
    /// <param name="root">The marketplace's folder in it; entry paths come back relative to the source.</param>
    public static (PluginMarketplace? Marketplace, string? Problem) Read(PluginTree source, string root, bool allowHttp)
    {
        var tree = source.Sub(root);
        var (json, problem) = ReadObject(tree, File);
        if (json is null)
            return (null, problem);
        if (json["plugins"] is not JsonArray plugins)
            return (null, $"{File} has no plugins list.");
        if (plugins.Count > MaxEntries)
            return (null, $"{File} lists {plugins.Count} plugins; at most {MaxEntries}.");

        var pluginRoot = Text(At(json["metadata"], "pluginRoot"));
        var entries = new List<MarketplaceEntry>();
        foreach (var node in plugins)
        {
            var entry = Entry(node, tree, root, pluginRoot, allowHttp);
            entries.Add(entry.Problem is null && entries.Any(e => e.Name == entry.Name)
                ? Problem(entry.Name, entry.Description, entry.Version, PluginNotes.Refused(entry.Name, "Listed twice; the first entry counts."))
                : entry);
        }
        return (new PluginMarketplace(
            Text(json["name"]) ?? "",
            Text(json["description"]) ?? Text(At(json["metadata"], "description")) ?? "",
            entries), null);
    }

    private static MarketplaceEntry Entry(JsonNode? node, PluginTree tree, string root, string? pluginRoot, bool allowHttp)
    {
        if (node is not JsonObject entry)
            return Problem("", "", null, PluginNotes.Refused("(entry)", "A marketplace entry must be an object."));
        var name = Text(entry["name"]) ?? "";
        var description = Text(entry["description"]) ?? "";
        var version = Text(entry["version"]);
        if (!PluginNames.IsValidPluginName(name))
            return Refused($"'{name}' is not a plugin name: letters, digits, '.', '_' and '-', starting and ending with a letter or digit, at most 63 characters.");

        switch (entry["source"])
        {
            case JsonValue value when Text(value) is { } relative:
                var path = FolderPath(relative, pluginRoot);
                if (path is null)
                    return Refused($"Source '{relative}' must be ./<folder> inside the marketplace (or a bare name under metadata.pluginRoot).");
                if (!tree.HasFolder(path))
                    return Refused($"Source '{relative}' is not a folder of this marketplace.");
                return new(name, description, version, Combine(root, path), null, null);

            case JsonObject git:
                var kind = Text(git["source"]);
                var (url, subdir) = kind switch
                {
                    "github" => (Text(git["repo"]) is { } repo && OwnerRepo().IsMatch(repo) ? $"https://github.com/{repo}" : null, ""),
                    "url" => (Text(git["url"]), ""),
                    "git-subdir" => (Text(git["url"]) is { } u && OwnerRepo().IsMatch(u) ? $"https://github.com/{u}" : Text(git["url"]), SubdirPath(Text(git["path"]))),
                    _ => (null, null)
                };
                if (kind is not ("github" or "url" or "git-subdir"))
                    return new(name, description, version, null, null, PluginNotes.Unsupported(name,
                        kind is "npm" or "archive" or "command"
                            ? $"{kind} sources are not supported: only a folder of this marketplace or a git repository pinned to a commit."
                            : $"Unknown source type '{kind}'."));
                if (url is null || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
                    || !(uri.Scheme == Uri.UriSchemeHttps || (allowHttp && uri.Scheme == Uri.UriSchemeHttp)))
                    return Refused($"The repository must be an {(allowHttp ? "http(s)" : "https")} URL (or owner/repo for GitHub).");
                if (subdir is null)
                    return Refused("path must be a folder inside the repository (no '..').");
                if (Text(git["sha"]) is not { } sha)
                    return Refused(Text(git["ref"]) is { } gitRef
                        ? $"Not pinned: '{gitRef}' can move after review; the entry needs a full commit sha."
                        : "Not pinned: the entry needs a full commit sha.");
                if (!Sha().IsMatch(sha))
                    return Refused("sha must be a full 40-character lowercase commit SHA.");
                return new(name, description, version, null, new PinnedGitSource(url, sha, subdir), null);

            default:
                return Refused("The entry has no source.");
        }

        MarketplaceEntry Refused(string reason) => Problem(name, description, version, PluginNotes.Refused(name, reason));
    }

    private static MarketplaceEntry Problem(string name, string description, string? version, PluginNote problem) =>
        new(name, description, version, null, null, problem);

    /// <summary>"." for the marketplace root, ./folder, or a bare name under <c>metadata.pluginRoot</c>.</summary>
    private static string? FolderPath(string source, string? pluginRoot)
    {
        if (source is "." or "./")
            return "";
        if (source.StartsWith("./", StringComparison.Ordinal))
            return ComponentPath(source);
        if (pluginRoot is null || !Segment().IsMatch(source))
            return null;
        var rootPath = pluginRoot is "." or "./" ? "" : ComponentPath(pluginRoot.StartsWith("./", StringComparison.Ordinal) ? pluginRoot : "./" + pluginRoot);
        return rootPath is null ? null : Combine(rootPath, source);
    }

    private static string? SubdirPath(string? path) => path is null or "" or "." or "./" ? ""
        : ComponentPath(path.StartsWith("./", StringComparison.Ordinal) ? path : "./" + path);

    private static string Combine(string folder, string path) => folder.Length == 0 ? path : path.Length == 0 ? folder : $"{folder}/{path}";

    [GeneratedRegex("^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+\\z")]
    private static partial Regex OwnerRepo();

    [GeneratedRegex("^[0-9a-f]{40}\\z")]
    private static partial Regex Sha();

    [GeneratedRegex("^[A-Za-z0-9_-][A-Za-z0-9._-]*\\z")]
    private static partial Regex Segment();
}
