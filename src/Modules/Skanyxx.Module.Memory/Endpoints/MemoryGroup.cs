using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace Skanyxx.Module.Memory.Endpoints;

/// <summary>
/// Shared settings for every memory endpoint: a signed-in user is required (FastEndpoints' default, against the
/// Host's authentication schemes), CORS stays off so no other origin can drive them with the user's cookie, and the
/// body is capped.
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
            ep.Options(b => b.WithMetadata(new DisableCorsAttribute(), new RequestSizeLimitAttribute(MaxBodyBytes))));
}
