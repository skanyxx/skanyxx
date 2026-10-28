namespace Skanyxx.Core.Platform.Memory;

/// <param name="Scope">Memory's scope grammar; <c>personal</c> means the calling person's own.</param>
public sealed record AgentMemoryGrant(string Scope, bool Search, bool Upsert);
