using Skanyxx.Core.Platform.Plugins;

namespace Skanyxx.Module.Agents.Plugins.Features;

/// <param name="Target">What to post to <c>inspect</c> (and, with the digest it answers, to <c>install</c>); null when <see cref="Problem"/> says why it cannot be installed.</param>
internal sealed record PluginListingEntry(string Name, string Description, string? Version, PluginSourceRequest? Target, PluginNote? Problem);
