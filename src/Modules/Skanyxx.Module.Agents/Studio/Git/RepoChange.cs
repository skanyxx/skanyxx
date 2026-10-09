namespace Skanyxx.Module.Agents.Studio.Git;

/// <param name="Exists">The path is already in main (an update), else it is created.</param>
internal sealed record RepoChange(string Path, string Content, bool Exists);
