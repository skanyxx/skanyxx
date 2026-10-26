using Skanyxx.Core.Platform.Plugins;

namespace Skanyxx.Module.Agents.Plugins.Sources;

/// <summary>
/// The built-in catalog: a marketplace embedded in this module (<c>Plugins/Catalog</c>) of public, no-auth remote MCP
/// servers with an agent each. It ships with the build, so it is trusted as much as the code, and still read by the same
/// reader as any other source.
/// </summary>
internal sealed class CatalogPluginSource
{
    public const string Kind = "catalog";

    private const string ResourcePrefix = "catalog/";

    public CatalogPluginSource()
    {
        var assembly = typeof(CatalogPluginSource).Assembly;
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var resource in assembly.GetManifestResourceNames().Where(r => r.StartsWith(ResourcePrefix, StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            // RecursiveDir in the logical name carries the build machine's separator.
            files.Add(resource[ResourcePrefix.Length..].Replace('\\', '/'), copy.ToArray());
        }
        Tree = new PluginTree(files);
    }

    public PluginTree Tree { get; }

    /// <summary>The catalog marketplace itself.</summary>
    public PluginSource Root => new(Kind, Kind, null, "", Tree.Digest());

    /// <summary>One bundled plugin, by the folder its marketplace entry names.</summary>
    public PluginSource Plugin(string name, string path) => new(Kind, name, null, path, Tree.Sub(path).Digest());
}
