using System.IO.Compression;
using System.Security.Cryptography;

namespace Skanyxx.Module.Agents.Plugins.Sources;

/// <summary>
/// A plugin source from a zip (an upload, or a git host's archive of a commit) as a <see cref="PluginTree"/>. The zip is
/// untrusted: entry count, expanded size and each file's size are bounded by what is actually decompressed, and an
/// absolute path, a '..' or '\' in a path, or a symlink refuses the whole zip.
/// </summary>
internal static class ZipPluginSource
{
    public const int MaxEntries = 2_000;

    public const long MaxExpandedBytes = 50L * 1024 * 1024;

    /// <summary>Larger files (images, binaries in a repository archive) are skipped; nothing a plugin reads is this big.</summary>
    public const int MaxFileBytes = 1024 * 1024;

    private const int SymlinkType = 0xA000;

    // Plugin layout folders: a zip whose only top folder is one of these is a plugin root, not a wrapper to strip.
    private static readonly HashSet<string> LayoutFolders = [".claude-plugin", "agents", "skills", "commands", "hooks"];

    /// <summary>Reads at most <paramref name="maxBytes"/> of <paramref name="zip"/>, then the zip.</summary>
    public static async Task<PluginArchive> ReadAsync(Stream zip, long maxBytes, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81_920];
        int read;
        while ((read = await zip.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > maxBytes)
                throw new PluginSourceException($"The zip is larger than {maxBytes / (1024 * 1024)} MB.");
            buffer.Write(chunk, 0, read);
        }
        // The buffer itself, not a copy of it: a download is held in memory once.
        return Read(new ArraySegment<byte>(buffer.GetBuffer(), 0, (int)buffer.Length));
    }

    /// <summary>The files of <paramref name="zip"/>; the digest is the SHA-256 of its bytes.</summary>
    public static PluginArchive Read(ArraySegment<byte> zip) => new(Tree(zip), Convert.ToHexStringLower(SHA256.HashData(zip.AsSpan())));

    public static PluginTree Tree(ArraySegment<byte> zip)
    {
        ZipArchive archive;
        try
        {
            archive = new ZipArchive(new MemoryStream(zip.Array!, zip.Offset, zip.Count, writable: false), ZipArchiveMode.Read);
        }
        catch (InvalidDataException)
        {
            throw new PluginSourceException("Not a zip file.");
        }
        using (archive)
        {
            if (archive.Entries.Count > MaxEntries)
                throw new PluginSourceException($"The zip has {archive.Entries.Count} entries; at most {MaxEntries}.");

            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            long expanded = 0;
            foreach (var entry in archive.Entries)
            {
                var path = entry.FullName;
                if (path.StartsWith("__MACOSX/", StringComparison.Ordinal))
                    continue;
                CheckPath(path);
                if ((entry.ExternalAttributes >> 16 & 0xF000) == SymlinkType)
                    throw new PluginSourceException($"'{path}' is a symbolic link; links are not accepted.");
                if (path.EndsWith('/') || entry.Length > MaxFileBytes)
                    continue;

                var bytes = Expand(entry, path);
                if ((expanded += bytes.Length) > MaxExpandedBytes)
                    throw new PluginSourceException($"The zip expands to more than {MaxExpandedBytes / (1024 * 1024)} MB.");
                if (!files.TryAdd(path, bytes))
                    throw new PluginSourceException($"'{path}' is in the zip twice.");
            }
            return new PluginTree(StripTopFolder(files));
        }
    }

    private static void CheckPath(string path)
    {
        if (path.Length == 0 || path.StartsWith('/') || path.Contains('\\') || path.Contains(':') || path.Any(char.IsControl))
            throw new PluginSourceException($"'{path}' is not a relative path; absolute paths, '\\', ':' and control characters are refused.");
        if (path.TrimEnd('/').Split('/').Any(s => s is "" or "." or ".."))
            throw new PluginSourceException($"'{path}' leaves its folder ('..', '.' or an empty segment); refused.");
    }

    /// <summary>
    /// The entry's bytes, capped here whatever the entry declares. (.NET's entry stream also stops at the declared size,
    /// so a lying header yields a short file; this cap does not depend on that.)
    /// </summary>
    private static byte[] Expand(ZipArchiveEntry entry, string path)
    {
        try
        {
            using var stream = entry.Open();
            using var output = new MemoryStream();
            var chunk = new byte[81_920];
            int read;
            while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
            {
                if (output.Length + read > MaxFileBytes)
                    throw new PluginSourceException($"'{path}' expands past {MaxFileBytes / 1024} KB; refused.");
                output.Write(chunk, 0, read);
            }
            return output.ToArray();
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
        {
            throw new PluginSourceException($"'{path}' cannot be read from the zip ({ex.Message}).");
        }
    }

    /// <summary>GitHub and most zip tools wrap everything in one folder (<c>repo-&lt;sha&gt;/</c>); the plugin is inside it.</summary>
    private static Dictionary<string, byte[]> StripTopFolder(Dictionary<string, byte[]> files)
    {
        var tops = files.Keys.Select(p => p.Contains('/') ? p[..p.IndexOf('/')] : null).Distinct().ToList();
        if (tops is not [{ } top] || LayoutFolders.Contains(top))
            return files;
        return files.ToDictionary(f => f.Key[(top.Length + 1)..], f => f.Value, StringComparer.Ordinal);
    }
}
