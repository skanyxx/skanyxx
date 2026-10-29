namespace Skanyxx.Module.Quotas.Contracts;

/// <summary>A limit after a change; null <see cref="MonthlyTokens"/> = no limit on that scope now.</summary>
public sealed record LimitResult(string Kind, string Id, long? MonthlyTokens);
