namespace Skanyxx.Core.Platform.Studio;

/// <summary>A memory grant in an agent's <c>grants.yaml</c> (D033): <c>company</c>, <c>team:x</c> or <c>department:x</c>.</summary>
public sealed record StudioGrant(string Scope, bool Search, bool Upsert);
