namespace Skanyxx.Module.Agents.Plugins.Features;

/// <summary>
/// Where to read a plugin from (the body of <c>api/plugins/inspect</c> and <c>install</c>). <see cref="Source"/> is
/// <c>catalog</c>, <c>git</c> (<see cref="Url"/> and the full <see cref="Commit"/> SHA, <see cref="Path"/> the folder in it)
/// or <c>zip</c> (the upload, <see cref="Path"/> the folder in it). <see cref="Name"/> picks one plugin when the source is a
/// marketplace. <see cref="Digest"/> is what inspect answered; install refuses a source that no longer matches it (P12).
/// </summary>
internal sealed class PluginSourceRequest
{
    public string Source { get; set; } = "";

    public string? Name { get; set; }

    public string? Url { get; set; }

    public string? Commit { get; set; }

    public string? Path { get; set; }

    public string? Digest { get; set; }

    /// <summary>The same source narrowed to one marketplace entry: what the page posts to inspect or install that entry.</summary>
    public PluginSourceRequest Entry(string name) => new() { Source = Source, Name = name, Url = Url, Commit = Commit, Path = Path };
}
