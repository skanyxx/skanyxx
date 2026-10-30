using Skanyxx.Module.Quotas.Features;

namespace Skanyxx.Module.Quotas.Contracts;

/// <summary>
/// One person's month as the API shows it. <see cref="Used"/> is the model's tokens (what the limit counts);
/// <see cref="EstimatedTokens"/>: turns kagent did not report, counted by Chat's estimate. <see cref="ClientTokens"/>:
/// what Skanyxx sent, by its own count (D548), never part of <see cref="Used"/>. <see cref="Masked"/>: only the name and
/// the percentage are real (<see cref="Redacted"/>).
/// </summary>
public sealed record PersonQuotaDto(
    string UserId, string Name, string Email, IReadOnlyList<string> Teams,
    long Used, long PromptTokens, long CompletionTokens, long EstimatedTokens, long Turns, long EstimatedTurns,
    long? Limit, long Extra, string? BindingKind, string? BindingId, string? BindingName, long? PersonalLimit, int? Percent, bool Over,
    decimal? Cost, IReadOnlyList<string> UnpricedModels, long ClientTokens, bool Masked = false)
{
    public static PersonQuotaDto From(PersonQuota q, OrgSnapshot org, IReadOnlyList<ScopeLimit> applicable)
    {
        var binding = q.Quota.Binding;
        return new(q.UserId, org.PersonName(q.UserId), org.Person(q.UserId)?.Email ?? "", [.. org.TeamsOf(q.UserId).Select(t => t.Name)],
            q.Used, q.PromptTokens, q.CompletionTokens, q.EstimatedTokens, q.Turns, q.EstimatedTurns,
            q.Quota.Limit, q.Quota.Extra, binding?.Kind, binding?.Id, binding is null ? null : org.ScopeName(binding.Kind, binding.Id),
            applicable.FirstOrDefault(l => l.Kind == Data.QuotaScopes.User)?.MonthlyTokens, q.Percent, q.Over, q.Cost, q.UnpricedModels, q.ClientTokens);
    }

    /// <summary>For a supervisor's view of someone they may not act for: name and percentage only, no email, teams, tokens, limits or cost.</summary>
    public PersonQuotaDto Redacted() =>
        new(UserId, Name, "", [], 0, 0, 0, 0, 0, 0, null, 0, null, null, null, null, Percent, Over, null, [], 0, Masked: true);
}
