using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Identity.Entra;

/// <summary>
/// Where Entra sends the browser back: <c>Identity:PublicBaseUrl</c> + <see cref="GuardedPaths.ExternalSignInCallback"/>,
/// as invite links are built (<see cref="Accounts.InviteLinks"/>). Behind a TLS-terminating proxy the request says
/// <c>http</c> and an internal host, which Entra would refuse (AADSTS50011) or send the code to in clear. Only
/// Development without the setting falls back to the request (<see cref="Callback"/> null).
/// </summary>
internal sealed class EntraRedirectUri(IOptions<IdentityModuleOptions> options, IHostEnvironment environment)
{
    public const string NotConfigured = "Microsoft sign-in needs Identity:PublicBaseUrl in appsettings.json: Entra sends people back to " +
        "<PublicBaseUrl>" + GuardedPaths.ExternalSignInCallback + ", the redirect URI you register in the app registration.";

    public bool CanBuild => options.Value.PublicBaseUri is not null || environment.IsDevelopment();

    public string? Callback => options.Value.PublicBaseUri is { } configured
        ? configured.GetLeftPart(UriPartial.Path).TrimEnd('/') + GuardedPaths.ExternalSignInCallback
        : null;
}
