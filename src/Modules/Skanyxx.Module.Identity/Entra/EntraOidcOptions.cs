using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Identity.Entra;

/// <summary>
/// Builds the <c>entra</c> scheme's options from <see cref="EntraSettingsCache.Current"/> each time the options cache
/// is emptied (on a settings change). An external login under ASP.NET Core Identity: the handler signs into
/// <see cref="IdentityConstants.ExternalScheme"/> and Skanyxx then issues its own cookie. Code flow with PKCE, claims
/// under their own names. While sign-in is off the options stay valid but inert (a static empty configuration, never
/// fetched): the authentication middleware builds them for every request, and a stray callback just fails. The
/// backchannel (metadata, keys, code redemption) comes from <see cref="IHttpClientFactory"/>: left null, the handler
/// would create a new, never-disposed <see cref="HttpClient"/> on every rebuild (CR L1).
/// </summary>
internal sealed class EntraOidcOptions(
    EntraSettingsCache settings, EntraEndpoints endpoints, EntraRedirectUri redirect, IHttpClientFactory http)
    : IConfigureNamedOptions<OpenIdConnectOptions>
{
    public const string Backchannel = "entra-oidc";

    public void Configure(OpenIdConnectOptions options)
    {
    }

    public void Configure(string? name, OpenIdConnectOptions options)
    {
        if (name != EntraScheme.Name)
            return;

        var config = settings.Current;
        options.SignInScheme = IdentityConstants.ExternalScheme;
        options.CallbackPath = GuardedPaths.ExternalSignInCallback;
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.UsePkce = true;
        options.MapInboundClaims = false;
        options.GetClaimsFromUserInfoEndpoint = false;
        options.SaveTokens = false;
        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");
        options.TokenValidationParameters.NameClaimType = EntraClaims.Name;
        options.Events = new EntraOidcEvents(config.TenantId, redirect);
        options.Backchannel = http.CreateClient(Backchannel);

        if (config.CanSignIn)
        {
            options.Authority = endpoints.Authority(config.TenantId);
            options.ClientId = config.ClientId;
            options.ClientSecret = config.ClientSecret;
        }
        else
        {
            options.ClientId = "microsoft-sign-in-is-off";
            options.Configuration = new OpenIdConnectConfiguration();
        }
    }
}
