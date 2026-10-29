namespace Skanyxx.Module.Quotas.Contracts;

/// <summary>A team or department a limit can be set on; <see cref="Parent"/> is a team's department.</summary>
public sealed record ScopeOption(string Kind, string Id, string Name, string? Parent);
