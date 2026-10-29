using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Host.Navigation;

/// <summary>
/// D272 with the switch on the Settings page (D352): while Microsoft sign-in is off, <c>/Entra</c> has no endpoint, so
/// every caller gets exactly what a page that does not exist answers. Read per request: turning it on needs no restart.
/// Between routing and authorization, so the page's owner policy never answers first.
/// </summary>
public static class EntraPageGate
{
    public static IApplicationBuilder UseEntraPageGate(this IApplicationBuilder app)
    {
        var configuration = app.ApplicationServices.GetRequiredService<IConfiguration>();
        return app.Use((context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/Entra", StringComparison.OrdinalIgnoreCase) && !EntraSwitch.IsOn(configuration))
                context.SetEndpoint(null);
            return next(context);
        });
    }
}
