namespace Skanyxx.Module.Quotas.Data;

/// <summary>A monthly token budget for every person in one scope (<see cref="QuotaScopes"/>): a person, a team, a department.</summary>
public sealed class QuotaLimit
{
    public string Kind { get; set; } = "";
    public string ScopeId { get; set; } = "";
    public long MonthlyTokens { get; set; }
    public string SetBy { get; set; } = "";
    public DateTimeOffset SetAt { get; set; }
}
