namespace Skanyxx.Module.Quotas.Contracts;

public sealed record LimitDto(string Kind, string Id, string Name, long MonthlyTokens);
