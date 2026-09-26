using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace Skanyxx.Module.Tickets.Endpoints;

/// <summary>
/// Shared settings for every tickets endpoint. CORS is off for the same reason as memory: identity is a plain
/// header until the identity slice, so no web page may drive these from an employee's browser.
/// </summary>
internal sealed class TicketsGroup : Group
{
    /// <summary>A pipeline of 20 stages × 8k instructions, JSON-escaped, fits well inside this.</summary>
    public const int MaxBodyBytes = 1024 * 1024;

    public TicketsGroup() =>
        Configure("api/tickets", ep =>
        {
            ep.AllowAnonymous(); // TODO(identity-slice)
            ep.Options(b => b.WithMetadata(new DisableCorsAttribute(), new RequestSizeLimitAttribute(MaxBodyBytes)));
        });
}
