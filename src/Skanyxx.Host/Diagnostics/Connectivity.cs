namespace Skanyxx.Host.Diagnostics;

/// <summary>One dependency's check on the Debug page: a health status word, or "not configured" when the feature is off.</summary>
public sealed record Connectivity(string Name, string Status, string? Detail, int? Milliseconds);
