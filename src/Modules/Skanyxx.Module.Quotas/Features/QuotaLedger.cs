using Skanyxx.Module.Quotas.Data;

namespace Skanyxx.Module.Quotas.Features;

/// <summary>Reads one month's <see cref="QuotaBook"/>.</summary>
internal sealed class QuotaLedger(QuotaStore store)
{
    /// <summary><paramref name="users"/> null = everyone.</summary>
    public async Task<QuotaBook> BookAsync(DateOnly month, IReadOnlyCollection<string>? users, CancellationToken ct) => new(
        month,
        [.. (await store.LimitsAsync(ct)).Select(l => new ScopeLimit(l.Kind, l.ScopeId, l.MonthlyTokens))],
        await store.UsageAsync(month, users, ct),
        await store.ExtrasAsync(month, users, ct),
        (await store.PricesAsync(ct)).ToDictionary(p => p.Model, p => p.PricePer1K, StringComparer.Ordinal));
}
