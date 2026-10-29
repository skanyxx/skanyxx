namespace Skanyxx.Module.Quotas.Features;

/// <summary>One limit that applies to a person: <paramref name="Kind"/> is a <see cref="Data.QuotaScopes"/> value.</summary>
public sealed record ScopeLimit(string Kind, string Id, long MonthlyTokens);
