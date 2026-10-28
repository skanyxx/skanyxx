namespace Skanyxx.Host.Navigation;

/// <summary>
/// One panel in the sidebar. <paramref name="Roles"/> null = everyone signed in; otherwise any one of them. The page's
/// own <c>[Authorize]</c> is still the control (requirements §2): this only keeps a panel out of the nav of a role that
/// cannot use it. <paramref name="FullBleed"/> pages (Chat) fill the viewport, so the layout puts their title in the top
/// bar instead of a page header.
/// </summary>
public sealed record NavItem(
    string Key,
    string Page,
    string Label,
    string Icon,
    string Group,
    IReadOnlyList<string>? Roles = null,
    bool FullBleed = false,
    bool NeedsEntra = false);
