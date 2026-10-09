using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Skanyxx.Module.Identity.Accounts;

/// <summary>
/// The one-time links' addresses — the invite accept page, and the password-reset page (D156) — with the token in the
/// query string, not the path: request logs and traces record the path, and Serilog and OpenTelemetry leave the query
/// out (or redact it). Built on the parsed <see cref="IdentityModuleOptions.PublicBaseUrl"/>; only Development without
/// one falls back to the request, so outside it there is no link until the setting exists (<see cref="CanBuild"/>).
/// </summary>
internal sealed class InviteLinks(IOptions<IdentityModuleOptions> options, IHostEnvironment environment, IHttpContextAccessor http)
{
    public const string PagePath = "/Invite";

    public const string ResetPagePath = "/ResetPassword";

    public const string Example = "Set it to the address people use to reach Skanyxx: http://localhost:5282 on a desktop install, " +
        "https://skanyxx.example.com behind a proxy.";

    public const string NotConfigured = "Invite links need Identity:PublicBaseUrl in appsettings.json. " + Example;

    public bool CanBuild => options.Value.PublicBaseUri is not null || environment.IsDevelopment();

    public string For(string token) => $"{BaseUrl()}{PagePath}?token={Uri.EscapeDataString(token)}";

    public string ResetFor(string token) => ResetFor(BaseUrl(), token);

    /// <summary>For a background job, which has no request: <paramref name="baseUrl"/> was taken from <see cref="BaseUrl"/> while it had one.</summary>
    public static string ResetFor(string baseUrl, string token) => $"{baseUrl}{ResetPagePath}?token={Uri.EscapeDataString(token)}";

    public string BaseUrl()
    {
        if (options.Value.PublicBaseUri is { } configured)
            return configured.GetLeftPart(UriPartial.Path).TrimEnd('/');
        if (!environment.IsDevelopment())
            throw new InvalidOperationException(NotConfigured);
        var request = http.HttpContext!.Request;
        return $"{request.Scheme}://{request.Host}{request.PathBase}";
    }
}
