using Skanyxx.Core.Platform.Plugins;

namespace Skanyxx.Module.Agents.Plugins.Format;

/// <summary>
/// One plugin a marketplace lists, and where to install it from: <see cref="Path"/>, its folder in the same source
/// (relative to the source root), or <see cref="Git"/>, another repository pinned to a commit. When neither is set,
/// <see cref="Problem"/> says why it cannot be installed (<c>unsupported</c> or <c>refused</c>).
/// </summary>
internal sealed record MarketplaceEntry(
    string Name,
    string Description,
    string? Version,
    string? Path,
    PinnedGitSource? Git,
    PluginNote? Problem);
