using System.Text.Json.Nodes;
using Skanyxx.Core.Platform.Plugins;
using Skanyxx.Module.Agents.Studio.Definition;
using static Skanyxx.Module.Agents.Plugins.Format.PluginFiles;

namespace Skanyxx.Module.Agents.Plugins.Format;

/// <summary>
/// Reads a Claude Code plugin source: a marketplace listing when the folder has <c>.claude-plugin/marketplace.json</c>,
/// otherwise one plugin translated into studio drafts with a note for everything renamed, dropped or refused. Pure: the
/// tree is untrusted input and every file read from it is bounded (<see cref="PluginFiles"/>).
/// </summary>
internal static class PluginReader
{
    public const string ManifestFile = ".claude-plugin/plugin.json";

    public const int MaxDescription = 500;

    public const int MaxVersion = 64;

    private static readonly (string Key, string Default, string What)[] NotInstalled =
    [
        ("skills", "skills/", "Skills"),
        ("commands", "commands/", "Slash commands"),
        ("hooks", "hooks/", "Hooks"),
        ("lspServers", ".lsp.json", "LSP servers")
    ];

    /// <summary>The marketplace or plugin in <paramref name="tree"/>'s folder <see cref="PluginSource.Path"/>.</summary>
    public static PluginReadResult Read(PluginTree tree, PluginSource source, PluginReaderOptions options)
    {
        if (tree.Sub(source.Path).Read(MarketplaceReader.File) is null)
            return ReadPlugin(tree, source, options);
        var (marketplace, problem) = MarketplaceReader.Read(tree, source.Path, options.AllowHttp);
        return marketplace is null ? PluginReadResult.Refused(problem!) : PluginReadResult.Listing(marketplace);
    }

    /// <summary>
    /// The plugin in folder <see cref="PluginSource.Path"/>, even when a marketplace sits there too. Its name comes from
    /// <c>plugin.json</c>, else <paramref name="entryName"/> (the marketplace entry), else the folder.
    /// </summary>
    public static PluginReadResult ReadPlugin(PluginTree tree, PluginSource source, PluginReaderOptions options, string? entryName = null)
    {
        if (!tree.HasFolder(source.Path))
            return PluginReadResult.Refused($"The source has no folder '{source.Path}'.");
        var root = tree.Sub(source.Path);
        JsonObject? manifest = null;
        if (root.Read(ManifestFile) is not null)
        {
            (manifest, var problem) = ReadObject(root, ManifestFile);
            if (manifest is null)
                return PluginReadResult.Refused(problem!);
        }
        var raw = Text(manifest?["name"]) ?? entryName ?? FolderName(source);
        if (!PluginNames.IsValidPluginName(raw))
            return PluginReadResult.Refused($"'{raw}' is not a plugin name: letters, digits, '.', '_' and '-', starting and ending with a letter or digit, at most 63 characters.");
        // Studio keeps a plugin's files and labels under a kube-style name (StudioNames.IsValidPluginName).
        var name = PluginNames.Kebab(raw, StudioNames.MaxServerNameLength);
        if (!StudioNames.IsValidPluginName(name))
            return PluginReadResult.Refused($"'{raw}' is not a plugin name Skanyxx can keep: it must start with a letter.");

        var notes = new List<PluginNote>();
        if (name != raw)
            notes.Add(PluginNotes.Changed(raw, $"Installed as '{name}': lowercase letters, digits and '-', at most {StudioNames.MaxServerNameLength} characters."));
        var (servers, declared) = McpServerReader.Read(root, manifest, name, options, notes);
        var agents = AgentReader.Read(root, manifest, name, servers, declared, options, notes);
        foreach (var (key, folder, what) in NotInstalled)
            if (manifest?[key] is not null || (folder.EndsWith('/') ? root.HasFolder(folder.TrimEnd('/')) : root.Read(folder) is not null))
                notes.Add(PluginNotes.Unsupported(key, $"{what} are a Claude Code feature; they are not installed."));

        var version = Text(manifest?["version"]);
        if (version?.Length > MaxVersion)
        {
            notes.Add(PluginNotes.Changed("version", $"Longer than {MaxVersion} characters; left out."));
            version = null;
        }
        var description = OneLine(Text(manifest?["description"]));
        if (description.Length > MaxDescription)
        {
            notes.Add(PluginNotes.Changed("description", $"Truncated from {description.Length} to {MaxDescription} characters."));
            description = description[..MaxDescription];
        }
        return PluginReadResult.Translated(new PluginDraft(name, version, description, source, agents, [.. servers.Values], notes));
    }

    /// <summary>The plugin's folder name, or for a source's root the repository or file name.</summary>
    private static string FolderName(PluginSource source)
    {
        var path = source.Path.Length > 0 ? source.Path : source.Location.TrimEnd('/');
        var last = path[(path.LastIndexOf('/') + 1)..];
        return last.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || last.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? last[..^4] : last;
    }
}
