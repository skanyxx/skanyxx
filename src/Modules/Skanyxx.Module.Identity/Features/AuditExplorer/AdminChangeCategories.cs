namespace Skanyxx.Module.Identity.Features.AuditExplorer;

/// <summary>
/// Which successful actions are an admin change, and of what: roles, people, agents, models, tool servers, the org tree
/// or the Microsoft sign-in settings. Models and tool servers are matched by area so their rows count as they arrive;
/// an intent row (<c>*_requested</c>) is not a change.
/// </summary>
internal static class AdminChangeCategories
{
    public static readonly IReadOnlyList<string> All = ["roles", "people", "agents", "models", "toolServers", "org", "signInSettings"];

    public static string? Of(string action)
    {
        if (action.EndsWith("_requested", StringComparison.Ordinal))
            return null;
        return action switch
        {
            "person.roles_changed" => "roles",
            "person.disabled" or "person.enabled" or "person.entra_login_removed" or "invite.created" or "invite.revoked" => "people",
            "agent.created" or "agent.updated" or "agent.deleted" or "agent.suspended" or "agent.resumed" => "agents",
            "entra.settings_saved" => "signInSettings",
            _ => action.Split('.')[0] switch
            {
                "model" or "models" => "models",
                "toolserver" or "toolservers" or "tool_server" => "toolServers",
                "org" => "org",
                _ => null
            }
        };
    }
}
