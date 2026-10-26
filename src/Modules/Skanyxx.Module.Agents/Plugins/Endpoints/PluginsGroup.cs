using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace Skanyxx.Module.Agents.Plugins.Endpoints;

/// <summary>
/// Every plugins endpoint: a signed-in user (FastEndpoints' default), CORS off so no other origin drives them with the
/// person's cookie, and a small body; the two zip uploads take <see cref="MaxUploadBytes"/>. The roles on each endpoint
/// are the handlers' rules again (P11), so a refused person never reaches a git download.
/// </summary>
internal sealed class PluginsGroup : Group
{
    public const string Prefix = "api/plugins";

    public const int MaxBodyBytes = 64 * 1024;

    /// <summary>The largest zip accepted (P9).</summary>
    public const int MaxZipBytes = 10 * 1024 * 1024;

    /// <summary>The zip plus the multipart framing and the few form fields around it.</summary>
    public const int MaxUploadBytes = MaxZipBytes + 64 * 1024;

    public PluginsGroup() => Configure(Prefix, ep => Apply(ep, MaxBodyBytes));

    /// <summary>
    /// Also applied directly by the endpoints at the prefix itself (a group route <c>""</c> comes out as
    /// <c>api/plugins/</c>, D179) and by the uploads, whose limit differs.
    /// </summary>
    internal static void Apply(EndpointDefinition ep, long maxBodyBytes) =>
        ep.Options(b => b.WithMetadata(new DisableCorsAttribute(), new RequestSizeLimitAttribute(maxBodyBytes)));
}
