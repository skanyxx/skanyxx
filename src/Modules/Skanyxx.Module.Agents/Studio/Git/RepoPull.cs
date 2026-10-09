namespace Skanyxx.Module.Agents.Studio.Git;

/// <param name="BaseRef">The branch it would merge into; only one into main is a proposal (m7).</param>
internal sealed record RepoPull(int Number, string Title, string Body, string BaseRef, string HeadRef, string HeadSha, bool Open, bool Merged, bool Mergeable);
