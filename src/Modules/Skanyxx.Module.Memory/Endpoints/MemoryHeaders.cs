using Microsoft.AspNetCore.Http;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Endpoints;

/// <summary>
/// Identity is read from these headers only — never from a bound request DTO, which FastEndpoints
/// also fills from the query string and body. TODO(identity-slice): replace with authenticated claims.
/// </summary>
public static class MemoryHeaders
{
    public const string UserId = "X-User-Id";
    public const string AgentId = "X-Agent-Id";

    /// <summary>REST callers are always treated as humans; agents go through MCP.</summary>
    public static Caller Human(HttpContext context) => new(Read(context, UserId), null);

    public static Caller Agent(HttpContext context) => new(Read(context, UserId), Read(context, AgentId));

    private static string? Read(HttpContext context, string header)
    {
        string? value = context.Request.Headers[header];
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
