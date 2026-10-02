using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Identity.Entra;

/// <summary>
/// The three places the Microsoft sign-in needs more than the handler's defaults: the registered redirect URI, the
/// tenant pin and account key, and a failed callback.
/// </summary>
internal sealed class EntraOidcEvents(string tenantId, EntraRedirectUri redirect) : OpenIdConnectEvents
{
    public override Task RedirectToIdentityProvider(RedirectContext context)
    {
        if (redirect.Callback is { } callback)
            context.ProtocolMessage.RedirectUri = callback;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Only this tenant's members and guests get further (the issuer is already this tenant's; <c>tid</c> is checked as
    /// well, as Microsoft asks). The account key is set explicitly to <c>tid|oid</c>: Identity keys external logins on
    /// the NameIdentifier claim, falling back to <c>sub</c> (per-app pairwise), and never on email.
    /// </summary>
    public override Task TokenValidated(TokenValidatedContext context)
    {
        var identity = (ClaimsIdentity)context.Principal!.Identity!;
        if (!Guid.TryParse(identity.FindFirst(EntraClaims.TenantId)?.Value, out var tid) || !Guid.TryParse(tenantId, out var pinned) || tid != pinned)
        {
            context.Fail("The token is not from the configured tenant.");
            return Task.CompletedTask;
        }
        if (!Guid.TryParse(identity.FindFirst(EntraClaims.ObjectId)?.Value, out var oid))
        {
            context.Fail("The token has no object id (oid).");
            return Task.CompletedTask;
        }

        foreach (var claim in identity.FindAll(ClaimTypes.NameIdentifier).ToList())
            identity.RemoveClaim(claim);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, EntraClaims.Key(tid, oid)));
        return Task.CompletedTask;
    }

    /// <summary>
    /// A refused or broken callback (wrong tenant, cancelled at Microsoft, an expired or replayed state) goes back to the
    /// page that started it — its completion finds no Microsoft sign-in and says so. A failure raised by an event (the
    /// tenant pin) carries no properties, so they are read back from the <c>state</c>, which only this app can have
    /// protected; with no readable state, the sign-in completion.
    /// </summary>
    public override async Task RemoteFailure(RemoteFailureContext context)
    {
        context.HttpContext.RequestServices.GetRequiredService<ILogger<EntraOidcEvents>>().LogWarning(
            "Microsoft sign-in callback refused from {RemoteIp}: {Reason}", context.HttpContext.Connection.RemoteIpAddress, context.Failure?.Message);
        var properties = context.Properties ?? await StateOfAsync(context);
        context.Response.Redirect(properties?.RedirectUri ?? SignInCompletion);
        context.HandleResponse();
    }

    private const string SignInCompletion = AuthenticationExtensions.LoginPath + "?handler=Microsoft";

    private static async Task<AuthenticationProperties?> StateOfAsync(RemoteFailureContext context)
    {
        var request = context.Request;
        var state = (request.HasFormContentType ? (await request.ReadFormAsync(context.HttpContext.RequestAborted))["state"] : request.Query["state"]).ToString();
        return string.IsNullOrEmpty(state) ? null : ((OpenIdConnectOptions)context.Options).StateDataFormat.Unprotect(state);
    }
}
