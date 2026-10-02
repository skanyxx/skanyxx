namespace Skanyxx.Module.Sandboxes.Domain;

/// <summary>A repository AX clones into the workspace. AX has no credential field, so only public https repos work.</summary>
public sealed record GitSource(string Repo, string? Name = null, string? Branch = null, string? Dir = null, int Depth = 0);
