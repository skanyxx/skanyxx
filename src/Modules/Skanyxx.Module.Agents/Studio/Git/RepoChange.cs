namespace Skanyxx.Module.Agents.Studio.Git;

/// <param name="Content">The new text; null deletes the path (only a plugin proposal does, P8).</param>
/// <param name="Exists">The path is already in main (an update or a delete), else it is created.</param>
internal sealed record RepoChange(string Path, string? Content, bool Exists)
{
    public static RepoChange Delete(string path) => new(path, null, true);
}
