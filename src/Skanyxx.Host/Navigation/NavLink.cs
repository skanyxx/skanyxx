namespace Skanyxx.Host.Navigation;

/// <summary>A <see cref="NavItem"/> this person may see, with its resolved address.</summary>
public sealed record NavLink(NavItem Item, string Href, bool Active);
