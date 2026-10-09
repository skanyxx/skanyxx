namespace Skanyxx.Core.Platform.Memory;

/// <summary>The agent repo the studio created or adopted (D119): git's own id and creation time, and its name.</summary>
public sealed record StudioRepoIdentity(long Id, string CreatedAt, string FullName);
