using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace Skanyxx.Module.Memory.Endpoints;

/// <summary>
/// Shared settings for every memory endpoint. CORS is off: the Host's AllowAll policy would let any
/// web page drive these endpoints from an employee's browser while identity is a plain header.
/// </summary>
internal sealed class MemoryGroup : Group
{
    /// <summary>
    /// A card is at most ~23k characters (D037 + body/source caps). Default JSON serializers escape non-ASCII
    /// as \uXXXX (6 bytes per character), so a valid max-size Hebrew/CJK card is ~136 KB on the wire.
    /// </summary>
    public const int MaxBodyBytes = 256 * 1024;

    public MemoryGroup() =>
        Configure("api/memory", ep =>
        {
            ep.AllowAnonymous(); // TODO(identity-slice)
            ep.Options(b => b.WithMetadata(new DisableCorsAttribute(), new RequestSizeLimitAttribute(MaxBodyBytes)));
        });
}
