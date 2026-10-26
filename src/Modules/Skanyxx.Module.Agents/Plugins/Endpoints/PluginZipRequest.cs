using Microsoft.AspNetCore.Http;

namespace Skanyxx.Module.Agents.Plugins.Endpoints;

/// <summary>A multipart upload: the zip (<c>file</c>), and the folder, marketplace entry and digest fields of a zip source.</summary>
internal sealed class PluginZipRequest
{
    public IFormFile? File { get; set; }

    public string? Name { get; set; }

    public string? Path { get; set; }

    public string? Digest { get; set; }
}
