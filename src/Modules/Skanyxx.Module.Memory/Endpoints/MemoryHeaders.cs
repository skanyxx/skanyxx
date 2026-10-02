using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Skanyxx.Module.Memory.Access;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Endpoints;

/// <summary>
/// Identity on <c>/mcp/memory</c>. The agent is the owner of the secret the request authenticated with (D080). The user
/// is <c>X-User-Id</c>, which the agent vouches for (kagent forwards it with <c>allowedHeaders</c>) — honoured only when
/// the owner let that agent act for users, and only when it is a Skanyxx user id (a lowercase GUID); otherwise the call
/// has no user and so no personal scope (D084). REST never reads these.
/// </summary>
public static class MemoryHeaders
{
    public const string UserId = "X-User-Id";

    public static MemoryCaller Agent(HttpContext context)
    {
        var agentId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("/mcp/memory reached without an authenticated agent.");
        var actsForUsers = context.User.HasClaim(AgentSecretAuthentication.ActsForUsersClaim, "true");
        return new(actsForUsers ? SkanyxxUserId(context.Request.Headers[UserId]) : null, agentId);
    }

    private static string? SkanyxxUserId(string? value) =>
        Guid.TryParseExact(value, "D", out var id) && id.ToString() == value ? value : null;
}
