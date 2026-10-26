using System.Security.Cryptography;
using System.Text;

namespace Skanyxx.Module.Agents.Plugins;

/// <summary>
/// The files of a plugin source, read-only: relative '/'-separated paths (no leading '/', no '.' or '..' segments) to
/// their bytes. Sources build it after checking every path; the reader only looks things up.
/// </summary>
internal sealed class PluginTree(IReadOnlyDictionary<string, byte[]> files)
{
    public IEnumerable<string> Paths => files.Keys;

    public int Count => files.Count;

    public byte[]? Read(string path) => files.GetValueOrDefault(path);

    public bool HasFolder(string folder) => folder.Length == 0 || files.Keys.Any(p => p.StartsWith(folder + "/", StringComparison.Ordinal));

    /// <summary>The files under <paramref name="folder"/>, with paths relative to it; the tree itself for "".</summary>
    public PluginTree Sub(string folder)
    {
        if (folder.Length == 0)
            return this;
        var prefix = folder + "/";
        return new(files.Where(f => f.Key.StartsWith(prefix, StringComparison.Ordinal))
            .ToDictionary(f => f.Key[prefix.Length..], f => f.Value, StringComparer.Ordinal));
    }

    /// <summary>SHA-256 (lowercase hex) over every path and its bytes, in ordinal path order.</summary>
    public string Digest()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[8];
        foreach (var (path, bytes) in files.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(path));
            System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(length, bytes.Length);
            hash.AppendData([0]);
            hash.AppendData(length);
            hash.AppendData(bytes);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
