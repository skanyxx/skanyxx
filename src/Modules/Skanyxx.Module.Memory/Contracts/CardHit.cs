namespace Skanyxx.Module.Memory.Contracts;

/// <summary>What an agent sees (D034): no id, no body, no source.</summary>
public sealed record CardHit(
    string Scope, string Key, int Version, string Type, string What, string Why, string Who, DateTime UpdatedAt);
