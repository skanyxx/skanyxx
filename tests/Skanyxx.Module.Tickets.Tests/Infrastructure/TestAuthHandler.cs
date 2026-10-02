using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Skanyxx.Module.Tickets.Tests.Infrastructure;

/// <summary>
/// Test-only stand-in for the Host's cookie/bearer schemes: the signed-in user and roles come from test headers. No
/// header means no user, so the endpoint's auth requirement answers 401 exactly as it does in the Host.
/// </summary>
public sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string UserHeader = "X-Test-User";
    public const string RolesHeader = "X-Test-Roles";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? user = Request.Headers[UserHeader];
        if (string.IsNullOrEmpty(user))
            return Task.FromResult(AuthenticateResult.NoResult());

        var roles = Request.Headers[RolesHeader].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user), .. roles.Select(r => new Claim(ClaimTypes.Role, r))], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
