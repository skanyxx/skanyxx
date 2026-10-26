namespace Skanyxx.Module.Agents.Plugins.Sources;

/// <summary>
/// A source refused or could not be read; the message is the reason, in words for the person adding it.
/// <see cref="Upstream"/>: the git host failed (unreachable, an error status, too slow), not the request.
/// <see cref="Forbidden"/>: reading on would fetch content the person may not bring in (a catalog entry pinned to a git
/// repository is third-party content, the owner's, D222).
/// </summary>
internal sealed class PluginSourceException(string reason, bool upstream = false, bool forbidden = false) : Exception(reason)
{
    public bool Upstream { get; } = upstream;

    public bool Forbidden { get; } = forbidden;
}
