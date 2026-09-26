using Microsoft.AspNetCore.Http;

namespace Skanyxx.Module.Sandboxes.Endpoints;

/// <summary>Identity from the header only — never from a bound DTO. TODO(identity-slice): authenticated claims.</summary>
internal static class SandboxesHeaders
{
    public const string UserId = "X-User-Id";

    public static string? User(HttpContext context)
    {
        string? value = context.Request.Headers[UserId];
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
