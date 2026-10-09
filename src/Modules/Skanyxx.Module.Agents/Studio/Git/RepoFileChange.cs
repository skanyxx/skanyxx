namespace Skanyxx.Module.Agents.Studio.Git;

/// <param name="Status">git's word for it: added, modified, deleted, renamed…</param>
internal sealed record RepoFileChange(string Path, string Status);
