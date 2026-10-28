namespace Skanyxx.Host.Navigation;

/// <summary>A titled group of links in the sidebar (Pinned, Work, Admin).</summary>
public sealed record NavSection(string Title, IReadOnlyList<NavLink> Links);
