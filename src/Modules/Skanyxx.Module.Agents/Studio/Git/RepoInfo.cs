using Skanyxx.Core.Platform.Memory;

namespace Skanyxx.Module.Agents.Studio.Git;

/// <summary>The agent repo as git describes it: its identity (D119) and whether it is private.</summary>
internal sealed record RepoInfo(StudioRepoIdentity Identity, bool Private);
