using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform.Plugins;
using Skanyxx.Module.Agents.Plugins.Format;
using Skanyxx.Module.Agents.Plugins.Sources;
using Skanyxx.Module.Agents.Studio;

namespace Skanyxx.Module.Agents.Plugins.Features;

/// <summary>
/// Reads what a <see cref="PluginSourceRequest"/> names: the source's marketplace listing, or one plugin translated into
/// studio drafts. A marketplace entry is read from the same source, or, when it pins another git repository, from that
/// repository (one hop: an entry there is not followed further), and only for someone who may fetch git content
/// (<c>mayFollowGit</c>, the owner): the catalog is a builder's, but what an entry pins elsewhere is not (D222). Refusals
/// throw <see cref="PluginSourceException"/>.
/// </summary>
internal sealed class PluginLoader(CatalogPluginSource catalog, GitArchiveSource git, IOptions<StudioOptions> studio, IOptions<PluginsOptions> plugins)
{
    private PluginReaderOptions Options => new(studio.Value.PluginModel, plugins.Value.AllowHttp, plugins.Value.AllowPrivateServers);

    public async Task<PluginReadResult> LoadAsync(PluginSourceRequest request, PluginUpload? upload, bool mayFollowGit, CancellationToken ct)
    {
        var (tree, source) = request.Source switch
        {
            PluginSourceKinds.Catalog => (catalog.Tree, catalog.Root),
            PluginSourceKinds.Git => await FetchAsync(request.Url!, request.Commit!, request.Path ?? "", ct),
            _ => Unzip(upload!, request.Path ?? "")
        };
        var result = PluginReader.Read(tree, source, Options);
        if (result.Problem is { } problem)
            throw new PluginSourceException(problem);
        if (result.Marketplace is not { } marketplace || request.Name is not { } name)
            return result;

        var entry = marketplace.Entries.FirstOrDefault(e => e.Name == name)
            ?? throw new PluginSourceException($"The marketplace '{marketplace.Name}' lists no plugin '{name}'.");
        if (entry.Problem is { } note)
            throw new PluginSourceException($"{entry.Name} is {note.Status}: {note.Reason}");
        if (entry.Git is { } pinned)
        {
            if (!mayFollowGit)
                throw new PluginSourceException($"{entry.Name} is pinned to the git repository {pinned.Url}: only the owner brings in plugins from git.", forbidden: true);
            var (external, at) = await FetchAsync(pinned.Url, pinned.Sha, pinned.Subdir, ct);
            return Plugin(external, at, entry.Name);
        }
        var path = entry.Path!;
        return Plugin(tree, source.Kind == PluginSourceKinds.Catalog ? catalog.Plugin(entry.Name, path) : source with { Path = path }, entry.Name);
    }

    private PluginReadResult Plugin(PluginTree tree, PluginSource source, string entryName)
    {
        var result = PluginReader.ReadPlugin(tree, source, Options, entryName);
        return result.Problem is { } problem ? throw new PluginSourceException(problem) : result;
    }

    private async Task<(PluginTree, PluginSource)> FetchAsync(string url, string sha, string path, CancellationToken ct)
    {
        var archive = await git.FetchAsync(url, sha, ct);
        return (archive.Tree, new PluginSource(PluginSourceKinds.Git, url, sha, path, archive.Digest));
    }

    private static (PluginTree, PluginSource) Unzip(PluginUpload upload, string path)
    {
        var archive = ZipPluginSource.Read(upload.Bytes);
        return (archive.Tree, new PluginSource(PluginSourceKinds.Zip, upload.FileName, null, path, archive.Digest));
    }
}
