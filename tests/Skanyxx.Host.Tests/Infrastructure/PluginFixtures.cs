using System.IO.Compression;
using System.Text;
using Skanyxx.Core.Platform.Plugins;
using Skanyxx.Module.Agents.Plugins;
using Skanyxx.Module.Agents.Plugins.Format;

namespace Skanyxx.Host.Tests.Infrastructure;

/// <summary>Plugin trees for the reader tests: the fixture folders (real plugins' files) or files given inline.</summary>
internal static class PluginFixtures
{
    public static readonly PluginReaderOptions Options = new("default-model-config", AllowHttp: false);

    public static PluginTree Folder(string name)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Fixtures", "plugins", name);
        return new PluginTree(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(root, f).Replace('\\', '/'), File.ReadAllBytes, StringComparer.Ordinal));
    }

    public static PluginTree Tree(params (string Path, string Text)[] files) =>
        new(files.ToDictionary(f => f.Path, f => Encoding.UTF8.GetBytes(f.Text), StringComparer.Ordinal));

    public static PluginSource Source(string path = "", string location = "upload.zip") => new("zip", location, null, path, "digest");

    /// <summary>The fixture as a plugin draft, failing when the reader refused it outright.</summary>
    public static PluginDraft Plugin(string name, PluginReaderOptions? options = null) =>
        PluginReader.Read(Folder(name), Source(location: name + ".zip"), options ?? Options).Plugin
        ?? throw new InvalidOperationException($"{name} was not read as a plugin.");

    public static PluginDraft Plugin(PluginTree tree, PluginReaderOptions? options = null)
    {
        var result = PluginReader.Read(tree, Source(location: "inline.zip"), options ?? Options);
        return result.Plugin ?? throw new InvalidOperationException(result.Problem ?? "a marketplace");
    }

    public static byte[] Zip(params (string Path, byte[] Bytes)[] files) => Zip(files.Select(f => (f.Path, f.Bytes, (int?)null)).ToArray());

    /// <summary>A zip; <c>Unix</c> is the entry's Unix mode (e.g. 0xA1FF for a symlink).</summary>
    public static byte[] Zip((string Path, byte[] Bytes, int? Unix)[] files)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (path, bytes, unix) in files)
            {
                var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
                if (unix is { } mode)
                    entry.ExternalAttributes = mode << 16;
                using var stream = entry.Open();
                stream.Write(bytes);
            }
        return buffer.ToArray();
    }

    public static byte[] Text(string text) => Encoding.UTF8.GetBytes(text);
}
