using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Skanyxx.Module.Agents.Plugins.Features;

namespace Skanyxx.Module.Agents.Plugins.Endpoints;

/// <summary>Turns a zip upload into a source request: the bytes, and a file name safe to keep as the plugin's provenance.</summary>
internal static partial class PluginUploads
{
    public const string TooLarge = "The zip is larger than 10 MB.";

    private const int MaxFileName = 100;

    public static async Task<(PluginSourceRequest Request, PluginUpload? Upload)> ReadAsync(PluginZipRequest req, CancellationToken ct)
    {
        var request = new PluginSourceRequest { Source = PluginSourceKinds.Zip, Name = req.Name, Path = req.Path, Digest = req.Digest };
        if (req.File is null)
            return (request, null);
        using var buffer = new MemoryStream((int)req.File.Length);
        await req.File.CopyToAsync(buffer, ct);
        return (request, new PluginUpload(FileName(req.File.FileName), buffer.ToArray()));
    }

    /// <summary>The upload's own name is untrusted: its last segment, only [A-Za-z0-9._-], at most 100 characters.</summary>
    private static string FileName(string raw)
    {
        var name = Unsafe().Replace(raw[(raw.LastIndexOfAny(['/', '\\']) + 1)..], "-").Trim('-', '.');
        return name.Length == 0 ? "upload.zip" : name[..Math.Min(name.Length, MaxFileName)];
    }

    [GeneratedRegex("[^A-Za-z0-9._-]+")]
    private static partial Regex Unsafe();
}
