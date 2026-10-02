using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Hosting;

namespace Skanyxx.Core.Platform;

/// <summary>
/// One sign-in, two carriers: the browser gets the Identity cookie, API and desktop clients a bearer token
/// (ASP.NET Core Identity's own schemes; the identity module issues both). Every endpoint requires a signed-in user
/// unless it opts out with <c>AllowAnonymous</c>.
/// </summary>
public static class AuthenticationExtensions
{
    /// <summary>Bearer when the request carries <c>Authorization</c>, otherwise the cookie.</summary>
    public const string Scheme = "Skanyxx";

    public const string LoginPath = "/Login";

    private const string McpPath = "/mcp";

    public static IServiceCollection AddSkanyxxAuthentication(this IServiceCollection services, IHostEnvironment environment)
    {
        services.AddAuthentication(Scheme)
            .AddPolicyScheme(Scheme, null, o =>
            {
                // /mcp carries agent secrets, which the user bearer handler would try (and fail) to unprotect on every
                // call; its endpoints authenticate with their own scheme, so the cookie's cheap no-result is enough here.
                // Every /mcp endpoint must therefore name its own scheme: one left on this default would accept a
                // browser cookie and refuse a user bearer token.
                o.ForwardDefaultSelector = context =>
                    context.Request.Headers.Authorization.Count > 0 && !context.Request.Path.StartsWithSegments(McpPath)
                        ? IdentityConstants.BearerScheme
                        : IdentityConstants.ApplicationScheme;
                // Challenges and forbids always go to the cookie handler: it is the one that answers an API
                // caller with a ProblemDetails 401/403 and a page with a redirect to the login page.
                o.ForwardChallenge = IdentityConstants.ApplicationScheme;
                o.ForwardForbid = IdentityConstants.ApplicationScheme;
            })
            .AddBearerToken(IdentityConstants.BearerScheme)
            .AddIdentityCookies();

        services.ConfigureApplicationCookie(o =>
        {
            o.Cookie.Name = "skanyxx.auth";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Lax;
            o.Cookie.SecurePolicy = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            o.ExpireTimeSpan = TimeSpan.FromHours(8);
            o.SlidingExpiration = true;
            o.LoginPath = LoginPath;
            o.AccessDeniedPath = LoginPath;
            o.Events.OnRedirectToLogin = context => ApiProblemOr(context, StatusCodes.Status401Unauthorized, "Sign in first.");
            o.Events.OnRedirectToAccessDenied = context => ApiProblemOr(context, StatusCodes.Status403Forbidden, "Not allowed.");
        });

        services.AddAuthorization(o => o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        return services;
    }

    private static Task ApiProblemOr(RedirectContext<CookieAuthenticationOptions> context, int status, string detail)
    {
        if (!PlatformExtensions.IsApi(context.Request.Path))
        {
            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        }

        return Results.Problem(detail, statusCode: status).ExecuteAsync(context.HttpContext);
    }
}
