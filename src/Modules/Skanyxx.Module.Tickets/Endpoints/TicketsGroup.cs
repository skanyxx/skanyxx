using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace Skanyxx.Module.Tickets.Endpoints;

/// <summary>
/// Shared settings for every tickets endpoint: a signed-in user is required (FastEndpoints' default, against the
/// Host's authentication schemes), CORS stays off so no other origin can drive them with the user's cookie, and the
/// body is capped.
/// </summary>
internal sealed class TicketsGroup : Group
{
    /// <summary>A pipeline of 20 stages × 8k instructions, JSON-escaped, fits well inside this.</summary>
    public const int MaxBodyBytes = 1024 * 1024;

    public TicketsGroup() =>
        Configure("api/tickets", ep =>
            ep.Options(b => b.WithMetadata(new DisableCorsAttribute(), new RequestSizeLimitAttribute(MaxBodyBytes))));
}
