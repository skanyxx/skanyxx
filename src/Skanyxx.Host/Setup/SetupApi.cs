using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Skanyxx.Host.Setup;

/// <summary><c>/api/setup/guides</c>: the guides and what this install is missing, for the owner and supervisors only.</summary>
public static class SetupApi
{
    public static IEndpointRouteBuilder MapSetupGuides(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/setup/guides").RequireAuthorization(p => p.RequireRole(SetupAccess.Roles.Split(',')));
        group.MapGet("", (SetupInspector inspector) => SetupGuides.All
            .Select(g => (Guide: g, State: inspector.Inspect(g)))
            .Select(x => new { x.Guide.Id, x.Guide.Title, x.State.Ready, Missing = x.State.Missing.Select(s => s.Name) }));
        group.MapGet("/{id}", (string id, SetupInspector inspector) =>
            SetupGuides.Find(id) is { } guide ? Results.Ok(View(guide, inspector.Inspect(guide))) : Results.NotFound());
        return app;
    }

    private static SetupGuideView View(SetupGuide guide, SetupState state) => new(guide, state.Ready, state, AgentPrompt.For(guide, state));
}
