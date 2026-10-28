using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Core.Platform.Preferences;

namespace Skanyxx.Host.Navigation;

/// <summary>
/// Every panel, grouped by what people come to do: Work (everyone, by role) and Admin (running the installation).
/// A panel whose page is not in this build (Dashboard, Investigate, Analytics, Settings until they are rebuilt) has no
/// address, so it is left out until the page exists. A person's pins and order rearrange only what their roles already
/// allow: a stored key for a panel they cannot use is ignored.
/// </summary>
public static class NavCatalog
{
    public const string Work = "Work";
    public const string Admin = "Admin";
    public const string Pinned = "Pinned";

    private static readonly string[] Builders = [SkanyxxRoles.Owner, SkanyxxRoles.Supervisor, SkanyxxRoles.Builder];
    private static readonly string[] Supervisors = [SkanyxxRoles.Owner, SkanyxxRoles.Supervisor];
    private static readonly string[] OwnerOnly = [SkanyxxRoles.Owner];

    public static readonly IReadOnlyList<NavItem> Items =
    [
        new("chat", "/Chat", "Chat", "i-chat", Work, FullBleed: true),
        new("library", "/Library", "Library", "i-library", Work),
        new("agents", "/Agents", "Agents", "i-bot", Work),
        new("studio", "/Studio", "Studio", "i-studio", Work, Builders),
        new("plugins", "/Plugins", "Plugins", "i-puzzle", Work, Builders),
        new("investigate", "/Investigate", "Investigate", "i-search", Work, Builders),
        new("sandboxes", "/Sandboxes", "Sandboxes", "i-sandbox", Work, Supervisors),

        new("dashboard", "/Dashboard", "Dashboard", "i-dashboard", Admin, OwnerOnly),
        new("models", "/Model", "Models", "i-cpu", Admin, OwnerOnly),
        new("toolservers", "/ToolServers", "Tool servers", "i-server", Admin, Builders),
        new("people", "/People", "People", "i-users", Admin, OwnerOnly),
        new("org", "/Org", "Org", "i-org", Admin, OwnerOnly),
        new("entra", "/Entra", "Microsoft sign-in", "i-signin", Admin, OwnerOnly, NeedsEntra: true),
        new("audit", "/Audit", "Audit", "i-audit", Admin, OwnerOnly),
        new("analytics", "/Analytics", "Analytics", "i-chart", Admin, Supervisors),
        new("settings", "/Settings", "Settings", "i-settings", Admin, OwnerOnly),
        new("debug", "/Debug", "Debug", "i-terminal", Admin, OwnerOnly),
    ];

    public static bool Allows(NavItem item, ClaimsPrincipal user, IConfiguration configuration) =>
        user.Identity?.IsAuthenticated == true
        && (item.Roles is null || item.Roles.Any(user.IsInRole))
        && (!item.NeedsEntra || EntraSwitch.IsOn(configuration));

    /// <summary>The panels this person may open whose pages exist, in catalog order.</summary>
    public static IReadOnlyList<NavLink> LinksFor(ClaimsPrincipal user, IConfiguration configuration, IUrlHelper url, string currentPage) =>
        [.. Items.Where(i => Allows(i, user, configuration))
            .Select(i => (Item: i, Href: url.Page(i.Page)))
            .Where(x => x.Href is not null)
            .Select(x => new NavLink(x.Item, x.Href!, string.Equals(x.Item.Page, currentPage, StringComparison.OrdinalIgnoreCase)))];

    /// <summary>The sidebar: pinned panels first, then Work and Admin, each in the person's order.</summary>
    public static IReadOnlyList<NavSection> Sections(IReadOnlyList<NavLink> links, EffectivePreferences preferences)
    {
        var rank = preferences.NavOrder.Select((key, index) => (key, index)).ToDictionary(x => x.key, x => x.index, StringComparer.Ordinal);
        // OrderBy is stable: panels the person has not ordered keep catalog order, after the ordered ones.
        IReadOnlyList<NavLink> Ordered(IEnumerable<NavLink> group) => [.. group.OrderBy(l => rank.GetValueOrDefault(l.Item.Key, int.MaxValue))];

        var pinned = preferences.PinnedNav.ToHashSet(StringComparer.Ordinal);
        var sections = new List<NavSection>
        {
            new(Pinned, Ordered(links.Where(l => pinned.Contains(l.Item.Key)))),
            new(Work, Ordered(links.Where(l => l.Item.Group == Work && !pinned.Contains(l.Item.Key)))),
            new(Admin, Ordered(links.Where(l => l.Item.Group == Admin && !pinned.Contains(l.Item.Key))))
        };
        return [.. sections.Where(s => s.Links.Count > 0)];
    }

    public static NavItem? ForPage(string page) =>
        Items.FirstOrDefault(i => string.Equals(i.Page, page, StringComparison.OrdinalIgnoreCase));
}
